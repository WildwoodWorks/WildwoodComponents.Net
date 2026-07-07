/**
 * WildwoodComponents.Razor - Subscription Admin Component JavaScript
 * Handles tab switching, tier selection, cancellation, feature toggling,
 * override management, add-on subscribe/cancel, and usage limit editing.
 * Razor Pages equivalent of the Blazor SubscriptionAdminComponent interactivity.
 */
(function () {
    'use strict';

    var roots = document.querySelectorAll('.ww-subscription-admin-component');
    for (var r = 0; r < roots.length; r++) {
        if (!roots[r]._wwSubAdminInit) {
            roots[r]._wwSubAdminInit = true;
            initSubscriptionAdmin(roots[r]);
        }
    }

    function initSubscriptionAdmin(root) {
        var cid = root.dataset.componentId;
        var appId = root.dataset.appId;
        var proxyUrl = (root.dataset.proxyUrl || '').replace(/\/+$/, '');
        var companyId = root.dataset.companyId || '';
        var userId = root.dataset.userId || '';
        var isAdmin = root.dataset.isAdmin === 'true';
        var isCompanyMode = root.dataset.isCompanyMode === 'true';
        var currency = root.dataset.currency || 'USD';

        var messageEl = root.querySelector('.ww-sub-admin-message');
        var loadingEl = root.querySelector('.ww-sub-admin-loading');
        var loadingMsg = root.querySelector('.ww-sub-admin-loading-msg');

        // ===== HELPERS =====

        function showMessage(text, type) {
            if (!messageEl) return;
            messageEl.textContent = text;
            messageEl.className = 'ww-sub-admin-message alert alert-' + type;
            messageEl.style.display = '';
            setTimeout(function () { if (messageEl) messageEl.style.display = 'none'; }, 5000);
        }

        // Like showMessage, but supports a trailing link and does not auto-hide — used for
        // store-billing follow-up instructions the user must be able to read and click.
        function showPersistentMessage(text, type, linkUrl, linkText) {
            if (!messageEl) return;
            messageEl.textContent = text;
            if (linkUrl) {
                var link = document.createElement('a');
                link.href = linkUrl;
                link.target = '_blank';
                link.rel = 'noopener noreferrer';
                link.className = 'alert-link ms-1';
                link.textContent = linkText || linkUrl;
                messageEl.appendChild(document.createTextNode(' '));
                messageEl.appendChild(link);
            }
            messageEl.className = 'ww-sub-admin-message alert alert-' + type;
            messageEl.style.display = '';
        }

        function setLoading(loading, msg) {
            if (loadingEl) loadingEl.style.display = loading ? '' : 'none';
            if (loadingMsg && msg) loadingMsg.textContent = msg;
        }

        function scopeParams() {
            var useCompany = isCompanyMode && companyId;
            var useUser = !isCompanyMode && userId;
            return { useCompany: useCompany, useUser: useUser };
        }

        function buildProxyUrl(path) {
            return proxyUrl + '/' + path.replace(/^\//, '');
        }

        function apiPost(path, body) {
            return fetch(buildProxyUrl(path), {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: body ? JSON.stringify(body) : null
            }).then(function (r) {
                if (!r.ok) {
                    return r.text().catch(function () { return ''; }).then(function (text) {
                        throw new Error(text || 'Request failed (HTTP ' + r.status + ')');
                    });
                }
                return r.json().catch(function () { return { success: true }; });
            });
        }

        function apiPut(path, body) {
            return fetch(buildProxyUrl(path), {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: body ? JSON.stringify(body) : null
            }).then(function (r) {
                if (!r.ok) throw new Error('Request failed (HTTP ' + r.status + ')');
                return r.json().catch(function () { return { success: true }; });
            });
        }

        function apiDelete(path) {
            return fetch(buildProxyUrl(path), {
                method: 'DELETE'
            }).then(function (r) {
                if (!r.ok) throw new Error('Request failed (HTTP ' + r.status + ')');
                return { success: true };
            });
        }

        // ===== TAB SWITCHING =====

        root.addEventListener('click', function (e) {
            var tab = e.target.closest('.ww-sub-admin-tab');
            if (!tab) return;

            var tabName = tab.dataset.tab;
            var tabs = root.querySelectorAll('.ww-sub-admin-tab');
            for (var i = 0; i < tabs.length; i++) {
                tabs[i].classList.toggle('active', tabs[i].dataset.tab === tabName);
            }

            var panels = root.querySelectorAll('.ww-sub-admin-panel');
            for (var i = 0; i < panels.length; i++) {
                panels[i].classList.toggle('d-none', panels[i].dataset.panel !== tabName);
            }
        });

        // ===== SUBSCRIPTION STATUS: CANCEL =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="cancel-subscription"]');
            if (!btn) return;

            var statusPanel = btn.closest('.ww-sub-status-panel');
            if (!statusPanel) return;
            var panelCid = statusPanel.dataset.componentId;
            var confirmEl = document.getElementById('ww-cancel-confirm-' + panelCid);
            if (confirmEl) confirmEl.style.display = '';
        });

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="keep-subscription"]');
            if (!btn) return;

            var statusPanel = btn.closest('.ww-sub-status-panel');
            if (!statusPanel) return;
            var panelCid = statusPanel.dataset.componentId;
            var confirmEl = document.getElementById('ww-cancel-confirm-' + panelCid);
            if (confirmEl) confirmEl.style.display = 'none';
        });

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="confirm-cancel"]');
            if (!btn) return;

            setLoading(true, 'Cancelling subscription...');

            var scope = scopeParams();
            var path;
            if (scope.useCompany) {
                path = appId + '/cancel/company/' + companyId;
            } else if (scope.useUser) {
                path = appId + '/cancel/' + userId;
            } else {
                path = appId + '/my-subscription/cancel';
            }

            apiPost(path)
                .then(function (result) {
                    // The cancel endpoints report failures via success/errorMessage on a 2xx
                    // body too — surface them instead of celebrating a failed cancel.
                    if (result && result.success === false) {
                        showMessage('Failed to cancel: ' + (result.errorMessage || 'Unknown error'), 'danger');
                        return;
                    }

                    var scheduled = result && result.isScheduled;
                    var message = scheduled
                        ? (result.effectiveDate
                            ? 'Your cancellation is scheduled — access continues until ' + new Date(result.effectiveDate).toLocaleDateString() + '.'
                            : 'Your cancellation is scheduled for the end of the current billing period.')
                        : 'Your subscription has been cancelled.';

                    dispatchChanged('cancelled');

                    if (result && result.requiresUserAction) {
                        // Store-billed subscription (App Store / Google Play): the platform
                        // cannot stop the store's billing, so keep the instructions on screen
                        // and give the user time to follow them before the page reloads.
                        showPersistentMessage(
                            message + ' ' + (result.userActionInstructions || 'Also cancel the subscription in your store settings.'),
                            'warning', result.userActionUrl, 'Manage your store subscription');
                        setTimeout(function () { window.location.reload(); }, 10000);
                    } else {
                        showMessage(message, 'success');
                        setTimeout(function () { window.location.reload(); }, 1500);
                    }
                })
                .catch(function (err) {
                    showMessage('Failed to cancel: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== TIER PLANS: SELECT TIER =====
        // For an existing subscription a tier click previews the change and shows a
        // confirmation modal (preview -> modal -> confirm). New subscriptions execute
        // directly. Mirrors the Blazor SubscriptionAdminComponent / React flow.

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="select-tier"]');
            if (!btn) return;

            var tierId = btn.dataset.tierId;
            var tierName = btn.dataset.tierName;
            var isChange = btn.dataset.isChange === 'true';
            var isFree = btn.dataset.isFree === 'true';

            // Find visible pricing option
            var card = btn.closest('.ww-admin-plan-card');
            var pricingId = null;
            if (card && !isFree) {
                var visiblePrice = card.querySelector('.ww-admin-price-option:not(.d-none)');
                if (visiblePrice) pricingId = visiblePrice.dataset.pricingId;
            }

            if (isChange) {
                // Preview the change, then show the confirmation modal.
                setLoading(true, 'Loading preview...');

                var scope = scopeParams();
                // Known limitation (mirrors @wildwood/core / React useSubscriptionAdmin):
                // there is no company-scoped preview endpoint, so in company mode the
                // preview falls back to the self endpoint. The change itself is still
                // routed company-scoped in executeTierChange. In Company tracking the API
                // typically resolves "my-subscription" to the caller's company, so this is
                // only inaccurate when an admin previews a company other than their own.
                var previewPath = scope.useUser
                    ? appId + '/admin/preview-change/' + userId
                    : appId + '/my-subscription/preview-change';

                apiPost(previewPath, { NewAppTierId: tierId, NewAppTierPricingId: pricingId })
                    .then(function (preview) {
                        if (!preview || preview.success === false) {
                            showMessage((preview && preview.errorMessage) || 'Failed to preview tier change.', 'danger');
                            return;
                        }
                        openTierChangeModal(preview, { tierId: tierId, tierName: tierName, pricingId: pricingId, isChange: true });
                    })
                    .catch(function (err) {
                        showMessage('Failed to preview: ' + err.message, 'danger');
                    })
                    .finally(function () {
                        setLoading(false);
                    });
            } else {
                // New subscription - confirm and execute directly (no preview).
                if (!confirm('Subscribe to ' + tierName + '?')) return;
                executeTierChange({ tierId: tierId, tierName: tierName, pricingId: pricingId, isChange: false }, true);
            }
        });

        // Performs the actual subscribe/change request. Resolves to true on success,
        // false on failure (the failure message is shown before resolving). Never rejects,
        // so callers can ignore the result without risking an unhandled rejection.
        function executeTierChange(ctx, immediate) {
            setLoading(true, ctx.isChange ? 'Changing plan...' : 'Subscribing...');

            var scope = scopeParams();
            var path, body;

            if (ctx.isChange) {
                if (scope.useCompany) {
                    path = appId + '/change-tier/company';
                    body = { CompanyId: companyId, NewAppTierId: ctx.tierId, NewAppTierPricingId: ctx.pricingId, Immediate: immediate };
                } else if (scope.useUser) {
                    path = 'change-tier';
                    body = { UserId: userId, AppId: appId, NewAppTierId: ctx.tierId, NewAppTierPricingId: ctx.pricingId, Immediate: immediate };
                } else {
                    path = appId + '/my-subscription/change';
                    body = { NewAppTierId: ctx.tierId, NewAppTierPricingId: ctx.pricingId, Immediate: immediate };
                }
            } else {
                if (scope.useCompany) {
                    path = appId + '/subscribe/company';
                    body = { CompanyId: companyId, AppTierId: ctx.tierId, AppTierPricingId: ctx.pricingId };
                } else if (scope.useUser) {
                    path = 'subscribe';
                    body = { UserId: userId, AppId: appId, AppTierId: ctx.tierId, AppTierPricingId: ctx.pricingId };
                } else {
                    path = appId + '/my-subscription';
                    body = { AppTierId: ctx.tierId, AppTierPricingId: ctx.pricingId };
                }
            }

            return apiPost(path, body)
                .then(function (result) {
                    if (result.success === false) {
                        showMessage(result.errorMessage || 'Tier change failed.', 'danger');
                        return false;
                    }
                    showMessage('Successfully ' + (ctx.isChange ? 'changed to ' : 'subscribed to ') + ctx.tierName + '!', 'success');
                    dispatchChanged(ctx.isChange ? 'changed' : 'subscribed');
                    setTimeout(function () { window.location.reload(); }, 1500);
                    return true;
                })
                .catch(function (err) {
                    showMessage('Failed: ' + err.message, 'danger');
                    return false;
                })
                .finally(function () {
                    setLoading(false);
                });
        }

        // ===== TIER CHANGE CONFIRMATION MODAL =====

        function formatCurrency(amount, ccy) {
            if (amount === null || amount === undefined) return '$0.00';
            try {
                return new Intl.NumberFormat('en-US', { style: 'currency', currency: ccy || 'USD' }).format(amount);
            } catch (e) {
                return '$' + Number(amount).toFixed(2);
            }
        }

        function el(tag, className, text) {
            var node = document.createElement(tag);
            if (className) node.className = className;
            if (text !== undefined && text !== null) node.textContent = text;
            return node;
        }

        function openTierChangeModal(preview, ctx) {
            // Downgrades default to end-of-period; upgrades/other default to immediate.
            var state = { immediate: !preview.isDowngrade, bypassPayment: false, loading: false };

            var overlay = el('div', 'ww-modal-overlay');
            overlay.addEventListener('click', function (e) {
                if (e.target === overlay && !state.loading) close();
            });

            function close() {
                if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
            }

            function submitChange() {
                state.loading = true;
                render();
                executeTierChange(ctx, state.immediate).then(function (ok) {
                    // On failure the message was already shown; close the modal so it is
                    // visible (it renders in the component, beneath the overlay). On success
                    // the page reloads shortly, so leave the modal up.
                    if (!ok) close();
                });
            }

            function render() {
                var ccy = preview.currency || currency;
                var effectivePaymentRequired = preview.paymentRequired && !state.bypassPayment;
                var showPaymentBypass = preview.paymentBypassAllowed && preview.paymentRequired;

                overlay.innerHTML = '';

                var modal = el('div', 'ww-modal ww-tier-change-modal');
                modal.addEventListener('click', function (e) { e.stopPropagation(); });

                // Header
                var header = el('div', 'ww-modal-header');
                var title = preview.isUpgrade
                    ? 'Upgrade to ' + (preview.newTierName || '')
                    : 'Downgrade to ' + (preview.newTierName || '');
                header.appendChild(el('h3', 'ww-modal-title', title));
                var closeBtn = el('button', 'ww-modal-close', '×');
                closeBtn.type = 'button';
                closeBtn.setAttribute('aria-label', 'Close');
                closeBtn.disabled = state.loading;
                closeBtn.addEventListener('click', close);
                header.appendChild(closeBtn);
                modal.appendChild(header);

                // Body
                var body = el('div', 'ww-modal-body');

                // Plan comparison
                var comparison = el('div', 'ww-tier-change-comparison');
                var curPlan = el('div', 'ww-tier-change-plan');
                curPlan.appendChild(el('span', 'ww-tier-change-plan-label', 'Current'));
                curPlan.appendChild(el('span', 'ww-tier-change-plan-name', preview.currentTierName || 'None'));
                if (preview.currentPrice !== null && preview.currentPrice !== undefined) {
                    curPlan.appendChild(el('span', 'ww-tier-change-plan-price',
                        formatCurrency(preview.currentPrice, ccy) + '/' + (preview.currentBillingFrequency || 'mo').toLowerCase()));
                }
                comparison.appendChild(curPlan);
                comparison.appendChild(el('span', 'ww-tier-change-arrow', '→'));
                var newPlan = el('div', 'ww-tier-change-plan');
                newPlan.appendChild(el('span', 'ww-tier-change-plan-label', 'New'));
                newPlan.appendChild(el('span', 'ww-tier-change-plan-name', preview.newTierName || 'Unknown'));
                if (preview.newPrice !== null && preview.newPrice !== undefined) {
                    newPlan.appendChild(el('span', 'ww-tier-change-plan-price',
                        formatCurrency(preview.newPrice, ccy) + '/' + (preview.newBillingFrequency || 'mo').toLowerCase()));
                }
                comparison.appendChild(newPlan);
                body.appendChild(comparison);

                // Billing frequency savings
                if (preview.isBillingFrequencyChange &&
                    preview.monthlyEquivalentCurrent != null &&
                    preview.monthlyEquivalentNew != null &&
                    preview.monthlyEquivalentNew < preview.monthlyEquivalentCurrent) {
                    var pct = Math.round(((preview.monthlyEquivalentCurrent - preview.monthlyEquivalentNew) / preview.monthlyEquivalentCurrent) * 100);
                    body.appendChild(el('div', 'ww-tier-change-savings',
                        'Save ' + pct + '% — ' + formatCurrency(preview.monthlyEquivalentNew, ccy) + '/mo billed ' + (preview.newBillingFrequency || '').toLowerCase()));
                }

                // Proration / charge details for upgrades
                if (preview.isUpgrade && effectivePaymentRequired && preview.proratedChargeToday != null) {
                    var charge = el('div', 'ww-tier-change-charge');
                    charge.appendChild(el('div', 'ww-tier-change-charge-header', "Today's charge"));
                    if (preview.creditAmount != null && preview.creditAmount > 0) {
                        var creditLine = el('div', 'ww-tier-change-line-item');
                        creditLine.appendChild(el('span', null, 'Credit (' + preview.daysRemainingInPeriod + ' unused days on ' + (preview.currentTierName || '') + ')'));
                        creditLine.appendChild(el('span', 'ww-tier-change-credit', '-' + formatCurrency(preview.creditAmount, ccy)));
                        charge.appendChild(creditLine);
                    }
                    var newLine = el('div', 'ww-tier-change-line-item');
                    newLine.appendChild(el('span', null, (preview.newTierName || '') + ' (' + preview.daysRemainingInPeriod + ' days)'));
                    newLine.appendChild(el('span', null, formatCurrency((preview.proratedChargeToday || 0) + (preview.creditAmount || 0), ccy)));
                    charge.appendChild(newLine);
                    var total = el('div', 'ww-tier-change-total');
                    total.appendChild(el('span', null, 'Net charge today'));
                    total.appendChild(el('span', null, formatCurrency(preview.proratedChargeToday, ccy)));
                    charge.appendChild(total);
                    if (preview.nextBillingDate) {
                        charge.appendChild(el('div', 'ww-tier-change-next-billing',
                            'Next billing: ' + new Date(preview.nextBillingDate).toLocaleDateString() + ' — ' +
                            formatCurrency(preview.nextBillingAmount, ccy) + '/' + (preview.newBillingFrequency || 'mo').toLowerCase()));
                    }
                    body.appendChild(charge);
                }

                // Downgrade credit
                if (preview.isDowngrade && preview.creditAmount != null && preview.creditAmount > 0) {
                    body.appendChild(el('div', 'ww-tier-change-credit-info',
                        formatCurrency(preview.creditAmount, ccy) + ' credit will be applied to your next bill.'));
                }

                // Feature diff
                if (preview.featuresGained && preview.featuresGained.length > 0) {
                    body.appendChild(buildFeatureList('You\'ll gain:', preview.featuresGained, true));
                }
                if (preview.featuresLost && preview.featuresLost.length > 0) {
                    body.appendChild(buildFeatureList('You\'ll lose access to:', preview.featuresLost, false));
                }

                // Downgrade timing choice
                if (preview.isDowngrade && preview.allowScheduledChange) {
                    var timing = el('div', 'ww-tier-change-timing');
                    timing.appendChild(el('div', 'ww-tier-change-timing-label', 'When should this take effect?'));
                    var endLabel = preview.nextBillingDate
                        ? 'End of billing period (' + new Date(preview.nextBillingDate).toLocaleDateString() + ')'
                        : 'End of billing period';
                    var creditNote = (preview.creditAmount != null && preview.creditAmount > 0)
                        ? ' ' + formatCurrency(preview.creditAmount, ccy) + ' credit on your next bill.'
                        : '';
                    timing.appendChild(buildTimingOption(endLabel,
                        'Keep ' + (preview.currentTierName || '') + ' features until then.' + creditNote,
                        !state.immediate, function () { state.immediate = false; render(); }));
                    timing.appendChild(buildTimingOption('Immediately',
                        'Switch to ' + (preview.newTierName || '') + ' now.' + creditNote,
                        state.immediate, function () { state.immediate = true; render(); }));
                    body.appendChild(timing);
                }

                // No payment provider warning
                if (effectivePaymentRequired && !preview.paymentProviderAvailable && !preview.paymentBypassAllowed) {
                    body.appendChild(el('div', 'ww-alert ww-alert-warning',
                        'Payment processing is not configured for this application. Contact your administrator.'));
                }

                // Admin bypass toggle
                if (showPaymentBypass) {
                    var bypassLabel = el('label', 'ww-tier-change-bypass');
                    var bypassInput = document.createElement('input');
                    bypassInput.type = 'checkbox';
                    bypassInput.checked = state.bypassPayment;
                    bypassInput.disabled = state.loading;
                    bypassInput.addEventListener('change', function () { state.bypassPayment = bypassInput.checked; render(); });
                    bypassLabel.appendChild(bypassInput);
                    bypassLabel.appendChild(el('span', null, 'Bypass payment (admin override)'));
                    body.appendChild(bypassLabel);
                }

                modal.appendChild(body);

                // Footer
                var footer = el('div', 'ww-modal-footer');
                var cancelBtn = el('button', 'ww-btn ww-btn-outline btn btn-outline-secondary', 'Keep Current Plan');
                cancelBtn.type = 'button';
                cancelBtn.disabled = state.loading;
                cancelBtn.addEventListener('click', close);
                footer.appendChild(cancelBtn);

                var confirmLabel = state.loading
                    ? 'Processing...'
                    : state.bypassPayment
                        ? 'Apply change (no charge)'
                        : (preview.isUpgrade && preview.proratedChargeToday)
                            ? 'Upgrade for ' + formatCurrency(preview.proratedChargeToday, ccy)
                            : preview.isDowngrade
                                ? 'Confirm Downgrade'
                                : 'Switch to ' + (preview.newTierName || '');
                var confirmBtn = el('button', 'ww-btn btn ' + (preview.isUpgrade ? 'ww-btn-primary btn-primary' : 'ww-btn-outline btn-outline-primary'), confirmLabel);
                confirmBtn.type = 'button';
                confirmBtn.disabled = state.loading ||
                    (effectivePaymentRequired && !preview.paymentProviderAvailable && !preview.paymentBypassAllowed);
                confirmBtn.addEventListener('click', submitChange);
                footer.appendChild(confirmBtn);

                modal.appendChild(footer);
                overlay.appendChild(modal);
            }

            function buildFeatureList(label, features, gained) {
                var wrap = el('div', 'ww-tier-change-features');
                wrap.appendChild(el('div', 'ww-tier-change-features-label', label));
                var list = el('ul', 'ww-tier-change-features-list');
                var icon = gained
                    ? '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"></polyline></svg>'
                    : '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>';
                for (var i = 0; i < features.length; i++) {
                    var li = el('li', gained ? 'ww-tier-change-feature-gained' : 'ww-tier-change-feature-lost');
                    var iconSpan = document.createElement('span');
                    iconSpan.innerHTML = icon;
                    li.appendChild(iconSpan);
                    li.appendChild(document.createTextNode(features[i]));
                    list.appendChild(li);
                }
                wrap.appendChild(list);
                return wrap;
            }

            function buildTimingOption(titleText, descText, checked, onSelect) {
                var label = el('label', 'ww-tier-change-timing-option');
                var input = document.createElement('input');
                input.type = 'radio';
                input.name = 'ww-tier-change-timing';
                input.checked = checked;
                input.disabled = state.loading;
                input.addEventListener('change', onSelect);
                label.appendChild(input);
                var content = el('div');
                content.appendChild(el('strong', null, titleText));
                content.appendChild(el('span', 'ww-tier-change-timing-desc', descText));
                label.appendChild(content);
                return label;
            }

            render();
            root.appendChild(overlay);
        }

        // ===== TIER PLANS: BILLING TOGGLE =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('.ww-admin-billing-btn');
            if (!btn) return;

            var cycle = btn.dataset.cycle;
            var btns = root.querySelectorAll('.ww-admin-billing-btn');
            for (var i = 0; i < btns.length; i++) {
                if (btns[i].dataset.cycle === cycle) {
                    btns[i].classList.remove('btn-outline-primary');
                    btns[i].classList.add('btn-primary');
                } else {
                    btns[i].classList.remove('btn-primary');
                    btns[i].classList.add('btn-outline-primary');
                }
            }

            // Show/hide pricing options
            var priceOptions = root.querySelectorAll('.ww-admin-price-option');
            for (var i = 0; i < priceOptions.length; i++) {
                if (priceOptions[i].dataset.billing === cycle) {
                    priceOptions[i].classList.remove('d-none');
                } else {
                    priceOptions[i].classList.add('d-none');
                }
            }

            // Fallback: if a tier card has no visible price for this cycle, show its default or first option
            var planCards = root.querySelectorAll('.ww-admin-plan-card');
            for (var j = 0; j < planCards.length; j++) {
                var options = planCards[j].querySelectorAll('.ww-admin-price-option');
                var anyVisible = false;
                for (var k = 0; k < options.length; k++) {
                    if (!options[k].classList.contains('d-none')) { anyVisible = true; break; }
                }
                if (!anyVisible && options.length > 0) {
                    // Show default option first, or first option as fallback
                    var shown = false;
                    for (var k = 0; k < options.length; k++) {
                        if (options[k].dataset.isDefault === 'true') {
                            options[k].classList.remove('d-none');
                            shown = true;
                            break;
                        }
                    }
                    if (!shown) options[0].classList.remove('d-none');
                }
            }
        });

        // ===== FEATURES: TOGGLE =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="toggle-feature"]');
            if (!btn) return;

            var featureCode = btn.dataset.featureCode;
            var confirmEl = root.querySelector('[data-confirm-for="' + featureCode + '"]');
            if (confirmEl) confirmEl.style.display = '';
        });

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="cancel-toggle"]');
            if (!btn) return;

            var featureCode = btn.dataset.featureCode;
            var confirmEl = root.querySelector('[data-confirm-for="' + featureCode + '"]');
            if (confirmEl) confirmEl.style.display = 'none';
        });

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="confirm-toggle"]');
            if (!btn) return;

            var featureCode = btn.dataset.featureCode;
            var newState = btn.dataset.newState === 'true';

            var confirmRow = root.querySelector('[data-confirm-for="' + featureCode + '"]');
            var expirationSelect = confirmRow ? confirmRow.querySelector('.ww-override-expiration') : null;
            var reasonInput = confirmRow ? confirmRow.querySelector('.ww-override-reason') : null;

            var reason = reasonInput ? reasonInput.value.trim() : null;
            var expiresAt = null;
            if (expirationSelect && expirationSelect.value) {
                var now = new Date();
                var val = expirationSelect.value;
                if (val === '1h') now.setHours(now.getHours() + 1);
                else if (val === '24h') now.setHours(now.getHours() + 24);
                else if (val === '7d') now.setDate(now.getDate() + 7);
                else if (val === '30d') now.setDate(now.getDate() + 30);
                else if (val === '90d') now.setDate(now.getDate() + 90);
                expiresAt = now.toISOString();
            }

            setLoading(true, 'Updating feature...');

            var scope = scopeParams();
            var scopeUserId = scope.useUser ? userId : null;

            apiPost(appId + '/admin/feature-overrides', {
                UserId: scopeUserId,
                FeatureCode: featureCode,
                IsEnabled: newState,
                Reason: reason || null,
                ExpiresAt: expiresAt
            })
                .then(function () {
                    showMessage('Feature ' + featureCode + ' ' + (newState ? 'enabled' : 'disabled') + '.', 'success');
                    if (confirmRow) confirmRow.style.display = 'none';
                    dispatchChanged('feature_toggled');
                    setTimeout(function () { window.location.reload(); }, 1000);
                })
                .catch(function (err) {
                    showMessage('Failed to update feature: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== OVERRIDES: REMOVE =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="remove-override"]');
            if (!btn) return;

            var featureCode = btn.dataset.featureCode;
            if (!confirm('Remove override for ' + featureCode + '? Feature access will revert to tier-based.')) return;

            setLoading(true, 'Removing override...');

            var scope = scopeParams();
            var userQuery = scope.useUser ? '?userId=' + userId : '';

            apiDelete(appId + '/admin/feature-overrides/' + featureCode + userQuery)
                .then(function () {
                    showMessage('Override removed for ' + featureCode + '.', 'success');
                    dispatchChanged('override_removed');
                    setTimeout(function () { window.location.reload(); }, 1000);
                })
                .catch(function (err) {
                    showMessage('Failed to remove override: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== OVERRIDES: MAKE PERMANENT =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="make-permanent"]');
            if (!btn) return;

            var featureCode = btn.dataset.featureCode;
            var isEnabled = btn.dataset.isEnabled === 'true';

            if (!confirm('Make override for ' + featureCode + ' permanent (remove expiration)?')) return;

            setLoading(true, 'Updating override...');

            var scope = scopeParams();
            var scopeUserId = scope.useUser ? userId : null;

            apiPost(appId + '/admin/feature-overrides', {
                UserId: scopeUserId,
                FeatureCode: featureCode,
                IsEnabled: isEnabled,
                ExpiresAt: null
            })
                .then(function () {
                    showMessage('Override for ' + featureCode + ' is now permanent.', 'success');
                    dispatchChanged('override_updated');
                    setTimeout(function () { window.location.reload(); }, 1000);
                })
                .catch(function (err) {
                    showMessage('Failed to update override: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== ADD-ONS: SUBSCRIBE =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="subscribe-addon"]');
            if (!btn) return;

            var addOnId = btn.dataset.addonId;
            var addOnName = btn.dataset.addonName || 'this add-on';

            if (!confirm('Subscribe to ' + addOnName + '?')) return;

            setLoading(true, 'Subscribing to add-on...');

            var scope = scopeParams();
            var path, body;

            if (scope.useCompany) {
                path = appId + '/addons/subscribe/company';
                body = { CompanyId: companyId, AppTierAddOnId: addOnId };
            } else if (scope.useUser) {
                path = appId + '/addons/admin/subscribe-user/' + userId;
                body = { AppTierAddOnId: addOnId };
            } else {
                path = appId + '/addons/subscribe';
                body = { AppId: appId, AppTierAddOnId: addOnId };
            }

            apiPost(path, body)
                .then(function () {
                    showMessage('Subscribed to ' + addOnName + '.', 'success');
                    dispatchChanged('addon_subscribed');
                    setTimeout(function () { window.location.reload(); }, 1500);
                })
                .catch(function (err) {
                    showMessage('Failed to subscribe: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== ADD-ONS: CANCEL =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="cancel-addon"]');
            if (!btn) return;

            var subscriptionId = btn.dataset.subscriptionId;
            var addonName = btn.dataset.addonName || 'this add-on';

            if (!confirm('Cancel ' + addonName + '?')) return;

            setLoading(true, 'Cancelling add-on...');

            var scope = scopeParams();
            var path;

            if (scope.useCompany) {
                path = appId + '/addons/subscriptions/' + subscriptionId + '/cancel?immediate=true';
            } else if (scope.useUser) {
                path = appId + '/addons/admin/cancel-user-addon/' + subscriptionId;
            } else {
                path = appId + '/addons/subscriptions/' + subscriptionId + '/cancel';
            }

            apiPost(path)
                .then(function () {
                    showMessage(addonName + ' cancelled.', 'success');
                    dispatchChanged('addon_cancelled');
                    setTimeout(function () { window.location.reload(); }, 1500);
                })
                .catch(function (err) {
                    showMessage('Failed to cancel: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== USAGE LIMITS: EDIT =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="edit-limit"]');
            if (!btn) return;

            var limitCode = btn.dataset.limitCode;
            var editView = root.querySelector('[data-edit-for="' + limitCode + '"]');
            var actionsView = root.querySelector('[data-actions-for="' + limitCode + '"]');
            if (editView) editView.style.display = '';
            if (actionsView) actionsView.style.display = 'none';
        });

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="cancel-edit"]');
            if (!btn) return;

            var limitCode = btn.dataset.limitCode;
            var editView = root.querySelector('[data-edit-for="' + limitCode + '"]');
            var actionsView = root.querySelector('[data-actions-for="' + limitCode + '"]');
            if (editView) editView.style.display = 'none';
            if (actionsView) actionsView.style.display = '';
        });

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="save-limit"]');
            if (!btn) return;

            var limitCode = btn.dataset.limitCode;
            var input = root.querySelector('.ww-limit-input[data-limit-code="' + limitCode + '"]');
            var newMax = parseInt(input ? input.value : '0', 10);

            if (isNaN(newMax)) {
                showMessage('Invalid limit value.', 'danger');
                return;
            }

            setLoading(true, 'Updating limit...');

            var scope = scopeParams();
            var path;

            if (scope.useCompany) {
                path = appId + '/admin/usage-limits/company/' + companyId + '/' + limitCode;
            } else if (scope.useUser) {
                path = appId + '/admin/usage-limits/user/' + userId + '/' + limitCode;
            } else {
                path = appId + '/admin/usage-limits/' + limitCode;
            }

            apiPut(path, { MaxValue: newMax })
                .then(function () {
                    showMessage('Limit updated for ' + limitCode + '.', 'success');
                    dispatchChanged('limit_updated');
                    setTimeout(function () { window.location.reload(); }, 1000);
                })
                .catch(function (err) {
                    showMessage('Failed to update limit: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== USAGE LIMITS: RESET =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="reset-usage"]');
            if (!btn) return;

            var limitCode = btn.dataset.limitCode;
            if (!confirm('Reset usage counter for ' + limitCode + '?')) return;

            setLoading(true, 'Resetting usage...');

            var scope = scopeParams();
            var path;

            if (scope.useCompany) {
                path = appId + '/admin/usage-limits/company/' + companyId + '/' + limitCode + '/reset';
            } else if (scope.useUser) {
                path = appId + '/admin/usage-limits/user/' + userId + '/' + limitCode + '/reset';
            } else {
                path = appId + '/admin/usage-limits/' + limitCode + '/reset';
            }

            apiPost(path)
                .then(function () {
                    showMessage('Usage reset for ' + limitCode + '.', 'success');
                    dispatchChanged('usage_reset');
                    setTimeout(function () { window.location.reload(); }, 1000);
                })
                .catch(function (err) {
                    showMessage('Failed to reset: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== EVENT DISPATCH =====

        function dispatchChanged(action) {
            root.dispatchEvent(new CustomEvent('ww-subscription-admin-changed', {
                detail: { action: action, appId: appId },
                bubbles: true
            }));
        }
    }

    // ===== PUBLIC API =====

    window.wwSubscriptionAdmin = {
        refresh: function () {
            window.location.reload();
        }
    };
})();
