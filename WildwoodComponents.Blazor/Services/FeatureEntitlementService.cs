using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Shared, cached feature-entitlement lookup backing <c>FeatureGateComponent</c>.
    /// Mirrors the JS useFeatures hook: ONE bulk fetch of the user's feature entitlements
    /// (GET app-tiers/{appId}/user-features) is shared by every gate in the circuit instead
    /// of a per-gate network round-trip.
    ///
    /// Failure policy: FAIL OPEN. Client-side gating is UX; the server enforces the real
    /// entitlement. A transient fetch failure is never cached and never locks gates —
    /// <see cref="HasFeatureAsync"/> returns true while entitlements are unknown.
    /// </summary>
    public interface IFeatureEntitlementService
    {
        /// <summary>
        /// Whether the user's plan includes the feature (case-insensitive). FAILS OPEN while
        /// entitlements are unknown (no appId, or the fetch failed): returns true, because the
        /// server enforces the real entitlement and wrongly locking paid features is worse
        /// than briefly showing them.
        /// </summary>
        Task<bool> HasFeatureAsync(string featureCode, string? appId = null);

        /// <summary>
        /// The normalized (upper-cased key) feature map, or null when entitlements are
        /// unknown (no appId configured, or the fetch failed — failures are never cached).
        /// </summary>
        Task<IReadOnlyDictionary<string, bool>?> GetFeaturesAsync(string? appId = null);

        /// <summary>
        /// Drops the cached feature maps AND notifies every mounted FeatureGateComponent to
        /// reload. Call after entitlement-changing mutations (tier change/subscribe/cancel,
        /// feature overrides, add-on purchases) so gates elsewhere in the app don't serve the
        /// old plan for the cache TTL. The subscription-admin flow calls this automatically.
        /// </summary>
        void Invalidate();

        /// <summary>Raised when the cache is invalidated (auth change or entitlement mutation).</summary>
        event Action? EntitlementsChanged;
    }

    public class FeatureEntitlementService : IFeatureEntitlementService, IDisposable
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private readonly IAppTierComponentService _appTierService;
        private readonly IAuthenticationService? _authService;
        private readonly WildwoodComponentsOptions? _options;
        private readonly ILogger<FeatureEntitlementService> _logger;

        // One in-flight/cached task per appId so N concurrent gates share a single request.
        private readonly Dictionary<string, (Task<Dictionary<string, bool>?> Task, DateTime LoadedAt)> _cache = new();
        private readonly object _cacheLock = new();
        private bool _disposed;

        public event Action? EntitlementsChanged;

        public FeatureEntitlementService(
            IAppTierComponentService appTierService,
            ILogger<FeatureEntitlementService> logger,
            IAuthenticationService? authService = null,
            WildwoodComponentsOptions? options = null)
        {
            _appTierService = appTierService;
            _logger = logger;
            _authService = authService;
            _options = options;

            // A login/logout changes who the entitlements belong to — drop the cache and
            // let mounted gates reload through it (one refetch, not one per gate).
            if (_authService != null)
            {
                _authService.OnAuthenticationChanged += OnAuthChanged;
                _authService.OnLogout += OnLogout;
            }
        }

        public async Task<bool> HasFeatureAsync(string featureCode, string? appId = null)
        {
            var features = await GetFeaturesAsync(appId);
            if (features == null) return true; // unknown → fail open; the server enforces
            return features.TryGetValue(featureCode.ToUpperInvariant(), out var enabled) && enabled;
        }

        public async Task<IReadOnlyDictionary<string, bool>?> GetFeaturesAsync(string? appId = null)
        {
            var effectiveAppId = !string.IsNullOrEmpty(appId) ? appId : _options?.AppId;
            if (string.IsNullOrEmpty(effectiveAppId)) return null; // unknown → fail open

            Task<Dictionary<string, bool>?> task;
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(effectiveAppId, out var entry) && DateTime.UtcNow - entry.LoadedAt < CacheTtl)
                {
                    task = entry.Task;
                }
                else
                {
                    task = LoadFeaturesAsync(effectiveAppId);
                    _cache[effectiveAppId] = (task, DateTime.UtcNow);
                }
            }

            var features = await task;

            // Never cache a failure: a transient error must not lock every gate for the TTL.
            // Evict here (after the insert above) so a synchronously-failing load can't be
            // re-pinned; only evict our own task — a newer load may already be in flight.
            if (features == null)
            {
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(effectiveAppId, out var entry) && entry.Task == task)
                    {
                        _cache.Remove(effectiveAppId);
                    }
                }
            }

            return features;
        }

        public void Invalidate()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
            EntitlementsChanged?.Invoke();
        }

        private async Task<Dictionary<string, bool>?> LoadFeaturesAsync(string appId)
        {
            try
            {
                var map = await _appTierService.GetUserFeaturesAsync(appId);

                // Normalize keys so lookups are case-insensitive (codes are conventionally
                // UPPER_SNAKE, but callers have used lowercase variants).
                var normalized = new Dictionary<string, bool>();
                foreach (var pair in map)
                {
                    normalized[pair.Key.ToUpperInvariant()] = pair.Value;
                }
                return normalized;
            }
            catch (Exception ex)
            {
                // Null signals "unknown" — GetFeaturesAsync evicts the failed entry so the
                // failure is never cached, and HasFeatureAsync fails open.
                _logger.LogWarning(ex, "Failed to load feature entitlements for app {AppId} — gates fail open", appId);
                return null;
            }
        }

        private void OnAuthChanged(AuthenticationResponse _) => Invalidate();

        private void OnLogout() => Invalidate();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_authService != null)
            {
                _authService.OnAuthenticationChanged -= OnAuthChanged;
                _authService.OnLogout -= OnLogout;
            }
            GC.SuppressFinalize(this);
        }
    }
}
