using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.WebForms.Logging;
using WildwoodComponents.WebForms.Session;

namespace WildwoodComponents.WebForms.Services
{
    /// <summary>
    /// Calls the WildwoodAPI two-factor settings endpoints. Endpoint paths match the Razor
    /// package's <c>WildwoodTwoFactorSettingsService</c> exactly.
    /// </summary>
    public sealed class WildwoodTwoFactorSettingsService : WildwoodServiceBase, IWildwoodTwoFactorSettingsService
    {
        /// <summary>
        /// Reads the leading count out of the revoke-all response message, whose shape is
        /// <c>{ "message": "3 device(s) revoked" }</c>.
        /// </summary>
        private static readonly Regex LeadingCount = new Regex(@"^(\d+)", RegexOptions.Compiled);

        /// <summary>Creates the service.</summary>
        /// <param name="httpClient">The shared client.</param>
        /// <param name="sessionManager">Supplies the bearer token.</param>
        /// <param name="logger">Diagnostics sink.</param>
        /// <param name="appId">The Wildwood app id.</param>
        public WildwoodTwoFactorSettingsService(
            HttpClient httpClient,
            IWildwoodSessionManager sessionManager,
            IWildwoodLogger? logger,
            string? appId)
            : base(httpClient, sessionManager, logger, appId)
        {
        }

        /// <inheritdoc />
        public Task<TwoFactorUserStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return SendForResultAsync<TwoFactorUserStatus>(HttpMethod.Get, "twofactor/status", null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<List<TwoFactorCredential>> GetCredentialsAsync(CancellationToken cancellationToken = default)
        {
            var credentials = await SendForResultAsync<List<TwoFactorCredential>>(
                HttpMethod.Get, "twofactor/credentials", null, cancellationToken).ConfigureAwait(false);

            return credentials ?? new List<TwoFactorCredential>();
        }

        /// <inheritdoc />
        public Task<bool> SetPrimaryCredentialAsync(string credentialId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(credentialId)) throw new ArgumentException("Credential id is required.", nameof(credentialId));

            // Interpolated rather than concatenated so the parity checker extracts the same
            // normalised path the other stacks produce for this call.
            return SendForSuccessAsync(
                HttpMethod.Put,
                $"twofactor/credentials/{Uri.EscapeDataString(credentialId)}/primary",
                null,
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> RemoveCredentialAsync(string credentialId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(credentialId)) throw new ArgumentException("Credential id is required.", nameof(credentialId));

            return SendForSuccessAsync(
                HttpMethod.Delete,
                $"twofactor/credentials/{Uri.EscapeDataString(credentialId)}",
                null,
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<EmailEnrollmentResult?> EnrollEmailAsync(string? email = null, CancellationToken cancellationToken = default)
        {
            return SendForResultAsync<EmailEnrollmentResult>(
                HttpMethod.Post,
                "twofactor/enroll/email",
                new EmailEnrollmentRequest { Email = email },
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> VerifyEmailEnrollmentAsync(string credentialId, string code, CancellationToken cancellationToken = default)
        {
            return SendForSuccessAsync(
                HttpMethod.Post,
                "twofactor/enroll/email/verify",
                new CredentialCodeRequest { CredentialId = credentialId, Code = code },
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<AuthenticatorEnrollmentResult?> BeginAuthenticatorEnrollmentAsync(string? friendlyName = null, CancellationToken cancellationToken = default)
        {
            return SendForResultAsync<AuthenticatorEnrollmentResult>(
                HttpMethod.Post,
                "twofactor/enroll/authenticator",
                new AuthenticatorEnrollmentRequest { FriendlyName = friendlyName },
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> CompleteAuthenticatorEnrollmentAsync(string credentialId, string code, CancellationToken cancellationToken = default)
        {
            return SendForSuccessAsync(
                HttpMethod.Post,
                "twofactor/enroll/authenticator/verify",
                new CredentialCodeRequest { CredentialId = credentialId, Code = code },
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<RecoveryCodeInfo?> GetRecoveryCodeInfoAsync(CancellationToken cancellationToken = default)
        {
            return SendForResultAsync<RecoveryCodeInfo>(HttpMethod.Get, "twofactor/recovery-codes/info", null, cancellationToken);
        }

        /// <inheritdoc />
        public Task<RegenerateRecoveryCodesResult?> RegenerateRecoveryCodesAsync(CancellationToken cancellationToken = default)
        {
            return SendForResultAsync<RegenerateRecoveryCodesResult>(
                HttpMethod.Post, "twofactor/recovery-codes/regenerate", null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<List<TrustedDevice>> GetTrustedDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = await SendForResultAsync<List<TrustedDevice>>(
                HttpMethod.Get, "twofactor/trusted-devices", null, cancellationToken).ConfigureAwait(false);

            return devices ?? new List<TrustedDevice>();
        }

        /// <inheritdoc />
        public Task<bool> RevokeTrustedDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("Device id is required.", nameof(deviceId));

            return SendForSuccessAsync(
                HttpMethod.Delete,
                $"twofactor/trusted-devices/{Uri.EscapeDataString(deviceId)}",
                null,
                cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> RevokeAllTrustedDevicesAsync(CancellationToken cancellationToken = default)
        {
            var result = await SendForResultAsync<Dictionary<string, string>>(
                HttpMethod.Delete, "twofactor/trusted-devices", null, cancellationToken).ConfigureAwait(false);

            if (result == null || !result.TryGetValue("message", out var message) || message == null)
            {
                return 0;
            }

            var match = LeadingCount.Match(message);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
            {
                return count;
            }

            return 0;
        }

        // Named request shapes rather than anonymous types: the base serializer applies
        // camelCase naming, and a named type keeps the wire contract greppable.

        private sealed class EmailEnrollmentRequest
        {
            public string? Email { get; set; }
        }

        private sealed class AuthenticatorEnrollmentRequest
        {
            public string? FriendlyName { get; set; }
        }

        private sealed class CredentialCodeRequest
        {
            public string? CredentialId { get; set; }

            public string? Code { get; set; }
        }
    }
}
