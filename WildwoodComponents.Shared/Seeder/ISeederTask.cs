using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>
    /// A single unit of seedable app data (an AI flow, a set of tiers, provider wiring, etc.).
    /// Tasks must be idempotent — running one whose work is already present should reconcile,
    /// not duplicate. Generalizes TrailForecast.Installer's IInstallStep.
    /// </summary>
    public interface ISeederTask
    {
        /// <summary>Stable natural key — the ledger row for this task. e.g. "trailforecast.flow.data-gather".</summary>
        string Key { get; }

        /// <summary>Human-readable display name.</summary>
        string Name { get; }

        /// <summary>Human note describing what this task seeds and why — recorded in history.</summary>
        string Note { get; }

        /// <summary>Bump + redeploy to force this task to re-run on the next startup.</summary>
        int Version { get; }

        /// <summary>Keys of tasks that must complete before this one (topologically ordered).</summary>
        IReadOnlyList<string> DependsOn { get; }

        /// <summary>Perform the seeding. Must be idempotent.</summary>
        Task<SeederTaskResult> RunAsync(SeederContext context, CancellationToken ct);
    }
}
