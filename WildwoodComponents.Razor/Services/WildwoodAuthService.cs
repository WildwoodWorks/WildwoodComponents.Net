using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Authentication service implementation that calls WildwoodAPI auth endpoints.
/// Razor Pages equivalent of WildwoodComponents.Services.AuthenticationService.
/// </summary>
public class WildwoodAuthService : IWildwoodAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodAuthService> _logger;
    private readonly string _appId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodAuthService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodAuthService> logger,
        string appId)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
        _appId = appId;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        try
        {
            var apiRequest = new WildwoodLoginRequest
            {
                Username = request.Username,
                Password = request.Password,
                AppId = _appId
            };

            var response = await _httpClient.PostAsJsonAsync("auth/login", apiRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var wwResponse = JsonSerializer.Deserialize<WildwoodAuthenticateResponse>(content, JsonOptions);
                if (wwResponse != null)
                {
                    var authResponse = AuthResponse.FromWildwoodResponse(wwResponse);

                    if (!wwResponse.RequiresTwoFactor)
                    {
                        _sessionManager.SetTokens(wwResponse.JwtToken, wwResponse.RefreshToken);
                    }

                    return AuthResult.Success(authResponse);
                }
            }

            _logger.LogWarning("WildwoodAPI login returned {StatusCode}: {Content}",
                (int)response.StatusCode, content);

            var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return AuthResult.Failure(
                errorResponse?.Message ?? "Login failed",
                errorResponse?.RequiresTwoFactor ?? false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login request failed");
            return AuthResult.Failure("An error occurred during login. Please try again.");
        }
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request)
    {
        try
        {
            var apiRequest = new WildwoodRegisterRequest
            {
                Email = request.Email,
                Username = request.Username,
                Password = request.Password,
                ConfirmPassword = request.ConfirmPassword,
                FirstName = request.FirstName ?? string.Empty,
                LastName = request.LastName ?? string.Empty,
                AppId = _appId
            };

            var response = await _httpClient.PostAsJsonAsync("auth/register", apiRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var wwResponse = JsonSerializer.Deserialize<WildwoodAuthenticateResponse>(content, JsonOptions);
                if (wwResponse != null)
                {
                    var authResponse = AuthResponse.FromWildwoodResponse(wwResponse);
                    _sessionManager.SetTokens(wwResponse.JwtToken, wwResponse.RefreshToken);
                    return AuthResult.Success(authResponse);
                }
            }

            var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return AuthResult.Failure(errorResponse?.Message ?? "Registration failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration request failed");
            return AuthResult.Failure("An error occurred during registration. Please try again.");
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            var refreshToken = _sessionManager.GetRefreshToken();
            if (!string.IsNullOrEmpty(refreshToken))
            {
                _sessionManager.ApplyAuthorizationHeader(_httpClient);
                await _httpClient.PostAsJsonAsync("auth/revoke-token", new { RefreshToken = refreshToken });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Revoke token API call failed, clearing local tokens anyway");
        }
        finally
        {
            _sessionManager.ClearTokens();
        }
    }

    public async Task<AuthResult> RefreshTokenAsync()
    {
        try
        {
            var refreshToken = _sessionManager.GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken))
                return AuthResult.Failure("No refresh token available");

            var apiRequest = new WildwoodRefreshTokenRequest { RefreshToken = refreshToken };
            var response = await _httpClient.PostAsJsonAsync("auth/refresh-token", apiRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var wwResponse = JsonSerializer.Deserialize<WildwoodAuthenticateResponse>(content, JsonOptions);
                if (wwResponse != null)
                {
                    var authResponse = AuthResponse.FromWildwoodResponse(wwResponse);
                    _sessionManager.SetTokens(wwResponse.JwtToken, wwResponse.RefreshToken);
                    return AuthResult.Success(authResponse);
                }
            }

            _sessionManager.ClearTokens();
            return AuthResult.Failure("Token refresh failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            _sessionManager.ClearTokens();
            return AuthResult.Failure("An error occurred refreshing the session.");
        }
    }

    public async Task<ApiResult> ForgotPasswordAsync(string email)
    {
        try
        {
            var apiRequest = new WildwoodForgotPasswordRequest
            {
                Email = email,
                AppId = _appId
            };

            var response = await _httpClient.PostAsJsonAsync("auth/forgot-password", apiRequest);
            return response.IsSuccessStatusCode
                ? ApiResult.Ok("If an account with that email exists, a reset link has been sent.")
                : ApiResult.Fail("Failed to process password reset request.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forgot password request failed");
            return ApiResult.Fail("An error occurred. Please try again.");
        }
    }

    public async Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest request)
    {
        try
        {
            var apiRequest = new WildwoodResetPasswordRequest
            {
                NewPassword = request.NewPassword,
                ConfirmPassword = request.ConfirmPassword,
                AppId = _appId
            };

            var response = await _httpClient.PostAsJsonAsync("auth/reset-password", apiRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return ApiResult.Ok("Password has been reset successfully.");

            var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return ApiResult.Fail(errorResponse?.Message ?? "Password reset failed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password reset failed");
            return ApiResult.Fail("An error occurred. Please try again.");
        }
    }

    public async Task<AuthResult> VerifyTwoFactorAsync(TwoFactorVerifyRequest request)
    {
        try
        {
            var apiRequest = new WildwoodTwoFactorVerifyRequest
            {
                SessionId = request.SessionId ?? string.Empty,
                Code = request.Code,
                RememberDevice = request.RememberDevice
            };

            var response = await _httpClient.PostAsJsonAsync("twofactor/verify", apiRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var wwResponse = JsonSerializer.Deserialize<WildwoodTwoFactorResponse>(content, JsonOptions);
                if (wwResponse?.Success == true && wwResponse.AuthResponse != null)
                {
                    var authResponse = AuthResponse.FromWildwoodResponse(wwResponse.AuthResponse);
                    _sessionManager.SetTokens(
                        wwResponse.AuthResponse.JwtToken,
                        wwResponse.AuthResponse.RefreshToken);
                    return AuthResult.Success(authResponse);
                }

                return AuthResult.Failure(wwResponse?.Message ?? "Two-factor verification failed");
            }

            var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return AuthResult.Failure(errorResponse?.Message ?? "Two-factor verification failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Two-factor verification failed");
            return AuthResult.Failure("An error occurred during verification. Please try again.");
        }
    }

    public async Task<AuthConfigResponse?> GetAuthConfigAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"twofactor/configuration/{_appId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AuthConfigResponse>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get auth configuration");
        }

        // Return defaults if API is unavailable
        return new AuthConfigResponse
        {
            AllowRegistration = true,
            EnableTwoFactor = false
        };
    }
}
