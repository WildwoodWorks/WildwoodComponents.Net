using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// The signed-in user's own passkeys: list, register (options + completion), rename and delete, over
    /// the api/webauthn endpoints. The browser ceremony itself (navigator.credentials.create) is the
    /// component's job; this service only moves JSON.
    /// </summary>
    public interface IPasskeyManagementService
    {
        void SetApiBaseUrl(string baseUrl);
        void SetAuthToken(string token);

        /// <summary>Whether passkeys are enabled for the app at all (anonymous endpoint).</summary>
        Task<bool> IsEnabledAsync(string appId);

        Task<List<UserPasskey>> GetPasskeysAsync(string appId);

        /// <summary>Registration options to hand to navigator.credentials.create, as raw JSON.</summary>
        Task<JsonElement> GetRegistrationOptionsAsync(string appId, string? friendlyName);

        /// <summary>Completes a registration with the browser's attestation (the webauthnRegister result).</summary>
        Task<UserPasskey?> CompleteRegistrationAsync(string appId, JsonElement credential);

        Task<UserPasskey?> RenameAsync(string passkeyId, string friendlyName);

        Task<bool> DeleteAsync(string passkeyId);
    }

    /// <inheritdoc />
    public class PasskeyManagementService : IPasskeyManagementService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PasskeyManagementService> _logger;

        public PasskeyManagementService(HttpClient httpClient, ILogger<PasskeyManagementService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>Same rule as TwoFactorSettingsService: paths are rooted at "api/", so a base ending in /api is trimmed.</summary>
        public void SetApiBaseUrl(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl))
                return;

            var root = baseUrl.TrimEnd('/');
            if (root.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                root = root[..^4];
            _httpClient.BaseAddress = new Uri(root + "/");
        }

        public void SetAuthToken(string token)
        {
            if (!string.IsNullOrEmpty(token))
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public async Task<bool> IsEnabledAsync(string appId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/webauthn/enabled/{Uri.EscapeDataString(appId)}");
                if (!response.IsSuccessStatusCode)
                    return false;
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                return json.ValueKind == JsonValueKind.True
                    || (json.ValueKind == JsonValueKind.Object
                        && TryGetBool(json, "enabled", out var enabled) && enabled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check whether passkeys are enabled for app {AppId}", appId);
                return false;
            }
        }

        public async Task<List<UserPasskey>> GetPasskeysAsync(string appId)
        {
            var response = await _httpClient.GetAsync($"api/webauthn/credentials?appId={Uri.EscapeDataString(appId)}");
            await EnsureSuccessAsync(response, "Could not load your passkeys.");
            return await response.Content.ReadFromJsonAsync<List<UserPasskey>>() ?? new List<UserPasskey>();
        }

        public async Task<JsonElement> GetRegistrationOptionsAsync(string appId, string? friendlyName)
        {
            var response = await _httpClient.PostAsJsonAsync("api/webauthn/register/options",
                new { AppId = appId, FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName.Trim() });
            await EnsureSuccessAsync(response, "Could not start passkey registration.");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<UserPasskey?> CompleteRegistrationAsync(string appId, JsonElement credential)
        {
            var responseJson = credential.GetProperty("response");
            var request = new
            {
                AppId = appId,
                Id = credential.GetProperty("id").GetString(),
                RawId = credential.GetProperty("rawId").GetString(),
                Type = credential.TryGetProperty("type", out var type) ? type.GetString() : "public-key",
                Response = new
                {
                    ClientDataJSON = responseJson.GetProperty("clientDataJSON").GetString(),
                    AttestationObject = responseJson.GetProperty("attestationObject").GetString(),
                    Transports = responseJson.TryGetProperty("transports", out var transports) && transports.ValueKind == JsonValueKind.Array
                        ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(transports.EnumerateArray(), t => t.GetString()))
                        : null
                }
            };

            var response = await _httpClient.PostAsJsonAsync("api/webauthn/register", request);
            await EnsureSuccessAsync(response, "The passkey could not be registered.");
            return await response.Content.ReadFromJsonAsync<UserPasskey>();
        }

        public async Task<UserPasskey?> RenameAsync(string passkeyId, string friendlyName)
        {
            var response = await _httpClient.PatchAsJsonAsync($"api/webauthn/credentials/{Uri.EscapeDataString(passkeyId)}",
                new { FriendlyName = friendlyName.Trim() });
            await EnsureSuccessAsync(response, "The passkey could not be renamed.");
            return await response.Content.ReadFromJsonAsync<UserPasskey>();
        }

        public async Task<bool> DeleteAsync(string passkeyId)
        {
            var response = await _httpClient.DeleteAsync($"api/webauthn/credentials/{Uri.EscapeDataString(passkeyId)}");
            await EnsureSuccessAsync(response, "The passkey could not be removed.");
            return true;
        }

        /// <summary>Throws with the API's own message ({error, message}) when it gives one.</summary>
        private async Task EnsureSuccessAsync(HttpResponseMessage response, string fallback)
        {
            if (response.IsSuccessStatusCode)
                return;

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Passkey API call failed: {Status} {Body}", response.StatusCode, body);
            string? message = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var name in new[] { "message", "Message", "error" })
                    {
                        if (doc.RootElement.TryGetProperty(name, out var m) && m.ValueKind == JsonValueKind.String)
                        {
                            message = m.GetString();
                            break;
                        }
                    }
                }
            }
            catch (JsonException) { }

            throw new PasskeyManagementException(string.IsNullOrWhiteSpace(message) ? fallback : message!);
        }

        private static bool TryGetBool(JsonElement obj, string name, out bool value)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                    && (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False))
                {
                    value = prop.Value.GetBoolean();
                    return true;
                }
            }
            value = false;
            return false;
        }
    }
}
