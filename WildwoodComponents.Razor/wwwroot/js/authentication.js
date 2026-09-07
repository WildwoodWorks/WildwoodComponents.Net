/**
 * WildwoodComponents.Razor - Authentication Component JavaScript
 *
 * Handles client-side state transitions and AJAX calls for the
 * AuthenticationViewComponent. Communicates with the consuming app's
 * proxy endpoints (not directly with WildwoodAPI).
 */
(function () {
    'use strict';

    const component = document.getElementById('ww-auth-component');
    if (!component) return;

    const proxyUrl = component.dataset.proxyUrl;
    const returnUrl = component.dataset.returnUrl || '/';
    // Host-owned sign-up: when set, the Register link navigates there instead of toggling to the
    // in-component register view. The link is rendered only when registration is allowed, so a
    // hidden sign-up stays hidden regardless of this value.
    const registerUrl = component.dataset.registerUrl || '';
    const messageEl = document.getElementById('ww-auth-message');

    // 2FA session state (set by login response when 2FA is required)
    let twoFactorSessionId = null;

    // Carried from the login form so the forced-reset POST can name the user it is
    // finishing sign-in for. The server has the token; it does not have the identity.
    let pendingUsername = null;

    // ===== View switching =====
    const views = {
        login: document.getElementById('ww-login-view'),
        register: document.getElementById('ww-register-view'),
        forgot: document.getElementById('ww-forgot-view'),
        twoFactor: document.getElementById('ww-2fa-view'),
        reset: document.getElementById('ww-reset-view')
    };

    function showView(name) {
        Object.values(views).forEach(v => { if (v) v.style.display = 'none'; });
        if (views[name]) views[name].style.display = 'block';
        hideMessage();
    }

    // Navigation links
    bindClick('ww-show-register', () => {
        if (registerUrl) {
            window.location.href = registerUrl;
            return;
        }
        showView('register');
    });
    bindClick('ww-show-forgot', () => showView('forgot'));
    bindClick('ww-show-login-from-register', () => showView('login'));
    bindClick('ww-show-login-from-forgot', () => showView('login'));
    bindClick('ww-show-login-from-2fa', () => showView('login'));

    function bindClick(id, handler) {
        const el = document.getElementById(id);
        if (el) el.addEventListener('click', function (e) { e.preventDefault(); handler(); });
    }

    // ===== Password visibility toggles =====
    // Same affordance as the React (.ww-password-toggle) and Blazor (.password-toggle) components.
    // type="button" without data-ww-submit, so wildwood-forms.js never treats one as the submitter;
    // flipping type= leaves constraint validation and autocomplete untouched.
    component.querySelectorAll('[data-ww-password-toggle]').forEach(function (button) {
        button.addEventListener('click', function (e) {
            e.preventDefault();
            const input = document.getElementById(button.getAttribute('data-ww-password-toggle'));
            if (!input) return;
            const reveal = input.type === 'password';
            input.type = reveal ? 'text' : 'password';
            button.setAttribute('aria-pressed', reveal ? 'true' : 'false');
            button.setAttribute('aria-label', reveal ? 'Hide password' : 'Show password');
        });
    });

    // ===== Message display =====
    function showMessage(text, type) {
        messageEl.textContent = text;
        messageEl.className = 'ww-alert ww-alert-' + type;
        messageEl.style.display = 'block';
    }

    function hideMessage() {
        messageEl.style.display = 'none';
    }

    // ===== Loading state =====
    function setLoading(button, loading) {
        const textEl = button.querySelector('.ww-btn-text');
        const spinnerEl = button.querySelector('.ww-btn-spinner');
        if (textEl) textEl.style.display = loading ? 'none' : 'inline';
        if (spinnerEl) spinnerEl.style.display = loading ? 'inline' : 'none';
        button.disabled = loading;
    }

    // ===== Form helpers =====
    // The containers below are <form> elements everywhere except classic WebForms, where
    // a nested form is illegal and they render as <div data-ww-form>. wildwood-forms.js,
    // when the page loads it, provides the submit/validate semantics a form would have.
    // Without it these fall through to the native calls, unchanged.
    function bindSubmit(container, handler) {
        if (window.WildwoodForms) {
            window.WildwoodForms.onSubmit(container, handler);
        } else {
            container.addEventListener('submit', handler);
        }
    }

    function validateForm(form) {
        var valid = window.WildwoodForms
            ? window.WildwoodForms.checkValidity(form)
            : form.checkValidity();

        if (!valid) {
            form.classList.add('was-validated');
            return false;
        }
        return true;
    }

    // ===== API calls =====
    async function postJson(endpoint, data) {
        let response;
        try {
            response = await fetch(proxyUrl + endpoint, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
        } catch (networkError) {
            throw new Error('Unable to reach authentication service. Please check your connection.');
        }

        if (!response.ok) {
            if (response.status >= 500) {
                throw new Error('Authentication service is temporarily unavailable. Please try again in a moment.');
            }
            throw new Error('Request failed. Please try again.');
        }

        return await response.json();
    }

    // ===== Post-credential routing =====
    // Login and two-factor verification both land here, because both can come back needing a
    // forced password change: a temporary password with 2FA enabled surfaces the flag only after
    // the code is verified. Handling it in one place is what keeps the two paths in step.
    //
    // The order matters. requiresTwoFactor is checked first because that sign-in is not finished
    // and no token exists yet; requiresPasswordReset carries a real token and must be dealt with
    // before success, or the user is signed in without ever changing the temporary password.
    function routeAuthResult(result) {
        if (result.requiresTwoFactor) {
            twoFactorSessionId = result.twoFactorSessionId;
            showView('twoFactor');
            return;
        }

        if (result.requiresPasswordReset) {
            showView('reset');
            return;
        }

        if (result.success) {
            window.location.href = result.redirectUrl || returnUrl;
            return;
        }

        showMessage(result.message || 'Sign-in failed', 'danger');
    }

    // ===== Login form =====
    const loginForm = document.getElementById('ww-login-form');
    if (loginForm) {
        bindSubmit(loginForm, async function (e) {
            e.preventDefault();
            if (!validateForm(loginForm)) return;

            const btn = document.getElementById('ww-login-submit');
            setLoading(btn, true);
            hideMessage();

            try {
                pendingUsername = document.getElementById('ww-login-username').value;

                const result = await postJson('/login', {
                    username: pendingUsername,
                    password: document.getElementById('ww-login-password').value,
                    rememberMe: document.getElementById('ww-login-remember').checked,
                    returnUrl: returnUrl
                });

                routeAuthResult(result);
            } catch (err) {
                showMessage(err.message || 'An error occurred. Please try again.', 'danger');
            } finally {
                setLoading(btn, false);
            }
        });
    }

    // ===== Register form =====
    const registerForm = document.getElementById('ww-register-form');
    if (registerForm) {
        bindSubmit(registerForm, async function (e) {
            e.preventDefault();

            const password = document.getElementById('ww-reg-password').value;
            const confirm = document.getElementById('ww-reg-confirm').value;
            if (password !== confirm) {
                document.getElementById('ww-reg-confirm').setCustomValidity('Passwords must match');
            } else {
                document.getElementById('ww-reg-confirm').setCustomValidity('');
            }

            if (!validateForm(registerForm)) return;

            const btn = document.getElementById('ww-register-submit');
            setLoading(btn, true);
            hideMessage();

            try {
                const result = await postJson('/register', {
                    email: document.getElementById('ww-reg-email').value,
                    firstName: document.getElementById('ww-reg-firstname').value,
                    lastName: document.getElementById('ww-reg-lastname').value,
                    password: password,
                    confirmPassword: confirm,
                    returnUrl: returnUrl
                });

                if (result.success) {
                    window.location.href = result.redirectUrl || returnUrl;
                } else {
                    showMessage(result.message || 'Registration failed', 'danger');
                }
            } catch (err) {
                showMessage(err.message || 'An error occurred. Please try again.', 'danger');
            } finally {
                setLoading(btn, false);
            }
        });
    }

    // ===== Forgot password form =====
    const forgotForm = document.getElementById('ww-forgot-form');
    if (forgotForm) {
        bindSubmit(forgotForm, async function (e) {
            e.preventDefault();
            if (!validateForm(forgotForm)) return;

            const btn = document.getElementById('ww-forgot-submit');
            setLoading(btn, true);
            hideMessage();

            try {
                const result = await postJson('/forgot-password', {
                    email: document.getElementById('ww-forgot-email').value
                });

                showMessage(result.message || 'If an account with that email exists, a reset link has been sent.', 'success');
            } catch (err) {
                showMessage(err.message || 'An error occurred. Please try again.', 'danger');
            } finally {
                setLoading(btn, false);
            }
        });
    }

    // ===== Two-factor form =====
    const twoFactorForm = document.getElementById('ww-2fa-form');
    if (twoFactorForm) {
        bindSubmit(twoFactorForm, async function (e) {
            e.preventDefault();
            if (!validateForm(twoFactorForm)) return;

            const btn = document.getElementById('ww-2fa-submit');
            setLoading(btn, true);
            hideMessage();

            try {
                const result = await postJson('/two-factor-verify', {
                    code: document.getElementById('ww-2fa-code').value,
                    sessionId: twoFactorSessionId,
                    rememberDevice: document.getElementById('ww-2fa-remember').checked,
                    returnUrl: returnUrl
                });

                // Not a plain success check: a temporary password combined with 2FA only
                // surfaces requiresPasswordReset once the code has been verified.
                routeAuthResult(result);
            } catch (err) {
                showMessage(err.message || 'An error occurred. Please try again.', 'danger');
            } finally {
                setLoading(btn, false);
            }
        });
    }

    // ===== Forced password reset =====
    // Reached only from routeAuthResult, never from a navigation link: the user signed in with a
    // temporary password and cannot proceed until it is replaced. The credentials are already
    // authenticated at this point, so the server holds a real token and the reset call it makes
    // is an authenticated one.
    const resetForm = document.getElementById('ww-reset-form');
    if (resetForm) {
        bindSubmit(resetForm, async function (e) {
            e.preventDefault();

            const passwordEl = document.getElementById('ww-reset-password');
            const confirmEl = document.getElementById('ww-reset-confirm');
            confirmEl.setCustomValidity(passwordEl.value !== confirmEl.value ? 'Passwords must match' : '');

            if (!validateForm(resetForm)) return;

            const btn = document.getElementById('ww-reset-submit');
            setLoading(btn, true);
            hideMessage();

            try {
                const result = await postJson('/reset-password', {
                    username: pendingUsername,
                    newPassword: passwordEl.value,
                    confirmPassword: confirmEl.value,
                    returnUrl: returnUrl
                });

                if (result.success) {
                    window.location.href = result.redirectUrl || returnUrl;
                } else {
                    showMessage(result.message || 'Password reset failed', 'danger');
                }
            } catch (err) {
                showMessage(err.message || 'An error occurred. Please try again.', 'danger');
            } finally {
                setLoading(btn, false);
            }
        });
    }
})();
