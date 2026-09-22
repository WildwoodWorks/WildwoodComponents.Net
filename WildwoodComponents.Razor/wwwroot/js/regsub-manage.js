/*
 * WildwoodComponents.Razor - Registration & Subscription: manage view
 *
 * The client half of <vc:registration-subscription-manage />. Classic script, no modules, no
 * build step. Load order (the first three are shared with the other surfaces):
 *
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-packcheckout.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-planchange.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/subscription-admin.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-manage.js"></script>
 *
 * This file is DELIBERATELY thin, because the manage view is a composition rather than a new
 * surface:
 *
 *   * The panels are the platform's own (status, plans, features, packs, usage, overrides) and
 *     subscription-admin.js already wires every one of their actions - cancel, feature overrides,
 *     usage limits, and the pack rows' subscribe/cancel/reactivate. The manage root carries the
 *     .ww-subscription-admin-component class so all of that keeps working unchanged, and the
 *     panels' own ww-subscription-admin-changed / ww-entitlements-changed events bubble as they
 *     always have.
 *   * The plan change is regsub-planchange.js, the SAME driver subscription-admin.js uses - the
 *     admin panel hands it a message bar and this view hands it a notice region, and that is the
 *     whole of the difference. The root's data-ww-view="manage" is what tells subscription-admin
 *     to leave the plan change to this file rather than attach a second driver to the same root.
 *   * The pack picker is regsub-packcheckout.js, the SAME driver the signup's pack step uses.
 *
 * What is left here: which section is on screen, the plan-change notice, the pack picker's
 * selection, and the events a host listens for. No English (every string is server-rendered into
 * data-ww-labels or the markup), no money (the picker's prices were formatted by the server; the
 * plan-change confirmation's come from the live preview through the shared formatter), and no
 * markup - text goes in with textContent, and a source guard greps this file for every
 * markup-writing property.
 */
(function () {
    'use strict';

    var instances = {};

    var EVT_STEP = 'ww-regsub-step';
    var EVT_ERROR = 'ww-regsub-error';
    var EVT_PLAN_CHANGED = 'ww-regsub-plan-changed';
    var EVT_PAYMENT_REQUIRED = 'ww-regsub-payment-required';
    var EVT_PACKS_CHANGED = 'ww-regsub-packs-changed';
    var EVT_ENTITLEMENTS = 'ww-entitlements-changed';

    var REASON_TIER_CHANGE = 'tierChange';
    var REASON_ADD_ON = 'addOn';

    // ===== Pure decisions (no DOM, no state, no I/O) ==============================================

    /** Fills one {key} slot; an unmatched slot is left as written. */
    function formatLabel(template, key, value) {
        if (!template) return '';
        return String(template).split('{' + key + '}').join(String(value));
    }

    /** JSON the server wrote onto an attribute; an unreadable one is an empty object. */
    function parseJsonAttribute(value) {
        if (!value) return {};
        try {
            var parsed = JSON.parse(value);
            return parsed && typeof parsed === 'object' ? parsed : {};
        } catch (e) {
            return {};
        }
    }

    /** A comma-separated attribute as a list of non-empty ids. */
    function parseIdList(value) {
        var ids = [];
        if (!value) return ids;
        var parts = String(value).split(',');
        for (var i = 0; i < parts.length; i++) {
            var id = parts[i].trim();
            if (id.length > 0 && ids.indexOf(id) === -1) ids.push(id);
        }
        return ids;
    }

    /** The pack-status word for one outcome, off the server-rendered bundle. */
    function packStatusLabelKey(status) {
        if (status === 'trialing') return 'packStatusTrialing';
        if (status === 'active') return 'packStatusActive';
        if (status === 'granted') return 'packStatusGranted';
        return 'packStatusFailed';
    }

    /** Whether anything in the basket actually landed - the test for "entitlements changed". */
    function anyPackBought(outcomes) {
        if (!outcomes) return false;
        for (var i = 0; i < outcomes.length; i++) {
            var status = outcomes[i] && outcomes[i].status;
            if (status === 'trialing' || status === 'active') return true;
        }
        return false;
    }

    // ===== One instance ===========================================================================

    function initInstance(root) {
        if (!root) return null;

        var cid = root.getAttribute('data-component-id');
        if (!cid || instances[cid]) return instances[cid] || null;

        var machines = typeof window !== 'undefined' ? window.wwRegSubMachines : null;
        var planChanges = typeof window !== 'undefined' ? window.wwRegSubPlanChange : null;
        var packCheckouts = typeof window !== 'undefined' ? window.wwRegSubPackCheckout : null;

        if (!machines || !planChanges || !packCheckouts) {
            // Loading the three shared scripts is the host's one obligation; say so rather than
            // half-working.
            if (window.console) {
                window.console.error(
                    '[wwRegSubManage] regsub-machines.js, regsub-packcheckout.js and '
                    + 'regsub-planchange.js must be loaded first.');
            }
            return null;
        }

        var data = root.dataset;
        var appId = data.wwAppId || data.appId || '';
        var regsubProxy = (data.wwProxy || '/api/wildwood-regsub').replace(/\/+$/, '');
        var hostProxy = (data.proxyUrl || '/api/wildwood-app-tiers').replace(/\/+$/, '');
        var labels = parseJsonAttribute(data.wwLabels);
        var packNames = parseJsonAttribute(data.wwPackNames);
        var packOrder = parseIdList(data.wwPackOrder);
        var packMax = parseInt(data.wwPackMax, 10);
        if (!isFinite(packMax) || packMax <= 0) packMax = 25;

        var selectedPacks = [];
        var packRunner = null;
        var packsLanded = false;
        var lastStep = '';
        var detached = false;

        function q(selector) { return root.querySelector(selector); }
        function qa(selector) { return root.querySelectorAll(selector); }

        function setText(el, text) {
            if (el) el.textContent = text || '';
        }

        function show(el, visible) {
            if (el) el.hidden = !visible;
        }

        function raise(name, detail) {
            var event;
            try {
                event = new CustomEvent(name, { bubbles: true, detail: detail });
            } catch (e) {
                return;
            }
            root.dispatchEvent(event);
        }

        function report(code, message) {
            raise(EVT_ERROR, { code: code, message: message });
        }

        // ----- The proxies ------------------------------------------------------------------------

        function readJson(response) {
            if (response.status === 401) {
                var expired = new Error(labels.sessionExpired || '');
                expired.status = 401;
                throw expired;
            }
            if (!response.ok) {
                var failed = new Error('HTTP ' + response.status);
                failed.status = response.status;
                throw failed;
            }
            return response.json().catch(function () { return {}; });
        }

        function appQuery(path) {
            return path + (path.indexOf('?') === -1 ? '?' : '&') + 'appId=' + encodeURIComponent(appId);
        }

        function postTo(base, path, body) {
            return fetch(base + '/' + String(path).replace(/^\//, ''), {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(body || {})
            }).then(readJson);
        }

        function regsubPost(path, body) {
            return postTo(regsubProxy, appQuery(path), body);
        }

        function hostPost(path, body) {
            return postTo(hostProxy, path, body);
        }

        // ----- The plan change ----------------------------------------------------------------------

        var flow = planChanges.create({
            machines: machines,
            appId: appId,
            userId: data.userId || '',
            companyId: data.companyId || '',
            isCompanyMode: data.isCompanyMode === 'true',
            currency: data.currency || '',
            publishableKey: data.wwPublishableKey || '',
            container: root,
            labels: labels,
            text: {
                // The three modal strings the label contract names. The rest keep the words the
                // admin panel has always shown.
                upgradeTitle: labels.upgradeToPlan,
                keepPlan: labels.keepPlan,
                paymentMethodRequired: labels.paymentMethodRequired
            },
            hostPost: hostPost,
            regsubPost: regsubPost,
            onState: paintFlow,
            onError: report,
            onPaymentRequired: function (detail) { raise(EVT_PAYMENT_REQUIRED, detail); },
            onChanged: function () {
                raise(EVT_PLAN_CHANGED, {
                    appId: appId,
                    tierId: flow.getState().tierId,
                    pricingId: flow.getState().pricingId || null
                });
                // The established Razor refresh: the panels are server-rendered, so the server is
                // the one place that knows what they should say now.
                reload();
            },
            onEntitlements: function (reason) {
                raise(EVT_ENTITLEMENTS, { appId: appId, reason: reason || REASON_TIER_CHANGE });
            },
            confirmSubscribe: function (ctx) {
                var question = formatLabel(labels.subscribeConfirm, 'tier', ctx.tierName || '');
                return question.length === 0 || window.confirm(question);
            }
        });

        function paintFlow(view) {
            if (detached) return;

            root.setAttribute('data-ww-step', view.step);

            var status = q('[data-ww-plan-change-status]');
            if (view.step === 'authenticating') {
                setText(status, labels.authenticatingChange);
            } else if (view.step === 'completing') {
                setText(status, labels.applyingChange);
            } else {
                setText(status, '');
            }
            show(status, view.step === 'authenticating' || view.step === 'completing');

            var alert = q('[data-ww-plan-change-error]');
            setText(q('[data-ww-plan-change-message]'), view.error || '');
            show(alert, view.step === 'failed' && Boolean(view.error));
            show(q('[data-ww-action="retry-change"]'), view.canRetry);

            show(q('[data-ww-plan-change-notice]'),
                view.step === 'authenticating' || view.step === 'completing' || view.step === 'failed');

            if (view.step !== lastStep) {
                lastStep = view.step;
                raise(EVT_STEP, { step: view.step });
            }
        }

        function reload() {
            if (detached) return;
            window.setTimeout(function () { window.location.reload(); }, 1200);
        }

        // ----- The pack picker -----------------------------------------------------------------------

        function picker() { return q('[data-ww-pack-picker]'); }

        function openPicker() {
            selectedPacks = [];
            packsLanded = false;
            paintPicks();
            show(q('[data-ww-pack-outcomes]'), false);
            show(q('[data-ww-pack-grid]'), true);
            show(q('[data-ww-pack-continue]'), true);
            show(picker(), true);
        }

        function closePicker() {
            // A run whose money-moving call is in flight is never thrown away.
            if (packRunner && packRunner.isSettling()) return;

            show(picker(), false);
            if (packsLanded) reload();
        }

        function paintPicks() {
            var cards = qa('[data-ww-pack-picker] [data-ww-pack]');
            for (var i = 0; i < cards.length; i++) {
                var card = cards[i];
                var isSelected = selectedPacks.indexOf(card.getAttribute('data-ww-pack')) !== -1;
                card.classList.toggle('ww-pack-card--selected', isSelected);
                if (card.hasAttribute('aria-pressed')) {
                    card.setAttribute('aria-pressed', isSelected ? 'true' : 'false');
                }
            }

            var continueBtn = q('[data-ww-pack-continue]');
            if (continueBtn) {
                continueBtn.textContent = selectedPacks.length === 1
                    ? labels.continueWithOnePack
                    : formatLabel(labels.continueWithPacks, 'count', selectedPacks.length);
                continueBtn.disabled = selectedPacks.length === 0;
            }
        }

        function buyPacks() {
            if (selectedPacks.length === 0) return;

            show(q('[data-ww-pack-grid]'), false);
            show(q('[data-ww-pack-continue]'), false);

            packRunner = packCheckouts.create({
                machines: machines,
                appId: appId,
                scope: picker(),
                post: function (path, body) { return regsubPost(path, body); },
                labels: labels,
                packNames: packNames,
                publishableKey: data.wwPublishableKey || '',
                providerId: data.wwPaymentProvider || '',
                onError: report,
                onFinished: finishPacks
            });

            packRunner.start(packCheckouts.checkoutItems(selectedPacks.slice()));
        }

        function finishPacks(outcomes) {
            paintOutcomes(outcomes);
            raise(EVT_PACKS_CHANGED, { appId: appId, packs: outcomes });

            if (anyPackBought(outcomes)) {
                packsLanded = true;
                raise(EVT_ENTITLEMENTS, { appId: appId, reason: REASON_ADD_ON });
            }
        }

        function paintOutcomes(outcomes) {
            var list = q('[data-ww-pack-outcomes]');
            if (!list) return;

            while (list.firstChild) list.removeChild(list.firstChild);

            for (var i = 0; i < outcomes.length; i++) {
                var pack = outcomes[i];
                var row = document.createElement('li');
                row.className = 'ww-pack-outcome ww-pack-outcome--' + pack.status;
                row.setAttribute('data-ww-pack', pack.addOnId);

                var nameEl = document.createElement('span');
                nameEl.className = 'ww-pack-outcome-name';
                nameEl.textContent = pack.name;
                row.appendChild(nameEl);

                var statusEl = document.createElement('span');
                statusEl.className = 'ww-pack-outcome-status';
                statusEl.textContent = labels[packStatusLabelKey(pack.status)] || '';
                row.appendChild(statusEl);

                if (pack.errorMessage) {
                    var why = document.createElement('span');
                    why.className = 'ww-pack-outcome-error';
                    why.textContent = pack.errorMessage;
                    row.appendChild(why);
                }

                list.appendChild(row);
            }

            show(list, outcomes.length > 0);
        }

        // ----- Sections -------------------------------------------------------------------------------

        /**
         * The tabbed layout. subscription-admin.js already switches .ww-sub-admin-tab /
         * .ww-sub-admin-panel on this same root, so all this adds is React's own active-tab class
         * alongside the Bootstrap one.
         */
        function selectSection(name) {
            var tabs = qa('[data-ww-section][data-tab]');
            for (var i = 0; i < tabs.length; i++) {
                tabs[i].classList.toggle('ww-sub-admin-tab-active', tabs[i].getAttribute('data-tab') === name);
            }
        }

        // ----- Wiring ---------------------------------------------------------------------------------

        root.addEventListener('click', function (e) {
            var trigger = e.target.closest ? e.target.closest('[data-ww-action]') : null;
            if (!trigger || !root.contains(trigger)) return;

            switch (trigger.getAttribute('data-ww-action')) {
                case 'retry-change':
                    flow.retry();
                    break;

                case 'dismiss-change':
                    flow.reset();
                    break;

                case 'add-packs':
                    openPicker();
                    break;

                case 'close-packs':
                    closePicker();
                    break;

                case 'toggle-pack': {
                    var pack = trigger.closest('[data-ww-pack]');
                    if (!pack) break;
                    selectedPacks = packCheckouts.toggleSelection(
                        packOrder, selectedPacks, pack.getAttribute('data-ww-pack'), packMax);
                    paintPicks();
                    break;
                }

                case 'continue-packs':
                    buyPacks();
                    break;

                case 'confirm-card':
                    if (packRunner) packRunner.confirmCard();
                    break;

                case 'retry-packs':
                    if (packRunner) packRunner.retry();
                    break;

                case 'skip-packs':
                    if (packRunner) packRunner.skip();
                    break;

                case 'select-section':
                    selectSection(trigger.getAttribute('data-tab') || '');
                    break;

                default:
                    break;
            }
        });

        /**
         * The panels' own plan cards carry data-action="select-tier"; subscription-admin.js leaves
         * them alone on this root (data-ww-view="manage") so the change runs through the shared
         * driver from here instead.
         */
        root.addEventListener('click', function (e) {
            var btn = e.target.closest ? e.target.closest('[data-action="select-tier"]') : null;
            if (!btn || !root.contains(btn)) return;
            selectTier(btn);
        });

        function selectTier(btn) {
            var card = btn.closest('.ww-admin-plan-card');
            var isFree = btn.dataset.isFree === 'true';
            var pricingId = null;

            if (card && !isFree) {
                var visible = card.querySelector('.ww-admin-price-option:not(.d-none)');
                if (visible) pricingId = visible.dataset.pricingId;
            }

            flow.selectTier({
                tierId: btn.dataset.tierId,
                tierName: btn.dataset.tierName,
                pricingId: pricingId,
                pricingModelId: btn.dataset.pricingModelId || null,
                price: Number(btn.dataset.price || '0'),
                trialDays: parseInt(btn.dataset.trialDays || '0', 10) || 0,
                isChange: btn.dataset.isChange === 'true',
                isFreeTier: isFree
            });
        }

        instances[cid] = {
            root: root,

            /** The plan-change flow, for a host that wants to watch it. */
            getPlanChange: function () { return flow; },

            /**
             * DETACHES this instance. Nothing already under way is cut short - a plan change the
             * bank has authenticated is still completed on the server, and so is a pack's.
             */
            destroy: function () {
                detached = true;
                flow.destroy();
                if (packRunner) packRunner.destroy();
                delete instances[cid];
            }
        };

        return instances[cid];
    }

    // ===== AUTO-INIT =============================================================================

    function initAll() {
        var roots = document.querySelectorAll('[data-ww-view="manage"]');
        for (var i = 0; i < roots.length; i++) initInstance(roots[i]);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAll);
    } else {
        initAll();
    }

    window.wwRegSubManage = {
        init: initInstance,
        initAll: initAll,
        getInstance: function (componentId) { return instances[componentId] || null; },

        // Pure. The selection rules themselves live in regsub-packcheckout.js, which the Node
        // self-test covers; these two are this view's own.
        anyPackBought: anyPackBought,
        packStatusLabelKey: packStatusLabelKey
    };
})();
