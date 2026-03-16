/**
 * WildwoodComponents.Razor - Subscription & Subscription Manager Component JavaScript
 * Handles plan browsing, billing cycle toggle, subscription/cancellation/upgrade,
 * and invoice management.
 * Razor Pages equivalent of the Blazor SubscriptionComponent and SubscriptionManagerComponent interactivity.
 */
(function () {
    'use strict';

    // ===== SUBSCRIPTION COMPONENT (Plan Selection) =====

    var subRoots = document.querySelectorAll('.ww-subscription-component');
    for (var r = 0; r < subRoots.length; r++) {
        initSubscription(subRoots[r]);
    }

    function initSubscription(root) {
        var cid = root.dataset.componentId;
        var proxyBase = root.dataset.proxyBase;
        var currency = root.dataset.currency || 'USD';
        var annualDiscount = parseInt(root.dataset.annualDiscount || '0', 10);

        var currentBillingCycle = 'monthly';
        var messageEl = root.querySelector('.ww-subscription-message');
        var loadingEl = root.querySelector('.ww-subscription-loading');

        function showMessage(text, type) {
            if (!messageEl) return;
            messageEl.textContent = text;
            messageEl.className = 'ww-subscription-message alert alert-' + type;
            messageEl.style.display = '';
            setTimeout(function () { messageEl.style.display = 'none'; }, 5000);
        }

        function setLoading(loading) {
            if (loadingEl) loadingEl.style.display = loading ? '' : 'none';
        }

        // Billing cycle toggle
        var billingBtns = root.querySelectorAll('.ww-billing-btn');
        for (var i = 0; i < billingBtns.length; i++) {
            billingBtns[i].addEventListener('click', function () {
                currentBillingCycle = this.dataset.cycle;
                for (var j = 0; j < billingBtns.length; j++) {
                    if (billingBtns[j].dataset.cycle === currentBillingCycle) {
                        billingBtns[j].classList.remove('btn-outline-primary');
                        billingBtns[j].classList.add('btn-primary');
                    } else {
                        billingBtns[j].classList.remove('btn-primary');
                        billingBtns[j].classList.add('btn-outline-primary');
                    }
                }
                updatePlanVisibility();
            });
        }

        function updatePlanVisibility() {
            var cards = root.querySelectorAll('.ww-plan-card');
            for (var i = 0; i < cards.length; i++) {
                var billing = cards[i].dataset.billing;
                // Show all cards, but highlight matching billing cycle
                if (billing && billing !== currentBillingCycle && billing !== 'free') {
                    cards[i].closest('.col-md-4').style.display = 'none';
                } else {
                    cards[i].closest('.col-md-4').style.display = '';
                }
            }
        }

        // Plan selection
        root.addEventListener('click', function (e) {
            var btn = e.target.closest('.ww-select-plan-btn');
            if (!btn) return;
            var planId = btn.dataset.planId;
            var planName = btn.dataset.planName || 'this plan';

            setLoading(true);

            fetch(proxyBase + '/subscribe', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ planId: planId })
            })
                .then(function (r) {
                    if (!r.ok) throw new Error('Subscription failed');
                    return r.json();
                })
                .then(function (result) {
                    showMessage('Successfully subscribed to ' + planName + '!', 'success');
                    root.dispatchEvent(new CustomEvent('ww-subscription-changed', {
                        detail: { action: 'subscribed', planId: planId },
                        bubbles: true
                    }));
                    setTimeout(function () { window.location.reload(); }, 1500);
                })
                .catch(function (err) {
                    showMessage('Failed to subscribe: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });
    }

    // ===== SUBSCRIPTION MANAGER COMPONENT =====

    var mgrRoots = document.querySelectorAll('.ww-subscription-manager-component');
    for (var r = 0; r < mgrRoots.length; r++) {
        initSubscriptionManager(mgrRoots[r]);
    }

    function initSubscriptionManager(root) {
        var cid = root.dataset.componentId;
        var proxyBase = root.dataset.proxyBase;

        var messageEl = root.querySelector('.ww-sub-manager-message');
        var loadingEl = root.querySelector('.ww-sub-manager-loading');

        function showMessage(text, type) {
            if (!messageEl) return;
            messageEl.textContent = text;
            messageEl.className = 'ww-sub-manager-message alert alert-' + type;
            messageEl.style.display = '';
            setTimeout(function () { messageEl.style.display = 'none'; }, 5000);
        }

        function setLoading(loading) {
            if (loadingEl) loadingEl.style.display = loading ? '' : 'none';
        }

        // Cancel subscription
        var cancelBtn = root.querySelector('.ww-cancel-sub-btn');
        var confirmCancelBtn = root.querySelector('.ww-confirm-cancel-btn');
        var cancelView = root.querySelector('.ww-cancel-confirmation-view');
        var detailsView = root.querySelector('.ww-sub-details-view');

        if (cancelBtn) {
            cancelBtn.addEventListener('click', function () {
                if (cancelView) cancelView.style.display = '';
                if (detailsView) detailsView.style.display = 'none';
            });
        }

        var keepBtn = root.querySelector('.ww-keep-sub-btn');
        if (keepBtn) {
            keepBtn.addEventListener('click', function () {
                if (cancelView) cancelView.style.display = 'none';
                if (detailsView) detailsView.style.display = '';
            });
        }

        if (confirmCancelBtn) {
            confirmCancelBtn.addEventListener('click', function () {
                setLoading(true);

                fetch(proxyBase + '/cancel', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' }
                })
                    .then(function (r) {
                        if (!r.ok) throw new Error('Cancellation failed');
                        showMessage('Subscription cancelled successfully.', 'success');
                        root.dispatchEvent(new CustomEvent('ww-subscription-changed', {
                            detail: { action: 'cancelled' },
                            bubbles: true
                        }));
                        setTimeout(function () { window.location.reload(); }, 1500);
                    })
                    .catch(function (err) {
                        showMessage('Failed to cancel: ' + err.message, 'danger');
                        if (cancelView) cancelView.style.display = 'none';
                        if (detailsView) detailsView.style.display = '';
                    })
                    .finally(function () {
                        setLoading(false);
                    });
            });
        }

        // Change plan
        root.addEventListener('click', function (e) {
            var btn = e.target.closest('.ww-change-plan-btn');
            if (!btn) return;
            var planId = btn.dataset.planId;
            if (!planId) return;

            if (!confirm('Are you sure you want to change your plan?')) return;

            setLoading(true);
            fetch(proxyBase + '/change-plan', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ planId: planId })
            })
                .then(function (r) {
                    if (!r.ok) throw new Error('Plan change failed');
                    showMessage('Plan changed successfully!', 'success');
                    setTimeout(function () { window.location.reload(); }, 1500);
                })
                .catch(function (err) {
                    showMessage('Failed to change plan: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // Invoice download
        root.addEventListener('click', function (e) {
            var btn = e.target.closest('.ww-download-invoice-btn');
            if (!btn) return;
            var invoiceId = btn.dataset.invoiceId;
            if (invoiceId) {
                window.open(proxyBase + '/invoices/' + invoiceId + '/download', '_blank');
            }
        });
    }
})();
