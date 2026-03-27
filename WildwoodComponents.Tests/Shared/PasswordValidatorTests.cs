using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

public class PasswordValidatorTests
{
    [Fact]
    public void ValidatePassword_ValidPassword_ReturnsIsValidTrue()
    {
        var result = PasswordValidator.ValidatePassword("StrongP@ss1", 8, true, true, true, true);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidatePassword_TooShort_ReturnsLengthError()
    {
        var result = PasswordValidator.ValidatePassword("Ab1!", 8, false, false, false, false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least 8 characters"));
    }

    [Fact]
    public void ValidatePassword_MissingUppercase_ReturnsUppercaseError()
    {
        var result = PasswordValidator.ValidatePassword("lowercase1!", 8, true, false, false, false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("uppercase letter"));
    }

    [Fact]
    public void ValidatePassword_MissingLowercase_ReturnsLowercaseError()
    {
        var result = PasswordValidator.ValidatePassword("UPPERCASE1!", 8, false, true, false, false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("lowercase letter"));
    }

    [Fact]
    public void ValidatePassword_MissingDigit_ReturnsDigitError()
    {
        var result = PasswordValidator.ValidatePassword("NoDigits!", 8, false, false, true, false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("number"));
    }

    [Fact]
    public void ValidatePassword_MissingSpecialChar_ReturnsSpecialCharError()
    {
        var result = PasswordValidator.ValidatePassword("NoSpecial1", 8, false, false, false, true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("special character"));
    }

    [Fact]
    public void ValidatePassword_MultipleViolations_ReturnsAllErrors()
    {
        // Short, no uppercase, no digit, no special char
        var result = PasswordValidator.ValidatePassword("ab", 8, true, false, true, true);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3, "Expected at least 3 errors for multiple violations");
        Assert.Contains(result.Errors, e => e.Contains("at least 8 characters"));
        Assert.Contains(result.Errors, e => e.Contains("uppercase letter"));
        Assert.Contains(result.Errors, e => e.Contains("number"));
        Assert.Contains(result.Errors, e => e.Contains("special character"));
    }
}
