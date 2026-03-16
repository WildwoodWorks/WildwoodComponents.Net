/**
 * WildwoodComponents.Razor - Payment ViewComponent JavaScript
 *
 * Handles payment provider initialization, form management, and payment processing.
 * Supports Stripe, PayPal, Apple Pay, Google Pay, and BNPL providers.
 *
 * Usage: Include this script, then call wwPayment.init(componentId) after DOM ready.
 *
 * Events dispatched on the component root element:
 *   - ww-payment-success: { detail: { transactionId, paymentIntentId, amount, currency, providerType, receiptUrl } }
 *   - ww-payment-failure: { detail: { errorMessage, errorCode, providerType, isRetryable } }
 *   - ww-payment-cancel: {}
 */
(function () {
    'use strict';

    // Provider type constants (mirrors PaymentProviderType enum)
    var PT = {
        Stripe: 1,
        PayPal: 2,
        Square: 3,
        ApplePay: 20,
        GooglePay: 21,
        Klarna: 30,
        Affirm: 31,
        Afterpay: 32
    };

    // Redirect-based providers
    function isRedirectProvider(type) {
        return type === PT.Klarna || type === PT.Affirm || type === PT.Afterpay;
    }

    function isRetryableError(code) {
        if (!code) return true;
        var nonRetryable = ['card_declined', 'insufficient_funds', 'lost_card', 'stolen_card'];
        for (var i = 0; i < nonRetryable.length; i++) {
            if (nonRetryable[i] === code) return false;
        }
        return true;
    }

    // Instances keyed by componentId
    var instances = {};

    function PaymentInstance(componentId) {
        this.cid = componentId;
        this.root = document.getElementById('ww-payment-' + componentId);
        if (!this.root) {
            console.error('[wwPayment] Root element not found: ww-payment-' + componentId);
            return;
        }

        this.appId = this.root.dataset.appId;
        this.amount = parseFloat(this.root.dataset.amount) || 0;
        this.currency = this.root.dataset.currency || 'USD';
        this.description = this.root.dataset.description || '';
        this.customerId = this.root.dataset.customerId || '';
        this.customerEmail = this.root.dataset.customerEmail || '';
        this.orderId = this.root.dataset.orderId || '';
        this.subscriptionId = this.root.dataset.subscriptionId || '';
        this.pricingModelId = this.root.dataset.pricingModelId || '';
        this.isSubscription = this.root.dataset.isSubscription === 'true';
        this.requireBilling = this.root.dataset.requireBilling === 'true';
        this.returnUrl = this.root.dataset.returnUrl || '';
        this.cancelUrl = this.root.dataset.cancelUrl || '';
        this.proxyBase = this.root.dataset.proxyBase || '/api/wildwood-payment';
        this.selectedProviderId = this.root.dataset.selectedProvider || '';

        this.isProcessing = false;
        this.stripe = null;
        this.stripeElements = null;
        this.stripeCard = null;
        this.cardComplete = false;
        this.selectedProvider = null;

        this._bindEvents();
        this._initSelectedProvider();
    }

    PaymentInstance.prototype._bindEvents = function () {
        var self = this;

        // Provider selection clicks
        var options = this.root.querySelectorAll('.provider-option');
        for (var i = 0; i < options.length; i++) {
            options[i].addEventListener('click', function () {
                self._selectProvider(this);
            });
        }

        // Pay button
        var payBtn = this.root.querySelector('.ww-pay-btn');
        if (payBtn) {
            payBtn.addEventListener('click', function () {
                self.processPayment();
            });
        }

        // Retry button
        var retryBtn = this.root.querySelector('.ww-payment-retry-btn');
        if (retryBtn) {
            retryBtn.addEventListener('click', function () {
                self._hideError();
                self._showForm();
            });
        }

        // Error close button
        var errClose = this.root.querySelector('.ww-payment-error .btn-close');
        if (errClose) {
            errClose.addEventListener('click', function () {
                self._hideError();
            });
        }

        // Cancel button
        var cancelBtn = this.root.querySelector('.ww-cancel-btn');
        if (cancelBtn) {
            cancelBtn.addEventListener('click', function () {
                self.root.dispatchEvent(new CustomEvent('ww-payment-cancel', { bubbles: true }));
            });
        }

        // Continue button (success state)
        var continueBtn = this.root.querySelector('.ww-payment-continue-btn');
        if (continueBtn) {
            continueBtn.addEventListener('click', function () {
                // Re-dispatch success event
                self.root.dispatchEvent(new CustomEvent('ww-payment-success', {
                    bubbles: true,
                    detail: self._lastSuccessDetail || {}
                }));
            });
        }

        // BNPL button
        var bnplBtn = this.root.querySelector('.ww-bnpl-btn');
        if (bnplBtn) {
            bnplBtn.addEventListener('click', function () {
                self._initiateBnplPayment();
            });
        }
    };

    PaymentInstance.prototype._initSelectedProvider = function () {
        // If single provider, use the hidden input
        var singleProvider = this.root.querySelector('.ww-single-provider');
        if (singleProvider) {
            this.selectedProvider = this._readProviderData(singleProvider);
            this._showProviderForm(this.selectedProvider.type);
            this._initProviderJs(this.selectedProvider);
            return;
        }

        // Multi-provider: find the selected one
        if (this.selectedProviderId) {
            var selected = this.root.querySelector('.provider-option[data-provider-id="' + this.selectedProviderId + '"]');
            if (selected) {
                this.selectedProvider = this._readProviderData(selected);
                this._showProviderForm(this.selectedProvider.type);
                this._initProviderJs(this.selectedProvider);
            }
        }
    };

    PaymentInstance.prototype._readProviderData = function (el) {
        return {
            id: el.dataset.providerId,
            type: parseInt(el.dataset.providerType, 10),
            publishableKey: el.dataset.publishableKey || '',
            clientId: el.dataset.clientId || '',
            merchantId: el.dataset.merchantId || '',
            isSandbox: el.dataset.isSandbox === 'true',
            supportsApplePay: el.dataset.supportsApplePay === 'true',
            supportsGooglePay: el.dataset.supportsGooglePay === 'true',
            displayLabel: el.querySelector('.provider-name') ? el.querySelector('.provider-name').textContent.trim() : 'Payment'
        };
    };

    PaymentInstance.prototype._selectProvider = function (optionEl) {
        // Deselect all
        var options = this.root.querySelectorAll('.provider-option');
        for (var i = 0; i < options.length; i++) {
            options[i].classList.remove('selected');
        }
        optionEl.classList.add('selected');

        this.selectedProvider = this._readProviderData(optionEl);
        this.cardComplete = false;
        this.stripe = null;
        this.stripeCard = null;

        this._hideAllProviderForms();
        this._showProviderForm(this.selectedProvider.type);
        this._initProviderJs(this.selectedProvider);
    };

    PaymentInstance.prototype._hideAllProviderForms = function () {
        var forms = this.root.querySelectorAll('.ww-stripe-payment, .ww-paypal-payment, .ww-applepay-payment, .ww-googlepay-payment, .ww-bnpl-payment, .ww-generic-payment');
        for (var i = 0; i < forms.length; i++) {
            forms[i].style.display = 'none';
        }
    };

    PaymentInstance.prototype._showProviderForm = function (providerType) {
        var formMap = {};
        formMap[PT.Stripe] = '.ww-stripe-payment';
        formMap[PT.PayPal] = '.ww-paypal-payment';
        formMap[PT.ApplePay] = '.ww-applepay-payment';
        formMap[PT.GooglePay] = '.ww-googlepay-payment';
        formMap[PT.Klarna] = '.ww-bnpl-payment';
        formMap[PT.Affirm] = '.ww-bnpl-payment';
        formMap[PT.Afterpay] = '.ww-bnpl-payment';

        var selector = formMap[providerType] || '.ww-generic-payment';
        var form = this.root.querySelector(selector);
        if (form) form.style.display = '';

        // Show submit button for non-redirect, non-PayPal providers
        var submitDiv = this.root.querySelector('.ww-payment-submit');
        if (submitDiv) {
            var showSubmit = !isRedirectProvider(providerType) && providerType !== PT.PayPal && providerType !== PT.ApplePay && providerType !== PT.GooglePay;
            submitDiv.style.display = showSubmit ? '' : 'none';
        }

        // Show security notice
        var securityDiv = this.root.querySelector('.ww-payment-security');
        if (securityDiv && this.selectedProvider) {
            securityDiv.style.display = '';
            var providerSpan = securityDiv.querySelector('.ww-security-provider');
            if (providerSpan) providerSpan.textContent = this.selectedProvider.displayLabel;
        }

        // Show cancel button
        var cancelDiv = this.root.querySelector('.ww-payment-cancel');
        if (cancelDiv) cancelDiv.style.display = '';

        // BNPL: update provider name
        if (providerType === PT.Klarna || providerType === PT.Affirm || providerType === PT.Afterpay) {
            var bnplNames = this.root.querySelectorAll('.ww-bnpl-provider-name');
            for (var i = 0; i < bnplNames.length; i++) {
                bnplNames[i].textContent = this.selectedProvider ? this.selectedProvider.displayLabel : '';
            }
            var bnplBtn = this.root.querySelector('.ww-bnpl-btn');
            if (bnplBtn) bnplBtn.disabled = false;
        }

        // Update pay button state
        this._updatePayButton();
    };

    PaymentInstance.prototype._initProviderJs = function (provider) {
        var self = this;

        switch (provider.type) {
            case PT.Stripe:
                this._initStripe(provider);
                break;
            case PT.PayPal:
                this._initPayPal(provider);
                break;
            case PT.ApplePay:
                // Apple Pay handled via native APIs
                break;
            case PT.GooglePay:
                // Google Pay handled via Google Pay API
                break;
            default:
                // Generic or BNPL - no special init needed
                break;
        }
    };

    // ===== Stripe =====
    PaymentInstance.prototype._initStripe = function (provider) {
        var self = this;

        if (!provider.publishableKey) {
            this._showError('Stripe is not properly configured (missing publishable key).', 'config_error');
            return;
        }

        if (typeof Stripe === 'undefined') {
            // Load Stripe.js dynamically
            this._loadScript('https://js.stripe.com/v3/', function () {
                self._createStripeElements(provider);
            });
        } else {
            this._createStripeElements(provider);
        }
    };

    PaymentInstance.prototype._createStripeElements = function (provider) {
        var self = this;
        this.stripe = Stripe(provider.publishableKey);
        this.stripeElements = this.stripe.elements();

        var cardElementId = 'stripe-card-element-' + this.cid;
        var cardErrorsId = 'stripe-card-errors-' + this.cid;

        // Clear any existing card element
        var container = document.getElementById(cardElementId);
        if (container) container.innerHTML = '';

        this.stripeCard = this.stripeElements.create('card', {
            style: {
                base: {
                    fontSize: '16px',
                    color: '#32325d',
                    fontFamily: 'system-ui, -apple-system, sans-serif',
                    '::placeholder': { color: '#aab7c4' }
                },
                invalid: { color: '#dc3545' }
            }
        });

        this.stripeCard.mount('#' + cardElementId);
        this.stripeCard.on('change', function (event) {
            self.cardComplete = event.complete;
            var errorsEl = document.getElementById(cardErrorsId);
            if (errorsEl) {
                errorsEl.textContent = event.error ? event.error.message : '';
            }
            self._updatePayButton();
        });
    };

    // ===== PayPal =====
    PaymentInstance.prototype._initPayPal = function (provider) {
        var self = this;

        if (!provider.clientId) {
            this._showError('PayPal is not properly configured (missing client ID).', 'config_error');
            return;
        }

        var containerId = 'paypal-button-container-' + this.cid;

        if (typeof paypal === 'undefined') {
            var src = 'https://www.paypal.com/sdk/js?client-id=' + encodeURIComponent(provider.clientId) + '&currency=' + encodeURIComponent(this.currency);
            if (this.isSubscription) {
                src += '&vault=true&intent=subscription';
            }
            this._loadScript(src, function () {
                self._renderPayPalButtons(containerId);
            });
        } else {
            this._renderPayPalButtons(containerId);
        }
    };

    PaymentInstance.prototype._renderPayPalButtons = function (containerId) {
        var self = this;
        var container = document.getElementById(containerId);
        if (!container) return;

        // Clear existing buttons
        container.innerHTML = '';

        paypal.Buttons({
            createOrder: function (data, actions) {
                return actions.order.create({
                    purchase_units: [{
                        amount: {
                            value: self.amount.toFixed(2),
                            currency_code: self.currency
                        },
                        description: self.description || undefined
                    }]
                });
            },
            onApprove: function (data, actions) {
                return actions.order.capture().then(function (details) {
                    var txnId = '';
                    if (details.purchase_units && details.purchase_units[0] &&
                        details.purchase_units[0].payments && details.purchase_units[0].payments.captures &&
                        details.purchase_units[0].payments.captures[0]) {
                        txnId = details.purchase_units[0].payments.captures[0].id;
                    }
                    self._handlePaymentSuccess({
                        transactionId: txnId,
                        paymentIntentId: data.orderID,
                        amount: self.amount,
                        currency: self.currency,
                        providerType: PT.PayPal
                    });
                });
            },
            onCancel: function () {
                // User cancelled - no action needed
            },
            onError: function (err) {
                self._showError(err.message || 'PayPal payment failed', 'paypal_error');
            }
        }).render('#' + containerId).then(function () {
            // Hide loading, show buttons
            var loadingEl = self.root.querySelector('.ww-paypal-loading');
            if (loadingEl) loadingEl.style.display = 'none';
            container.style.display = '';
        });
    };

    // ===== Payment Processing =====
    PaymentInstance.prototype.processPayment = function () {
        if (!this.selectedProvider || this.isProcessing) return;

        var providerType = this.selectedProvider.type;

        if (providerType === PT.Stripe) {
            this._processStripePayment();
        } else {
            this._processGenericPayment();
        }
    };

    PaymentInstance.prototype._processStripePayment = function () {
        var self = this;
        if (!this.stripe || !this.stripeCard || !this.cardComplete) return;

        this._showProcessing('Processing payment...');

        // Step 1: Initiate payment on server to get client secret
        this._apiPost('/initiate', this._buildPaymentRequest())
            .then(function (response) {
                if (!response.success) {
                    self._showError(response.errorMessage || 'Payment initiation failed', response.errorCode);
                    return;
                }

                // If no client-side confirmation needed (trial or payment succeeded immediately)
                if (!response.requiresClientConfirmation || !response.clientSecret) {
                    return self._apiPost('/confirm', {
                        paymentIntentId: response.paymentIntentId || response.subscriptionId,
                        providerType: PT.Stripe
                    });
                }

                // Step 2: Confirm with Stripe.js
                return self.stripe.confirmCardPayment(response.clientSecret, {
                    payment_method: { card: self.stripeCard }
                });
            })
            .then(function (result) {
                if (!result) return; // Error already handled

                if (result.error) {
                    self._showError(result.error.message, result.error.code);
                } else if (result.paymentIntent && result.paymentIntent.status === 'succeeded') {
                    // Step 3: Confirm on server
                    return self._apiPost('/confirm', {
                        paymentIntentId: result.paymentIntent.id,
                        providerType: PT.Stripe
                    });
                } else {
                    self._showError('Payment was not completed. Status: ' + (result.paymentIntent ? result.paymentIntent.status : 'unknown'), null);
                }
            })
            .then(function (confirmation) {
                if (!confirmation) return;

                if (confirmation.success) {
                    self._handlePaymentSuccess({
                        transactionId: confirmation.transactionId,
                        paymentIntentId: confirmation.paymentIntentId,
                        subscriptionId: confirmation.subscriptionId,
                        amount: self.amount,
                        currency: self.currency,
                        providerType: PT.Stripe,
                        receiptUrl: confirmation.receiptUrl
                    });
                } else {
                    self._showError(confirmation.errorMessage || 'Payment confirmation failed', confirmation.errorCode);
                }
            })
            .catch(function (err) {
                self._showError(err.message || 'Payment failed', null);
            });
    };

    PaymentInstance.prototype._processGenericPayment = function () {
        var self = this;
        this._showProcessing('Processing payment...');

        this._apiPost('/initiate', this._buildPaymentRequest())
            .then(function (response) {
                if (!response.success) {
                    self._showError(response.errorMessage || 'Payment failed', response.errorCode);
                    return;
                }

                if (response.paymentIntentId) {
                    return self._apiPost('/confirm', {
                        paymentIntentId: response.paymentIntentId,
                        providerType: self.selectedProvider.type
                    });
                }
            })
            .then(function (confirmation) {
                if (!confirmation) return;

                if (confirmation.success) {
                    self._handlePaymentSuccess({
                        transactionId: confirmation.transactionId,
                        paymentIntentId: confirmation.paymentIntentId,
                        amount: self.amount,
                        currency: self.currency,
                        providerType: self.selectedProvider.type,
                        receiptUrl: confirmation.receiptUrl
                    });
                } else {
                    self._showError(confirmation.errorMessage || 'Payment failed', confirmation.errorCode);
                }
            })
            .catch(function (err) {
                self._showError(err.message || 'Payment failed', null);
            });
    };

    PaymentInstance.prototype._initiateBnplPayment = function () {
        var self = this;
        if (!this.selectedProvider || this.isProcessing) return;

        this._showProcessing('Redirecting...');

        this._apiPost('/initiate', this._buildPaymentRequest())
            .then(function (response) {
                if (response.success && response.redirectUrl) {
                    window.location.href = response.redirectUrl;
                } else {
                    self._showError(response.errorMessage || 'Failed to initialize payment', response.errorCode);
                }
            })
            .catch(function (err) {
                self._showError(err.message || 'Payment failed', null);
            });
    };

    PaymentInstance.prototype._buildPaymentRequest = function () {
        var req = {
            providerId: this.selectedProvider.id,
            appId: this.appId,
            amount: this.amount,
            currency: this.currency,
            description: this.description || null,
            customerId: this.customerId || null,
            customerEmail: this.customerEmail || null,
            orderId: this.orderId || null,
            subscriptionId: this.subscriptionId || null,
            pricingModelId: this.pricingModelId || null,
            isSubscription: this.isSubscription,
            returnUrl: this.returnUrl || null,
            cancelUrl: this.cancelUrl || null
        };

        // Add billing address if required
        if (this.requireBilling) {
            var billing = this._readBillingAddress();
            if (billing) req.billingAddress = billing;
        }

        return req;
    };

    PaymentInstance.prototype._readBillingAddress = function () {
        var root = this.root;
        return {
            firstName: (root.querySelector('.ww-billing-first') || {}).value || '',
            lastName: (root.querySelector('.ww-billing-last') || {}).value || '',
            street: (root.querySelector('.ww-billing-address-line') || {}).value || '',
            city: (root.querySelector('.ww-billing-city') || {}).value || '',
            state: (root.querySelector('.ww-billing-state') || {}).value || '',
            zipCode: (root.querySelector('.ww-billing-zip') || {}).value || ''
        };
    };

    // ===== UI State Management =====
    PaymentInstance.prototype._showProcessing = function (msg) {
        this.isProcessing = true;
        var loading = this.root.querySelector('.ww-payment-loading');
        var form = this.root.querySelector('.ww-payment-form');
        var loadingMsg = this.root.querySelector('.ww-payment-loading-msg');

        if (loading) loading.style.display = '';
        if (form) form.style.display = 'none';
        if (loadingMsg) loadingMsg.textContent = msg || 'Processing...';

        this._hideError();
    };

    PaymentInstance.prototype._hideProcessing = function () {
        this.isProcessing = false;
        var loading = this.root.querySelector('.ww-payment-loading');
        if (loading) loading.style.display = 'none';
    };

    PaymentInstance.prototype._showForm = function () {
        this._hideProcessing();
        var form = this.root.querySelector('.ww-payment-form');
        if (form) form.style.display = '';
        var success = this.root.querySelector('.ww-payment-success');
        if (success) success.style.display = 'none';
    };

    PaymentInstance.prototype._showError = function (message, code) {
        this._hideProcessing();
        var form = this.root.querySelector('.ww-payment-form');
        if (form) form.style.display = '';

        var errorDiv = this.root.querySelector('.ww-payment-error');
        var errorMsg = this.root.querySelector('.ww-payment-error-msg');
        if (errorDiv) errorDiv.style.display = '';
        if (errorMsg) errorMsg.textContent = message;

        this.root.dispatchEvent(new CustomEvent('ww-payment-failure', {
            bubbles: true,
            detail: {
                errorMessage: message,
                errorCode: code,
                providerType: this.selectedProvider ? this.selectedProvider.type : 0,
                isRetryable: isRetryableError(code)
            }
        }));
    };

    PaymentInstance.prototype._hideError = function () {
        var errorDiv = this.root.querySelector('.ww-payment-error');
        if (errorDiv) errorDiv.style.display = 'none';
    };

    PaymentInstance.prototype._handlePaymentSuccess = function (detail) {
        this._hideProcessing();
        this._lastSuccessDetail = detail;

        var form = this.root.querySelector('.ww-payment-form');
        var successDiv = this.root.querySelector('.ww-payment-success');
        if (form) form.style.display = 'none';
        if (successDiv) successDiv.style.display = '';

        // Update success view
        var txnCode = successDiv ? successDiv.querySelector('.ww-payment-txn-id code') : null;
        if (txnCode) txnCode.textContent = detail.transactionId || '';

        var amountEl = successDiv ? successDiv.querySelector('.ww-payment-success-amount') : null;
        if (amountEl) amountEl.textContent = this._formatAmount(detail.amount, detail.currency);

        if (detail.receiptUrl) {
            var receiptLink = successDiv ? successDiv.querySelector('.ww-payment-receipt-link') : null;
            if (receiptLink) {
                receiptLink.style.display = '';
                var a = receiptLink.querySelector('a');
                if (a) a.href = detail.receiptUrl;
            }
        }

        // Show continue button
        var continueBtn = successDiv ? successDiv.querySelector('.ww-payment-continue-btn') : null;
        if (continueBtn) continueBtn.style.display = '';

        this.root.dispatchEvent(new CustomEvent('ww-payment-success', {
            bubbles: true,
            detail: detail
        }));
    };

    PaymentInstance.prototype._updatePayButton = function () {
        var btn = this.root.querySelector('.ww-pay-btn');
        if (!btn) return;

        var enabled = false;
        if (this.selectedProvider) {
            switch (this.selectedProvider.type) {
                case PT.Stripe:
                    enabled = this.cardComplete;
                    break;
                default:
                    enabled = true;
                    break;
            }
        }

        btn.disabled = !enabled || this.isProcessing;
    };

    PaymentInstance.prototype._formatAmount = function (amount, currency) {
        var symbols = { USD: '$', EUR: '\u20ac', GBP: '\u00a3', JPY: '\u00a5', CAD: 'CA$', AUD: 'A$' };
        var symbol = symbols[currency.toUpperCase()] || (currency + ' ');
        return symbol + parseFloat(amount).toFixed(2);
    };

    // ===== API Helpers =====
    PaymentInstance.prototype._apiPost = function (path, body) {
        return fetch(this.proxyBase + path, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
            credentials: 'same-origin'
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (text) {
                    throw new Error(text || ('HTTP ' + resp.status));
                });
            }
            return resp.json();
        });
    };

    PaymentInstance.prototype._loadScript = function (src, callback) {
        // Check if already loaded
        var scripts = document.querySelectorAll('script[src]');
        for (var i = 0; i < scripts.length; i++) {
            if (scripts[i].src === src) {
                callback();
                return;
            }
        }

        var script = document.createElement('script');
        script.src = src;
        script.async = true;
        script.onload = callback;
        script.onerror = function () {
            console.error('[wwPayment] Failed to load script: ' + src);
        };
        document.head.appendChild(script);
    };

    // ===== Public API =====
    window.wwPayment = {
        init: function (componentId) {
            if (instances[componentId]) {
                return instances[componentId];
            }
            var instance = new PaymentInstance(componentId);
            instances[componentId] = instance;
            return instance;
        },
        getInstance: function (componentId) {
            return instances[componentId] || null;
        },
        destroy: function (componentId) {
            var instance = instances[componentId];
            if (instance) {
                if (instance.stripeCard) {
                    try { instance.stripeCard.destroy(); } catch (e) { /* ignore */ }
                }
                delete instances[componentId];
            }
        }
    };

    // Auto-initialize any payment components on the page
    document.addEventListener('DOMContentLoaded', function () {
        var components = document.querySelectorAll('.ww-payment-component');
        for (var i = 0; i < components.length; i++) {
            var cid = components[i].dataset.componentId;
            if (cid) {
                wwPayment.init(cid);
            }
        }
    });
})();
