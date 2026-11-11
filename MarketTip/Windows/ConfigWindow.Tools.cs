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

/// <summary>La page d'outil : valeur de l'inventaire.</summary>
public sealed partial class ConfigWindow
{
    private const int InventoryRows = 30;

    // Contenu des sacs, relu une fois par seconde.
    private List<OwnedItem> bagItems = new();
    private DateTime lastBagRead = DateTime.MinValue;

    private string ReferenceWorldName => Or(plugin.Universalis.WorldName(plugin.Lookup.WorldId), "ton monde");

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
}
