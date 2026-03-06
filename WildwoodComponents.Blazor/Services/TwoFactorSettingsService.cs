using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Service for Two-Factor Authentication self-service operations.
    /// </summary>
    public class TwoFactorSettingsService : ITwoFactorSettingsService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TwoFactorSettingsService> _logger;
        private string? _authToken;

        public TwoFactorSettingsService(
            HttpClient httpClient,
            ILogger<TwoFactorSettingsService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void SetApiBaseUrl(string baseUrl)
        {
            if (!string.IsNullOrEmpty(baseUrl))
            {
                _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            }
        }

        public void SetAuthToken(string token)
        {
            _authToken = token;
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        // ============================================
        // Status Operations
        // ============================================

        public async Task<TwoFactorUserStatus> GetStatusAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/twofactor/status");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TwoFactorUserStatus>()
                    ?? new TwoFactorUserStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting 2FA status");
                throw new TwoFactorServiceException("Failed to get 2FA status", ex);
            }
        }

        // ============================================
        // Credential Management
        // ============================================

        public async Task<List<TwoFactorCredential>> GetCredentialsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/twofactor/credentials");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<TwoFactorCredential>>()
                    ?? new List<TwoFactorCredential>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting 2FA credentials");
                throw new TwoFactorServiceException("Failed to get credentials", ex);
            }
        }

        public async Task<bool> SetPrimaryCredentialAsync(string credentialId)
        {
            try
            {
                var response = await _httpClient.PutAsync($"api/twofactor/credentials/{credentialId}/primary", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting primary credential {CredentialId}", credentialId);
                throw new TwoFactorServiceException("Failed to set primary credential", ex);
            }
        }

        public async Task<bool> RemoveCredentialAsync(string credentialId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/twofactor/credentials/{credentialId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing credential {CredentialId}", credentialId);
                throw new TwoFactorServiceException("Failed to remove credential", ex);
            }
        }

        // ============================================
        // Email 2FA Enrollment
        // ============================================

        public async Task<EmailEnrollmentResult> EnrollEmailAsync(string? email = null)
        {
            try
            {
                var request = new { Email = email };
                var response = await _httpClient.PostAsJsonAsync("api/twofactor/enroll/email", request);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<EmailEnrollmentResult>()
                        ?? new EmailEnrollmentResult { Success = false, Message = "Invalid response" };
                }

                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Email enrollment failed: {Error}", error);
                return new EmailEnrollmentResult { Success = false, Message = error };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enrolling in email 2FA");
                throw new TwoFactorServiceException("Failed to enroll in email 2FA", ex);
            }
        }

        public async Task<bool> VerifyEmailEnrollmentAsync(string credentialId, string code)
        {
            try
            {
                var request = new { CredentialId = credentialId, Code = code };
                var response = await _httpClient.PostAsJsonAsync("api/twofactor/enroll/email/verify", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying email enrollment");
                throw new TwoFactorServiceException("Failed to verify email enrollment", ex);
            }
        }

        // ============================================
        // Authenticator (TOTP) Enrollment
        // ============================================

        public async Task<AuthenticatorEnrollmentResult> BeginAuthenticatorEnrollmentAsync(string? friendlyName = null)
        {
            try
            {
                var request = new { FriendlyName = friendlyName };
                var response = await _httpClient.PostAsJsonAsync("api/twofactor/enroll/authenticator", request);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<AuthenticatorEnrollmentResult>()
                        ?? new AuthenticatorEnrollmentResult { Success = false, Message = "Invalid response" };
                }

                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Authenticator enrollment failed: {Error}", error);
                return new AuthenticatorEnrollmentResult { Success = false, Message = error };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error beginning authenticator enrollment");
                throw new TwoFactorServiceException("Failed to begin authenticator enrollment", ex);
            }
        }

        public async Task<bool> CompleteAuthenticatorEnrollmentAsync(string credentialId, string code)
        {
            try
            {
                var request = new { CredentialId = credentialId, Code = code };
                var response = await _httpClient.PostAsJsonAsync("api/twofactor/enroll/authenticator/verify", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing authenticator enrollment");
                throw new TwoFactorServiceException("Failed to complete authenticator enrollment", ex);
            }
        }

        // ============================================
        // Recovery Codes
        // ============================================

        public async Task<RecoveryCodeInfo> GetRecoveryCodeInfoAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/twofactor/recovery-codes/info");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<RecoveryCodeInfo>()
                    ?? new RecoveryCodeInfo();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recovery code info");
                throw new TwoFactorServiceException("Failed to get recovery code info", ex);
            }
        }

        public async Task<RegenerateRecoveryCodesResult> RegenerateRecoveryCodesAsync()
        {
            try
            {
                var response = await _httpClient.PostAsync("api/twofactor/recovery-codes/regenerate", null);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<RegenerateRecoveryCodesResult>()
                        ?? new RegenerateRecoveryCodesResult { Success = false, Message = "Invalid response" };
                }

                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Recovery code regeneration failed: {Error}", error);
                return new RegenerateRecoveryCodesResult { Success = false, Message = error };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error regenerating recovery codes");
                throw new TwoFactorServiceException("Failed to regenerate recovery codes", ex);
            }
        }

        // ============================================
        // Trusted Devices
        // ============================================

        public async Task<List<TrustedDevice>> GetTrustedDevicesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/twofactor/trusted-devices");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<TrustedDevice>>()
                    ?? new List<TrustedDevice>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trusted devices");
                throw new TwoFactorServiceException("Failed to get trusted devices", ex);
            }
        }

        public async Task<bool> RevokeTrustedDeviceAsync(string deviceId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/twofactor/trusted-devices/{deviceId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking trusted device {DeviceId}", deviceId);
                throw new TwoFactorServiceException("Failed to revoke trusted device", ex);
            }
        }

        public async Task<int> RevokeAllTrustedDevicesAsync()
        {
            try
            {
                var response = await _httpClient.DeleteAsync("api/twofactor/trusted-devices");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<RevokeDevicesResult>();
                    return result?.Count ?? 0;
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking all trusted devices");
                throw new TwoFactorServiceException("Failed to revoke all trusted devices", ex);
            }
        }

        private class RevokeDevicesResult
        {
            public string? Message { get; set; }
            public int Count { get; set; }
        }
    }

    /// <summary>
    /// Exception thrown by TwoFactorSettingsService operations.
    /// </summary>
    public class TwoFactorServiceException : Exception
    {
        public TwoFactorServiceException(string message) : base(message) { }
        public TwoFactorServiceException(string message, Exception innerException) : base(message, innerException) { }
    }
}
