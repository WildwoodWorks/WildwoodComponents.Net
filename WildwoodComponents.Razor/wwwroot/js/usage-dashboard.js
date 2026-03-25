/**
 * WildwoodComponents.Razor - Usage Dashboard Component JavaScript
 * Handles upgrade button events and optional auto-refresh of usage data.
 * Razor Pages equivalent of the Blazor UsageDashboardComponent interactivity.
 */
(function () {
    'use strict';

    var instances = {};

    function initInstance(root) {
        var cid = root.dataset.componentId;
        if (!cid || instances[cid]) return;

        var config = {
            appId: root.dataset.appId,
            proxyUrl: (root.dataset.proxyUrl || '/api/wildwood-app-tiers').replace(/\/+$/, ''),
            warningThreshold: parseInt(root.dataset.warningThreshold || '80', 10)
        };

        var refreshTimer = null;

        // ===== MESSAGE DISPLAY =====

        function showMessage(text, type) {
            var msg = root.querySelector('.ww-usage-message');
            if (!msg) return;
            msg.innerHTML = '<div class="alert alert-' + type + ' mb-3">' + text + '</div>';
            msg.style.display = '';
        }

        function clearMessage() {
            var msg = root.querySelector('.ww-usage-message');
            if (msg) {
                msg.innerHTML = '';
                msg.style.display = 'none';
            }
        }

        // ===== PROGRESS BAR HELPERS =====

        function getBarClass(usagePercent, isExceeded, isAtWarning) {
            if (isExceeded) return 'ww-bar-danger';
            if (isAtWarning || usagePercent >= config.warningThreshold) return 'ww-bar-warning';
            return 'ww-bar-normal';
        }

        function getStatusTextClass(isExceeded, isAtWarning, usagePercent) {
            if (isExceeded) return 'text-danger fw-medium';
            if (isAtWarning || usagePercent >= config.warningThreshold) return 'text-warning';
            return 'text-muted';
        }

        // ===== DATA REFRESH =====

        function refreshData() {
            clearMessage();

            fetch(config.proxyUrl + '/' + config.appId + '/limit-statuses', {
                method: 'GET',
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            })
            .then(function (resp) {
                if (!resp.ok) throw new Error('Failed to load usage data');
                return resp.json();
            })
            .then(function (limits) {
                updateLimitCards(limits);
                root.dispatchEvent(new CustomEvent('ww-usage-data-refreshed', {
                    bubbles: true,
                    detail: { appId: config.appId, limits: limits }
                }));
            })
            .catch(function (err) {
                showMessage('Failed to refresh usage data. <button class="btn btn-sm btn-outline-primary ms-2 ww-retry-btn">Retry</button>', 'warning');
            });
        }

        function updateLimitCards(limits) {
            var anyExceeded = false;
            var anyWarning = false;

            for (var i = 0; i < limits.length; i++) {
                var limit = limits[i];
                var card = root.querySelector('[data-limit-code="' + limit.limitCode + '"]');
                if (!card) continue;

                var percent = limit.isUnlimited ? 0 : Math.min(limit.usagePercent, 100);
                var barClass = getBarClass(limit.usagePercent, limit.isExceeded, limit.isAtWarningThreshold);

                // Update progress bar
                var bar = card.querySelector('.progress-bar');
                if (bar) {
                    bar.style.width = percent + '%';
                    bar.className = 'progress-bar ' + barClass;
                    bar.setAttribute('aria-valuenow', limit.currentUsage);
                    bar.setAttribute('aria-valuemax', limit.maxValue);
                }

                // Update values text
                var valuesEl = card.querySelector('.fw-medium + .text-muted, .text-end');
                if (valuesEl && !limit.isUnlimited) {
                    var fwEl = valuesEl.querySelector('.fw-medium');
                    var mutedEl = valuesEl.querySelector('.text-muted');
                    if (fwEl) fwEl.textContent = Number(limit.currentUsage).toLocaleString();
                    if (mutedEl) mutedEl.textContent = ' / ' + Number(limit.maxValue).toLocaleString();
                }

                // Update percent text
                var percentEl = card.querySelector('small.text-muted');
                if (percentEl && !limit.isUnlimited) {
                    percentEl.textContent = Math.round(limit.usagePercent) + '% used';
                }

                if (limit.isExceeded) anyExceeded = true;
                if (limit.isAtWarningThreshold) anyWarning = true;
            }

            // Show/hide upgrade CTA
            var cta = root.querySelector('.ww-upgrade-cta');
            if (cta) {
                cta.style.display = (anyExceeded || anyWarning) ? '' : 'none';
            }
        }

        // ===== EVENT DELEGATION =====

        root.addEventListener('click', function (e) {
            // Upgrade button
            if (e.target.closest('.ww-upgrade-plan-btn')) {
                root.dispatchEvent(new CustomEvent('ww-usage-upgrade-click', {
                    bubbles: true,
                    detail: { appId: config.appId }
                }));
                return;
            }

            // Subscribe button (no subscription state)
            if (e.target.closest('.ww-subscribe-btn')) {
                root.dispatchEvent(new CustomEvent('ww-usage-subscribe-click', {
                    bubbles: true,
                    detail: { appId: config.appId }
                }));
                return;
            }

            // Retry button
            if (e.target.closest('.ww-retry-btn')) {
                refreshData();
                return;
            }
        });

        // ===== AUTO-REFRESH =====

        var refreshInterval = parseInt(root.dataset.refreshInterval || '0', 10);
        if (refreshInterval > 0) {
            refreshTimer = setInterval(refreshData, refreshInterval * 1000);
        }

        instances[cid] = {
            refresh: refreshData,
            destroy: function () {
                if (refreshTimer) {
                    clearInterval(refreshTimer);
                    refreshTimer = null;
                }
                delete instances[cid];
            }
        };
    }

    // ===== AUTO-INIT =====

    function initAll() {
        var roots = document.querySelectorAll('.ww-usage-dashboard-component');
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
    window.wwUsageDashboard = {
        init: initInstance,
        initAll: initAll,
        getInstance: function (componentId) {
            return instances[componentId] || null;
        }
    };
})();
