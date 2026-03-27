using System.Text.Json;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Error codes matching the JS WildwoodErrorCode type in wildwood-core/src/client/errors.ts.
/// </summary>
public enum WildwoodErrorCode
{
    InvalidCredentials,
    Unauthorized,
    Forbidden,
    NotFound,
    ValidationError,
    TwoFactorRequired,
    SessionExpired,
    RateLimited,
    ServerError,
    NetworkError,
    Timeout,
    Unknown
}

/// <summary>
/// Typed exception for all Wildwood API errors.
/// .NET equivalent of the JS WildwoodError class in wildwood-core/src/client/errors.ts.
/// </summary>
public class WildwoodException : Exception
{
    public int? Status { get; }
    public WildwoodErrorCode? Code { get; }
    public object? Details { get; }

    public WildwoodException(string message, int? status = null, WildwoodErrorCode? code = null, object? details = null)
        : base(message)
    {
        Status = status;
        Code = code ?? (status.HasValue ? CodeFromStatus(status.Value) : WildwoodErrorCode.Unknown);
        Details = details;
    }

    public WildwoodException(string message, Exception innerException, int? status = null, WildwoodErrorCode? code = null, object? details = null)
        : base(message, innerException)
    {
        Status = status;
        Code = code ?? (status.HasValue ? CodeFromStatus(status.Value) : WildwoodErrorCode.Unknown);
        Details = details;
    }

    /// <summary>
    /// Create a WildwoodException from an HTTP response status code and optional body.
    /// Mirrors the JS WildwoodError.fromResponse factory method.
    /// </summary>
    public static WildwoodException FromHttpResponse(int statusCode, string? body, string? fallbackMessage = null)
    {
        var message = fallbackMessage ?? $"Request failed with status {statusCode}";
        WildwoodErrorCode? code = null;
        object? details = null;

        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                details = body;

                // Extract message: try "message", then "error", then "title"
                if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                {
                    message = msgProp.GetString() ?? message;
                }
                else if (root.TryGetProperty("error", out var errProp) && errProp.ValueKind == JsonValueKind.String)
                {
                    message = errProp.GetString() ?? message;
                }
                else if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    message = titleProp.GetString() ?? message;
                }

                // Extract error code from "error" field
                if (root.TryGetProperty("error", out var errorCodeProp) && errorCodeProp.ValueKind == JsonValueKind.String)
                {
                    code = MapApiCode(errorCodeProp.GetString());
                }

                // Check for two-factor requirement
                if (root.TryGetProperty("requiresTwoFactor", out var tfaProp) && tfaProp.ValueKind == JsonValueKind.True)
                {
                    code = WildwoodErrorCode.TwoFactorRequired;
                }
            }
            catch (JsonException)
            {
                // Body is not valid JSON; keep fallback message and treat body as details
                details = body;
            }
        }

        return new WildwoodException(message, statusCode, code, details);
    }

    private static WildwoodErrorCode CodeFromStatus(int status)
    {
        if (status == 401) return WildwoodErrorCode.Unauthorized;
        if (status == 403) return WildwoodErrorCode.Forbidden;
        if (status == 404) return WildwoodErrorCode.NotFound;
        if (status == 422) return WildwoodErrorCode.ValidationError;
        if (status == 429) return WildwoodErrorCode.RateLimited;
        if (status >= 500) return WildwoodErrorCode.ServerError;
        if (status == 0) return WildwoodErrorCode.NetworkError;
        return WildwoodErrorCode.Unknown;
    }

    private static WildwoodErrorCode? MapApiCode(string? apiError)
    {
        return apiError switch
        {
            nameof(WildwoodErrorCode.InvalidCredentials) => WildwoodErrorCode.InvalidCredentials,
            nameof(WildwoodErrorCode.Unauthorized) => WildwoodErrorCode.Unauthorized,
            nameof(WildwoodErrorCode.Forbidden) => WildwoodErrorCode.Forbidden,
            nameof(WildwoodErrorCode.NotFound) => WildwoodErrorCode.NotFound,
            nameof(WildwoodErrorCode.ValidationError) => WildwoodErrorCode.ValidationError,
            nameof(WildwoodErrorCode.RateLimited) => WildwoodErrorCode.RateLimited,
            _ => null
        };
    }
}
