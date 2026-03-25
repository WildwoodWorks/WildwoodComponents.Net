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
                .then(function () {
                    showMessage('Subscription cancelled successfully.', 'success');
                    dispatchChanged('cancelled');
                    setTimeout(function () { window.location.reload(); }, 1500);
                })
                .catch(function (err) {
                    showMessage('Failed to cancel: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== TIER PLANS: SELECT TIER =====

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

            if (!confirm((isChange ? 'Change plan to ' : 'Subscribe to ') + tierName + '?')) return;

            setLoading(true, isChange ? 'Changing plan...' : 'Subscribing...');

            var scope = scopeParams();
            var path, body;

            if (isChange) {
                if (scope.useCompany) {
                    path = appId + '/change-tier/company';
                    body = { CompanyId: companyId, NewAppTierId: tierId, NewAppTierPricingId: pricingId, Immediate: true };
                } else if (scope.useUser) {
                    path = 'change-tier';
                    body = { UserId: userId, AppId: appId, NewAppTierId: tierId, NewAppTierPricingId: pricingId, Immediate: true };
                } else {
                    path = appId + '/my-subscription/change';
                    body = { NewAppTierId: tierId, NewAppTierPricingId: pricingId, Immediate: true };
                }
            } else {
                if (scope.useCompany) {
                    path = appId + '/subscribe/company';
                    body = { CompanyId: companyId, AppTierId: tierId, AppTierPricingId: pricingId };
                } else if (scope.useUser) {
                    path = 'subscribe';
                    body = { UserId: userId, AppId: appId, AppTierId: tierId, AppTierPricingId: pricingId };
                } else {
                    path = appId + '/my-subscription';
                    body = { AppTierId: tierId, AppTierPricingId: pricingId };
                }
            }

            apiPost(path, body)
                .then(function (result) {
                    if (result.success === false && result.errorMessage) {
                        showMessage(result.errorMessage, 'danger');
                    } else {
                        showMessage('Successfully ' + (isChange ? 'changed to ' : 'subscribed to ') + tierName + '!', 'success');
                        dispatchChanged(isChange ? 'changed' : 'subscribed');
                        setTimeout(function () { window.location.reload(); }, 1500);
                    }
                })
                .catch(function (err) {
                    showMessage('Failed: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
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
