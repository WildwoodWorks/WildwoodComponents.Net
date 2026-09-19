using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Registration service for communicating with WildwoodAPI registration endpoints.
/// Razor Pages equivalent of the Blazor TokenRegistrationComponent's direct HTTP calls.
/// </summary>
public interface IWildwoodRegistrationService
{
    /// <summary>
    /// Validate a registration token
    /// </summary>
    Task<TokenValidationResponse?> ValidateTokenAsync(string token);

    /// <summary>
    /// What a registration token grants (tier, pricing, packs and features per app), from
    /// <c>GET api/registrationtokens/validate-detailed/{token}</c> with an optional <c>?appId=</c>
    /// scope. Returns <c>null</c> when the details cannot be READ — an older server without the
    /// route, or a transport failure — which is NOT the same as an invalid token: callers fall
    /// back to <see cref="ValidateTokenAsync"/> instead of telling the registrant their token is
    /// bad. An invalid token comes back as details with <c>IsValid=false</c> and the server's
    /// message.
    /// </summary>
    Task<RegistrationTokenDetails?> GetRegistrationTokenDetailsAsync(string token, string? appId = null);

    /// <summary>
    /// Validate registration data before creating the user (pre-payment check)
    /// </summary>
    Task<RegistrationValidationResponse?> ValidateRegistrationAsync(ValidateRegistrationRequest request);

    /// <summary>
    /// Register a new user with a token
    /// </summary>
    Task<RegistrationSuccessResponse?> RegisterWithTokenAsync(TokenRegistrationRequest request);

    /// <summary>
    /// Register a new user without a token (open registration)
    /// </summary>
    Task<RegistrationSuccessResponse?> RegisterAsync(OpenRegistrationRequest request);

    /// <summary>
    /// Get password requirements for an app
    /// </summary>
    Task<string?> GetPasswordRequirementsAsync(string appId);

    /// <summary>
    /// Get public pricing model details
    /// </summary>
    Task<PricingModelResponse?> GetPricingModelAsync(string pricingModelId);

    /// <summary>
    /// Get pricing details for a registration token
    /// </summary>
    Task<PricingDetails?> GetTokenPricingAsync(string token);

    /// <summary>
    /// Skip payment setup during registration
    /// </summary>
    Task<bool> SkipPaymentAsync(SkipPaymentRequest request);

    /// <summary>
    /// Link a payment transaction to a newly registered user
    /// </summary>
    Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null);

    /// <summary>
    /// Get pending disclaimers for registration
    /// </summary>
    Task<PendingDisclaimersResponse?> GetRegistrationDisclaimersAsync(string appId);

    /// <summary>
    /// Log in after registration (auto-login)
    /// </summary>
    Task<AuthResult> LoginAsync(string username, string email, string password, string? appId);
}
