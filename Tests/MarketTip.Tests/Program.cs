using System;
using System.Collections.Generic;
using System.Linq;
using MarketTip.Core;

// Petit lanceur sans dépendance : chaque test est une méthode, un échec fait échouer la CI.
var tests = new (string Name, Action Run)[]
{
    ("Nombres : espace entre les milliers", Tests.ThousandsSeparator),
    ("Nombres : décimales à la française", Tests.FrenchDecimals),
    ("Ancienneté : minutes, heures, jours", Tests.Ages),
    ("Objet hors marché : rien n'est ajouté", Tests.NotMarketableIsEmpty),
    ("Recherche en cours : message d'attente", Tests.LoadingMessage),
    ("Échec : message, et Alt si activé", Tests.FailedMessage),
    ("Prix sur le monde de référence", Tests.WorldPrice),
    ("Moins cher ailleurs dans le centre de données", Tests.CheaperOnOtherWorld),
    ("Moins cher sur son monde : pas de ligne en double", Tests.CheapestAtHomeNoDuplicate),
    ("Région : seulement si autre centre de données", Tests.RegionOnlyIfOtherDatacenter),
    ("Qualité survolée mise en valeur", Tests.HoveredQualityHighlighted),
    ("Une seule qualité si demandé", Tests.SingleQuality),
    ("Haute qualité absente : rien en qualité unique", Tests.MissingHqHidden),
    ("Ancienneté masquable", Tests.AgeCanBeHidden),
    ("Statistiques : prix moyen et ventes par jour", Tests.Statistics),
    ("Aucun prix connu : message", Tests.NoDataMessage),
};

var failed = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"  ok    {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  ÉCHEC {name} : {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests réussis");
return failed == 0 ? 0 : 1;

internal sealed class Options : ITooltipOptions
{
    public bool ShowCheapestWorld { get; set; } = true;
    public bool ShowCheapestDatacenter { get; set; } = true;
    public bool ShowCheapestRegion { get; set; }
    public bool ShowLastSaleWorld { get; set; }
    public bool ShowLastSaleDatacenter { get; set; }
    public bool ShowLastSaleRegion { get; set; }
    public Scope? AveragePriceScope { get; set; }
    public Scope? DailySalesScope { get; set; }
    public bool ShowBothQualities { get; set; } = true;
    public bool ShowAge { get; set; } = true;
    public bool ShowDatacenterName { get; set; } = true;
    public bool RefreshWithAlt { get; set; } = true;
}

internal static class Tests
{
    private static readonly DateTime Now = new(2026, 11, 10, 20, 0, 0);

    private static void Check(bool condition, string message = "condition fausse")
    {
        if (!condition)
            throw new Exception(message);
    }

    private static void Equal(string expected, string actual)
    {
        if (expected != actual)
            throw new Exception($"attendu « {expected} », obtenu « {actual} »");
    }

    private static Listing L(long price, string world, string dc = "Chaos", double hoursAgo = 1) => new(price, world, dc, Now.AddHours(-hoursAgo));

    private static MarketData Data(
        Qualities<Listing>? world = null, Qualities<Listing>? dc = null, Qualities<Listing>? region = null) => new()
    {
        World = "Moogle",
        Datacenter = "Chaos",
        Region = "Europe",
        Cheapest = new Scopes<Listing>
        {
            World = world ?? new() { Nq = L(1_500, "Moogle"), Hq = L(2_500, "Moogle") },
            Datacenter = dc ?? new() { Nq = L(1_500, "Moogle"), Hq = L(2_500, "Moogle") },
            Region = region ?? new() { Nq = L(1_500, "Moogle"), Hq = L(2_500, "Moogle") },
        },
        LastSale = new Scopes<Listing>(),
        AveragePrice = new Scopes<double>(),
        DailySales = new Scopes<double>(),
    };

    // Tout le texte, sans les icônes.
    private static string Text(List<Segment> segments) => string.Concat(segments.Select(s => s.Text));

    private static List<Segment> Build(MarketData? data, Options? options = null, bool hq = false, LookupState state = LookupState.Ready) =>
        TooltipText.Build(data, state, hq, options ?? new Options(), Now);

    private const string Gil = "";
    private const string Hq = "";

    public static void ThousandsSeparator()
    {
        Equal("1 234 567", TooltipText.Number(1_234_567));
        Equal("980", TooltipText.Number(980));
        Equal("12 500" + Gil, TooltipText.Gil(12_500));
    }

    public static void FrenchDecimals()
    {
        Equal("2,5", TooltipText.Decimal(2.5));
        Equal("3", TooltipText.Decimal(3));
        Equal("1 240", TooltipText.Decimal(1_240.4));
    }

    public static void Ages()
    {
        Equal("à l'instant", TooltipText.Age(TimeSpan.FromSeconds(20)));
        Equal("il y a 5 min", TooltipText.Age(TimeSpan.FromMinutes(5.8)));
        Equal("il y a 3 h", TooltipText.Age(TimeSpan.FromHours(3.2)));
        Equal("il y a 2 j", TooltipText.Age(TimeSpan.FromDays(2.9)));
    }

    public static void NotMarketableIsEmpty() =>
        Check(Build(null, state: LookupState.NotMarketable).Count == 0);

    public static void LoadingMessage()
    {
        var segments = Build(null, state: LookupState.Loading);
        Check(segments.Any(s => s.Kind == SegmentKind.LoadingIcon), "icône de chargement");
        Check(Text(segments).Contains("Recherche des prix"), Text(segments));
    }

    public static void FailedMessage()
    {
        var text = Text(Build(null, state: LookupState.Failed));
        Check(text.Contains("Universalis ne répond pas") && text.Contains("Alt"), text);
        var withoutAlt = Text(Build(null, new Options { RefreshWithAlt = false }, state: LookupState.Failed));
        Check(!withoutAlt.Contains("Alt"), withoutAlt);
    }

    public static void WorldPrice()
    {
        var text = Text(Build(Data(), new Options { ShowAge = false }));
        Equal("Prix au marché\n  Sur Moogle : 1 500" + Gil + " / " + Hq + "2 500" + Gil, text);
    }

    public static void CheaperOnOtherWorld()
    {
        var data = Data(dc: new() { Nq = L(1_100, "Cerberus"), Hq = L(2_000, "Cerberus") });
        var text = Text(Build(data, new Options { ShowAge = false }));
        Check(text.Contains("Moins cher (Cerberus) : 1 100" + Gil), text);
        Check(text.Contains("Sur Moogle : 1 500" + Gil), text);
        Check(text.IndexOf("Cerberus", StringComparison.Ordinal) < text.IndexOf("Sur Moogle", StringComparison.Ordinal), "le moins cher d'abord");
    }

    public static void CheapestAtHomeNoDuplicate()
    {
        // Le moins cher du centre de données est sur Moogle : une seule ligne, même sans « Sur le monde ».
        var text = Text(Build(Data(), new Options { ShowCheapestWorld = false, ShowAge = false }));
        Check(!text.Contains("Moins cher"), text);
        Check(text.Split("Sur Moogle").Length == 2, text);
    }

    public static void RegionOnlyIfOtherDatacenter()
    {
        var options = new Options { ShowCheapestRegion = true, ShowAge = false };
        var sameDc = Text(Build(Data(region: new() { Nq = L(900, "Cerberus", "Chaos") }), options));
        Check(!sameDc.Contains("Cerberus Chaos"), sameDc);

        var otherDc = Text(Build(Data(region: new() { Nq = L(900, "Odin", "Light") }), options));
        Check(otherDc.Contains("Moins cher (Odin Light) : 900" + Gil), otherDc);

        var noDcName = Text(Build(Data(region: new() { Nq = L(900, "Odin", "Light") }), new Options { ShowCheapestRegion = true, ShowDatacenterName = false }));
        Check(noDcName.Contains("Moins cher (Odin) : "), noDcName);
    }

    public static void HoveredQualityHighlighted()
    {
        var nq = Build(Data(), new Options { ShowAge = false });
        Equal("1 500" + Gil, nq.Single(s => s.Kind == SegmentKind.Highlight).Text);

        var hq = Build(Data(), new Options { ShowAge = false }, hq: true);
        Equal(Hq + "2 500" + Gil, hq.Single(s => s.Kind == SegmentKind.Highlight).Text);
    }

    public static void SingleQuality()
    {
        var text = Text(Build(Data(), new Options { ShowBothQualities = false, ShowAge = false }, hq: true));
        Equal("Prix au marché\n  Sur Moogle : " + Hq + "2 500" + Gil, text);
    }

    public static void MissingHqHidden()
    {
        var nqOnly = new Qualities<Listing> { Nq = L(1_500, "Moogle") };
        var single = Build(Data(nqOnly, nqOnly, nqOnly), new Options { ShowBothQualities = false }, hq: true);
        Check(Text(single).Contains("Aucun prix connu"), Text(single));

        // Les deux qualités affichées : le prix normal reste visible.
        var both = Text(Build(Data(nqOnly, nqOnly, nqOnly), new Options { ShowAge = false }, hq: true));
        Check(both.Contains("Sur Moogle : 1 500" + Gil), both);
    }

    public static void AgeCanBeHidden()
    {
        Check(Text(Build(Data())).Contains("(il y a 1 h)"), "ancienneté affichée");
        Check(!Text(Build(Data(), new Options { ShowAge = false })).Contains("il y a"), "ancienneté masquée");
    }

    public static void Statistics()
    {
        var data = Data() with
        {
            AveragePrice = new Scopes<double> { Datacenter = new() { Nq = 1_420.6, Hq = 2_300d } },
            DailySales = new Scopes<double> { World = new() { Nq = 2.5 } },
        };
        var options = new Options { AveragePriceScope = Scope.Datacenter, DailySalesScope = Scope.World, ShowAge = false };
        var text = Text(Build(data, options));
        Check(text.Contains("\nPrix moyen (Chaos) : 1 421" + Gil + " / " + Hq + "2 300" + Gil), text);
        Check(text.Contains("\nVentes par jour (Moogle) : 2,5"), text);
    }

    public static void NoDataMessage()
    {
        var empty = new Qualities<Listing>();
        var text = Text(Build(Data(empty, empty, empty)));
        Check(text.Contains("Aucun prix connu pour cet objet."), text);
    }
}
