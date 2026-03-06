/**
 * WildwoodComponents.Razor - App Tier Component JavaScript
 * Handles billing cycle toggles, tier selection, subscription actions, and view transitions.
 * Razor Pages equivalent of the Blazor AppTierComponent interactivity.
 */
(function () {
    'use strict';

    const root = document.getElementById('ww-app-tier-component');
    if (!root) return;

    const config = {
        appId: root.dataset.appId,
        proxyUrl: root.dataset.proxyUrl,
        currency: root.dataset.currency || 'USD',
        annualDiscount: parseInt(root.dataset.annualDiscount || '20', 10),
        allowRegistration: root.dataset.allowRegistration === 'true'
    };

    let currentBillingCycle = 'monthly';

    // ===== VIEW MANAGEMENT =====

    function showView(viewId) {
        const views = root.querySelectorAll('.ww-tier-view');
        for (let i = 0; i < views.length; i++) {
            views[i].style.display = 'none';
        }
        const target = document.getElementById(viewId);
        if (target) target.style.display = '';
    }

    function showMessage(text, type) {
        const msg = document.getElementById('ww-tier-message');
        if (!msg) return;
        msg.textContent = text;
        msg.className = 'ww-alert ww-alert-' + type;
        msg.style.display = '';
        setTimeout(function () { msg.style.display = 'none'; }, 5000);
    }

    function clearMessage() {
        const msg = document.getElementById('ww-tier-message');
        if (msg) msg.style.display = 'none';
    }

    // ===== BILLING CYCLE TOGGLE =====

    function updatePriceDisplay(cycle) {
        currentBillingCycle = cycle;

        // Update toggle buttons
        const toggleBtns = root.querySelectorAll('.ww-billing-btn');
        for (let i = 0; i < toggleBtns.length; i++) {
            var btn = toggleBtns[i];
            if (btn.dataset.cycle === cycle) {
                btn.classList.remove('btn-outline-primary');
                btn.classList.add('btn-primary');
            } else {
                btn.classList.remove('btn-primary');
                btn.classList.add('btn-outline-primary');
            }
        }

        // Update price displays on each tier card
        var priceOptions = root.querySelectorAll('.ww-price-option');
        for (let i = 0; i < priceOptions.length; i++) {
            var opt = priceOptions[i];
            opt.style.display = 'none';
        }

        var tierCards = root.querySelectorAll('.ww-tier-card');
        for (let i = 0; i < tierCards.length; i++) {
            var card = tierCards[i];
            var options = card.querySelectorAll('.ww-price-option');
            var shown = false;

            // Show matching billing cycle price
            for (let j = 0; j < options.length; j++) {
                if (options[j].dataset.billing === cycle) {
                    options[j].style.display = '';
                    shown = true;
                    break;
                }
            }

            // Fall back to default or first
            if (!shown) {
                for (let j = 0; j < options.length; j++) {
                    if (options[j].dataset.isDefault === 'true') {
                        options[j].style.display = '';
                        shown = true;
                        break;
                    }
                }
            }
            if (!shown && options.length > 0) {
                options[0].style.display = '';
            }
        }
    }

    // ===== TIER SELECTION =====

    function selectTier(tierId, tierName, isFree) {
        clearMessage();

        if (isFree) {
            subscribeTier(tierId, null);
        } else {
            // For paid tiers, find the visible pricing option
            var card = root.querySelector('.ww-tier-card[data-tier-id="' + tierId + '"]');
            var pricingId = null;
            if (card) {
                var visiblePricing = card.querySelectorAll('.ww-price-option');
                for (let i = 0; i < visiblePricing.length; i++) {
                    if (visiblePricing[i].style.display !== 'none') {
                        pricingId = visiblePricing[i].dataset.pricingId;
                        break;
                    }
                }
            }

            // Dispatch event so consuming app can handle payment
            var event = new CustomEvent('ww-tier-selected', {
                detail: {
                    tierId: tierId,
                    tierName: tierName,
                    pricingId: pricingId,
                    billingCycle: currentBillingCycle,
                    isFree: false
                },
                bubbles: true
            });
            root.dispatchEvent(event);
        }
    }

    // ===== API CALLS =====

    function subscribeTier(tierId, pricingId, paymentTransactionId) {
        var body = {
            appId: config.appId,
            appTierId: tierId,
            appTierPricingId: pricingId || null,
            paymentTransactionId: paymentTransactionId || null
        };

        fetch(config.proxyUrl + '/subscribe', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        })
            .then(function (response) { return response.json(); })
            .then(function (result) {
                if (result.success) {
                    var successName = document.getElementById('ww-success-tier-name');
                    if (successName) successName.textContent = 'Welcome!';
                    showView('ww-tier-success-view');

                    var event = new CustomEvent('ww-subscription-changed', {
                        detail: { action: 'subscribed', tierId: tierId },
                        bubbles: true
                    });
                    root.dispatchEvent(event);
                } else {
                    showMessage(result.errorMessage || 'Subscription failed.', 'danger');
                }
            })
            .catch(function (err) {
                showMessage('Error: ' + err.message, 'danger');
            });
    }

    function cancelSubscription() {
        var btn = document.getElementById('ww-confirm-cancel-btn');
        if (btn) {
            btn.querySelector('.ww-btn-text').style.display = 'none';
            btn.querySelector('.ww-btn-spinner').style.display = '';
            btn.disabled = true;
        }

        fetch(config.proxyUrl + '/cancel', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        })
            .then(function (response) {
                if (response.ok) {
                    showMessage('Subscription cancelled successfully.', 'success');
                    showView('ww-tier-selection-view');

                    // Hide current subscription section
                    var subSection = document.getElementById('ww-current-subscription');
                    if (subSection) subSection.style.display = 'none';

                    var event = new CustomEvent('ww-subscription-changed', {
                        detail: { action: 'cancelled' },
                        bubbles: true
                    });
                    root.dispatchEvent(event);
                } else {
                    showMessage('Failed to cancel subscription.', 'danger');
                    showView('ww-tier-selection-view');
                }
            })
            .catch(function (err) {
                showMessage('Error: ' + err.message, 'danger');
                showView('ww-tier-selection-view');
            })
            .finally(function () {
                if (btn) {
                    btn.querySelector('.ww-btn-text').style.display = '';
                    btn.querySelector('.ww-btn-spinner').style.display = 'none';
                    btn.disabled = false;
                }
            });
    }

    // ===== EVENT BINDING =====

    // Billing cycle toggle
    var billingBtns = root.querySelectorAll('.ww-billing-btn');
    for (let i = 0; i < billingBtns.length; i++) {
        billingBtns[i].addEventListener('click', function () {
            updatePriceDisplay(this.dataset.cycle);
        });
    }

    // Tier select buttons
    var selectBtns = root.querySelectorAll('.ww-select-tier-btn');
    for (let i = 0; i < selectBtns.length; i++) {
        selectBtns[i].addEventListener('click', function () {
            selectTier(
                this.dataset.tierId,
                this.dataset.tierName,
                this.dataset.isFree === 'true'
            );
        });
    }

    // Cancel subscription
    var cancelBtn = document.getElementById('ww-cancel-subscription-btn');
    if (cancelBtn) {
        cancelBtn.addEventListener('click', function () {
            showView('ww-cancel-confirmation-view');
        });
    }

    // Keep subscription (back from cancel)
    var keepBtn = document.getElementById('ww-keep-subscription-btn');
    if (keepBtn) {
        keepBtn.addEventListener('click', function () {
            showView('ww-tier-selection-view');
        });
    }

    // Confirm cancel
    var confirmCancelBtn = document.getElementById('ww-confirm-cancel-btn');
    if (confirmCancelBtn) {
        confirmCancelBtn.addEventListener('click', cancelSubscription);
    }

    // Success continue
    var successBtn = document.getElementById('ww-success-continue-btn');
    if (successBtn) {
        successBtn.addEventListener('click', function () {
            showView('ww-tier-selection-view');
            window.location.reload();
        });
    }

    // ===== PUBLIC API for consuming apps =====

    window.wwAppTier = {
        subscribeTier: subscribeTier,
        showView: showView,
        showMessage: showMessage
    };

    // Initialize default pricing display
    updatePriceDisplay('monthly');

})();
