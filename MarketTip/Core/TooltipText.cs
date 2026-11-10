using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarketTip.Core;

/// <summary>Le genre d'un morceau du texte ajouté à l'infobulle.</summary>
public enum SegmentKind
{
    /// <summary>Texte normal.</summary>
    Text,

    /// <summary>Prix de la qualité survolée : mis en valeur.</summary>
    Highlight,

    /// <summary>Texte secondaire (date, message d'attente) : grisé.</summary>
    Dim,

    /// <summary>Icône du jeu : autre monde.</summary>
    CrossWorldIcon,

    /// <summary>Icône du jeu : chargement.</summary>
    LoadingIcon,

    /// <summary>Icône du jeu : avertissement.</summary>
    WarningIcon,
}

public readonly record struct Segment(SegmentKind Kind, string Text = "");

/// <summary>Les réglages qui décident des lignes affichées dans l'infobulle.</summary>
public interface ITooltipOptions
{
    bool ShowCheapestWorld { get; }
    bool ShowCheapestDatacenter { get; }
    bool ShowCheapestRegion { get; }
    bool ShowLastSaleWorld { get; }
    bool ShowLastSaleDatacenter { get; }
    bool ShowLastSaleRegion { get; }

    /// <summary>Portée du prix moyen, ou null pour ne pas l'afficher.</summary>
    Scope? AveragePriceScope { get; }

    /// <summary>Portée du nombre de ventes par jour, ou null pour ne pas l'afficher.</summary>
    Scope? DailySalesScope { get; }

    bool ShowBothQualities { get; }
    bool ShowAge { get; }
    bool ShowDatacenterName { get; }
    bool RefreshWithAlt { get; }
}

/// <summary>Construit le texte des prix ajouté sous l'infobulle d'un objet. Sans le jeu : testé à part.</summary>
public static class TooltipText
{
    public const char GilIcon = '';
    public const char HqIcon = '';

    // Espace simple entre les milliers : la police du jeu n'a pas l'espace fine du français.
    private static readonly NumberFormatInfo Numbers = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = new[] { 3 },
    };

    public static string Number(long value) => value.ToString("N0", Numbers);

    public static string Decimal(double value) => value.ToString(value < 10 ? "0.#" : "N0", Numbers);

    public static string Gil(long value) => Number(value) + GilIcon;

    public static string Age(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return $"il y a {(int)span.TotalDays} j";
        if (span.TotalHours >= 1)
            return $"il y a {(int)span.TotalHours} h";
        if (span.TotalMinutes >= 1)
            return $"il y a {(int)span.TotalMinutes} min";
        return "à l'instant";
    }

    public static List<Segment> Build(MarketData? data, LookupState state, bool hq, ITooltipOptions options, DateTime now)
    {
        var output = new List<Segment>();
        switch (state)
        {
            case LookupState.NotMarketable:
                return output;

            case LookupState.Failed:
                output.Add(new(SegmentKind.WarningIcon));
                output.Add(new(SegmentKind.Dim, " Prix indisponibles : Universalis ne répond pas."));
                if (options.RefreshWithAlt)
                    output.Add(new(SegmentKind.Dim, "\nAlt pour réessayer."));
                return output;
        }

        if (data == null)
        {
            output.Add(new(SegmentKind.LoadingIcon));
            output.Add(new(SegmentKind.Dim, " Recherche des prix du marché..."));
            return output;
        }

        new Writer(data, hq, options, now, output).Write();

        if (output.Count == 0)
        {
            output.Add(new(SegmentKind.Dim, "Aucun prix connu pour cet objet."));
            output.Add(new(SegmentKind.Dim, "\nConsulter le marché en jeu met les prix à jour."));
        }

        return output;
    }

    private sealed class Writer(MarketData data, bool hq, ITooltipOptions options, DateTime now, List<Segment> output)
    {
        public void Write()
        {
            WriteCheapest();
            WriteLastSales();
            WriteStatistic("Prix moyen", data.AveragePrice, options.AveragePriceScope, value => Gil((long)Math.Round(value)));
            WriteStatistic("Ventes par jour", data.DailySales, options.DailySalesScope, Decimal);
        }

        // La qualité survolée, sinon l'autre si les deux sont affichées.
        private Optional<T> Pick<T>(Qualities<T> qualities)
            where T : notnull
        {
            var wanted = hq ? qualities.Hq : qualities.Nq;
            if (wanted.HasValue || !options.ShowBothQualities)
                return wanted;
            return hq ? qualities.Nq : qualities.Hq;
        }

        private void WriteCheapest()
        {
            var header = false;
            void Header()
            {
                if (header)
                    return;
                NewSection("Prix au marché");
                header = true;
            }

            var dcWorld = Pick(data.Cheapest.Datacenter);
            var regionDc = Pick(data.Cheapest.Region);
            var cheapestWorld = dcWorld.HasValue ? dcWorld.Value.World : null;
            var cheapestDc = regionDc.HasValue ? regionDc.Value.Datacenter : null;

            // Moins cher dans la région, s'il est dans un autre centre de données.
            if (options.ShowCheapestRegion && cheapestDc != null && cheapestDc != data.Datacenter)
            {
                Header();
                var listing = regionDc.Value;
                Label("Moins cher", listing.World, cheapestDc);
                Prices(data.Cheapest.Region, l => l.Price);
                Time(listing.Time);
            }

            // Moins cher dans le centre de données, s'il est sur un autre monde.
            var showDc = options.ShowCheapestDatacenter || (options.ShowCheapestRegion && cheapestDc == data.Datacenter);
            if (showDc && cheapestWorld != null && cheapestWorld != data.World)
            {
                Header();
                Label("Moins cher", cheapestWorld, null);
                Prices(data.Cheapest.Datacenter, l => l.Price);
                Time(dcWorld.Value.Time);
            }

            // Sur le monde du joueur : aussi quand le moins cher du centre de données est déjà chez lui.
            var world = Pick(data.Cheapest.World);
            var showWorld = options.ShowCheapestWorld || (showDc && cheapestWorld == data.World);
            if (showWorld && world.HasValue)
            {
                Header();
                Line($"Sur {data.World} : ");
                Prices(data.Cheapest.World, l => l.Price);
                Time(world.Value.Time);
            }
        }

        private void WriteLastSales()
        {
            var header = false;
            void Header()
            {
                if (header)
                    return;
                NewSection("Dernière vente");
                header = true;
            }

            var dcSale = Pick(data.LastSale.Datacenter);
            var regionSale = Pick(data.LastSale.Region);
            var saleWorld = dcSale.HasValue ? dcSale.Value.World : null;
            var saleDc = regionSale.HasValue ? regionSale.Value.Datacenter : null;

            if (options.ShowLastSaleRegion && saleDc != null && saleDc != data.Datacenter)
            {
                Header();
                Label("Dans la région", regionSale.Value.World, saleDc);
                Prices(data.LastSale.Region, l => l.Price);
                Time(regionSale.Value.Time);
            }

            var showDc = options.ShowLastSaleDatacenter || (options.ShowLastSaleRegion && saleDc == data.Datacenter);
            if (showDc && saleWorld != null && saleWorld != data.World)
            {
                Header();
                Label("Dans le centre de données", saleWorld, null);
                Prices(data.LastSale.Datacenter, l => l.Price);
                Time(dcSale.Value.Time);
            }

            var world = Pick(data.LastSale.World);
            var showWorld = options.ShowLastSaleWorld || (showDc && saleWorld == data.World);
            if (showWorld && world.HasValue)
            {
                Header();
                Line($"Sur {data.World} : ");
                Prices(data.LastSale.World, l => l.Price);
                Time(world.Value.Time);
            }
        }

        private void WriteStatistic(string title, Scopes<double> values, Scope? scope, Func<double, string> format)
        {
            if (scope is not { } s)
                return;
            var qualities = values.Get(s);
            if (!Pick(qualities).HasValue)
                return;

            NewSection($"{title} ({ScopeName(s)}) : ");
            Prices(qualities, v => v, format, withGil: false);
        }

        private string ScopeName(Scope scope) => scope switch
        {
            Scope.World => data.World,
            Scope.Datacenter => data.Datacenter,
            _ => data.Region,
        };

        // ------------------------------------------------------------ Écriture

        private void NewSection(string title)
        {
            if (output.Count > 0)
                output.Add(new(SegmentKind.Text, "\n"));
            output.Add(new(SegmentKind.Text, title));
        }

        private void Line(string text) => output.Add(new(SegmentKind.Text, "\n  " + text));

        // « Moins cher (⇄ Monde Centre) : »
        private void Label(string title, string? world, string? datacenter)
        {
            Line(title + " (");
            output.Add(new(SegmentKind.CrossWorldIcon));
            var place = world ?? "?";
            if (datacenter != null && options.ShowDatacenterName)
                place += " " + datacenter;
            output.Add(new(SegmentKind.Text, place + ") : "));
        }

        private void Prices(Qualities<Listing> qualities, Func<Listing, long> price) =>
            Prices(qualities, l => price(l), value => Number((long)value), withGil: true);

        // La qualité survolée est mise en valeur, l'autre suit si les deux sont affichées.
        private void Prices<T>(Qualities<T> qualities, Func<T, double> value, Func<double, string>? format = null, bool withGil = true)
            where T : notnull
        {
            format ??= v => Number((long)v);
            var gil = withGil ? GilIcon.ToString() : string.Empty;
            var nq = qualities.Nq;
            var hqValue = qualities.Hq;
            var both = options.ShowBothQualities;

            var wroteNq = false;
            if (nq.HasValue && (both || !hq || !hqValue.HasValue))
            {
                output.Add(new(hq ? SegmentKind.Text : SegmentKind.Highlight, format(value(nq.Value)) + gil));
                wroteNq = true;
            }

            if (hqValue.HasValue && (both || hq || !nq.HasValue))
            {
                if (wroteNq)
                    output.Add(new(SegmentKind.Text, " / "));
                output.Add(new(hq ? SegmentKind.Highlight : SegmentKind.Text, HqIcon + format(value(hqValue.Value)) + gil));
            }
        }

        private void Time(DateTime? time)
        {
            if (!options.ShowAge || time is not { } t)
                return;
            var span = now - t;
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;
            output.Add(new(SegmentKind.Dim, $" ({Age(span)})"));
        }
    }
}
