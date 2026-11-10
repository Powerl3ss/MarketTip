using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using MarketTip.Core;

namespace MarketTip.Market;

/// <summary>
/// Garde les prix déjà reçus et regroupe les demandes : les objets demandés en même temps partent dans une seule
/// requête à Universalis.
/// </summary>
public sealed class PriceLookup : IDisposable
{
    /// <summary>Durée pendant laquelle un prix reçu est réutilisé sans le redemander.</summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(60);

    /// <summary>Délai avant de redemander un objet dont la requête a échoué.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly UniversalisClient client;
    private readonly ConcurrentDictionary<uint, (MarketData Data, DateTime Received)> cache = new();
    private readonly ConcurrentDictionary<uint, byte> pending = new();
    private readonly ConcurrentDictionary<uint, DateTime> failed = new();
    private readonly ConcurrentQueue<uint> queue = new();
    private readonly CancellationTokenSource stop = new();
    private readonly ExcelSheet<Item> items;

    // Le monde depuis lequel les prix sont lus. Les prix changent avec lui : le cache est vidé s'il change.
    private uint worldId;

    /// <summary>Appelé quand des prix arrivent (hors du fil du jeu).</summary>
    public event Action<IReadOnlyDictionary<uint, MarketData>>? Received;

    /// <summary>Appelé quand une requête échoue, avec les objets concernés (hors du fil du jeu).</summary>
    public event Action<IReadOnlyCollection<uint>>? Failed;

    public PriceLookup(UniversalisClient client)
    {
        this.client = client;
        items = Plugin.DataManager.GetExcelSheet<Item>();
        _ = Task.Run(ProcessQueue);
    }

    /// <summary>Nombre d'objets dont le prix est en mémoire.</summary>
    public int CachedCount => cache.Count;

    /// <summary>Date de la dernière réponse d'Universalis, réussie ou non.</summary>
    public DateTime? LastAnswer { get; private set; }

    /// <summary>La dernière requête a échoué.</summary>
    public bool LastFailed { get; private set; }

    public uint WorldId => worldId;

    /// <summary>Change le monde de référence. Les prix d'un autre monde ne sont plus valables : le cache est vidé.</summary>
    public void SetWorld(uint world)
    {
        if (world == worldId)
            return;
        worldId = world;
        Clear();
    }

    public void Clear()
    {
        cache.Clear();
        failed.Clear();
        queue.Clear();
        pending.Clear();
    }

    /// <summary>
    /// L'identifiant d'objet survolé tel que le jeu le donne (+1 000 000 en haute qualité, +500 000 pour un objet de
    /// collection), ramené à l'objet de base s'il se vend au marché.
    /// </summary>
    public bool TryGetMarketableId(ulong hoveredId, out uint itemId, out bool hq)
    {
        itemId = (uint)(hoveredId % 500_000);
        hq = hoveredId is >= 1_000_000 and < 2_000_000;
        if (hoveredId is >= 2_000_000 or (>= 500_000 and < 1_000_000))
            return false;
        return items.GetRowOrDefault(itemId) is { ItemSearchCategory.RowId: not 0 };
    }

    /// <summary>Les prix connus de l'objet, ou l'état de la recherche. Lance la recherche si besoin.</summary>
    public (MarketData? Data, LookupState State) Get(uint itemId, bool refresh = false)
    {
        if (refresh)
        {
            cache.TryRemove(itemId, out _);
            failed.TryRemove(itemId, out _);
        }
        else if (cache.TryGetValue(itemId, out var cached))
        {
            if (DateTime.Now - cached.Received < CacheDuration)
                return (cached.Data, LookupState.Ready);
            cache.TryRemove(itemId, out _);
        }

        if (failed.TryGetValue(itemId, out var when))
        {
            if (DateTime.Now - when < RetryDelay)
                return (null, LookupState.Failed);
            failed.TryRemove(itemId, out _);
        }

        Request(itemId);
        return (null, LookupState.Loading);
    }

    /// <summary>Demande les prix de plusieurs objets à l'avance (inventaire), sans attendre.</summary>
    public void Prefetch(IEnumerable<uint> itemIds)
    {
        foreach (var id in itemIds)
        {
            if (!TryGetMarketableId(id, out var itemId, out _))
                continue;
            if (cache.TryGetValue(itemId, out var cached) && DateTime.Now - cached.Received < CacheDuration)
                continue;
            if (failed.ContainsKey(itemId))
                continue;
            Request(itemId);
        }
    }

    private void Request(uint itemId)
    {
        if (pending.TryAdd(itemId, 0))
            queue.Enqueue(itemId);
    }

    // Toutes les 200 ms, les objets en attente partent par paquets.
    private async Task ProcessQueue()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                while (!queue.IsEmpty && worldId != 0)
                {
                    var batch = new List<uint>();
                    while (batch.Count < UniversalisClient.MaxItemsPerRequest && queue.TryDequeue(out var id))
                        batch.Add(id);
                    if (batch.Count > 0)
                        await FetchBatch(worldId, batch);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt du plugin.
        }
    }

    private async Task FetchBatch(uint world, List<uint> batch)
    {
        try
        {
            var result = await client.Fetch(world, batch, stop.Token);
            // Le monde a changé pendant la requête : ces prix ne valent plus.
            if (world != worldId)
                return;

            var now = DateTime.Now;
            foreach (var (id, data) in result)
                cache[id] = (data, now);
            LastAnswer = now;
            LastFailed = false;
            Received?.Invoke(result);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Prix indisponibles pour {Count} objets", batch.Count);
            var now = DateTime.Now;
            foreach (var id in batch)
                failed[id] = now;
            LastAnswer = now;
            LastFailed = true;
            Failed?.Invoke(batch);
        }
        finally
        {
            foreach (var id in batch)
                pending.TryRemove(id, out _);
        }
    }

    public void Dispose()
    {
        // Le jeton n'est pas libéré : la boucle d'envoi peut encore le lire en s'arrêtant.
        stop.Cancel();
    }
}
