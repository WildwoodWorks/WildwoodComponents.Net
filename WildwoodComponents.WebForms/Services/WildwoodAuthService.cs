using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.WebForms.Logging;
using WildwoodComponents.WebForms.Models;
using WildwoodComponents.WebForms.Session;

namespace WildwoodComponents.WebForms.Services
{
    /// <summary>
    /// Calls the WildwoodAPI authentication endpoints. Endpoint paths and request shapes
    /// match the Razor package's <c>WildwoodAuthService</c> exactly.
    /// </summary>
    public sealed class WildwoodAuthService : WildwoodServiceBase, IWildwoodAuthService
    {
        private readonly string _appVersion;

        /// <summary>Creates the service.</summary>
        /// <param name="httpClient">The shared client.</param>
        /// <param name="sessionManager">Token storage for the current request.</param>
        /// <param name="logger">Diagnostics sink.</param>
        /// <param name="appId">The Wildwood app id.</param>
        /// <param name="appVersion">Reported to the API for diagnostics.</param>
        public WildwoodAuthService(
            HttpClient httpClient,
            IWildwoodSessionManager sessionManager,
            IWildwoodLogger? logger,
            string? appId,
            string appVersion = "1.0.0")
            : base(httpClient, sessionManager, logger, appId)
        {
            _appVersion = appVersion;
        }

        /// <inheritdoc />
        public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                var apiRequest = new WildwoodLoginRequest
                {
                    Username = request.Username,
                    Password = request.Password,
                    AppId = AppId,
                    AppVersion = _appVersion,
                    Platform = "web",
                    DeviceInfo = "server"
                };

                using (var response = await SendAsync(HttpMethod.Post, "auth/login", apiRequest, cancellationToken).ConfigureAwait(false))
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var wwResponse = Deserialize<WildwoodAuthenticateResponse>(content);
                        if (wwResponse != null)
                        {
                            var authResponse = AuthResponse.FromWildwoodResponse(wwResponse);

                            // Tokens are only good once 2FA is satisfied; storing them here
                            // would sign the user in on the strength of a password alone.
                            if (!wwResponse.RequiresTwoFactor)
                            {
                                SessionManager.SetTokens(wwResponse.JwtToken, wwResponse.RefreshToken);
                            }

                            return AuthResult.Success(authResponse);
                        }
                    }

                    Logger.Warn("WildwoodAPI login returned " + (int)response.StatusCode + ": " + Truncate(content));

                    var errorResponse = Deserialize<ApiErrorResponse>(content);
                    return AuthResult.Failure(
                        errorResponse?.Message ?? "Login failed",
                        errorResponse?.RequiresTwoFactor ?? false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Login request failed.", ex);
                return AuthResult.Failure("An error occurred during login. Please try again.");
            }
        }

        /// <inheritdoc />
        public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

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
                    AppId = AppId
                };

                using (var response = await SendAsync(HttpMethod.Post, "auth/register", apiRequest, cancellationToken).ConfigureAwait(false))
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var wwResponse = Deserialize<WildwoodAuthenticateResponse>(content);
                        if (wwResponse != null)
                        {
                            var authResponse = AuthResponse.FromWildwoodResponse(wwResponse);
                            SessionManager.SetTokens(wwResponse.JwtToken, wwResponse.RefreshToken);
                            return AuthResult.Success(authResponse);
                        }
                    }

                    Logger.Warn("WildwoodAPI registration returned " + (int)response.StatusCode + ": " + Truncate(content));

                    var errorResponse = Deserialize<ApiErrorResponse>(content);
                    return AuthResult.Failure(errorResponse?.Message ?? "Registration failed");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Registration request failed.", ex);
                return AuthResult.Failure("An error occurred during registration. Please try again.");
            }
        }

        /// <inheritdoc />
        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var refreshToken = SessionManager.GetRefreshToken();
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    using (await SendAsync(
                               HttpMethod.Post,
                               "auth/revoke-token",
                               new WildwoodRefreshTokenRequest { RefreshToken = refreshToken! },
                               cancellationToken).ConfigureAwait(false))
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                // Including cancellation: a sign-out that cannot reach the API still has to
                // drop the local tokens, or the user stays signed in on this server.
                Logger.Warn("Revoking the refresh token failed; clearing local tokens anyway.", ex);
            }
            finally
            {
                SessionManager.ClearTokens();
            }
        }

        /// <inheritdoc />
        public async Task<AuthResult> RefreshTokenAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var refreshToken = SessionManager.GetRefreshToken();
                if (string.IsNullOrEmpty(refreshToken))
                {
                    return AuthResult.Failure("No refresh token available");
                }

                var apiRequest = new WildwoodRefreshTokenRequest { RefreshToken = refreshToken! };

                using (var response = await SendAsync(HttpMethod.Post, "auth/refresh-token", apiRequest, cancellationToken).ConfigureAwait(false))
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var wwResponse = Deserialize<WildwoodAuthenticateResponse>(content);
                        if (wwResponse != null)
                        {
                            var authResponse = AuthResponse.FromWildwoodResponse(wwResponse);
                            SessionManager.SetTokens(wwResponse.JwtToken, wwResponse.RefreshToken);
                            return AuthResult.Success(authResponse);
                        }
                    }

                    Logger.Warn("WildwoodAPI token refresh returned " + (int)response.StatusCode + ".");
                    SessionManager.ClearTokens();
                    return AuthResult.Failure("Token refresh failed");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Token refresh failed.", ex);
                SessionManager.ClearTokens();
                return AuthResult.Failure("An error occurred refreshing the session.");
            }
        }

        /// <inheritdoc />
        public async Task<ApiResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiRequest = new WildwoodForgotPasswordRequest
                {
                    Email = email,
                    AppId = AppId
                };

                using (var response = await SendAsync(HttpMethod.Post, "auth/forgot-password", apiRequest, cancellationToken).ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode
                        ? ApiResult.Ok("If an account with that email exists, a reset link has been sent.")
                        : ApiResult.Fail("Failed to process password reset request.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Forgot-password request failed.", ex);
                return ApiResult.Fail("An error occurred. Please try again.");
            }
        }

        /// <inheritdoc />
        public async Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                var apiRequest = new WildwoodResetPasswordRequest
                {
                    NewPassword = request.NewPassword,
                    ConfirmPassword = request.ConfirmPassword,
                    AppId = AppId
                };

                using (var response = await SendAsync(HttpMethod.Post, "auth/reset-password", apiRequest, cancellationToken).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return ApiResult.Ok("Password has been reset successfully.");
                    }

                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var errorResponse = Deserialize<ApiErrorResponse>(content);
                    return ApiResult.Fail(errorResponse?.Message ?? "Password reset failed.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Password reset failed.", ex);
                return ApiResult.Fail("An error occurred. Please try again.");
            }
        }

        /// <inheritdoc />
        public async Task<AuthResult> VerifyTwoFactorAsync(TwoFactorVerifyRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                var apiRequest = new WildwoodTwoFactorVerifyRequest
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Code = request.Code,
                    RememberDevice = request.RememberDevice
                };

                using (var response = await SendAsync(HttpMethod.Post, "twofactor/verify", apiRequest, cancellationToken).ConfigureAwait(false))
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var wwResponse = Deserialize<WildwoodTwoFactorResponse>(content);
                        if (wwResponse != null && wwResponse.Success && wwResponse.AuthResponse != null)
                        {
                            var authResponse = AuthResponse.FromWildwoodResponse(wwResponse.AuthResponse);
                            SessionManager.SetTokens(
                                wwResponse.AuthResponse.JwtToken,
                                wwResponse.AuthResponse.RefreshToken);
                            return AuthResult.Success(authResponse);
                        }

                        return AuthResult.Failure(wwResponse?.Message ?? "Two-factor verification failed");
                    }

                    var errorResponse = Deserialize<ApiErrorResponse>(content);
                    return AuthResult.Failure(errorResponse?.Message ?? "Two-factor verification failed");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Two-factor verification failed.", ex);
                return AuthResult.Failure("An error occurred during verification. Please try again.");
            }
        }

        /// <inheritdoc />
        public async Task<AuthConfigResponse> GetAuthConfigAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Interpolated rather than concatenated so the parity checker extracts the
                // same normalised path the other stacks produce for this call.
                using (var response = await SendAsync(
                           HttpMethod.Get,
                           $"twofactor/configuration/{Uri.EscapeDataString(AppId)}",
                           null,
                           cancellationToken).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var config = Deserialize<AuthConfigResponse>(content);
                        if (config != null)
                        {
                            return config;
                        }
                    }
                    else
                    {
                        Logger.Warn("WildwoodAPI auth configuration returned " + (int)response.StatusCode + ".");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read the auth configuration.", ex);
            }

            // Permissive defaults so an API outage degrades to a plain login form rather
            // than an empty page. Two-factor is off here because the login response, not
            // this configuration, decides whether a given sign-in needs a second factor.
            return new AuthConfigResponse
            {
                AllowRegistration = true,
                EnableTwoFactor = false
            };
        }

        /// <summary>
        /// Deserializes a body that may be empty or may not be JSON at all — an HTML error
        /// page from a proxy, for instance — without letting that become an exception.
        /// </summary>
        private T? Deserialize<T>(string content) where T : class
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(content, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Truncate(string content)
        {
            if (content == null)
            {
                return string.Empty;
            }

            return content.Length > 512 ? content.Substring(0, 512) + "..." : content;
        }
    }
}
