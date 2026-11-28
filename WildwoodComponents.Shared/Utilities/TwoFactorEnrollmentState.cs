using System;
using System.Text;
using System.Text.Json;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Whether a session is restricted to two-factor enrollment: the app requires 2FA and the user has
/// not set it up. Port of <c>isTwoFactorEnrollmentPending</c> in
/// packages/wildwood-core/src/auth/tokenUtils.ts and of the session manager's
/// <c>updateEnrollmentState</c> rule.
/// </summary>
/// <remarks>
/// Client-side decode only: the API is what enforces the restriction. The response flag is what
/// every auth endpoint returns and the claim is what the API actually enforces, so either one means
/// restricted — which matters on refresh, whose response does not carry the flag.
/// </remarks>
public static class TwoFactorEnrollmentState
{
    /// <summary>JWT claim the API sets on a token restricted to enrollment ("true" when present).</summary>
    public const string PendingClaim = "tfa_enroll_pending";

    /// <summary>Raised from an auth response (or restored session) that is restricted.</summary>
    public const string SourceAuthResponse = "authResponse";

    /// <summary>Raised because the API refused a call with 403 TWO_FACTOR_ENROLLMENT_REQUIRED.</summary>
    public const string SourceApiRefusal = "apiRefusal";

    /// <summary>The response flag OR the token claim.</summary>
    public static bool IsPending(bool requiresTwoFactorEnrollment, string? jwtToken)
    {
        return requiresTwoFactorEnrollment || TokenCarriesPendingClaim(jwtToken);
    }

    /// <summary>Whether the token's payload carries <see cref="PendingClaim"/> as true or "true".</summary>
    public static bool TokenCarriesPendingClaim(string? jwtToken)
    {
        if (string.IsNullOrEmpty(jwtToken)) return false;

        var parts = jwtToken!.Split('.');
        if (parts.Length < 2) return false;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using (var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                if (!doc.RootElement.TryGetProperty(PendingClaim, out var value)) return false;

                return value.ValueKind == JsonValueKind.True
                    || (value.ValueKind == JsonValueKind.String
                        && string.Equals(value.GetString(), "true", StringComparison.Ordinal));
            }
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// The session became restricted to two-factor enrollment. JS <c>twoFactorEnrollmentRequired</c>
/// event payload <c>{ source }</c>.
/// </summary>
public sealed class TwoFactorEnrollmentPendingEventArgs : EventArgs
{
    public TwoFactorEnrollmentPendingEventArgs(string source)
    {
        Source = source;
    }

    /// <summary><see cref="TwoFactorEnrollmentState.SourceAuthResponse"/> or <see cref="TwoFactorEnrollmentState.SourceApiRefusal"/>.</summary>
    public string Source { get; }
}
