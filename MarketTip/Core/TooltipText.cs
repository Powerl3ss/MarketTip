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

    /// <summary>À remarquer (prix ancien, mieux vaut vendre au marchand) : orangé.</summary>
    Warning,

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
    /// <summary>Jusqu'où chercher l'annonce la moins chère : le monde seul, le centre de données ou la région.</summary>
    Scope SearchScope { get; }

    bool ShowLastSale { get; }
    bool ShowDailySales { get; }
    bool ShowAveragePrice { get; }

    /// <summary>Les deux qualités, au lieu de la seule qualité survolée.</summary>
    bool ShowBothQualities { get; }

    bool ShowAge { get; }

    /// <summary>« -30 % » à côté du prix moins cher ailleurs.</summary>
    bool ShowSavings { get; }

    /// <summary>L'ancienneté d'un prix vieux de plusieurs jours est signalée en orangé.</summary>
    bool WarnStale { get; }

    /// <summary>Signale quand le marchand rachète l'objet aussi cher que le marché.</summary>
    bool ShowVendorHint { get; }
}

/// <summary>Ce que l'on sait de l'objet survolé en dehors du marché. Tout est facultatif.</summary>
public sealed record ItemContext
{
    /// <summary>Prix de rachat par un marchand, pour la qualité survolée. 0 : ne se revend pas.</summary>
    public long VendorPrice { get; init; }
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

    /// <summary>
    /// Le bloc ajouté à l'infobulle. Des lignes courtes, pour tenir dans la largeur de l'infobulle du jeu :
    /// <code>
    /// Prix sur Omega : 2 500 (il y a 2 h)
    /// Moins cher sur Spriggan (Chaos) : 2 033
    /// Dernière vente : 8 000 (il y a 8 h)
    /// Ventes par jour : 6,5
    /// </code>
    /// </summary>
    /// <summary>Âge à partir duquel un prix est signalé comme ancien.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    /// <summary>Taxe du marché retenue pour comparer avec le marchand.</summary>
    public const double MarketTax = 0.05;

    /// <summary>L'annonce la moins chère dans la portée donnée, toutes qualités confondues.</summary>
    public static Optional<Listing> BestListing(MarketData data, Scope scope)
    {
        var qualities = data.Cheapest.Get(scope);
        if (!qualities.Nq.HasValue)
            return qualities.Hq;
        if (!qualities.Hq.HasValue)
            return qualities.Nq;
        return qualities.Nq.Value.Price <= qualities.Hq.Value.Price ? qualities.Nq : qualities.Hq;
    }

    public static List<Segment> Build(MarketData? data, LookupState state, bool hq, ITooltipOptions options, DateTime now, ItemContext? context = null)
    {
        var output = new List<Segment>();
        switch (state)
        {
            case LookupState.NotMarketable:
                return output;

            case LookupState.Failed:
                output.Add(new(SegmentKind.Dim, "Prix du marché indisponibles pour le moment."));
                return output;
        }

        if (data == null)
        {
            output.Add(new(SegmentKind.Dim, "Recherche des prix du marché..."));
            return output;
        }

        new Writer(data, hq, options, now, output, context ?? new ItemContext()).Write();
        return output;
    }

    private sealed class Writer(MarketData data, bool hq, ITooltipOptions options, DateTime now, List<Segment> output, ItemContext context)
    {
        public void Write()
        {
            WritePrice();
            WriteCheaper();
            WriteLastSale();
            WriteNumber("Ventes par jour", options.ShowDailySales, data.DailySales.World, Decimal);
            WriteNumber("Prix moyen", options.ShowAveragePrice, data.AveragePrice.World, value => Gil((long)Math.Round(value)));
            WriteVendorHint();
        }

        // Le prix de la qualité survolée sur le monde de référence, sans se rabattre sur l'autre qualité.
        private Optional<Listing> HomePrice => hq ? data.Cheapest.World.Hq : data.Cheapest.World.Nq;

        // Une fois la taxe du marché retirée, le marchand en donne autant ou plus.
        private void WriteVendorHint()
        {
            if (!options.ShowVendorHint || context.VendorPrice <= 0 || !HomePrice.HasValue)
                return;
            if (HomePrice.Value.Price * (1 - MarketTax) > context.VendorPrice)
                return;
            output.Add(new(SegmentKind.Warning, (output.Count > 0 ? "\n" : string.Empty) + $"Autant vendre au marchand ({Gil(context.VendorPrice)})"));
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

        // Le prix sur le monde de référence : toujours affiché, c'est celui auquel on vend.
        private void WritePrice()
        {
            Line($"Prix sur {data.World} : ");
            var listing = Pick(data.Cheapest.World);
            if (!listing.HasValue)
            {
                output.Add(new(SegmentKind.Dim, "aucune annonce"));
                return;
            }

            Prices(data.Cheapest.World);
            Time(listing.Value.Time);
        }

        // Moins cher ailleurs, dans la portée choisie. Rien si le monde de référence est déjà le moins cher.
        private void WriteCheaper()
        {
            if (options.SearchScope == Scope.World)
                return;

            var qualities = data.Cheapest.Get(options.SearchScope);
            var best = Pick(qualities);
            if (!best.HasValue)
                return;
            var listing = best.Value;
            if (listing.World == null || listing.World == data.World)
                return;

            var home = Pick(data.Cheapest.World);
            if (home.HasValue && home.Value.Price <= listing.Price)
                return;

            // Le monde fait partie de l'intitulé, avec son centre de données.
            var place = string.IsNullOrEmpty(listing.Datacenter) ? listing.World : $"{listing.World} ({listing.Datacenter})";
            // Une seule qualité ici, pour que la ligne reste courte.
            var isHq = hq ? qualities.Hq.HasValue : !qualities.Nq.HasValue;
            Line($"Moins cher sur {place} : ");
            output.Add(new(SegmentKind.Highlight, (isHq ? HqIcon.ToString() : string.Empty) + Gil(listing.Price)));
            if (options.ShowSavings && home.HasValue && home.Value.Price > 0)
            {
                var saved = (int)Math.Round((home.Value.Price - listing.Price) * 100d / home.Value.Price);
                if (saved > 0)
                    output.Add(new(SegmentKind.Dim, $" (-{saved} %)"));
            }
        }

        private void WriteLastSale()
        {
            if (!options.ShowLastSale)
                return;
            var sale = Pick(data.LastSale.World);
            if (!sale.HasValue)
                return;

            Line("Dernière vente : ");
            Prices(data.LastSale.World);
            Time(sale.Value.Time);
        }

        private void WriteNumber(string title, bool shown, Qualities<double> qualities, Func<double, string> format)
        {
            if (!shown || !Pick(qualities).HasValue)
                return;
            Line(title + " : ");
            Values(qualities, v => v, format);
        }

        // ------------------------------------------------------------ Écriture

        private void Line(string text) => output.Add(new(SegmentKind.Text, (output.Count > 0 ? "\n" : string.Empty) + text));

        // La qualité survolée est mise en valeur ; l'autre suit si les deux sont affichées.
        private void Values<T>(Qualities<T> qualities, Func<T, double> value, Func<double, string> format)
            where T : notnull
        {
            var nq = qualities.Nq;
            var hqValue = qualities.Hq;
            var both = options.ShowBothQualities;

            var wroteNq = false;
            if (nq.HasValue && (both || !hq))
            {
                output.Add(new(hq ? SegmentKind.Text : SegmentKind.Highlight, format(value(nq.Value))));
                wroteNq = true;
            }

            if (hqValue.HasValue && (both || hq))
            {
                if (wroteNq)
                    output.Add(new(SegmentKind.Text, " / "));
                output.Add(new(hq ? SegmentKind.Highlight : SegmentKind.Text, HqIcon + format(value(hqValue.Value))));
            }
        }

        private void Prices(Qualities<Listing> qualities) =>
            Values(qualities, l => (double)l.Price, v => Gil((long)v));

        private void Time(DateTime? time)
        {
            if (!options.ShowAge || time is not { } t)
                return;
            var span = now - t;
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;
            var stale = options.WarnStale && span >= StaleAfter;
            output.Add(new(stale ? SegmentKind.Warning : SegmentKind.Dim, $" ({Age(span)})"));
        }
    }
}
