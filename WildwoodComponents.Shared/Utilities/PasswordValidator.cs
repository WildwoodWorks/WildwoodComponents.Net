using System.Text.RegularExpressions;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Result of password validation, collecting all errors rather than failing fast.
/// Matches the JS { isValid, errors } return shape from wildwood-core/src/auth/passwordUtils.ts.
/// </summary>
public class PasswordValidationResult
{
    public bool IsValid { get; }
    public List<string> Errors { get; }

    public PasswordValidationResult(bool isValid, List<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }
}

/// <summary>
/// Public, static password validator matching the JS validatePasswordClientSide function
/// in wildwood-core/src/auth/passwordUtils.ts and the private ValidatePassword method
/// in WildwoodComponents.Blazor AuthenticationService.
///
/// Unlike the existing .NET implementation (which fails fast on the first error),
/// this collects ALL validation errors before returning, matching the JS behavior.
/// </summary>
public static class PasswordValidator
{
    private static readonly Regex UppercasePattern = new("[A-Z]", RegexOptions.Compiled);
    private static readonly Regex LowercasePattern = new("[a-z]", RegexOptions.Compiled);
    private static readonly Regex DigitPattern = new(@"\d", RegexOptions.Compiled);
    private static readonly Regex SpecialCharPattern = new("[^a-zA-Z0-9]", RegexOptions.Compiled);

    /// <summary>
    /// Validates a password against the given requirements, collecting all errors.
    /// </summary>
    public static PasswordValidationResult ValidatePassword(
        string password,
        int minLength,
        bool requireUppercase,
        bool requireLowercase,
        bool requireDigit,
        bool requireSpecialChar)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password) || password.Length < minLength)
        {
            errors.Add($"Password must be at least {minLength} characters long.");
        }

        if (requireUppercase && (string.IsNullOrEmpty(password) || !UppercasePattern.IsMatch(password)))
        {
            errors.Add("Password must contain at least one uppercase letter (A-Z).");
        }

        if (requireLowercase && (string.IsNullOrEmpty(password) || !LowercasePattern.IsMatch(password)))
        {
            errors.Add("Password must contain at least one lowercase letter (a-z).");
        }

        if (requireDigit && (string.IsNullOrEmpty(password) || !DigitPattern.IsMatch(password)))
        {
            errors.Add("Password must contain at least one number (0-9).");
        }

        if (requireSpecialChar && (string.IsNullOrEmpty(password) || !SpecialCharPattern.IsMatch(password)))
        {
            errors.Add("Password must contain at least one special character.");
        }

        return new PasswordValidationResult(errors.Count == 0, errors);
    }
}
