namespace WildwoodComponents.Shared.Models;

/// <summary>
/// Known error codes returned by WildwoodAPI auth endpoints.
/// </summary>
public static class AuthErrorCodes
{
    public const string InvalidApplication = "InvalidApplication";
    public const string InvalidCredentials = "InvalidCredentials";
    public const string NotAuthorizedForApplication = "NotAuthorizedForApplication";
    public const string AccountDeactivated = "AccountDeactivated";
    public const string TemporaryPasswordExpired = "TemporaryPasswordExpired";
    public const string UserExists = "USER_EXISTS";

    /// <summary>
    /// The app requires two-factor authentication and the signed-in user has not enrolled: a 403
    /// for every call but enrollment. Same value as <c>TwoFactorEnrollmentRequiredEventArgs.ErrorCode</c>.
    /// </summary>
    public const string TwoFactorEnrollmentRequired = "TWO_FACTOR_ENROLLMENT_REQUIRED";
}
