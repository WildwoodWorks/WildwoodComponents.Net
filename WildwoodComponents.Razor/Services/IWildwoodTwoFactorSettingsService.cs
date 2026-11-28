using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public interface IWildwoodTwoFactorSettingsService
{
    Task<TwoFactorUserStatus?> GetStatusAsync();
    Task<List<TwoFactorCredential>> GetCredentialsAsync();
    Task<bool> SetPrimaryCredentialAsync(string credentialId);
    Task<bool> RemoveCredentialAsync(string credentialId);
    Task<EmailEnrollmentResult?> EnrollEmailAsync(string? email = null);
    Task<bool> VerifyEmailEnrollmentAsync(string credentialId, string code);
    Task<AuthenticatorEnrollmentResult?> BeginAuthenticatorEnrollmentAsync(string? friendlyName = null);
    Task<bool> CompleteAuthenticatorEnrollmentAsync(string credentialId, string code);
    Task<RecoveryCodeInfo?> GetRecoveryCodeInfoAsync();
    Task<RegenerateRecoveryCodesResult?> RegenerateRecoveryCodesAsync();
    Task<List<TrustedDevice>> GetTrustedDevicesAsync();
    Task<bool> RevokeTrustedDeviceAsync(string deviceId);
    Task<int> RevokeAllTrustedDevicesAsync();

    /// <summary>
    /// The app's two-factor configuration (anonymous endpoint), or null when it cannot be read.
    /// Drives the "app-not-enforcing" notice: when <c>IsEnabled</c> is false the app's sign-in never
    /// asks for a second factor. Defaults to null so existing implementations keep compiling.
    /// </summary>
    Task<TwoFactorConfiguration?> GetConfigurationAsync() => Task.FromResult<TwoFactorConfiguration?>(null);
}
