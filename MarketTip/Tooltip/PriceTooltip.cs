using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MarketTip.Core;
using MarketTip.Market;

namespace MarketTip.Tooltip;

/// <summary>
/// Ajoute les prix sous l'infobulle d'un objet du jeu (fenêtre « ItemDetail »), dans un texte à part placé sous le prix
/// de vente au marchand. L'infobulle est agrandie d'autant, puis remise à sa taille quand le jeu la met à jour.
/// Principe repris de Price Insight (Kouzukii, licence MIT : voir THIRD-PARTY-PriceInsight.txt).
/// </summary>
public sealed unsafe class PriceTooltip : IDisposable
{
    private const string AddonName = "ItemDetail";

    // Identifiant du texte ajouté, choisi loin de ceux du jeu et de ceux de Price Insight.
    private const uint NodeId = 0x4D5401;

    // Nœuds du jeu : le bloc placé en bas de l'infobulle, et un texte dont on copie l'apparence.
    private const uint BottomNodeId = 2;
    private const uint StyleTextNodeId = 44;

    // Couleurs du jeu (feuille UIColor) : mise en valeur et texte grisé.
    private const ushort HighlightColor = 506;
    private const ushort DimColor = 20;
    private const ushort WarningColor = 500;

    // Espace laissé sous le texte des prix, avant les raccourcis du bas de l'infobulle.
    private const int BottomGap = 8;

    private readonly Configuration config;
    private readonly PriceLookup lookup;
    private readonly ItemInfo info;

    public PriceTooltip(Configuration config, PriceLookup lookup, ItemInfo info)
    {
        this.info = info;
        this.config = config;
        this.lookup = lookup;
        lookup.Received += OnPricesReceived;
        lookup.Failed += OnPricesFailed;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreUpdate);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, AddonName, OnPostUpdate);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreUpdate);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, AddonName, OnPostUpdate);
        lookup.Received -= OnPricesReceived;
        lookup.Failed -= OnPricesFailed;
        RemoveNode();
    }

    // ------------------------------------------------------------ Événements

    // Avant que le jeu remplisse l'infobulle : elle reprend sa taille d'origine.
    private void OnPreUpdate(AddonEvent type, AddonArgs args) => Restore((AtkUnitBase*)args.Addon.Address);

    private void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        try
        {
            if (!config.Enabled)
                return;
            var hovered = Plugin.GameGui.HoveredItem;
            if (!lookup.TryGetMarketableId(hovered, out var itemId, out var hq))
                return;
            Show((AtkUnitBase*)args.Addon.Address, BuildText(itemId, hq));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Infobulle des prix");
        }
    }

    // Des prix arrivent pendant que l'infobulle est ouverte : elle est mise à jour tout de suite. Ce peut être
    // ceux de l'objet survolé comme ceux de ses matériaux.
    private void OnPricesReceived(IReadOnlyDictionary<uint, MarketData> received) =>
        Plugin.Framework.RunOnFrameworkThread(RedrawHovered);

    private void OnPricesFailed(IReadOnlyCollection<uint> items) =>
        Plugin.Framework.RunOnFrameworkThread(RedrawHovered);

    private void RedrawHovered()
    {
        if (!config.Enabled || !lookup.TryGetMarketableId(Plugin.GameGui.HoveredItem, out var itemId, out var hq))
            return;
        Redraw(BuildText(itemId, hq));
    }

    private List<Segment> BuildText(uint itemId, bool hq)
    {
        var (data, state) = lookup.Get(itemId);
        var context = data == null ? null : info.Context(itemId, hq, config);
        return TooltipText.Build(data, state, hq, config, DateTime.Now, context);
    }

    private void Redraw(List<Segment> segments)
    {
        try
        {
            var addon = Plugin.GameGui.GetAddonByName(AddonName);
            if (addon.IsNull || !addon.IsVisible)
                return;
            var tooltip = (AtkUnitBase*)addon.Address;
            Restore(tooltip);
            Show(tooltip, segments);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Mise à jour de l'infobulle des prix");
        }
    }

    // ------------------------------------------------------------ Infobulle

    private static AtkTextNode* FindNode(AtkUnitBase* tooltip)
    {
        for (var i = 0; i < tooltip->UldManager.NodeListCount; i++)
        {
            var node = tooltip->UldManager.NodeList[i];
            if (node != null && node->NodeId == NodeId)
                return (AtkTextNode*)node;
        }

        return null;
    }

    /// <summary>Masque le texte des prix et rend à l'infobulle sa hauteur d'origine.</summary>
    private static void Restore(AtkUnitBase* tooltip)
    {
        if (tooltip == null)
            return;
        var node = FindNode(tooltip);
        if (node == null || !node->AtkResNode.IsVisible())
            return;

        node->AtkResNode.ToggleVisibility(false);
        var bottom = tooltip->GetNodeById(BottomNodeId);
        if (bottom == null)
            return;

        var removed = node->AtkResNode.Height + BottomGap;
        var window = tooltip->WindowNode;
        window->AtkResNode.SetHeight((ushort)(window->AtkResNode.Height - removed));
        window->Component->UldManager.RootNode->SetHeight(window->AtkResNode.Height);
        window->Component->UldManager.RootNode->PrevSiblingNode->SetHeight(window->AtkResNode.Height);
        bottom->SetYFloat(bottom->Y - removed);
    }

    private static void Show(AtkUnitBase* tooltip, List<Segment> segments)
    {
        if (tooltip == null || segments.Count == 0)
            return;

        var bottom = tooltip->GetNodeById(BottomNodeId);
        if (bottom == null)
            return;

        var node = FindNode(tooltip);
        if (node == null)
            node = CreateNode(tooltip, bottom);
        if (node == null)
            return;

        node->AtkResNode.ToggleVisibility(true);
        // Le jeu lit le texte jusqu'à un octet nul : il est ajouté à la fin.
        var encoded = ToSeString(segments).Encode();
        var text = new byte[encoded.Length + 1];
        encoded.CopyTo(text, 0);
        node->SetText(text);
        node->ResizeNodeForCurrentText();

        // Le texte se place sous le contenu actuel, et l'infobulle s'agrandit d'autant.
        var window = tooltip->WindowNode;
        node->AtkResNode.SetYFloat(window->AtkResNode.Height - 8);
        var added = node->AtkResNode.Height + BottomGap;
        var height = (ushort)(window->AtkResNode.Height + added);
        window->SetHeight(height);
        window->AtkResNode.SetHeight(height);
        window->Component->UldManager.RootNode->SetHeight(height);
        window->Component->UldManager.RootNode->PrevSiblingNode->SetHeight(height);
        tooltip->RootNode->SetHeight(height);
        bottom->SetYFloat(bottom->Y + added);

        // Si l'infobulle dépasse en bas de l'écran, elle remonte.
        var overflow = tooltip->Y + tooltip->GetScaledHeight(true) + 36 - ImGuiHelpers.MainViewport.WorkSize.Y;
        if (overflow > 0)
            tooltip->SetPosition(tooltip->X, (short)Math.Max(0, tooltip->Y - overflow));
    }

    private static AtkTextNode* CreateNode(AtkUnitBase* tooltip, AtkResNode* bottom)
    {
        var style = tooltip->GetTextNodeById(StyleTextNodeId);
        if (style == null)
            return null;

        var node = IMemorySpace.GetUISpace()->Create<AtkTextNode>();
        if (node == null)
            return null;

        node->AtkResNode.Type = NodeType.Text;
        node->AtkResNode.NodeId = NodeId;
        node->AtkResNode.NodeFlags = NodeFlags.AnchorLeft | NodeFlags.AnchorTop;
        node->AtkResNode.X = 16;
        node->AtkResNode.Width = 50;
        node->AtkResNode.Color = style->AtkResNode.Color;
        node->TextColor = style->TextColor;
        node->EdgeColor = style->EdgeColor;
        node->LineSpacing = 18;
        node->FontSize = 12;
        node->TextFlags = style->TextFlags | TextFlags.MultiLine | TextFlags.AutoAdjustNodeSize;

        // Inséré juste avant le bloc du bas, dans le même parent.
        var previous = bottom->PrevSiblingNode;
        node->AtkResNode.ParentNode = bottom->ParentNode;
        bottom->PrevSiblingNode = (AtkResNode*)node;
        if (previous != null)
            previous->NextSiblingNode = (AtkResNode*)node;
        node->AtkResNode.PrevSiblingNode = previous;
        node->AtkResNode.NextSiblingNode = bottom;
        tooltip->UldManager.UpdateDrawNodeList();
        return node;
    }

    /// <summary>Retire le texte ajouté de l'infobulle (arrêt du plugin).</summary>
    private static void RemoveNode()
    {
        var addon = Plugin.GameGui.GetAddonByName(AddonName);
        if (addon.IsNull)
            return;
        var tooltip = (AtkUnitBase*)addon.Address;
        Restore(tooltip);
        var node = (AtkResNode*)FindNode(tooltip);
        if (node == null)
            return;

        if (node->ParentNode != null && node->ParentNode->ChildNode == node)
            node->ParentNode->ChildNode = node->PrevSiblingNode;
        if (node->PrevSiblingNode != null)
            node->PrevSiblingNode->NextSiblingNode = node->NextSiblingNode;
        if (node->NextSiblingNode != null)
            node->NextSiblingNode->PrevSiblingNode = node->PrevSiblingNode;
        tooltip->UldManager.UpdateDrawNodeList();
        node->Destroy(true);
    }

    // ------------------------------------------------------------ Texte

    private static SeString ToSeString(List<Segment> segments)
    {
        var payloads = new List<Payload>();
        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Text:
                    payloads.Add(new TextPayload(segment.Text));
                    break;
                case SegmentKind.Highlight:
                    Colored(payloads, HighlightColor, segment.Text);
                    break;
                case SegmentKind.Dim:
                    Colored(payloads, DimColor, segment.Text);
                    break;
                case SegmentKind.Warning:
                    Colored(payloads, WarningColor, segment.Text);
                    break;
                case SegmentKind.CrossWorldIcon:
                    payloads.Add(new IconPayload(BitmapFontIcon.CrossWorld));
                    break;
                case SegmentKind.LoadingIcon:
                    payloads.Add(new IconPayload(BitmapFontIcon.LevelSync));
                    break;
                case SegmentKind.WarningIcon:
                    payloads.Add(new IconPayload(BitmapFontIcon.Warning));
                    break;
            }
        }

        return new SeString(payloads);
    }

    private static void Colored(List<Payload> payloads, ushort color, string text)
    {
        payloads.Add(new UIForegroundPayload(color));
        payloads.Add(new TextPayload(text));
        payloads.Add(new UIForegroundPayload(0));
    }
}
