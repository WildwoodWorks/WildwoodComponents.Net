using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Service for Two-Factor Authentication self-service operations.
    /// Allows users to manage their own 2FA settings, credentials, devices, and recovery codes.
    /// </summary>
    public interface ITwoFactorSettingsService
    {
        /// <summary>
        /// Sets the API base URL for all requests.
        /// </summary>
        void SetApiBaseUrl(string baseUrl);

        /// <summary>
        /// Sets the authentication token for API requests.
        /// </summary>
        void SetAuthToken(string token);

        // ============================================
        // Status Operations
        // ============================================

        /// <summary>
        /// Gets the current user's 2FA status.
        /// </summary>
        Task<TwoFactorUserStatus> GetStatusAsync();

        // ============================================
        // Credential Management
        // ============================================

        /// <summary>
        /// Gets all 2FA credentials for the current user.
        /// </summary>
        Task<List<TwoFactorCredential>> GetCredentialsAsync();

        /// <summary>
        /// Sets a credential as the primary 2FA method.
        /// </summary>
        Task<bool> SetPrimaryCredentialAsync(string credentialId);

        /// <summary>
        /// Removes a 2FA credential.
        /// </summary>
        Task<bool> RemoveCredentialAsync(string credentialId);

        // ============================================
        // Email 2FA Enrollment
        // ============================================

        /// <summary>
        /// Enrolls the user in email-based 2FA.
        /// </summary>
        Task<EmailEnrollmentResult> EnrollEmailAsync(string? email = null);

        /// <summary>
        /// Verifies email enrollment with the sent code.
        /// </summary>
        Task<bool> VerifyEmailEnrollmentAsync(string credentialId, string code);

        // ============================================
        // Authenticator (TOTP) Enrollment
        // ============================================

        /// <summary>
        /// Begins authenticator app enrollment.
        /// Returns QR code and secret for setup.
        /// </summary>
        Task<AuthenticatorEnrollmentResult> BeginAuthenticatorEnrollmentAsync(string? friendlyName = null);

        /// <summary>
        /// Completes authenticator enrollment by verifying the initial code.
        /// </summary>
        Task<bool> CompleteAuthenticatorEnrollmentAsync(string credentialId, string code);

        // ============================================
        // Recovery Codes
        // ============================================

        /// <summary>
        /// Gets information about the user's recovery codes.
        /// </summary>
        Task<RecoveryCodeInfo> GetRecoveryCodeInfoAsync();

        /// <summary>
        /// Regenerates recovery codes. Invalidates existing codes.
        /// </summary>
        Task<RegenerateRecoveryCodesResult> RegenerateRecoveryCodesAsync();

        // ============================================
        // Trusted Devices
        // ============================================

        /// <summary>
        /// Gets all trusted devices for the current user.
        /// </summary>
        Task<List<TrustedDevice>> GetTrustedDevicesAsync();

        /// <summary>
        /// Revokes a specific trusted device.
        /// </summary>
        Task<bool> RevokeTrustedDeviceAsync(string deviceId);

        /// <summary>
        /// Revokes all trusted devices.
        /// </summary>
        Task<int> RevokeAllTrustedDevicesAsync();
    }
}
