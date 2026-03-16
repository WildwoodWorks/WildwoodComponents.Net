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
}
