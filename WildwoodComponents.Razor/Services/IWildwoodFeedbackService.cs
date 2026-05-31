using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Server-side service for the feedback widget. Talks to the Wildwood feedback API endpoints to
/// load the widget configuration, submit feedback, check for duplicates, and upvote existing
/// feedback. The Razor Pages sibling of the Blazor IFeedbackService.
///
/// Unlike the Blazor service, the base URL comes from the named "WildwoodAPI" HttpClient and the
/// Bearer token comes from the server-side session (<see cref="IWildwoodSessionManager"/>), so the
/// token is never exposed to the browser. The client JavaScript calls a thin proxy that the
/// consuming app maps onto these methods.
/// </summary>
public interface IWildwoodFeedbackService
{
    /// <summary>
    /// Loads the anonymous widget configuration for the given app. Returns null if unavailable.
    /// The result is briefly cached; call <see cref="InvalidateWidgetConfig"/> after changing the
    /// app's feedback configuration so the change takes effect immediately.
    /// </summary>
    Task<FeedbackWidgetConfig?> GetWidgetConfigAsync(string appId);

    /// <summary>
    /// Evicts the cached widget configuration for an app so the next <see cref="GetWidgetConfigAsync"/>
    /// re-fetches it. Call this right after saving the app's feedback configuration so toggles like
    /// "enable widget" are honored on the very next render instead of waiting for the cache to expire.
    /// </summary>
    void InvalidateWidgetConfig(string appId);

    /// <summary>
    /// Submits feedback. Returns a result indicating success, rate limiting, or an error message.
    /// </summary>
    Task<FeedbackSubmissionResult> SubmitFeedbackAsync(FeedbackSubmissionRequest request);

    /// <summary>
    /// Checks whether feedback with a similar title already exists for the app.
    /// </summary>
    Task<FeedbackDuplicateCheckResult> CheckDuplicateAsync(string title, string appId);

    /// <summary>
    /// Upvotes an existing piece of feedback by id.
    /// </summary>
    Task<FeedbackVoteResult> VoteAsync(string feedbackId);
}
