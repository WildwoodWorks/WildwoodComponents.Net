using System.Net.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Authentication;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register WildwoodComponents.Razor services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds WildwoodComponents.Razor services with a base URL
    /// </summary>
    public static IServiceCollection AddWildwoodComponentsRazor(this IServiceCollection services, string baseUrl)
    {
        return AddWildwoodComponentsRazor(services, config =>
        {
            config.BaseUrl = baseUrl;
        });
    }

    /// <summary>
    /// Adds WildwoodComponents.Razor services reading from IConfiguration
    /// </summary>
    public static IServiceCollection AddWildwoodComponentsRazor(this IServiceCollection services, IConfiguration configuration, string configSection = "WildwoodAPI")
    {
        return AddWildwoodComponentsRazor(services, options =>
        {
            var section = configuration.GetSection(configSection);

            var baseUrl = section["BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
                options.BaseUrl = baseUrl;

            var apiKey = section["ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
                options.ApiKey = apiKey;

            var appId = section["AppId"];
            if (!string.IsNullOrEmpty(appId))
                options.AppId = appId;

            if (bool.TryParse(section["EnableDetailedErrors"], out var enableDetailedErrors))
                options.EnableDetailedErrors = enableDetailedErrors;

            if (int.TryParse(section["RequestTimeoutSeconds"], out var timeoutSeconds))
                options.RequestTimeoutSeconds = timeoutSeconds;
        });
    }

    /// <summary>
    /// Adds WildwoodComponents.Razor services with configuration action
    /// </summary>
    public static IServiceCollection AddWildwoodComponentsRazor(this IServiceCollection services, Action<WildwoodComponentsRazorOptions> configureOptions)
    {
        var options = new WildwoodComponentsRazorOptions();
        configureOptions(options);

        services.Configure(configureOptions);
        services.AddSingleton(options);

        if (!services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            services.AddHttpClient();
        }

        // Register named HttpClient for WildwoodAPI calls
        var httpClientBuilder = services.AddHttpClient("WildwoodAPI", client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
            }
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        // In dev mode, accept self-signed certificates for localhost WildwoodAPI
        if (options.EnableDetailedErrors)
        {
            httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, sslPolicyErrors) =>
                    sslPolicyErrors == SslPolicyErrors.None
                    || options.BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            });
        }

        // Register services
        RegisterServices(services, options);

        return services;
    }

    /// <summary>
    /// Registers WildwoodCookieAuthenticationEvents for DI.
    /// Call this in addition to AddWildwoodComponentsRazor when using cookie auth with token management.
    /// Then set options.EventsType = typeof(WildwoodCookieAuthenticationEvents) in your cookie config.
    /// </summary>
    public static IServiceCollection AddWildwoodCookieAuthEvents(this IServiceCollection services)
    {
        services.AddScoped<WildwoodCookieAuthenticationEvents>();
        return services;
    }

    private static void RegisterServices(IServiceCollection services, WildwoodComponentsRazorOptions options)
    {
        // Session manager (manages JWT tokens and sessions for server-side use)
        services.AddScoped<IWildwoodSessionManager, WildwoodSessionManager>();

        // Authentication service
        services.AddScoped<IWildwoodAuthService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("WildwoodAPI");
            var sessionManager = sp.GetRequiredService<IWildwoodSessionManager>();
            var logger = sp.GetRequiredService<ILogger<WildwoodAuthService>>();
            return new WildwoodAuthService(httpClient, sessionManager, logger, options.AppId ?? string.Empty);
        });

        // AI Proxy service
        services.AddScoped<IWildwoodAIProxyService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("WildwoodAPI");
            var sessionManager = sp.GetRequiredService<IWildwoodSessionManager>();
            var logger = sp.GetRequiredService<ILogger<WildwoodAIProxyService>>();
            return new WildwoodAIProxyService(httpClient, sessionManager, logger);
        });

        // App Tier service
        services.AddScoped<IWildwoodAppTierService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("WildwoodAPI");
            var sessionManager = sp.GetRequiredService<IWildwoodSessionManager>();
            var logger = sp.GetRequiredService<ILogger<WildwoodAppTierService>>();
            return new WildwoodAppTierService(httpClient, sessionManager, logger);
        });

        // Payment service
        services.AddScoped<IWildwoodPaymentService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("WildwoodAPI");
            var sessionManager = sp.GetRequiredService<IWildwoodSessionManager>();
            var logger = sp.GetRequiredService<ILogger<WildwoodPaymentService>>();
            return new WildwoodPaymentService(httpClient, sessionManager, logger);
        });

        // Registration service
        services.AddScoped<IWildwoodRegistrationService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("WildwoodAPI");
            var sessionManager = sp.GetRequiredService<IWildwoodSessionManager>();
            var logger = sp.GetRequiredService<ILogger<WildwoodRegistrationService>>();
            return new WildwoodRegistrationService(httpClient, sessionManager, logger, options.AppId ?? string.Empty);
        });
    }
}

/// <summary>
/// Configuration options for WildwoodComponents.Razor
/// </summary>
public class WildwoodComponentsRazorOptions
{
    /// <summary>
    /// Base URL for the WildwoodAPI server
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authentication with WildwoodAPI
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Application ID for configuration loading
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Enable detailed error logging
    /// </summary>
    public bool EnableDetailedErrors { get; set; } = true;

    /// <summary>
    /// Timeout for HTTP requests in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}
