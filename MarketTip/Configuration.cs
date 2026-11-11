using System;
using System.Numerics;
using Dalamud.Configuration;
using MarketTip.Core;

namespace MarketTip;

[Serializable]
public class Configuration : IPluginConfiguration, ITooltipOptions
{
    public int Version { get; set; } = 3;

    /// <summary>Les prix s'affichent dans les infobulles.</summary>
    public bool Enabled { get; set; } = true;

    // --- Contenu de l'infobulle ---

    /// <summary>Jusqu'où chercher l'annonce la moins chère.</summary>
    public Scope SearchScope { get; set; } = Scope.Region;

    public bool ShowLastSale { get; set; } = true;
    public bool ShowDailySales { get; set; } = true;
    public bool ShowAveragePrice { get; set; } = false;
    public bool ShowBothQualities { get; set; } = false;
    public bool HighlightHq { get; set; } = false;

    // Toujours actifs : ils n'ont plus de réglage, pour garder la fenêtre simple.
    public bool ShowAge => true;
    public bool ShowSavings => true;
    public bool WarnStale => true;
    public bool ShowVendorHint => true;

    /// <summary>Ctrl+Maj+U pendant le survol d'un objet ouvre sa page Universalis.</summary>
    public bool UniversalisShortcut => true;


    // --- Fixes : les prix du monde d'origine, ceux de l'inventaire demandés à l'avance ---
    public bool UseCurrentWorld => false;
    public bool PrefetchInventory => true;

    // --- Fenêtre de réglages ---
    public Vector4 AccentColor { get; set; } = new(0.30f, 0.65f, 1.00f, 1.00f);
    public bool AnimatedSettingsBackground { get; set; } = true;
    public float SettingsOpacity { get; set; } = 0.80f;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
