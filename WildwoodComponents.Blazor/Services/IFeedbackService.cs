using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Service for the feedback widget. Talks to the Wildwood feedback API endpoints to
    /// load the widget configuration, submit feedback, check for duplicates, and upvote
    /// existing feedback. Modeled on the other component services (HttpClient + SetApiBaseUrl).
    /// </summary>
    public interface IFeedbackService
    {
        /// <summary>
        /// Sets the API base URL (e.g. <c>https://api.wildwoodworks.io/api/</c>). The service
        /// strips a trailing <c>/api</c> segment and re-adds <c>/api/...</c> per endpoint, matching
        /// the canonical vanilla widget behavior.
        /// </summary>
        void SetApiBaseUrl(string apiBaseUrl);

        /// <summary>
        /// Sets the optional Bearer token used to attribute submissions and votes to an authenticated user.
        /// </summary>
        void SetAuthToken(string? token);

        /// <summary>
        /// Loads the anonymous widget configuration for the given app.
        /// </summary>
        Task<FeedbackWidgetConfig?> GetWidgetConfigAsync(string appId);

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
}
