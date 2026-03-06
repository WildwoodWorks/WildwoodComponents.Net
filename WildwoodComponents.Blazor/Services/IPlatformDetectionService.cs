using System;
using System.Threading.Tasks;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Service for detecting the current runtime platform and determining
    /// platform-specific payment requirements.
    /// </summary>
    public interface IPlatformDetectionService
    {
        /// <summary>
        /// Gets the current runtime platform.
        /// </summary>
        RuntimePlatform CurrentPlatform { get; }

        /// <summary>
        /// Gets detailed platform information.
        /// </summary>
        PlatformInfo GetPlatformInfo();

        /// <summary>
        /// Determines if the current platform requires app store payments for digital goods.
        /// iOS/macOS apps distributed via App Store must use Apple IAP.
        /// Android apps distributed via Play Store must use Google Play Billing.
        /// </summary>
        bool RequiresAppStorePayment { get; }

        /// <summary>
        /// Gets the required app store payment provider type for this platform, if any.
        /// Returns null if the platform doesn't require a specific payment provider.
        /// </summary>
        int? RequiredAppStoreProviderType { get; }

        /// <summary>
        /// Determines if a specific payment provider type is available on this platform.
        /// </summary>
        /// <param name="providerType">The payment provider type to check.</param>
        /// <returns>True if the provider can be used on this platform.</returns>
        bool IsProviderAvailable(int providerType);

        /// <summary>
        /// Gets the platform flags for the current platform (matches PaymentPlatform enum).
        /// </summary>
        int GetPlatformFlags();

        /// <summary>
        /// Determines if Apple Pay is available on this device/browser.
        /// </summary>
        Task<bool> IsApplePayAvailableAsync();

        /// <summary>
        /// Determines if Google Pay is available on this device/browser.
        /// </summary>
        Task<bool> IsGooglePayAvailableAsync();

        /// <summary>
        /// Determines if the app is running as a distributed app (App Store/Play Store)
        /// vs. development/sideloaded.
        /// </summary>
        bool IsDistributedApp { get; }
    }

    /// <summary>
    /// Runtime platform enumeration
    /// </summary>
    public enum RuntimePlatform
    {
        Unknown = 0,
        Web = 1,
        iOS = 2,
        macOS = 3,
        Android = 4,
        Windows = 5,
        Linux = 6
    }

    /// <summary>
    /// Detailed platform information
    /// </summary>
    public class PlatformInfo
    {
        /// <summary>
        /// The detected runtime platform
        /// </summary>
        public RuntimePlatform Platform { get; set; } = RuntimePlatform.Unknown;

        /// <summary>
        /// Operating system name
        /// </summary>
        public string OperatingSystem { get; set; } = string.Empty;

        /// <summary>
        /// Operating system version
        /// </summary>
        public string OsVersion { get; set; } = string.Empty;

        /// <summary>
        /// Device manufacturer (for mobile)
        /// </summary>
        public string? DeviceManufacturer { get; set; }

        /// <summary>
        /// Device model (for mobile)
        /// </summary>
        public string? DeviceModel { get; set; }

        /// <summary>
        /// Browser name (for web)
        /// </summary>
        public string? BrowserName { get; set; }

        /// <summary>
        /// Browser version (for web)
        /// </summary>
        public string? BrowserVersion { get; set; }

        /// <summary>
        /// User agent string (for web)
        /// </summary>
        public string? UserAgent { get; set; }

        /// <summary>
        /// Whether the app is running in a WebView
        /// </summary>
        public bool IsWebView { get; set; }

        /// <summary>
        /// Whether the app is running as a Progressive Web App
        /// </summary>
        public bool IsPwa { get; set; }

        /// <summary>
        /// Whether this is a mobile device
        /// </summary>
        public bool IsMobile { get; set; }

        /// <summary>
        /// Whether this is a tablet device
        /// </summary>
        public bool IsTablet { get; set; }

        /// <summary>
        /// Whether this is a desktop/laptop
        /// </summary>
        public bool IsDesktop { get; set; }

        /// <summary>
        /// App distribution source
        /// </summary>
        public AppDistributionSource DistributionSource { get; set; } = AppDistributionSource.Unknown;

        /// <summary>
        /// Whether Apple Pay is likely available (based on platform detection)
        /// </summary>
        public bool SupportsApplePay { get; set; }

        /// <summary>
        /// Whether Google Pay is likely available (based on platform detection)
        /// </summary>
        public bool SupportsGooglePay { get; set; }
    }

    /// <summary>
    /// How the app was distributed/installed
    /// </summary>
    public enum AppDistributionSource
    {
        Unknown = 0,
        AppleAppStore = 1,
        GooglePlayStore = 2,
        MicrosoftStore = 3,
        MacAppStore = 4,
        Sideloaded = 5,
        WebBrowser = 6,
        Development = 7
    }
}
