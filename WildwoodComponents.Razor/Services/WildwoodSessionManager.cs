using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Server-side session manager that stores JWT tokens in HttpContext session.
/// Unlike the Blazor WildwoodSessionManager which uses browser localStorage,
/// this uses server-side session storage appropriate for Razor Pages.
/// Tracks token expiry so IsAuthenticated reflects actual token validity.
/// </summary>
public class WildwoodSessionManager : IWildwoodSessionManager
{
    public const string AccessTokenKey = "WildwoodAPI_AccessToken";
    public const string RefreshTokenKey = "WildwoodAPI_RefreshToken";
    public const string TokenExpiryKey = "WildwoodAPI_TokenExpiry";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<WildwoodSessionManager> _logger;

    public WildwoodSessionManager(
        IHttpContextAccessor httpContextAccessor,
        ILogger<WildwoodSessionManager> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public string? GetAccessToken()
    {
        return _httpContextAccessor.HttpContext?.Session.GetString(AccessTokenKey);
    }

    public string? GetRefreshToken()
    {
        return _httpContextAccessor.HttpContext?.Session.GetString(RefreshTokenKey);
    }

    public string? GetTokenExpiry()
    {
        return _httpContextAccessor.HttpContext?.Session.GetString(TokenExpiryKey);
    }

    public void SetTokens(string accessToken, string refreshToken)
    {
        var expiry = ExtractTokenExpiry(accessToken);
        SetTokens(accessToken, refreshToken, expiry);
    }

    public void SetTokens(string accessToken, string refreshToken, DateTime expiryUtc)
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null)
        {
            _logger.LogWarning("Cannot set tokens: HttpContext.Session is not available");
            return;
        }

        session.SetString(AccessTokenKey, accessToken);
        session.SetString(RefreshTokenKey, refreshToken);
        session.SetString(TokenExpiryKey, expiryUtc.ToString("o"));
        _logger.LogDebug("WildwoodAPI tokens stored in session, expiry: {Expiry}", expiryUtc);
    }

    public void ClearTokens()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null) return;

        session.Remove(AccessTokenKey);
        session.Remove(RefreshTokenKey);
        session.Remove(TokenExpiryKey);
        _logger.LogDebug("WildwoodAPI tokens cleared from session");
    }

    public bool IsAuthenticated
    {
        get
        {
            var token = GetAccessToken();
            if (string.IsNullOrEmpty(token))
                return false;

            var expiryStr = GetTokenExpiry();
            if (string.IsNullOrEmpty(expiryStr))
                return true; // No expiry tracked — assume valid (backward compat)

            if (DateTimeOffset.TryParse(expiryStr, out var offset))
                return DateTime.UtcNow < offset.UtcDateTime;

            if (DateTime.TryParse(expiryStr, out var dt))
                return DateTime.UtcNow < (dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime());

            return true; // Can't parse — fail open
        }
    }

    public void ApplyAuthorizationHeader(HttpClient httpClient)
    {
        var token = GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    /// <summary>
    /// Extracts the expiry time from a JWT token's 'exp' claim.
    /// Falls back to 15 minutes from now if parsing fails.
    /// </summary>
    private DateTime ExtractTokenExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return DateTime.UtcNow.AddMinutes(15);

            var payload = parts[1];
            var padding = 4 - (payload.Length % 4);
            if (padding < 4)
                payload += new string('=', padding);
            payload = payload.Replace('-', '+').Replace('_', '/');

            var jsonBytes = Convert.FromBase64String(payload);
            var jsonText = Encoding.UTF8.GetString(jsonBytes);
            var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonText);

            if (json != null && json.TryGetValue("exp", out var expValue))
            {
                return DateTimeOffset.FromUnixTimeSeconds(expValue.GetInt64()).UtcDateTime;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract token expiry from JWT");
        }

        return DateTime.UtcNow.AddMinutes(15);
    }
}
