using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MarketTip.Core;

namespace MarketTip.Windows;

public sealed partial class ConfigWindow
{
    // Couleur des prix mis en valeur dans l'aperçu, proche de celle du jeu.
    private static readonly Vector4 PreviewHighlight = new(1f, 0.87f, 0.56f, 1f);

    private static readonly Vector4 PreviewWarning = new(1f, 0.62f, 0.26f, 1f);

    // L'objet d'exemple : racheté 40 gils par le marchand.
    private static readonly ItemContext PreviewContext = new() { VendorPrice = 40 };

    private bool previewHq;

    // =================================================================== Prix affichés

    private void DrawHomePage()
    {
        var changed = false;

        Card(() =>
        {
            changed |= ToggleRow("Afficher les prix dans les infobulles", "Raccourci : /markettip", Cfg.Enabled, v => Cfg.Enabled = v);
            DrawStatusRow();
        }, glow: true);

        // Les vrais noms du monde, du centre de données et de la région quand ils sont connus.
        var worldId = plugin.Lookup.WorldId;
        var world = Or(plugin.Universalis.WorldName(worldId), "ton monde");
        var datacenter = Or(plugin.Universalis.DatacenterName(worldId), "Centre de données");
        var region = Or(plugin.Universalis.RegionName(worldId), "Région");

        SectionTitle("Aperçu", FontAwesomeIcon.Eye);
        Card(DrawPreview);

        SectionTitle("Chercher moins cher", FontAwesomeIcon.Search);
        Card(() =>
        {
            var current = (int)Cfg.SearchScope;
            var chosen = Choice("##scope", null, $"Le prix sur {world} est toujours affiché.", new[]
            {
                ("Non", FontAwesomeIcon.Ban),
                (datacenter, FontAwesomeIcon.Server),
                (region, FontAwesomeIcon.Globe),
            }, current);
            if (chosen != current)
            {
                Cfg.SearchScope = (Scope)chosen;
                changed = true;
            }
        });

        SectionTitle("Afficher aussi", FontAwesomeIcon.ListUl);
        Card(() =>
        {
            changed |= ToggleRow("Dernière vente", null, Cfg.ShowLastSale, v => Cfg.ShowLastSale = v);
            changed |= ToggleRow("Ventes par jour", null, Cfg.ShowDailySales, v => Cfg.ShowDailySales = v);
            changed |= ToggleRow("Prix moyen", null, Cfg.ShowAveragePrice, v => Cfg.ShowAveragePrice = v);
            changed |= ToggleRow("Les deux qualités", null, Cfg.ShowBothQualities, v => Cfg.ShowBothQualities = v);
        });

        if (changed)
            SaveSoon();
    }

    /// <summary>
    /// Un choix sur toute la largeur du panneau, sous son titre et sa description. À côté du texte, il le
    /// comprimait dans une colonne étroite et ses intitulés se chevauchaient.
    /// </summary>
    private int Choice(string id, string? title, string? description, (string Label, FontAwesomeIcon Icon)[] items, int current)
    {
        if (!string.IsNullOrEmpty(title))
            ImGui.TextUnformatted(title);
        if (!string.IsNullOrEmpty(description))
            TextDimmed(description);
        ImGui.Dummy(new Vector2(0f, 2f * S));
        return Segmented(id, items, current, cardInner);
    }

    private void DrawStatusRow()
    {
        var lookup = plugin.Lookup;
        var (text, color) = lookup.LastAnswer switch
        {
            null => ("En attente", TextDim),
            _ when lookup.LastFailed => ("Universalis ne répond pas", Danger),
            _ => ("Universalis connecté", Success),
        };
        var world = plugin.Universalis.WorldName(lookup.WorldId);
        Row(() =>
        {
            ImGui.TextUnformatted("Prix en mémoire");
            TextDimmed(string.IsNullOrEmpty(world)
                ? $"{lookup.CachedCount} objets. Les prix sont lus une fois connecté."
                : $"{lookup.CachedCount} objets, vus depuis {world}. Ils sont redemandés au bout d'une heure.");
        }, PillWidth(text, dot: true), () => Pill(text, color, dot: true, centerInFrame: true));
    }

    // ------------------------------------------------------------ Aperçu

    private void DrawPreview()
    {
        var segments = TooltipText.Build(PreviewData(), LookupState.Ready, previewHq, Cfg, DateTime.Now, PreviewContext);

        // Un cadre sombre comme l'infobulle du jeu, avec le texte des prix tel qu'il y sera ajouté.
        // Le panneau autour sépare déjà le dessin en deux calques, et ImGui ne permet pas de le refaire ici :
        // la hauteur du cadre est donc calculée d'avance pour le dessiner avant le texte.
        var pad = new Vector2(14f, 10f) * S;
        var lineGap = 3f * S;
        // Une première ligne nomme l'objet d'exemple et sa qualité, pour voir ce que le choix du dessous change.
        var lines = 2;
        foreach (var segment in segments)
            lines += segment.Text.Split('\n').Length - 1;
        var height = lines * ImGui.GetTextLineHeight() + (lines - 1) * lineGap + pad.Y * 2f;

        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var end = new Vector2(start.X + cardInner, start.Y + height);
        drawList.AddRectFilled(start, end, Col(new Vector4(0.05f, 0.05f, 0.06f, 0.92f)), 10f * S);
        drawList.AddRect(start, end, Col(Line), 10f * S);

        ImGui.SetCursorScreenPos(start + pad);
        ImGui.BeginGroup();
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0f, lineGap)))
        {
            ImGui.TextColored(TextDim, "Objet d'exemple, avec de faux prix");
            DrawSegments(segments);
        }

        ImGui.EndGroup();

        ImGui.SetCursorScreenPos(new Vector2(start.X, end.Y));
        ImGui.Dummy(new Vector2(cardInner, 4f * S));
    }

    private static void DrawSegments(List<Segment> segments)
    {
        var lineStart = true;
        foreach (var segment in segments)
        {
            var (text, color) = segment.Kind switch
            {
                SegmentKind.Highlight => (segment.Text, PreviewHighlight),
                SegmentKind.Dim => (segment.Text, TextDim),
                SegmentKind.Warning => (segment.Text, PreviewWarning),
                SegmentKind.CrossWorldIcon => (SeIconChar.CrossWorld.ToIconString(), TextMain),
                SegmentKind.LoadingIcon or SegmentKind.WarningIcon => (string.Empty, TextMain),
                _ => (segment.Text, TextMain),
            };

            var parts = text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    lineStart = true;
                if (parts[i].Length == 0)
                    continue;
                if (!lineStart)
                    ImGui.SameLine(0f, 0f);
                ImGui.TextColored(color, parts[i]);
                lineStart = false;
            }
        }
    }

    // De faux prix, avec les vrais noms du monde de référence quand ils sont connus.
    private MarketData PreviewData()
    {
        var worldId = plugin.Lookup.WorldId;
        var world = Or(plugin.Universalis.WorldName(worldId), "Moogle");
        var dc = Or(plugin.Universalis.DatacenterName(worldId), "Chaos");
        var region = Or(plugin.Universalis.RegionName(worldId), "Europe");
        var otherWorld = world == "Cerberus" ? "Omega" : "Cerberus";
        var otherDc = dc == "Light" ? "Chaos" : "Light";
        var regionWorld = otherDc == "Light" ? "Odin" : "Ragnarok";
        var now = DateTime.Now;

        Listing L(long price, string w, string d, double hoursAgo) => new(price, w, d, now.AddHours(-hoursAgo));

        return new MarketData
        {
            World = world,
            Datacenter = dc,
            Region = region,
            Cheapest = new Scopes<Listing>
            {
                World = new() { Nq = L(1_450, world, dc, 2), Hq = L(2_300, world, dc, 2) },
                Datacenter = new() { Nq = L(1_180, otherWorld, dc, 5), Hq = L(1_990, otherWorld, dc, 5) },
                Region = new() { Nq = L(990, regionWorld, otherDc, 1), Hq = L(1_750, regionWorld, otherDc, 1) },
            },
            LastSale = new Scopes<Listing>
            {
                World = new() { Nq = L(1_400, world, dc, 0.4), Hq = L(2_250, world, dc, 100) },
                Datacenter = new() { Nq = L(1_200, otherWorld, dc, 0.1), Hq = L(2_000, otherWorld, dc, 3) },
                Region = new() { Nq = L(1_000, regionWorld, otherDc, 0.05), Hq = L(1_800, regionWorld, otherDc, 7) },
            },
            AveragePrice = new Scopes<double>
            {
                World = new() { Nq = 1_420d, Hq = 2_280d },
                Datacenter = new() { Nq = 1_260d, Hq = 2_060d },
                Region = new() { Nq = 1_150d, Hq = 1_940d },
            },
            DailySales = new Scopes<double>
            {
                World = new() { Nq = 6.5, Hq = 1.2 },
                Datacenter = new() { Nq = 38d, Hq = 9.4 },
                Region = new() { Nq = 145d, Hq = 31d },
            },
        };
    }

    private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    // =================================================================== À propos

    private void DrawAboutPage()
    {
        var version = Plugin.PluginInterface.Manifest.AssemblyVersion.ToString();
        Card(() =>
        {
            var start = ImGui.GetCursorPos();
            DrawLogo(64f * S);
            ImGui.SameLine(0, 16f * S);
            ImGui.BeginGroup();
            ImGui.SetCursorPosY(start.Y + 6f * S);
            using (heroFont.Push())
                ImGui.TextUnformatted("MarketTip");
            ImGui.SameLine(0, 10f * S);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f * S);
            Chip($"Version {version}");
            TextDimmed("Les prix du marché dans l'infobulle des objets, en français. Les prix viennent d'Universalis.");
            ImGui.Dummy(new Vector2(0f, 2f * S));
            // Les lignes des crédits se suivent de près.
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 2f * S)))
            {
                ImGui.TextUnformatted("Créé par Powerl3ss.");
                TextDimmed("Claude, l'IA d'Anthropic, a grandement participé à son développement.");
            }

            TextDimmed("L'ajout du texte dans l'infobulle du jeu reprend le principe de Price Insight (Kouzukii, licence MIT).");
            ImGui.EndGroup();
        });

        SectionTitle("Commandes", FontAwesomeIcon.Terminal);
        Card(() =>
        {
            CommandRow("/markettip", "Affiche ou masque les prix dans les infobulles");
            CommandRow("/markettip config", "Ouvre ces réglages");
            CommandRow("/markettip inv", "Ouvre la valeur de l'inventaire");
            CommandRow("/markettip watch", "Ouvre les objets surveillés");
            CommandRow("/markettip clear", "Vide les prix en mémoire");
        });

        SectionTitle("D'où viennent les prix", FontAwesomeIcon.Database);
        Card(() =>
        {
            TextDimmed("D'Universalis, un site qui rassemble les relevés du marché envoyés par les joueurs. Un prix peut donc dater : l'ancienneté à côté indique le dernier relevé, en orangé au-delà de 3 jours. MarketTip envoie seulement ton monde et les objets dont il cherche le prix.");
            TextDimmed("Ctrl+Maj+U en survolant un objet ouvre sa page Universalis.");
        });

        if (FancyButton("##repo", "Ouvrir la page GitHub", FontAwesomeIcon.ExternalLinkAlt, ButtonSize("Ouvrir la page GitHub", FontAwesomeIcon.ExternalLinkAlt), primary: false))
            Util.OpenLink(RepoUrl);
    }

    /// <summary>Une commande et ce qu'elle fait, sur une ligne simple : les commandes se suivent de près.</summary>
    private void CommandRow(string command, string description)
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(16f * S, 4f * S)))
        {
            ImGui.TextColored(Accent, command);
            ImGui.SameLine();
            ImGui.TextColored(TextDim, description);
        }
    }
}
