/**
 * WildwoodComponents.Razor - Notification Inbox JavaScript
 * Hydrates the backend-connected notification INBOX: the bell (badge + dropdown list), the
 * standalone list, and the delivery-preferences form. All data flows through the same-origin
 * proxy at data-proxy-url (default /api/wildwood-notifications); the Bearer token stays
 * server-side (the proxy applies it). Distinct from notification.js, which drives the transient
 * in-memory toast/notification surface (window.wwNotification / window.wwToast).
 *
 * Behavior parity with @wildwood/react:
 *   - Poll a FULL refresh (list + unread count together) every ~45s so the badge and list never drift.
 *   - Badge hidden at 0; shows "N+" past maxBadgeCount.
 *   - Transient failures (proxy non-ok / 502) RETAIN the last-good data instead of clobbering it.
 *   - Clicking an unread item marks it read, then navigates to its link.
 *   - "Mark all read" disabled when unreadCount === 0.
 *   - Preferences toggles persist immediately (PUT) with optimistic rollback on failure; the browser
 *     toggle is gated on the Web Notifications permission (requested on enable).
 *   - Optional native browser notification for newly-arrived unread items (seeded silently on first load).
 */
(function () {
    'use strict';

    // ===== Web Notifications API helper (mirrors @wildwood/core browserNotifications.ts) =====
    var browserNotifications = {
        isSupported: function () {
            return typeof window !== 'undefined' && 'Notification' in window;
        },
        getPermission: function () {
            if (!browserNotifications.isSupported()) return 'unsupported';
            return Notification.permission;
        },
        requestPermission: function () {
            if (!browserNotifications.isSupported()) return Promise.resolve('unsupported');
            try {
                return Promise.resolve(Notification.requestPermission()).then(
                    function (r) { return r; },
                    function () { return browserNotifications.getPermission(); }
                );
            } catch (e) {
                return Promise.resolve(browserNotifications.getPermission());
            }
        },
        show: function (title, options) {
            options = options || {};
            if (!browserNotifications.isSupported()) return;
            if (Notification.permission !== 'granted') return;
            try {
                var n = new Notification(title, { body: options.body, tag: options.tag });
                if (options.onClick) {
                    n.onclick = function () {
                        try { if (window.focus) window.focus(); } catch (e) { /* embedded contexts */ }
                        options.onClick();
                    };
                }
            } catch (e) {
                // Some platforms (e.g. mobile Chrome) require a service worker — degrade silently.
            }
        }
    };
    window.wwBrowserNotifications = browserNotifications;

    // ===== helpers =====
    function escapeHtml(str) {
        var d = document.createElement('div');
        d.textContent = str == null ? '' : str;
        return d.innerHTML;
    }

    function timeAgo(timestamp) {
        var diff = Date.now() - new Date(timestamp).getTime();
        if (!isFinite(diff)) return '';
        if (diff < 60000) return 'just now';
        var mins = Math.floor(diff / 60000);
        if (mins < 60) return mins + 'm ago';
        var hours = Math.floor(mins / 60);
        if (hours < 24) return hours + 'h ago';
        return Math.floor(hours / 24) + 'd ago';
    }

    var CHECK_SVG = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';
    var CLOSE_SVG = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>';

    // ===== inbox engine (data + polling, shared by bell and standalone list) =====
    function createEngine(cfg) {
        // cfg: { proxyUrl, pollInterval, browserNotifications }
        var state = { notifications: [], unreadCount: 0, loading: true };
        var listeners = [];
        var seen = null;   // Set of seen ids (seeded silently on first load); null until then.
        var pollTimer = null;

        function emit() {
            for (var i = 0; i < listeners.length; i++) listeners[i](state);
        }

        function req(path, method) {
            return fetch(cfg.proxyUrl + path, {
                method: method || 'GET',
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            });
        }

        // Reconcile a freshly-loaded list against the seen set, raising a browser notification
        // for genuinely-new unread items (only after the initial silent seed).
        function reconcile(list) {
            if (seen === null) {
                seen = {};
                for (var s = 0; s < list.length; s++) seen[list[s].id] = true;
                return;
            }
            var canNotify = cfg.browserNotifications && browserNotifications.getPermission() === 'granted';
            for (var i = 0; i < list.length; i++) {
                var n = list[i];
                if (seen[n.id]) continue;
                seen[n.id] = true;
                if (canNotify && n.status === 'Unread') {
                    (function (item) {
                        browserNotifications.show(item.title || 'New notification', {
                            body: item.message,
                            tag: item.id,
                            onClick: item.link ? function () { window.location.assign(item.link); } : undefined
                        });
                    })(n);
                }
            }
        }

        function refresh() {
            var listP = req('', 'GET').then(function (r) {
                if (!r.ok) return null;   // transient / 502 -> retain last-good
                return r.json().catch(function () { return null; });
            }, function () { return null; });

            var countP = req('/count', 'GET').then(function (r) {
                if (!r.ok) return null;
                return r.json().catch(function () { return null; });
            }, function () { return null; });

            return Promise.all([listP, countP]).then(function (res) {
                var list = res[0], count = res[1];
                if (list !== null && Object.prototype.toString.call(list) === '[object Array]') {
                    reconcile(list);
                    state.notifications = list;
                }
                if (count !== null && isFinite(Number(count))) {
                    state.unreadCount = Number(count);
                }
                state.loading = false;
                emit();
            });
        }

        function markRead(id) {
            return req('/' + encodeURIComponent(id) + '/read', 'PUT').then(function (r) {
                if (r.ok) return refresh().then(function () { return true; });
                return false;
            }, function () { return false; });
        }

        function markAllRead() {
            return req('/read-all', 'PUT').then(function (r) {
                if (!r.ok) return 0;
                return r.json().catch(function () { return {}; }).then(function (body) {
                    return refresh().then(function () { return (body && body.markedAsRead) || 0; });
                });
            }, function () { return 0; });
        }

        function remove(id) {
            return req('/' + encodeURIComponent(id), 'DELETE').then(function (r) {
                if (r.ok) return refresh().then(function () { return true; });
                return false;
            }, function () { return false; });
        }

        return {
            subscribe: function (fn) { listeners.push(fn); fn(state); },
            start: function () {
                refresh();
                if (cfg.pollInterval > 0) pollTimer = setInterval(refresh, cfg.pollInterval);
            },
            refresh: refresh,
            markRead: markRead,
            markAllRead: markAllRead,
            remove: remove,
            getState: function () { return state; }
        };
    }

    // ===== list rendering (shared) =====
    function renderList(container, state, opts) {
        var html = '<div class="ww-notification-inbox">';

        if (opts.showMarkAllRead) {
            html += '<div class="ww-notification-inbox-header">' +
                '<span class="ww-notification-inbox-title">Notifications</span>' +
                '<button type="button" class="ww-btn ww-btn-sm ww-btn-outline" data-action="mark-all"' +
                (state.unreadCount === 0 ? ' disabled' : '') + '>Mark all read</button>' +
                '</div>';
        }

        if (state.loading && state.notifications.length === 0) {
            html += '<div class="ww-notification-inbox-empty">Loading…</div>';
        } else if (state.notifications.length === 0) {
            html += '<div class="ww-notification-inbox-empty">' + escapeHtml(opts.emptyText) + '</div>';
        } else {
            html += '<ul class="ww-notification-inbox-list">';
            for (var i = 0; i < state.notifications.length; i++) {
                var n = state.notifications[i];
                var isUnread = n.status === 'Unread';
                var clickable = !!n.link;

                html += '<li class="ww-notification-inbox-item' + (isUnread ? ' ww-notification-inbox-item-unread' : '') + '">';
                html += '<button type="button" class="ww-notification-inbox-item-main" data-action="open" data-id="' +
                    escapeHtml(n.id) + '" data-clickable="' + clickable + '" aria-label="' + escapeHtml(n.title || n.message) + '">';
                if (isUnread) html += '<span class="ww-notification-inbox-dot" aria-hidden="true"></span>';
                html += '<span class="ww-notification-inbox-item-body">';
                if (n.title) html += '<span class="ww-notification-inbox-item-title">' + escapeHtml(n.title) + '</span>';
                html += '<span class="ww-notification-inbox-item-message">' + escapeHtml(n.message) + '</span>';
                html += '<span class="ww-notification-inbox-item-time">' + escapeHtml(timeAgo(n.createdAt)) + '</span>';
                html += '</span></button>';

                html += '<div class="ww-notification-inbox-item-actions">';
                if (isUnread) {
                    html += '<button type="button" class="ww-btn-icon ww-btn-sm" data-action="read" data-id="' +
                        escapeHtml(n.id) + '" aria-label="Mark as read" title="Mark as read">' + CHECK_SVG + '</button>';
                }
                html += '<button type="button" class="ww-btn-icon ww-btn-sm" data-action="remove" data-id="' +
                    escapeHtml(n.id) + '" aria-label="Delete notification" title="Delete">' + CLOSE_SVG + '</button>';
                html += '</div></li>';
            }
            html += '</ul>';
        }

        html += '</div>';
        container.innerHTML = html;
    }

    // Delegated click handling attached ONCE per container (innerHTML re-renders keep working).
    function wireContainer(container, engine, onItemClick) {
        container.addEventListener('click', function (e) {
            var btn = e.target && e.target.closest ? e.target.closest('[data-action]') : null;
            if (!btn || !container.contains(btn)) return;
            var action = btn.getAttribute('data-action');
            var id = btn.getAttribute('data-id');
            if (action === 'mark-all') {
                if (!btn.disabled) engine.markAllRead();
            } else if (action === 'read') {
                engine.markRead(id);
            } else if (action === 'remove') {
                engine.remove(id);
            } else if (action === 'open') {
                var list = engine.getState().notifications;
                for (var i = 0; i < list.length; i++) {
                    if (list[i].id === id) { onItemClick(list[i]); break; }
                }
            }
        });
    }

    // ===== bell =====
    function initBell(root) {
        var proxyUrl = (root.getAttribute('data-proxy-url') || '/api/wildwood-notifications').replace(/\/+$/, '');
        var maxBadge = parseInt(root.getAttribute('data-max-badge-count') || '99', 10);
        var pollInterval = parseInt(root.getAttribute('data-poll-interval') || '45000', 10);
        var emptyText = root.getAttribute('data-empty-text') || 'No notifications';
        var browserNotif = root.getAttribute('data-browser-notifications') === 'true';

        var button = root.querySelector('.ww-notification-bell-button');
        var badge = root.querySelector('.ww-notification-bell-badge');
        var panel = root.querySelector('.ww-notification-bell-panel');
        var container = root.querySelector('.ww-notification-list-container');
        if (!button || !panel || !container) return;

        var engine = createEngine({ proxyUrl: proxyUrl, pollInterval: pollInterval, browserNotifications: browserNotif });
        var open = false;

        function setOpen(v) {
            open = v;
            panel.style.display = v ? '' : 'none';
            button.setAttribute('aria-expanded', v ? 'true' : 'false');
        }
        function closePanel() { setOpen(false); }

        function onItemClick(n) {
            if (n.status === 'Unread') engine.markRead(n.id);
            if (n.link) window.location.assign(n.link);
            closePanel();
        }
        wireContainer(container, engine, onItemClick);

        button.addEventListener('click', function (e) { e.stopPropagation(); setOpen(!open); });
        document.addEventListener('mousedown', function (e) {
            if (open && !root.contains(e.target)) closePanel();
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && open) closePanel();
        });

        engine.subscribe(function (state) {
            if (badge) {
                if (state.unreadCount > 0) {
                    badge.textContent = state.unreadCount > maxBadge ? (maxBadge + '+') : String(state.unreadCount);
                    badge.style.display = '';
                } else {
                    badge.style.display = 'none';
                }
            }
            button.setAttribute('aria-label',
                state.unreadCount > 0 ? ('Notifications, ' + state.unreadCount + ' unread') : 'Notifications');
            renderList(container, state, { showMarkAllRead: true, emptyText: emptyText });
        });

        engine.start();
    }

    // ===== standalone list =====
    function initStandalone(root) {
        var proxyUrl = (root.getAttribute('data-proxy-url') || '/api/wildwood-notifications').replace(/\/+$/, '');
        var pollInterval = parseInt(root.getAttribute('data-poll-interval') || '45000', 10);
        var emptyText = root.getAttribute('data-empty-text') || 'No notifications';
        var showMarkAll = root.getAttribute('data-show-mark-all-read') !== 'false';
        var browserNotif = root.getAttribute('data-browser-notifications') === 'true';

        var container = root.querySelector('.ww-notification-list-container');
        if (!container) return;

        var engine = createEngine({ proxyUrl: proxyUrl, pollInterval: pollInterval, browserNotifications: browserNotif });

        function onItemClick(n) {
            if (n.status === 'Unread') engine.markRead(n.id);
            if (n.link) window.location.assign(n.link);
        }
        wireContainer(container, engine, onItemClick);

        engine.subscribe(function (state) {
            renderList(container, state, { showMarkAllRead: showMarkAll, emptyText: emptyText });
        });

        engine.start();
    }

    // ===== preferences =====
    function initPreferences(root) {
        var proxyUrl = (root.getAttribute('data-proxy-url') || '/api/wildwood-notifications').replace(/\/+$/, '');
        var appId = root.getAttribute('data-app-id') || '';
        var eventOptOutsJson = root.getAttribute('data-event-opt-outs-json');
        if (eventOptOutsJson === '' || eventOptOutsJson === null) eventOptOutsJson = null;

        var savingEl = root.querySelector('.ww-notification-prefs-saving');
        var browserHintEl = root.querySelector('[data-role="browser-hint"]');
        var toggles = root.querySelectorAll('.ww-notification-prefs-toggle');

        function setSaving(on) { if (savingEl) savingEl.style.display = on ? '' : 'none'; }

        function permissionLabel(perm) {
            return perm === 'granted' ? 'Allowed'
                : perm === 'denied' ? 'Blocked'
                : perm === 'unsupported' ? 'Not supported'
                : 'Not yet allowed';
        }
        function updateBrowserHint(extra) {
            if (!browserHintEl) return;
            var label = 'Permission: ' + permissionLabel(browserNotifications.getPermission());
            browserHintEl.textContent = extra ? (label + ' — ' + extra) : label;
        }
        updateBrowserHint();

        function buildPref() {
            var pref = {
                appId: appId,
                emailEnabled: false,
                smsEnabled: false,
                pushEnabled: false,
                browserEnabled: false,
                eventOptOutsJson: eventOptOutsJson
            };
            for (var i = 0; i < toggles.length; i++) {
                pref[toggles[i].getAttribute('data-channel')] = toggles[i].checked;
            }
            return pref;
        }

        // persist current toggle state; on failure restore the given channel to prevValue.
        function persist(channel, prevValue) {
            setSaving(true);
            return fetch(proxyUrl + '/preferences', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(buildPref())
            }).then(function (r) {
                setSaving(false);
                if (!r.ok) { restore(channel, prevValue); return false; }
                return true;
            }, function () {
                setSaving(false);
                restore(channel, prevValue);
                return false;
            });
        }

        function restore(channel, value) {
            for (var i = 0; i < toggles.length; i++) {
                if (toggles[i].getAttribute('data-channel') === channel) { toggles[i].checked = value; break; }
            }
        }

        for (var t = 0; t < toggles.length; t++) {
            (function (toggle) {
                var channel = toggle.getAttribute('data-channel');
                toggle.addEventListener('change', function () {
                    var prevValue = !toggle.checked; // state before this change

                    if (channel === 'browserEnabled' && toggle.checked) {
                        // Turning the browser channel ON requires an explicit permission grant.
                        browserNotifications.requestPermission().then(function (result) {
                            if (result === 'granted') {
                                updateBrowserHint();
                                persist(channel, prevValue);
                            } else {
                                toggle.checked = false;
                                updateBrowserHint(result === 'denied'
                                    ? 'Blocked in your browser settings'
                                    : 'Browser notifications were not enabled');
                            }
                        });
                        return;
                    }

                    persist(channel, prevValue);
                });
            })(toggles[t]);
        }
    }

    // ===== bootstrap =====
    function initAll() {
        var bells = document.querySelectorAll('.ww-notification-bell');
        for (var a = 0; a < bells.length; a++) {
            if (bells[a].dataset.wwInboxInit === 'true') continue;
            bells[a].dataset.wwInboxInit = 'true';
            initBell(bells[a]);
        }
        var standalone = document.querySelectorAll('.ww-notification-inbox-standalone');
        for (var b = 0; b < standalone.length; b++) {
            if (standalone[b].dataset.wwInboxInit === 'true') continue;
            standalone[b].dataset.wwInboxInit = 'true';
            initStandalone(standalone[b]);
        }
        var prefs = document.querySelectorAll('.ww-notification-prefs');
        for (var c = 0; c < prefs.length; c++) {
            if (prefs[c].dataset.wwInboxInit === 'true') continue;
            prefs[c].dataset.wwInboxInit = 'true';
            initPreferences(prefs[c]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAll);
    } else {
        initAll();
    }
})();
