using System.Text.Json.Serialization;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the <see cref="WildwoodComponents.Razor.Components.Feedback.FeedbackWidgetViewComponent"/>.
/// Carries the server-resolved widget configuration plus the proxy base URL the client JavaScript
/// uses for its AJAX calls. Razor Pages equivalent of the Blazor FeedbackWidgetComponent state.
/// </summary>
public class FeedbackWidgetViewModel
{
    /// <summary>The app the feedback belongs to (required).</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the server-side feedback proxy endpoints (default: /api/wildwood-feedback).
    /// The client JavaScript POSTs/GETs against this; the proxy forwards to the Wildwood API
    /// with the server-side session token applied. Mirrors the proxy pattern used by the other
    /// Razor ViewComponents so the Bearer token never reaches the browser.
    /// </summary>
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-feedback";

    /// <summary>The widget configuration loaded server-side, or a safe default when unavailable.</summary>
    public FeedbackWidgetConfig Config { get; set; } = new();

    /// <summary>Whether the current server-side session has an authenticated user (hides email/name fields).</summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>The resolved list of feedback type codes for the type dropdown.</summary>
    public List<string> FeedbackTypes { get; set; } = new();

    /// <summary>A short unique id so multiple widgets/instances do not collide on element ids.</summary>
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

/// <summary>
/// Widget configuration returned by the anonymous feedback widget config endpoint
/// (<c>GET /api/AppComponentConfigurations/{appId}/feedback/widget</c>). Drives the
/// behavior and appearance of the feedback widget.
/// </summary>
public class FeedbackWidgetConfig
{
    /// <summary>Whether the feedback widget is enabled for the app. When false the widget renders nothing.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether anonymous (unauthenticated) submissions are permitted.</summary>
    public bool AllowAnonymous { get; set; } = true;

    /// <summary>The list of feedback type codes to populate the type dropdown (e.g. "Bug", "FeatureRequest").</summary>
    public List<string> FeedbackTypes { get; set; } = new();

    /// <summary>The accent color for the widget button and panel header (CSS color string).</summary>
    public string? WidgetColor { get; set; }

    /// <summary>The position of the floating button: "bottom-right" (default) or "bottom-left".</summary>
    public string? WidgetPosition { get; set; }

    /// <summary>Whether a screenshot is required before the feedback can be submitted.</summary>
    public bool RequireScreenshot { get; set; }

    /// <summary>The maximum screenshot size in kilobytes. 0 means no limit.</summary>
    public int ScreenshotMaxSizeKb { get; set; } = 500;

    /// <summary>The JPEG quality (0-100) used when compressing captured screenshots.</summary>
    public int ScreenshotQuality { get; set; } = 80;

    /// <summary>Whether the widget should check for potential duplicates as the user types a title.</summary>
    public bool EnableDuplicateDetection { get; set; } = true;

    /// <summary>Whether file attachments are allowed.</summary>
    public bool AllowAttachments { get; set; }

    /// <summary>The maximum size per attachment in kilobytes.</summary>
    public int MaxAttachmentSizeKb { get; set; } = 2048;

    /// <summary>A comma-separated list of allowed attachment extensions (e.g. ".png,.jpg,.log").</summary>
    public string? AllowedAttachmentTypes { get; set; }
}

/// <summary>
/// Request body for submitting feedback (<c>POST /api/SystemFeedback</c>). Also the shape the
/// client JavaScript posts to the feedback proxy.
/// </summary>
public class FeedbackSubmissionRequest
{
    public string? AppId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FeedbackType { get; set; } = string.Empty;
    public string? PageUrl { get; set; }

    /// <summary>A base64 data URL of the screenshot, or null when none was captured.</summary>
    public string? ScreenshotData { get; set; }

    /// <summary>A JSON-serialized array of attachments, or null when there are none.</summary>
    public string? Attachments { get; set; }

    /// <summary>A JSON-serialized browser context blob (console log, environment, performance), or null.</summary>
    public string? BrowserContext { get; set; }

    public string? SubmitterEmail { get; set; }
    public string? SubmitterName { get; set; }
}

/// <summary>
/// Result of a feedback submission attempt.
/// </summary>
public class FeedbackSubmissionResult
{
    /// <summary>Whether the submission succeeded (HTTP 201).</summary>
    public bool Success { get; set; }

    /// <summary>Whether the submission was rejected due to rate limiting (HTTP 429).</summary>
    public bool RateLimited { get; set; }

    /// <summary>An error message to display when the submission failed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of a duplicate-detection check (<c>GET /api/SystemFeedback/duplicate-check</c>).
/// </summary>
public class FeedbackDuplicateCheckResult
{
    public bool HasPotentialDuplicate { get; set; }
    public string? DuplicateTitle { get; set; }
    public string? DuplicateId { get; set; }
    public int DuplicateVoteCount { get; set; }
    public DateTime? DuplicateCreatedAt { get; set; }
}

/// <summary>
/// Result of upvoting an existing piece of feedback (<c>POST /api/SystemFeedback/{id}/vote</c>).
/// </summary>
public class FeedbackVoteResult
{
    /// <summary>Whether the vote was recorded.</summary>
    public bool Success { get; set; }

    /// <summary>The new total vote count after the vote.</summary>
    public int VoteCount { get; set; }

    /// <summary>An error message to display when the vote failed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Browser context captured by the companion JS and forwarded with a submission.
/// Serialized to JSON for the <see cref="FeedbackSubmissionRequest.BrowserContext"/> field.
/// </summary>
public class FeedbackBrowserContext
{
    [JsonPropertyName("consoleLog")]
    public List<FeedbackConsoleEntry> ConsoleLog { get; set; } = new();
}

/// <summary>
/// A single captured console/error entry.
/// </summary>
public class FeedbackConsoleEntry
{
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;
}
