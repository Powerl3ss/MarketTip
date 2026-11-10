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

/// <summary>
/// Ce que le jeu sait d'un objet en dehors du marché : son nom, son prix chez le marchand, combien le joueur en
/// possède et, s'il se fabrique, ce que coûtent ses matériaux.
/// </summary>
public sealed unsafe class ItemInfo
{
    private static readonly InventoryType[] Bags =
    {
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    };

    private readonly PriceLookup lookup;
    private readonly ExcelSheet<Item> items;

    // Recettes par objet fabriqué, lues une seule fois à la première demande.
    private Dictionary<uint, (int Yield, (uint ItemId, int Amount)[] Ingredients)>? recipes;

    public ItemInfo(PriceLookup lookup)
    {
        this.lookup = lookup;
        items = Plugin.DataManager.GetExcelSheet<Item>();
    }

    public string Name(uint itemId) =>
        items.GetRowOrDefault(itemId) is { } item ? item.Name.ExtractText() : $"Objet {itemId}";

    /// <summary>Les objets vendables au marché dont le nom contient le texte cherché.</summary>
    public List<(uint ItemId, string Name)> Search(string text, int max)
    {
        var found = new List<(uint, string)>();
        text = text.Trim();
        if (text.Length < 2)
            return found;

        foreach (var item in items)
        {
            if (item.ItemSearchCategory.RowId == 0)
                continue;
            var name = item.Name.ExtractText();
            if (!name.Contains(text, StringComparison.OrdinalIgnoreCase))
                continue;
            found.Add((item.RowId, name));
            if (found.Count >= max)
                break;
        }

        return found;
    }

    /// <summary>Prix de rachat par un marchand. La haute qualité se revend 10 % plus cher.</summary>
    public long VendorPrice(uint itemId, bool hq)
    {
        if (items.GetRowOrDefault(itemId) is not { } item)
            return 0;
        long price = item.PriceLow;
        return hq ? (long)Math.Ceiling(price * 1.1) : price;
    }

    /// <summary>Nombre d'exemplaires dans les sacs, pour une qualité.</summary>
    public int Owned(uint itemId, bool hq)
    {
        var manager = InventoryManager.Instance();
        return manager == null ? 0 : manager->GetInventoryItemCount(itemId, hq, false, false);
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
    public ItemContext Context(uint itemId, bool hq, ITooltipOptions options)
    {
        var (cost, partial) = options.ShowCraftCost ? MaterialCost(itemId) : (Optional<long>.None, false);
        return new ItemContext
        {
            VendorPrice = options.ShowVendorHint ? VendorPrice(itemId, hq) : 0,
            Owned = options.ShowOwnedValue ? Owned(itemId, hq) : 0,
            MaterialCost = cost,
            MaterialCostPartial = partial,
        };
    }

    /// <summary>
    /// Le coût des matériaux d'un exemplaire, au prix le plus bas du monde de référence. Les prix encore inconnus
    /// sont demandés : le coût est alors partiel, et se complète quand ils arrivent.
    /// </summary>
    public (Optional<long> Cost, bool Partial) MaterialCost(uint itemId)
    {
        recipes ??= LoadRecipes();
        if (!recipes.TryGetValue(itemId, out var recipe))
            return (Optional<long>.None, false);

        long total = 0;
        var known = 0;
        foreach (var (ingredient, amount) in recipe.Ingredients)
        {
            if (!lookup.TryGetMarketableId(ingredient, out var id, out _))
                continue;
            var (data, _) = lookup.Get(id);
            if (data == null)
                continue;
            var best = TooltipText.BestListing(data, Scope.World);
            if (!best.HasValue)
                continue;
            total += best.Value.Price * amount;
            known++;
        }

        if (known == 0)
            return (Optional<long>.None, false);
        var perItem = (long)Math.Round(total / (double)Math.Max(1, recipe.Yield));
        return (perItem, known < recipe.Ingredients.Length);
    }

    private static Dictionary<uint, (int, (uint, int)[])> LoadRecipes()
    {
        var result = new Dictionary<uint, (int, (uint, int)[])>();
        foreach (var recipe in Plugin.DataManager.GetExcelSheet<Recipe>())
        {
            var made = recipe.ItemResult.RowId;
            if (made == 0 || result.ContainsKey(made))
                continue;

            var ingredients = new List<(uint, int)>();
            for (var i = 0; i < recipe.Ingredient.Count; i++)
            {
                var id = recipe.Ingredient[i].RowId;
                var amount = (int)recipe.AmountIngredient[i];
                if (id != 0 && amount > 0)
                    ingredients.Add((id, amount));
            }

            if (ingredients.Count > 0)
                result[made] = (recipe.AmountResult, ingredients.ToArray());
        }

        return result;
    }
}
