using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using MarketTip.Core;
using MarketTip.Market;
using MarketTip.Tooltip;
using MarketTip.Windows;

namespace MarketTip;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/markettip";
    private const string ShortCommandName = "/mtip";
    private const string ChatPrefix = "[MarketTip] ";

    // Inventaires lus pour demander les prix à l'avance.
    private static readonly InventoryType[] BagInventories =
        { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
    private static readonly InventoryType[] SaddlebagInventories =
        { InventoryType.SaddleBag1, InventoryType.SaddleBag2, InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2 };
    private static readonly InventoryType[] RetainerInventories =
    {
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
        InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
    };

    public Configuration Configuration { get; }
    public readonly WindowSystem WindowSystem = new("MarketTip");

    public UniversalisClient Universalis { get; }
    public PriceLookup Lookup { get; }
    public ItemInfo Items { get; }

    private readonly PriceTooltip tooltip;
    private readonly ConfigWindow configWindow;

    // Dernières lectures de chaque inventaire, pour ne pas redemander les mêmes prix à chaque ouverture.
    private DateTime lastBagScan = DateTime.MinValue;
    private DateTime lastSaddlebagScan = DateTime.MinValue;
    private DateTime lastRetainerScan = DateTime.MinValue;

    // Surveillance : dernière actualisation, et objets dont l'alerte a déjà été annoncée.
    private static readonly TimeSpan WatchRefreshInterval = TimeSpan.FromMinutes(10);
    private DateTime lastWatchRefresh = DateTime.MinValue;
    private readonly HashSet<uint> alerted = new();
    private bool shortcutHeld;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        // Infobulles des réglages : même style que les autres plugins, avec la couleur d'accent choisie.
        Tip.AccentProvider = () => Configuration.AccentColor;

        Universalis = new UniversalisClient();
        Lookup = new PriceLookup(Universalis);
        Items = new ItemInfo(Lookup);
        // Les prix reçus avant la liste des centres de données d'Universalis peuvent nommer le mauvais centre.
        Universalis.DatacentersLoaded += Lookup.Clear;
        tooltip = new PriceTooltip(Configuration, Lookup, Items);
        Lookup.Received += OnPricesReceived;
        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Affiche/masque les prix dans les infobulles. « config » : réglages, « inv » : valeur de l'inventaire, « watch » : surveillance.",
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand) { HelpMessage = "Comme /markettip.", ShowInHelp = false });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;
        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;
        Framework.Update += OnFrameworkUpdate;
        AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, new[] { "Inventory", "InventoryLarge", "InventoryExpansion" }, OnBagsUpdated);
        AddonLifecycle.RegisterListener(AddonEvent.PreSetup, "InventoryBuddy", OnSaddlebagOpened);
        AddonLifecycle.RegisterListener(AddonEvent.PreSetup, new[] { "InventoryRetainer", "InventoryRetainerLarge" }, OnRetainerOpened);

        if (ClientState.IsLoggedIn)
            Framework.RunOnFrameworkThread(OnLogin);
    }

    public void Dispose()
    {
        AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, new[] { "Inventory", "InventoryLarge", "InventoryExpansion" }, OnBagsUpdated);
        AddonLifecycle.UnregisterListener(AddonEvent.PreSetup, "InventoryBuddy", OnSaddlebagOpened);
        AddonLifecycle.UnregisterListener(AddonEvent.PreSetup, new[] { "InventoryRetainer", "InventoryRetainerLarge" }, OnRetainerOpened);
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        configWindow.Dispose();
        Lookup.Received -= OnPricesReceived;
        Universalis.DatacentersLoaded -= Lookup.Clear;
        tooltip.Dispose();
        Lookup.Dispose();
        Universalis.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
    }

    // ---------------------------------------------------------------- Commande

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
            case "settings":
                ToggleConfigUi();
                break;
            case "inv":
            case "inventaire":
                configWindow.OpenInventory();
                break;
            case "watch":
            case "surveillance":
                configWindow.OpenWatchlist();
                break;
            case "clear":
                Lookup.Clear();
                Print("Prix en mémoire effacés : ils seront redemandés.");
                break;
            default:
                Configuration.Enabled = !Configuration.Enabled;
                Configuration.Save();
                Print(Configuration.Enabled ? "Prix affichés dans les infobulles." : "Prix masqués.");
                break;
        }
    }

    public void ToggleConfigUi() => configWindow.Toggle();

    public static void Print(string message) => ChatGui.Print(ChatPrefix + message);

    // ---------------------------------------------------------------- Monde de référence

    /// <summary>Le monde dont les prix sont lus : celui d'origine, ou celui où se trouve le personnage.</summary>
    public static uint ReferenceWorldId(Configuration config)
    {
        if (!PlayerState.IsLoaded)
            return 0;
        return config.UseCurrentWorld ? PlayerState.CurrentWorld.RowId : PlayerState.HomeWorld.RowId;
    }

    // Suivi du monde à chaque image : un changement de monde (visite) ou de réglage vide les prix de l'ancien.
    private void OnFrameworkUpdate(IFramework framework)
    {
        var world = ReferenceWorldId(Configuration);
        if (world != 0)
            Lookup.SetWorld(world);

        if (world != 0 && DateTime.Now - lastWatchRefresh > WatchRefreshInterval)
        {
            lastWatchRefresh = DateTime.Now;
            RefreshWatchlist();
        }

        CheckUniversalisShortcut();
    }

    // ---------------------------------------------------------------- Surveillance

    /// <summary>Redemande les prix des objets surveillés.</summary>
    public void RefreshWatchlist()
    {
        foreach (var entry in Configuration.Watchlist)
        {
            if (Lookup.TryGetMarketableId(entry.ItemId, out var id, out _))
                Lookup.Get(id, refresh: true);
        }
    }

    // Des prix arrivent : un message dans le chat si un objet surveillé passe sous son seuil.
    // Une seule fois, tant que le prix n'est pas remonté au-dessus.
    private void OnPricesReceived(IReadOnlyDictionary<uint, MarketData> received) =>
        Framework.RunOnFrameworkThread(() =>
        {
            foreach (var entry in Configuration.Watchlist)
            {
                if (entry.Threshold <= 0 || !received.TryGetValue(entry.ItemId, out var data))
                    continue;
                var best = TooltipText.BestListing(data, Configuration.SearchScope);
                if (!best.HasValue || best.Value.Price > entry.Threshold)
                {
                    alerted.Remove(entry.ItemId);
                    continue;
                }

                if (!alerted.Add(entry.ItemId))
                    continue;
                var listing = best.Value;
                var place = string.IsNullOrEmpty(listing.Datacenter) ? listing.World : $"{listing.World} ({listing.Datacenter})";
                Print($"{Items.Name(entry.ItemId)} : {TooltipText.Gil(listing.Price)} sur {place}, sous ton seuil de {TooltipText.Gil(entry.Threshold)}.");
            }
        });

    // ---------------------------------------------------------------- Raccourci Universalis

    // Ctrl+Maj+U pendant le survol d'un objet vendable ouvre sa page Universalis. Une fois par appui.
    private void CheckUniversalisShortcut()
    {
        var pressed = Configuration.UniversalisShortcut
                      && KeyState[VirtualKey.CONTROL] && KeyState[VirtualKey.SHIFT] && KeyState[VirtualKey.U];
        if (pressed && !shortcutHeld && Lookup.TryGetMarketableId(GameGui.HoveredItem, out var itemId, out _))
            Util.OpenLink($"https://universalis.app/market/{itemId}");
        shortcutHeld = pressed;
    }

    private void OnLogin()
    {
        lastBagScan = DateTime.MinValue;
        OnFrameworkUpdate(Framework);
        ScanInventories(BagInventories);
    }

    private void OnLogout(int type, int code)
    {
        Lookup.SetWorld(0);
        lastBagScan = lastSaddlebagScan = lastRetainerScan = DateTime.MinValue;
    }

    // ---------------------------------------------------------------- Préchargement

    private void OnBagsUpdated(AddonEvent type, AddonArgs args)
    {
        if (DateTime.Now - lastBagScan < TimeSpan.FromMinutes(1))
            return;
        lastBagScan = DateTime.Now;
        ScanInventories(BagInventories);
    }

    private void OnSaddlebagOpened(AddonEvent type, AddonArgs args)
    {
        if (DateTime.Now - lastSaddlebagScan < TimeSpan.FromSeconds(30))
            return;
        lastSaddlebagScan = DateTime.Now;
        ScanInventories(SaddlebagInventories);
    }

    private void OnRetainerOpened(AddonEvent type, AddonArgs args)
    {
        if (DateTime.Now - lastRetainerScan < TimeSpan.FromSeconds(5))
            return;
        lastRetainerScan = DateTime.Now;
        ScanInventories(RetainerInventories);
    }

    private unsafe void ScanInventories(InventoryType[] inventories)
    {
        if (!Configuration.Enabled || !Configuration.PrefetchInventory || ReferenceWorldId(Configuration) == 0)
            return;

        try
        {
            var found = new HashSet<uint>();
            var manager = InventoryManager.Instance();
            if (manager == null)
                return;
            foreach (var inventory in inventories)
            {
                var container = manager->GetInventoryContainer(inventory);
                if (container == null || !container->IsLoaded)
                    continue;
                for (var i = 0; i < container->Size; i++)
                {
                    var itemId = container->Items[i].ItemId;
                    if (itemId != 0)
                        found.Add(itemId);
                }
            }

            if (found.Count > 0)
                Lookup.Prefetch(found);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Lecture de l'inventaire");
        }
    }
}
