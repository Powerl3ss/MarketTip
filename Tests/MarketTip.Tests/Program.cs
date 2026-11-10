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
    ("Recherche en cours et échec : une ligne", Tests.StatusMessages),
    ("Prix sur le monde de référence", Tests.WorldPrice),
    ("Aucune annonce sur le monde", Tests.NoListing),
    ("Moins cher sur un autre monde", Tests.CheaperOnOtherWorld),
    ("Moins cher dans un autre centre de données", Tests.CheaperInOtherDatacenter),
    ("Pas de ligne si le monde est déjà le moins cher", Tests.HomeIsCheapest),
    ("Portée : monde seul, centre de données", Tests.ScopeLimits),
    ("Qualité survolée seule par défaut", Tests.HoveredQualityOnly),
    ("Les deux qualités, la survolée mise en valeur", Tests.BothQualities),
    ("Dernière vente, ventes par jour, prix moyen", Tests.ExtraLines),
    ("Ancienneté masquable", Tests.AgeCanBeHidden),
    ("Lignes courtes", Tests.LinesStayShort),
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
    public Scope SearchScope { get; set; } = Scope.Region;
    public bool ShowLastSale { get; set; }
    public bool ShowDailySales { get; set; }
    public bool ShowAveragePrice { get; set; }
    public bool ShowBothQualities { get; set; }
    public bool ShowAge { get; set; }
}

internal static class Tests
{
    private static readonly DateTime Now = new(2026, 11, 10, 20, 0, 0);
    private const string Gil = "\uE049";
    private const string Hq = "\uE03C";

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

    private static Qualities<Listing> Q(Listing nq, Listing? hq = null) =>
        hq == null ? new() { Nq = nq } : new() { Nq = nq, Hq = hq };

    private static MarketData Data(Qualities<Listing>? world = null, Qualities<Listing>? dc = null, Qualities<Listing>? region = null)
    {
        var home = world ?? Q(L(1_500, "Omega"), L(2_500, "Omega"));
        return new MarketData
        {
            World = "Omega",
            Datacenter = "Chaos",
            Region = "Europe",
            Cheapest = new Scopes<Listing> { World = home, Datacenter = dc ?? home, Region = region ?? dc ?? home },
            LastSale = new Scopes<Listing> { World = Q(L(1_400, "Omega", hoursAgo: 8), L(2_400, "Omega", hoursAgo: 30)) },
            AveragePrice = new Scopes<double> { World = new() { Nq = 1_420.6, Hq = 2_300d } },
            DailySales = new Scopes<double> { World = new() { Nq = 6.5 } },
        };
    }

    private static string Text(MarketData? data, Options? options = null, bool hq = false, LookupState state = LookupState.Ready) =>
        string.Concat(Build(data, options, hq, state).Select(s => s.Text));

    private static List<Segment> Build(MarketData? data, Options? options = null, bool hq = false, LookupState state = LookupState.Ready) =>
        TooltipText.Build(data, state, hq, options ?? new Options(), Now);

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

    public static void StatusMessages()
    {
        Equal("Recherche des prix du marché...", Text(null, state: LookupState.Loading));
        Equal("Prix du marché indisponibles pour le moment.", Text(null, state: LookupState.Failed));
    }

    public static void WorldPrice() =>
        Equal("Prix : 1 500" + Gil + " Omega", Text(Data()));

    public static void NoListing()
    {
        var text = Text(Data(world: new Qualities<Listing>(), dc: new Qualities<Listing>()));
        Equal("Prix : aucune annonce sur Omega", text);
    }

    public static void CheaperOnOtherWorld()
    {
        var text = Text(Data(dc: Q(L(1_100, "Spriggan"))));
        Equal("Prix : 1 500" + Gil + " Omega\nMoins cher : 1 100" + Gil + " Spriggan", text);
    }

    public static void CheaperInOtherDatacenter()
    {
        var text = Text(Data(dc: Q(L(1_100, "Spriggan")), region: Q(L(900, "Odin", "Light"))));
        Check(text.EndsWith("\nMoins cher : 900" + Gil + " Odin, Light"), text);
        Check(!text.Contains("Spriggan"), text);

        // Sans annonce sur son monde, le moins cher ailleurs reste affiché.
        var none = Text(Data(world: new Qualities<Listing>(), region: Q(L(900, "Odin", "Light"))));
        Check(none.Contains("Prix : aucune annonce sur Omega\nMoins cher : 900" + Gil + " Odin, Light"), none);
    }

    public static void HomeIsCheapest()
    {
        Check(!Text(Data()).Contains("Moins cher"), "le monde est le moins cher");
        // À prix égal ailleurs, pas de ligne non plus.
        Check(!Text(Data(dc: Q(L(1_500, "Spriggan")))).Contains("Moins cher"), "prix égal");
    }

    public static void ScopeLimits()
    {
        var data = Data(dc: Q(L(1_100, "Spriggan")), region: Q(L(900, "Odin", "Light")));
        Check(!Text(data, new Options { SearchScope = Scope.World }).Contains("Moins cher"), "monde seul");
        var dc = Text(data, new Options { SearchScope = Scope.Datacenter });
        Check(dc.Contains("Moins cher : 1 100" + Gil + " Spriggan") && !dc.Contains("Odin"), dc);
    }

    public static void HoveredQualityOnly()
    {
        Equal("Prix : " + Hq + "2 500" + Gil + " Omega", Text(Data(), hq: true));
        // Pas d'annonce en haute qualité : le prix normal n'est pas affiché à sa place.
        var nqOnly = Q(L(1_500, "Omega"));
        Equal("Prix : aucune annonce sur Omega", Text(Data(world: nqOnly), hq: true));
    }

    public static void BothQualities()
    {
        var segments = Build(Data(), new Options { ShowBothQualities = true }, hq: true);
        Equal("Prix : 1 500" + Gil + " / " + Hq + "2 500" + Gil + " Omega", string.Concat(segments.Select(s => s.Text)));
        Equal(Hq + "2 500" + Gil, segments.Single(s => s.Kind == SegmentKind.Highlight).Text);
    }

    public static void ExtraLines()
    {
        var options = new Options { ShowLastSale = true, ShowDailySales = true, ShowAveragePrice = true };
        var text = Text(Data(), options);
        Equal("Prix : 1 500" + Gil + " Omega\nDernière vente : 1 400" + Gil + "\nVentes par jour : 6,5\nPrix moyen : 1 421" + Gil, text);
        // En haute qualité : pas de ventes par jour connues, la ligne disparaît.
        Check(!Text(Data(), options, hq: true).Contains("Ventes par jour"), "ventes HQ inconnues");
    }

    public static void AgeCanBeHidden()
    {
        var text = Text(Data(), new Options { ShowAge = true, ShowLastSale = true });
        Check(text.Contains("Prix : 1 500" + Gil + " Omega (il y a 1 h)"), text);
        Check(text.Contains("Dernière vente : 1 400" + Gil + " (il y a 8 h)"), text);
        Check(!Text(Data()).Contains("il y a"), "ancienneté masquée");
    }

    public static void LinesStayShort()
    {
        // Le pire cas courant : gros prix, deux qualités, autre centre de données, ancienneté.
        var data = Data(
            world: Q(L(1_234_567, "Omega"), L(2_345_678, "Omega")),
            region: Q(L(1_034_567, "Shiva", "Light"), L(2_045_678, "Shiva", "Light")));
        var options = new Options { ShowBothQualities = true, ShowAge = true, ShowLastSale = true, ShowDailySales = true, ShowAveragePrice = true };
        foreach (var line in Text(data, options).Split('\n'))
            Check(line.Length <= 52, $"ligne trop longue ({line.Length}) : {line}");
    }
}
