using System;
using System.Collections.Generic;
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
            var ordered = TopoSort(new List<ISeederTask>(_tasks));
            if (ordered.Count == 0)
                return new SeederRunSummary(0, 0, 0, "No seed tasks registered.");

            bool stopOnFirstFailure = _options.StopOnFirstFailureDefault;
            int maxAttempts = Math.Max(1, _options.MaxAttemptsDefault);
            int retryDelaySeconds = Math.Max(0, _options.RetryDelaySecondsDefault);
            var ledger = new Dictionary<string, SeedTaskLedgerDto>(StringComparer.Ordinal);

            if (_options.DryRun)
            {
                // A dry-run touches the server not at all — no login (a real auth side effect:
                // audit rows, LastLoginAt), no config/ledger reads. All tasks are treated as
                // pending; their own dry-run guards keep them from writing.
                _logger.LogInformation("Dry-run: skipping authentication and server reads; treating all tasks as pending.");
            }
            else
            {
                // The auth gate guards the ENTIRE run: without a retry, one transient blip during a
                // coordinated rollout (WildwoodAPI restarting alongside the app) aborts seeding
                // until the next process start. Same bounded-retry knobs as per-task retries.
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await _client.EnsureAuthenticatedAsync(ct);
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (attempt < maxAttempts)
                    {
                        _logger.LogWarning(ex, "Seeder authentication attempt {Attempt}/{Max} failed; retrying in {Delay}s.",
                            attempt, maxAttempts, retryDelaySeconds);
                        await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), ct);
                    }
                }

                // Server config (enable kill-switch + run knobs); only a PERSISTED row overrides
                // the app's option defaults.
                try
                {
                    var config = await _client.GetSeederConfigurationAsync(_options.AppId, ct);
                    if (!config.Enabled)
                    {
                        _logger.LogInformation("Seeder is disabled for app {AppId} (server config). Skipping.", _options.AppId);
                        return new SeederRunSummary(0, ordered.Count, 0, "Seeder disabled via admin configuration.");
                    }
                    // The server returns a TRANSIENT default DTO (empty Id) when no row has been
                    // persisted yet — its knob values are the server's defaults, not an operator's
                    // choice, so only a persisted row overrides the app's option defaults.
                    if (!string.IsNullOrEmpty(config.Id))
                    {
                        stopOnFirstFailure = config.StopOnFirstFailure;
                        maxAttempts = Math.Max(1, config.MaxAttempts);
                        retryDelaySeconds = Math.Max(0, config.RetryDelaySeconds);
                    }
                    else
                    {
                        _logger.LogDebug("No persisted seeder configuration for app {AppId}; using option defaults.", _options.AppId);
                    }
                }
                catch (Exception ex)
                {
                    // Fail CLOSED: the Enabled kill-switch lives in this config, so an unreadable
                    // config must not be treated as "enabled". Seeding is idempotent and retried on
                    // the next startup — skipping one run is cheaper than running against a
                    // deliberate operator disable.
                    _logger.LogWarning(ex, "Could not load seeder configuration; skipping this run (kill-switch state unknown).");
                    return new SeederRunSummary(0, ordered.Count, 0, "Seeder configuration unavailable; run skipped.");
                }

                // Ledger keyed by task key for this environment.
                try
                {
                    foreach (var row in await _client.GetLedgerAsync(_options.AppId, _options.Environment, ct))
                        ledger[row.TaskKey] = row;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load seed ledger; treating all tasks as pending.");
                }
            }

            var correlationId = Guid.NewGuid().ToString("N");
            // One run-scoped state bag shared by every task's context, so a task can hand values
            // (resolved ids, tool names, ...) to later tasks.
            var sharedState = new Dictionary<string, object?>(StringComparer.Ordinal);
            // Keys that failed (or were skipped because a dependency failed) THIS run — their
            // dependents must not run against half-seeded prerequisites.
            var failedKeys = new HashSet<string>(StringComparer.Ordinal);
            int ran = 0, skipped = 0, failed = 0;

            foreach (var task in ordered)
            {
                ct.ThrowIfCancellationRequested();

                // DependsOn is more than ordering: a dependent running after its dependency FAILED
                // can persist artifacts referencing things that were never created (e.g. flows
                // naming AI configs) and then ledger Success over the breakage. Skip instead —
                // Skipped is never recorded, so the dependent re-runs next boot once the
                // dependency succeeds, converging exactly like the failure itself.
                string? failedDep = null;
                foreach (var dep in task.DependsOn)
                {
                    if (failedKeys.Contains(dep))
                    {
                        failedDep = dep;
                        break;
                    }
                }
                if (failedDep != null)
                {
                    skipped++;
                    failedKeys.Add(task.Key); // transitive: this task's own dependents skip too
                    _logger.LogWarning("Seed task '{Key}' skipped: dependency '{Dep}' failed this run.", task.Key, failedDep);
                    continue;
                }

                if (!ShouldRun(task, ledger))
                {
                    skipped++;
                    _logger.LogDebug("Seed task '{Key}' already seeded (v{Version}); skipping.", task.Key, task.Version);
                    continue;
                }

                ledger.TryGetValue(task.Key, out var priorLedger);
                var (result, error) = await RunWithRetriesAsync(task, maxAttempts, retryDelaySeconds, correlationId, sharedState, priorLedger, ct);
                if (result.Status == SeederTaskStatus.Failed)
                {
                    failed++;
                    failedKeys.Add(task.Key);
                    _logger.LogError("Seed task '{Key}' failed: {Message}", task.Key, result.Message);
                    if (stopOnFirstFailure)
                    {
                        _logger.LogError("Aborting seeding: task '{Key}' failed and StopOnFirstFailure is set.", task.Key);
                        break;
                    }
                }
                else if (result.Status == SeederTaskStatus.Skipped)
                {
                    // Ran but declined to do work (dry-run, missing prerequisites) — counting it
                    // as "run" would make an entirely-unseeded pass look healthy in the summary.
                    skipped++;
                    _logger.LogInformation("Seed task '{Key}' -> Skipped: {Message}", task.Key, result.Message);
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
            IDictionary<string, object?> sharedState, SeedTaskLedgerDto? priorLedger, CancellationToken ct)
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
            // Nothing was written during a dry-run, so nothing is recorded either (the client's
            // write guard would reject the POSTs anyway — this avoids the noisy swallowed errors).
            if (!_options.DryRun)
                await RecordAsync(task, result, startedAt, completedAt, correlationId, priorLedger, ct);
            return (result, lastError);
        }

        private async Task RecordAsync(
            ISeederTask task, SeederTaskResult result, DateTime startedAt, DateTime completedAt,
            string correlationId, SeedTaskLedgerDto? priorLedger, CancellationToken ct)
        {
            // A Skipped result means the task's work was NOT performed (dry-run, missing
            // prerequisites, ...). Recording nothing keeps the ledger honest AND avoids an
            // append-only history row per startup from a perpetually-skipped task; the task
            // simply runs again next boot once it can do real work.
            if (result.Status == SeederTaskStatus.Skipped)
                return;

            // The same guard for a PERSISTENTLY failing task: the ledger already says
            // Failed@thisVersion, so re-recording every boot would grow SeedRunHistory without
            // bound across restarts (deploys, drains, crashes). The first failure at this
            // version keeps its history row + ledger state; repeats only log.
            if (result.Status == SeederTaskStatus.Failed
                && priorLedger != null
                && priorLedger.InstalledVersion == task.Version
                && string.Equals(priorLedger.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Seed task '{Key}' still failing at v{Version}; not re-recording.", task.Key, task.Version);
                return;
            }

            // camelCase like every other payload on this surface — consumers of ArtifactsJson
            // should not need a casing exception for this one field.
            var artifactsJson = result.Artifacts is { Count: > 0 }
                ? JsonSerializer.Serialize(result.Artifacts, SeederApiClient.JsonOptions)
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

        /// <summary>
        /// Kahn topological sort by DependsOn, stable in registration order. Unknown deps are
        /// ignored (warned); cycles throw. Written without LINQ (WildwoodComponents no-LINQ rule);
        /// task counts are tiny so the O(n^2) ready-scan is fine.
        /// </summary>
        private List<ISeederTask> TopoSort(List<ISeederTask> tasks)
        {
            var byKey = new Dictionary<string, ISeederTask>(StringComparer.Ordinal);
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                if (byKey.ContainsKey(t.Key))
                    throw new InvalidOperationException($"Duplicate seed task key '{t.Key}'.");
                byKey[t.Key] = t;
                order[t.Key] = i;
            }

            var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
            var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var t in tasks)
            {
                indegree[t.Key] = 0;
                dependents[t.Key] = new List<string>();
            }

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

            var remaining = new List<string>();
            foreach (var t in tasks)
                remaining.Add(t.Key);

            var result = new List<ISeederTask>();
            while (remaining.Count > 0)
            {
                // Pick the ready (indegree 0) task with the lowest registration order — stable.
                string? pick = null;
                foreach (var key in remaining)
                {
                    if (indegree[key] == 0 && (pick == null || order[key] < order[pick]))
                        pick = key;
                }

                if (pick == null)
                    throw new InvalidOperationException("Seed tasks contain a dependency cycle.");

                remaining.Remove(pick);
                result.Add(byKey[pick]);
                foreach (var d in dependents[pick])
                    indegree[d]--;
            }

            return result;
        }
    }
}
