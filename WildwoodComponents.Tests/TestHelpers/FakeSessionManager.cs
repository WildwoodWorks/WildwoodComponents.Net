using System.Net.Http.Headers;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>Minimal in-memory IWildwoodSessionManager for Razor service tests.</summary>
public class FakeSessionManager : IWildwoodSessionManager
{
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime? _expiryUtc;

    public int ApplyAuthorizationHeaderCalls { get; private set; }

    public FakeSessionManager(string? accessToken = "test-jwt")
    {
        _accessToken = accessToken;
    }

    public string? GetAccessToken() => _accessToken;
    public string? GetRefreshToken() => _refreshToken;

    public void SetTokens(string accessToken, string refreshToken)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
    }

    public void SetTokens(string accessToken, string refreshToken, DateTime expiryUtc)
    {
        SetTokens(accessToken, refreshToken);
        _expiryUtc = expiryUtc;
    }

    public string? GetTokenExpiry() => _expiryUtc?.ToString("O");

    public void ClearTokens()
    {
        _accessToken = null;
        _refreshToken = null;
        _expiryUtc = null;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public void ApplyAuthorizationHeader(HttpClient httpClient)
    {
        ApplyAuthorizationHeaderCalls++;
        if (!string.IsNullOrEmpty(_accessToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }
}
