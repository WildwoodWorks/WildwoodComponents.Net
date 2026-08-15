using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.WebForms.Services
{
    /// <summary>
    /// Two-factor enrolment and device management for the signed-in user. Mirrors the
    /// Razor package's <c>IWildwoodTwoFactorSettingsService</c> and calls the same
    /// endpoints. Every call requires a bearer token.
    /// </summary>
    public interface IWildwoodTwoFactorSettingsService
    {
        /// <summary>The user's overall 2FA state, or null when it cannot be read.</summary>
        Task<TwoFactorUserStatus?> GetStatusAsync(CancellationToken cancellationToken = default);

        /// <summary>Enrolled credentials; empty when none or on failure.</summary>
        Task<List<TwoFactorCredential>> GetCredentialsAsync(CancellationToken cancellationToken = default);

        /// <summary>Makes a credential the default challenge method.</summary>
        Task<bool> SetPrimaryCredentialAsync(string credentialId, CancellationToken cancellationToken = default);

        /// <summary>Removes an enrolled credential.</summary>
        Task<bool> RemoveCredentialAsync(string credentialId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts email enrolment, sending a code to <paramref name="email"/> or to the
        /// account's address when null.
        /// </summary>
        Task<EmailEnrollmentResult?> EnrollEmailAsync(string? email = null, CancellationToken cancellationToken = default);

        /// <summary>Completes email enrolment with the emailed code.</summary>
        Task<bool> VerifyEmailEnrollmentAsync(string credentialId, string code, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts authenticator-app enrolment, returning the shared secret and QR payload.
        /// </summary>
        Task<AuthenticatorEnrollmentResult?> BeginAuthenticatorEnrollmentAsync(string? friendlyName = null, CancellationToken cancellationToken = default);

        /// <summary>Completes authenticator enrolment with a generated code.</summary>
        Task<bool> CompleteAuthenticatorEnrollmentAsync(string credentialId, string code, CancellationToken cancellationToken = default);

        /// <summary>How many recovery codes remain, and when they were issued.</summary>
        Task<RecoveryCodeInfo?> GetRecoveryCodeInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Issues a fresh set of recovery codes, invalidating the previous set. The plain
        /// codes are returned once and are not retrievable afterwards.
        /// </summary>
        Task<RegenerateRecoveryCodesResult?> RegenerateRecoveryCodesAsync(CancellationToken cancellationToken = default);

        /// <summary>Devices currently allowed to skip the second factor.</summary>
        Task<List<TrustedDevice>> GetTrustedDevicesAsync(CancellationToken cancellationToken = default);

        /// <summary>Revokes one trusted device.</summary>
        Task<bool> RevokeTrustedDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>Revokes every trusted device, returning how many were revoked.</summary>
        Task<int> RevokeAllTrustedDevicesAsync(CancellationToken cancellationToken = default);
    }
}
