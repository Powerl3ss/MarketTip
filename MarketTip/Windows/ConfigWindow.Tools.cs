using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MarketTip.Core;
using MarketTip.Market;

namespace MarketTip.Windows;

/// <summary>Les pages d'outils : valeur de l'inventaire et objets surveillés.</summary>
public sealed partial class ConfigWindow
{
    private const int InventoryRows = 30;

    // Contenu des sacs, relu une fois par seconde.
    private List<OwnedItem> bagItems = new();
    private DateTime lastBagRead = DateTime.MinValue;

    private string watchSearch = string.Empty;
    private List<(uint ItemId, string Name)> watchResults = new();

    private string ReferenceWorldName => Or(plugin.Universalis.WorldName(plugin.Lookup.WorldId), "ton monde");

    private static string Place(Listing listing) =>
        string.IsNullOrEmpty(listing.Datacenter) ? listing.World ?? "?" : $"{listing.World} ({listing.Datacenter})";

    // =================================================================== Inventaire

    private void DrawInventoryPage()
    {
        if ((DateTime.Now - lastBagRead).TotalSeconds > 1)
        {
            bagItems = plugin.Items.ScanBags();
            lastBagRead = DateTime.Now;
        }

        // Chaque objet vendable des sacs, au prix le plus bas sur le monde de référence (à défaut, sa dernière vente).
        var rows = new List<(string Name, int Count, long Unit, long Total)>();
        var pending = 0;
        long total = 0;
        foreach (var item in bagItems)
        {
            if (!plugin.Lookup.TryGetMarketableId(item.ItemId, out var id, out _))
                continue;
            var (data, state) = plugin.Lookup.Get(id);
            if (data == null)
            {
                if (state == LookupState.Loading)
                    pending++;
                continue;
            }

            var price = item.Hq ? data.Cheapest.World.Hq : data.Cheapest.World.Nq;
            if (!price.HasValue)
                price = item.Hq ? data.LastSale.World.Hq : data.LastSale.World.Nq;
            if (!price.HasValue)
                continue;

            var name = plugin.Items.Name(id) + (item.Hq ? " " + TooltipText.HqIcon : string.Empty);
            var value = price.Value.Price * item.Count;
            rows.Add((name, item.Count, price.Value.Price, value));
            total += value;
        }

        rows.Sort((a, b) => b.Total.CompareTo(a.Total));

        Card(() =>
        {
            using (heroFont.Push())
                ImGui.TextUnformatted($"{TooltipText.Number(total)} gils");
            TextDimmed($"Valeur estimée des {rows.Count} objets vendables de tes sacs, au prix le plus bas sur {ReferenceWorldName}. Tout vendre d'un coup ferait baisser les prix.");
            if (pending > 0)
                TextDimmed($"Recherche des prix en cours ({pending} objets)...");
        }, glow: true);

        SectionTitle("Ce qui vaut le plus", FontAwesomeIcon.Coins);
        Card(() =>
        {
            if (rows.Count == 0)
            {
                TextDimmed("Aucun objet vendable trouvé. Connecte-toi, puis ouvre ton inventaire.");
                return;
            }

            foreach (var row in rows.Take(InventoryRows))
            {
                var left = ImGui.GetCursorPosX();
                ImGui.TextUnformatted(row.Name);
                ImGui.SameLine();
                ImGui.TextColored(TextDim, $"x{TooltipText.Number(row.Count)} à {TooltipText.Gil(row.Unit)}");

                // Le total de la ligne, aligné à droite du panneau.
                var text = TooltipText.Gil(row.Total);
                ImGui.SameLine();
                ImGui.SetCursorPosX(left + cardInner - ImGui.CalcTextSize(text).X);
                ImGui.TextColored(PreviewHighlight, text);
            }

            if (rows.Count > InventoryRows)
                TextDimmed($"Et {rows.Count - InventoryRows} autres objets de moindre valeur.");
        });
    }

    // =================================================================== Surveillance

    private void DrawWatchPage()
    {
        var changed = false;

        SectionTitle("Ajouter un objet", FontAwesomeIcon.Plus);
        Card(() =>
        {
            ImGui.SetNextItemWidth(cardInner);
            if (ImGui.InputTextWithHint("##watchSearch", "Nom d'un objet vendable au marché...", ref watchSearch, 64))
                watchResults = plugin.Items.Search(watchSearch, 8);

            foreach (var (itemId, name) in watchResults)
            {
                if (Cfg.Watchlist.Any(e => e.ItemId == itemId))
                    continue;
                if (!FancyButton($"##add{itemId}", name, FontAwesomeIcon.Plus, ButtonSize(name, FontAwesomeIcon.Plus), primary: false))
                    continue;

                Cfg.Watchlist.Add(new WatchEntry { ItemId = itemId });
                watchSearch = string.Empty;
                watchResults = new List<(uint, string)>();
                plugin.RefreshWatchlist();
                changed = true;
                break;
            }
        });

        SectionTitle("Objets surveillés", FontAwesomeIcon.Bell);
        Card(() =>
        {
            if (Cfg.Watchlist.Count == 0)
            {
                TextDimmed("Aucun objet pour l'instant. Cherche un nom au-dessus pour en ajouter un.");
                return;
            }

            TextDimmed("Seuil : un message arrive dans le chat quand le prix passe dessous. 0 : pas d'alerte. Les prix sont redemandés toutes les 10 minutes.");

            WatchEntry? removed = null;
            foreach (var entry in Cfg.Watchlist)
            {
                var controlWidth = 110f * S + (FrameH + ImGui.GetStyle().ItemSpacing.X) * 2f;
                Row(() => DrawWatchPrices(entry), controlWidth, () =>
                {
                    var threshold = (int)Math.Min(entry.Threshold, int.MaxValue);
                    ImGui.SetNextItemWidth(110f * S);
                    if (ImGui.InputInt($"##threshold{entry.ItemId}", ref threshold, 0, 0))
                    {
                        entry.Threshold = Math.Max(0, threshold);
                        changed = true;
                    }

                    ImGui.SameLine();
                    if (IconButton($"##open{entry.ItemId}", FontAwesomeIcon.ExternalLinkAlt, "Ouvrir sur Universalis"))
                        Util.OpenLink($"https://universalis.app/market/{entry.ItemId}");
                    ImGui.SameLine();
                    if (IconButton($"##remove{entry.ItemId}", FontAwesomeIcon.Trash, "Retirer de la liste", hoverColor: Danger))
                        removed = entry;
                });
            }

            if (removed != null)
            {
                Cfg.Watchlist.Remove(removed);
                changed = true;
            }

            if (FancyButton("##refreshWatch", "Actualiser maintenant", FontAwesomeIcon.SyncAlt, ButtonSize("Actualiser maintenant", FontAwesomeIcon.SyncAlt), primary: false))
                plugin.RefreshWatchlist();
        });

        if (changed)
            SaveSoon();
    }

    // Le nom de l'objet, puis son prix sur le monde de référence et le moins cher ailleurs.
    private void DrawWatchPrices(WatchEntry entry)
    {
        ImGui.TextUnformatted(plugin.Items.Name(entry.ItemId));

        var (data, state) = plugin.Lookup.Get(entry.ItemId);
        if (data == null)
        {
            TextDimmed(state == LookupState.Loading ? "Recherche des prix..." : "Prix indisponibles pour le moment.");
            return;
        }

        var home = TooltipText.BestListing(data, Scope.World);
        var best = TooltipText.BestListing(data, Cfg.SearchScope);
        var text = home.HasValue
            ? $"{ReferenceWorldName} : {TooltipText.Gil(home.Value.Price)}"
            : $"Aucune annonce sur {ReferenceWorldName}";
        if (best.HasValue && best.Value.World != data.World && (!home.HasValue || best.Value.Price < home.Value.Price))
            text += $"  |  moins cher : {TooltipText.Gil(best.Value.Price)} sur {Place(best.Value)}";

        // En vert quand le prix est sous le seuil.
        var reached = entry.Threshold > 0 && best.HasValue && best.Value.Price <= entry.Threshold;
        using (ImRaii.PushColor(ImGuiCol.Text, reached ? Success : TextDim))
            ImGui.TextWrapped(text);
    }
}
