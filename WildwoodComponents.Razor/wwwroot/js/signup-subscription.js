/**
 * WildwoodComponents.Razor - Signup With Subscription Component JavaScript
 * Handles the multi-step flow: plan selection → registration → payment → completion.
 * Razor Pages equivalent of the Blazor SignupWithSubscriptionComponent interactivity.
 */
(function () {
    'use strict';

    var roots = document.querySelectorAll('.ww-signup-subscription-component');
    for (var r = 0; r < roots.length; r++) {
        initSignupSubscription(roots[r]);
    }

    function initSignupSubscription(root) {
        var cid = root.dataset.componentId;
        var appId = root.dataset.appId;
        var authProxy = root.dataset.authProxy;
        var subProxy = root.dataset.subProxy;
        var paymentProxy = root.dataset.paymentProxy;
        var returnUrl = root.dataset.returnUrl;
        var allowRegistration = root.dataset.allowRegistration === 'true';
        var currency = root.dataset.currency || 'USD';

        var messageEl = root.querySelector('.ww-signup-sub-message');
        var loadingEl = root.querySelector('.ww-signup-sub-loading');
        var loadingMsg = root.querySelector('.ww-signup-sub-loading-msg');

        var currentStep = 1;
        var selectedTier = null;
        var selectedPricing = null;
        var registeredUser = null;

        // ===== HELPERS =====

        function showMessage(text, type) {
            if (!messageEl) return;
            messageEl.textContent = text;
            messageEl.className = 'ww-signup-sub-message alert alert-' + type;
            messageEl.style.display = '';
        }

        function clearMessage() {
            if (messageEl) messageEl.style.display = 'none';
        }

        function setLoading(loading, msg) {
            if (loadingEl) loadingEl.style.display = loading ? '' : 'none';
            if (loadingMsg && msg) loadingMsg.textContent = msg;
        }

        function goToStep(step) {
            currentStep = step;

            // Update step indicators
            var steps = root.querySelectorAll('.ww-step');
            for (var i = 0; i < steps.length; i++) {
                var stepNum = parseInt(steps[i].dataset.step, 10);
                steps[i].classList.remove('active', 'completed');
                if (stepNum < currentStep) steps[i].classList.add('completed');
                if (stepNum === currentStep) steps[i].classList.add('active');
            }

            // Update step lines
            var lines = root.querySelectorAll('.ww-step-line');
            for (var i = 0; i < lines.length; i++) {
                lines[i].classList.toggle('completed', i < currentStep - 1);
            }

            // Show/hide step views
            var stepViews = root.querySelectorAll('.ww-signup-step');
            for (var i = 0; i < stepViews.length; i++) {
                var viewStep = parseInt(stepViews[i].dataset.step, 10);
                stepViews[i].style.display = viewStep === currentStep ? '' : 'none';
            }

            // Show payment step indicator only for paid tiers
            var paymentStepEl = root.querySelector('.ww-step-payment');
            var paymentLine = root.querySelector('.ww-step-line-payment');
            var isPaid = selectedTier && selectedTier.isFree !== 'true';
            if (paymentStepEl) paymentStepEl.style.display = isPaid ? '' : 'none';
            if (paymentLine) paymentLine.style.display = isPaid ? '' : 'none';

            clearMessage();
        }

        // ===== STEP 1: PLAN SELECTION =====

        root.addEventListener('click', function (e) {
            var card = e.target.closest('.ww-plan-select-card');
            if (!card || currentStep !== 1) return;

            var btn = e.target.closest('.ww-select-plan-btn') || card.querySelector('.ww-select-plan-btn');
            // Also allow clicking the card itself
            if (!btn && !card) return;

            selectedTier = {
                id: card.dataset.tierId,
                name: card.dataset.tierName,
                isFree: card.dataset.isFree
            };

            // Find visible pricing option
            var visiblePricing = card.querySelector('.ww-price-option[style*="display"]') ||
                card.querySelector('.ww-price-option');
            if (visiblePricing) {
                selectedPricing = {
                    id: visiblePricing.dataset.pricingId,
                    price: parseFloat(visiblePricing.dataset.price || '0'),
                    billing: visiblePricing.dataset.billing
                };
            }

            goToStep(2);
        });

        // ===== STEP 2: REGISTRATION =====

        var registerForm = root.querySelector('.ww-register-form');
        if (registerForm) {
            registerForm.addEventListener('submit', function (e) {
                e.preventDefault();
                clearMessage();

                var emailInput = registerForm.querySelector('.ww-reg-email');
                var passwordInput = registerForm.querySelector('.ww-reg-password');
                var confirmInput = registerForm.querySelector('.ww-reg-confirm-password');
                var firstNameInput = registerForm.querySelector('.ww-reg-first-name');
                var lastNameInput = registerForm.querySelector('.ww-reg-last-name');

                var email = emailInput ? emailInput.value.trim() : '';
                var password = passwordInput ? passwordInput.value : '';
                var confirm = confirmInput ? confirmInput.value : '';
                var firstName = firstNameInput ? firstNameInput.value.trim() : '';
                var lastName = lastNameInput ? lastNameInput.value.trim() : '';

                // Validation
                if (!email) { showMessage('Email is required.', 'danger'); return; }
                if (!password) { showMessage('Password is required.', 'danger'); return; }
                if (password !== confirm) { showMessage('Passwords do not match.', 'danger'); return; }
                if (password.length < 6) { showMessage('Password must be at least 6 characters.', 'danger'); return; }

                setLoading(true, 'Creating account...');

                fetch(authProxy + '/register', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        email: email,
                        password: password,
                        confirmPassword: confirm,
                        firstName: firstName,
                        lastName: lastName,
                        appId: appId
                    })
                })
                    .then(function (r) { return r.json(); })
                    .then(function (result) {
                        setLoading(false);
                        if (result.jwtToken || result.success) {
                            registeredUser = result;

                            if (selectedTier && selectedTier.isFree !== 'true' && selectedPricing) {
                                goToStep(3); // Payment needed
                            } else {
                                // Free tier - subscribe directly
                                subscribeTier();
                            }
                        } else {
                            showMessage(result.message || result.error || 'Registration failed.', 'danger');
                        }
                    })
                    .catch(function (err) {
                        setLoading(false);
                        showMessage('Registration error: ' + err.message, 'danger');
                    });
            });
        }

        // ===== STEP 3: PAYMENT (delegated to PaymentForm if embedded) =====

        // Listen for payment success from embedded payment form
        root.addEventListener('ww-payment-success', function (e) {
            subscribeTier(e.detail.transactionId);
        });

        // ===== SUBSCRIBE =====

        function subscribeTier(paymentTransactionId) {
            setLoading(true, 'Activating subscription...');

            var body = {
                appId: appId,
                appTierId: selectedTier.id,
                appTierPricingId: selectedPricing ? selectedPricing.id : null,
                paymentTransactionId: paymentTransactionId || null
            };

            fetch(subProxy + '/subscribe', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            })
                .then(function (r) {
                    if (!r.ok) throw new Error('Subscription failed');
                    return r.json();
                })
                .then(function () {
                    setLoading(false);
                    goToStep(4); // Complete

                    root.dispatchEvent(new CustomEvent('ww-signup-complete', {
                        detail: {
                            tierId: selectedTier.id,
                            tierName: selectedTier.name,
                            user: registeredUser
                        },
                        bubbles: true
                    }));
                })
                .catch(function (err) {
                    setLoading(false);
                    showMessage('Subscription failed: ' + err.message, 'danger');
                });
        }

        // ===== STEP 4: COMPLETION =====

        var continueBtn = root.querySelector('.ww-signup-continue-btn');
        if (continueBtn) {
            continueBtn.addEventListener('click', function () {
                if (returnUrl) {
                    window.location.href = returnUrl;
                } else {
                    window.location.reload();
                }
            });
        }

        // ===== BACK BUTTONS =====

        root.addEventListener('click', function (e) {
            var backBtn = e.target.closest('.ww-signup-back-btn');
            if (!backBtn) return;
            if (currentStep > 1) {
                goToStep(currentStep - 1);
            }
        });

        // ===== BILLING TOGGLE (on step 1) =====

        var billingBtns = root.querySelectorAll('.ww-billing-btn');
        for (var i = 0; i < billingBtns.length; i++) {
            billingBtns[i].addEventListener('click', function () {
                var cycle = this.dataset.cycle;
                for (var j = 0; j < billingBtns.length; j++) {
                    if (billingBtns[j].dataset.cycle === cycle) {
                        billingBtns[j].classList.remove('btn-outline-primary');
                        billingBtns[j].classList.add('btn-primary');
                    } else {
                        billingBtns[j].classList.remove('btn-primary');
                        billingBtns[j].classList.add('btn-outline-primary');
                    }
                }

                // Show/hide pricing options based on billing cycle
                var priceOptions = root.querySelectorAll('.ww-price-option');
                for (var j = 0; j < priceOptions.length; j++) {
                    priceOptions[j].style.display = 'none';
                }

                var cards = root.querySelectorAll('.ww-plan-select-card');
                for (var j = 0; j < cards.length; j++) {
                    var options = cards[j].querySelectorAll('.ww-price-option');
                    var shown = false;
                    for (var k = 0; k < options.length; k++) {
                        if (options[k].dataset.billing === cycle) {
                            options[k].style.display = '';
                            shown = true;
                            break;
                        }
                    }
                    if (!shown && options.length > 0) {
                        // Show default or first
                        for (var k = 0; k < options.length; k++) {
                            if (options[k].dataset.isDefault === 'true') {
                                options[k].style.display = '';
                                shown = true;
                                break;
                            }
                        }
                        if (!shown) options[0].style.display = '';
                    }
                }
            });
        }

        // Initialize
        goToStep(1);

        // Show default pricing
        var defaultPricing = root.querySelectorAll('.ww-price-option[data-is-default="true"]');
        for (var i = 0; i < defaultPricing.length; i++) {
            defaultPricing[i].style.display = '';
        }
        // If no defaults, show first pricing per card
        if (defaultPricing.length === 0) {
            var cards = root.querySelectorAll('.ww-plan-select-card');
            for (var i = 0; i < cards.length; i++) {
                var first = cards[i].querySelector('.ww-price-option');
                if (first) first.style.display = '';
            }
        }
    }
})();
