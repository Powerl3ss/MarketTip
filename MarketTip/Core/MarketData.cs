using System;

namespace MarketTip.Core;

/// <summary>Ce que l'on sait des prix d'un objet, vu depuis le monde du joueur.</summary>
public sealed record MarketData
{
    public required string World { get; init; }
    public required string Datacenter { get; init; }
    public required string Region { get; init; }

    /// <summary>Annonce la moins chère, par portée.</summary>
    public required Scopes<Listing> Cheapest { get; init; }

    /// <summary>Dernière vente, par portée.</summary>
    public required Scopes<Listing> LastSale { get; init; }

    /// <summary>Prix de vente moyen, par portée.</summary>
    public required Scopes<double> AveragePrice { get; init; }

    /// <summary>Nombre de ventes par jour, par portée.</summary>
    public required Scopes<double> DailySales { get; init; }
}

/// <summary>Une valeur sur le monde du joueur, son centre de données et sa région.</summary>
public sealed record Scopes<T>
    where T : notnull
{
    public Qualities<T> World { get; init; } = new();
    public Qualities<T> Datacenter { get; init; } = new();
    public Qualities<T> Region { get; init; } = new();

    public Qualities<T> Get(Scope scope) => scope switch
    {
        Scope.World => World,
        Scope.Datacenter => Datacenter,
        _ => Region,
    };
}

/// <summary>Une valeur en qualité normale et en haute qualité, chacune absente si inconnue.</summary>
public sealed record Qualities<T>
    where T : notnull
{
    public Optional<T> Nq { get; init; }
    public Optional<T> Hq { get; init; }
}

/// <summary>Une valeur peut-être absente, pour les classes comme pour les nombres.</summary>
public readonly record struct Optional<T>(bool HasValue, T Value)
    where T : notnull
{
    public static Optional<T> None => default;

    public static implicit operator Optional<T>(T value) => new(true, value);
}

/// <summary>Une annonce ou une vente : prix unitaire, monde, centre de données et date.</summary>
public sealed record Listing(long Price, string? World, string? Datacenter, DateTime? Time);

public enum Scope
{
    World,
    Datacenter,
    Region,
}

/// <summary>Où en est la recherche des prix d'un objet.</summary>
public enum LookupState
{
    /// <summary>L'objet ne se vend pas au marché : rien n'est affiché.</summary>
    NotMarketable,

    /// <summary>Les prix sont demandés à Universalis.</summary>
    Loading,

    /// <summary>Les prix sont connus.</summary>
    Ready,

    /// <summary>Universalis n'a pas répondu.</summary>
    Failed,
}
