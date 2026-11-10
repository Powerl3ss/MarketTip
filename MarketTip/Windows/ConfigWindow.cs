using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using MarketTip.Core;

namespace MarketTip.Windows;

/// <summary>Fenêtre de réglages : barre de navigation à gauche, pages à droite. Même cadre que les autres plugins.</summary>
public sealed partial class ConfigWindow : Window, IDisposable
{
    private enum Page { Home, Inventory, Watch, About }

    // La navigation, rangée par thème : chaque groupe a son petit titre dans la barre latérale.
    private static readonly (string Group, (Page Page, FontAwesomeIcon Icon, string Label)[] Items)[] Navigation =
    {
        ("MarketTip", new[]
        {
            (Page.Home, FontAwesomeIcon.Coins, "Réglages"),
            (Page.Inventory, FontAwesomeIcon.BoxOpen, "Inventaire"),
            (Page.Watch, FontAwesomeIcon.Bell, "Surveillance"),
            (Page.About, FontAwesomeIcon.InfoCircle, "À propos"),
        }),
    };

    private const string RepoUrl = "https://github.com/Powerl3ss/MarketTip";

    private readonly Plugin plugin;
    private Configuration Cfg => plugin.Configuration;

    private Page page = Page.Home;

    /// <summary>Ouvre la fenêtre sur la valeur de l'inventaire.</summary>
    public void OpenInventory()
    {
        page = Page.Inventory;
        IsOpen = true;
    }

    /// <summary>Ouvre la fenêtre sur les objets surveillés.</summary>
    public void OpenWatchlist()
    {
        page = Page.Watch;
        IsOpen = true;
    }

    // Styles de fenêtre poussés avant Begin (PreDraw) et retirés après End (PostDraw).
    private IDisposable? windowStyle;
    private IDisposable? windowColors;

    private Task<IDalamudTextureWrap?>? logoTask;

    public ConfigWindow(Plugin plugin)
        : base("MarketTip | Réglages###MarketTipSettings",
               ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
               ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(1000, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = ExpandedConstraints;

        // Vraies polices de grande taille (au lieu d'agrandir le texte, ce qui le rend flou).
        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;
        // La police par défaut est dessinée à des tailles fixes : toute autre taille est étirée et devient floue.
        // 14 et 18 points (soit 18,67 et 24 pixels) sont des tailles exactes.
        titleFont = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(14f * 4f / 3f)));
        heroFont = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(24f)));
        // 36 points (48 pixels) : les grands chiffres de l'accueil.
        numberFont = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(36f * 4f / 3f)));
    }

    private readonly IFontHandle titleFont;
    private readonly IFontHandle heroFont;
    private readonly IFontHandle numberFont;

    // Tailles en pixels "non mis à l'échelle" (Dalamud applique l'échelle de l'interface).
    private const float HeaderHeight = 44f;
    private static readonly WindowSizeConstraints ExpandedConstraints = new()
    {
        MinimumSize = new Vector2(860, 600),
        MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
    };
    private static readonly WindowSizeConstraints CollapsedConstraints = new()
    {
        MinimumSize = new Vector2(780, HeaderHeight),
        MaximumSize = new Vector2(float.MaxValue, HeaderHeight),
    };
    private static readonly WindowSizeConstraints FoldingConstraints = new()
    {
        MinimumSize = new Vector2(780, HeaderHeight),
        MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
    };

    // Fenêtre repliée : seul l'en-tête reste visible.
    private bool collapsed;
    private float expandedHeight;
    private Vector2 lastSize;

    private void ToggleWindowCollapsed()
    {
        if (folding)
            return;

        if (!collapsed)
        {
            expandedHeight = lastSize.Y;
            collapsed = true;
            StartFold(lastSize.Y, HeaderHeight * S);
        }
        else
        {
            collapsed = false;
            StartFold(lastSize.Y, Math.Max(expandedHeight, 560f * S));
        }
    }

    // --- Enregistrement différé des réglages
    private bool savePending;

    /// <summary>Demande l'enregistrement des réglages : il est fait dès que plus rien n'est en cours de modification.</summary>
    private void SaveSoon() => savePending = true;

    private void FlushSave()
    {
        if (!savePending)
            return;
        savePending = false;
        Cfg.Save();
    }

    public override void OnClose()
    {
        FlushSave();
    }

    public void Dispose()
    {
        FlushSave();
        titleFont.Dispose();
        heroFont.Dispose();
        numberFont.Dispose();
        if (logoTask is { IsCompletedSuccessfully: true })
            logoTask.Result?.Dispose();
    }

    // =================================================================== Cadre

    public override void PreDraw()
    {
        windowStyle = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero)
                            .Push(ImGuiStyleVar.WindowRounding, 22f * S)
                            .Push(ImGuiStyleVar.WindowBorderSize, 1f);
        windowColors = ImRaii.PushColor(ImGuiCol.WindowBg, WindowBg with { W = Math.Clamp(Cfg.SettingsOpacity, 0f, 1f) })
                             .Push(ImGuiCol.Border, Line);

        // Repli / dépli animé de la fenêtre.
        ApplyFoldAnimation();
    }

    public override void PostDraw()
    {
        // Enregistré une fois le geste fini (curseur relâché, couleur choisie, texte validé),
        // et non à chaque image pendant qu'on fait glisser un curseur.
        if (savePending && !ImGui.IsAnyItemActive())
            FlushSave();

        windowColors?.Dispose();
        windowColors = null;
        windowStyle?.Dispose();
        windowStyle = null;
    }

    public override void Draw()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 10f * S)
                                .Push(ImGuiStyleVar.GrabRounding, 10f * S)
                                .Push(ImGuiStyleVar.PopupRounding, 10f * S)
                                .Push(ImGuiStyleVar.ScrollbarRounding, 8f * S)
                                .Push(ImGuiStyleVar.ScrollbarSize, 10f * S)
                                .Push(ImGuiStyleVar.FramePadding, new Vector2(10f, 6f) * S)
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 8f) * S)
                                .Push(ImGuiStyleVar.WindowPadding, new Vector2(10f, 10f) * S);
        using var colors = ImRaii.PushColor(ImGuiCol.Text, TextMain)
                                 .Push(ImGuiCol.TextDisabled, TextDim)
                                 .Push(ImGuiCol.ChildBg, Vector4.Zero)
                                 .Push(ImGuiCol.FrameBg, InputBg)
                                 .Push(ImGuiCol.FrameBgHovered, InputBgHover)
                                 .Push(ImGuiCol.FrameBgActive, InputBgHover)
                                 .Push(ImGuiCol.Button, InputBg)
                                 .Push(ImGuiCol.ButtonHovered, InputBgHover)
                                 .Push(ImGuiCol.ButtonActive, AccentSoft)
                                 .Push(ImGuiCol.Header, AccentSoft)
                                 .Push(ImGuiCol.HeaderHovered, AccentSoft)
                                 .Push(ImGuiCol.HeaderActive, AccentBorder)
                                 .Push(ImGuiCol.CheckMark, Accent)
                                 .Push(ImGuiCol.SliderGrab, Accent)
                                 .Push(ImGuiCol.SliderGrabActive, Vector4.Lerp(Accent, White, 0.2f))
                                 .Push(ImGuiCol.PopupBg, new Vector4(0.110f, 0.106f, 0.129f, 0.99f))
                                 .Push(ImGuiCol.ScrollbarBg, Vector4.Zero)
                                 .Push(ImGuiCol.ScrollbarGrab, new Vector4(1f, 1f, 1f, 0.12f))
                                 .Push(ImGuiCol.ScrollbarGrabHovered, new Vector4(1f, 1f, 1f, 0.20f))
                                 .Push(ImGuiCol.ScrollbarGrabActive, AccentBorder)
                                 .Push(ImGuiCol.Separator, Line);

        var size = ImGui.GetWindowSize();
        lastSize = size;
        var origin = ImGui.GetWindowPos();
        var margin = PanelGap;
        var sidebarW = 216f * S;
        var drawList = ImGui.GetWindowDrawList();

        DrawAnimatedBackground(origin, size);

        // La barre latérale est un panneau à part, posé sur le fond comme les autres.
        var sidebarMin = origin + new Vector2(margin, margin);
        var sidebarMax = origin + new Vector2(margin + sidebarW, size.Y - margin);
        drawList.AddRectFilled(sidebarMin, sidebarMax, Col(CardBg), PanelRounding);
        drawList.AddRect(sidebarMin, sidebarMax, Col(Line), PanelRounding);
        DrawSidebar(sidebarMin, sidebarMax);

        // Les pages défilent entre les mêmes marges que la barre latérale : leurs panneaux s'alignent sur elle en haut
        // et en bas.
        var pageX = margin + sidebarW;
        ImGui.SetCursorPos(new Vector2(pageX, margin));
        var noPadding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using (var child = ImRaii.Child("##page", new Vector2(size.X - pageX, size.Y - margin * 2f), false))
        {
            noPadding.Dispose();
            if (child.Success)
                DrawPage();
        }
    }

    private void DrawSidebar(Vector2 min, Vector2 max)
    {
        var pad = 14f * S;
        var origin = ImGui.GetWindowPos();
        var drawList = ImGui.GetWindowDrawList();
        var width = max.X - min.X - pad * 2;
        var x = min.X - origin.X + pad;
        var y = min.Y - origin.Y + pad + 4f * S;

        // Le logo, le nom, et la croix pour fermer.
        var logoSize = 30f * S;
        ImGui.SetCursorPos(new Vector2(x, y));
        DrawLogo(logoSize);
        using (titleFont.Push())
        {
            ImGui.SetCursorPos(new Vector2(x + logoSize + 10f * S, y + MathF.Round((logoSize - ImGui.GetTextLineHeight()) / 2f)));
            ImGui.TextUnformatted("MarketTip");
        }

        ImGui.SetCursorPos(new Vector2(x + width - FrameH, y + (logoSize - FrameH) / 2f));
        if (IconButton("##close", FontAwesomeIcon.Times))
            IsOpen = false;

        y += logoSize + 22f * S;

        // Les pages, par groupe. Le surlignage de la page active glisse jusqu'à la nouvelle.
        var itemH = 33f * S;
        var activeY = y;
        var positions = new List<(Page Page, FontAwesomeIcon Icon, string Label, float Y)>();
        foreach (var (group, items) in Navigation)
        {
            ImGui.SetCursorPos(new Vector2(x + 8f * S, y));
            ImGui.TextColored(TextFaint, group.ToUpperInvariant());
            y += ImGui.GetTextLineHeight() + 4f * S;

            foreach (var (target, icon, label) in items)
            {
                positions.Add((target, icon, label, y));
                if (target == page)
                    activeY = y;
                y += itemH + 1f * S;
            }

            y += 8f * S;
        }

        var slide = Animate(ImGui.GetID("##navHighlight"), activeY, 18f);
        var highlightMin = origin + new Vector2(x, slide);
        drawList.AddRectFilled(highlightMin, highlightMin + new Vector2(width, itemH), Col(InputBg), 11f * S);

        foreach (var (target, icon, label, itemY) in positions)
        {
            ImGui.SetCursorPos(new Vector2(x, itemY));
            var itemMin = ImGui.GetCursorScreenPos();
            var itemMax = itemMin + new Vector2(width, itemH);
            if (ImGui.InvisibleButton($"##nav{target}", itemMax - itemMin))
                page = target;

            var hovered = ImGui.IsItemHovered();
            var active = page == target;
            if (!active && hovered)
                drawList.AddRectFilled(itemMin, itemMax, Col(HoverOverlay), 11f * S);

            var iconBox = new Vector2(itemH, itemH);
            DrawIconCentered(icon, itemMin + new Vector2(4f * S, 0f), itemMin + new Vector2(4f * S, 0f) + iconBox, active ? Accent : hovered ? TextMain : TextDim);
            // L'icône et les majuscules du texte sont centrées sur le milieu de la ligne, chacune sur son dessin.
            var textY = MathF.Round(itemMin.Y + itemH / 2f - CapMiddle());
            drawList.AddText(new Vector2(itemMin.X + itemH + 4f * S, textY), Col(active || hovered ? TextMain : TextDim), label);
        }

    }

    private void DrawLogo(float size)
    {
        logoTask ??= LoadLogoAsync();
        if (logoTask is { IsCompletedSuccessfully: true, Result: { } logo })
        {
            ImGui.Image(logo.Handle, new Vector2(size, size));
            return;
        }

        // Le temps que le logo charge : pastille avec une icône de graphique.
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size, size);
        ImGui.GetWindowDrawList().AddRectFilled(min, max, Col(Accent), 8f * S);
        DrawIconCentered(FontAwesomeIcon.Coins, min, max, OnAccent);
        ImGui.Dummy(new Vector2(size, size));
    }

    private static async Task<IDalamudTextureWrap?> LoadLogoAsync()
    {
        try
        {
            await using var stream = typeof(ConfigWindow).Assembly.GetManifestResourceStream("MarketTip.icon.png");
            if (stream == null)
                return null;
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return await Plugin.TextureProvider.CreateFromImageAsync(memory.ToArray());
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Logo introuvable");
            return null;
        }
    }

    private void DrawPage()
    {
        var pad = PanelGap;
        // La barre de défilement passe dans la marge de droite.
        pageWidth = ImGui.GetContentRegionAvail().X - pad - ImGui.GetStyle().ScrollbarSize;
        cardInner = pageWidth;

        // Transition : la nouvelle page apparaît en fondu en remontant légèrement.
        var pageBefore = lastPage;
        var appear = PageAppear();
        if (pageBefore != page)
            ImGui.SetScrollY(0);
        using var fade = ImRaii.PushStyle(ImGuiStyleVar.Alpha, Math.Max(0.02f, appear) * ImGui.GetStyle().Alpha);

        ImGui.SetCursorPos(new Vector2(pad, (1f - appear) * 16f * S));
        ImGui.BeginGroup();
        switch (page)
        {
            case Page.Home: DrawHomePage(); break;
            case Page.Inventory: DrawInventoryPage(); break;
            case Page.Watch: DrawWatchPage(); break;
            case Page.About: DrawAboutPage(); break;
        }
        // La page s'arrête au bas de son dernier élément, sans marge en plus.
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, ImGui.GetItemRectMax().Y));
        ImGui.EndGroup();
    }

}
