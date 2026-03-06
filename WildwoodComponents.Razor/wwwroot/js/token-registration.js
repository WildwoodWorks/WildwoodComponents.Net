/**
 * WildwoodComponents.Razor - Token Registration Component
 * Client-side logic for multi-step registration with token validation,
 * disclaimer acceptance, payment integration, and auto-login.
 * Razor Pages equivalent of Blazor TokenRegistrationComponent.
 */
(function () {
    'use strict';

    var instances = {};

    function RegistrationInstance(root) {
        this.root = root;
        this.cid = root.dataset.componentId;
        this.appId = root.dataset.appId || '';
        this.proxyBase = root.dataset.proxyBase || '/api/wildwood-registration';
        this.paymentProxyBase = root.dataset.paymentProxyBase || '';
        this.autoLogin = root.dataset.autoLogin === 'true';
        this.redirectUrl = root.dataset.redirectUrl || '';
        this.allowToken = root.dataset.allowToken === 'true';
        this.allowOpen = root.dataset.allowOpen === 'true';
        this.tokenRequired = root.dataset.tokenRequired === 'true';
        this.tokenOptional = root.dataset.tokenOptional === 'true';
        this.defaultPricingId = root.dataset.defaultPricingId || '';

        // State
        this.currentStep = 1;
        this.tokenValidated = false;
        this.tokenValue = '';
        this.tokenInfo = null;
        this.useToken = false;
        this.requiresPayment = false;
        this.paymentComplete = false;
        this.paymentSkipped = false;
        this.registrationPending = false;
        this.registrationSuccessful = false;
        this.registrationResponse = null;
        this.validationResponse = null;
        this.pricingDetails = null;
        this.disclaimers = null;
        this.disclaimerAcceptances = null;
        this.pendingPaymentTransactionId = null;
        this.pendingPaymentIntentId = null;
        this.showPassword = false;
        this.isLoading = false;

        // Parse default pricing if provided
        var dpStr = root.dataset.defaultPricing;
        if (dpStr) {
            try { this.pricingDetails = JSON.parse(dpStr); } catch (e) { /* ignore */ }
            if (this.pricingDetails && this.pricingDetails.priceAmount > 0) {
                this.requiresPayment = true;
            }
        }

        // Cache DOM elements
        this.els = {
            steps: root.querySelector('.ww-reg-steps'),
            stepToken: root.querySelector('.ww-step-token'),
            connectorToken: root.querySelector('.ww-connector-token'),
            stepAccount: root.querySelector('.ww-step-account'),
            connectorPayment: root.querySelector('.ww-connector-payment'),
            stepPayment: root.querySelector('.ww-step-payment'),
            accountStepNum: root.querySelector('.ww-account-step-num'),
            paymentStepNum: root.querySelector('.ww-payment-step-num'),
            tokenStep: root.querySelector('.ww-reg-token-step'),
            formStep: root.querySelector('.ww-reg-form-step'),
            disclaimerStep: root.querySelector('.ww-reg-disclaimer-step'),
            paymentStep: root.querySelector('.ww-reg-payment-step'),
            successStep: root.querySelector('.ww-reg-success-step'),
            tokenInput: root.querySelector('.ww-token-input'),
            tokenError: root.querySelector('.ww-token-error'),
            validateTokenBtn: root.querySelector('.ww-validate-token-btn'),
            tokenSpinner: root.querySelector('.ww-token-spinner'),
            tokenBtnText: root.querySelector('.ww-token-btn-text'),
            optTokenCard: root.querySelector('.ww-optional-token-card'),
            optTokenInput: root.querySelector('.ww-optional-token-input'),
            applyTokenBtn: root.querySelector('.ww-apply-token-btn'),
            optTokenSpinner: root.querySelector('.ww-opt-token-spinner'),
            optTokenText: root.querySelector('.ww-opt-token-text'),
            optTokenError: root.querySelector('.ww-opt-token-error'),
            optTokenSuccess: root.querySelector('.ww-opt-token-success'),
            tokenInfoBox: root.querySelector('.ww-token-info'),
            tokenInfoContent: root.querySelector('.ww-token-info-content'),
            clearTokenBtn: root.querySelector('.ww-clear-token-btn'),
            regForm: root.querySelector('.ww-registration-form'),
            regSubtitle: root.querySelector('.ww-reg-subtitle'),
            regError: root.querySelector('.ww-reg-error'),
            regErrorMsg: root.querySelector('.ww-reg-error-msg'),
            registerBtn: root.querySelector('.ww-register-btn'),
            regSpinner: root.querySelector('.ww-reg-spinner'),
            regBtnText: root.querySelector('.ww-reg-btn-text'),
            togglePasswordBtn: root.querySelector('.ww-toggle-password-btn'),
            passwordReqs: root.querySelector('.ww-password-requirements'),
            useDiffTokenBtn: root.querySelector('.ww-use-different-token-btn'),
            cancelBtn: root.querySelector('.ww-cancel-btn'),
            disclaimersList: root.querySelector('.ww-disclaimers-list'),
            acceptDisclaimersBtn: root.querySelector('.ww-accept-disclaimers-btn'),
            cancelDisclaimersBtn: root.querySelector('.ww-cancel-disclaimers-btn'),
            backToAccountBtn: root.querySelector('.ww-back-to-account-btn'),
            paymentSubtitle: root.querySelector('.ww-payment-subtitle'),
            pricingPlanName: root.querySelector('.ww-pricing-plan-name'),
            subscriptionBadge: root.querySelector('.ww-subscription-badge'),
            priceAmount: root.querySelector('.ww-price-amount'),
            pricePeriod: root.querySelector('.ww-price-period'),
            pricingDescription: root.querySelector('.ww-pricing-description'),
            subscriptionInfo: root.querySelector('.ww-subscription-info'),
            paymentContainer: root.querySelector('.ww-payment-container'),
            paymentFallback: root.querySelector('.ww-payment-fallback'),
            skipPaymentBtn: root.querySelector('.ww-skip-payment-btn'),
            autoLoginSpinnerWrap: root.querySelector('.ww-auto-login-spinner'),
            successContent: root.querySelector('.ww-success-content'),
            successTitle: root.querySelector('.ww-success-title'),
            successMessage: root.querySelector('.ww-success-message'),
            grantedApps: root.querySelector('.ww-granted-apps'),
            grantedAppsList: root.querySelector('.ww-granted-apps-list'),
            paymentSuccessMsg: root.querySelector('.ww-payment-success-msg'),
            paymentSkippedMsg: root.querySelector('.ww-payment-skipped-msg'),
            autoLoginError: root.querySelector('.ww-auto-login-error'),
            continueLogin: root.querySelector('.ww-continue-login'),
            continueLoginBtn: root.querySelector('.ww-continue-login-btn')
        };

        this._bindEvents();
        this._initializeStep();
    }

    RegistrationInstance.prototype._bindEvents = function () {
        var self = this;

        // Token validation
        if (this.els.validateTokenBtn) {
            this.els.validateTokenBtn.addEventListener('click', function () { self._validateToken(); });
        }
        if (this.els.tokenInput) {
            this.els.tokenInput.addEventListener('keypress', function (e) {
                if (e.key === 'Enter') { e.preventDefault(); self._validateToken(); }
            });
        }

        // Optional token
        if (this.els.applyTokenBtn) {
            this.els.applyTokenBtn.addEventListener('click', function () { self._validateOptionalToken(); });
        }
        if (this.els.optTokenInput) {
            this.els.optTokenInput.addEventListener('input', function () {
                self.els.applyTokenBtn.disabled = !this.value.trim();
            });
            this.els.optTokenInput.addEventListener('blur', function () {
                if (this.value.trim() && !self.useToken && !self.isLoading) {
                    self._validateOptionalToken();
                }
            });
        }
        if (this.els.clearTokenBtn) {
            this.els.clearTokenBtn.addEventListener('click', function () { self._clearToken(); });
        }

        // Registration form
        if (this.els.regForm) {
            this.els.regForm.addEventListener('submit', function (e) {
                e.preventDefault();
                self._submitRegistration();
            });
        }

        // Password toggle
        if (this.els.togglePasswordBtn) {
            this.els.togglePasswordBtn.addEventListener('click', function () { self._togglePassword(); });
        }

        // Navigation
        if (this.els.useDiffTokenBtn) {
            this.els.useDiffTokenBtn.addEventListener('click', function () { self._resetForm(); });
        }
        if (this.els.cancelBtn) {
            this.els.cancelBtn.addEventListener('click', function () {
                self.root.dispatchEvent(new CustomEvent('ww-registration-cancel', { bubbles: true }));
            });
        }

        // Disclaimers
        if (this.els.cancelDisclaimersBtn) {
            this.els.cancelDisclaimersBtn.addEventListener('click', function () { self._cancelDisclaimers(); });
        }
        if (this.els.acceptDisclaimersBtn) {
            this.els.acceptDisclaimersBtn.addEventListener('click', function () { self._acceptDisclaimers(); });
        }

        // Payment
        if (this.els.backToAccountBtn) {
            this.els.backToAccountBtn.addEventListener('click', function () { self._backToAccount(); });
        }
        if (this.els.skipPaymentBtn) {
            this.els.skipPaymentBtn.addEventListener('click', function () { self._skipPayment(); });
        }

        // Continue to login
        if (this.els.continueLoginBtn) {
            this.els.continueLoginBtn.addEventListener('click', function () { self._navigateToLogin(); });
        }

        // Listen for payment events from embedded PaymentViewComponent
        this.root.addEventListener('ww-payment-success', function (e) { self._handlePaymentSuccess(e.detail); });
        this.root.addEventListener('ww-payment-failure', function (e) { self._handlePaymentFailure(e.detail); });
        this.root.addEventListener('ww-payment-cancel', function () { self._skipPayment(); });
    };

    RegistrationInstance.prototype._initializeStep = function () {
        var preToken = this.root.dataset.token;

        if (preToken) {
            this.tokenValue = preToken;
            if (this.els.tokenInput) this.els.tokenInput.value = preToken;
            this._validateToken();
        } else if (this.tokenRequired) {
            this.currentStep = 1;
            this._showStep('token');
        } else if (this.allowOpen) {
            this.tokenValidated = true;
            this.currentStep = 2;
            this._showStep('form');
            this._loadPasswordRequirements();
        }
    };

    // ── Step Management ──────────────────────────────────────────────

    RegistrationInstance.prototype._showStep = function (step) {
        var allSteps = ['tokenStep', 'formStep', 'disclaimerStep', 'paymentStep', 'successStep'];
        for (var i = 0; i < allSteps.length; i++) {
            if (this.els[allSteps[i]]) this.els[allSteps[i]].style.display = 'none';
        }

        switch (step) {
            case 'token':
                this.els.tokenStep.style.display = '';
                break;
            case 'form':
                this.els.formStep.style.display = '';
                this._updateFormUI();
                break;
            case 'disclaimer':
                this.els.disclaimerStep.style.display = '';
                break;
            case 'payment':
                this.els.paymentStep.style.display = '';
                this._updatePaymentUI();
                break;
            case 'success':
                this.els.successStep.style.display = '';
                break;
        }

        this._updateStepIndicator();
    };

    RegistrationInstance.prototype._updateStepIndicator = function () {
        if (this.currentStep < 1) return;
        this.els.steps.style.display = '';

        // Token step visibility
        if (this.tokenRequired) {
            this.els.stepToken.style.display = '';
            this.els.connectorToken.style.display = '';
            this.els.accountStepNum.textContent = '2';
            this.els.paymentStepNum.textContent = '3';
        } else {
            this.els.stepToken.style.display = 'none';
            this.els.connectorToken.style.display = 'none';
            this.els.accountStepNum.textContent = '1';
            this.els.paymentStepNum.textContent = '2';
        }

        // Payment step visibility
        if (this.requiresPayment) {
            this.els.connectorPayment.style.display = '';
            this.els.stepPayment.style.display = '';
        } else {
            this.els.connectorPayment.style.display = 'none';
            this.els.stepPayment.style.display = 'none';
        }

        // Active/completed states
        var steps = this.root.querySelectorAll('.ww-step');
        var connectors = this.root.querySelectorAll('.ww-step-connector');
        for (var i = 0; i < steps.length; i++) {
            steps[i].classList.remove('active', 'completed');
        }
        for (var j = 0; j < connectors.length; j++) {
            connectors[j].classList.remove('completed');
        }

        // Calculate visible step index
        var visibleSteps = [];
        if (this.tokenRequired) visibleSteps.push(this.els.stepToken);
        visibleSteps.push(this.els.stepAccount);
        if (this.requiresPayment) visibleSteps.push(this.els.stepPayment);

        var activeIdx = this.currentStep - (this.tokenRequired ? 1 : 2);
        if (!this.tokenRequired) activeIdx = this.currentStep - 2;
        if (this.currentStep === 1 && this.tokenRequired) activeIdx = 0;
        if (this.currentStep === 2) activeIdx = this.tokenRequired ? 1 : 0;
        if (this.currentStep === 3) activeIdx = this.tokenRequired ? 2 : 1;

        for (var k = 0; k < visibleSteps.length; k++) {
            if (k < activeIdx) {
                visibleSteps[k].classList.add('completed');
            } else if (k === activeIdx) {
                visibleSteps[k].classList.add('active');
            }
        }

        var visibleConnectors = this.root.querySelectorAll('.ww-step-connector:not([style*="display: none"])');
        for (var m = 0; m < visibleConnectors.length; m++) {
            if (m < activeIdx) visibleConnectors[m].classList.add('completed');
        }
    };

    // ── Token Validation ─────────────────────────────────────────────

    RegistrationInstance.prototype._validateToken = function () {
        if (this.isLoading) return;
        var self = this;
        var value = (this.els.tokenInput ? this.els.tokenInput.value : '').trim();

        if (!value) {
            this._showTokenError('Please enter a registration token.');
            return;
        }

        this.isLoading = true;
        this._clearTokenError();
        this.els.tokenSpinner.style.display = '';
        this.els.tokenBtnText.textContent = 'Validating...';
        this.els.validateTokenBtn.disabled = true;

        this._apiGet('api/registrationtokens/validate-detailed/' + encodeURIComponent(value))
            .then(function (data) {
                if (data && data.isValid) {
                    self.tokenInfo = data;
                    self.tokenValue = value;
                    self.tokenValidated = true;
                    self.useToken = true;
                    self.requiresPayment = !!data.requiresPaymentSetup;

                    if (self.requiresPayment) {
                        self.pricingDetails = {
                            planName: data.pricingModelName,
                            planDescription: data.pricingDescription,
                            priceAmount: data.priceAmount || 0,
                            currency: data.priceCurrency || 'USD',
                            isSubscription: data.isSubscription,
                            billingFrequency: data.billingFrequency || (data.isSubscription ? 'Monthly' : 'One-time')
                        };
                    }

                    self.currentStep = 2;
                    self._showStep('form');
                    self._loadPasswordRequirements();
                } else {
                    self._showTokenError(data ? data.errorMessage || 'Invalid registration token.' : 'Invalid registration token.');
                }
            })
            .catch(function () {
                self._showTokenError('Unable to validate token. Please try again.');
                self._dispatchError('Unable to validate token. Please try again.');
            })
            .finally(function () {
                self.isLoading = false;
                self.els.tokenSpinner.style.display = 'none';
                self.els.tokenBtnText.textContent = 'Validate Token';
                self.els.validateTokenBtn.disabled = false;
            });
    };

    RegistrationInstance.prototype._validateOptionalToken = function () {
        if (this.isLoading) return;
        var self = this;
        var value = (this.els.optTokenInput ? this.els.optTokenInput.value : '').trim();
        if (!value) return;

        this.isLoading = true;
        this.els.optTokenSpinner.style.display = '';
        this.els.optTokenText.textContent = '';
        this.els.optTokenError.style.display = 'none';
        this.els.optTokenSuccess.style.display = 'none';

        this._apiGet('api/registrationtokens/validate-detailed/' + encodeURIComponent(value))
            .then(function (data) {
                if (data && data.isValid) {
                    self.tokenInfo = data;
                    self.tokenValue = value;
                    self.useToken = true;
                    self.els.optTokenSuccess.style.display = '';
                    self.els.optTokenInput.classList.add('is-valid');
                    self.els.optTokenInput.classList.remove('is-invalid');

                    if (data.requiresPaymentSetup) {
                        self.requiresPayment = true;
                        self.pricingDetails = {
                            planName: data.pricingModelName,
                            planDescription: data.pricingDescription,
                            priceAmount: data.priceAmount || 0,
                            currency: data.priceCurrency || 'USD',
                            isSubscription: data.isSubscription,
                            billingFrequency: data.billingFrequency || (data.isSubscription ? 'Monthly' : 'One-time')
                        };
                    }

                    self._showTokenInfo();
                    self._updateStepIndicator();
                    self._updateFormUI();
                } else {
                    self.els.optTokenError.textContent = data ? data.errorMessage || 'Invalid token.' : 'Invalid token.';
                    self.els.optTokenError.style.display = '';
                    self.els.optTokenInput.classList.add('is-invalid');
                    self.els.optTokenInput.classList.remove('is-valid');
                }
            })
            .catch(function () {
                self.els.optTokenError.textContent = 'Unable to validate token.';
                self.els.optTokenError.style.display = '';
            })
            .finally(function () {
                self.isLoading = false;
                self.els.optTokenSpinner.style.display = 'none';
                self.els.optTokenText.textContent = 'Apply';
            });
    };

    RegistrationInstance.prototype._clearToken = function () {
        this.tokenValue = '';
        this.tokenInfo = null;
        this.useToken = false;
        this.els.tokenInfoBox.style.display = 'none';

        if (this.defaultPricingId && this.pricingDetails) {
            // Revert to default pricing
        } else {
            this.requiresPayment = false;
            this.pricingDetails = null;
        }

        if (this.els.optTokenInput) {
            this.els.optTokenInput.value = '';
            this.els.optTokenInput.classList.remove('is-valid', 'is-invalid');
        }
        this.els.optTokenSuccess.style.display = 'none';
        this.els.optTokenError.style.display = 'none';

        this._updateFormUI();
        this._updateStepIndicator();
    };

    RegistrationInstance.prototype._showTokenInfo = function () {
        if (!this.tokenInfo) return;
        var ti = this.tokenInfo;
        var html = '';

        if (ti.companyClientName) {
            html += '<strong>Organization:</strong> ' + this._esc(ti.companyClientName) + '<br>';
        } else if (ti.companyName) {
            html += '<strong>Company:</strong> ' + this._esc(ti.companyName) + '<br>';
        }

        html += '<strong>Role:</strong> ' + this._esc(ti.assignedRole || '') + '<br>';

        if (ti.restrictedToAppNames && ti.restrictedToAppNames.length > 0) {
            html += '<strong>Apps:</strong> ';
            if (ti.restrictedToAppNames.length === 1) {
                html += this._esc(ti.restrictedToAppNames[0]) + '<br>';
            } else {
                html += '<ul class="mb-0 ps-3">';
                for (var i = 0; i < ti.restrictedToAppNames.length; i++) {
                    html += '<li>' + this._esc(ti.restrictedToAppNames[i]) + '</li>';
                }
                html += '</ul>';
            }
        } else if (ti.appName) {
            html += '<strong>App:</strong> ' + this._esc(ti.appName) + '<br>';
        } else if (ti.assignedRole === 'CompanyAdmin') {
            html += '<strong>Apps:</strong> <em>All company apps</em><br>';
        }

        if (ti.pricingModelName) {
            html += '<strong>Plan:</strong> ' + this._esc(ti.pricingModelName) + '<br>';
        }
        if (ti.isSubscription) {
            html += '<span class="badge bg-info">Subscription</span> ';
        }
        if (ti.maxUsages != null) {
            html += '<strong>Usage:</strong> ' + ti.currentUsages + ' / ' + ti.maxUsages + '<br>';
        }
        if (ti.expirationDate) {
            var d = new Date(ti.expirationDate);
            html += '<strong>Expires:</strong> ' + d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
        }

        this.els.tokenInfoContent.innerHTML = html;
        this.els.tokenInfoBox.style.display = '';

        if (this.tokenOptional) {
            this.els.clearTokenBtn.style.display = '';
        }
    };

    // ── Form UI Management ───────────────────────────────────────────

    RegistrationInstance.prototype._updateFormUI = function () {
        // Subtitle
        if (this.useToken && this.tokenInfo) {
            this.els.regSubtitle.textContent = 'Your registration token is valid. Please complete your account setup.';
            this._showTokenInfo();
        } else {
            this.els.regSubtitle.textContent = 'Please complete the form below to create your account.';
        }

        // Optional token card
        if (this.tokenOptional && !this.useToken) {
            this.els.optTokenCard.style.display = '';
        } else {
            this.els.optTokenCard.style.display = 'none';
        }

        // Button text
        this.els.regBtnText.textContent = this.requiresPayment ? 'Continue to Payment' : 'Create Account';

        // Navigation buttons
        if (this.tokenRequired) {
            this.els.useDiffTokenBtn.style.display = '';
            this.els.cancelBtn.style.display = 'none';
        } else {
            this.els.useDiffTokenBtn.style.display = 'none';
            this.els.cancelBtn.style.display = '';
        }
    };

    RegistrationInstance.prototype._togglePassword = function () {
        this.showPassword = !this.showPassword;
        var pwField = this.root.querySelector('[name="password"]');
        if (pwField) {
            pwField.type = this.showPassword ? 'text' : 'password';
        }
        var icon = this.els.togglePasswordBtn.querySelector('i');
        if (icon) {
            icon.className = this.showPassword ? 'fas fa-eye-slash' : 'fas fa-eye';
        }
    };

    RegistrationInstance.prototype._loadPasswordRequirements = function () {
        var self = this;
        if (!this.appId) return;

        this._apiGet('api/auth/password-requirements/' + encodeURIComponent(this.appId))
            .then(function (text) {
                if (text) {
                    self.els.passwordReqs.textContent = typeof text === 'string' ? text : '';
                    self.els.passwordReqs.style.display = '';
                }
            })
            .catch(function () { /* ignore */ });
    };

    // ── Registration Submission ──────────────────────────────────────

    RegistrationInstance.prototype._submitRegistration = function () {
        if (this.isLoading) return;
        var self = this;

        // Client-side validation
        var form = this.els.regForm;
        if (!form.checkValidity()) {
            form.classList.add('was-validated');
            return;
        }

        // Password match check
        var pw = form.querySelector('[name="password"]').value;
        var cpw = form.querySelector('[name="confirmPassword"]').value;
        if (pw !== cpw) {
            form.querySelector('[name="confirmPassword"]').classList.add('is-invalid');
            return;
        }

        this.isLoading = true;
        this._hideRegError();
        this.els.regSpinner.style.display = '';
        this.els.registerBtn.disabled = true;

        var formData = {
            username: form.querySelector('[name="username"]').value.trim(),
            email: form.querySelector('[name="email"]').value.trim(),
            password: pw,
            token: this.useToken ? this.tokenValue : null,
            appId: this.appId
        };

        // Step 1: Validate registration data
        this._apiPost('api/userregistration/validate', formData)
            .then(function (validation) {
                if (!validation || !validation.isValid) {
                    self._showRegError(validation ? validation.errorMessage || 'Validation failed.' : 'Validation failed.');
                    return;
                }
                self.validationResponse = validation;

                // Step 2: Check disclaimers
                return self._checkDisclaimers().then(function (hasDisclaimers) {
                    if (hasDisclaimers) return; // Disclaimer step shown, wait for acceptance

                    // Step 3: Payment or complete
                    if (validation.requiresPayment) {
                        self._setupPaymentStep(validation);
                    } else {
                        return self._completeRegistration();
                    }
                });
            })
            .catch(function (err) {
                self._showRegError('An error occurred during registration. Please try again.');
                self._dispatchError('An error occurred during registration. Please try again.');
            })
            .finally(function () {
                self.isLoading = false;
                self.els.regSpinner.style.display = 'none';
                self.els.registerBtn.disabled = false;
            });
    };

    RegistrationInstance.prototype._checkDisclaimers = function () {
        var self = this;
        if (!this.appId) return Promise.resolve(false);

        return this._apiGet('api/disclaimeracceptance/pending/' + encodeURIComponent(this.appId) + '?showOn=registration')
            .then(function (response) {
                if (response && response.hasPendingDisclaimers && response.disclaimers && response.disclaimers.length > 0) {
                    self.disclaimers = response.disclaimers;
                    self._renderDisclaimers();
                    self._showStep('disclaimer');
                    return true;
                }
                return false;
            })
            .catch(function () {
                return false;
            });
    };

    RegistrationInstance.prototype._renderDisclaimers = function () {
        var list = this.els.disclaimersList;
        list.innerHTML = '';
        var self = this;

        for (var i = 0; i < this.disclaimers.length; i++) {
            var d = this.disclaimers[i];
            var card = document.createElement('div');
            card.className = 'card mb-3';
            card.innerHTML =
                '<div class="card-body">' +
                '<h6 class="card-title">' + this._esc(d.title) +
                (d.isRequired ? ' <span class="text-danger">*</span>' : '') + '</h6>' +
                '<div class="card-text small mb-3" style="max-height: 200px; overflow-y: auto;">' +
                (d.contentFormat === 'html' ? d.content : this._esc(d.content)) + '</div>' +
                '<div class="form-check">' +
                '<input class="form-check-input ww-disclaimer-check" type="checkbox" ' +
                'data-disclaimer-id="' + this._esc(d.disclaimerId) + '" ' +
                'data-version-id="' + this._esc(d.versionId) + '" ' +
                (d.isRequired ? 'required' : '') + '>' +
                '<label class="form-check-label">I accept</label>' +
                '</div></div>';
            list.appendChild(card);
        }

        // Bind checkbox changes
        var checks = list.querySelectorAll('.ww-disclaimer-check');
        for (var j = 0; j < checks.length; j++) {
            checks[j].addEventListener('change', function () { self._updateDisclaimerBtn(); });
        }
        this._updateDisclaimerBtn();
    };

    RegistrationInstance.prototype._updateDisclaimerBtn = function () {
        var checks = this.els.disclaimersList.querySelectorAll('.ww-disclaimer-check[required]');
        var allChecked = true;
        for (var i = 0; i < checks.length; i++) {
            if (!checks[i].checked) { allChecked = false; break; }
        }
        this.els.acceptDisclaimersBtn.disabled = !allChecked;
    };

    RegistrationInstance.prototype._acceptDisclaimers = function () {
        var checks = this.els.disclaimersList.querySelectorAll('.ww-disclaimer-check:checked');
        this.disclaimerAcceptances = [];
        for (var i = 0; i < checks.length; i++) {
            this.disclaimerAcceptances.push({
                companyDisclaimerId: checks[i].dataset.disclaimerId,
                companyDisclaimerVersionId: checks[i].dataset.versionId
            });
        }

        if (this.validationResponse && this.validationResponse.requiresPayment) {
            this._setupPaymentStep(this.validationResponse);
        } else {
            this._completeRegistration();
        }
    };

    RegistrationInstance.prototype._cancelDisclaimers = function () {
        this.disclaimers = null;
        this.disclaimerAcceptances = null;
        this.currentStep = 2;
        this._showStep('form');
    };

    // ── Payment Step ─────────────────────────────────────────────────

    RegistrationInstance.prototype._setupPaymentStep = function (validation) {
        this.requiresPayment = true;
        this.registrationPending = true;
        this.currentStep = 3;

        this.pricingDetails = {
            planName: validation.pricingModelName,
            priceAmount: validation.priceAmount || 0,
            currency: validation.currency || 'USD',
            isSubscription: validation.isSubscription
        };

        this.registrationResponse = {
            success: true,
            requiresPaymentSetup: true,
            isSubscription: validation.isSubscription,
            paymentAppId: validation.paymentAppId || this.appId,
            pricingModelId: validation.pricingModelId,
            pricingModelName: validation.pricingModelName,
            grantedApps: validation.grantedApps
        };

        this._showStep('payment');
    };

    RegistrationInstance.prototype._updatePaymentUI = function () {
        var p = this.pricingDetails;
        if (!p) return;

        var planName = p.planName || this.registrationResponse?.pricingModelName || 'Pricing';
        if (planName === 'Default Pricing' || planName === 'Default') planName = 'Pricing';
        this.els.pricingPlanName.textContent = planName;

        var isSub = p.isSubscription || (this.registrationResponse && this.registrationResponse.isSubscription);
        this.els.subscriptionBadge.style.display = isSub ? '' : 'none';
        this.els.priceAmount.textContent = this._formatPrice(p.priceAmount || 0, p.currency);
        this.els.pricePeriod.textContent = isSub ? '/month' : ' one-time';

        if (p.planDescription) {
            this.els.pricingDescription.textContent = p.planDescription;
            this.els.pricingDescription.style.display = '';
        }

        this.els.subscriptionInfo.style.display = isSub ? '' : 'none';

        if (this.registrationPending) {
            this.els.paymentSubtitle.textContent = 'Your account details have been verified. Complete payment to activate your account.';
        }

        // Load payment component (invoke via AJAX or embed)
        this._loadPaymentComponent();
    };

    RegistrationInstance.prototype._loadPaymentComponent = function () {
        var self = this;
        var paymentAppId = this.registrationResponse ? this.registrationResponse.paymentAppId : this.appId;

        if (!paymentAppId) {
            this.els.paymentFallback.style.display = '';
            return;
        }

        // Build URL for the PaymentViewComponent via AJAX
        var formData = this.els.regForm;
        var amount = this.pricingDetails ? this.pricingDetails.priceAmount : 0;
        var currency = this.pricingDetails ? this.pricingDetails.currency : 'USD';
        var description = 'Registration: ' + (this.pricingDetails ? this.pricingDetails.planName || '' : '');
        var isSub = this.pricingDetails ? this.pricingDetails.isSubscription : false;
        var pricingModelId = this.registrationResponse ? this.registrationResponse.pricingModelId : '';
        var email = formData ? formData.querySelector('[name="email"]').value : '';
        var proxyBase = this.paymentProxyBase || this.proxyBase;

        // Render a simplified payment UI inline
        // For full integration, the consuming app can render the PaymentViewComponent server-side
        // Here we use the payment.js API if available, otherwise show the fallback
        if (typeof window.wwPayment !== 'undefined') {
            this.els.paymentContainer.innerHTML =
                '<div class="ww-payment-component" ' +
                'data-component-id="reg-pay-' + this.cid + '" ' +
                'data-app-id="' + this._esc(paymentAppId) + '" ' +
                'data-amount="' + amount + '" ' +
                'data-currency="' + this._esc(currency) + '" ' +
                'data-description="' + this._esc(description) + '" ' +
                'data-customer-email="' + this._esc(email) + '" ' +
                'data-pricing-model-id="' + this._esc(pricingModelId || '') + '" ' +
                'data-is-subscription="' + (isSub ? 'true' : 'false') + '" ' +
                'data-proxy-base="' + this._esc(proxyBase) + '" ' +
                'data-show-amount="false">' +
                '</div>';
            window.wwPayment.init('reg-pay-' + this.cid);
        } else {
            // Fallback: show skip payment option
            this.els.paymentFallback.style.display = '';
        }
    };

    RegistrationInstance.prototype._handlePaymentSuccess = function (detail) {
        var self = this;
        this.pendingPaymentTransactionId = detail ? detail.transactionId : null;
        this.pendingPaymentIntentId = detail ? detail.paymentIntentId : null;

        if (this.registrationPending) {
            this._completeRegistration().then(function () {
                if (self.registrationSuccessful) {
                    self._linkPaymentTransaction();
                }
                self.paymentComplete = true;
                self.registrationSuccessful = true;
                self._showSuccessStep();
            });
        } else {
            this.paymentComplete = true;
            this.registrationSuccessful = true;
            this._showSuccessStep();
        }
    };

    RegistrationInstance.prototype._handlePaymentFailure = function (detail) {
        // Payment failed - user can retry via the payment component
    };

    RegistrationInstance.prototype._skipPayment = function () {
        var self = this;

        var doSkip = function () {
            if (self.registrationResponse && self.registrationResponse.userId) {
                self._apiPost('api/registrationpayment/skip', {
                    userId: self.registrationResponse.userId,
                    registrationToken: self.tokenValue,
                    reason: 'User chose to skip during registration'
                }).catch(function () { /* ignore */ });
            }
            self.paymentSkipped = true;
            self.registrationSuccessful = true;
            self._showSuccessStep();
        };

        if (this.registrationPending) {
            this._completeRegistration().then(function () {
                if (self.registrationSuccessful) {
                    doSkip();
                }
            });
        } else {
            doSkip();
        }
    };

    RegistrationInstance.prototype._linkPaymentTransaction = function () {
        var txnId = this.pendingPaymentIntentId || this.pendingPaymentTransactionId;
        var userId = this.registrationResponse ? this.registrationResponse.userId : null;
        if (!txnId || !userId) return;

        var clientId = this.tokenInfo ? this.tokenInfo.companyClientId : null;
        this._apiPost('api/payment/link-transaction', {
            externalTransactionId: txnId,
            userId: userId,
            companyClientId: clientId
        }).catch(function () { /* log but don't fail */ });

        this.pendingPaymentTransactionId = null;
        this.pendingPaymentIntentId = null;
    };

    RegistrationInstance.prototype._backToAccount = function () {
        this.currentStep = 2;
        this._showStep('form');
    };

    // ── Complete Registration ────────────────────────────────────────

    RegistrationInstance.prototype._completeRegistration = function () {
        var self = this;
        var form = this.els.regForm;

        var baseRequest = {
            firstName: form.querySelector('[name="firstName"]').value.trim(),
            lastName: form.querySelector('[name="lastName"]').value.trim(),
            email: form.querySelector('[name="email"]').value.trim(),
            password: form.querySelector('[name="password"]').value,
            appId: this.appId,
            platform: 'Web',
            deviceInfo: 'Browser',
            username: form.querySelector('[name="username"]').value.trim(),
            disclaimerAcceptances: this.disclaimerAcceptances
        };

        var url, body;
        if (this.useToken && this.tokenValue) {
            url = 'api/userregistration/register-with-token';
            body = Object.assign({}, baseRequest, { token: this.tokenValue });
        } else {
            url = 'api/userregistration/register';
            body = Object.assign({}, baseRequest, { pricingModelId: this.defaultPricingId || null });
        }

        return this._apiPost(url, body)
            .then(function (result) {
                if (result && result.success) {
                    self.registrationResponse = result;
                    self.registrationPending = false;
                    self.registrationSuccessful = true;

                    self.root.dispatchEvent(new CustomEvent('ww-registration-success', {
                        bubbles: true,
                        detail: {
                            userId: result.userId,
                            email: baseRequest.email,
                            requiresPaymentSetup: result.requiresPaymentSetup,
                            isSubscription: result.isSubscription
                        }
                    }));

                    if (!self.requiresPayment || self.paymentComplete || self.paymentSkipped) {
                        self._showSuccessStep();
                    }
                } else {
                    var errMsg = result ? result.message || 'Registration failed.' : 'Registration failed.';
                    self._showRegError(errMsg);
                    self._dispatchError(errMsg);
                    self.registrationSuccessful = false;
                }
            })
            .catch(function () {
                self._showRegError('An error occurred during registration. Please try again.');
                self._dispatchError('An error occurred during registration. Please try again.');
                self.registrationSuccessful = false;
            });
    };

    // ── Success Step ─────────────────────────────────────────────────

    RegistrationInstance.prototype._showSuccessStep = function () {
        this._showStep('success');

        if (this.autoLogin) {
            this._performAutoLogin();
        } else {
            this._renderSuccessContent(false);
        }
    };

    RegistrationInstance.prototype._performAutoLogin = function () {
        var self = this;
        this.els.autoLoginSpinnerWrap.style.display = '';
        this.els.successContent.style.display = 'none';

        var form = this.els.regForm;
        var loginData = {
            username: form.querySelector('[name="username"]').value.trim(),
            email: form.querySelector('[name="email"]').value.trim(),
            password: form.querySelector('[name="password"]').value,
            appId: this.appId
        };

        this._apiPost('api/auth/login', loginData)
            .then(function (authResponse) {
                self.els.autoLoginSpinnerWrap.style.display = 'none';

                if (authResponse && authResponse.jwtToken) {
                    self._renderSuccessContent(true);

                    self.root.dispatchEvent(new CustomEvent('ww-auto-login-success', {
                        bubbles: true,
                        detail: authResponse
                    }));

                    if (self.redirectUrl) {
                        window.location.href = self.redirectUrl;
                    }
                } else {
                    self._renderSuccessContent(false, 'Auto-login completed but no session was created. Please log in manually.');
                }
            })
            .catch(function () {
                self.els.autoLoginSpinnerWrap.style.display = 'none';
                self._renderSuccessContent(false, 'Could not log you in automatically. Please use the login page to sign in.');
            });
    };

    RegistrationInstance.prototype._renderSuccessContent = function (autoLoginOk, errorMsg) {
        this.els.successContent.style.display = '';

        if (autoLoginOk) {
            this.els.successTitle.textContent = 'Welcome!';
            this.els.successMessage.textContent = 'Your account has been created and you are now logged in.';
        }

        // Granted apps
        var apps = this.registrationResponse ? this.registrationResponse.grantedApps : null;
        if (apps && apps.length > 0) {
            this.els.grantedAppsList.innerHTML = '';
            for (var i = 0; i < apps.length; i++) {
                var li = document.createElement('li');
                li.textContent = apps[i];
                this.els.grantedAppsList.appendChild(li);
            }
            this.els.grantedApps.style.display = '';
        }

        // Payment status
        this.els.paymentSuccessMsg.style.display = this.paymentComplete ? '' : 'none';
        this.els.paymentSkippedMsg.style.display = this.paymentSkipped ? '' : 'none';

        // Auto-login error
        if (errorMsg) {
            this.els.autoLoginError.innerHTML = '<i class="fas fa-exclamation-triangle me-2"></i>' + this._esc(errorMsg);
            this.els.autoLoginError.style.display = '';
        }

        // Show login button if auto-login failed or disabled
        if (!autoLoginOk) {
            this.els.continueLogin.style.display = '';
        }
    };

    RegistrationInstance.prototype._navigateToLogin = function () {
        var url = '/login';
        if (this.appId) url += '?appId=' + encodeURIComponent(this.appId);
        window.location.href = url;
    };

    // ── Form Reset ───────────────────────────────────────────────────

    RegistrationInstance.prototype._resetForm = function () {
        this.currentStep = 1;
        this.tokenValidated = false;
        this.tokenValue = '';
        this.tokenInfo = null;
        this.useToken = false;
        this.requiresPayment = false;
        this.paymentComplete = false;
        this.paymentSkipped = false;
        this.registrationPending = false;
        this.registrationSuccessful = false;
        this.registrationResponse = null;
        this.validationResponse = null;
        this.pricingDetails = null;
        this.disclaimers = null;
        this.disclaimerAcceptances = null;
        this.pendingPaymentTransactionId = null;
        this.pendingPaymentIntentId = null;

        if (this.els.tokenInput) this.els.tokenInput.value = '';
        if (this.els.regForm) this.els.regForm.reset();
        this.els.regForm.classList.remove('was-validated');
        this._hideRegError();
        this._clearTokenError();

        if (this.tokenRequired) {
            this._showStep('token');
        } else {
            this._showStep('form');
        }
    };

    // ── Error Helpers ────────────────────────────────────────────────

    RegistrationInstance.prototype._dispatchError = function (msg) {
        this.root.dispatchEvent(new CustomEvent('ww-registration-error', {
            bubbles: true,
            detail: { message: msg }
        }));
    };

    RegistrationInstance.prototype._showTokenError = function (msg) {
        this.els.tokenError.textContent = msg;
        this.els.tokenInput.classList.add('is-invalid');
    };

    RegistrationInstance.prototype._clearTokenError = function () {
        this.els.tokenError.textContent = '';
        if (this.els.tokenInput) this.els.tokenInput.classList.remove('is-invalid');
    };

    RegistrationInstance.prototype._showRegError = function (msg) {
        this.els.regErrorMsg.textContent = msg;
        this.els.regError.style.display = '';
    };

    RegistrationInstance.prototype._hideRegError = function () {
        this.els.regError.style.display = 'none';
        this.els.regErrorMsg.textContent = '';
    };

    // ── API Helpers ──────────────────────────────────────────────────

    RegistrationInstance.prototype._apiGet = function (path) {
        var url = this.proxyBase + '/' + path;
        // If path starts with 'api/', use direct URL relative to base
        if (path.indexOf('api/') === 0) {
            url = path;
        }
        return fetch(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' },
            credentials: 'same-origin'
        }).then(function (r) {
            if (!r.ok) return null;
            var ct = r.headers.get('content-type') || '';
            if (ct.indexOf('json') >= 0) return r.json();
            return r.text();
        });
    };

    RegistrationInstance.prototype._apiPost = function (path, body) {
        var url = this.proxyBase + '/' + path;
        if (path.indexOf('api/') === 0) {
            url = path;
        }
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(body),
            credentials: 'same-origin'
        }).then(function (r) {
            var ct = r.headers.get('content-type') || '';
            if (ct.indexOf('json') >= 0) return r.json();
            return r.text();
        });
    };

    RegistrationInstance.prototype._formatPrice = function (amount, currency) {
        var sym = '$';
        switch ((currency || 'USD').toUpperCase()) {
            case 'EUR': sym = '\u20ac'; break;
            case 'GBP': sym = '\u00a3'; break;
            case 'JPY': sym = '\u00a5'; break;
            case 'CAD': sym = 'CA$'; break;
            case 'AUD': sym = 'A$'; break;
        }
        return sym + parseFloat(amount).toFixed(2);
    };

    RegistrationInstance.prototype._esc = function (str) {
        if (!str) return '';
        var div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    };

    // ── Public API ───────────────────────────────────────────────────

    window.wwTokenRegistration = {
        init: function (componentId) {
            var root = document.querySelector('[data-component-id="' + componentId + '"].ww-registration-component');
            if (!root || instances[componentId]) return;
            instances[componentId] = new RegistrationInstance(root);
        },
        getInstance: function (componentId) {
            return instances[componentId] || null;
        },
        destroy: function (componentId) {
            delete instances[componentId];
        }
    };

    // Auto-initialize all components on page load
    document.addEventListener('DOMContentLoaded', function () {
        var comps = document.querySelectorAll('.ww-registration-component');
        for (var i = 0; i < comps.length; i++) {
            var id = comps[i].dataset.componentId;
            if (id && !instances[id]) {
                instances[id] = new RegistrationInstance(comps[i]);
            }
        }
    });
})();
