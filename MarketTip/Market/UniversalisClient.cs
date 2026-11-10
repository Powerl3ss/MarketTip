using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Networking.Http;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using MarketTip.Core;

namespace MarketTip.Market;

/// <summary>Demande les prix à Universalis, le site qui rassemble les relevés du marché envoyés par les joueurs.</summary>
public sealed class UniversalisClient : IDisposable
{
    /// <summary>Nombre d'objets par requête (Universalis en accepte 100).</summary>
    public const int MaxItemsPerRequest = 50;

    private static readonly Dictionary<uint, string> RegionNames = new()
    {
        { 1, "Japon" },
        { 2, "Amérique du Nord" },
        { 3, "Europe" },
        { 4, "Océanie" },
    };

    private readonly Dictionary<uint, (string Name, string Datacenter, string Region)> worlds;

    // Le centre de données et la région de chaque monde selon Universalis, qui font foi pour ses prix.
    // Les données du jeu ne servent qu'en attendant leur arrivée.
    private readonly ConcurrentDictionary<uint, (string Datacenter, string Region)> universalisWorlds = new();

    private static readonly Dictionary<string, string> UniversalisRegions = new()
    {
        { "Japan", "Japon" },
        { "North-America", "Amérique du Nord" },
        { "Europe", "Europe" },
        { "Oceania", "Océanie" },
    };

    /// <summary>Appelé quand les centres de données d'Universalis sont connus : les prix déjà reçus sont à redemander.</summary>
    public event Action? DatacentersLoaded;
    private readonly HappyEyeballsCallback happyEyeballs = new();
    private readonly HttpClient http;

    public UniversalisClient()
    {
        worlds = Plugin.DataManager.GetExcelSheet<World>()
            .ToDictionary(
                w => w.RowId,
                w => (w.Name.ExtractText(),
                      w.DataCenter.ValueNullable?.Name.ExtractText() ?? string.Empty,
                      RegionNames.GetValueOrDefault(w.DataCenter.ValueNullable?.Region.RowId ?? 0) ?? string.Empty));

        http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = happyEyeballs.ConnectCallback,
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var dalamud = Assembly.GetAssembly(typeof(Util))?.GetName().Version;
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MarketTip/{version} Dalamud/{dalamud}");
        _ = LoadDatacenters();
    }

    private bool TryWorld(uint worldId, out (string Name, string Datacenter, string Region) world)
    {
        if (!worlds.TryGetValue(worldId, out world))
            return false;
        if (universalisWorlds.TryGetValue(worldId, out var place))
            world = (world.Name, place.Datacenter, place.Region);
        return true;
    }

    // Quelques essais espacés : sans cette liste, les noms des centres de données viennent des données du jeu.
    private async Task LoadDatacenters()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await using var stream = await http.GetStreamAsync("https://universalis.app/api/v2/data-centers");
                var list = await JsonSerializer.DeserializeAsync<List<DatacenterEntry>>(stream) ?? new List<DatacenterEntry>();
                foreach (var dc in list)
                {
                    if (string.IsNullOrEmpty(dc.Name) || dc.Worlds == null)
                        continue;
                    var region = UniversalisRegions.GetValueOrDefault(dc.Region ?? string.Empty) ?? dc.Region ?? string.Empty;
                    foreach (var id in dc.Worlds)
                        universalisWorlds[id] = (dc.Name, region);
                }

                DatacentersLoaded?.Invoke();
                return;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Liste des centres de données d'Universalis indisponible");
                await Task.Delay(TimeSpan.FromSeconds(30 * (attempt + 1)));
            }
        }
    }

    public string WorldName(uint worldId) => TryWorld(worldId, out var w) ? w.Name : string.Empty;

    public string DatacenterName(uint worldId) => TryWorld(worldId, out var w) ? w.Datacenter : string.Empty;

    public string RegionName(uint worldId) => TryWorld(worldId, out var w) ? w.Region : string.Empty;

    /// <summary>Les prix des objets demandés, vus depuis <paramref name="worldId"/>. Lance une exception en cas d'échec.</summary>
    public async Task<Dictionary<uint, MarketData>> Fetch(uint worldId, IReadOnlyCollection<uint> itemIds, CancellationToken token)
    {
        var url = $"https://universalis.app/api/v2/aggregated/{worldId}/{string.Join(',', itemIds)}";
        using var response = await http.GetAsync(url, token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"Universalis a répondu {(int)response.StatusCode}", null, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var json = await JsonSerializer.DeserializeAsync<AggregatedResponse>(stream, cancellationToken: token)
                   ?? throw new HttpRequestException("Réponse vide d'Universalis");

        var result = new Dictionary<uint, MarketData>();
        foreach (var item in json.Results ?? new List<ItemResult>())
            result[item.ItemId] = ToMarketData(worldId, item);
        return result;
    }

    private MarketData ToMarketData(uint worldId, ItemResult item)
    {
        TryWorld(worldId, out var home);
        var uploads = item.WorldUploadTimes?.ToDictionary(u => u.WorldId, u => FromMilliseconds(u.Timestamp))
                      ?? new Dictionary<uint, DateTime>();
        var homeUpload = uploads.TryGetValue(worldId, out var t) ? t : (DateTime?)null;

        // L'annonce la moins chère n'a pas de date : celle du dernier relevé de son monde.
        Optional<Listing> Cheapest(Entry? entry)
        {
            if (entry?.Price is not { } price)
                return Optional<Listing>.None;
            var (world, dc) = Place(entry.WorldId);
            var time = entry.WorldId is { } id && uploads.TryGetValue(id, out var upload) ? upload : homeUpload;
            return new Listing((long)Math.Round(price), world ?? home.Name, dc ?? home.Datacenter, time);
        }

        Optional<Listing> Sale(Entry? entry)
        {
            if (entry?.Price is not { } price)
                return Optional<Listing>.None;
            var (world, dc) = Place(entry.WorldId);
            var time = entry.Timestamp is { } ms ? FromMilliseconds(ms) : (DateTime?)null;
            return new Listing((long)Math.Round(price), world ?? home.Name, dc ?? home.Datacenter, time);
        }

        static Optional<double> Positive(double? value) =>
            value is > 0 ? value.Value : Optional<double>.None;

        return new MarketData
        {
            World = home.Name ?? string.Empty,
            Datacenter = home.Datacenter ?? string.Empty,
            Region = home.Region ?? string.Empty,
            Cheapest = new Scopes<Listing>
            {
                World = new() { Nq = Cheapest(item.Nq?.MinListing?.World), Hq = Cheapest(item.Hq?.MinListing?.World) },
                Datacenter = new() { Nq = Cheapest(item.Nq?.MinListing?.Dc), Hq = Cheapest(item.Hq?.MinListing?.Dc) },
                Region = new() { Nq = Cheapest(item.Nq?.MinListing?.Region), Hq = Cheapest(item.Hq?.MinListing?.Region) },
            },
            LastSale = new Scopes<Listing>
            {
                World = new() { Nq = Sale(item.Nq?.RecentPurchase?.World), Hq = Sale(item.Hq?.RecentPurchase?.World) },
                Datacenter = new() { Nq = Sale(item.Nq?.RecentPurchase?.Dc), Hq = Sale(item.Hq?.RecentPurchase?.Dc) },
                Region = new() { Nq = Sale(item.Nq?.RecentPurchase?.Region), Hq = Sale(item.Hq?.RecentPurchase?.Region) },
            },
            AveragePrice = new Scopes<double>
            {
                World = new() { Nq = Positive(item.Nq?.AverageSalePrice?.World?.Price), Hq = Positive(item.Hq?.AverageSalePrice?.World?.Price) },
                Datacenter = new() { Nq = Positive(item.Nq?.AverageSalePrice?.Dc?.Price), Hq = Positive(item.Hq?.AverageSalePrice?.Dc?.Price) },
                Region = new() { Nq = Positive(item.Nq?.AverageSalePrice?.Region?.Price), Hq = Positive(item.Hq?.AverageSalePrice?.Region?.Price) },
            },
            DailySales = new Scopes<double>
            {
                World = new() { Nq = Positive(item.Nq?.DailySaleVelocity?.World?.Quantity), Hq = Positive(item.Hq?.DailySaleVelocity?.World?.Quantity) },
                Datacenter = new() { Nq = Positive(item.Nq?.DailySaleVelocity?.Dc?.Quantity), Hq = Positive(item.Hq?.DailySaleVelocity?.Dc?.Quantity) },
                Region = new() { Nq = Positive(item.Nq?.DailySaleVelocity?.Region?.Quantity), Hq = Positive(item.Hq?.DailySaleVelocity?.Region?.Quantity) },
            },
        };
    }

    private (string? World, string? Datacenter) Place(uint? worldId) =>
        worldId is { } id && TryWorld(id, out var w) ? (w.Name, w.Datacenter) : (null, null);

    private static DateTime FromMilliseconds(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    public void Dispose()
    {
        http.Dispose();
        happyEyeballs.Dispose();
    }

    // ------------------------------------------------- Réponse d'Universalis

    private sealed class AggregatedResponse
    {
        [JsonPropertyName("results")] public List<ItemResult>? Results { get; set; }
    }

    private sealed class ItemResult
    {
        [JsonPropertyName("itemId")] public uint ItemId { get; set; }
        [JsonPropertyName("nq")] public Aggregate? Nq { get; set; }
        [JsonPropertyName("hq")] public Aggregate? Hq { get; set; }
        [JsonPropertyName("worldUploadTimes")] public List<WorldUpload>? WorldUploadTimes { get; set; }
    }

    private sealed class Aggregate
    {
        [JsonPropertyName("minListing")] public ScopedEntries? MinListing { get; set; }
        [JsonPropertyName("recentPurchase")] public ScopedEntries? RecentPurchase { get; set; }
        [JsonPropertyName("averageSalePrice")] public ScopedEntries? AverageSalePrice { get; set; }
        [JsonPropertyName("dailySaleVelocity")] public ScopedEntries? DailySaleVelocity { get; set; }
    }

    private sealed class ScopedEntries
    {
        [JsonPropertyName("world")] public Entry? World { get; set; }
        [JsonPropertyName("dc")] public Entry? Dc { get; set; }
        [JsonPropertyName("region")] public Entry? Region { get; set; }
    }

    private sealed class Entry
    {
        // Entier pour les annonces et les ventes, décimal pour le prix moyen.
        [JsonPropertyName("price")] public double? Price { get; set; }
        [JsonPropertyName("worldId")] public uint? WorldId { get; set; }
        [JsonPropertyName("timestamp")] public long? Timestamp { get; set; }
        [JsonPropertyName("quantity")] public double? Quantity { get; set; }
    }

    private sealed class DatacenterEntry
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("region")] public string? Region { get; set; }
        [JsonPropertyName("worlds")] public List<uint>? Worlds { get; set; }
    }

    private sealed class WorldUpload
    {
        [JsonPropertyName("worldId")] public uint WorldId { get; set; }
        [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
    }
}
