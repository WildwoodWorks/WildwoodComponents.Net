using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// The shared, short-lived cache in front of the public catalog — what an app sells, at the
    /// price the server is quoting right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The .NET spelling of JS's <c>usePublicCatalog</c>
    /// (packages/wildwood-react-shared/src/hooks/usePublicCatalog.ts). Several pricing surfaces on
    /// one page — a plan grid, a pack grid, an upgrade panel — share ONE pair of requests instead
    /// of each issuing its own, and a price that was read a moment ago is not read again for the
    /// TTL.
    /// </para>
    /// <para>
    /// Two rules are load-bearing. A FAILURE IS NEVER CACHED: an unreadable catalog must not pin
    /// "pricing is unavailable" on the page for a minute, so a failed load evicts itself and the
    /// next call retries. And nothing here ever invents or remembers a price: the cache holds
    /// whole catalogs the server just built, and when it holds none the caller is told so rather
    /// than handed an empty one.
    /// </para>
    /// </remarks>
    public interface IPublicCatalogService
    {
        /// <summary>
        /// Everything the app sells. Served from cache while it is younger than the TTL, otherwise
        /// loaded; concurrent callers share the one in-flight load.
        /// </summary>
        /// <param name="appId">The app whose catalog to read.</param>
        /// <param name="currencyOverride">
        /// Used only when the server's responses carry no currency of their own. Part of the cache
        /// key, so two surfaces asking in different currencies do not read each other's answer.
        /// </param>
        /// <param name="forceRefresh">Bypass the TTL — what a Retry button asks for.</param>
        /// <exception cref="Exception">
        /// Whatever the app-tier service threw. THROWS rather than returning an empty catalog: a
        /// pricing view has to tell "this app sells nothing" from "pricing is unavailable right
        /// now", and an empty catalog cannot express the second.
        /// </exception>
        Task<PublicCatalog> GetAsync(string appId, string? currencyOverride = null, bool forceRefresh = false);

        /// <summary>
        /// Drops the cached catalog for one app, or every app when <paramref name="appId"/> is
        /// null. Call it after an operator changes what the app sells; entitlement changes do NOT
        /// invalidate the catalog (what is on offer did not move because one account's plan did).
        /// </summary>
        void Invalidate(string? appId = null);
    }

    /// <inheritdoc cref="IPublicCatalogService"/>
    public class PublicCatalogService : IPublicCatalogService
    {
        /// <summary>
        /// Sixty seconds, the same window JS's <c>usePublicCatalog</c> keeps a catalog for. Long
        /// enough that a page of pricing surfaces makes one round trip; short enough that an
        /// operator's price change reaches the next visitor rather than the next deploy.
        /// </summary>
        public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Separates the two halves of a cache key. A character neither an app id nor a currency
        /// code can contain, so ("app", "USD-X") and ("app-USD", "X") cannot collide.
        /// </summary>
        private const string KeySeparator = "\u0001";

        private readonly IAppTierComponentService _appTierService;
        private readonly ILogger<PublicCatalogService> _logger;
        private readonly TimeProvider _timeProvider;

        // One in-flight/cached task per (app, currency override) so N surfaces share one load.
        private readonly Dictionary<string, (Task<PublicCatalog> Task, DateTimeOffset LoadedAt)> _cache =
            new Dictionary<string, (Task<PublicCatalog>, DateTimeOffset)>(StringComparer.Ordinal);

        private readonly object _cacheLock = new object();

        public PublicCatalogService(
            IAppTierComponentService appTierService,
            ILogger<PublicCatalogService>? logger = null,
            TimeProvider? timeProvider = null)
        {
            _appTierService = appTierService;
            _logger = logger ?? NullLogger<PublicCatalogService>.Instance;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc />
        public async Task<PublicCatalog> GetAsync(string appId, string? currencyOverride = null, bool forceRefresh = false)
        {
            if (appId is not { Length: > 0 })
            {
                throw new ArgumentException("An appId is required to read the public catalog.", nameof(appId));
            }

            var key = CacheKey(appId, currencyOverride);
            var now = _timeProvider.GetUtcNow();

            Task<PublicCatalog> task;
            lock (_cacheLock)
            {
                if (!forceRefresh &&
                    _cache.TryGetValue(key, out var entry) &&
                    now - entry.LoadedAt < CacheTtl)
                {
                    task = entry.Task;
                }
                else
                {
                    task = _appTierService.GetPublicCatalogAsync(appId, currencyOverride);
                    _cache[key] = (task, now);
                }
            }

            try
            {
                return await task;
            }
            catch (Exception ex)
            {
                // Never cache a failure: a transient error must not hold "pricing is unavailable"
                // on the page for the TTL. Evict only OUR task — a newer load may already be in
                // flight behind a Retry.
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(key, out var entry) && ReferenceEquals(entry.Task, task))
                    {
                        _cache.Remove(key);
                    }
                }

                _logger.LogWarning(ex, "Failed to load the public catalog for app {AppId}", appId);
                throw;
            }
        }

        /// <inheritdoc />
        public void Invalidate(string? appId = null)
        {
            lock (_cacheLock)
            {
                if (appId is not { Length: > 0 })
                {
                    _cache.Clear();
                    return;
                }

                // Every currency override for the app goes, not just the default one: they are all
                // views of the same catalog.
                var prefix = appId + KeySeparator;
                var doomed = new List<string>();
                foreach (var pair in _cache)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) doomed.Add(pair.Key);
                }

                foreach (var key in doomed) _cache.Remove(key);
            }
        }

        /// <summary>
        /// App id and currency override, separated by a character neither can contain, so
        /// ("app", "USD-X") and ("app-USD", "X") cannot collide.
        /// </summary>
        private static string CacheKey(string appId, string? currencyOverride)
        {
            return appId + KeySeparator + (currencyOverride ?? string.Empty);
        }
    }
}
