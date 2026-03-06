using System.Net.Http.Json;
using System.Text.Json;

namespace WildwoodComponentsTestSuiteBlazor.Services;

/// <summary>
/// Handles authentication against WildwoodAPI for the test suite.
/// Calls POST /api/auth/login with the configured AppId.
/// </summary>
public class TestSuiteAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly CircuitStateStorageService _storageService;
    private readonly ILogger<TestSuiteAuthService> _logger;

    public TestSuiteAuthService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CircuitStateStorageService storageService,
        ILogger<TestSuiteAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("WildwoodAPI");
            var appId = _configuration["WildwoodAPI:AppId"] ?? "";

            var loginRequest = new
            {
                email,
                password,
                appId,
                appVersion = "1.0.0",
                platform = "web",
                deviceInfo = "WildwoodComponentsTestSuiteBlazor"
            };

            var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var json = JsonDocument.Parse(content);
                var token = json.RootElement.GetProperty("jwtToken").GetString();

                if (!string.IsNullOrEmpty(token))
                {
                    await _storageService.SetAuthTokenAsync(token);

                    // Also store refresh token if available
                    if (json.RootElement.TryGetProperty("refreshToken", out var refreshToken))
                    {
                        await _storageService.SetItemAsync("wildwood_refresh_token",
                            refreshToken.GetString() ?? "");
                    }

                    return new LoginResult { Success = true, Token = token };
                }

                return new LoginResult { Success = false, Error = "No token in response" };
            }

            // Try to extract error message from response
            try
            {
                var errorJson = JsonDocument.Parse(content);
                if (errorJson.RootElement.TryGetProperty("message", out var msg))
                    return new LoginResult { Success = false, Error = msg.GetString() ?? "Login failed" };
                if (errorJson.RootElement.TryGetProperty("error", out var err))
                    return new LoginResult { Success = false, Error = err.GetString() ?? "Login failed" };
            }
            catch { }

            return new LoginResult { Success = false, Error = $"Login failed ({response.StatusCode})" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login request failed");
            return new LoginResult { Success = false, Error = $"Connection error: {ex.Message}" };
        }
    }

    public async Task LogoutAsync()
    {
        await _storageService.RemoveItemAsync("wildwood_auth_token");
        await _storageService.RemoveItemAsync("wildwood_refresh_token");
    }

    public async Task<string?> GetCurrentTokenAsync()
    {
        return await _storageService.GetAuthTokenAsync();
    }
}

public class LoginResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Error { get; set; }
}
