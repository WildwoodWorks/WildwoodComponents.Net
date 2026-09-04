using System;
using System.Threading.Tasks;
using System.Web;
using WildwoodComponents.WebForms.Authentication;
using WildwoodComponents.WebForms.Models;
using WildwoodComponents.WebForms.Services;

namespace WildwoodComponents.WebForms.Handlers
{
    /// <summary>
    /// Same-origin proxy for the Authentication control. Exposes the five routes
    /// <c>authentication.js</c> posts to, forwards each to
    /// <see cref="IWildwoodAuthService"/>, and — on a completed sign-in — issues the Forms
    /// Authentication cookie so the rest of the site sees an authenticated user.
    /// </summary>
    /// <remarks>
    /// Routes, all POST: <c>/login</c>, <c>/register</c>, <c>/forgot-password</c>,
    /// <c>/two-factor-verify</c>, <c>/reset-password</c>.
    /// <para>
    /// A rejected sign-in is answered with HTTP 200 and <c>success: false</c>, not a 4xx:
    /// the script treats any non-OK status as a transport failure and replaces the API's
    /// message with a generic one, so a 401 here would hide "wrong password" from the user.
    /// </para>
    /// <para>
    /// <b>The forced-reset gate.</b> A sign-in whose response carries
    /// <c>RequiresPasswordReset</c> is authenticated but NOT finished: the tokens are stored
    /// (so <c>/reset-password</c> can call an <c>[Authorize]</c> endpoint), but no Forms
    /// Authentication cookie is issued. Issuing one there would sign the user into the site
    /// with a temporary password still in force and no way for the app to know. The cookie is
    /// issued by <c>/reset-password</c> instead, once a real password exists.
    /// </para>
    /// </remarks>
    public class WildwoodAuthProxyHandler : WildwoodProxyHandlerBase
    {
        /// <inheritdoc />
        protected override async Task<bool> TryHandleAsync(HttpContextBase context, string route, string method)
        {
            if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (RouteEquals(route, "/login"))
            {
                await LoginAsync(context).ConfigureAwait(false);
                return true;
            }

            if (RouteEquals(route, "/register"))
            {
                await RegisterAsync(context).ConfigureAwait(false);
                return true;
            }

            if (RouteEquals(route, "/forgot-password"))
            {
                await ForgotPasswordAsync(context).ConfigureAwait(false);
                return true;
            }

            if (RouteEquals(route, "/two-factor-verify"))
            {
                await VerifyTwoFactorAsync(context).ConfigureAwait(false);
                return true;
            }

            if (RouteEquals(route, "/reset-password"))
            {
                await ResetPasswordAsync(context).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        private static async Task LoginAsync(HttpContextBase context)
        {
            var body = await ReadJsonAsync<LoginProxyRequest>(context).ConfigureAwait(false);

            // Pattern form rather than string.IsNullOrEmpty: only this narrows the
            // nullable properties for the assignments below on this target framework.
            if (body == null || body.Username is not { Length: > 0 } || body.Password is not { Length: > 0 })
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail("Username and password are required.")).ConfigureAwait(false);
                return;
            }

            var result = await WildwoodWebForms.Auth.LoginAsync(new LoginRequest
            {
                Username = body.Username,
                Password = body.Password,
                RememberMe = body.RememberMe
            }).ConfigureAwait(false);

            if (result.Succeeded && result.Response != null && result.Response.RequiresTwoFactor)
            {
                // Credentials were right but the sign-in is not finished: no cookie yet.
                await WriteJsonAsync(context, new AuthProxyResponse
                {
                    Success = false,
                    RequiresTwoFactor = true,
                    TwoFactorSessionId = result.Response.TwoFactorSessionId,
                    Message = "Enter the verification code to finish signing in."
                }).ConfigureAwait(false);
                return;
            }

            await CompleteSignInAsync(context, result, body.ReturnUrl, body.RememberMe).ConfigureAwait(false);
        }

        private static async Task RegisterAsync(HttpContextBase context)
        {
            var body = await ReadJsonAsync<RegisterProxyRequest>(context).ConfigureAwait(false);
            if (body == null || body.Email is not { Length: > 0 } || body.Password is not { Length: > 0 })
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail("Email and password are required.")).ConfigureAwait(false);
                return;
            }

            if (body.ConfirmPassword is not { Length: > 0 }
                || !string.Equals(body.Password, body.ConfirmPassword, StringComparison.Ordinal))
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail("Passwords must match.")).ConfigureAwait(false);
                return;
            }

            var result = await WildwoodWebForms.Auth.RegisterAsync(new RegisterRequest
            {
                Email = body.Email,
                Username = body.Username,
                Password = body.Password,
                ConfirmPassword = body.ConfirmPassword,
                FirstName = body.FirstName,
                LastName = body.LastName,
                RegistrationToken = body.RegistrationToken
            }).ConfigureAwait(false);

            await CompleteSignInAsync(context, result, body.ReturnUrl, createPersistentCookie: false).ConfigureAwait(false);
        }

        private static async Task ForgotPasswordAsync(HttpContextBase context)
        {
            var body = await ReadJsonAsync<ForgotPasswordProxyRequest>(context).ConfigureAwait(false);
            if (body == null || string.IsNullOrEmpty(body.Email))
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail("An email address is required.")).ConfigureAwait(false);
                return;
            }

            var result = await WildwoodWebForms.Auth.ForgotPasswordAsync(body.Email!).ConfigureAwait(false);

            // The message is deliberately the same whether or not the address is
            // registered, so this cannot be used to discover who has an account.
            await WriteJsonAsync(context, new AuthProxyResponse
            {
                Success = result.Succeeded,
                Message = result.Message ?? "If an account with that email exists, a reset link has been sent."
            }).ConfigureAwait(false);
        }

        private static async Task VerifyTwoFactorAsync(HttpContextBase context)
        {
            var body = await ReadJsonAsync<TwoFactorProxyRequest>(context).ConfigureAwait(false);
            if (body == null || body.Code is not { Length: > 0 })
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail("A verification code is required.")).ConfigureAwait(false);
                return;
            }

            var result = await WildwoodWebForms.Auth.VerifyTwoFactorAsync(new TwoFactorVerifyRequest
            {
                Code = body.Code,
                SessionId = body.SessionId,
                Method = body.Method,
                RememberDevice = body.RememberDevice
            }).ConfigureAwait(false);

            await CompleteSignInAsync(context, result, body.ReturnUrl, body.RememberDevice).ConfigureAwait(false);
        }

        /// <summary>
        /// Completes a sign-in that stalled on a temporary password: replaces the password, then
        /// issues the auth cookie the login was refused.
        /// </summary>
        /// <remarks>
        /// The reset itself is authenticated by the tokens the temporary-password login already
        /// stored — <c>api/auth/reset-password</c> is <c>[Authorize]</c> and identifies the user
        /// from the JWT alone, so there is no email or user id on the wire.
        /// <para>
        /// The cookie is issued from the SESSION token rather than by signing in again with the
        /// new password. Re-authenticating would be the obvious alternative and is wrong here: it
        /// would re-trigger two-factor for an account that just satisfied it on the way to this
        /// screen. This mirrors the Blazor component, which completes the pending authentication
        /// rather than re-logging in.
        /// </para>
        /// </remarks>
        private static async Task ResetPasswordAsync(HttpContextBase context)
        {
            var body = await ReadJsonAsync<ResetPasswordProxyRequest>(context).ConfigureAwait(false);

            if (body == null || body.NewPassword is not { Length: > 0 })
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail("A new password is required.")).ConfigureAwait(false);
                return;
            }

            if (body.ConfirmPassword is not { Length: > 0 }
                || !string.Equals(body.NewPassword, body.ConfirmPassword, StringComparison.Ordinal))
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail("Passwords must match.")).ConfigureAwait(false);
                return;
            }

            var session = WildwoodWebForms.Session;
            var accessToken = session.GetAccessToken();

            // No token means no temporary-password sign-in happened, so there is nobody to reset.
            // Refusing here keeps this route from being an anonymous password-change surface.
            if (accessToken is not { Length: > 0 })
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail(
                    "Your session has expired. Please sign in again.")).ConfigureAwait(false);
                return;
            }

            var result = await WildwoodWebForms.Auth.ResetPasswordAsync(new ResetPasswordRequest
            {
                NewPassword = body.NewPassword,
                ConfirmPassword = body.ConfirmPassword
            }).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail(
                    result.Message ?? "Password reset failed.")).ConfigureAwait(false);
                return;
            }

            // The password is real now, so the sign-in the login refused can be completed.
            var userName = body.Username;
            if (userName is not { Length: > 0 })
            {
                // The reset succeeded; only the cookie could not be issued. Say so plainly and
                // send the user to sign in with the password they just set, rather than reporting
                // a failure for work that actually landed.
                Logger.Warn("Password reset succeeded but no user name was supplied; no auth cookie was issued.");
                await WriteJsonAsync(context, new AuthProxyResponse
                {
                    Success = false,
                    Message = "Your password has been changed. Please sign in with your new password."
                }).ConfigureAwait(false);
                return;
            }

            WildwoodFormsAuthHelper.IssueAuthCookie(
                context,
                userName,
                accessToken,
                session.GetRefreshToken(),
                session.GetTokenExpiryUtc() ?? DateTime.UtcNow.AddMinutes(15),
                createPersistentCookie: false,
                Logger);

            await WriteJsonAsync(context, new AuthProxyResponse
            {
                Success = true,
                RedirectUrl = ResolveReturnUrl(body.ReturnUrl),
                Message = "Password updated."
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Turns a successful <see cref="AuthResult"/> into a signed-in browser session:
        /// the Forms Authentication cookie carrying a backup of the tokens, plus the
        /// redirect the script follows.
        /// </summary>
        private static async Task CompleteSignInAsync(
            HttpContextBase context,
            AuthResult result,
            string? returnUrl,
            bool createPersistentCookie)
        {
            if (!result.Succeeded || result.Response == null)
            {
                await WriteJsonAsync(context, AuthProxyResponse.Fail(
                    result.ErrorMessage ?? "Sign-in failed.")).ConfigureAwait(false);
                return;
            }

            var response = result.Response;

            // Authenticated, but the sign-in is NOT finished: the account is on a temporary
            // password. The tokens are already in session (LoginAsync stores them for this
            // branch), which is what lets /reset-password call an [Authorize] endpoint — but no
            // auth cookie is issued, because the site must not treat this as a signed-in user
            // until a real password exists.
            if (response.RequiresPasswordReset)
            {
                await WriteJsonAsync(context, new AuthProxyResponse
                {
                    Success = false,
                    RequiresPasswordReset = true,
                    Message = "Please choose a new password to finish signing in."
                }).ConfigureAwait(false);
                return;
            }

            var session = WildwoodWebForms.Session;
            var expiry = session.GetTokenExpiryUtc() ?? DateTime.UtcNow.AddMinutes(15);
            var userName = response.Email ?? response.UserId ?? response.DisplayName;

            if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(response.Token))
            {
                WildwoodFormsAuthHelper.IssueAuthCookie(
                    context,
                    userName!,
                    response.Token,
                    response.RefreshToken,
                    expiry,
                    createPersistentCookie,
                    Logger);
            }
            else
            {
                Logger.Warn("Wildwood sign-in succeeded but returned no user identity; no auth cookie was issued.");
            }

            await WriteJsonAsync(context, new AuthProxyResponse
            {
                Success = true,
                RedirectUrl = ResolveReturnUrl(returnUrl),
                Message = "Signed in."
            }).ConfigureAwait(false);
        }

        // ── Wire shapes ──────────────────────────────────────────────────────────
        // These mirror exactly what wwwroot/js/authentication.js sends and reads.

        private sealed class LoginProxyRequest
        {
            public string? Username { get; set; }
            public string? Password { get; set; }
            public bool RememberMe { get; set; }
            public string? ReturnUrl { get; set; }
        }

        private sealed class RegisterProxyRequest
        {
            public string? Email { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string? ConfirmPassword { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? RegistrationToken { get; set; }
            public string? ReturnUrl { get; set; }
        }

        private sealed class ForgotPasswordProxyRequest
        {
            public string? Email { get; set; }
        }

        private sealed class TwoFactorProxyRequest
        {
            public string? Code { get; set; }
            public string? SessionId { get; set; }
            public string? Method { get; set; }
            public bool RememberDevice { get; set; }
            public string? ReturnUrl { get; set; }
        }

        /// <summary>
        /// The forced-reset POST. <c>Username</c> comes from the login form the script still
        /// holds: the API identifies the user from the JWT, but the Forms Authentication cookie
        /// needs a name, and the session stores tokens only — no identity.
        /// </summary>
        private sealed class ResetPasswordProxyRequest
        {
            public string? Username { get; set; }
            public string? NewPassword { get; set; }
            public string? ConfirmPassword { get; set; }
            public string? ReturnUrl { get; set; }
        }

        /// <summary>
        /// The response shape <c>authentication.js</c> expects: it reads
        /// <c>requiresTwoFactor</c>, <c>twoFactorSessionId</c>, <c>requiresPasswordReset</c>,
        /// <c>success</c>, <c>redirectUrl</c> and <c>message</c>.
        /// </summary>
        private sealed class AuthProxyResponse
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public bool RequiresTwoFactor { get; set; }
            public string? TwoFactorSessionId { get; set; }
            public bool RequiresPasswordReset { get; set; }
            public string? RedirectUrl { get; set; }

            public static AuthProxyResponse Fail(string message)
            {
                return new AuthProxyResponse { Success = false, Message = message };
            }
        }
    }
}
