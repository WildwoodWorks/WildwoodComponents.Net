using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>
    /// DI registration for the Seeder component. Framework-neutral: works in any .NET host
    /// (Blazor Server, MAUI, ASP.NET Core Web API, worker) that builds an <see cref="IHost"/>.
    /// </summary>
    public static class SeederServiceCollectionExtensions
    {
        /// <summary>
        /// Register the seeder SDK client, runner, and the automatic startup hosted service.
        /// Chain <see cref="AddSeederTask{T}"/> to register each seed task.
        /// </summary>
        public static IServiceCollection AddWildwoodSeeder(this IServiceCollection services, Action<SeederOptions> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));

            var options = new SeederOptions();
            configure(options);
            services.TryAddSingleton(options);

            services.TryAddScoped<ISeederApiClient, SeederApiClient>();
            services.TryAddScoped<ISeederRunner, SeederRunner>();
            services.AddHostedService<SeederRunnerService>();

            return services;
        }

        /// <summary>Register a seed task implementation. Call once per task.</summary>
        public static IServiceCollection AddSeederTask<T>(this IServiceCollection services) where T : class, ISeederTask
        {
            services.AddScoped<ISeederTask, T>();
            return services;
        }
    }
}
