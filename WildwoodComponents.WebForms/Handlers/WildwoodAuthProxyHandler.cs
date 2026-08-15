using System;
using System.Threading.Tasks;
using System.Web;
using WildwoodComponents.WebForms.Authentication;
using WildwoodComponents.WebForms.Models;
using WildwoodComponents.WebForms.Services;

namespace WildwoodComponents.WebForms.Handlers
{
    /// <summary>
    /// Same-origin proxy for the Authentication control. Exposes the four routes
    /// <c>authentication.js</c> posts to, forwards each to
    /// <see cref="IWildwoodAuthService"/>, and — on a completed sign-in — issues the Forms
    /// Authentication cookie so the rest of the site sees an authenticated user.
    /// </summary>
    /// <remarks>
    /// Routes, all POST: <c>/login</c>, <c>/register</c>, <c>/forgot-password</c>,
    /// <c>/two-factor-verify</c>.
    /// <para>
    /// A rejected sign-in is answered with HTTP 200 and <c>success: false</c>, not a 4xx:
    /// the script treats any non-OK status as a transport failure and replaces the API's
    /// message with a generic one, so a 401 here would hide "wrong password" from the user.
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
        /// The response shape <c>authentication.js</c> expects: it reads
        /// <c>requiresTwoFactor</c>, <c>twoFactorSessionId</c>, <c>success</c>,
        /// <c>redirectUrl</c> and <c>message</c>.
        /// </summary>
        private sealed class AuthProxyResponse
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public bool RequiresTwoFactor { get; set; }
            public string? TwoFactorSessionId { get; set; }
            public string? RedirectUrl { get; set; }

            public static AuthProxyResponse Fail(string message)
            {
                return new AuthProxyResponse { Success = false, Message = message };
            }
        }
    }
}
