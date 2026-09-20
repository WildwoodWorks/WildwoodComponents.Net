/**
 * WildwoodComponents.Razor - Subscription Admin Component JavaScript
 * Handles tab switching, tier selection, cancellation, feature toggling,
 * override management, add-on subscribe/cancel, and usage limit editing.
 * Razor Pages equivalent of the Blazor SubscriptionAdminComponent interactivity.
 *
 * TWO SHARED SCRIPTS MUST LOAD FIRST for the plan change to work - the preview, the confirmation
 * modal, the flag that lets the server park a change on a bank challenge, the challenge itself on
 * a card already on file, and the completion of the parked change all live in
 * regsub-planchange.js, shared with the manage view so the two cannot drift:
 *
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-planchange.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/subscription-admin.js"></script>
 *
 * Without them every other action on this panel still works and a plan click says so rather than
 * failing silently.
 */
(function () {
    'use strict';

    // ===== ADD-ON ROW RULES =====
    //
    // Named by hand after WildwoodComponents.Shared/Utilities/AddOnRowRules.cs, which decides the
    // rows server-side. Nothing here re-derives ownership or what a row offers - these only turn an
    // already-decided row into a request and an outcome into words. Pure, so they can be read and
    // reasoned about on their own; this package has no JS test harness.

    /**
     * Why a user's entitlements changed. The six values of the JS entitlementsChanged event
     * (events/eventEmitter.ts) and of C# EntitlementsChangedReasons - one vocabulary across the
     * three stacks, so a host listening on the DOM reads the same words the SDKs emit.
     */
    var WW_REASON = {
        Signup: 'signup',
        TierChange: 'tierChange',
        AddOn: 'addOn',
        Cancel: 'cancel',
        Reactivate: 'reactivate',
        Manual: 'manual'
    };


    /** Where the shipped same-origin proxy lives when a panel names none. */
    var WW_REGSUB_DEFAULT_URL = '/api/wildwood-regsub';

    /** What the panel says when the session is gone (the proxy answers 401 and forwards nothing). */
    var WW_SIGNED_OUT_MESSAGE = 'Your session has expired. Please sign in again.';

    /**
     * Mirrors AddOnRowRules.FailureMessage: the server's own refusal when it sent words, else the
     * action's wording. Never empty.
     */
    function addOnFailureMessage(action, name, serverMessage) {
        if (typeof serverMessage === 'string' && serverMessage.trim().length > 0) return serverMessage.trim();

        var verb = action === 'subscribe' ? 'subscribe to' : (action === 'reactivate' ? 'reactivate' : 'cancel');
        var packName = typeof name === 'string' && name.trim().length > 0 ? name.trim() : 'this pack';
        return 'Could not ' + verb + ' ' + packName + '. Please try again.';
    }

    /**
     * The refusal's message wherever the structured result carries it: subscribe nests it under
     * `error`, cancel and reactivate put it flat on `errorMessage`.
     */
    function addOnRefusalMessage(payload) {
        if (!payload) return '';
        if (payload.error && payload.error.message) return payload.error.message;
        return payload.errorMessage || '';
    }

    /**
     * Mirrors AddOnRowDecision.ActionsAttribute: whether the server said this row offers the action.
     * A row that offers nothing (a bundled pack) carries an empty attribute.
     */
    function addOnRowAllows(actionsAttribute, action) {
        if (typeof actionsAttribute !== 'string' || actionsAttribute.length === 0) return false;

        var allowed = actionsAttribute.split(/\s+/);
        for (var i = 0; i < allowed.length; i++) {
            if (allowed[i] === action) return true;
        }
        return false;
    }

    /** Proxy path for cancelling a pack at the end of the period already paid for. */
    function addOnCancelPath(subscriptionId) {
        return 'addons/' + encodeURIComponent(subscriptionId) + '/cancel?immediate=false';
    }

    /** Proxy path for taking a scheduled cancellation back. */
    function addOnReactivatePath(subscriptionId) {
        return 'addons/' + encodeURIComponent(subscriptionId) + '/reactivate';
    }

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

                    // Cancel-result wording is mirrored in apptier.js (cancelSubscription) —
                    // keep the two in sync.
                    var scheduled = result && result.isScheduled;
                    var message = scheduled
                        ? (result.effectiveDate
                            ? 'Your cancellation is scheduled — access continues until ' + new Date(result.effectiveDate).toLocaleDateString() + '.'
                            : 'Your cancellation is scheduled for the end of the current billing period.')
                        : 'Your subscription has been cancelled.';

                    dispatchChanged('cancelled', WW_REASON.Cancel);

                    if (result && result.requiresUserAction) {
                        // Store-billed subscription (App Store / Google Play): the platform
                        // cannot stop the store's billing, so keep the instructions on screen.
                        // No reload — it would wipe this persistent notice before the user can
                        // read and follow it; hosts listening for ww-subscription-admin-changed
                        // can refresh their own state.
                        showPersistentMessage(
                            message + ' ' + (result.userActionInstructions || 'Also cancel the subscription in your store settings.'),
                            'warning', result.userActionUrl, 'Manage your store subscription');
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
        //
        // The whole sequence - preview, the confirmation modal, the change itself, the 3-D Secure
        // challenge on a card already on file and the completion of the parked change - is
        // regsub-planchange.js, shared with <vc:registration-subscription-manage />. It is shared
        // precisely because that is the part that moves money: a second copy is a second place
        // for "may the server park this change" and "is a parked change ever completed" to drift.
        // What stays here is this panel's own presentation: the loading overlay, the message bar
        // and the ww-subscription-admin-changed event.
        //
        // The manage view carries data-ww-view="manage" on the SAME root (it is a
        // .ww-subscription-admin-component too, so every other panel action keeps working), and
        // regsub-manage.js attaches the driver there. One root, one driver.

        var isManagedElsewhere = root.getAttribute('data-ww-view') === 'manage';
        var planChangeCtx = null;
        var planChange = null;

        function ensurePlanChange() {
            if (planChange) return planChange;

            var factory = window.wwRegSubPlanChange;
            if (!factory || !window.wwRegSubMachines) {
                if (window.console) {
                    window.console.error(
                        '[wwSubscriptionAdmin] regsub-machines.js and regsub-planchange.js must be '
                        + 'loaded before subscription-admin.js for plan changes to work.');
                }
                showMessage('Plan changes are unavailable on this page.', 'danger');
                return null;
            }

            planChange = factory.create({
                appId: appId,
                userId: userId,
                companyId: companyId,
                isCompanyMode: isCompanyMode,
                currency: currency,
                container: root,
                publishableKey: root.dataset.publishableKey || '',
                hostPost: apiPost,
                regsubPost: regsubPost,
                onState: paintPlanChange,
                onError: function (code, message) { showMessage(message, 'danger'); },
                onPaymentRequired: function (detail) {
                    root.dispatchEvent(new CustomEvent('ww-regsub-payment-required', {
                        detail: detail,
                        bubbles: true
                    }));
                },
                onChanged: function () {
                    var ctx = planChangeCtx || { isChange: true, tierName: '' };
                    showMessage(
                        'Successfully ' + (ctx.isChange ? 'changed to ' : 'subscribed to ') + ctx.tierName + '!',
                        'success');
                    dispatchChanged(ctx.isChange ? 'changed' : 'subscribed', WW_REASON.TierChange);
                    setTimeout(function () { window.location.reload(); }, 1500);
                },
                // dispatchChanged already raises ww-entitlements-changed with the reason; a second
                // event from the driver would have hosts counting one change twice.
                onEntitlements: function () { },
                confirmSubscribe: function (ctx) {
                    return confirm('Subscribe to ' + ctx.tierName + '?');
                }
            });

            return planChange;
        }

        function paintPlanChange(view) {
            var isChange = planChangeCtx ? planChangeCtx.isChange : true;

            switch (view.step) {
                case 'previewing':
                    setLoading(true, 'Loading preview...');
                    break;

                case 'changing':
                    setLoading(true, isChange ? 'Changing plan...' : 'Subscribing...');
                    break;

                case 'authenticating':
                    setLoading(true, 'Confirming the charge with your bank...');
                    break;

                case 'completing':
                    setLoading(true, 'Applying your new plan...');
                    break;

                default:
                    setLoading(false);
                    break;
            }
        }

        root.addEventListener('click', function (e) {
            if (isManagedElsewhere) return;

            var btn = e.target.closest('[data-action="select-tier"]');
            if (!btn) return;

            var isFree = btn.dataset.isFree === 'true';

            // The pricing option currently on screen decides the price and the trial.
            var card = btn.closest('.ww-admin-plan-card');
            var pricingId = null;
            if (card && !isFree) {
                var visiblePrice = card.querySelector('.ww-admin-price-option:not(.d-none)');
                if (visiblePrice) pricingId = visiblePrice.dataset.pricingId;
            }

            var flow = ensurePlanChange();
            if (!flow) return;

            planChangeCtx = {
                tierId: btn.dataset.tierId,
                tierName: btn.dataset.tierName,
                pricingId: pricingId,
                pricingModelId: btn.dataset.pricingModelId || null,
                price: Number(btn.dataset.price || '0'),
                trialDays: parseInt(btn.dataset.trialDays || '0', 10) || 0,
                isChange: btn.dataset.isChange === 'true',
                isFreeTier: isFree
            };

            flow.selectTier(planChangeCtx);
        });

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
                    dispatchChanged('feature_toggled', WW_REASON.Manual);
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
                    dispatchChanged('override_removed', WW_REASON.Manual);
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
                    dispatchChanged('override_updated', WW_REASON.Manual);
                    setTimeout(function () { window.location.reload(); }, 1000);
                })
                .catch(function (err) {
                    showMessage('Failed to update override: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== ADD-ONS =====
        //
        // The rows were decided server-side by AddOnRowRules and carry what they allow in
        // data-ww-addon-actions, so nothing here re-derives who owns what. Every call goes to the
        // SHIPPED same-origin proxy (/api/wildwood-regsub), which answers HTTP 200 with the
        // structured result - refusals included - so a refusal keeps the server's own words.

        var addOnsPanel = root.querySelector('.ww-addons-panel');
        var addOnErrorEl = root.querySelector('.ww-addons-error');

        function regsubUrl(path) {
            var base = (addOnsPanel && addOnsPanel.dataset.regsubUrl) || WW_REGSUB_DEFAULT_URL;
            return base.replace(/\/+$/, '') + '/' + path.replace(/^\//, '');
        }

        function regsubPost(path, body) {
            return fetch(regsubUrl(path), {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: body ? JSON.stringify(body) : null
            }).then(function (r) {
                if (r.status === 401) {
                    var signedOut = new Error(WW_SIGNED_OUT_MESSAGE);
                    signedOut.status = 401;
                    throw signedOut;
                }
                if (!r.ok) {
                    return r.text().catch(function () { return ''; }).then(function (text) {
                        var failed = new Error(text || 'Request failed (HTTP ' + r.status + ')');
                        failed.status = r.status;
                        throw failed;
                    });
                }
                return r.json().catch(function () { return { success: true }; });
            });
        }

        // Server strings go in as text, never as markup.
        function showAddOnError(text) {
            if (!addOnErrorEl) {
                showMessage(text, 'danger');
                return;
            }
            addOnErrorEl.textContent = text;
            addOnErrorEl.style.display = '';
        }

        // A retry never shows the last attempt's message.
        function clearAddOnError() {
            if (!addOnErrorEl) return;
            addOnErrorEl.textContent = '';
            addOnErrorEl.style.display = 'none';
        }

        function addOnCard(el) {
            return el ? el.closest('.ww-addon-card') : null;
        }

        function setRowBusy(card, text) {
            if (!card) return;
            var busy = card.querySelector('.ww-addon-busy');
            if (busy) {
                busy.textContent = text || '';
                busy.style.display = text ? '' : 'none';
            }
            var buttons = card.querySelectorAll('button');
            for (var i = 0; i < buttons.length; i++) buttons[i].disabled = !!text;
        }

        function showCancelConfirm(card, showing) {
            if (!card) return;
            var confirmEl = card.querySelector('.ww-addon-confirm');
            var cancelBtn = card.querySelector('.ww-cancel-addon-btn');
            if (confirmEl) confirmEl.style.display = showing ? '' : 'none';
            if (cancelBtn) cancelBtn.style.display = showing ? 'none' : '';
        }

        // One place that turns any outcome - refusal, 401, transport failure - into what the panel
        // says, and reloads only when the mutation actually happened.
        function runAddOnAction(card, action, name, busyText, successText, reason, entitlementReason, request) {
            clearAddOnError();
            setRowBusy(card, busyText);

            return request()
                .then(function (payload) {
                    if (payload && payload.success === false) {
                        showAddOnError(addOnFailureMessage(action, name, addOnRefusalMessage(payload)));
                        setRowBusy(card, '');
                        return;
                    }
                    showMessage(successText, 'success');
                    dispatchChanged(reason, entitlementReason);
                    setTimeout(function () { window.location.reload(); }, 1500);
                })
                .catch(function (err) {
                    var message = err && err.status === 401
                        ? WW_SIGNED_OUT_MESSAGE
                        : addOnFailureMessage(action, name, err && err.message);
                    showAddOnError(message);
                    setRowBusy(card, '');
                });
        }

        // ===== ADD-ONS: SUBSCRIBE =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="subscribe-addon"]');
            if (!btn) return;

            var card = addOnCard(btn);
            var addOnName = btn.dataset.addonName || '';
            var addOnId = btn.dataset.addonId;
            var pricingId = btn.dataset.pricingId || '';
            var scope = scopeParams();

            runAddOnAction(card, 'subscribe', addOnName, 'Subscribing...',
                'Subscribed to ' + (addOnName || 'the add-on') + '.', 'addon_subscribed', WW_REASON.AddOn, function () {
                    // Buying for someone else stays on the host's app-tier proxy: the shipped proxy
                    // acts as the signed-in user and has no company or admin scope.
                    if (scope.useCompany) {
                        return apiPost(appId + '/addons/subscribe/company',
                            { CompanyId: companyId, AppTierAddOnId: addOnId });
                    }
                    if (scope.useUser) {
                        return apiPost(appId + '/addons/admin/subscribe-user/' + userId,
                            { AppTierAddOnId: addOnId });
                    }
                    // The pricing option that is bought decides the price and the trial, so its id
                    // travels with the request instead of being guessed server-side.
                    return regsubPost('addons/subscribe', { AddOnId: addOnId, PricingId: pricingId || null });
                });
        });

        // ===== ADD-ONS: CANCEL (two steps - the row asks first) =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="cancel-addon"]');
            if (!btn) return;

            var card = addOnCard(btn);
            if (!addOnRowAllows(card && card.dataset.wwAddonActions, 'cancel')) return;

            clearAddOnError();
            showCancelConfirm(card, true);
        });

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="cancel-addon-keep"]');
            if (!btn) return;

            showCancelConfirm(addOnCard(btn), false);
        });

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="cancel-addon-confirm"]');
            if (!btn) return;

            var card = addOnCard(btn);
            if (!addOnRowAllows(card && card.dataset.wwAddonActions, 'cancel')) return;

            var subscriptionId = btn.dataset.subscriptionId;
            var addonName = (card && card.dataset.addonName) || '';
            var scope = scopeParams();

            showCancelConfirm(card, false);

            runAddOnAction(card, 'cancel', addonName, 'Cancelling...',
                (addonName || 'The add-on') + ' cancelled.', 'addon_cancelled', WW_REASON.Cancel, function () {
                    // Cancelling someone else's pack stays on the host's app-tier proxy.
                    if (scope.useCompany) {
                        return apiPost(appId + '/addons/subscriptions/' + subscriptionId + '/cancel?immediate=true');
                    }
                    if (scope.useUser) {
                        return apiPost(appId + '/addons/admin/cancel-user-addon/' + subscriptionId);
                    }
                    // immediate=false - the user keeps what the current period was paid for, which
                    // is what the confirmation just promised.
                    return regsubPost(addOnCancelPath(subscriptionId));
                });
        });

        // ===== ADD-ONS: REACTIVATE =====

        root.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action="reactivate-addon"]');
            if (!btn) return;

            var card = addOnCard(btn);
            if (!addOnRowAllows(card && card.dataset.wwAddonActions, 'reactivate')) return;

            var subscriptionId = btn.dataset.subscriptionId;
            var addonName = (card && card.dataset.addonName) || '';

            runAddOnAction(card, 'reactivate', addonName, 'Reactivating...',
                (addonName || 'The add-on') + ' reactivated.', 'addon_reactivated', WW_REASON.Reactivate, function () {
                    return regsubPost(addOnReactivatePath(subscriptionId));
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
                    dispatchChanged('limit_updated', null);
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
                    dispatchChanged('usage_reset', null);
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

        /**
         * Says a mutation happened. `action` is this panel's own fine-grained word and is
         * unchanged; `reason` is the cross-stack one (WW_REASON), null for a mutation that changes
         * no entitlement - editing a usage limit or resetting a counter moves a number, not what
         * the plan includes. A reason also raises a second, dedicated event so a host can listen
         * for "re-read my entitlements" without knowing this panel's action words.
         */
        function dispatchChanged(action, reason) {
            root.dispatchEvent(new CustomEvent('ww-subscription-admin-changed', {
                detail: { action: action, appId: appId, reason: reason || null },
                bubbles: true
            }));

            if (reason) {
                root.dispatchEvent(new CustomEvent('ww-entitlements-changed', {
                    detail: { appId: appId, reason: reason },
                    bubbles: true
                }));
            }
        }
    }

    // ===== PUBLIC API =====

    window.wwSubscriptionAdmin = {
        refresh: function () {
            window.location.reload();
        }
    };
})();
