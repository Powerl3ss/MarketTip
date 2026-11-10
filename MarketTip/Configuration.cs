using System;
using System.Numerics;
using Dalamud.Configuration;
using MarketTip.Core;

namespace MarketTip;

[Serializable]
public class Configuration : IPluginConfiguration, ITooltipOptions
{
    public int Version { get; set; } = 1;

    /// <summary>Les prix s'affichent dans les infobulles.</summary>
    public bool Enabled { get; set; } = true;

    // --- Prix les plus bas ---
    public bool ShowCheapestWorld { get; set; } = true;
    public bool ShowCheapestDatacenter { get; set; } = true;
    public bool ShowCheapestRegion { get; set; } = false;

    // --- Dernières ventes ---
    public bool ShowLastSaleWorld { get; set; } = true;
    public bool ShowLastSaleDatacenter { get; set; } = false;
    public bool ShowLastSaleRegion { get; set; } = false;

    // --- Statistiques (null : non affichées) ---
    public Scope? AveragePriceScope { get; set; } = null;
    public Scope? DailySalesScope { get; set; } = Scope.World;

    // --- Présentation ---
    public bool ShowBothQualities { get; set; } = true;
    public bool ShowAge { get; set; } = true;
    public bool ShowDatacenterName { get; set; } = true;

    // --- Données ---

    /// <summary>Prix du monde où se trouve le personnage, au lieu de son monde d'origine.</summary>
    public bool UseCurrentWorld { get; set; } = false;

    /// <summary>Les prix de l'inventaire sont demandés à l'avance, pour s'afficher sans attendre.</summary>
    public bool PrefetchInventory { get; set; } = true;

    public bool RefreshWithAlt { get; set; } = true;

    // --- Fenêtre de réglages ---
    public Vector4 AccentColor { get; set; } = new(0.30f, 0.65f, 1.00f, 1.00f);
    public bool AnimatedSettingsBackground { get; set; } = true;
    public float SettingsOpacity { get; set; } = 0.80f;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
