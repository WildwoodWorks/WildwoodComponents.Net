using System.Threading;
using System.Threading.Tasks;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>Runs the registered seed tasks against WildwoodAPI, honoring the ledger and recording history.</summary>
    public interface ISeederRunner
    {
        /// <summary>
        /// Run all pending tasks (those not yet seeded at their current version, or previously failed).
        /// Returns a short human-readable summary.
        /// </summary>
        Task<SeederRunSummary> RunPendingAsync(CancellationToken ct = default);
    }

    /// <summary>Outcome of a seeding pass.</summary>
    public sealed record SeederRunSummary(int Ran, int Skipped, int Failed, string Message);
}
