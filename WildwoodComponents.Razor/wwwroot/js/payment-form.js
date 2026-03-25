/**
 * WildwoodComponents.Razor - Payment Form Component JavaScript
 * Handles card input formatting, validation, form submission, and payment processing.
 * Razor Pages equivalent of the Blazor PaymentFormComponent interactivity.
 */
(function () {
    'use strict';

    var roots = document.querySelectorAll('.ww-payment-form-component');
    for (var r = 0; r < roots.length; r++) {
        initPaymentForm(roots[r]);
    }

    function initPaymentForm(root) {
        var cid = root.dataset.componentId;
        var proxyBase = root.dataset.proxyBase;
        var amount = parseFloat(root.dataset.amount || '0');
        var currency = root.dataset.currency || 'USD';
        var description = root.dataset.description || '';
        var requireBilling = root.dataset.requireBilling === 'true';
        var merchantId = root.dataset.merchantId || '';
        var orderId = root.dataset.orderId || '';

        var formView = root.querySelector('.ww-pf-form');
        var loadingView = root.querySelector('.ww-pf-loading');
        var successView = root.querySelector('.ww-pf-success');
        var errorView = root.querySelector('.ww-pf-error');

        var cardNumber = root.querySelector('.ww-pf-card-number');
        var cardExpiry = root.querySelector('.ww-pf-card-expiry');
        var cardCvv = root.querySelector('.ww-pf-card-cvv');
        var cardName = root.querySelector('.ww-pf-card-name');
        var submitBtn = root.querySelector('.ww-pf-submit-btn');

        var loadingTimer = null;
        var loadingTimeoutMs = parseInt(root.dataset.loadingTimeout || '30000', 10);

        // ===== VIEW MANAGEMENT =====

        function showView(view) {
            if (formView) formView.style.display = view === 'form' ? '' : 'none';
            if (loadingView) loadingView.style.display = view === 'loading' ? '' : 'none';
            if (successView) successView.style.display = view === 'success' ? '' : 'none';
            if (errorView) errorView.style.display = view === 'error' ? '' : 'none';
        }

        // ===== CARD NUMBER FORMATTING =====

        if (cardNumber) {
            cardNumber.addEventListener('input', function () {
                var val = this.value.replace(/\D/g, '').substring(0, 16);
                var formatted = val.replace(/(\d{4})(?=\d)/g, '$1 ');
                this.value = formatted;
                updateSubmitState();
            });
        }

        // ===== EXPIRY FORMATTING =====

        if (cardExpiry) {
            cardExpiry.addEventListener('input', function () {
                var val = this.value.replace(/\D/g, '').substring(0, 4);
                if (val.length >= 2) {
                    this.value = val.substring(0, 2) + '/' + val.substring(2);
                } else {
                    this.value = val;
                }
                updateSubmitState();
            });
        }

        // ===== CVV =====

        if (cardCvv) {
            cardCvv.addEventListener('input', function () {
                this.value = this.value.replace(/\D/g, '').substring(0, 4);
                updateSubmitState();
            });
        }

        if (cardName) {
            cardName.addEventListener('input', updateSubmitState);
        }

        // ===== VALIDATION =====

        function validateCard() {
            var errors = [];
            var num = cardNumber ? cardNumber.value.replace(/\s/g, '') : '';
            if (num.length < 13 || num.length > 16) errors.push('card');

            var exp = cardExpiry ? cardExpiry.value : '';
            if (!/^\d{2}\/\d{2}$/.test(exp)) {
                errors.push('expiry');
            } else {
                var parts = exp.split('/');
                var month = parseInt(parts[0], 10);
                var year = parseInt('20' + parts[1], 10);
                var now = new Date();
                if (month < 1 || month > 12 || new Date(year, month) < now) errors.push('expiry');
            }

            var cvv = cardCvv ? cardCvv.value : '';
            if (cvv.length < 3 || cvv.length > 4) errors.push('cvv');

            var name = cardName ? cardName.value.trim() : '';
            if (name.length < 2) errors.push('name');

            return errors;
        }

        function updateSubmitState() {
            if (submitBtn) {
                submitBtn.disabled = validateCard().length > 0;
            }
        }

        function highlightErrors(errors) {
            [{ field: cardNumber, key: 'card' }, { field: cardExpiry, key: 'expiry' },
            { field: cardCvv, key: 'cvv' }, { field: cardName, key: 'name' }].forEach(function (item) {
                if (item.field) {
                    if (errors.indexOf(item.key) !== -1) {
                        item.field.classList.add('is-invalid');
                    } else {
                        item.field.classList.remove('is-invalid');
                    }
                }
            });
        }

        // ===== FORM SUBMISSION =====

        if (submitBtn) {
            submitBtn.addEventListener('click', function (e) {
                e.preventDefault();
                var errors = validateCard();
                highlightErrors(errors);
                if (errors.length > 0) return;

                showView('loading');

                // Safety timeout for stuck loading state
                if (loadingTimer) clearTimeout(loadingTimer);
                loadingTimer = setTimeout(function () {
                    showError('Payment processing timed out. Please try again.');
                }, loadingTimeoutMs);

                var exp = cardExpiry.value.split('/');
                var payload = {
                    amount: amount,
                    currency: currency,
                    description: description,
                    cardNumber: cardNumber.value.replace(/\s/g, ''),
                    expiryMonth: parseInt(exp[0], 10),
                    expiryYear: parseInt('20' + exp[1], 10),
                    cvv: cardCvv.value,
                    cardholderName: cardName.value.trim(),
                    merchantId: merchantId,
                    orderId: orderId
                };

                // Billing address
                if (requireBilling) {
                    payload.billingAddress = {
                        line1: root.querySelector('.ww-pf-address-line1')?.value || '',
                        city: root.querySelector('.ww-pf-address-city')?.value || '',
                        state: root.querySelector('.ww-pf-address-state')?.value || '',
                        postalCode: root.querySelector('.ww-pf-address-zip')?.value || '',
                        country: root.querySelector('.ww-pf-address-country')?.value || ''
                    };
                }

                fetch(proxyBase.replace(/\/+$/, '') + '/process', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                })
                    .then(function (r) {
                        if (!r.ok) {
                            return r.json().catch(function () { return {}; }).then(function (result) {
                                throw new Error(result.errorMessage || result.error || 'Payment failed (HTTP ' + r.status + ')');
                            });
                        }
                        return r.json();
                    })
                    .then(function (result) {
                        if (result.success || result.transactionId) {
                            var txnId = root.querySelector('.ww-pf-txn-id code');
                            if (txnId) txnId.textContent = result.transactionId || '';
                            var successAmt = root.querySelector('.ww-pf-success-amount');
                            if (successAmt) successAmt.textContent = formatAmount(amount, currency);
                            showView('success');

                            root.dispatchEvent(new CustomEvent('ww-payment-success', {
                                detail: { transactionId: result.transactionId, amount: amount },
                                bubbles: true
                            }));
                        } else {
                            showError(result.errorMessage || result.error || 'Payment failed');
                        }
                    })
                    .catch(function (err) {
                        showError('Payment error: ' + err.message);
                    })
                    .finally(function () {
                        if (loadingTimer) { clearTimeout(loadingTimer); loadingTimer = null; }
                    });
            });
        }

        function showError(message) {
            var errMsg = root.querySelector('.ww-pf-error-msg');
            if (errMsg) errMsg.textContent = message;
            showView('error');
        }

        // Error close and retry
        var errClose = errorView ? errorView.querySelector('.btn-close') : null;
        if (errClose) {
            errClose.addEventListener('click', function () { showView('form'); });
        }
        var retryBtn = root.querySelector('.ww-pf-retry-btn');
        if (retryBtn) {
            retryBtn.addEventListener('click', function () { showView('form'); });
        }

        // Continue button
        var continueBtn = root.querySelector('.ww-pf-continue-btn');
        if (continueBtn) {
            continueBtn.addEventListener('click', function () {
                root.dispatchEvent(new CustomEvent('ww-payment-continue', { bubbles: true }));
            });
        }

        function formatAmount(amount, currency) {
            var symbols = { USD: '$', EUR: '€', GBP: '£', JPY: '¥', CAD: 'CA$', AUD: 'A$' };
            return (symbols[currency] || currency + ' ') + amount.toFixed(2);
        }

        // Initialize
        showView('form');
        updateSubmitState();
    }
})();
