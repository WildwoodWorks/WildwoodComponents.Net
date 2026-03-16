/**
 * WildwoodComponents.Razor - Notification & Toast Component JavaScript
 * Manages notification creation, dismissal, auto-expiry with progress bar, and animations.
 * Provides a public API for consuming apps to show notifications programmatically.
 */
(function () {
    'use strict';

    var typeIcons = {
        Success: 'bi-check-circle-fill',
        Error: 'bi-exclamation-triangle-fill',
        Warning: 'bi-exclamation-circle-fill',
        Info: 'bi-info-circle-fill'
    };

    // ===== NOTIFICATION COMPONENT =====

    var notifRoots = document.querySelectorAll('.ww-notification-component');
    for (var r = 0; r < notifRoots.length; r++) {
        initNotification(notifRoots[r]);
    }

    function initNotification(root) {
        var cid = root.dataset.componentId;
        var defaultDuration = parseInt(root.dataset.defaultDuration || '5000', 10);
        var maxVisible = parseInt(root.dataset.maxVisible || '5', 10);

        var itemsContainer = document.getElementById('ww-notif-items-' + cid);
        var template = document.getElementById('ww-notif-template-' + cid);
        var dismissAllWrap = document.getElementById('ww-notif-dismiss-all-' + cid);

        if (!itemsContainer || !template) return;

        var items = [];

        function updateDismissAll() {
            if (dismissAllWrap) {
                dismissAllWrap.style.display = items.length > 1 ? '' : 'none';
            }
        }

        function dismiss(id) {
            var idx = -1;
            for (var i = 0; i < items.length; i++) {
                if (items[i].id === id) { idx = i; break; }
            }
            if (idx === -1) return;

            var item = items[idx];
            if (item.timer) clearTimeout(item.timer);
            item.el.classList.add('ww-notif-removing');

            setTimeout(function () {
                if (item.el.parentNode) item.el.parentNode.removeChild(item.el);
                items.splice(idx, 1);
                updateDismissAll();
            }, 300);
        }

        function dismissAll() {
            var ids = [];
            for (var i = 0; i < items.length; i++) ids.push(items[i].id);
            for (var i = 0; i < ids.length; i++) dismiss(ids[i]);
        }

        function show(options) {
            var id = options.id || ('n-' + Date.now() + '-' + Math.random().toString(36).substr(2, 5));
            var type = options.type || 'Info';
            var duration = options.duration !== undefined ? options.duration : defaultDuration;

            // Enforce max visible
            while (items.length >= maxVisible) {
                dismiss(items[0].id);
            }

            var clone = template.content.cloneNode(true);
            var el = clone.querySelector('.ww-notif-item');
            el.dataset.type = type;
            el.dataset.notifId = id;

            var icon = el.querySelector('.ww-notif-icon');
            if (icon) icon.classList.add(typeIcons[type] || typeIcons.Info);

            var title = el.querySelector('.ww-notif-title');
            if (title) title.textContent = options.title || type;

            var message = el.querySelector('.ww-notif-message');
            if (message) message.textContent = options.message || '';

            // Actions
            if (options.actions && options.actions.length > 0) {
                var actionsContainer = el.querySelector('.ww-notif-actions');
                for (var i = 0; i < options.actions.length; i++) {
                    var action = options.actions[i];
                    var btn = document.createElement('button');
                    btn.className = 'btn btn-sm btn-' + (action.style || 'primary').toLowerCase();
                    btn.textContent = action.text;
                    btn.dataset.actionId = action.id || '';
                    (function (a, nid) {
                        btn.addEventListener('click', function () {
                            root.dispatchEvent(new CustomEvent('ww-notification-action', {
                                detail: { notificationId: nid, actionId: a.id, data: a.data },
                                bubbles: true
                            }));
                            if (a.dismissOnClick !== false) dismiss(nid);
                        });
                    })(action, id);
                    actionsContainer.appendChild(btn);
                }
            }

            // Dismiss button
            var dismissBtn = el.querySelector('.ww-notif-dismiss-btn');
            if (dismissBtn) {
                (function (nid) {
                    dismissBtn.addEventListener('click', function () { dismiss(nid); });
                })(id);
            }

            // Progress bar animation
            var progressBar = el.querySelector('.ww-notif-progress-bar');
            var timer = null;
            if (duration > 0 && progressBar) {
                progressBar.style.transition = 'transform ' + duration + 'ms linear';
                requestAnimationFrame(function () {
                    progressBar.style.transform = 'scaleX(0)';
                });
                timer = setTimeout(function () { dismiss(id); }, duration);

                // Pause on hover
                el.addEventListener('mouseenter', function () {
                    if (timer) { clearTimeout(timer); timer = null; }
                    progressBar.style.transitionDuration = '0s';
                    var computed = getComputedStyle(progressBar).transform;
                    progressBar.style.transform = computed;
                });
                el.addEventListener('mouseleave', function () {
                    var currentScale = parseFloat(getComputedStyle(progressBar).transform.split(',')[0].replace('matrix(', '')) || 0;
                    var remaining = duration * currentScale;
                    if (remaining > 0) {
                        progressBar.style.transition = 'transform ' + remaining + 'ms linear';
                        requestAnimationFrame(function () {
                            progressBar.style.transform = 'scaleX(0)';
                        });
                        timer = setTimeout(function () { dismiss(id); }, remaining);
                    } else {
                        dismiss(id);
                    }
                });
            } else if (progressBar) {
                progressBar.parentNode.style.display = 'none';
            }

            itemsContainer.appendChild(clone);
            items.push({ id: id, el: itemsContainer.lastElementChild, timer: timer });
            updateDismissAll();

            return id;
        }

        // Dismiss all button
        if (dismissAllWrap) {
            var dismissAllBtn = dismissAllWrap.querySelector('.ww-notif-dismiss-all-btn');
            if (dismissAllBtn) dismissAllBtn.addEventListener('click', dismissAll);
        }

        // Store instance for public API
        root._wwNotif = { show: show, dismiss: dismiss, dismissAll: dismissAll };
    }

    // ===== TOAST COMPONENT =====

    var toastRoots = document.querySelectorAll('.ww-toast-component');
    for (var r = 0; r < toastRoots.length; r++) {
        initToast(toastRoots[r]);
    }

    function initToast(root) {
        var cid = root.dataset.componentId;
        var defaultDuration = parseInt(root.dataset.defaultDuration || '5000', 10);
        var maxVisible = parseInt(root.dataset.maxVisible || '5', 10);

        var itemsContainer = document.getElementById('ww-toast-items-' + cid);
        var template = document.getElementById('ww-toast-template-' + cid);
        var dismissAllWrap = document.getElementById('ww-toast-dismiss-all-' + cid);

        if (!itemsContainer || !template) return;

        var items = [];

        function updateDismissAll() {
            if (dismissAllWrap) {
                dismissAllWrap.style.display = items.length > 1 ? '' : 'none';
            }
        }

        function dismiss(id) {
            var idx = -1;
            for (var i = 0; i < items.length; i++) {
                if (items[i].id === id) { idx = i; break; }
            }
            if (idx === -1) return;

            var item = items[idx];
            if (item.timer) clearTimeout(item.timer);
            item.el.classList.add('ww-toast-removing');

            setTimeout(function () {
                if (item.el.parentNode) item.el.parentNode.removeChild(item.el);
                items.splice(idx, 1);
                updateDismissAll();
            }, 300);
        }

        function dismissAll() {
            var ids = [];
            for (var i = 0; i < items.length; i++) ids.push(items[i].id);
            for (var i = 0; i < ids.length; i++) dismiss(ids[i]);
        }

        function show(options) {
            var id = options.id || ('t-' + Date.now() + '-' + Math.random().toString(36).substr(2, 5));
            var type = options.type || 'Info';
            var duration = options.duration !== undefined ? options.duration : defaultDuration;

            while (items.length >= maxVisible) {
                dismiss(items[0].id);
            }

            var clone = template.content.cloneNode(true);
            var el = clone.querySelector('.ww-toast-item');
            el.dataset.type = type;
            el.dataset.toastId = id;

            var icon = el.querySelector('.ww-toast-icon');
            if (icon) icon.classList.add(typeIcons[type] || typeIcons.Info);

            var title = el.querySelector('.ww-toast-title');
            if (title) title.textContent = options.title || '';

            var message = el.querySelector('.ww-toast-message');
            if (message) message.textContent = options.message || '';

            // Actions
            if (options.actions && options.actions.length > 0) {
                var actionsContainer = el.querySelector('.ww-toast-actions');
                for (var i = 0; i < options.actions.length; i++) {
                    var action = options.actions[i];
                    var btn = document.createElement('button');
                    btn.className = 'btn btn-sm btn-' + (action.style || 'primary').toLowerCase();
                    btn.textContent = action.text;
                    (function (a, tid) {
                        btn.addEventListener('click', function () {
                            root.dispatchEvent(new CustomEvent('ww-toast-action', {
                                detail: { toastId: tid, actionId: a.id, data: a.data },
                                bubbles: true
                            }));
                            if (a.dismissOnClick !== false) dismiss(tid);
                        });
                    })(action, id);
                    actionsContainer.appendChild(btn);
                }
            }

            // Dismiss button
            var closeBtn = el.querySelector('.ww-toast-close');
            if (closeBtn) {
                (function (tid) {
                    closeBtn.addEventListener('click', function () { dismiss(tid); });
                })(id);
            }

            // Timer bar
            var timerBar = el.querySelector('.ww-toast-timer-bar');
            var timer = null;
            if (duration > 0 && timerBar) {
                timerBar.style.transition = 'transform ' + duration + 'ms linear';
                requestAnimationFrame(function () {
                    timerBar.style.transform = 'scaleX(0)';
                });
                timer = setTimeout(function () { dismiss(id); }, duration);

                el.addEventListener('mouseenter', function () {
                    if (timer) { clearTimeout(timer); timer = null; }
                    timerBar.style.transitionDuration = '0s';
                    var computed = getComputedStyle(timerBar).transform;
                    timerBar.style.transform = computed;
                });
                el.addEventListener('mouseleave', function () {
                    var currentScale = parseFloat(getComputedStyle(timerBar).transform.split(',')[0].replace('matrix(', '')) || 0;
                    var remaining = duration * currentScale;
                    if (remaining > 0) {
                        timerBar.style.transition = 'transform ' + remaining + 'ms linear';
                        requestAnimationFrame(function () {
                            timerBar.style.transform = 'scaleX(0)';
                        });
                        timer = setTimeout(function () { dismiss(id); }, remaining);
                    } else {
                        dismiss(id);
                    }
                });
            } else if (timerBar) {
                timerBar.parentNode.style.display = 'none';
            }

            itemsContainer.appendChild(clone);
            items.push({ id: id, el: itemsContainer.lastElementChild, timer: timer });
            updateDismissAll();

            return id;
        }

        if (dismissAllWrap) {
            var dismissAllBtn = dismissAllWrap.querySelector('.ww-toast-dismiss-all-btn');
            if (dismissAllBtn) dismissAllBtn.addEventListener('click', dismissAll);
        }

        root._wwToast = { show: show, dismiss: dismiss, dismissAll: dismissAll };
    }

    // ===== PUBLIC API =====

    window.wwNotification = {
        /**
         * Show a notification. Options: { type, title, message, duration, actions }
         * type: 'Success' | 'Error' | 'Warning' | 'Info'
         */
        show: function (options) {
            var root = document.querySelector('.ww-notification-component');
            if (root && root._wwNotif) return root._wwNotif.show(options);
        },
        dismiss: function (id) {
            var root = document.querySelector('.ww-notification-component');
            if (root && root._wwNotif) root._wwNotif.dismiss(id);
        },
        dismissAll: function () {
            var root = document.querySelector('.ww-notification-component');
            if (root && root._wwNotif) root._wwNotif.dismissAll();
        }
    };

    window.wwToast = {
        /**
         * Show a toast. Options: { type, title, message, duration, actions }
         * type: 'Success' | 'Error' | 'Warning' | 'Info'
         */
        show: function (options) {
            var root = document.querySelector('.ww-toast-component');
            if (root && root._wwToast) return root._wwToast.show(options);
        },
        dismiss: function (id) {
            var root = document.querySelector('.ww-toast-component');
            if (root && root._wwToast) root._wwToast.dismiss(id);
        },
        dismissAll: function () {
            var root = document.querySelector('.ww-toast-component');
            if (root && root._wwToast) root._wwToast.dismissAll();
        }
    };
})();
