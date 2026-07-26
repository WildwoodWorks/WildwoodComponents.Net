using System.Threading;
using System.Threading.Tasks;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>
    /// Typed HTTP client over the WildwoodAPI surface, used by seed tasks to create/reconcile
    /// resources and by the runner to read/write the seed ledger and history. Mirrors the
    /// admin REST conventions: X-API-Key on every request (the primary credential — mint it
    /// with the <c>tiers:manage</c> scope for tier-catalog tasks), Bearer auth after the
    /// deprecated email/password login, camelCase JSON.
    /// </summary>
    public interface ISeederApiClient
    {
        /// <summary>The bearer token acquired at login (deprecated path) or pre-issued, if any.</summary>
        string? BearerToken { get; }

        /// <summary>The app's X-API-Key sent with every request — the seeder's primary credential.</summary>
        string? ApiKey { get; set; }

        /// <summary>
        /// Ensures the client is authenticated. Precedence: pre-issued BearerToken, then
        /// <see cref="ApiKey"/> (no user login), then the deprecated email/password login.
        /// </summary>
        Task EnsureAuthenticatedAsync(CancellationToken ct = default);

        // ---- generic verbs (tasks call arbitrary WildwoodAPI endpoints) ----
        Task<T> GetAsync<T>(string path, CancellationToken ct = default);
        Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken ct = default) where T : class;
        Task<T> PostAsync<T>(string path, object? body, CancellationToken ct = default);
        Task PostAsync(string path, object? body, CancellationToken ct = default);
        Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default);
        Task PutAsync(string path, object body, CancellationToken ct = default);

        // ---- seeder ledger / history / config ----
        Task<SeederConfigurationDto> GetSeederConfigurationAsync(string appId, CancellationToken ct = default);
        Task<System.Collections.Generic.List<SeedTaskLedgerDto>> GetLedgerAsync(string appId, string? environment = null, CancellationToken ct = default);
        Task UpsertLedgerAsync(string appId, UpsertSeedLedgerRequest request, CancellationToken ct = default);
        Task<SeedRunHistoryDto> RecordRunAsync(string appId, RecordSeedRunRequest request, CancellationToken ct = default);
    }
}
