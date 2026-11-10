using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using MarketTip.Core;

namespace MarketTip;

[Serializable]
public class Configuration : IPluginConfiguration, ITooltipOptions
{
    public int Version { get; set; } = 2;

    /// <summary>Les prix s'affichent dans les infobulles.</summary>
    public bool Enabled { get; set; } = true;

    // --- Contenu de l'infobulle ---

    /// <summary>Jusqu'où chercher l'annonce la moins chère.</summary>
    public Scope SearchScope { get; set; } = Scope.Region;

    public bool ShowLastSale { get; set; } = true;
    public bool ShowDailySales { get; set; } = true;
    public bool ShowAveragePrice { get; set; } = false;
    public bool ShowBothQualities { get; set; } = false;
    public bool ShowAge { get; set; } = true;
    public bool ShowSavings { get; set; } = true;
    public bool WarnStale { get; set; } = true;
    public bool ShowVendorHint { get; set; } = true;

    /// <summary>Ctrl+Maj+U pendant le survol d'un objet ouvre sa page Universalis.</summary>
    public bool UniversalisShortcut { get; set; } = true;

    // --- Surveillance ---
    public List<WatchEntry> Watchlist { get; set; } = new();

    // --- Données ---

    /// <summary>Prix du monde où se trouve le personnage, au lieu de son monde d'origine.</summary>
    public bool UseCurrentWorld { get; set; } = false;

    /// <summary>Les prix de l'inventaire sont demandés à l'avance, pour s'afficher sans attendre.</summary>
    public bool PrefetchInventory { get; set; } = true;

    // --- Fenêtre de réglages ---
    public Vector4 AccentColor { get; set; } = new(0.30f, 0.65f, 1.00f, 1.00f);
    public bool AnimatedSettingsBackground { get; set; } = true;
    public float SettingsOpacity { get; set; } = 0.80f;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

/// <summary>Un objet surveillé, avec le prix sous lequel prévenir (0 : pas d'alerte).</summary>
[Serializable]
public class WatchEntry
{
    public uint ItemId { get; set; }
    public long Threshold { get; set; }
}
