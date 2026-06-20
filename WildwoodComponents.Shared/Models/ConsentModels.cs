using System.Collections.Generic;

namespace WildwoodComponents.Shared.Models
{
    /// <summary>
    /// Result of the consent engine's initialize() call (deserialized from JS interop).
    /// </summary>
    public class ConsentInitResult
    {
        public ConsentConfigModel? Config { get; set; }
        public ConsentStateModel? State { get; set; }
        public bool ShouldShowBanner { get; set; }
    }

    /// <summary>
    /// The merged public consent config returned by GET /api/consent/config.
    /// </summary>
    public class ConsentConfigModel
    {
        public string AppId { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public int Version { get; set; }
        public GeoDecisionModel Geo { get; set; } = new();
        public bool HonorGpc { get; set; }
        public bool ShowDoNotSell { get; set; }
        public bool ShowLimitSensitive { get; set; }
        public string NonTargetDefault { get; set; } = "LoadAll";
        public List<string> Categories { get; set; } = new();
        public Dictionary<string, bool>? CategoryDefaults { get; set; }
        public ConsentAppearanceModel? Appearance { get; set; }
        public ConsentBannerTextModel? BannerText { get; set; }
        public string? PrivacyPolicyUrl { get; set; }
        public string? AccessibilityUrl { get; set; }
        public List<ConsentScriptModel> Scripts { get; set; } = new();
    }

    public class GeoDecisionModel
    {
        public bool Aware { get; set; }
        public bool InTarget { get; set; }
        public List<string> ResolvedTags { get; set; } = new();
    }

    public class ConsentAppearanceModel
    {
        public string? Position { get; set; }
        public string? Theme { get; set; }
    }

    public class ConsentBannerTextModel
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? AcceptAll { get; set; }
        public string? RejectAll { get; set; }
        public string? Manage { get; set; }
    }

    public class ConsentScriptModel
    {
        public string Id { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string InjectionMode { get; set; } = string.Empty;
    }

    /// <summary>
    /// The visitor's current consent state.
    /// </summary>
    public class ConsentStateModel
    {
        public string VisitorKey { get; set; } = string.Empty;
        public Dictionary<string, bool> Categories { get; set; } = new();
        public int ConfigVersion { get; set; }
        public bool Decided { get; set; }
        public bool GpcPresent { get; set; }
    }
}
