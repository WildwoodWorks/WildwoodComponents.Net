using System.Text;
using System.Text.Json;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Consolidated utility for parsing token expiry values.
/// Handles ISO 8601 strings, DateTime strings, and JWT exp claims.
/// iOS-compatible: uses manual parsing, no LINQ.
/// </summary>
public static class TokenExpiryParser
{
    /// <summary>
    /// Parses a token expiry string into a UTC DateTime.
    /// Supports DateTimeOffset (ISO 8601 with timezone) and DateTime formats.
    /// Returns false if the string cannot be parsed.
    /// </summary>
    public static bool TryParseUtc(string? expiryStr, out DateTime utcExpiry)
    {
        utcExpiry = DateTime.MinValue;

        if (string.IsNullOrEmpty(expiryStr))
            return false;

        if (DateTimeOffset.TryParse(expiryStr, out var offset))
        {
            utcExpiry = offset.UtcDateTime;
            return true;
        }

        if (DateTime.TryParse(expiryStr, out var dt))
        {
            utcExpiry = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the token expiry string represents an expired token.
    /// Returns false if the string cannot be parsed (fail-open: don't reject on parse failure).
    /// </summary>
    public static bool IsExpired(string? expiryStr)
    {
        return TryParseUtc(expiryStr, out var utc) && DateTime.UtcNow >= utc;
    }

    /// <summary>
    /// Returns true if the token expiry string represents a still-valid token.
    /// Returns false if the string cannot be parsed or the token is expired.
    /// </summary>
    public static bool IsValid(string? expiryStr)
    {
        return TryParseUtc(expiryStr, out var utc) && DateTime.UtcNow < utc;
    }

    /// <summary>
    /// Extracts the expiration time from a JWT token by decoding the payload.
    /// Does not validate the token signature - only reads the exp claim.
    /// iOS-compatible: uses manual string operations instead of LINQ.
    /// </summary>
    public static DateTime? GetJwtExpiration(string? jwtToken)
    {
        if (string.IsNullOrEmpty(jwtToken))
            return null;

        // JWT format: header.payload.signature
        // Split manually (no LINQ - iOS compatible)
        var firstDot = jwtToken.IndexOf('.');
        if (firstDot < 0)
            return null;

        var secondDot = jwtToken.IndexOf('.', firstDot + 1);
        if (secondDot < 0)
            return null;

        var payload = jwtToken.Substring(firstDot + 1, secondDot - firstDot - 1);

        // Base64Url decode: replace URL-safe chars and add padding
        payload = payload.Replace('-', '+').Replace('_', '/');
        var remainder = payload.Length % 4;
        if (remainder == 2)
            payload += "==";
        else if (remainder == 3)
            payload += "=";

        byte[] payloadBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(payload);
        }
        catch
        {
            return null;
        }

        var payloadJson = Encoding.UTF8.GetString(payloadBytes);

        // Parse the exp claim from JSON
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("exp", out var expElement))
            {
                var expUnix = expElement.GetInt64();
                return DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
            }
        }
        catch
        {
            // JSON parsing failed
        }

        return null;
    }

    /// <summary>
    /// Returns the remaining lifetime of a JWT token, or null if the token
    /// is invalid or already expired.
    /// </summary>
    public static TimeSpan? GetRemainingLifetime(string? jwtToken)
    {
        var expiration = GetJwtExpiration(jwtToken);
        if (expiration == null)
            return null;

        var remaining = expiration.Value - DateTime.UtcNow;
        return remaining.TotalSeconds > 0 ? remaining : null;
    }
}
