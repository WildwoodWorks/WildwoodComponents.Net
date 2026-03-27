using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Implementation of platform detection service that works across
    /// MAUI (using DeviceInfo) and web (using JavaScript).
    /// </summary>
    public class PlatformDetectionService : IPlatformDetectionService
    {
        private readonly IJSRuntime? _jsRuntime;
        private readonly ILogger<PlatformDetectionService> _logger;
        private readonly bool _isBlazorServer;
        private PlatformInfo? _cachedPlatformInfo;
        private RuntimePlatform? _cachedPlatform;

        // Payment provider type constants (matching PaymentProviderType enum)
        private const int ProviderTypeAppleAppStore = 10;
        private const int ProviderTypeGooglePlayStore = 11;
        private const int ProviderTypeApplePay = 20;
        private const int ProviderTypeGooglePay = 21;

        // Platform flags (matching PaymentPlatform enum)
        private const int PlatformFlagWeb = 1;
        private const int PlatformFlagIOS = 2;
        private const int PlatformFlagMacOS = 4;
        private const int PlatformFlagAndroid = 8;
        private const int PlatformFlagWindows = 16;

        public PlatformDetectionService(ILogger<PlatformDetectionService> logger, IJSRuntime? jsRuntime = null)
        {
            _logger = logger;
            _jsRuntime = jsRuntime;
            
            // Determine if we're running in Blazor Server context
            // Blazor Server uses a specific IJSRuntime implementation that runs on server
            // but should be treated as Web platform since the UI is in a browser
            _isBlazorServer = IsBlazorServerContext(jsRuntime);
            
            _logger.LogDebug("PlatformDetectionService initialized. JSRuntime: {HasJSRuntime}, IsBlazorServer: {IsBlazorServer}", 
                jsRuntime != null, _isBlazorServer);
        }

        /// <summary>
        /// Determines if we're running in a Blazor Server context.
        /// Blazor Server renders on the server but the UI is displayed in a browser,
        /// so it should be treated as Web platform for payment provider purposes.
        /// </summary>
        private static bool IsBlazorServerContext(IJSRuntime? jsRuntime)
        {
            if (jsRuntime == null)
                return false;
            
            // Check the type name to determine if this is Blazor Server
            // Blazor Server uses RemoteJSRuntime, while MAUI uses WebViewJSRuntime
            var typeName = jsRuntime.GetType().FullName ?? string.Empty;
            
            // RemoteJSRuntime = Blazor Server (server-side, but UI in browser)
            // JSRuntime from Microsoft.JSInterop = Blazor WebAssembly or Server
            // WebViewJSRuntime = MAUI Blazor Hybrid
            
            return typeName.Contains("Remote") || 
                   typeName.Contains("Server") ||
                   // If we have JSRuntime but NOT in a native platform, assume web
                   (!OperatingSystem.IsIOS() && 
                    !OperatingSystem.IsAndroid() && 
                    !OperatingSystem.IsMacCatalyst() &&
                    // Check if we're NOT in a MAUI WebView context
                    !typeName.Contains("WebView"));
        }

        public RuntimePlatform CurrentPlatform
        {
            get
            {
                if (_cachedPlatform.HasValue)
                    return _cachedPlatform.Value;

                _cachedPlatform = DetectPlatform();
                _logger.LogInformation("Platform detected: {Platform}", _cachedPlatform.Value);
                return _cachedPlatform.Value;
            }
        }

        public bool RequiresAppStorePayment
        {
            get
            {
                var info = GetPlatformInfo();
                
                // Only require app store payment if distributed through app stores
                if (info.DistributionSource == AppDistributionSource.AppleAppStore)
                    return true;
                if (info.DistributionSource == AppDistributionSource.GooglePlayStore)
                    return true;
                    
                return false;
            }
        }

        public int? RequiredAppStoreProviderType
        {
            get
            {
                var info = GetPlatformInfo();
                
                if (info.DistributionSource == AppDistributionSource.AppleAppStore)
                    return ProviderTypeAppleAppStore;
                if (info.DistributionSource == AppDistributionSource.GooglePlayStore)
                    return ProviderTypeGooglePlayStore;
                    
                return null;
            }
        }

        public bool IsDistributedApp
        {
            get
            {
                var info = GetPlatformInfo();
                return info.DistributionSource == AppDistributionSource.AppleAppStore ||
                       info.DistributionSource == AppDistributionSource.GooglePlayStore ||
                       info.DistributionSource == AppDistributionSource.MicrosoftStore ||
                       info.DistributionSource == AppDistributionSource.MacAppStore;
            }
        }

        public PlatformInfo GetPlatformInfo()
        {
            if (_cachedPlatformInfo != null)
                return _cachedPlatformInfo;

            _cachedPlatformInfo = BuildPlatformInfo();
            return _cachedPlatformInfo;
        }

        public bool IsProviderAvailable(int providerType)
        {
            var platform = CurrentPlatform;
            var info = GetPlatformInfo();

            // App store providers are platform-specific
            if (providerType == ProviderTypeAppleAppStore)
            {
                return platform == RuntimePlatform.iOS || platform == RuntimePlatform.macOS;
            }
            
            if (providerType == ProviderTypeGooglePlayStore)
            {
                return platform == RuntimePlatform.Android;
            }

            // Apple Pay availability
            if (providerType == ProviderTypeApplePay)
            {
                return info.SupportsApplePay;
            }

            // Google Pay availability
            if (providerType == ProviderTypeGooglePay)
            {
                return info.SupportsGooglePay;
            }

            // If app store exclusive mode, only allow the required provider
            if (RequiresAppStorePayment)
            {
                var required = RequiredAppStoreProviderType;
                if (required.HasValue && providerType != required.Value)
                {
                    return false;
                }
            }

            // Traditional payment processors available on all platforms
            return true;
        }

        public int GetPlatformFlags()
        {
            return CurrentPlatform switch
            {
                RuntimePlatform.Web => PlatformFlagWeb,
                RuntimePlatform.iOS => PlatformFlagIOS,
                RuntimePlatform.macOS => PlatformFlagMacOS,
                RuntimePlatform.Android => PlatformFlagAndroid,
                RuntimePlatform.Windows => PlatformFlagWindows,
                _ => PlatformFlagWeb // Default to web
            };
        }

        public async Task<bool> IsApplePayAvailableAsync()
        {
            try
            {
                var platform = CurrentPlatform;
                
                if (platform != RuntimePlatform.iOS && 
                    platform != RuntimePlatform.macOS && 
                    platform != RuntimePlatform.Web)
                {
                    return false;
                }

                if (platform == RuntimePlatform.Web && _jsRuntime != null)
                {
                    try
                    {
                        return await _jsRuntime.InvokeAsync<bool>("wildwoodPayment.isApplePayAvailable");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Apple Pay availability check failed via JS");
                        return false;
                    }
                }

                return platform == RuntimePlatform.iOS || platform == RuntimePlatform.macOS;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking Apple Pay availability");
                return false;
            }
        }

        public async Task<bool> IsGooglePayAvailableAsync()
        {
            try
            {
                var platform = CurrentPlatform;
                
                if (platform != RuntimePlatform.Android && platform != RuntimePlatform.Web)
                {
                    return false;
                }

                if (platform == RuntimePlatform.Web && _jsRuntime != null)
                {
                    try
                    {
                        return await _jsRuntime.InvokeAsync<bool>("wildwoodPayment.isGooglePayAvailable");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Google Pay availability check failed via JS");
                        return false;
                    }
                }

                return platform == RuntimePlatform.Android;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking Google Pay availability");
                return false;
            }
        }

        private RuntimePlatform DetectPlatform()
        {
            try
            {
                // IMPORTANT: Check for Blazor Server/Web context FIRST
                // Blazor Server runs on the server (which could be Windows/Linux/macOS)
                // but the UI is in a browser, so we should treat it as Web platform
                if (_isBlazorServer)
                {
                    _logger.LogDebug("Detected Blazor Server context - treating as Web platform");
                    return RuntimePlatform.Web;
                }
                
                // Check for native platforms (MAUI)
                // These checks are only accurate for MAUI apps, not Blazor Server
                if (OperatingSystem.IsIOS())
                    return RuntimePlatform.iOS;
                    
                if (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS())
                    return RuntimePlatform.macOS;
                    
                if (OperatingSystem.IsAndroid())
                    return RuntimePlatform.Android;
                    
                // For Windows, check if we have JSRuntime (indicating browser context)
                if (OperatingSystem.IsWindows())
                {
                    // If we have JSRuntime, we're likely in a browser context (Blazor WebAssembly)
                    // or Blazor Server (handled above). Without JSRuntime on Windows = native app
                    if (_jsRuntime != null)
                    {
                        _logger.LogDebug("Windows with JSRuntime - treating as Web platform");
                        return RuntimePlatform.Web;
                    }
                    return RuntimePlatform.Windows;
                }
                    
                if (OperatingSystem.IsLinux())
                {
                    // Similar logic for Linux servers running Blazor
                    if (_jsRuntime != null)
                    {
                        _logger.LogDebug("Linux with JSRuntime - treating as Web platform");
                        return RuntimePlatform.Web;
                    }
                    return RuntimePlatform.Linux;
                }

                // If we have JSRuntime but no specific OS detected, assume Web
                if (_jsRuntime != null)
                    return RuntimePlatform.Web;

                return RuntimePlatform.Unknown;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting platform");
                // Default to Web for safety - most payment providers work on Web
                return _jsRuntime != null ? RuntimePlatform.Web : RuntimePlatform.Unknown;
            }
        }

        private PlatformInfo BuildPlatformInfo()
        {
            var platform = CurrentPlatform;
            var info = new PlatformInfo
            {
                Platform = platform,
                OperatingSystem = GetOperatingSystemName(),
                OsVersion = Environment.OSVersion.VersionString
            };

            switch (platform)
            {
                case RuntimePlatform.iOS:
                    info.IsMobile = true;
                    info.SupportsApplePay = true;
                    info.DistributionSource = DetectIOSDistributionSource();
                    break;
                    
                case RuntimePlatform.macOS:
                    info.IsDesktop = true;
                    info.SupportsApplePay = true;
                    info.DistributionSource = DetectMacOSDistributionSource();
                    break;
                    
                case RuntimePlatform.Android:
                    info.IsMobile = true;
                    info.SupportsGooglePay = true;
                    info.DistributionSource = DetectAndroidDistributionSource();
                    break;
                    
                case RuntimePlatform.Windows:
                    info.IsDesktop = true;
                    info.DistributionSource = DetectWindowsDistributionSource();
                    break;
                    
                case RuntimePlatform.Web:
                    info.DistributionSource = AppDistributionSource.WebBrowser;
                    info.SupportsApplePay = true; // Will be verified via JS
                    info.SupportsGooglePay = true; // Will be verified via JS
                    break;
                    
                default:
                    info.DistributionSource = AppDistributionSource.Unknown;
                    break;
            }

            return info;
        }

        private string GetOperatingSystemName()
        {
            // For web apps, report the platform as Web, not the server OS
            if (CurrentPlatform == RuntimePlatform.Web)
                return "Web Browser";
                
            if (OperatingSystem.IsIOS()) return "iOS";
            if (OperatingSystem.IsMacCatalyst()) return "macOS Catalyst";
            if (OperatingSystem.IsMacOS()) return "macOS";
            if (OperatingSystem.IsAndroid()) return "Android";
            if (OperatingSystem.IsWindows()) return "Windows";
            if (OperatingSystem.IsLinux()) return "Linux";
            return "Unknown";
        }

        private AppDistributionSource DetectIOSDistributionSource()
        {
            // iOS apps distributed outside the App Store have no receipt file.
            // On real devices the presence of the embedded receipt is a reliable indicator.
            try
            {
                var receiptPath = Path.Combine(AppContext.BaseDirectory, "StoreKit", "receipt");
                if (File.Exists(receiptPath))
                    return AppDistributionSource.AppleAppStore;
            }
            catch { /* sandbox may block file access */ }

#if DEBUG
            return AppDistributionSource.Development;
#else
            return AppDistributionSource.AppleAppStore;
#endif
        }

        private AppDistributionSource DetectMacOSDistributionSource()
        {
            try
            {
                var receiptPath = Path.Combine(AppContext.BaseDirectory, "..", "Resources", "receipt");
                if (File.Exists(receiptPath))
                    return AppDistributionSource.MacAppStore;
            }
            catch { /* sandbox may block file access */ }

#if DEBUG
            return AppDistributionSource.Development;
#else
            return AppDistributionSource.Sideloaded;
#endif
        }

        private AppDistributionSource DetectAndroidDistributionSource()
        {
            // Check the installer package name when available (set by app stores).
            try
            {
                var installer = Environment.GetEnvironmentVariable("INSTALLER_PACKAGE_NAME");
                if (!string.IsNullOrEmpty(installer))
                {
                    if (installer.Contains("vending") || installer.Contains("google"))
                        return AppDistributionSource.GooglePlayStore;
                }
            }
            catch { /* env var may not be available */ }

#if DEBUG
            return AppDistributionSource.Development;
#else
            return AppDistributionSource.GooglePlayStore;
#endif
        }

        private AppDistributionSource DetectWindowsDistributionSource()
        {
            // MSIX-packaged apps from the Microsoft Store have a package identity.
            try
            {
                var packageId = Environment.GetEnvironmentVariable("PACKAGE_FAMILY_NAME");
                if (!string.IsNullOrEmpty(packageId))
                    return AppDistributionSource.MicrosoftStore;
            }
            catch { /* env var may not be available */ }

#if DEBUG
            return AppDistributionSource.Development;
#else
            return AppDistributionSource.Sideloaded;
#endif
        }
    }
}
