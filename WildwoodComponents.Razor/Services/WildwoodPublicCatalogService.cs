using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <inheritdoc cref="IWildwoodPublicCatalogService"/>
public class WildwoodPublicCatalogService : IWildwoodPublicCatalogService
{
    /// <summary>
    /// Sixty seconds, the same window JS's <c>usePublicCatalog</c> and the Blazor
    /// <c>PublicCatalogService</c> keep a catalog for. Long enough that a page of pricing surfaces
    /// makes one round trip; short enough that an operator's price change reaches the next visitor
    /// rather than the next deploy.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Separates the parts of a cache key. A character neither an app id nor a currency code can
    /// contain, so ("app", "USD-X") and ("app-USD", "X") cannot collide.
    /// </summary>
    private const string KeySeparator = "\u0001";

    private const string KeyPrefix = "ww:regsub:catalog" + KeySeparator;
    private const string ResetPrefix = "ww:regsub:catalog-reset" + KeySeparator;

    private readonly IWildwoodAppTierService _appTierService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WildwoodPublicCatalogService> _logger;

    public WildwoodPublicCatalogService(
        IWildwoodAppTierService appTierService,
        IMemoryCache cache,
        ILogger<WildwoodPublicCatalogService>? logger = null)
    {
        _appTierService = appTierService;
        _cache = cache;
        _logger = logger ?? NullLogger<WildwoodPublicCatalogService>.Instance;
    }

    /// <inheritdoc />
    public async Task<PublicCatalog> GetAsync(string appId, string? currencyOverride = null, bool forceRefresh = false)
    {
        if (appId is not { Length: > 0 })
        {
            throw new ArgumentException("An appId is required to read the public catalog.", nameof(appId));
        }

        var key = KeyPrefix + appId + KeySeparator + (currencyOverride ?? string.Empty);

        if (!forceRefresh && _cache.TryGetValue(key, out PublicCatalog? cached) && cached is not null)
        {
            return cached;
        }

        PublicCatalog catalog;
        try
        {
            catalog = await _appTierService.GetPublicCatalogAsync(appId, currencyOverride);
        }
        catch (Exception ex)
        {
            // Never cache a failure: a transient error must not hold "pricing is unavailable" on
            // every page for the TTL. Nothing is written, so the next render retries.
            _logger.LogWarning(ex, "Failed to load the public catalog for app {AppId}", appId);
            throw;
        }

        // The stored instance is shared with every later request inside the window, so callers
        // treat it as immutable and build their own view models from it.
        using (var entry = _cache.CreateEntry(key))
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            entry.AddExpirationToken(new CancellationChangeToken(ResetTokenSource(appId).Token));
            entry.Value = catalog;
        }

        return catalog;
    }

    /// <inheritdoc />
    public void Invalidate(string appId)
    {
        if (appId is not { Length: > 0 }) return;

        var resetKey = ResetPrefix + appId;
        if (_cache.TryGetValue(resetKey, out CancellationTokenSource? source) && source is not null)
        {
            _cache.Remove(resetKey);
            source.Cancel();
            source.Dispose();
        }
    }

    /// <summary>
    /// One cancellation source per app, itself held in the cache, so every currency's entry for
    /// that app can be dropped together — <see cref="IMemoryCache"/> has no prefix removal.
    /// </summary>
    private CancellationTokenSource ResetTokenSource(string appId)
    {
        var resetKey = ResetPrefix + appId;
        if (_cache.TryGetValue(resetKey, out CancellationTokenSource? existing) && existing is not null)
        {
            return existing;
        }

        var created = new CancellationTokenSource();
        using (var entry = _cache.CreateEntry(resetKey))
        {
            entry.Priority = CacheItemPriority.NeverRemove;
            entry.Value = created;
        }

        return created;
    }
}
