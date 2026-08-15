<%--
    Wildwood Authentication control.

    Inherits from a compiled type in WildwoodComponents.WebForms and has no CodeFile, so
    the ASP.NET runtime compiles this markup against that base class: there is nothing
    here for you to build.

    Register it once per page and drop it in:

        <%@ Register TagPrefix="ww" TagName="Authentication"
                     Src="~/Controls/Wildwood/AuthenticationControl.ascx" %>

        <ww:Authentication runat="server" ReturnUrl="~/Default.aspx" />

    Customising: copy this file rather than editing it in place. A NuGet update replaces
    files it still recognises and silently keeps ones you changed, so an edited copy here
    would freeze at this version. Point your Src= at the copy and leave Inherits= alone.

    The containers below are <div data-ww-form>, not <form>: an .aspx page already sits
    inside one <form runat="server">, HTML forbids nesting another, and a browser would
    drop the inner tag entirely. wildwood-forms.js gives these divs the submit and
    validation behaviour a form would have.

    Requires Bootstrap 5 CSS (class names only) and, for the provider icons,
    Bootstrap Icons.
--%>
<%@ Control Language="C#" Inherits="WildwoodComponents.WebForms.Controls.AuthenticationControlBase" %>

<div class="ww-auth-component" id="ww-auth-component"
     data-proxy-url="<%= Attr(ResolvedProxyUrl) %>"
     data-return-url="<%= Attr(ResolvedReturnUrl) %>"
     data-allow-registration="<%= Attr(EffectiveAllowRegistration) %>"
     data-enable-2fa="<%= Attr(EffectiveEnableTwoFactor) %>">

    <%-- Error/success message area --%>
    <div id="ww-auth-message" class="ww-alert" style="display:none;"></div>

    <%-- ===== LOGIN ===== --%>
    <div id="ww-login-view" class="ww-auth-view">
        <div class="ww-card">
            <div class="ww-card-header">
                <h2><%= Text(Title) %></h2>
                <p><%= Text(Subtitle) %></p>
            </div>
            <div class="ww-card-body">
                <div id="ww-login-form" data-ww-form role="form">
                    <div class="mb-3">
                        <label for="ww-login-username" class="form-label">Username</label>
                        <input type="text" class="form-control" id="ww-login-username" name="username"
                               autocomplete="username" required />
                        <div class="invalid-feedback">Please enter your username.</div>
                    </div>

                    <div class="mb-3">
                        <label for="ww-login-password" class="form-label">Password</label>
                        <input type="password" class="form-control" id="ww-login-password" name="password"
                               autocomplete="current-password" required />
                        <div class="invalid-feedback">Please enter your password.</div>
                    </div>

                    <div class="mb-3 form-check">
                        <input type="checkbox" class="form-check-input" id="ww-login-remember" name="rememberMe" />
                        <label class="form-check-label" for="ww-login-remember">Remember me</label>
                    </div>

                    <div class="mb-3 d-grid">
                        <%-- type="button": a submit button would post the host page back. --%>
                        <button type="button" data-ww-submit class="btn btn-primary" id="ww-login-submit">
                            <span class="ww-btn-text">Sign in</span>
                            <span class="ww-btn-spinner" style="display:none;">
                                <span class="ww-spinner"></span> Signing in...
                            </span>
                        </button>
                    </div>
                </div>

                <% if (ExternalProviders.Count > 0) { %>
                <hr />
                <p class="text-center text-muted mb-3">Or sign in with</p>
                <div class="d-grid gap-2">
                    <% foreach (var provider in ExternalProviders) { %>
                    <a href="<%= Attr(ExternalLoginUrl(provider)) %>" class="btn btn-outline-secondary">
                        <i class="bi <%= Attr(ExternalProviderIcon(provider)) %>"></i> <%= Text(provider) %>
                    </a>
                    <% } %>
                </div>
                <% } %>

                <div class="mt-3 text-center">
                    <a href="#" id="ww-show-forgot" class="text-decoration-none">Forgot your password?</a>
                </div>

                <% if (EffectiveAllowRegistration) { %>
                <div class="mt-2 text-center">
                    <span class="text-muted">Don't have an account?</span>
                    <a href="#" id="ww-show-register" class="text-decoration-none">Register</a>
                </div>
                <% } %>
            </div>
        </div>
    </div>

    <%-- ===== REGISTER ===== --%>
    <% if (EffectiveAllowRegistration) { %>
    <div id="ww-register-view" class="ww-auth-view" style="display:none;">
        <div class="ww-card">
            <div class="ww-card-header">
                <h2>Create Account</h2>
                <p><%= Text(Subtitle) %></p>
            </div>
            <div class="ww-card-body">
                <div id="ww-register-form" data-ww-form role="form">
                    <div class="row">
                        <div class="col-md-6 mb-3">
                            <label for="ww-reg-firstname" class="form-label">First Name</label>
                            <input type="text" class="form-control" id="ww-reg-firstname" name="firstName" required />
                            <div class="invalid-feedback">First name is required.</div>
                        </div>
                        <div class="col-md-6 mb-3">
                            <label for="ww-reg-lastname" class="form-label">Last Name</label>
                            <input type="text" class="form-control" id="ww-reg-lastname" name="lastName" required />
                            <div class="invalid-feedback">Last name is required.</div>
                        </div>
                    </div>

                    <div class="mb-3">
                        <label for="ww-reg-email" class="form-label">Email</label>
                        <input type="email" class="form-control" id="ww-reg-email" name="email"
                               autocomplete="email" required />
                        <div class="invalid-feedback">Please enter a valid email address.</div>
                    </div>

                    <div class="mb-3">
                        <label for="ww-reg-password" class="form-label">Password</label>
                        <input type="password" class="form-control" id="ww-reg-password" name="password"
                               autocomplete="new-password" required minlength="8" />
                        <div class="invalid-feedback">Password must be at least 8 characters.</div>
                    </div>

                    <div class="mb-3">
                        <label for="ww-reg-confirm" class="form-label">Confirm Password</label>
                        <input type="password" class="form-control" id="ww-reg-confirm" name="confirmPassword"
                               autocomplete="new-password" required />
                        <div class="invalid-feedback">Passwords must match.</div>
                    </div>

                    <div class="mb-3 d-grid">
                        <button type="button" data-ww-submit class="btn btn-primary" id="ww-register-submit">
                            <span class="ww-btn-text">Create Account</span>
                            <span class="ww-btn-spinner" style="display:none;">
                                <span class="ww-spinner"></span> Creating account...
                            </span>
                        </button>
                    </div>
                </div>

                <div class="mt-3 text-center">
                    <span class="text-muted">Already have an account?</span>
                    <a href="#" id="ww-show-login-from-register" class="text-decoration-none">Sign in</a>
                </div>
            </div>
        </div>
    </div>
    <% } %>

    <%-- ===== FORGOT PASSWORD ===== --%>
    <div id="ww-forgot-view" class="ww-auth-view" style="display:none;">
        <div class="ww-card">
            <div class="ww-card-header">
                <h2>Reset Password</h2>
                <p>Enter your email to receive a reset link</p>
            </div>
            <div class="ww-card-body">
                <div id="ww-forgot-form" data-ww-form role="form">
                    <div class="mb-3">
                        <label for="ww-forgot-email" class="form-label">Email</label>
                        <input type="email" class="form-control" id="ww-forgot-email" name="email"
                               autocomplete="email" required />
                        <div class="invalid-feedback">Please enter a valid email address.</div>
                    </div>

                    <div class="mb-3 d-grid">
                        <button type="button" data-ww-submit class="btn btn-primary" id="ww-forgot-submit">
                            <span class="ww-btn-text">Send Reset Link</span>
                            <span class="ww-btn-spinner" style="display:none;">
                                <span class="ww-spinner"></span> Sending...
                            </span>
                        </button>
                    </div>
                </div>

                <div class="mt-3 text-center">
                    <a href="#" id="ww-show-login-from-forgot" class="text-decoration-none">Back to sign in</a>
                </div>
            </div>
        </div>
    </div>

    <%-- ===== TWO-FACTOR ===== --%>
    <div id="ww-2fa-view" class="ww-auth-view" style="display:none;">
        <div class="ww-card">
            <div class="ww-card-header">
                <h2>Two-Factor Authentication</h2>
                <p>Enter the code from your authenticator app</p>
            </div>
            <div class="ww-card-body">
                <div id="ww-2fa-form" data-ww-form role="form">
                    <div class="mb-3">
                        <label for="ww-2fa-code" class="form-label">Verification Code</label>
                        <input type="text" class="form-control" id="ww-2fa-code" name="code"
                               autocomplete="one-time-code" required maxlength="6"
                               pattern="[0-9]{6}" inputmode="numeric" />
                        <div class="invalid-feedback">Please enter a 6-digit code.</div>
                    </div>

                    <div class="mb-3 form-check">
                        <input type="checkbox" class="form-check-input" id="ww-2fa-remember" name="rememberDevice" />
                        <label class="form-check-label" for="ww-2fa-remember">Remember this device</label>
                    </div>

                    <div class="mb-3 d-grid">
                        <button type="button" data-ww-submit class="btn btn-primary" id="ww-2fa-submit">
                            <span class="ww-btn-text">Verify</span>
                            <span class="ww-btn-spinner" style="display:none;">
                                <span class="ww-spinner"></span> Verifying...
                            </span>
                        </button>
                    </div>
                </div>

                <div class="mt-3 text-center">
                    <a href="#" id="ww-show-login-from-2fa" class="text-decoration-none">Back to sign in</a>
                </div>
            </div>
        </div>
    </div>
</div>
