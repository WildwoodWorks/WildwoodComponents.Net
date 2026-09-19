/**
 * WildwoodComponents.Razor - Payment ViewComponent JavaScript
 *
 * Handles payment provider initialization, form management, and payment processing.
 * Supports Stripe, PayPal, Apple Pay, Google Pay, and BNPL providers.
 *
 * Usage: Include this script, then call wwPayment.init(componentId) after DOM ready.
 * Call wwPayment.update(componentId) after the component is re-rendered for another plan.
 *
 * Events dispatched on the component root element:
 *   - ww-payment-success: { detail: { transactionId, paymentIntentId, subscriptionId, amount,
 *                                     currency, providerType, receiptUrl, trialEnd } }
 *       Dispatched EXACTLY ONCE per payment, when the payment completes.
 *   - ww-payment-continue: { detail: <the same payload> }
 *       The success panel's Continue button. It used to re-dispatch ww-payment-success, which ran
 *       the host's success handler (a signup, an upgrade) a second time.
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

    // What an initiate response's clientSecret confirms (mirrors PaymentClientSecretTypes).
    var SETUP_INTENT = 'setup_intent';

    var MISSING_PAYMENT_ID_MESSAGE =
        'The payment could not be confirmed because the server did not return a payment id. Please try again.';
    var INCOMPLETE_BILLING_ADDRESS_MESSAGE = 'Please complete your billing address.';
    var CARD_NOT_VERIFIED_MESSAGE = 'Your card could not be verified. Please try another card.';

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

    // ===== Decisions =====
    // The rules that decide whether a card is charged, saved or left alone, kept pure and named so
    // they can be read without following the flow. Ported from the Blazor PaymentDecisions class
    // (which in turn ports react/components/payment/PaymentComponent.tsx - JS f8b095f, 3298f70,
    // 1236503, d7eae5a).

    /**
     * Identifies the Stripe intent a plan's payment belongs to. A retry after a declined card must
     * confirm the SAME intent - initiating again creates a second subscription that bills
     * separately or is abandoned - so the key covers everything that would make the server create
     * a different one.
     */
    function buildIntentKey(providerId, pricingModelId, amount, isSubscription) {
        return [
            providerId || '',
            pricingModelId || '',
            String(amount),
            isSubscription ? 'sub' : 'once'
        ].join('|');
    }

    /** True when a stored intent belongs to the payment about to be made and can be reused. */
    function canReuseIntent(storedKey, storedResponse, currentKey) {
        if (!storedResponse) return false;
        if (!storedKey) return false;
        return storedKey === currentKey;
    }

    /**
     * Whether the initiate request tells the server the client can confirm a SetupIntent. Only a
     * Stripe payment that is actually offering a trial asks for one: that is the payment where a
     * card must be saved rather than charged.
     */
    function shouldRequestSetupIntent(isStripe, hasTrial) {
        return !!isStripe && !!hasTrial;
    }

    /** True when the server answered with a SetupIntent secret - a card to save, not charge. */
    function isSetupIntentResponse(response) {
        if (!response) return false;
        if (!response.clientSecret) return false;
        return response.clientSecretType === SETUP_INTENT;
    }

    /**
     * True when a trial was offered but the server started a paid subscription instead (the account
     * has already used its one trial for the app). Nothing is charged on that click: the component
     * says so and waits for the user to agree, then confirms the SAME intent.
     */
    function shouldAskBeforeCharging(hasTrial, isStripe, response) {
        if (!hasTrial || !isStripe) return false;
        if (!response) return false;
        if (!response.clientSecret) return false;
        return response.clientSecretType !== SETUP_INTENT;
    }

    /**
     * The id the SERVER recorded for this payment - a subscription's first invoice, or the
     * PaymentIntent itself for a one-time payment - which is what the confirm endpoint can verify
     * with the processor. Empty when the server named neither, in which case nothing is confirmed:
     * confirming an empty id asks the server to verify a payment it cannot find.
     */
    function serverRecordedId(response) {
        if (!response) return '';
        return response.paymentIntentId || response.subscriptionId || '';
    }

    /**
     * The id to confirm a Stripe card payment with: the server's own id when it recorded one,
     * otherwise the PaymentIntent the browser just confirmed.
     */
    function confirmationId(response, clientPaymentIntentId) {
        return serverRecordedId(response) || clientPaymentIntentId || '';
    }

    /**
     * True when the plan the form is collecting for has changed, so a "the trial isn't available"
     * answer given about the previous plan no longer applies (JS 1236503).
     */
    function planChanged(previous, current) {
        return previous.pricingModelId !== current.pricingModelId
            || previous.trialDays !== current.trialDays
            || previous.amount !== current.amount;
    }

    /**
     * Validates and builds the billing address to send with the payment. An address the app
     * requires has to be complete BEFORE anything is charged - an incomplete one fails at the
     * provider, after the intent already exists. Returns null when a required address is
     * incomplete, and undefined when none is required (so the key is omitted entirely).
     */
    function buildBillingAddress(required, values) {
        if (!required) return undefined;

        var fields = ['firstName', 'lastName', 'street', 'city', 'state', 'zipCode'];
        var address = {};
        for (var i = 0; i < fields.length; i++) {
            var value = (values[fields[i]] || '').trim();
            if (!value) return null;
            address[fields[i]] = value;
        }
        address.country = (values.country || '').trim();
        return address;
    }

    /**
     * True when a URL is safe to navigate the browser to: an absolute http(s) address and nothing
     * else. The address comes from the provider through the server.
     */
    function isSafeRedirectUrl(url) {
        if (!url) return false;
        return /^https?:\/\//i.test(url);
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

        this.isProcessing = false;
        this.stripe = null;
        this.stripeElements = null;
        this.stripeCard = null;
        this.cardComplete = false;
        this.selectedProvider = null;

        // The Stripe intent created for this payment, kept after a declined card so a retry
        // confirms the same intent instead of creating another subscription.
        this.pendingIntent = null;
        this.pendingIntentKey = '';

        // Set when the plan advertises a trial but the server started a paid subscription instead
        // (the account has already had its trial). The charge then waits for the user to agree.
        this.trialUnavailable = false;

        this._readAttributes();
        this._bindEvents();
        this._initSelectedProvider();
    }

    /** Everything the server rendered onto the root element. */
    PaymentInstance.prototype._readAttributes = function () {
        var data = this.root.dataset;

        this.appId = data.appId;
        this.currency = data.currency || 'USD';
        this.description = data.description || '';
        this.customerId = data.customerId || '';
        this.customerEmail = data.customerEmail || '';
        this.orderId = data.orderId || '';
        this.subscriptionId = data.subscriptionId || '';
        this.requireBilling = data.requireBilling === 'true';
        this.returnUrl = data.returnUrl || '';
        this.cancelUrl = data.cancelUrl || '';
        this.proxyBase = data.proxyBase || '/api/wildwood-payment';
        this.selectedProviderId = data.selectedProvider || '';

        // Money copy is formatted server-side; the script only ever swaps between these.
        this.payLabel = data.payLabel || '';
        this.chargeLabel = data.chargeLabel || '';

        var plan = this._readPlan();
        this.amount = plan.amount;
        this.pricingModelId = plan.pricingModelId;
        this.trialDays = plan.trialDays;
        this.isSubscription = plan.isSubscription;
    };

    /** The plan this form is collecting for — what a re-render can change. */
    PaymentInstance.prototype._readPlan = function () {
        var data = this.root.dataset;
        return {
            amount: parseFloat(data.amount) || 0,
            pricingModelId: data.pricingModelId || '',
            trialDays: parseInt(data.trialDays, 10) || 0,
            isSubscription: data.isSubscription === 'true'
        };
    };

    /** True while the component is offering a free trial rather than a charge. */
    PaymentInstance.prototype.hasTrial = function () {
        return this.trialDays > 0 && !this.trialUnavailable;
    };

    /**
     * Re-reads the server-rendered attributes after the component has been rendered again. A "the
     * trial isn't available" answer was about ONE plan: when the form is reused for another — a
     * different pricing model, trial length or amount — the new plan's trial is offered again and
     * the previous plan's intent is dropped so nothing confirms against it (JS 1236503).
     */
    PaymentInstance.prototype.refresh = function () {
        var current = document.getElementById('ww-payment-' + this.cid);
        if (!current) return;

        if (current !== this.root) {
            // A fresh server render replaced the element: adopt it and bind to the new nodes.
            this.root = current;
            this.stripe = null;
            this.stripeElements = null;
            this.stripeCard = null;
            this.cardComplete = false;
            this.selectedProvider = null;
            this.isProcessing = false;
            this._resetPlanState();
            this._readAttributes();
            this._bindEvents();
            this._initSelectedProvider();
            return;
        }

        var previous = {
            amount: this.amount,
            pricingModelId: this.pricingModelId,
            trialDays: this.trialDays,
            isSubscription: this.isSubscription
        };

        this._readAttributes();

        if (planChanged(previous, this._readPlan())) {
            this._resetPlanState();
            this._applyTrialUi();
        }
    };

    /** Forgets everything that belonged to the previous plan. */
    PaymentInstance.prototype._resetPlanState = function () {
        this.trialUnavailable = false;
        this.pendingIntent = null;
        this.pendingIntentKey = '';
    };

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

        // Retry button. pendingIntent deliberately survives: the retry confirms the intent the
        // declined attempt already created rather than starting a second subscription.
        var retryBtn = this.root.querySelector('.ww-payment-retry-btn');
        if (retryBtn) {
            retryBtn.addEventListener('click', function () {
                self._hideError();
                self._hideFormError();
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

        // Continue button (success state). It dispatches its OWN event: ww-payment-success already
        // fired when the payment completed, and firing it again ran the host's success handler
        // (a signup, an upgrade) a second time.
        var continueBtn = this.root.querySelector('.ww-payment-continue-btn');
        if (continueBtn) {
            continueBtn.addEventListener('click', function () {
                self.root.dispatchEvent(new CustomEvent('ww-payment-continue', {
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
        this._hideFormError();
        // Another provider means another intent: the stored one belongs to the provider that made it.
        this.pendingIntent = null;
        this.pendingIntentKey = '';

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

        var billing = this._requireBillingAddress();
        if (billing === null) return;

        var hasTrial = this.hasTrial();
        var intentKey = buildIntentKey(this.selectedProvider.id, this.pricingModelId, this.amount, this.isSubscription);

        this._showProcessing('Processing payment...');

        // Step 1: the intent. A declined attempt's intent is reused rather than initiated again,
        // which would leave a second subscription behind.
        var initiation;
        if (canReuseIntent(this.pendingIntentKey, this.pendingIntent, intentKey)) {
            initiation = Promise.resolve(this.pendingIntent);
        } else {
            initiation = this._apiPost('/initiate', this._buildPaymentRequest(billing, shouldRequestSetupIntent(true, hasTrial)))
                .then(function (response) {
                    if (!response || !response.success) {
                        self._showError((response && response.errorMessage) || 'Payment initiation failed', response && response.errorCode);
                        return null;
                    }
                    if (response.clientSecret) {
                        self.pendingIntent = response;
                        self.pendingIntentKey = intentKey;
                    }
                    return response;
                });
        }

        initiation
            .then(function (response) {
                if (!response) return null;

                // Offered a trial, but the server wants a charge today: never charge a card the
                // user handed over for a free trial. Say so, and confirm the same intent next click.
                if (shouldAskBeforeCharging(hasTrial, true, response)) {
                    self._showTrialUnavailable();
                    return null;
                }

                // Stripe free trial - nothing is charged now, but the card is saved (SetupIntent)
                // so Stripe can charge it when the trial ends.
                if (isSetupIntentResponse(response)) {
                    return self._confirmStripeSetup(response, billing);
                }

                // No client-side confirmation needed ($0 amount, or it succeeded immediately).
                if (!response.requiresClientConfirmation || !response.clientSecret) {
                    return self._confirmServerRecorded(response, PT.Stripe);
                }

                return self.stripe.confirmCardPayment(response.clientSecret, {
                    payment_method: {
                        card: self.stripeCard,
                        billing_details: self._stripeBillingDetails(billing)
                    }
                }).then(function (result) {
                    if (result.error) {
                        self._showError(result.error.message, result.error.code);
                        return null;
                    }
                    if (!result.paymentIntent || result.paymentIntent.status !== 'succeeded') {
                        self._showError('Payment was not completed. Status: ' + (result.paymentIntent ? result.paymentIntent.status : 'unknown'), null);
                        return null;
                    }

                    // Confirm with the id the SERVER recorded (a subscription's first invoice, or
                    // the PaymentIntent itself) so it can verify the payment with Stripe. The
                    // browser's id is the fallback, never an empty string.
                    var id = confirmationId(response, result.paymentIntent.id);
                    if (!id) {
                        self._showError(MISSING_PAYMENT_ID_MESSAGE, null);
                        return null;
                    }
                    return self._confirmOnServer(id, PT.Stripe);
                });
            })
            .then(function (confirmation) {
                self._applyConfirmation(confirmation, PT.Stripe);
            })
            .catch(function (err) {
                self._showError(err.message || 'Payment failed', null);
            });
    };

    /**
     * Confirms a free trial's SetupIntent: nothing is charged now, but the card is saved so Stripe
     * can charge it when the trial ends. Without this a trial "succeeded" with no card attached and
     * there was nothing to bill.
     */
    PaymentInstance.prototype._confirmStripeSetup = function (initiation, billingAddress) {
        var self = this;

        return this.stripe.confirmCardSetup(initiation.clientSecret, {
            payment_method: {
                card: this.stripeCard,
                billing_details: this._stripeBillingDetails(billingAddress)
            }
        }).then(function (result) {
            if (result.error) {
                self._showError(result.error.message, result.error.code);
                return null;
            }
            if (!result.setupIntent || result.setupIntent.status !== 'succeeded' || !result.setupIntent.id) {
                self._showError('Card setup status: ' + (result.setupIntent ? result.setupIntent.status : 'unknown') + '. Please try again.', null);
                return null;
            }

            return self._confirmOnServer(result.setupIntent.id, PT.Stripe)
                .then(function (confirmation) {
                    if (!confirmation || !confirmation.success) {
                        self._showError(
                            (confirmation && confirmation.errorMessage) || CARD_NOT_VERIFIED_MESSAGE,
                            confirmation && confirmation.errorCode);
                        return null;
                    }

                    // The server verifies the SetupIntent but need not echo its ids back, so fall
                    // back to what this flow already knows.
                    if (!confirmation.paymentIntentId) confirmation.paymentIntentId = result.setupIntent.id;
                    if (!confirmation.subscriptionId) confirmation.subscriptionId = initiation.subscriptionId;
                    confirmation.trialEnd = initiation.trialEnd || null;
                    return confirmation;
                });
        });
    };

    PaymentInstance.prototype._processGenericPayment = function () {
        var self = this;
        var providerType = this.selectedProvider.type;

        var billing = this._requireBillingAddress();
        if (billing === null) return;

        this._showProcessing('Processing payment...');

        this._apiPost('/initiate', this._buildPaymentRequest(billing, false))
            .then(function (response) {
                if (!response || !response.success) {
                    self._showError((response && response.errorMessage) || 'Payment failed', response && response.errorCode);
                    return null;
                }
                return self._confirmServerRecorded(response, providerType);
            })
            .then(function (confirmation) {
                self._applyConfirmation(confirmation, providerType);
            })
            .catch(function (err) {
                self._showError(err.message || 'Payment failed', null);
            });
    };

    PaymentInstance.prototype._initiateBnplPayment = function () {
        var self = this;
        if (!this.selectedProvider || this.isProcessing) return;

        var billing = this._requireBillingAddress();
        if (billing === null) return;

        this._showProcessing('Redirecting...');

        this._apiPost('/initiate', this._buildPaymentRequest(billing, false))
            .then(function (response) {
                if (response && response.success && isSafeRedirectUrl(response.redirectUrl)) {
                    // Navigated to as a value, never interpolated into a script string.
                    window.location.href = response.redirectUrl;
                } else if (response && response.success) {
                    self._showError('The payment provider returned an address that cannot be opened. Please try again.', response.errorCode);
                } else {
                    self._showError((response && response.errorMessage) || 'Failed to initialize payment', response && response.errorCode);
                }
            })
            .catch(function (err) {
                self._showError(err.message || 'Payment failed', null);
            });
    };

    /**
     * Confirms with the id the server recorded. When it named neither a payment intent nor a
     * subscription there is nothing to confirm, and the payment fails with a message instead of
     * asking the server to verify an empty id.
     */
    PaymentInstance.prototype._confirmServerRecorded = function (response, providerType) {
        var id = serverRecordedId(response);
        if (!id) {
            this._showError(MISSING_PAYMENT_ID_MESSAGE, response.errorCode);
            return Promise.resolve(null);
        }
        return this._confirmOnServer(id, providerType);
    };

    PaymentInstance.prototype._confirmOnServer = function (paymentIntentId, providerType) {
        return this._apiPost('/confirm', {
            paymentIntentId: paymentIntentId,
            providerType: providerType
        });
    };

    /** Turns a server confirmation into the success panel or an error. */
    PaymentInstance.prototype._applyConfirmation = function (confirmation, providerType) {
        if (!confirmation) return; // Already handled, or the flow stopped deliberately.

        if (!confirmation.success) {
            this._showError(confirmation.errorMessage || 'Payment confirmation failed', confirmation.errorCode);
            return;
        }

        this._handlePaymentSuccess({
            transactionId: confirmation.transactionId,
            paymentIntentId: confirmation.paymentIntentId,
            subscriptionId: confirmation.subscriptionId,
            amount: this.amount,
            currency: this.currency,
            providerType: providerType,
            receiptUrl: confirmation.receiptUrl,
            trialEnd: confirmation.trialEnd || null
        });
    };

    PaymentInstance.prototype._buildPaymentRequest = function (billingAddress, supportsSetupIntent) {
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

        // PascalCase key, matching the JS SDK and the [JsonPropertyName] on the shared
        // InitiatePaymentRequest. Omitted entirely when the app asks for no address.
        if (billingAddress) req.BillingAddress = billingAddress;

        // Lets a Stripe trial come back as a SetupIntent to confirm, so the card is saved for the
        // charge at trial end instead of nothing being collected at all.
        if (supportsSetupIntent) req.supportsSetupIntent = true;

        return req;
    };

    /**
     * The validated billing address, or null when a required one is incomplete (the message is
     * shown before returning). undefined means the app asks for no address.
     */
    PaymentInstance.prototype._requireBillingAddress = function () {
        var address = buildBillingAddress(this.requireBilling, this._readBillingAddressValues());
        if (address === null) {
            this._showFormError(INCOMPLETE_BILLING_ADDRESS_MESSAGE);
            return null;
        }
        this._hideFormError();
        return address;
    };

    PaymentInstance.prototype._readBillingAddressValues = function () {
        var root = this.root;
        function value(selector) {
            var el = root.querySelector(selector);
            return el ? el.value : '';
        }
        return {
            firstName: value('.ww-billing-first'),
            lastName: value('.ww-billing-last'),
            street: value('.ww-billing-address-line'),
            city: value('.ww-billing-city'),
            state: value('.ww-billing-state'),
            zipCode: value('.ww-billing-zip'),
            country: value('.ww-billing-country')
        };
    };

    /** What Stripe puts on the payment method, from the address the form already validated. */
    PaymentInstance.prototype._stripeBillingDetails = function (address) {
        if (!address || !address.street) return undefined;
        return {
            name: ((address.firstName || '') + ' ' + (address.lastName || '')).trim(),
            address: {
                line1: address.street,
                city: address.city,
                state: address.state,
                postal_code: address.zipCode,
                country: address.country || undefined
            }
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

    /** A problem with the form itself, shown above the submit button; the form stays put. */
    PaymentInstance.prototype._showFormError = function (message) {
        var el = this.root.querySelector('.ww-payment-form-error');
        if (!el) return;
        el.textContent = message;
        el.style.display = '';
    };

    PaymentInstance.prototype._hideFormError = function () {
        var el = this.root.querySelector('.ww-payment-form-error');
        if (!el) return;
        el.textContent = '';
        el.style.display = 'none';
    };

    /**
     * The trial the view advertised is not available on this account. Nothing has been charged:
     * the notice asks for agreement, and the next Pay click confirms the SAME intent.
     */
    PaymentInstance.prototype._showTrialUnavailable = function () {
        this.trialUnavailable = true;
        this._hideProcessing();

        var form = this.root.querySelector('.ww-payment-form');
        if (form) form.style.display = '';

        this._applyTrialUi();
        this._updatePayButton();
    };

    /** Puts the button label, the trial note and the trial notice in step with the trial state. */
    PaymentInstance.prototype._applyTrialUi = function () {
        var offering = this.hasTrial();

        var notice = this.root.querySelector('.ww-payment-trial-unavailable');
        if (notice) notice.style.display = this.trialUnavailable ? '' : 'none';

        var note = this.root.querySelector('.ww-payment-trial-note');
        if (note) note.style.display = offering ? '' : 'none';

        var label = this.root.querySelector('.ww-pay-btn-label');
        if (label) label.textContent = offering ? this.payLabel : this.chargeLabel;
    };

    PaymentInstance.prototype._handlePaymentSuccess = function (detail) {
        this._hideProcessing();
        this._lastSuccessDetail = detail;
        // The payment is done; nothing may confirm against this intent again.
        this.pendingIntent = null;
        this.pendingIntentKey = '';

        var form = this.root.querySelector('.ww-payment-form');
        var successDiv = this.root.querySelector('.ww-payment-success');
        if (form) form.style.display = 'none';
        if (successDiv) successDiv.style.display = '';

        // A saved card for a free trial is not a charge, so it gets the trial copy and the date the
        // first charge is due — never "Payment Successful! Amount: ...".
        var isTrialStart = !!detail.trialEnd;
        var chargeBlock = successDiv ? successDiv.querySelector('.ww-payment-success-charge') : null;
        var trialBlock = successDiv ? successDiv.querySelector('.ww-payment-success-trial') : null;
        if (chargeBlock) chargeBlock.style.display = isTrialStart ? 'none' : '';
        if (trialBlock) trialBlock.style.display = isTrialStart ? '' : 'none';

        if (isTrialStart) {
            var trialEndEl = successDiv ? successDiv.querySelector('.ww-payment-trial-end') : null;
            if (trialEndEl) trialEndEl.textContent = this._formatDate(detail.trialEnd);
        }

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

    PaymentInstance.prototype._formatDate = function (value) {
        if (!value) return '';
        var parsed = new Date(value);
        if (isNaN(parsed.getTime())) return '';
        return parsed.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
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
            var existing = instances[componentId];
            if (existing) {
                // Initialising an existing component re-reads what the server rendered, so a form
                // rendered again for another plan offers that plan's trial rather than the
                // previous plan's "trial unavailable" answer.
                existing.refresh();
                return existing;
            }
            var instance = new PaymentInstance(componentId);
            instances[componentId] = instance;
            return instance;
        },
        /** Re-reads the server-rendered plan after the component has been rendered again. */
        update: function (componentId) {
            var instance = instances[componentId];
            if (instance) instance.refresh();
            return instance || null;
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
