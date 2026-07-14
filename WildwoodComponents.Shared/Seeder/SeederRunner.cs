using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>
    /// Default <see cref="ISeederRunner"/>. Topologically orders tasks by their DependsOn edges,
    /// consults the server-side ledger to skip already-seeded tasks, runs the rest with bounded
    /// retries, and records ledger + history via the SDK client.
    /// </summary>
    public sealed class SeederRunner : ISeederRunner
    {
        private readonly ISeederApiClient _client;
        private readonly IEnumerable<ISeederTask> _tasks;
        private readonly SeederOptions _options;
        private readonly ILogger<SeederRunner> _logger;

        public SeederRunner(
            ISeederApiClient client,
            IEnumerable<ISeederTask> tasks,
            SeederOptions options,
            ILogger<SeederRunner> logger)
        {
            _client = client;
            _tasks = tasks;
            _options = options;
            _logger = logger;
        }

        public async Task<SeederRunSummary> RunPendingAsync(CancellationToken ct = default)
        {
            var ordered = TopoSort(_tasks.ToList());
            if (ordered.Count == 0)
                return new SeederRunSummary(0, 0, 0, "No seed tasks registered.");

            await _client.EnsureAuthenticatedAsync(ct);

            // Server config (enable kill-switch + run knobs); fall back to option defaults if absent.
            bool stopOnFirstFailure = _options.StopOnFirstFailureDefault;
            int maxAttempts = Math.Max(1, _options.MaxAttemptsDefault);
            int retryDelaySeconds = Math.Max(0, _options.RetryDelaySecondsDefault);
            try
            {
                var config = await _client.GetSeederConfigurationAsync(_options.AppId, ct);
                if (!config.Enabled)
                {
                    _logger.LogInformation("Seeder is disabled for app {AppId} (server config). Skipping.", _options.AppId);
                    return new SeederRunSummary(0, ordered.Count, 0, "Seeder disabled via admin configuration.");
                }
                stopOnFirstFailure = config.StopOnFirstFailure;
                maxAttempts = Math.Max(1, config.MaxAttempts);
                retryDelaySeconds = Math.Max(0, config.RetryDelaySeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load seeder configuration; using option defaults.");
            }

            // Ledger keyed by task key for this environment.
            var ledger = new Dictionary<string, SeedTaskLedgerDto>(StringComparer.Ordinal);
            try
            {
                foreach (var row in await _client.GetLedgerAsync(_options.AppId, _options.Environment, ct))
                    ledger[row.TaskKey] = row;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load seed ledger; treating all tasks as pending.");
            }

            var correlationId = Guid.NewGuid().ToString("N");
            // One run-scoped state bag shared by every task's context, so a task can hand values
            // (resolved ids, tool names, ...) to later tasks.
            var sharedState = new Dictionary<string, object?>(StringComparer.Ordinal);
            int ran = 0, skipped = 0, failed = 0;

            foreach (var task in ordered)
            {
                ct.ThrowIfCancellationRequested();

                if (!ShouldRun(task, ledger))
                {
                    skipped++;
                    _logger.LogDebug("Seed task '{Key}' already seeded (v{Version}); skipping.", task.Key, task.Version);
                    continue;
                }

                var (result, error) = await RunWithRetriesAsync(task, maxAttempts, retryDelaySeconds, correlationId, sharedState, ct);
                if (result.Status == SeederTaskStatus.Failed)
                {
                    failed++;
                    _logger.LogError("Seed task '{Key}' failed: {Message}", task.Key, result.Message);
                    if (stopOnFirstFailure)
                    {
                        _logger.LogError("Aborting seeding: task '{Key}' failed and StopOnFirstFailure is set.", task.Key);
                        break;
                    }
                }
                else
                {
                    ran++;
                    _logger.LogInformation("Seed task '{Key}' -> {Status}: {Message}", task.Key, result.Status, result.Message);
                }
            }

            var msg = $"Seeding complete: {ran} run, {skipped} skipped, {failed} failed.";
            _logger.LogInformation("{Message}", msg);
            return new SeederRunSummary(ran, skipped, failed, msg);
        }

        private bool ShouldRun(ISeederTask task, IReadOnlyDictionary<string, SeedTaskLedgerDto> ledger)
        {
            if (!ledger.TryGetValue(task.Key, out var row))
                return true;                                 // never seeded
            if (row.InstalledVersion < task.Version)
                return true;                                 // newer content shipped
            if (!string.Equals(row.Status, "Success", StringComparison.OrdinalIgnoreCase))
                return true;                                 // last run failed
            return false;                                    // already seeded at this version
        }

        private async Task<(SeederTaskResult Result, Exception? Error)> RunWithRetriesAsync(
            ISeederTask task, int maxAttempts, int retryDelaySeconds, string correlationId,
            IDictionary<string, object?> sharedState, CancellationToken ct)
        {
            var startedAt = DateTime.UtcNow;
            SeederTaskResult result = SeederTaskResult.Failed("Not run");
            Exception? lastError = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var context = new SeederContext(_logger)
                {
                    Client = _client,
                    AppId = _options.AppId,
                    Environment = _options.Environment,
                    ResourcesPath = _options.ResourcesPath,
                    DryRun = _options.DryRun,
                    SharedState = sharedState,
                };

                try
                {
                    result = await task.RunAsync(context, ct);
                    lastError = null;
                    if (result.Status != SeederTaskStatus.Failed)
                        break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    result = SeederTaskResult.Failed(ex.Message);
                }

                if (attempt < maxAttempts)
                {
                    _logger.LogWarning("Seed task '{Key}' attempt {Attempt}/{Max} failed; retrying in {Delay}s.",
                        task.Key, attempt, maxAttempts, retryDelaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), ct);
                }
            }

            var completedAt = DateTime.UtcNow;
            await RecordAsync(task, result, startedAt, completedAt, correlationId, ct);
            return (result, lastError);
        }

        private async Task RecordAsync(
            ISeederTask task, SeederTaskResult result, DateTime startedAt, DateTime completedAt, string correlationId, CancellationToken ct)
        {
            var artifactsJson = result.Artifacts is { Count: > 0 }
                ? JsonSerializer.Serialize(result.Artifacts)
                : null;

            SeedRunHistoryDto? history = null;
            try
            {
                history = await _client.RecordRunAsync(_options.AppId, new RecordSeedRunRequest
                {
                    Environment = _options.Environment,
                    TaskKey = task.Key,
                    TaskName = task.Name,
                    Note = task.Note,
                    Version = task.Version,
                    Status = result.Status.ToString(),
                    Message = result.Message,
                    ArtifactsJson = artifactsJson,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    RunBy = "system:auto",
                    CorrelationId = correlationId,
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record seed history for task '{Key}'.", task.Key);
            }

            try
            {
                await _client.UpsertLedgerAsync(_options.AppId, new UpsertSeedLedgerRequest
                {
                    Environment = _options.Environment,
                    TaskKey = task.Key,
                    TaskName = task.Name,
                    Note = task.Note,
                    InstalledVersion = task.Version,
                    Status = result.Status == SeederTaskStatus.Failed ? "Failed" : "Success",
                    LastRunBy = "system:auto",
                    CurrentHistoryId = history?.Id,
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upsert seed ledger for task '{Key}'.", task.Key);
            }
        }

        /// <summary>Kahn topological sort by DependsOn. Unknown deps are ignored (warned); cycles throw.</summary>
        private List<ISeederTask> TopoSort(List<ISeederTask> tasks)
        {
            var byKey = new Dictionary<string, ISeederTask>(StringComparer.Ordinal);
            foreach (var t in tasks)
            {
                if (byKey.ContainsKey(t.Key))
                    throw new InvalidOperationException($"Duplicate seed task key '{t.Key}'.");
                byKey[t.Key] = t;
            }

            var indegree = tasks.ToDictionary(t => t.Key, _ => 0, StringComparer.Ordinal);
            var dependents = tasks.ToDictionary(t => t.Key, _ => new List<string>(), StringComparer.Ordinal);

            foreach (var t in tasks)
            {
                foreach (var dep in t.DependsOn)
                {
                    if (!byKey.ContainsKey(dep))
                    {
                        _logger.LogWarning("Seed task '{Key}' depends on unknown task '{Dep}'; ignoring.", t.Key, dep);
                        continue;
                    }
                    indegree[t.Key]++;
                    dependents[dep].Add(t.Key);
                }
            }

            // Stable order: process ready tasks in their original registration order.
            var order = tasks.Select((t, i) => (t.Key, i)).ToDictionary(x => x.Key, x => x.i, StringComparer.Ordinal);
            var ready = new List<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            ready.Sort((a, b) => order[a].CompareTo(order[b]));

            var result = new List<ISeederTask>();
            while (ready.Count > 0)
            {
                var key = ready[0];
                ready.RemoveAt(0);
                result.Add(byKey[key]);
                foreach (var d in dependents[key])
                {
                    if (--indegree[d] == 0)
                    {
                        var idx = ready.BinarySearch(d, Comparer<string>.Create((a, b) => order[a].CompareTo(order[b])));
                        ready.Insert(idx < 0 ? ~idx : idx, d);
                    }
                }
            }

            if (result.Count != tasks.Count)
                throw new InvalidOperationException("Seed tasks contain a dependency cycle.");

            return result;
        }
    }
}
