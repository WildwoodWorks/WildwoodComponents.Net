/**
 * WildwoodComponents.Razor - Pricing Display Component JavaScript
 * Handles billing cycle toggle and tier selection for the read-only pricing display.
 * Razor Pages equivalent of the Blazor PricingDisplayComponent interactivity.
 */
(function () {
    'use strict';

    var instances = {};

    function initInstance(root) {
        var cid = root.dataset.componentId;
        if (!cid || instances[cid]) return;

        var config = {
            appId: root.dataset.appId,
            currency: root.dataset.currency || 'USD',
            preSelected: root.dataset.preSelected || ''
        };

        var currentCycle = 'monthly';

        // ===== BILLING CYCLE TOGGLE =====

        function updatePriceDisplay(cycle) {
            currentCycle = cycle;

            // Update toggle buttons
            var btns = root.querySelectorAll('.ww-pricing-billing-btn');
            for (var i = 0; i < btns.length; i++) {
                var btn = btns[i];
                if (btn.dataset.cycle === cycle) {
                    btn.classList.remove('btn-outline-primary');
                    btn.classList.add('btn-primary');
                } else {
                    btn.classList.remove('btn-primary');
                    btn.classList.add('btn-outline-primary');
                }
            }

            // Show/hide price options
            var prices = root.querySelectorAll('.ww-price-option');
            for (var j = 0; j < prices.length; j++) {
                var el = prices[j];
                var billing = el.dataset.billing;
                var isDefault = el.dataset.isDefault === 'true';

                if (billing === cycle) {
                    el.style.display = '';
                } else if (!billing) {
                    // No billing attribute — always show (e.g. free tier)
                    el.style.display = '';
                } else {
                    el.style.display = 'none';
                }
            }

            // For tiers that don't have a price option for the selected cycle,
            // show the default option as fallback
            var cards = root.querySelectorAll('.ww-pricing-card');
            for (var k = 0; k < cards.length; k++) {
                var card = cards[k];
                var options = card.querySelectorAll('.ww-price-option');
                var anyVisible = false;
                for (var m = 0; m < options.length; m++) {
                    if (options[m].style.display !== 'none') {
                        anyVisible = true;
                        break;
                    }
                }
                if (!anyVisible) {
                    // Show default or first option as fallback
                    for (var n = 0; n < options.length; n++) {
                        if (options[n].dataset.isDefault === 'true') {
                            options[n].style.display = '';
                            anyVisible = true;
                            break;
                        }
                    }
                    if (!anyVisible && options.length > 0) {
                        options[0].style.display = '';
                    }
                }
            }
        }

        // ===== EVENT DELEGATION =====

        root.addEventListener('click', function (e) {
            var target = e.target.closest('[data-cycle]');
            if (target && target.classList.contains('ww-pricing-billing-btn')) {
                updatePriceDisplay(target.dataset.cycle);
                return;
            }

            // Tier selection (if consuming app wants to handle it)
            var card = e.target.closest('.ww-pricing-card');
            if (card) {
                var selectBtn = e.target.closest('.ww-select-tier-btn');
                if (selectBtn) {
                    var tierId = card.dataset.tierId;
                    var tierName = card.dataset.tierName;
                    root.dispatchEvent(new CustomEvent('ww-pricing-tier-selected', {
                        bubbles: true,
                        detail: {
                            tierId: tierId,
                            tierName: tierName,
                            billingCycle: currentCycle,
                            appId: config.appId
                        }
                    }));
                }
            }
        });

        // ===== INITIALIZATION =====

        // Show initial prices (monthly by default)
        updatePriceDisplay('monthly');

        instances[cid] = {
            setBillingCycle: function (cycle) {
                updatePriceDisplay(cycle);
            },
            getCurrentCycle: function () {
                return currentCycle;
            }
        };
    }

    // ===== AUTO-INIT =====

    function initAll() {
        var roots = document.querySelectorAll('.ww-pricing-display-component');
        for (var i = 0; i < roots.length; i++) {
            initInstance(roots[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAll);
    } else {
        initAll();
    }

    // Public API
    window.wwPricingDisplay = {
        init: initInstance,
        initAll: initAll,
        getInstance: function (componentId) {
            return instances[componentId] || null;
        }
    };
})();
