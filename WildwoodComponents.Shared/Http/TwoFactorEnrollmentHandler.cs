using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WildwoodComponents.Shared.Http;

/// <summary>
/// The app requires two-factor authentication and the signed-in user has not set it up: the API
/// answered 403 with errorCode <see cref="ErrorCode"/>. The user must enroll before anything else works.
/// </summary>
public sealed class TwoFactorEnrollmentRequiredEventArgs : EventArgs
{
    public const string ErrorCode = "TWO_FACTOR_ENROLLMENT_REQUIRED";

    /// <summary>The "sub" of the bearer token the refused request carried (null when it had none).</summary>
    public string? UserId { get; set; }

    public string? RequestUri { get; set; }

    /// <summary>The API's sentence for it.</summary>
    public string? Message { get; set; }
}

/// <summary>
/// Raised for every API response refused because 2FA enrollment is pending. One instance per
/// application: on Blazor Server several users share it, so a subscriber must compare
/// <see cref="TwoFactorEnrollmentRequiredEventArgs.UserId"/> with its own signed-in user.
/// </summary>
public interface IWildwoodApiEvents
{
    event EventHandler<TwoFactorEnrollmentRequiredEventArgs>? TwoFactorEnrollmentRequired;

    void RaiseTwoFactorEnrollmentRequired(TwoFactorEnrollmentRequiredEventArgs args);
}

/// <inheritdoc />
public sealed class WildwoodApiEvents : IWildwoodApiEvents
{
    public event EventHandler<TwoFactorEnrollmentRequiredEventArgs>? TwoFactorEnrollmentRequired;

    public void RaiseTwoFactorEnrollmentRequired(TwoFactorEnrollmentRequiredEventArgs args)
        => TwoFactorEnrollmentRequired?.Invoke(this, args);
}

/// <summary>
/// Watches API responses for the "2FA enrollment required" refusal (403 + errorCode
/// TWO_FACTOR_ENROLLMENT_REQUIRED) and raises <see cref="IWildwoodApiEvents.TwoFactorEnrollmentRequired"/>.
/// The response itself is passed through untouched (its body is buffered so callers can still read it).
/// </summary>
public sealed class TwoFactorEnrollmentHandler : DelegatingHandler
{
    private readonly IWildwoodApiEvents _events;

    public TwoFactorEnrollmentHandler(IWildwoodApiEvents events) => _events = events;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Forbidden || response.Content == null)
            return response;

        // Buffer so the caller can still read the body after we have.
        await response.Content.LoadIntoBufferAsync();
        var body = await response.Content.ReadAsStringAsync();
        if (!TryReadEnrollmentRefusal(body, out var message))
            return response;

        _events.RaiseTwoFactorEnrollmentRequired(new TwoFactorEnrollmentRequiredEventArgs
        {
            UserId = SubjectOf(request.Headers.Authorization),
            RequestUri = request.RequestUri?.ToString(),
            Message = message
        });
        return response;
    }

    public static bool TryReadEnrollmentRefusal(string body, out string? message)
    {
        message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "errorCode", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String
                    && prop.Value.GetString() == TwoFactorEnrollmentRequiredEventArgs.ErrorCode)
                {
                    if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                        message = error.GetString();
                    return true;
                }
            }
        }
        catch (JsonException) { }
        return false;
    }

    /// <summary>The unvalidated "sub" claim of a bearer JWT: a label for routing the event, never trusted.</summary>
    public static string? SubjectOf(AuthenticationHeaderValue? authorization)
    {
        if (authorization == null || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(authorization.Parameter))
            return null;
        var parts = authorization.Parameter.Split('.');
        if (parts.Length < 2)
            return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String ? sub.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}
