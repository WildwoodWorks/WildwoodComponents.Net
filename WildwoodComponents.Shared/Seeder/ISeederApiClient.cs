using System.Threading;
using System.Threading.Tasks;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>
    /// Typed HTTP client over the WildwoodAPI surface, used by seed tasks to create/reconcile
    /// resources and by the runner to read/write the seed ledger and history. Mirrors the
    /// admin REST conventions: Bearer auth after login, optional X-API-Key, camelCase JSON.
    /// </summary>
    public interface ISeederApiClient
    {
        /// <summary>The bearer token acquired at login, if any.</summary>
        string? BearerToken { get; }

        /// <summary>Optional X-API-Key sent with every request.</summary>
        string? ApiKey { get; set; }

        /// <summary>Ensures the client is authenticated (logs in on first call using the configured credentials).</summary>
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
