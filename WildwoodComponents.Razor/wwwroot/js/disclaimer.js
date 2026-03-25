/**
 * WildwoodComponents.Razor - Disclaimer Component JavaScript
 * Handles checkbox tracking, accept button state, and disclaimer acceptance submission.
 * Razor Pages equivalent of the Blazor DisclaimerComponent interactivity.
 */
(function () {
    'use strict';

    // Support multiple instances on the same page
    var roots = document.querySelectorAll('.ww-disclaimer-component');
    for (var r = 0; r < roots.length; r++) {
        initDisclaimer(roots[r]);
    }

    function initDisclaimer(root) {
        var cid = root.dataset.componentId;
        var proxyUrl = (root.dataset.proxyUrl || '').replace(/\/+$/, '');

        var acceptBtn = root.querySelector('.ww-disclaimer-accept-all-btn');
        var cancelBtn = root.querySelector('.ww-disclaimer-cancel-btn');
        var checkboxes = root.querySelectorAll('.ww-disclaimer-checkbox');
        var messageEl = document.getElementById('ww-disclaimer-message-' + cid);

        if (!acceptBtn || checkboxes.length === 0) return;

        var requiredCount = parseInt(acceptBtn.dataset.requiredCount || '0', 10);

        // ===== CHECKBOX STATE TRACKING =====

        function updateAcceptButtonState() {
            var requiredChecked = 0;
            for (var i = 0; i < checkboxes.length; i++) {
                if (checkboxes[i].dataset.isRequired === 'true' && checkboxes[i].checked) {
                    requiredChecked++;
                }
            }
            acceptBtn.disabled = requiredChecked < requiredCount;
        }

        for (var i = 0; i < checkboxes.length; i++) {
            checkboxes[i].addEventListener('change', updateAcceptButtonState);
        }

        // ===== MESSAGE DISPLAY =====

        function showMessage(text, type) {
            if (!messageEl) return;
            messageEl.textContent = text;
            messageEl.className = 'ww-alert ww-alert-' + type;
            messageEl.style.display = '';
        }

        function setLoading(loading) {
            var btnText = acceptBtn.querySelector('.ww-btn-text');
            var btnSpinner = acceptBtn.querySelector('.ww-btn-spinner');
            if (btnText) btnText.style.display = loading ? 'none' : '';
            if (btnSpinner) btnSpinner.style.display = loading ? '' : 'none';
            acceptBtn.disabled = loading;
        }

        // ===== ACCEPT SUBMISSION =====

        acceptBtn.addEventListener('click', function () {
            var acceptances = [];
            for (var i = 0; i < checkboxes.length; i++) {
                if (checkboxes[i].checked) {
                    acceptances.push({
                        companyDisclaimerId: checkboxes[i].dataset.disclaimerId,
                        companyDisclaimerVersionId: checkboxes[i].dataset.versionId
                    });
                }
            }

            if (acceptances.length === 0) return;

            setLoading(true);

            if (!proxyUrl) {
                showMessage('Configuration error: missing proxy URL.', 'danger');
                setLoading(false);
                return;
            }

            fetch(proxyUrl + '/accept', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ acceptances: acceptances })
            })
                .then(function (response) {
                    if (response.ok) {
                        showMessage('Disclaimers accepted successfully.', 'success');

                        // Hide the disclaimer list and actions
                        var list = root.querySelector('.ww-disclaimer-list');
                        if (list) list.style.display = 'none';

                        // Dispatch event for consuming app
                        root.dispatchEvent(new CustomEvent('ww-disclaimers-accepted', {
                            detail: { acceptances: acceptances },
                            bubbles: true
                        }));
                    } else {
                        return response.text().then(function (text) {
                            showMessage('Failed to accept disclaimers: ' + (text || response.statusText), 'danger');
                        });
                    }
                })
                .catch(function (err) {
                    showMessage('Error: ' + err.message, 'danger');
                })
                .finally(function () {
                    setLoading(false);
                });
        });

        // ===== CANCEL =====

        if (cancelBtn) {
            cancelBtn.addEventListener('click', function () {
                root.dispatchEvent(new CustomEvent('ww-disclaimers-cancelled', {
                    bubbles: true
                }));
            });
        }

        // Initialize button state
        updateAcceptButtonState();
    }

    // ===== PUBLIC API =====

    window.wwDisclaimer = {
        /**
         * Refresh disclaimers by reloading the page or fetching new data.
         * Consuming apps can call this after login state changes.
         */
        refresh: function () {
            window.location.reload();
        }
    };
})();
