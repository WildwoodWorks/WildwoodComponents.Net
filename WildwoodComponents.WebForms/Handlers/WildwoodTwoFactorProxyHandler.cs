using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using WildwoodComponents.WebForms.Services;

namespace WildwoodComponents.WebForms.Handlers
{
    /// <summary>
    /// Same-origin proxy for the Two-Factor Settings control, exposing the routes
    /// <c>twofactor-settings.js</c> calls and forwarding each to
    /// <see cref="IWildwoodTwoFactorSettingsService"/>.
    /// </summary>
    /// <remarks>
    /// Routes: <c>POST /email/enroll</c>, <c>POST /email/verify</c>,
    /// <c>POST /authenticator/enroll</c>, <c>POST /authenticator/verify</c>,
    /// <c>POST /credentials/{id}/set-primary</c>, <c>DELETE /credentials/{id}</c>,
    /// <c>POST /recovery-codes/regenerate</c>, <c>DELETE /trusted-devices/{id}</c>,
    /// <c>DELETE /trusted-devices</c>.
    /// <para>
    /// Every route requires a signed-in user; the request is refused with 401 when the
    /// session holds no token, rather than being forwarded to fail at the API.
    /// </para>
    /// </remarks>
    public class WildwoodTwoFactorProxyHandler : WildwoodProxyHandlerBase
    {
        /// <inheritdoc />
        protected override async Task<bool> TryHandleAsync(HttpContextBase context, string route, string method)
        {
            if (!WildwoodWebForms.Session.IsAuthenticated)
            {
                await WriteErrorAsync(context, 401, "Sign in to manage two-factor settings.").ConfigureAwait(false);
                return true;
            }

            var service = WildwoodWebForms.TwoFactorSettings;
            var isPost = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
            var isDelete = string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);

            if (isPost)
            {
                if (RouteEquals(route, "/email/enroll"))
                {
                    var body = await ReadJsonAsync<EnrollEmailRequest>(context).ConfigureAwait(false);
                    var result = await service.EnrollEmailAsync(body?.Email).ConfigureAwait(false);
                    if (result == null)
                    {
                        await WriteErrorAsync(context, 502, "Could not start email enrolment.").ConfigureAwait(false);
                        return true;
                    }

                    await WriteJsonAsync(context, new EmailEnrollmentResponse
                    {
                        Success = result.Success,
                        CredentialId = result.CredentialId,
                        Email = result.MaskedEmail,
                        ExpiresIn = result.ExpiresIn,
                        Message = result.Message
                    }).ConfigureAwait(false);
                    return true;
                }

                if (RouteEquals(route, "/email/verify"))
                {
                    var body = await ReadJsonAsync<CredentialCodeRequest>(context).ConfigureAwait(false);
                    if (body == null || body.CredentialId is not { Length: > 0 } || body.Code is not { Length: > 0 })
                    {
                        await WriteErrorAsync(context, 400, "A credential id and code are required.").ConfigureAwait(false);
                        return true;
                    }

                    var ok = await service.VerifyEmailEnrollmentAsync(body.CredentialId, body.Code).ConfigureAwait(false);
                    await WriteSuccessAsync(context, ok, "That code was not accepted.").ConfigureAwait(false);
                    return true;
                }

                if (RouteEquals(route, "/authenticator/enroll"))
                {
                    var body = await ReadJsonAsync<EnrollAuthenticatorRequest>(context).ConfigureAwait(false);
                    var result = await service.BeginAuthenticatorEnrollmentAsync(body?.FriendlyName).ConfigureAwait(false);
                    if (result == null)
                    {
                        await WriteErrorAsync(context, 502, "Could not start authenticator enrolment.").ConfigureAwait(false);
                        return true;
                    }

                    await WriteJsonAsync(context, new AuthenticatorEnrollmentResponse
                    {
                        Success = result.Success,
                        CredentialId = result.CredentialId,
                        QrCodeUri = result.QrCodeDataUrl,
                        ManualEntryKey = result.ManualEntryKey,
                        Secret = result.Secret,
                        Issuer = result.Issuer,
                        AccountName = result.AccountName,
                        Message = result.Message
                    }).ConfigureAwait(false);
                    return true;
                }

                if (RouteEquals(route, "/authenticator/verify"))
                {
                    var body = await ReadJsonAsync<CredentialCodeRequest>(context).ConfigureAwait(false);
                    if (body == null || body.CredentialId is not { Length: > 0 } || body.Code is not { Length: > 0 })
                    {
                        await WriteErrorAsync(context, 400, "A credential id and code are required.").ConfigureAwait(false);
                        return true;
                    }

                    var ok = await service.CompleteAuthenticatorEnrollmentAsync(body.CredentialId, body.Code).ConfigureAwait(false);
                    await WriteSuccessAsync(context, ok, "That code was not accepted.").ConfigureAwait(false);
                    return true;
                }

                if (RouteEquals(route, "/recovery-codes/regenerate"))
                {
                    var result = await service.RegenerateRecoveryCodesAsync().ConfigureAwait(false);
                    if (result == null)
                    {
                        await WriteErrorAsync(context, 502, "Could not regenerate recovery codes.").ConfigureAwait(false);
                        return true;
                    }

                    await WriteJsonAsync(context, new RegenerateRecoveryCodesResponse
                    {
                        Success = result.Success,
                        Codes = result.Codes,
                        TotalCodes = result.TotalCodes,
                        Message = result.Message
                    }).ConfigureAwait(false);
                    return true;
                }

                // POST /credentials/{id}/set-primary
                var credentialId = MatchSegment(route, "/credentials/", "/set-primary");
                if (credentialId != null)
                {
                    var ok = await service.SetPrimaryCredentialAsync(credentialId).ConfigureAwait(false);
                    await WriteSuccessAsync(context, ok, "Could not set that method as primary.").ConfigureAwait(false);
                    return true;
                }

                return false;
            }

            if (isDelete)
            {
                if (RouteEquals(route, "/trusted-devices"))
                {
                    var count = await service.RevokeAllTrustedDevicesAsync().ConfigureAwait(false);
                    await WriteJsonAsync(context, new RevokedResponse { Success = true, Revoked = count }).ConfigureAwait(false);
                    return true;
                }

                var deviceId = MatchSegment(route, "/trusted-devices/", null);
                if (deviceId != null)
                {
                    var ok = await service.RevokeTrustedDeviceAsync(deviceId).ConfigureAwait(false);
                    await WriteSuccessAsync(context, ok, "Could not revoke that device.").ConfigureAwait(false);
                    return true;
                }

                var credentialId = MatchSegment(route, "/credentials/", null);
                if (credentialId != null)
                {
                    var ok = await service.RemoveCredentialAsync(credentialId).ConfigureAwait(false);
                    await WriteSuccessAsync(context, ok, "Could not remove that method.").ConfigureAwait(false);
                    return true;
                }
            }

            return false;
        }

        private static Task WriteSuccessAsync(HttpContextBase context, bool succeeded, string failureMessage)
        {
            if (!succeeded)
            {
                return WriteErrorAsync(context, 400, failureMessage);
            }

            return WriteJsonAsync(context, new SuccessResponse { Success = true });
        }

        private sealed class EnrollEmailRequest
        {
            public string? Email { get; set; }
        }

        private sealed class EnrollAuthenticatorRequest
        {
            public string? FriendlyName { get; set; }
        }

        private sealed class CredentialCodeRequest
        {
            public string? CredentialId { get; set; }
            public string? Code { get; set; }
        }

        private sealed class SuccessResponse
        {
            public bool Success { get; set; }
        }

        // Response shapes named for what twofactor-settings.js actually reads, rather than
        // forwarding the service models: the Shared models carry JsonPropertyName values
        // aimed at the WildwoodAPI contract, and two of them differ from the keys the
        // script looks for (qrCodeDataUrl vs qrCodeUri, maskedEmail vs email).

        private sealed class EmailEnrollmentResponse
        {
            public bool Success { get; set; }
            public string? CredentialId { get; set; }

            /// <summary>The masked address the code went to; read as <c>result.email</c>.</summary>
            public string? Email { get; set; }

            public int ExpiresIn { get; set; }
            public string? Message { get; set; }
        }

        private sealed class AuthenticatorEnrollmentResponse
        {
            public bool Success { get; set; }
            public string? CredentialId { get; set; }

            /// <summary>QR payload; read as <c>result.qrCodeUri</c>.</summary>
            public string? QrCodeUri { get; set; }

            public string? ManualEntryKey { get; set; }
            public string? Secret { get; set; }
            public string? Issuer { get; set; }
            public string? AccountName { get; set; }
            public string? Message { get; set; }
        }

        private sealed class RegenerateRecoveryCodesResponse
        {
            public bool Success { get; set; }
            public List<string>? Codes { get; set; }
            public int TotalCodes { get; set; }
            public string? Message { get; set; }
        }

        private sealed class RevokedResponse
        {
            public bool Success { get; set; }
            public int Revoked { get; set; }
        }
    }
}
