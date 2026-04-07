using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using WildwoodComponents.Blazor.Services.Payment;

namespace WildwoodComponents.Blazor.Extensions
{
    /// <summary>
    /// Extension methods for IServiceCollection to simplify WildwoodComponents registration
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds WildwoodComponents services to the service collection with simplified configuration
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="baseUrl">Base URL for the WildwoodAPI server</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddWildwoodComponents(this IServiceCollection services, string baseUrl)
        {
            return AddWildwoodComponents(services, config =>
            {
                config.BaseUrl = baseUrl;
            });
        }

        /// <summary>
        /// Adds WildwoodComponents services to the service collection reading from IConfiguration
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configuration">The configuration instance to read from</param>
        /// <param name="configSection">The configuration section name (defaults to "WildwoodAPI")</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddWildwoodComponents(this IServiceCollection services, IConfiguration configuration, string configSection = "WildwoodAPI")
        {
            return AddWildwoodComponents(services, options =>
            {
                var section = configuration.GetSection(configSection);
                
                // Read BaseUrl from configuration, fallback to default if not found
                var baseUrl = section["BaseUrl"];
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    options.BaseUrl = baseUrl;
                }
                
                // Read other configuration values
                var apiKey = section["ApiKey"];
                if (!string.IsNullOrEmpty(apiKey))
                {
                    options.ApiKey = apiKey;
                }
                
                var appId = section["AppId"];
                if (!string.IsNullOrEmpty(appId))
                {
                    options.AppId = appId;
                }
                
                // Read optional settings with defaults
                if (bool.TryParse(section["EnableDetailedErrors"], out var enableDetailedErrors))
                {
                    options.EnableDetailedErrors = enableDetailedErrors;
                }
                
                if (int.TryParse(section["RequestTimeoutSeconds"], out var timeoutSeconds))
                {
                    options.RequestTimeoutSeconds = timeoutSeconds;
                }

                // Read session management settings
                if (int.TryParse(section["SessionExpirationMinutes"], out var sessionMinutes))
                {
                    options.SessionExpirationMinutes = sessionMinutes;
                }

                if (bool.TryParse(section["EnableAutoTokenRefresh"], out var enableAutoRefresh))
                {
                    options.EnableAutoTokenRefresh = enableAutoRefresh;
                }

                if (bool.TryParse(section["SlidingExpiration"], out var slidingExpiration))
                {
                    options.SlidingExpiration = slidingExpiration;
                }

                if (bool.TryParse(section["PersistentSession"], out var persistentSession))
                {
                    options.PersistentSession = persistentSession;
                }

                if (bool.TryParse(section["EnableAntiforgeryValidation"], out var enableAntiforgery))
                {
                    options.EnableAntiforgeryValidation = enableAntiforgery;
                }

                var appVersion = section["AppVersion"];
                if (!string.IsNullOrEmpty(appVersion))
                {
                    options.AppVersion = appVersion;
                }
            });
        }

        /// <summary>
        /// Adds WildwoodComponents services to the service collection with configuration
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configureOptions">Configuration action</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddWildwoodComponents(this IServiceCollection services, Action<WildwoodComponentsOptions> configureOptions)
        {
            var options = new WildwoodComponentsOptions();
            configureOptions(options);

            // Register configuration as IOptions<T> pattern
            services.Configure<WildwoodComponentsOptions>(configureOptions);
            services.AddSingleton(options);

            // Register HttpClient if not already registered
            if (!services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
            {
                services.AddHttpClient();
            }

            // Register WildwoodComponents.Blazor services using reflection to avoid compile-time dependencies
            var wildwoodAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "WildwoodComponents.Blazor");

            if (wildwoodAssembly != null)
            {
                RegisterWildwoodServices(services, wildwoodAssembly, options);
            }
            else
            {
                throw new InvalidOperationException("WildwoodComponents.Blazor assembly not found. Ensure the package is properly referenced.");
            }

            return services;
        }

        /// <summary>
        /// Registers WildwoodComponents services using reflection
        /// </summary>
        private static void RegisterWildwoodServices(IServiceCollection services, Assembly wildwoodAssembly, WildwoodComponentsOptions options)
        {
            try
            {
                // Register theme service
                RegisterService(services, wildwoodAssembly, 
                    "WildwoodComponents.Blazor.Services.IComponentThemeService",
                    "WildwoodComponents.Blazor.Services.ComponentThemeService");

                // Register local storage service
                RegisterService(services, wildwoodAssembly,
                    "WildwoodComponents.Blazor.Services.ILocalStorageService",
                    "WildwoodComponents.Blazor.Services.LocalStorageService");

                // Register authentication service with dynamic constructor resolution
                RegisterAuthenticationService(services, wildwoodAssembly, options);

                // Register captcha service
                RegisterService(services, wildwoodAssembly,
                    "WildwoodComponents.Blazor.Services.ICaptchaService",
                    "WildwoodComponents.Blazor.Services.CaptchaService");

                // Register notification service
                RegisterService(services, wildwoodAssembly,
                    "WildwoodComponents.Blazor.Services.INotificationService",
                    "WildwoodComponents.Blazor.Services.NotificationService");

                // Register platform detection service
                RegisterPlatformDetectionService(services, wildwoodAssembly);

                // Register payment provider service with HttpClient dependency
                RegisterPaymentProviderService(services, wildwoodAssembly, options);

                // Register payment service with HttpClient dependency
                RegisterPaymentService(services, wildwoodAssembly, options);

                // Register subscription service with HttpClient dependency
                RegisterSubscriptionService(services, wildwoodAssembly, options);

                // Register configuration service
                RegisterConfigurationService(services, wildwoodAssembly, options);

                // Register payment script providers and loader
                RegisterPaymentScriptServices(services);

                // Register other services as needed
                RegisterAdditionalServices(services, wildwoodAssembly, options);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to register WildwoodComponents services: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Registers a service using reflection
        /// </summary>
        private static void RegisterService(IServiceCollection services, Assembly assembly, string interfaceName, string implementationName)
        {
            var serviceInterface = assembly.GetType(interfaceName);
            var serviceImplementation = assembly.GetType(implementationName);

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceImplementation);
            }
            else
            {
                // Service may not exist, which is fine for optional services
                Console.WriteLine($"Optional service not found: {interfaceName} or {implementationName}");
            }
        }

        /// <summary>
        /// Registers the authentication service with direct dependency injection
        /// </summary>
        private static void RegisterAuthenticationService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            // Register the authentication service directly
            services.AddScoped<WildwoodComponents.Blazor.Services.IAuthenticationService>(serviceProvider =>
            {
                try
                {
                    // Create HttpClient with configuration
                    var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
                    var httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);
                    
                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    // Get required dependencies
                    var localStorage = serviceProvider.GetRequiredService<WildwoodComponents.Blazor.Services.ILocalStorageService>();
                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.AuthenticationService>() 
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.AuthenticationService>.Instance;

                    // Create the service instance directly
                    return new WildwoodComponents.Blazor.Services.AuthenticationService(httpClient, localStorage, logger);
                }
                catch (Exception ex)
                {
                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger("WildwoodComponents.Blazor.ServiceRegistration");
                    logger?.LogError(ex, "Failed to create AuthenticationService instance");
                    throw;
                }
            });
        }

        /// <summary>
        /// Registers the configuration service
        /// </summary>
        private static void RegisterConfigurationService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var configServiceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.IConfigurationService");
            var configServiceImpl = assembly.GetType("WildwoodComponents.Blazor.Services.ConfigurationService");

            if (configServiceInterface != null && configServiceImpl != null)
            {
                services.AddScoped(configServiceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);
                    
                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    return Activator.CreateInstance(configServiceImpl, httpClient)!;
                });
            }
        }

        /// <summary>
        /// Registers the platform detection service
        /// </summary>
        private static void RegisterPlatformDetectionService(IServiceCollection services, Assembly assembly)
        {
            var serviceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.IPlatformDetectionService");
            var serviceImplementation = assembly.GetType("WildwoodComponents.Blazor.Services.PlatformDetectionService");

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceProvider =>
                {
                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.PlatformDetectionService>()
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.PlatformDetectionService>.Instance;
                    
                    // JSRuntime is optional - may not be available in all contexts
                    var jsRuntime = serviceProvider.GetService<Microsoft.JSInterop.IJSRuntime>();
                    
                    return new WildwoodComponents.Blazor.Services.PlatformDetectionService(logger, jsRuntime);
                });
            }
        }

        /// <summary>
        /// Registers the payment provider service with HttpClient dependency
        /// </summary>
        private static void RegisterPaymentProviderService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var serviceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.IPaymentProviderService");
            var serviceImplementation = assembly.GetType("WildwoodComponents.Blazor.Services.PaymentProviderService");

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);
                    
                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    var platformService = serviceProvider.GetRequiredService<WildwoodComponents.Blazor.Services.IPlatformDetectionService>();
                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.PaymentProviderService>()
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.PaymentProviderService>.Instance;
                    
                    var paymentProviderService = new WildwoodComponents.Blazor.Services.PaymentProviderService(httpClient, platformService, logger);
                    
                    // CRITICAL: Set the API base URL from configuration
                    // Without this, the service uses the hardcoded localhost URL
                    var apiBaseUrl = options.BaseUrl?.TrimEnd('/') + "/api";
                    paymentProviderService.SetApiBaseUrl(apiBaseUrl);
                    
                    Console.WriteLine($"[ServiceCollectionExtensions] PaymentProviderService configured with API URL: {apiBaseUrl}");
                    
                    return paymentProviderService;
                });
            }
        }

        /// <summary>
        /// Registers the payment service with dynamic constructor resolution
        /// </summary>
        private static void RegisterPaymentService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var paymentServiceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.IPaymentService");
            var paymentServiceImpl = assembly.GetType("WildwoodComponents.Blazor.Services.PaymentService");

            if (paymentServiceInterface != null && paymentServiceImpl != null)
            {
                services.AddScoped(paymentServiceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);
                    
                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    var logger = serviceProvider.GetService<ILogger<object>>();
                    return Activator.CreateInstance(paymentServiceImpl, httpClient, logger)!;
                });
            }
        }

        /// <summary>
        /// Registers the subscription service with dynamic constructor resolution
        /// </summary>
        private static void RegisterSubscriptionService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var subscriptionServiceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.ISubscriptionService");
            var subscriptionServiceImpl = assembly.GetType("WildwoodComponents.Blazor.Services.SubscriptionService");

            if (subscriptionServiceInterface != null && subscriptionServiceImpl != null)
            {
                services.AddScoped(subscriptionServiceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);
                    
                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    var logger = serviceProvider.GetService<ILogger<object>>();
                    return Activator.CreateInstance(subscriptionServiceImpl, httpClient, logger)!;
                });
            }
        }

        /// <summary>
        /// Registers the payment script providers and loader for dynamic script injection
        /// </summary>
        private static void RegisterPaymentScriptServices(IServiceCollection services)
        {
            // Register individual script providers as singletons (script content is cached)
            services.AddSingleton<IPaymentScriptProvider, StripeScriptProvider>();
            services.AddSingleton<IPaymentScriptProvider, PayPalScriptProvider>();
            services.AddSingleton<IPaymentScriptProvider, ApplePayScriptProvider>();
            services.AddSingleton<IPaymentScriptProvider, GooglePayScriptProvider>();

            // Register the script loader as scoped (per-request/circuit for reference counting)
            services.AddScoped<PaymentScriptLoader>();
        }

        /// <summary>
        /// Registers additional services that may be needed
        /// </summary>
        private static void RegisterAdditionalServices(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            // Register secure messaging service if available
            try
            {
                RegisterSecureMessagingService(services, assembly, options);
            }
            catch
            {
                // Service may not exist, ignore
            }

            // Register AI chat service if available with special configuration
            try
            {
                RegisterAIService(services, assembly, options);
            }
            catch
            {
                // Service may not exist, ignore
            }

            // Register session manager for automatic token refresh and session monitoring
            try
            {
                RegisterSessionManager(services, options);
            }
            catch
            {
                // Session manager registration may fail if dependencies are missing, ignore
            }

            // Register notification service if available
            try
            {
                RegisterService(services, assembly,
                    "WildwoodComponents.Blazor.Services.INotificationService",
                    "WildwoodComponents.Blazor.Services.NotificationService");
            }
            catch
            {
                // Service may not exist, ignore
            }

            // Register Two-Factor settings service if available
            try
            {
                RegisterTwoFactorSettingsService(services, assembly, options);
            }
            catch
            {
                // Service may not exist, ignore
            }

            // Register AI Flow service if available
            try
            {
                RegisterAIFlowService(services, assembly, options);
            }
            catch
            {
                // Service may not exist, ignore
            }

            // Register Disclaimer service if available
            try
            {
                RegisterDisclaimerService(services, assembly, options);
            }
            catch
            {
                // Service may not exist, ignore
            }

            // Register App Tier component service if available
            try
            {
                RegisterAppTierComponentService(services, assembly, options);
            }
            catch
            {
                // Service may not exist, ignore
            }
        }

        /// <summary>
        /// Registers the secure messaging service with proper URL configuration
        /// </summary>
        private static void RegisterSecureMessagingService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var serviceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.ISecureMessagingService");
            var serviceImplementation = assembly.GetType("WildwoodComponents.Blazor.Services.SecureMessagingService");

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);
                    
                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    var localStorage = serviceProvider.GetRequiredService<WildwoodComponents.Blazor.Services.ILocalStorageService>();
                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.SecureMessagingService>()
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.SecureMessagingService>.Instance;

                    var messagingService = new WildwoodComponents.Blazor.Services.SecureMessagingService(httpClient, localStorage, logger);
                    
                    // CRITICAL: Set the API base URL from configuration
                    var apiBaseUrl = options.BaseUrl?.TrimEnd('/') + "/api";
                    messagingService.SetApiBaseUrl(apiBaseUrl);
                    
                    Console.WriteLine($"[ServiceCollectionExtensions] SecureMessagingService configured with API URL: {apiBaseUrl}");
                    
                    return messagingService;
                });
            }
        }

        /// <summary>
        /// Registers AI service with proper URL configuration
        /// </summary>
        private static void RegisterAIService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var serviceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.IAIService");
            var serviceImplementation = assembly.GetType("WildwoodComponents.Blazor.Services.AIService");

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);
                    httpClient.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds > 0 ? options.RequestTimeoutSeconds : 300);
                    
                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    var localStorage = serviceProvider.GetRequiredService<WildwoodComponents.Blazor.Services.ILocalStorageService>();
                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.AIService>()
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.AIService>.Instance;

                    var aiService = new WildwoodComponents.Blazor.Services.AIService(httpClient, localStorage, logger);
                    
                    // CRITICAL: Set the API base URL from configuration
                    var apiBaseUrl = options.BaseUrl?.TrimEnd('/') + "/api";
                    aiService.SetApiBaseUrl(apiBaseUrl);
                    
                    Console.WriteLine($"[ServiceCollectionExtensions] AIService configured with API URL: {apiBaseUrl}");

                    return aiService;
                });
            }
        }

        /// <summary>
        /// Registers Two-Factor settings service with proper URL configuration
        /// </summary>
        private static void RegisterTwoFactorSettingsService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var serviceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.ITwoFactorSettingsService");
            var serviceImplementation = assembly.GetType("WildwoodComponents.Blazor.Services.TwoFactorSettingsService");

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);

                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.TwoFactorSettingsService>()
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.TwoFactorSettingsService>.Instance;

                    var twoFactorService = new WildwoodComponents.Blazor.Services.TwoFactorSettingsService(httpClient, logger);

                    // CRITICAL: Set the API base URL from configuration
                    var apiBaseUrl = options.BaseUrl?.TrimEnd('/');
                    twoFactorService.SetApiBaseUrl(apiBaseUrl ?? "");

                    Console.WriteLine($"[ServiceCollectionExtensions] TwoFactorSettingsService configured with API URL: {apiBaseUrl}");

                    return twoFactorService;
                });
            }
        }

        /// <summary>
        /// Registers AI Flow service with proper URL configuration
        /// </summary>
        private static void RegisterAIFlowService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var serviceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.IAIFlowService");
            var serviceImplementation = assembly.GetType("WildwoodComponents.Blazor.Services.AIFlowService");

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);

                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.AIFlowService>()
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.AIFlowService>.Instance;

                    var flowService = new WildwoodComponents.Blazor.Services.AIFlowService(httpClient, logger);

                    var apiBaseUrl = options.BaseUrl?.TrimEnd('/') + "/api";
                    flowService.SetApiBaseUrl(apiBaseUrl);

                    return flowService;
                });
            }
        }

        /// <summary>
        /// Registers the disclaimer service with proper URL configuration
        /// </summary>
        private static void RegisterDisclaimerService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var serviceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.IDisclaimerService");
            var serviceImplementation = assembly.GetType("WildwoodComponents.Blazor.Services.DisclaimerService");

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);

                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.DisclaimerService>()
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.DisclaimerService>.Instance;

                    return new WildwoodComponents.Blazor.Services.DisclaimerService(httpClient, logger);
                });
            }
        }

        /// <summary>
        /// Registers the App Tier component service with proper URL configuration
        /// </summary>
        private static void RegisterAppTierComponentService(IServiceCollection services, Assembly assembly, WildwoodComponentsOptions options)
        {
            var serviceInterface = assembly.GetType("WildwoodComponents.Blazor.Services.IAppTierComponentService");
            var serviceImplementation = assembly.GetType("WildwoodComponents.Blazor.Services.AppTierComponentService");

            if (serviceInterface != null && serviceImplementation != null)
            {
                services.AddScoped(serviceInterface, serviceProvider =>
                {
                    var httpClient = serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    httpClient.BaseAddress = new Uri(options.BaseUrl);

                    if (!string.IsNullOrEmpty(options.ApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    }

                    var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.AppTierComponentService>()
                                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.AppTierComponentService>.Instance;

                    var appTierService = new WildwoodComponents.Blazor.Services.AppTierComponentService(httpClient, logger);

                    var apiBaseUrl = options.BaseUrl?.TrimEnd('/') + "/api";
                    appTierService.SetApiBaseUrl(apiBaseUrl);

                    return appTierService;
                });
            }
        }

        /// <summary>
        /// Registers the WildwoodSessionManager for automatic token refresh and session monitoring.
        /// IAIService is optional - if not registered, reactive 401 refresh and AI token sync are skipped.
        /// </summary>
        private static void RegisterSessionManager(IServiceCollection services, WildwoodComponentsOptions options)
        {
            services.AddScoped<WildwoodComponents.Blazor.Services.IWildwoodSessionManager>(serviceProvider =>
            {
                var authService = serviceProvider.GetRequiredService<WildwoodComponents.Blazor.Services.IAuthenticationService>();
                var aiService = serviceProvider.GetService<WildwoodComponents.Blazor.Services.IAIService>();
                var localStorage = serviceProvider.GetRequiredService<WildwoodComponents.Blazor.Services.ILocalStorageService>();
                var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger<WildwoodComponents.Blazor.Services.WildwoodSessionManager>()
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildwoodComponents.Blazor.Services.WildwoodSessionManager>.Instance;

                return new WildwoodComponents.Blazor.Services.WildwoodSessionManager(
                    authService, aiService, localStorage, logger, options);
            });
        }
    }

    /// <summary>
    /// Configuration options for WildwoodComponents
    /// </summary>
    public class WildwoodComponentsOptions
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

        /// <summary>
        /// Enable automatic retry on failed requests
        /// </summary>
        public bool EnableRetry { get; set; } = true;

        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Enable response caching
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Cache duration in minutes
        /// </summary>
        public int CacheDurationMinutes { get; set; } = 10;

        /// <summary>
        /// Session duration in minutes. Determines how long the session stays alive via refresh tokens.
        /// Examples: 30 (30 minutes), 60 (1 hour), 1440 (1 day), 43200 (30 days).
        /// Default: 60 (1 hour).
        /// </summary>
        public int SessionExpirationMinutes { get; set; } = 60;

        /// <summary>
        /// Whether to automatically attempt token refresh when a 401/403 is detected.
        /// Default: false. Enable this to keep sessions alive beyond the JWT expiration
        /// by using refresh tokens to obtain new JWTs transparently.
        /// </summary>
        public bool EnableAutoTokenRefresh { get; set; } = false;

        /// <summary>
        /// Whether to use sliding expiration for the session. When true, the session expiry
        /// is automatically extended on auth events (token refresh, login). Consumers can also
        /// call TouchSessionAsync() on user activity to keep the session alive.
        /// Default: true.
        /// </summary>
        public bool SlidingExpiration { get; set; } = true;

        /// <summary>
        /// Whether authentication sessions should be persistent (survive browser close).
        /// Controls the IsPersistent flag on authentication cookies in server-side hosts.
        /// Default: false.
        /// </summary>
        public bool PersistentSession { get; set; } = false;

        /// <summary>
        /// Whether to require antiforgery token validation on authentication form submissions.
        /// Server-side hosts should validate antiforgery tokens on the cookie sign-in endpoint.
        /// Default: true.
        /// </summary>
        public bool EnableAntiforgeryValidation { get; set; } = true;

        /// <summary>
        /// Application version string sent with auth requests.
        /// Default: "1.0.0"
        /// </summary>
        public string AppVersion { get; set; } = "1.0.0";
    }
}
