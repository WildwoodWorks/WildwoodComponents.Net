using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Registration service implementation that calls WildwoodAPI registration endpoints.
/// Razor Pages equivalent of the Blazor TokenRegistrationComponent's direct HTTP calls.
/// </summary>
public class WildwoodRegistrationService : IWildwoodRegistrationService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodRegistrationService> _logger;
    private readonly string _appId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodRegistrationService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodRegistrationService> logger,
        string appId)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
        _appId = appId;
    }

    public async Task<TokenValidationResponse?> ValidateTokenAsync(string token)
    {
        var body = await ReadTokenValidationAsync(token, null, "Token validation");
        if (body is null) return null;

        try
        {
            return JsonSerializer.Deserialize<TokenValidationResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse the token validation response");
            return null;
        }
    }

    /// <summary>
    /// What a registration token grants (tier, pricing, packs and features per app), from the same
    /// <c>registrationtokens/validate-detailed/{token}</c> answer
    /// <see cref="ValidateTokenAsync"/> reads — this projection keeps the AppGrants, which the
    /// server-render model drops.
    ///
    /// Returns <c>null</c> when the details cannot be READ — an older server without the route, or
    /// a transport failure — which is NOT the same as an invalid token: callers fall back to the
    /// plain validity check instead of telling the registrant their token is bad. An invalid token
    /// comes back as details with <c>IsValid=false</c> and the server's message.
    /// </summary>
    public async Task<RegistrationTokenDetails?> GetRegistrationTokenDetailsAsync(string token, string? appId = null)
    {
        var body = await ReadTokenValidationAsync(token, appId, "Registration token details");
        if (body is null) return null;

        try
        {
            var details = JsonSerializer.Deserialize<RegistrationTokenDetails>(body, JsonOptions);
            if (details is null) return null;

            details.AppGrants ??= new List<RegistrationTokenAppGrant>();
            return details;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse the registration token details");
            return null;
        }
    }

    /// <summary>
    /// One GET of the detailed token validation, returning the raw body or null when it could not
    /// be read. Both token readers share it so the route, the URL-encoding of the token (a token
    /// carrying a slash must stay ONE path segment) and the optional <c>?appId=</c> scope are
    /// written once.
    /// </summary>
    private async Task<string?> ReadTokenValidationAsync(string token, string? appId, string operation)
    {
        try
        {
            var encodedToken = Uri.EscapeDataString(token ?? string.Empty);
            var appQuery = string.IsNullOrEmpty(appId) ? string.Empty : $"?appId={Uri.EscapeDataString(appId!)}";
            using var response = await _httpClient.GetAsync($"registrationtokens/validate-detailed/{encodedToken}{appQuery}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Operation} failed with status {StatusCode}", operation, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading registration token validation ({Operation})", operation);
            return null;
        }
    }

    public async Task<RegistrationValidationResponse?> ValidateRegistrationAsync(ValidateRegistrationRequest request)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("userregistration/validate", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<RegistrationValidationResponse>(JsonOptions);
            }

            _logger.LogWarning("Registration validation failed with status {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating registration data");
            return null;
        }
    }

    public async Task<RegistrationSuccessResponse?> RegisterWithTokenAsync(TokenRegistrationRequest request)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("userregistration/register-with-token", request);
            var content = await response.Content.ReadAsStringAsync();

            try
            {
                return JsonSerializer.Deserialize<RegistrationSuccessResponse>(content, JsonOptions);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Failed to parse registration response: {Content}", content);
                return new RegistrationSuccessResponse
                {
                    Success = false,
                    Message = response.IsSuccessStatusCode
                        ? "Registration completed but response was unexpected."
                        : $"Registration failed: {response.StatusCode}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering with token");
            return new RegistrationSuccessResponse { Success = false, Message = "An error occurred during registration." };
        }
    }

    public async Task<RegistrationSuccessResponse?> RegisterAsync(OpenRegistrationRequest request)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("userregistration/register", request);
            var content = await response.Content.ReadAsStringAsync();

            try
            {
                return JsonSerializer.Deserialize<RegistrationSuccessResponse>(content, JsonOptions);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Failed to parse registration response: {Content}", content);
                return new RegistrationSuccessResponse
                {
                    Success = false,
                    Message = response.IsSuccessStatusCode
                        ? "Registration completed but response was unexpected."
                        : $"Registration failed: {response.StatusCode}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during open registration");
            return new RegistrationSuccessResponse { Success = false, Message = "An error occurred during registration." };
        }
    }

    public async Task<string?> GetPasswordRequirementsAsync(string appId)
    {
        try
        {
            // There is no dedicated password-requirements endpoint; derive the text from the
            // app's auth configuration, matching the Blazor and JS implementations.
            using var response = await _httpClient.GetAsync($"appcomponentconfigurations/{appId}/auth-configuration");
            if (response.IsSuccessStatusCode)
            {
                var config = await response.Content.ReadFromJsonAsync<PasswordPolicyConfiguration>(JsonOptions);
                if (config != null)
                {
                    return GetPasswordRequirementsText(config);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load password requirements");
        }
        return null;
    }

    /// <summary>Password policy fields of the auth-configuration response.</summary>
    private sealed class PasswordPolicyConfiguration
    {
        public int PasswordMinimumLength { get; set; } = 8;
        public bool PasswordRequireUppercase { get; set; }
        public bool PasswordRequireLowercase { get; set; }
        public bool PasswordRequireDigit { get; set; }
        public bool PasswordRequireSpecialChar { get; set; }
    }

    private static string GetPasswordRequirementsText(PasswordPolicyConfiguration config)
    {
        var requirements = new List<string>
        {
            $"at least {config.PasswordMinimumLength} characters"
        };

        if (config.PasswordRequireUppercase)
            requirements.Add("uppercase letters (A-Z)");
        if (config.PasswordRequireLowercase)
            requirements.Add("lowercase letters (a-z)");
        if (config.PasswordRequireDigit)
            requirements.Add("numbers (0-9)");
        if (config.PasswordRequireSpecialChar)
            requirements.Add("special characters (!@#$%^&*)");

        if (requirements.Count == 1)
            return $"Password must have {requirements[0]}.";

        if (requirements.Count == 2)
            return $"Password must have {requirements[0]} and {requirements[1]}.";

        var allButLast = string.Join(", ", requirements.Take(requirements.Count - 1));
        return $"Password must have {allButLast}, and {requirements[^1]}.";
    }

    public async Task<PricingModelResponse?> GetPricingModelAsync(string pricingModelId)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"pricingmodels/{pricingModelId}/public");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PricingModelResponse>(JsonOptions);
            }

            _logger.LogWarning("Failed to load pricing model {PricingModelId}. Status: {StatusCode}",
                pricingModelId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load pricing model {PricingModelId}", pricingModelId);
        }
        return null;
    }

    public async Task<PricingDetails?> GetTokenPricingAsync(string token)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"registrationpayment/pricing/{token}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PricingDetails>(JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load token pricing details");
        }
        return null;
    }

    public async Task<bool> SkipPaymentAsync(SkipPaymentRequest request)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("registrationpayment/skip", request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error skipping payment");
            return false;
        }
    }

    public async Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var request = new
            {
                ExternalTransactionId = externalTransactionId,
                UserId = userId,
                CompanyClientId = companyClientId
            };

            using var response = await _httpClient.PostAsJsonAsync("paymenttransactions/link-by-external-id", request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking transaction {TransactionId} to user {UserId}",
                externalTransactionId, userId);
            return false;
        }
    }

    public async Task<PendingDisclaimersResponse?> GetRegistrationDisclaimersAsync(string appId)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"disclaimeracceptance/pending/{appId}?showOn=registration");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PendingDisclaimersResponse>(JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load registration disclaimers");
        }
        return null;
    }

    public async Task<AuthResult> LoginAsync(string username, string email, string password, string? appId)
    {
        try
        {
            var loginRequest = new WildwoodLoginRequest
            {
                Username = username,
                Email = email,
                Password = password,
                AppId = appId ?? _appId
            };

            using var response = await _httpClient.PostAsJsonAsync("auth/login", loginRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var authResponse = JsonSerializer.Deserialize<WildwoodAuthenticateResponse>(content, JsonOptions);
                if (authResponse != null && !string.IsNullOrEmpty(authResponse.JwtToken))
                {
                    _sessionManager.SetTokens(authResponse.JwtToken, authResponse.RefreshToken);
                    return AuthResult.Success(AuthResponse.FromWildwoodResponse(authResponse));
                }
            }

            return AuthResult.Failure("Auto-login failed. Please log in manually.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-login failed for user {Email}", email);
            return AuthResult.Failure($"Auto-login failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<SignupRegistrationSettings?> GetSignupRegistrationSettingsAsync(string? appId = null)
    {
        var resolved = appId is { Length: > 0 } ? appId : _appId;
        if (!(resolved is { Length: > 0 })) return null;

        try
        {
            // Anonymous: a signup screen has no session yet, and this is the same route the
            // Blazor authentication service reads before the visitor has one. No bearer is put on
            // the shared client, so nothing leaks into the rest of the request's calls either.
            using var response = await _httpClient.GetAsync(
                $"AppComponentConfigurations/{Uri.EscapeDataString(resolved!)}/auth-configuration");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Auth configuration for app {AppId} returned {StatusCode}", resolved, response.StatusCode);
                return null;
            }

            var config = await response.Content.ReadFromJsonAsync<SignupAuthConfigurationSlice>(JsonOptions);
            if (config is null) return null;

            // Two flags out of a configuration with a dozen: the rest is an operator's business.
            return new SignupRegistrationSettings
            {
                AllowOpenRegistration = config.AllowOpenRegistration,
                AllowTokenRegistration = config.AllowTokenRegistration
            };
        }
        catch (Exception ex)
        {
            // Unreadable is not closed. The caller falls back to open sign-up with the optional
            // token card, which the server refuses with a clear message if it is not on.
            _logger.LogWarning(ex, "Could not read the auth configuration for app {AppId}", resolved);
            return null;
        }
    }

    /// <summary>
    /// The only two properties of WildwoodAPI's auth-configuration answer this package binds, so
    /// the rest cannot be relayed by accident.
    /// </summary>
    private sealed class SignupAuthConfigurationSlice
    {
        public bool AllowOpenRegistration { get; set; }

        public bool AllowTokenRegistration { get; set; }
    }
}
