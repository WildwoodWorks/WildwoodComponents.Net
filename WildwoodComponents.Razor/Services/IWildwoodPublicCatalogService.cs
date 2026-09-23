using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// The short-lived cache in front of the public catalog — what an app sells, at the price the
/// server is quoting right now.
/// </summary>
/// <remarks>
/// <para>
/// The Razor spelling of the Blazor package's <c>IPublicCatalogService</c> and of JS's
/// <c>usePublicCatalog</c>. Several pricing surfaces on one page — a plan grid, a pack grid, an
/// upgrade panel — share ONE pair of requests instead of each issuing its own, and a price read a
/// moment ago is not read again for the TTL.
/// </para>
/// <para>
/// Two rules are load-bearing. A FAILURE IS NEVER CACHED: an unreadable catalog must not pin
/// "pricing is unavailable" on every page for a minute, so nothing is written when the load throws
/// and the next render retries. And a cached catalog is IMMUTABLE to its callers: the same
/// instance is handed to every request inside the window, so a caller that sorted or filtered it
/// in place would poison every later render. Build a view model from it; never edit it.
/// </para>
/// </remarks>
public interface IWildwoodPublicCatalogService
{
    /// <summary>
    /// Everything the app sells. Served from cache while the entry is younger than the TTL,
    /// otherwise loaded.
    /// </summary>
    /// <param name="appId">The app whose catalog to read.</param>
    /// <param name="currencyOverride">
    /// Used only when the server's responses carry no currency of their own. Part of the cache key,
    /// so two surfaces asking in different currencies do not read each other's answer.
    /// </param>
    /// <param name="forceRefresh">Bypass the TTL and reload.</param>
    /// <exception cref="Exception">
    /// Whatever the app-tier service threw. THROWS rather than returning an empty catalog: a
    /// pricing view has to tell "this app sells nothing" from "pricing is unavailable right now",
    /// and an empty catalog cannot express the second.
    /// </exception>
    Task<PublicCatalog> GetAsync(string appId, string? currencyOverride = null, bool forceRefresh = false);

    /// <summary>
    /// Drops every cached catalog for one app, whatever currency it was read under. Call it after
    /// an operator changes what the app sells; an entitlement change does NOT invalidate the
    /// catalog (what is on offer did not move because one account's plan did).
    /// </summary>
    void Invalidate(string appId);
}
