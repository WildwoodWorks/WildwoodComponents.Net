using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public class WildwoodTwoFactorSettingsService : IWildwoodTwoFactorSettingsService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodTwoFactorSettingsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodTwoFactorSettingsService(HttpClient httpClient, IWildwoodSessionManager sessionManager, ILogger<WildwoodTwoFactorSettingsService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<TwoFactorUserStatus?> GetStatusAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync("api/twofactor/status");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<TwoFactorUserStatus>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get 2FA status"); }
        return null;
    }

    public async Task<List<TwoFactorCredential>> GetCredentialsAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync("api/twofactor/credentials");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<TwoFactorCredential>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get 2FA credentials"); }
        return new();
    }

    public async Task<bool> SetPrimaryCredentialAsync(string credentialId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PutAsync($"api/twofactor/credentials/{credentialId}/primary", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to set primary credential"); return false; }
    }

    public async Task<bool> RemoveCredentialAsync(string credentialId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync($"api/twofactor/credentials/{credentialId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to remove credential"); return false; }
    }

    public async Task<EmailEnrollmentResult?> EnrollEmailAsync(string? email = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { Email = email };
            using var response = await _httpClient.PostAsJsonAsync("api/twofactor/enroll/email", payload);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<EmailEnrollmentResult>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to enroll email 2FA"); }
        return null;
    }

    public async Task<bool> VerifyEmailEnrollmentAsync(string credentialId, string code)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { CredentialId = credentialId, Code = code };
            using var response = await _httpClient.PostAsJsonAsync("api/twofactor/enroll/email/verify", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to verify email enrollment"); return false; }
    }

    public async Task<AuthenticatorEnrollmentResult?> BeginAuthenticatorEnrollmentAsync(string? friendlyName = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { FriendlyName = friendlyName };
            using var response = await _httpClient.PostAsJsonAsync("api/twofactor/enroll/authenticator", payload);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AuthenticatorEnrollmentResult>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to begin authenticator enrollment"); }
        return null;
    }

    public async Task<bool> CompleteAuthenticatorEnrollmentAsync(string credentialId, string code)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { CredentialId = credentialId, Code = code };
            using var response = await _httpClient.PostAsJsonAsync("api/twofactor/enroll/authenticator/verify", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to complete authenticator enrollment"); return false; }
    }

    public async Task<RecoveryCodeInfo?> GetRecoveryCodeInfoAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync("api/twofactor/recovery-codes/info");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<RecoveryCodeInfo>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get recovery code info"); }
        return null;
    }

    public async Task<RegenerateRecoveryCodesResult?> RegenerateRecoveryCodesAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync("api/twofactor/recovery-codes/regenerate", null);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<RegenerateRecoveryCodesResult>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to regenerate recovery codes"); }
        return null;
    }

    public async Task<List<TrustedDevice>> GetTrustedDevicesAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync("api/twofactor/trusted-devices");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<TrustedDevice>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get trusted devices"); }
        return new();
    }

    public async Task<bool> RevokeTrustedDeviceAsync(string deviceId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync($"api/twofactor/trusted-devices/{deviceId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to revoke trusted device"); return false; }
    }

    public async Task<int> RevokeAllTrustedDevicesAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync("api/twofactor/trusted-devices");
            if (response.IsSuccessStatusCode)
            {
                // API returns { "message": "3 device(s) revoked" } — parse count from message
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(content, JsonOptions);
                if (result != null && result.TryGetValue("message", out var message))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(message, @"^(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
                        return count;
                }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to revoke all trusted devices"); }
        return 0;
    }
}
