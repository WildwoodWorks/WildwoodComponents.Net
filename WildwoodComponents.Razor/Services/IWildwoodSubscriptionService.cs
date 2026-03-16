using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Subscription service interface for WildwoodComponents.Razor.
/// Razor Pages equivalent of WildwoodComponents.Blazor's ISubscriptionService.
/// Uses server-side session for JWT token management via IWildwoodSessionManager.
/// </summary>
public interface IWildwoodSubscriptionService
{
    /// <summary>
    /// Gets available subscription plans
    /// </summary>
    Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync();

    /// <summary>
    /// Gets the current subscription for the authenticated user (or a specific user)
    /// </summary>
    Task<SubscriptionDto?> GetCurrentSubscriptionAsync(string? userId = null);

    /// <summary>
    /// Subscribes the user to a plan
    /// </summary>
    Task<ApiResult> SubscribeToPlanAsync(string planId, string? userId = null);

    /// <summary>
    /// Cancels a subscription
    /// </summary>
    Task<ApiResult> CancelSubscriptionAsync(string subscriptionId);

    /// <summary>
    /// Pauses a subscription
    /// </summary>
    Task<ApiResult> PauseSubscriptionAsync(string subscriptionId);

    /// <summary>
    /// Resumes a paused subscription
    /// </summary>
    Task<ApiResult> ResumeSubscriptionAsync(string subscriptionId);

    /// <summary>
    /// Upgrades a subscription to a new plan
    /// </summary>
    Task<ApiResult> UpgradeSubscriptionAsync(string subscriptionId, string newPlanId);

    /// <summary>
    /// Gets invoices for the user or a specific subscription
    /// </summary>
    Task<List<InvoiceDto>> GetInvoicesAsync(string? subscriptionId = null);
}
