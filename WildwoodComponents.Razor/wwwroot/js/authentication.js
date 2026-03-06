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
    const messageEl = document.getElementById('ww-auth-message');

    // 2FA session state (set by login response when 2FA is required)
    let twoFactorSessionId = null;

    // ===== View switching =====
    const views = {
        login: document.getElementById('ww-login-view'),
        register: document.getElementById('ww-register-view'),
        forgot: document.getElementById('ww-forgot-view'),
        twoFactor: document.getElementById('ww-2fa-view')
    };

    function showView(name) {
        Object.values(views).forEach(v => { if (v) v.style.display = 'none'; });
        if (views[name]) views[name].style.display = 'block';
        hideMessage();
    }

    // Navigation links
    bindClick('ww-show-register', () => showView('register'));
    bindClick('ww-show-forgot', () => showView('forgot'));
    bindClick('ww-show-login-from-register', () => showView('login'));
    bindClick('ww-show-login-from-forgot', () => showView('login'));
    bindClick('ww-show-login-from-2fa', () => showView('login'));

    function bindClick(id, handler) {
        const el = document.getElementById(id);
        if (el) el.addEventListener('click', function (e) { e.preventDefault(); handler(); });
    }

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

    // ===== Form validation =====
    function validateForm(form) {
        if (!form.checkValidity()) {
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

    // ===== Login form =====
    const loginForm = document.getElementById('ww-login-form');
    if (loginForm) {
        loginForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            if (!validateForm(loginForm)) return;

            const btn = document.getElementById('ww-login-submit');
            setLoading(btn, true);
            hideMessage();

            try {
                const result = await postJson('/login', {
                    username: document.getElementById('ww-login-username').value,
                    password: document.getElementById('ww-login-password').value,
                    rememberMe: document.getElementById('ww-login-remember').checked,
                    returnUrl: returnUrl
                });

                if (result.requiresTwoFactor) {
                    twoFactorSessionId = result.twoFactorSessionId;
                    showView('twoFactor');
                } else if (result.success) {
                    window.location.href = result.redirectUrl || returnUrl;
                } else {
                    showMessage(result.message || 'Login failed', 'danger');
                }
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
        registerForm.addEventListener('submit', async function (e) {
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
        forgotForm.addEventListener('submit', async function (e) {
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
        twoFactorForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            if (!validateForm(twoFactorForm)) return;

            const btn = document.getElementById('ww-2fa-submit');
            setLoading(btn, true);
            hideMessage();

            try {
                const result = await postJson('/two-factor-verify', {
                    code: document.getElementById('ww-2fa-code').value,
                    sessionId: twoFactorSessionId,
                    rememberDevice: document.getElementById('ww-2fa-remember').checked
                });

                if (result.success) {
                    window.location.href = result.redirectUrl || returnUrl;
                } else {
                    showMessage(result.message || 'Verification failed', 'danger');
                }
            } catch (err) {
                showMessage(err.message || 'An error occurred. Please try again.', 'danger');
            } finally {
                setLoading(btn, false);
            }
        });
    }
})();
