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

        // ===== READ FULL DOCUMENT MODAL =====

        var modalOverlay = document.getElementById('ww-disclaimer-modal-overlay-' + cid);
        var expandBtns = root.querySelectorAll('.ww-disclaimer-expand-btn');

        function sanitizeHtml(html) {
            if (typeof DOMParser === 'undefined') return html;
            var doc = new DOMParser().parseFromString(html, 'text/html');
            var dangerous = doc.querySelectorAll('script, style, iframe, object, embed, form, link, meta');
            dangerous.forEach(function (el) { el.remove(); });
            var allEls = doc.querySelectorAll('*');
            allEls.forEach(function (el) {
                for (var a = el.attributes.length - 1; a >= 0; a--) {
                    var attr = el.attributes[a];
                    if (attr.name.indexOf('on') === 0 || attr.name === 'srcdoc' || attr.name === 'formaction') {
                        el.removeAttribute(attr.name);
                    }
                    if (attr.name === 'href' || attr.name === 'src' || attr.name === 'action') {
                        var val = (attr.value || '').trim().toLowerCase();
                        if (val.indexOf('javascript:') === 0 || val.indexOf('data:') === 0 || val.indexOf('vbscript:') === 0) {
                            el.removeAttribute(attr.name);
                        }
                    }
                }
            });
            return doc.body.innerHTML;
        }

        function openModal(disclaimerItem) {
            if (!modalOverlay) return;
            var title = disclaimerItem.dataset.title || '';
            var version = disclaimerItem.dataset.version || '';
            var dtype = disclaimerItem.dataset.type || '';
            var prevVersion = disclaimerItem.dataset.prevVersion || '';
            var changeNotes = disclaimerItem.dataset.changeNotes || '';
            var contentFormat = disclaimerItem.dataset.contentFormat || '';

            // Populate title
            var titleEl = modalOverlay.querySelector('.ww-disclaimer-modal-title');
            if (titleEl) titleEl.textContent = title;

            // Populate meta bar (use DOM APIs to avoid innerHTML XSS)
            var metaEl = modalOverlay.querySelector('.ww-disclaimer-modal-meta');
            if (metaEl) {
                metaEl.textContent = '';
                if (version) {
                    var vBadge = document.createElement('span');
                    vBadge.className = 'badge bg-info text-dark';
                    vBadge.textContent = 'v' + version;
                    metaEl.appendChild(vBadge);
                }
                if (dtype) {
                    var typeSpan = document.createElement('span');
                    typeSpan.textContent = dtype;
                    metaEl.appendChild(typeSpan);
                }
                if (prevVersion) {
                    var prevSpan = document.createElement('span');
                    prevSpan.textContent = 'Previously accepted: v' + prevVersion;
                    metaEl.appendChild(prevSpan);
                }
            }

            // Populate change notes (use textContent to avoid XSS from data attributes)
            var notesEl = modalOverlay.querySelector('.ww-disclaimer-modal-change-notes');
            if (notesEl) {
                if (changeNotes) {
                    notesEl.textContent = '';
                    var strong = document.createElement('strong');
                    strong.textContent = 'What changed: ';
                    notesEl.appendChild(strong);
                    notesEl.appendChild(document.createTextNode(changeNotes));
                    notesEl.style.display = '';
                } else {
                    notesEl.style.display = 'none';
                }
            }

            // Populate content — read from the existing rendered content in the card
            var contentEl = modalOverlay.querySelector('.ww-disclaimer-modal-content');
            if (contentEl) {
                var sourceHtml = disclaimerItem.querySelector('.ww-disclaimer-html-content');
                var sourceText = disclaimerItem.querySelector('.ww-disclaimer-text-content');
                if (sourceHtml) {
                    contentEl.innerHTML = sanitizeHtml(sourceHtml.innerHTML);
                    contentEl.style.whiteSpace = '';
                } else if (sourceText) {
                    contentEl.textContent = sourceText.textContent;
                    contentEl.style.whiteSpace = 'pre-wrap';
                }
            }

            modalOverlay.style.display = '';
        }

        function closeModal() {
            if (modalOverlay) modalOverlay.style.display = 'none';
        }

        for (var j = 0; j < expandBtns.length; j++) {
            expandBtns[j].addEventListener('click', function (e) {
                var item = e.target.closest('.ww-disclaimer-item');
                if (item) openModal(item);
            });
        }

        if (modalOverlay) {
            // Close on overlay click
            modalOverlay.addEventListener('click', function (e) {
                if (e.target === modalOverlay) closeModal();
            });

            // Close on close buttons
            var closeBtns = modalOverlay.querySelectorAll('.ww-disclaimer-modal-close-btn');
            for (var k = 0; k < closeBtns.length; k++) {
                closeBtns[k].addEventListener('click', closeModal);
            }

            // Close on Escape key
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && modalOverlay.style.display !== 'none') {
                    closeModal();
                }
            });
        }

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
