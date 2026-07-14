using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>
    /// State threaded through seed tasks for one seeding pass. Generalizes
    /// TrailForecast.Installer's InstallContext. App-specific typed state that must
    /// pass between tasks is best held in a shared DI service; <see cref="SharedState"/>
    /// is available for loosely-typed hand-off.
    /// </summary>
    public sealed class SeederContext
    {
        public required ISeederApiClient Client { get; init; }
        public required string AppId { get; init; }
        public required string Environment { get; init; }

        /// <summary>Absolute path to a resources directory (templates, prompts). May be empty.</summary>
        public string ResourcesPath { get; init; } = string.Empty;

        /// <summary>When true, no writes are performed.</summary>
        public bool DryRun { get; init; }

        /// <summary>
        /// Loosely-typed cross-task hand-off (e.g. resolved tool names, ids). The runner supplies a
        /// single run-scoped dictionary so state written by one task is visible to later tasks.
        /// </summary>
        public IDictionary<string, object?> SharedState { get; init; } = new Dictionary<string, object?>();

        private readonly ILogger _logger;

        public SeederContext(ILogger logger) => _logger = logger;

        public void Info(string message) => _logger.LogInformation("{Message}", message);
        public void Warn(string message) => _logger.LogWarning("{Message}", message);

        /// <summary>Log an intended write. Returns true when the write should actually run (false during dry-run).</summary>
        public bool ShouldWrite(string intent)
        {
            if (DryRun)
            {
                _logger.LogInformation("[dry-run] would {Intent}", intent);
                return false;
            }
            _logger.LogDebug("{Intent}", intent);
            return true;
        }
    }
}
