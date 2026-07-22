using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>
    /// Runs the seeder automatically when the app starts. Best-effort and non-fatal: a
    /// WildwoodAPI outage or a failing task never blocks or crashes app startup — it just
    /// leaves data unseeded until the next start. Mirrors GCM's GcmFlowInstaller pattern.
    /// </summary>
    public sealed class SeederRunnerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SeederOptions _options;
        private readonly ILogger<SeederRunnerService> _logger;

        public SeederRunnerService(
            IServiceScopeFactory scopeFactory,
            SeederOptions options,
            ILogger<SeederRunnerService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Local hard gate — lets an app disable seeding without a server round trip.
            if (!_options.RunOnStartup)
            {
                _logger.LogInformation("Seeder RunOnStartup is disabled; not seeding.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.AppId))
            {
                _logger.LogWarning("Seeder not configured (BaseUrl/AppId missing); skipping.");
                return;
            }
            if (!_options.HasCredentials)
            {
                _logger.LogWarning(
                    "Seeder has no admin credentials (AdminEmail/AdminPassword or BearerToken); skipping automatic seeding.");
                return;
            }

            try
            {
                await Task.Delay(_options.StartupDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ISeederRunner>();
                _logger.LogInformation("Seeder starting for app {AppId} (environment '{Environment}').",
                    _options.AppId, _options.Environment);
                var summary = await runner.RunPendingAsync(stoppingToken);
                _logger.LogInformation("Seeder finished: {Summary}", summary.Message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                // Best-effort: never crash the app because seeding failed.
                _logger.LogError(ex, "Seeder run failed (non-fatal). Data may be unseeded until next startup.");
            }
        }
    }
}
