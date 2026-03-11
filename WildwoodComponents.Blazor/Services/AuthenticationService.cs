using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    // Enum to match the server AssignedRole enum
    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal enum AssignedRole
    {
        ClientAdmin = 1,
        User = 2
    }

    // Simple internal DTO to match the API response structure
    internal class RegistrationResponseDto
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        
        [JsonPropertyName("userId")]
        public string? UserId { get; set; }
        
        [JsonPropertyName("companyClientId")]
        public string? CompanyClientId { get; set; }
        
        [JsonPropertyName("assignedRole")]
        public AssignedRole? AssignedRole { get; set; }
        
        [JsonPropertyName("grantedApps")]
        public string[]? GrantedApps { get; set; }
        
        [JsonPropertyName("requiresStripeSetup")]
        public bool RequiresStripeSetup { get; set; }
        
        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }
    }

    /// <summary>
    /// Standard API error response format
    /// </summary>
    public class ApiErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }
    }

    public interface IAuthenticationService
    {
        Task<AuthenticationResponse> LoginAsync(LoginRequest request);
        Task<AuthenticationResponse> RegisterAsync(RegistrationRequest request);
        Task<AuthenticationResponse> RegisterWithTokenAsync(RegistrationRequest request);
        Task<AuthenticationResponse> AuthenticateWithProviderAsync(string providerName, string appId);
        Task<string?> GetOAuthAuthorizeUrlAsync(string providerName, string appId);
        Task<List<AuthProvider>> GetAvailableProvidersAsync();
        Task<List<AuthProvider>> GetAvailableProvidersAsync(string appId);
        Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync(string appId);
        Task<AuthenticationConfiguration?> GetAuthenticationConfigurationAsync(string appId);
        Task<bool> ValidateLicenseTokenAsync(string token);
        Task<(bool IsValid, string ErrorMessage)> ValidatePasswordAsync(string password, string appId);
        Task<string> GetPasswordRequirementsAsync(string appId);
        Task<bool> HasRegistrationTokensAsync(string appId);
        Task<bool> ValidateRegistrationTokenAsync(string token);
        Task<bool> ResetPasswordAsync(string newPassword, string confirmPassword, string appId);
        Task<bool> RequestPasswordResetAsync(string email, string appId);
        Task LogoutAsync();
        Task<bool> RefreshTokenAsync();
        event Action<AuthenticationResponse>? OnAuthenticationChanged;
        event Action? OnLogout;

        // Passkey/WebAuthn methods
        Task<object> GetPasskeyAuthenticationOptionsAsync(string appId);
        Task<AuthenticationResponse> VerifyPasskeyAuthenticationAsync(string appId, System.Text.Json.JsonElement credential);
        Task<object> GetPasskeyRegistrationOptionsAsync(string appId);
        Task CompletePasskeyRegistrationAsync(string appId, System.Text.Json.JsonElement credential);

        // Two-Factor Authentication methods
        /// <summary>
        /// Sends a 2FA verification code (for email-based 2FA)
        /// </summary>
        Task<TwoFactorSendCodeResponse> SendTwoFactorCodeAsync(string sessionId);

        /// <summary>
        /// Verifies a 2FA code and completes authentication
        /// </summary>
        Task<TwoFactorVerifyResponse> VerifyTwoFactorCodeAsync(TwoFactorVerifyRequest request);

        /// <summary>
        /// Verifies a 2FA recovery code
        /// </summary>
        Task<TwoFactorVerifyResponse> VerifyTwoFactorRecoveryCodeAsync(string sessionId, string recoveryCode, string ipAddress);
    }

    /// <summary>
    /// Response from the OAuth authorize URL endpoint.
    /// </summary>
    internal class OAuthAuthorizeResponse
    {
        [JsonPropertyName("authorizationUrl")]
        public string? AuthorizationUrl { get; set; }
    }

    public class AuthenticationService : IAuthenticationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage;
        private readonly ILogger<AuthenticationService> _logger;

        public event Action<AuthenticationResponse>? OnAuthenticationChanged;
        public event Action? OnLogout;

        public AuthenticationService(
            HttpClient httpClient,
            ILocalStorageService localStorage,
            ILogger<AuthenticationService> logger)
        {
            _httpClient = httpClient;
            _localStorage = localStorage;
            _logger = logger;
        }

        public async Task<AuthenticationResponse> LoginAsync(LoginRequest request)
        {
            try
            {
                // Note: Login typically doesn't validate password complexity 
                // since existing users may have passwords that predate current requirements
                
                // Map LoginRequest to AuthenticateRequest (server expects PascalCase)
                _logger.LogDebug("LoginAsync - Request details: Username='{Username}', AppId='{AppId}', Platform='{Platform}'", 
                    request.Username, request.AppId ?? "NULL", request.Platform ?? "NULL");
                
                var loginDto = new
                {
                    Username = request.Username,  // Primary login identifier
                    Email = request.Email,        // Optional, for backward compatibility
                    Password = request.Password,
                    AppId = request.AppId,
                    Platform = request.Platform,
                    DeviceInfo = request.DeviceInfo,
                    ProviderName = request.ProviderName,
                    ProviderToken = request.ProviderToken,
                    TrustedDeviceToken = request.TrustedDeviceToken, // For 2FA trusted device bypass
                    AppVersion = "1.0.0" // Default app version
                };
                
                var response = await _httpClient.PostAsJsonAsync("api/auth/login", loginDto);

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
                    if (authResponse != null)
                    {
                        // If 2FA is required, don't store auth yet - return for UI to handle
                        if (authResponse.RequiresTwoFactor)
                        {
                            _logger.LogInformation("Login requires 2FA verification for user: {Username}", request.Username);
                            return authResponse;
                        }

                        await StoreAuthenticationAsync(authResponse);
                        OnAuthenticationChanged?.Invoke(authResponse);
                        return authResponse;
                    }
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Login failed with status {StatusCode}. Error content: {ErrorContent}", response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent);
                    throw new AuthenticationException(errorResponse?.Message ?? $"Login failed with status {response.StatusCode}");
                }
                catch (JsonException)
                {
                    // If we can't parse the error response, use the raw content
                    throw new AuthenticationException($"Login failed with status {response.StatusCode}: {errorContent}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during login");
                throw new AuthenticationException("Network error. Please check your connection and try again.");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error parsing login response");
                throw new AuthenticationException("Invalid response from server");
            }
        }

        public async Task<AuthenticationResponse> RegisterAsync(RegistrationRequest request)
        {
            try
            {
                // Validate password requirements before sending to API
                if (!string.IsNullOrEmpty(request.Password))
                {
                    var authConfig = await GetAuthenticationConfigurationAsync(request.AppId);
                    if (authConfig != null)
                    {
                        var validationResult = ValidatePassword(request.Password, authConfig);
                        if (!validationResult.IsValid)
                        {
                            throw new AuthenticationException(validationResult.ErrorMessage);
                        }
                    }
                }

                var response = await _httpClient.PostAsJsonAsync("api/auth/register", request);

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
                    if (authResponse != null)
                    {
                        await StoreAuthenticationAsync(authResponse);
                        OnAuthenticationChanged?.Invoke(authResponse);
                        return authResponse;
                    }
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Registration failed with status {StatusCode}. Error content: {ErrorContent}", response.StatusCode, errorContent);
                
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent);
                    throw new AuthenticationException(errorResponse?.Message ?? $"Registration failed with status {response.StatusCode}");
                }
                catch (JsonException)
                {
                    // If we can't parse the error response, use the raw content
                    throw new AuthenticationException($"Registration failed with status {response.StatusCode}: {errorContent}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during registration");
                throw new AuthenticationException("Network error. Please check your connection and try again.");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error parsing registration response");
                throw new AuthenticationException("Invalid response from server");
            }
        }

        public async Task<AuthenticationResponse> RegisterWithTokenAsync(RegistrationRequest request)
        {
            try
            {
                // Validate password requirements before sending to API
                if (!string.IsNullOrEmpty(request.Password))
                {
                    var authConfig = await GetAuthenticationConfigurationAsync(request.AppId);
                    if (authConfig != null)
                    {
                        var validationResult = ValidatePassword(request.Password, authConfig);
                        if (!validationResult.IsValid)
                        {
                            throw new AuthenticationException(validationResult.ErrorMessage);
                        }
                    }
                }

                // Map RegistrationRequest to RegisterUserWithTokenDto (server expects PascalCase)
                _logger.LogDebug("RegisterWithTokenAsync - Request details: RegistrationToken='{RegistrationToken}', ProviderToken='{ProviderToken}', Email='{Email}', Username='{Username}', AppId='{AppId}'", 
                    request.RegistrationToken ?? "NULL", request.ProviderToken ?? "NULL", request.Email, request.Username ?? "NULL", request.AppId);
                
                var tokenRegistrationDto = new 
                {
                    Token = request.RegistrationToken,
                    Username = request.Username ?? request.Email, // Fall back to email if username not provided
                    Email = request.Email,
                    Password = request.Password,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    AppId = request.AppId,
                    Platform = request.Platform,
                    DeviceInfo = request.DeviceInfo
                };                _logger.LogDebug("RegisterWithTokenAsync - Making registration request to api/userregistration/register-with-token with Email: {Email}, Username: {Username}, AppId: {AppId}, Platform: {Platform}", 
                    request.Email, request.Username ?? request.Email, request.AppId, request.Platform);

                var response = await _httpClient.PostAsJsonAsync("api/userregistration/register-with-token", tokenRegistrationDto);

                if (response.IsSuccessStatusCode)
                {
                    // For token registration, the response might be different
                    // Let's check if it's a RegistrationResponseDto or AuthenticationResponse
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    _logger.LogInformation("Raw response content (first 500 chars): {ResponseContent}", 
                        responseContent.Length > 500 ? responseContent.Substring(0, 500) + "..." : responseContent);
                    
                    try
                    {
                        // Try to parse as AuthenticationResponse first
                        _logger.LogDebug("Attempting to parse response as AuthenticationResponse");
                        var authResponse = JsonSerializer.Deserialize<AuthenticationResponse>(responseContent);
                        _logger.LogDebug("AuthenticationResponse parsed: JwtToken={HasJwtToken}", !string.IsNullOrEmpty(authResponse?.JwtToken));
                        
                        if (authResponse != null && !string.IsNullOrEmpty(authResponse.JwtToken))
                        {
                            _logger.LogInformation("Successful AuthenticationResponse with JWT token");
                            await StoreAuthenticationAsync(authResponse);
                            OnAuthenticationChanged?.Invoke(authResponse);
                            return authResponse;
                        }
                        else
                        {
                            _logger.LogDebug("AuthenticationResponse parsing failed or no JWT token found");
                        }
                    }
                    catch (JsonException ex)
                    {
                        // AuthenticationResponse parsing failed, continue to try RegistrationResponseDto
                        _logger.LogDebug("AuthenticationResponse JSON parsing failed: {Error}", ex.Message);
                    }
                    
                    try
                    {
                        // Try to parse as RegistrationResponseDto
                        _logger.LogDebug("Attempting to parse response as RegistrationResponseDto");
                        var registrationResponse = JsonSerializer.Deserialize<RegistrationResponseDto>(responseContent);
                        _logger.LogInformation("Parsed RegistrationResponseDto: Success={Success}, Message='{Message}', ErrorCode='{ErrorCode}', UserId='{UserId}', CompanyClientId='{CompanyClientId}', AssignedRole={AssignedRole}, RequiresStripeSetup={RequiresStripeSetup}", 
                            registrationResponse?.Success, registrationResponse?.Message, registrationResponse?.ErrorCode, 
                            registrationResponse?.UserId, registrationResponse?.CompanyClientId, registrationResponse?.AssignedRole, registrationResponse?.RequiresStripeSetup);
                        
                        if (registrationResponse != null)
                        {
                            if (registrationResponse.Success)
                            {
                                _logger.LogInformation("Registration response indicates success. Registration completed - user needs to login to authenticate.");
                                // Create a basic AuthenticationResponse for successful registration but don't store it
                                // as authentication data since there's no JWT token
                                var basicResponse = new AuthenticationResponse
                                {
                                    Email = request.Email,
                                    FirstName = request.FirstName,
                                    LastName = request.LastName,
                                    UserId = registrationResponse.UserId ?? string.Empty
                                    // Note: No JWT token - user must login separately for authentication
                                };
                                
                                // Don't store authentication state for registration - only return the response
                                // The UI should handle this by showing a "registration successful, please login" message
                                OnAuthenticationChanged?.Invoke(basicResponse);
                                return basicResponse;
                            }
                            else
                            {
                                _logger.LogWarning("Registration response indicates failure: Success={Success}, Message='{Message}', ErrorCode='{ErrorCode}'", 
                                    registrationResponse.Success, registrationResponse.Message, registrationResponse.ErrorCode);
                                
                                // Handle specific error codes appropriately
                                if (registrationResponse.ErrorCode == "USER_EXISTS")
                                {
                                    _logger.LogWarning("User already exists error detected");
                                    throw new AuthenticationException($"User already exists: {registrationResponse.Message}");
                                }
                                else
                                {
                                    var errorMessage = registrationResponse.Message ?? "Registration failed";
                                    _logger.LogError("Registration failed with error: '{ErrorMessage}', ErrorCode: '{ErrorCode}'", errorMessage, registrationResponse.ErrorCode);
                                    throw new AuthenticationException(errorMessage);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning("RegistrationResponseDto parsing returned null");
                        }
                        
                        // If registrationResponse is null, fall through to the fallback logic below
                    }
                    catch (JsonException ex)
                    {
                        // RegistrationResponseDto parsing also failed, continue to fallback
                        _logger.LogError("RegistrationResponseDto JSON parsing failed: {Error}", ex.Message);
                    }
                    
                    // If both parsing attempts fail or return null, log and create a basic response
                    _logger.LogWarning("Unable to parse registration response as either AuthenticationResponse or RegistrationResponseDto. Raw response: {ResponseContent}", responseContent);
                    
                    // Create a basic AuthenticationResponse assuming success since we got HTTP 200
                    // But don't store authentication state since there's no JWT token
                    var fallbackResponse = new AuthenticationResponse
                    {
                        Email = request.Email,
                        FirstName = request.FirstName,
                        LastName = request.LastName
                    };
                    
                    // Don't store authentication state for registration - only return the response
                    OnAuthenticationChanged?.Invoke(fallbackResponse);
                    return fallbackResponse;
                }
                else
                {
                    // Handle non-success HTTP status codes
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Token registration failed with status {StatusCode}. Error content: {ErrorContent}", response.StatusCode, errorContent);
                    
                    // Try to parse the error response to get a better error message
                    try
                    {
                        var registrationError = JsonSerializer.Deserialize<RegistrationResponseDto>(errorContent);
                        if (registrationError != null)
                        {
                            if (registrationError.ErrorCode == "USER_EXISTS")
                            {
                                throw new AuthenticationException($"User already exists: {registrationError.Message}");
                            }
                            else
                            {
                                throw new AuthenticationException(registrationError.Message ?? $"Token registration failed with status {response.StatusCode}");
                            }
                        }
                        else
                        {
                            // If we can't deserialize to RegistrationResponseDto, try generic error handling
                            throw new AuthenticationException($"Token registration failed with status {response.StatusCode}");
                        }
                    }
                    catch (JsonException)
                    {
                        // If we can't parse as RegistrationResponseDto, try generic ErrorResponse
                        try
                        {
                            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent);
                            throw new AuthenticationException(errorResponse?.Message ?? $"Token registration failed with status {response.StatusCode}");
                        }
                        catch (JsonException)
                        {
                            // If we can't parse the error response, use the raw content
                            throw new AuthenticationException($"Token registration failed with status {response.StatusCode}: {errorContent}");
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during token registration");
                throw new AuthenticationException("Network error. Please check your connection and try again.");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error parsing token registration response");
                throw new AuthenticationException("Invalid response from server");
            }
        }

        public Task<AuthenticationResponse> AuthenticateWithProviderAsync(string providerName, string appId)
        {
            // The OAuth popup flow is handled by the component directly:
            // 1. Call GetOAuthAuthorizeUrlAsync to get the authorization URL
            // 2. Open the URL in a popup via JS interop (wildwoodOAuth.openPopup)
            // 3. The callback endpoint handles code exchange and returns AuthenticationResponse
            // 4. The popup posts the response back via postMessage
            //
            // This method is kept for interface compatibility. If you need to authenticate
            // with a provider token directly, use LoginAsync with ProviderName/ProviderToken set.
            throw new InvalidOperationException(
                "OAuth authentication is handled via popup flow. " +
                "Use GetOAuthAuthorizeUrlAsync + JS interop instead.");
        }

        public async Task<string?> GetOAuthAuthorizeUrlAsync(string providerName, string appId)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"api/auth/oauth/{appId}/authorize?provider={Uri.EscapeDataString(providerName)}");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<OAuthAuthorizeResponse>();
                    return result?.AuthorizationUrl;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get OAuth authorize URL for {Provider}. Status: {Status}, Response: {Response}",
                    providerName, response.StatusCode, errorContent);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting OAuth authorize URL for {Provider}", providerName);
                return null;
            }
        }

        public async Task<List<AuthProvider>> GetAvailableProvidersAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/auth/providers");

                if (response.IsSuccessStatusCode)
                {
                    var providers = await response.Content.ReadFromJsonAsync<List<AuthProvider>>();
                    return providers ?? new List<AuthProvider>();
                }

                return new List<AuthProvider>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available providers");
                return new List<AuthProvider>();
            }
        }

        public async Task<List<AuthProvider>> GetAvailableProvidersAsync(string appId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/AppComponentConfigurations/{appId}/auth-providers");

                if (response.IsSuccessStatusCode)
                {
                    var configResponse = await response.Content.ReadFromJsonAsync<AppComponentAuthProvidersResponse>();
                    if (configResponse?.AuthProviders != null)
                    {
                        // Filter to only enabled providers without LINQ
                        var enabledProviders = new List<AuthProvider>();
                        foreach (var provider in configResponse.AuthProviders)
                        {
                            if (provider.IsEnabled)
                            {
                                enabledProviders.Add(new AuthProvider
                                {
                                    Name = provider.ProviderName,
                                    DisplayName = provider.DisplayName,
                                    Icon = provider.Icon,
                                    IsEnabled = provider.IsEnabled,
                                    ClientId = provider.ClientId,
                                    RedirectUri = provider.RedirectUri
                                });
                            }
                        }
                        
                        // Sort by DisplayName without LINQ
                        enabledProviders.Sort((p1, p2) => string.Compare(p1.DisplayName, p2.DisplayName, StringComparison.OrdinalIgnoreCase));
                        return enabledProviders;
                    }
                }

                return new List<AuthProvider>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available providers for app {AppId}", appId);
                return new List<AuthProvider>();
            }
        }

        public async Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync(string appId)
        {
            try
            {
                if (string.IsNullOrEmpty(appId))
                {
                    _logger.LogWarning("AppId is not provided, cannot get captcha configuration");
                    return null;
                }

                var response = await _httpClient.GetAsync($"api/AppComponentConfigurations/{appId}/captcha");

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<CaptchaConfiguration>();
                }

                _logger.LogWarning("Failed to get captcha configuration for app {AppId}. Status: {StatusCode}", appId, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting captcha configuration for app {AppId}", appId);
                return null;
            }
        }

        public async Task<AuthenticationConfiguration?> GetAuthenticationConfigurationAsync(string appId)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 2000;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation("Attempting to get auth configuration for app {AppId} (attempt {Attempt}/{MaxRetries})", appId, attempt, maxRetries);
                    
                    var response = await _httpClient.GetAsync($"api/AppComponentConfigurations/{appId}/auth-configuration");

                    _logger.LogInformation("Auth configuration response for app {AppId}: StatusCode={StatusCode}", appId, response.StatusCode);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        _logger.LogDebug("Auth configuration response content (first 500 chars): {Content}", 
                            content.Length > 500 ? content.Substring(0, 500) : content);
                        
                        var config = System.Text.Json.JsonSerializer.Deserialize<AuthenticationConfiguration>(
                            content, 
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                        if (config != null)
                        {
                            _logger.LogInformation("Successfully loaded auth configuration for app {AppId}. IsEnabled={IsEnabled}, AllowTokenRegistration={AllowTokenRegistration}, AllowOpenRegistration={AllowOpenRegistration}", 
                                appId, config.IsEnabled, config.AllowTokenRegistration, config.AllowOpenRegistration);
                        }
                        else
                        {
                            _logger.LogWarning("Auth configuration deserialization returned null for app {AppId}", appId);
                        }
                        
                        return config;
                    }

                    // Don't retry on 404 (config not found) or 401/403 (auth issues) - these won't be fixed by retrying
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                        response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        _logger.LogWarning("GetAuthenticationConfigurationAsync for app {AppId} returned {StatusCode}", appId, response.StatusCode);
                        return null;
                    }

                    // For other errors, log and potentially retry
                    _logger.LogWarning("GetAuthenticationConfigurationAsync for app {AppId} failed with status {StatusCode} on attempt {Attempt}", 
                        appId, response.StatusCode, attempt);
                }
                catch (HttpRequestException ex) when (attempt < maxRetries)
                {
                    // Connection refused or other network errors - retry after delay
                    _logger.LogWarning("Connection error getting auth configuration for app {AppId} on attempt {Attempt}: {Message}. Retrying in {DelayMs}ms...", 
                        appId, attempt, ex.Message, retryDelayMs);
                    await Task.Delay(retryDelayMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting authentication configuration for app {AppId} on attempt {Attempt}", appId, attempt);
                    
                    // Only retry on the last attempt if it's a recoverable error
                    if (attempt >= maxRetries)
                    {
                        return null;
                    }
                    
                    await Task.Delay(retryDelayMs);
                }
            }
            
            _logger.LogError("Failed to get authentication configuration for app {AppId} after {MaxRetries} attempts", appId, maxRetries);
            return null;
        }

        public async Task<bool> ValidateLicenseTokenAsync(string token)
        {
            try
            {
                var request = new { Token = token };
                var response = await _httpClient.PostAsJsonAsync("api/auth/validate-license", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<LicenseValidationResult>();
                    return result?.IsValid ?? false;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating license token");
                return false;
            }
        }

        public async Task<(bool IsValid, string ErrorMessage)> ValidatePasswordAsync(string password, string appId)
        {
            try
            {
                if (string.IsNullOrEmpty(password))
                {
                    return (false, "Password is required.");
                }
                
                var authConfig = await GetAuthenticationConfigurationAsync(appId);
                if (authConfig == null)
                {
                    return (true, "No password requirements configured.");
                }

                return ValidatePassword(password, authConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating password for app {AppId}", appId);
                return (false, "Unable to validate password requirements.");
            }
        }

        public async Task<string> GetPasswordRequirementsAsync(string appId)
        {
            try
            {
                var authConfig = await GetAuthenticationConfigurationAsync(appId);
                if (authConfig == null)
                {
                    return "No specific password requirements.";
                }

                return GetPasswordRequirementsText(authConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting password requirements for app {AppId}", appId);
                return "Unable to retrieve password requirements.";
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                await _httpClient.PostAsync("api/auth/logout", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
            }
            finally
            {
                await ClearAuthenticationAsync();
                OnLogout?.Invoke();
            }
        }

        public async Task<bool> RefreshTokenAsync()
        {
            try
            {
                var refreshToken = await _localStorage.GetItemAsync<string>("refreshToken");
                if (string.IsNullOrEmpty(refreshToken))
                    return false;

                // Clear any expired JWT from the Authorization header before refreshing.
                // The refresh endpoint should not require a valid JWT - only the refresh token.
                _httpClient.DefaultRequestHeaders.Authorization = null;

                var request = new { RefreshToken = refreshToken };
                var response = await _httpClient.PostAsJsonAsync("api/auth/refresh-token", request);

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
                    if (authResponse != null)
                    {
                        await StoreAuthenticationAsync(authResponse);
                        OnAuthenticationChanged?.Invoke(authResponse);
                        return true;
                    }
                }

                // Only clear stored tokens when the server explicitly rejects the refresh token
                // (400 Bad Request = invalid/revoked token, 401 Unauthorized = token expired).
                // Do NOT clear on 5xx server errors or network issues - the refresh token
                // may still be valid and a retry could succeed.
                var statusCode = (int)response.StatusCode;
                if (statusCode == 400 || statusCode == 401)
                {
                    _logger.LogWarning("Refresh token rejected by server (HTTP {StatusCode}) - clearing stored tokens", statusCode);
                    await ClearAuthenticationAsync();
                }
                else
                {
                    _logger.LogWarning("Token refresh failed with HTTP {StatusCode} - keeping stored tokens for retry", statusCode);
                }
                return false;
            }
            catch (Exception ex)
            {
                // Network errors, timeouts, etc. - do NOT clear tokens.
                // The refresh token is likely still valid, allow retry.
                _logger.LogError(ex, "Error refreshing token (network/transient) - keeping stored tokens for retry");
                return false;
            }
        }

        public async Task<bool> ResetPasswordAsync(string newPassword, string confirmPassword, string appId)
        {
            try
            {
                _logger.LogInformation("Attempting password reset for app: {AppId}", appId);

                var request = new
                {
                    NewPassword = newPassword,
                    ConfirmPassword = confirmPassword,
                    AppId = appId
                };

                var response = await _httpClient.PostAsJsonAsync("api/auth/reset-password", request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Password reset successful");
                    return true;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Password reset failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                // Try to parse the error message
                try
                {
                    var errorResponse = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    throw new InvalidOperationException(errorResponse?.Message ?? "Password reset failed");
                }
                catch (System.Text.Json.JsonException)
                {
                    throw new InvalidOperationException($"Password reset failed: {errorContent}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Error during password reset");
                throw new InvalidOperationException($"Password reset failed: {ex.Message}");
            }
        }

        public async Task<bool> RequestPasswordResetAsync(string email, string appId)
        {
            try
            {
                _logger.LogInformation("Requesting password reset for email: {Email}, app: {AppId}", email, appId);

                var request = new
                {
                    Email = email,
                    AppId = appId
                };

                var response = await _httpClient.PostAsJsonAsync("api/auth/forgot-password", request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Password reset request submitted successfully");
                    return true;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Password reset request failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                // Try to parse the error message
                try
                {
                    var errorResponse = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    throw new InvalidOperationException(errorResponse?.Message ?? "Password reset request failed");
                }
                catch (System.Text.Json.JsonException)
                {
                    throw new InvalidOperationException($"Password reset request failed: {errorContent}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Error during password reset request");
                throw new InvalidOperationException($"Password reset request failed: {ex.Message}");
            }
        }

        private async Task StoreAuthenticationAsync(AuthenticationResponse response)
        {
            _logger.LogInformation("🔐 Storing authentication data for user: {Email}", response.Email ?? "Unknown");
            _logger.LogInformation("🔑 JWT Token length: {TokenLength}", response.JwtToken?.Length ?? 0);
            _logger.LogInformation("🔄 Refresh Token length: {RefreshTokenLength}", response.RefreshToken?.Length ?? 0);

            await _localStorage.SetItemAsync("accessToken", response.JwtToken);
            await _localStorage.SetItemAsync("refreshToken", response.RefreshToken);
            await _localStorage.SetItemAsync("user", response);

            // Set authorization header for future requests
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", response.JwtToken);
            
            _logger.LogInformation("✅ Authorization header set on AuthenticationService HttpClient");
            _logger.LogInformation("🔑 Bearer token preview: {TokenPreview}...", 
                response.JwtToken?.Substring(0, Math.Min(20, response.JwtToken.Length)) ?? "null");
        }

        private async Task ClearAuthenticationAsync()
        {
            await _localStorage.RemoveItemAsync("accessToken");
            await _localStorage.RemoveItemAsync("refreshToken");
            await _localStorage.RemoveItemAsync("user");

            // Clear authorization header
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        private (bool IsValid, string ErrorMessage) ValidatePassword(string password, AuthenticationConfiguration config)
        {
            if (string.IsNullOrEmpty(password))
                return (false, "Password is required.");

            if (password.Length < config.PasswordMinimumLength)
                return (false, $"Password must be at least {config.PasswordMinimumLength} characters long.");

            if (config.PasswordRequireUppercase && !HasUppercaseCharacter(password))
                return (false, "Password must contain at least one uppercase letter (A-Z).");

            if (config.PasswordRequireLowercase && !HasLowercaseCharacter(password))
                return (false, "Password must contain at least one lowercase letter (a-z).");

            if (config.PasswordRequireDigit && !HasDigitCharacter(password))
                return (false, "Password must contain at least one number (0-9).");

            if (config.PasswordRequireSpecialChar && !HasSpecialCharacter(password))
                return (false, "Password must contain at least one special character (!@#$%^&*).");

            return (true, "Password meets all requirements.");
        }

        private bool HasUppercaseCharacter(string password)
        {
            foreach (char c in password)
            {
                if (char.IsUpper(c))
                    return true;
            }
            return false;
        }

        private bool HasLowercaseCharacter(string password)
        {
            foreach (char c in password)
            {
                if (char.IsLower(c))
                    return true;
            }
            return false;
        }

        private bool HasDigitCharacter(string password)
        {
            foreach (char c in password)
            {
                if (char.IsDigit(c))
                    return true;
            }
            return false;
        }

        private bool HasSpecialCharacter(string password)
        {
            foreach (char c in password)
            {
                if (!char.IsLetterOrDigit(c))
                    return true;
            }
            return false;
        }

        private string GetPasswordRequirementsText(AuthenticationConfiguration config)
        {
            var requirements = new List<string>();
            
            requirements.Add($"at least {config.PasswordMinimumLength} characters");
            
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

            // Build "all but last" string without LINQ
            var allButLastItems = new string[requirements.Count - 1];
            for (int i = 0; i < requirements.Count - 1; i++)
            {
                allButLastItems[i] = requirements[i];
            }
            var allButLast = string.Join(", ", allButLastItems);
            var lastItem = requirements[requirements.Count - 1];
            return $"Password must have {allButLast}, and {lastItem}.";
        }

        public async Task<bool> HasRegistrationTokensAsync(string appId)
        {
            try
            {
                // Check if there are active registration tokens for this app
                var response = await _httpClient.GetAsync($"api/registrationtokens/app/{appId}/has-tokens");
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<bool>();
                    return result;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking registration tokens for app {AppId}", appId);
                return false;
            }
        }

        public async Task<bool> ValidateRegistrationTokenAsync(string token)
        {
            try
            {
                // Validate the registration token
                var response = await _httpClient.GetAsync($"api/registrationtokens/validate-simple/{token}");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<bool>();
                    return result;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating registration token {Token}", token);
                return false;
            }
        }

        #region Passkey/WebAuthn Methods

        public async Task<object> GetPasskeyAuthenticationOptionsAsync(string appId)
        {
            try
            {
                _logger.LogInformation("Getting passkey authentication options for app: {AppId}", appId);

                var request = new { AppId = appId };
                var response = await _httpClient.PostAsJsonAsync("api/webauthn/authenticate/options", request);

                if (response.IsSuccessStatusCode)
                {
                    var options = await response.Content.ReadFromJsonAsync<object>();
                    return options ?? throw new InvalidOperationException("Failed to get authentication options.");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to get passkey authentication options: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    throw new InvalidOperationException(errorResponse?.Message ?? "Failed to get passkey authentication options.");
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException($"Failed to get passkey authentication options: {errorContent}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Error getting passkey authentication options");
                throw new InvalidOperationException($"Failed to get passkey authentication options: {ex.Message}");
            }
        }

        public async Task<AuthenticationResponse> VerifyPasskeyAuthenticationAsync(string appId, JsonElement credential)
        {
            try
            {
                _logger.LogInformation("Verifying passkey authentication for app: {AppId}", appId);

                // Build the request with the credential data
                var request = new
                {
                    AppId = appId,
                    Id = credential.GetProperty("id").GetString(),
                    RawId = credential.GetProperty("rawId").GetString(),
                    Type = credential.GetProperty("type").GetString(),
                    Response = new
                    {
                        ClientDataJSON = credential.GetProperty("response").GetProperty("clientDataJSON").GetString(),
                        AuthenticatorData = credential.GetProperty("response").GetProperty("authenticatorData").GetString(),
                        Signature = credential.GetProperty("response").GetProperty("signature").GetString(),
                        UserHandle = credential.GetProperty("response").TryGetProperty("userHandle", out var userHandle)
                            ? userHandle.GetString()
                            : null
                    }
                };

                var response = await _httpClient.PostAsJsonAsync("api/webauthn/authenticate", request);

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
                    if (authResponse != null)
                    {
                        await StoreAuthenticationAsync(authResponse);
                        OnAuthenticationChanged?.Invoke(authResponse);
                        return authResponse;
                    }
                    throw new InvalidOperationException("Invalid response from passkey authentication.");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Passkey authentication failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    throw new InvalidOperationException(errorResponse?.Message ?? "Passkey authentication failed.");
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException($"Passkey authentication failed: {errorContent}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Error during passkey authentication");
                throw new InvalidOperationException($"Passkey authentication failed: {ex.Message}");
            }
        }

        public async Task<object> GetPasskeyRegistrationOptionsAsync(string appId)
        {
            try
            {
                _logger.LogInformation("Getting passkey registration options for app: {AppId}", appId);

                var request = new { AppId = appId };
                var response = await _httpClient.PostAsJsonAsync("api/webauthn/register/options", request);

                if (response.IsSuccessStatusCode)
                {
                    var options = await response.Content.ReadFromJsonAsync<object>();
                    return options ?? throw new InvalidOperationException("Failed to get registration options.");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to get passkey registration options: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    throw new InvalidOperationException(errorResponse?.Message ?? "Failed to get passkey registration options.");
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException($"Failed to get passkey registration options: {errorContent}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Error getting passkey registration options");
                throw new InvalidOperationException($"Failed to get passkey registration options: {ex.Message}");
            }
        }

        public async Task CompletePasskeyRegistrationAsync(string appId, JsonElement credential)
        {
            try
            {
                _logger.LogInformation("Completing passkey registration for app: {AppId}", appId);

                // Build the request with the credential data
                var request = new
                {
                    AppId = appId,
                    Id = credential.GetProperty("id").GetString(),
                    RawId = credential.GetProperty("rawId").GetString(),
                    Type = credential.GetProperty("type").GetString(),
                    Response = new
                    {
                        ClientDataJSON = credential.GetProperty("response").GetProperty("clientDataJSON").GetString(),
                        AttestationObject = credential.GetProperty("response").GetProperty("attestationObject").GetString(),
                        Transports = credential.GetProperty("response").TryGetProperty("transports", out var transports)
                            ? transports.EnumerateArray().Select(t => t.GetString()).ToArray()
                            : null
                    }
                };

                var response = await _httpClient.PostAsJsonAsync("api/webauthn/register", request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Passkey registration completed successfully");
                    return;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Passkey registration failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    throw new InvalidOperationException(errorResponse?.Message ?? "Passkey registration failed.");
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException($"Passkey registration failed: {errorContent}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Error during passkey registration");
                throw new InvalidOperationException($"Passkey registration failed: {ex.Message}");
            }
        }

        #endregion

        #region Two-Factor Authentication Methods

        public async Task<TwoFactorSendCodeResponse> SendTwoFactorCodeAsync(string sessionId)
        {
            try
            {
                _logger.LogInformation("Sending 2FA verification code for session: {SessionId}", sessionId);

                var request = new TwoFactorSendCodeRequest { SessionId = sessionId };
                var response = await _httpClient.PostAsJsonAsync("api/twofactor/send-code", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<TwoFactorSendCodeResponse>();
                    return result ?? new TwoFactorSendCodeResponse { Success = false, ErrorMessage = "Failed to parse response" };
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send 2FA code: {StatusCode} - {Content}", response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    return new TwoFactorSendCodeResponse { Success = false, ErrorMessage = errorResponse?.Message ?? "Failed to send verification code" };
                }
                catch (JsonException)
                {
                    return new TwoFactorSendCodeResponse { Success = false, ErrorMessage = $"Failed to send verification code: {errorContent}" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending 2FA code");
                return new TwoFactorSendCodeResponse { Success = false, ErrorMessage = $"Error sending verification code: {ex.Message}" };
            }
        }

        public async Task<TwoFactorVerifyResponse> VerifyTwoFactorCodeAsync(TwoFactorVerifyRequest request)
        {
            try
            {
                _logger.LogInformation("Verifying 2FA code for session: {SessionId}", request.SessionId);

                var response = await _httpClient.PostAsJsonAsync("api/twofactor/verify", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<TwoFactorVerifyResponse>();
                    if (result?.Success == true && result.AuthResponse != null)
                    {
                        await StoreAuthenticationAsync(result.AuthResponse);
                        OnAuthenticationChanged?.Invoke(result.AuthResponse);
                    }
                    return result ?? new TwoFactorVerifyResponse { Success = false, ErrorMessage = "Failed to parse response" };
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to verify 2FA code: {StatusCode} - {Content}", response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    return new TwoFactorVerifyResponse { Success = false, ErrorMessage = errorResponse?.Message ?? "Verification failed" };
                }
                catch (JsonException)
                {
                    return new TwoFactorVerifyResponse { Success = false, ErrorMessage = $"Verification failed: {errorContent}" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying 2FA code");
                return new TwoFactorVerifyResponse { Success = false, ErrorMessage = $"Error verifying code: {ex.Message}" };
            }
        }

        public async Task<TwoFactorVerifyResponse> VerifyTwoFactorRecoveryCodeAsync(string sessionId, string recoveryCode, string ipAddress)
        {
            try
            {
                _logger.LogInformation("Verifying 2FA recovery code for session: {SessionId}", sessionId);

                var request = new
                {
                    SessionId = sessionId,
                    RecoveryCode = recoveryCode,
                    IpAddress = ipAddress
                };
                var response = await _httpClient.PostAsJsonAsync("api/twofactor/recovery", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<TwoFactorVerifyResponse>();
                    if (result?.Success == true && result.AuthResponse != null)
                    {
                        await StoreAuthenticationAsync(result.AuthResponse);
                        OnAuthenticationChanged?.Invoke(result.AuthResponse);
                    }
                    return result ?? new TwoFactorVerifyResponse { Success = false, ErrorMessage = "Failed to parse response" };
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to verify recovery code: {StatusCode} - {Content}", response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(errorContent);
                    return new TwoFactorVerifyResponse { Success = false, ErrorMessage = errorResponse?.Message ?? "Recovery code verification failed" };
                }
                catch (JsonException)
                {
                    return new TwoFactorVerifyResponse { Success = false, ErrorMessage = $"Recovery code verification failed: {errorContent}" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying recovery code");
                return new TwoFactorVerifyResponse { Success = false, ErrorMessage = $"Error verifying recovery code: {ex.Message}" };
            }
        }

        #endregion
    }

    public class AuthenticationException : Exception
    {
        public AuthenticationException(string message) : base(message) { }
        public AuthenticationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class ErrorResponse
    {
        public string? Message { get; set; }
        public string? Detail { get; set; }
    }

    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
