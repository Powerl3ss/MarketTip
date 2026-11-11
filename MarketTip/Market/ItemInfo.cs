using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using MarketTip.Core;

namespace MarketTip.Market;

/// <summary>Un objet possédé : sa qualité et le nombre d'exemplaires.</summary>
public readonly record struct OwnedItem(uint ItemId, bool Hq, int Count);

/// <summary>Ce que le jeu sait d'un objet en dehors du marché : son nom, son prix chez le marchand, le contenu des sacs.</summary>
public sealed unsafe class ItemInfo
{
    private static readonly InventoryType[] Bags =
    {
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    };

    private readonly PriceLookup lookup;
    private readonly ExcelSheet<Item> items;

    public ItemInfo(PriceLookup lookup)
    {
        this.lookup = lookup;
        items = Plugin.DataManager.GetExcelSheet<Item>();
    }

    public string Name(uint itemId) =>
        items.GetRowOrDefault(itemId) is { } item ? item.Name.ExtractText() : $"Objet {itemId}";

    /// <summary>Prix de rachat par un marchand. La haute qualité se revend 10 % plus cher.</summary>
    public long VendorPrice(uint itemId, bool hq)
    {
        if (items.GetRowOrDefault(itemId) is not { } item)
            return 0;
        long price = item.PriceLow;
        return hq ? (long)Math.Ceiling(price * 1.1) : price;
    }

    /// <summary>Le contenu des sacs, regroupé par objet et par qualité.</summary>
    public List<OwnedItem> ScanBags()
    {
        var counts = new Dictionary<(uint, bool), int>();
        var manager = InventoryManager.Instance();
        if (manager == null)
            return new List<OwnedItem>();

        foreach (var bag in Bags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null || !container->IsLoaded)
                continue;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->Items[i];
                if (slot.ItemId == 0)
                    continue;
                var key = (slot.ItemId, slot.Flags.HasFlag(InventoryItem.ItemFlags.HighQuality));
                counts[key] = counts.GetValueOrDefault(key) + (int)slot.Quantity;
            }
        }

        return counts.Select(c => new OwnedItem(c.Key.Item1, c.Key.Item2, c.Value)).ToList();
    }

    /// <summary>Tout ce qui complète les prix du marché dans l'infobulle de l'objet survolé.</summary>
    public ItemContext Context(uint itemId, bool hq, ITooltipOptions options) => new()
    {
        VendorPrice = options.ShowVendorHint ? VendorPrice(itemId, hq) : 0,
    };
}
