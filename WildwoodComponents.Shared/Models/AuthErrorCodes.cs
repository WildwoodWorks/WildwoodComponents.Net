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
    public const string UserExists = "USER_EXISTS";
}
