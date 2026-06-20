namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the ConsentBanner ViewComponent. Carries the app id, the API host root the
/// client-side engine calls (direct mode), an optional same-origin proxy base (proxy mode), and the
/// footer-link toggles. The full consent config (including the third-party script registry) is
/// fetched client-side by consent.js — mirroring the Blazor ConsentBanner, whose JS-isolation engine
/// owns cookie/GPC/decision/injection.
/// </summary>
public class ConsentViewModel
{
    /// <summary>The app whose consent config + script registry to load.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>API host root (no <c>/api</c> suffix); consent.js appends <c>/api/consent/...</c> in direct mode.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional same-origin proxy base (e.g. <c>/api/wildwood-consent</c>). When set, consent.js calls
    /// <c>{proxy}/config</c> and <c>{proxy}/record</c> instead of the API directly, avoiding cross-origin
    /// CORS. Empty = direct mode against <see cref="BaseUrl"/>.
    /// </summary>
    public string ProxyBaseUrl { get; set; } = string.Empty;

    /// <summary>Render a footer "Privacy choices" link so the visitor can reopen preferences.</summary>
    public bool ShowReopenLink { get; set; } = true;

    /// <summary>Render standalone CCPA opt-out footer links once the banner is dismissed.</summary>
    public bool ShowFooterOptOut { get; set; } = true;

    /// <summary>Short unique id so multiple instances on one page don't collide.</summary>
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}
