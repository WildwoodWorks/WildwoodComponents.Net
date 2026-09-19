/*
 * WildwoodComponents.Razor - Registration & Subscription: signup view
 *
 * The client half of <vc:registration-subscription-signup />. Classic script, no modules, no
 * build step, matching every other file in this folder. Load regsub-machines.js FIRST:
 *
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/payment.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-signup.js"></script>
 *
 * PAY-FIRST, as React and Blazor ship it. The plan's card is taken BEFORE the account exists, so
 * a declined card leaves nothing behind rather than an account sitting on a plan nobody paid for;
 * and the packs are bought AFTER the sign-in, because they are bought as the user:
 *
 *   register form -> token check -> plan -> packs -> plan payment
 *     -> register, log in, link the payment, subscribe   (one resumable attempt)
 *     -> disclaimers -> pack checkout -> success
 *
 * Three rules this file keeps, and they are the reason it is shaped the way it is.
 *
 *   1. NO ENGLISH, NO MONEY, NO MARKUP. Every visible string was rendered by the server, either
 *      into the step's markup or into the root's data-ww-labels. Text is written with
 *      textContent; no markup-writing property is ever assigned, and a source guard greps this
 *      file for one. Amounts
 *      are copied from server-rendered nodes, never formatted here.
 *   2. NO WORK IS DONE TWICE. Every async step is claimed by the machine's step token before it
 *      starts, so a double click, a doubled callback and a retry cannot register, charge or buy
 *      twice. The account creation writes its progress onto one attempt record, so Try Again
 *      RESUMES: it never re-registers and never re-charges.
 *   3. NOTHING WHOSE MONEY-MOVING CALL WAS ISSUED IS ABANDONED. A full page unload has nothing to
 *      drain, but inside the page - Start Over, or a host removing the component - an item that
 *      has already been authenticated with the bank is completed on the server before this
 *      instance lets go. The authenticate-then-complete pair is therefore ONE awaited chain
 *      rather than two separately pumped steps.
 *
 * Everything the browser posts goes to the SHIPPED same-origin proxy (/api/wildwood-regsub):
 * register, log in, link the payment, subscribe, the disclaimer gate, the pack checkout and the
 * payment intent. A consuming app writes none of those routes.
 */
(function () {
    'use strict';

    var instances = {};

    var EVT_COMPLETE = 'ww-regsub-signup-complete';
    var EVT_SIGNED_IN = 'ww-regsub-already-signed-in';
    var EVT_CANCEL = 'ww-regsub-cancel';
    var EVT_ERROR = 'ww-regsub-error';
    var EVT_ENTITLEMENTS = 'ww-entitlements-changed';
    var EVT_STEP = 'ww-regsub-step';

    var STRIPE_SRC = 'https://js.stripe.com/v3/';
    var DEFAULT_PACK_MAX = 25;
    var MAX_PUMP_ITERATIONS = 32;

    // Codes, matching the other stacks exactly. A host's analytics switches on these.
    var CODE_SIGNUP_FAILED = 'signup_failed';
    var CODE_TOKEN_REJECTED = 'registration_token_rejected';
    var CODE_CATALOG = 'catalog_unavailable';
    var CODE_PACK_QUOTE = 'pack_quote_failed';
    var CODE_PACK_CARD = 'pack_card_failed';
    var CODE_PACK_CHECKOUT = 'pack_checkout_failed';

    // The three sentences React itself hard-codes inside the pack checkout rather than putting
    // them in its label bundle. They are reproduced verbatim so the stacks read the same; every
    // OTHER string in this file comes off the server.
    var CARD_UNAVAILABLE_MESSAGE = 'A card could not be collected right now.';

    // ===== Pure decisions (no DOM, no state, no I/O) ==============================================

    /** RegistrationSubscriptionLabels.Format: fills one {key} slot; an unmatched slot is left as is. */
    function formatLabel(template, key, value) {
        if (!template) return '';
        return String(template).split('{' + key + '}').join(String(value));
    }

    /**
     * SignupPlanDecisions.FindGrantForApp. The app id is matched CASE-INSENSITIVELY: the server's
     * casing for a GUID is not the link's, and an exact match would silently lose the plan the
     * token actually grants.
     */
    function findGrantForApp(details, appId) {
        if (!details || !details.appGrants || !appId) return null;
        var wanted = String(appId).toLowerCase();
        for (var i = 0; i < details.appGrants.length; i++) {
            var grant = details.appGrants[i];
            if (grant && String(grant.appId || '').toLowerCase() === wanted) return grant;
        }
        return null;
    }

    /** SignupViewDecisions.ToMachineGrant: ids only, no display names. */
    function toMachineGrant(grant) {
        if (!grant) return undefined;
        return {
            tierId: grant.appTierId || '',
            pricingId: grant.appTierPricingId || undefined,
            addOnIds: grant.addOnIds || [],
            featureCodes: grant.featureCodes || []
        };
    }

    /** SignupViewDecisions.WithoutGranted: a granted pack is neither charged for nor shown as chosen. */
    function withoutGranted(selected, grantedIds) {
        if (!grantedIds || grantedIds.length === 0) return selected.slice();
        var kept = [];
        for (var i = 0; i < selected.length; i++) {
            if (grantedIds.indexOf(selected[i]) === -1) kept.push(selected[i]);
        }
        return kept;
    }

    /**
     * SignupViewDecisions.SuccessMessage, as a choice of which server-rendered template applies.
     * Returns the template's key, not a sentence.
     */
    function successTemplateKey(hasGrant, hasPlan, subscriptionFailed, trialDays) {
        if (hasGrant) return 'successToken';
        if (!hasPlan) return 'successPlain';
        if (subscriptionFailed) return 'successPending';
        if (trialDays > 0) return 'successTrial';
        return 'successActive';
    }

    /** The pack-status word for one outcome, off the server-rendered bundle. */
    function packStatusLabelKey(status) {
        if (status === 'trialing') return 'packStatusTrialing';
        if (status === 'active') return 'packStatusActive';
        if (status === 'granted') return 'packStatusGranted';
        return 'packStatusFailed';
    }

    /**
     * PackCheckout's toOutcomes: ONE outcome per requested item, in the order they were asked for.
     * A status that is not trialing/active is a failure, with the server's own words when it sent
     * any.
     */
    function toOutcomes(items, results, quote, names, fallbackMessage) {
        var outcomes = [];
        var lines = (quote && quote.lines) || [];

        for (var i = 0; i < items.length; i++) {
            var addOnId = items[i].addOnId;
            var result = null;
            for (var r = 0; r < results.length; r++) {
                if (results[r] && results[r].addOnId === addOnId) { result = results[r]; break; }
            }

            var quoted = null;
            for (var q = 0; q < lines.length; q++) {
                if (lines[q] && lines[q].addOnId === addOnId) { quoted = lines[q]; break; }
            }

            var name = names[addOnId] || (quoted && quoted.name) || addOnId;
            var status = result ? result.status : null;

            if (status === 'trialing' || status === 'active') {
                outcomes.push({
                    addOnId: addOnId,
                    name: name,
                    status: status,
                    trialEnd: result.trialEnd || undefined
                });
            } else {
                outcomes.push({
                    addOnId: addOnId,
                    name: name,
                    status: 'failed',
                    errorMessage: (result && result.errorMessage) || fallbackMessage
                });
            }
        }

        return outcomes;
    }

    /** Every requested pack as a checkout item. */
    function checkoutItems(addOnIds) {
        var items = [];
        for (var i = 0; i < addOnIds.length; i++) items.push({ addOnId: addOnIds[i] });
        return items;
    }

    /** A basket in CATALOG order rather than click order, so it reads like the grid it came from. */
    function orderSelection(order, selected) {
        var ordered = [];
        for (var i = 0; i < order.length; i++) {
            if (selected.indexOf(order[i]) !== -1) ordered.push(order[i]);
        }
        return ordered;
    }

    /** Tick or untick one pack. Unticking is always allowed, cap or no cap. */
    function toggleSelection(order, selected, addOnId, max) {
        var index = selected.indexOf(addOnId);
        if (index === -1) {
            if (selected.length >= max) return selected.slice();
            return orderSelection(order, selected.concat([addOnId]));
        }
        var next = selected.slice();
        next.splice(index, 1);
        return orderSelection(order, next);
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

    /** A positive integer attribute, or the shipped default when it is missing or junk. */
    function parseMax(value) {
        var parsed = parseInt(value, 10);
        return isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_PACK_MAX;
    }

    /** JSON the server wrote onto an attribute; an unreadable one is an empty object, never a throw. */
    function parseJsonAttribute(value) {
        if (!value) return {};
        try {
            var parsed = JSON.parse(value);
            return parsed && typeof parsed === 'object' ? parsed : {};
        } catch (e) {
            return {};
        }
    }

    /** The message a refused call is reported with: the server's words, else the shipped fallback. */
    function refusalMessage(result, fallback) {
        if (result && result.errorMessage) return result.errorMessage;
        if (result && result.message) return result.message;
        return fallback;
    }

    /**
     * The URL a plan choice navigates to when the chosen plan is not the one the server priced.
     * Same keys the pricing view's select-url writes, so one link format serves both.
     */
    function buildPlanUrl(location, selection) {
        var params = new URLSearchParams(location.search || '');
        params.delete('tier');
        params.delete('pricing');
        params.delete('addons');

        if (selection.tierId) params.set('tier', selection.tierId);
        if (selection.pricingId) params.set('pricing', selection.pricingId);
        if (selection.addOnIds && selection.addOnIds.length > 0) {
            params.set('addons', selection.addOnIds.join(','));
        }

        var query = params.toString();
        return location.pathname + (query.length > 0 ? '?' + query : '') + (location.hash || '');
    }

    /** Whether a chosen plan is the one the server already rendered the card form for. */
    function isRenderedPlan(rendered, chosen) {
        return String(rendered.tierId || '').toLowerCase() === String(chosen.tierId || '').toLowerCase()
            && String(rendered.pricingId || '') === String(chosen.pricingId || '');
    }

    // ===== DOM and I/O ============================================================================

    /** Loads a script once, deduping by exact src - the same rule payment.js follows. */
    function loadScript(src) {
        return new Promise(function (resolve, reject) {
            var existing = document.querySelectorAll('script[src]');
            for (var i = 0; i < existing.length; i++) {
                if (existing[i].getAttribute('src') === src) {
                    resolve();
                    return;
                }
            }
            var script = document.createElement('script');
            script.src = src;
            script.async = true;
            script.onload = function () { resolve(); };
            script.onerror = function () { reject(new Error('Could not load ' + src)); };
            document.head.appendChild(script);
        });
    }

    function initInstance(root) {
        if (!root) return null;

        var cid = root.getAttribute('data-component-id');
        if (!cid || instances[cid]) return instances[cid] || null;

        var machines = window.wwRegSubMachines;
        if (!machines) {
            // Loading regsub-machines.js is the host's one obligation; say so rather than
            // half-working.
            if (window.console) window.console.error('[wwRegSubSignup] regsub-machines.js must be loaded first.');
            return null;
        }

        var data = root.dataset;
        var appId = data.wwAppId || '';
        var proxy = (data.wwProxy || '/api/wildwood-regsub').replace(/\/+$/, '');
        var labels = parseJsonAttribute(data.wwLabels);
        var names = parseJsonAttribute(data.wwNames);
        var tierNames = names.tiers || {};
        var packNames = names.addOns || {};
        var packOrder = parseIdList(data.wwPackOrder);
        var packMax = parseMax(data.wwPackMax);
        var publishableKey = data.wwPublishableKey || '';
        var paymentCid = data.wwPaymentCid || '';
        var completeUrl = data.wwCompleteUrl || '';
        var alreadySignedInUrl = data.wwAlreadySignedInUrl || '';
        var catalogOk = data.wwCatalogOk !== 'false';

        var renderedPlan = { tierId: data.wwTier || '', pricingId: data.wwPricing || '' };

        var state = machines.initialSignupState({
            tokenMode: data.wwTokenMode || 'auto',
            planSelection: data.wwPlanSelection || 'choose',
            packSelection: data.wwPackSelection || 'none',
            // Razor is a WEB stack: the plan's card is taken before the account exists.
            paymentOrder: 'beforeAccount',
            selection: {
                tierId: data.wwTier || undefined,
                pricingId: data.wwPricing || undefined,
                addOnIds: parseIdList(data.wwAddons)
            }
        });

        // The attempt record. It survives a failure so Try Again RESUMES instead of registering
        // the same person again or charging a second card.
        var attempt = {
            registered: false,
            loggedIn: false,
            subscriptionFailed: false,
            userId: '',
            requiresDisclaimers: false
        };

        var form = null;                 // the register form's values, kept across retries
        var tokenGrant = null;           // the token's grant, with its display names
        var paymentIds = { transactionId: null, externalId: null };
        var selectedPacks = parseIdList(data.wwAddons);
        var runs = {};                   // work-key -> the step token it was claimed with
        var packState = machines.initialPackCheckoutState({ appId: appId });
        var packQuote = null;
        var stripe = null;
        var stripeCard = null;
        var detached = false;
        var settling = false;            // a money-moving chain is in flight
        var entitlementsNotified = false;
        var signedInNotified = false;
        var pumping = false;
        var lastStepName = '';

        // The SIGNUP step token the pack checkout belongs to, held across the pause while the
        // visitor fills in the card form.
        var packCheckoutStepToken = null;

        // ----- Elements -------------------------------------------------------------------------

        function q(selector) { return root.querySelector(selector); }
        function qa(selector) { return root.querySelectorAll(selector); }

        function stepEl(name) { return root.querySelector('[data-ww-step="' + name + '"]'); }

        function setText(el, text) {
            if (el) el.textContent = text || '';
        }

        function show(el, visible) {
            if (el) el.hidden = !visible;
        }

        // ----- Events ---------------------------------------------------------------------------

        function raise(name, detail, cancelable) {
            var event;
            try {
                event = new CustomEvent(name, {
                    bubbles: true,
                    cancelable: cancelable === true,
                    detail: detail
                });
            } catch (e) {
                // Very old browsers: no constructor. Nothing can be cancelled, so nothing follows.
                return false;
            }
            return root.dispatchEvent(event);
        }

        function report(code, message) {
            raise(EVT_ERROR, { code: code, message: message });
        }

        // ----- The proxy -------------------------------------------------------------------------

        function readJson(response) {
            if (response.status === 401) {
                var expired = new Error('Your session has expired. Please sign in again.');
                expired.status = 401;
                throw expired;
            }
            if (!response.ok) {
                var failed = new Error('Request failed (HTTP ' + response.status + ')');
                failed.status = response.status;
                throw failed;
            }
            return response.json().catch(function () { return {}; });
        }

        function proxyGet(path) {
            return fetch(proxy + path, { credentials: 'same-origin' }).then(readJson);
        }

        function proxyPost(path, body) {
            return fetch(proxy + path, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(body || {})
            }).then(readJson);
        }

        function appQuery(path) {
            return path + (path.indexOf('?') === -1 ? '?' : '&') + 'appId=' + encodeURIComponent(appId);
        }

        // ----- Rendering --------------------------------------------------------------------------

        function paintTokenPlan() {
            var panel = q('[data-ww-token-plan]');
            if (!panel) return;

            if (!tokenGrant) {
                show(panel, false);
                return;
            }

            setText(q('[data-ww-token-plan-tier]'), tokenGrant.appTierName || tierNames[tokenGrant.appTierId] || '');

            // Names only, never prices: a grant is not a quote.
            paintNameList('[data-ww-token-plan-packs]', '[data-ww-token-plan-packs-group]',
                tokenGrant.addOnNames && tokenGrant.addOnNames.length > 0
                    ? tokenGrant.addOnNames
                    : namesFor(tokenGrant.addOnIds, packNames));

            paintNameList('[data-ww-token-plan-features]', '[data-ww-token-plan-features-group]',
                tokenGrant.featureNames && tokenGrant.featureNames.length > 0
                    ? tokenGrant.featureNames
                    : tokenGrant.featureCodes || []);

            show(panel, true);
        }

        function namesFor(ids, lookup) {
            var out = [];
            if (!ids) return out;
            for (var i = 0; i < ids.length; i++) out.push(lookup[ids[i]] || ids[i]);
            return out;
        }

        function paintNameList(listSelector, groupSelector, values) {
            var list = q(listSelector);
            var group = q(groupSelector);
            if (!list) return;

            while (list.firstChild) list.removeChild(list.firstChild);

            for (var i = 0; i < values.length; i++) {
                var item = document.createElement('li');
                item.textContent = values[i];
                list.appendChild(item);
            }

            show(group, values.length > 0);
        }

        function paintPacks() {
            var cards = qa('[data-ww-step="packs"] [data-ww-pack]');
            for (var i = 0; i < cards.length; i++) {
                var card = cards[i];
                var isSelected = selectedPacks.indexOf(card.getAttribute('data-ww-pack')) !== -1;
                card.classList.toggle('ww-pack-card--selected', isSelected);
                if (card.hasAttribute('aria-pressed')) {
                    card.setAttribute('aria-pressed', isSelected ? 'true' : 'false');
                }
            }

            var continueBtn = q('[data-ww-action="continue-packs"]');
            if (continueBtn) {
                continueBtn.textContent = selectedPacks.length === 1
                    ? labels.continueWithOnePack
                    : formatLabel(labels.continueWithPacks, 'count', selectedPacks.length);
                continueBtn.disabled = selectedPacks.length === 0;
            }
        }

        function paintSuccess() {
            var outcome = state.outcome;
            if (!outcome) return;

            var key = successTemplateKey(
                Boolean(outcome.tokenGrant),
                Boolean(outcome.tier),
                attempt.subscriptionFailed,
                parseInt(root.dataset.wwTrialDays || '0', 10) || 0);

            var message = labels[key] || '';
            if (key === 'successToken') {
                var tierName = (tokenGrant && tokenGrant.appTierName)
                    || (outcome.tier && outcome.tier.name)
                    || labels.planWord;
                message = formatLabel(message, 'tier', tierName);
            }

            setText(q('[data-ww-success-message]'), message);

            var list = q('[data-ww-pack-outcomes]');
            if (!list) return;

            while (list.firstChild) list.removeChild(list.firstChild);

            for (var i = 0; i < outcome.packs.length; i++) {
                var pack = outcome.packs[i];
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

            show(list, outcome.packs.length > 0);
        }

        function render() {
            if (detached) return;

            var name = machines.signupStepName(state.step);
            var panels = qa('[data-ww-step]');
            for (var i = 0; i < panels.length; i++) {
                panels[i].hidden = panels[i].getAttribute('data-ww-step') !== name;
            }

            // The register form's submit wording follows whether a plan step is still ahead.
            var submit = q('[data-ww-action="submit-register"]');
            if (submit) {
                var planAhead = state.options.planSelection === 'choose'
                    && state.options.tokenMode !== 'required'
                    && !state.planPreset;
                submit.textContent = planAhead ? labels.submitContinue : labels.submitCreate;
            }

            var tokenError = q('[data-ww-token-error]');
            setText(tokenError, state.tokenError || '');
            show(tokenError, Boolean(state.tokenError));

            // A grant supersedes whatever plan the link carried.
            show(q('[data-ww-plan-summary]'), !tokenGrant);

            setText(q('[data-ww-error-message]'), state.error || '');

            if (state.step === 'done') paintSuccess();

            if (name !== lastStepName) {
                lastStepName = name;
                raise(EVT_STEP, { step: name });
            }
        }

        // ----- The machine ------------------------------------------------------------------------

        function apply(event) {
            var next = machines.signupTransition(state, event);
            if (next === state) return false;
            state = next;
            return true;
        }

        function dispatch(event) {
            if (detached) return;
            apply(event);
            if (pumping) return;
            pump();
        }

        /** One run per step token: a doubled effect carries a token already claimed. */
        function claim(key, token) {
            if (!token) return false;
            if (runs[key] === token) return false;
            runs[key] = token;
            return true;
        }

        function pump() {
            pumping = true;
            try {
                for (var i = 0; i < MAX_PUMP_ITERATIONS; i++) {
                    if (detached) break;
                    render();
                    if (!runStepWork()) break;
                }
            } finally {
                pumping = false;
                render();
            }
        }

        /** One step's work. Answers whether the machine moved, so the pump knows to look again. */
        function runStepWork() {
            switch (state.step) {
                case 'token':
                    return runTokenStep();

                case 'payment':
                    // A payment step with nothing to charge for cannot be paid, and a card taken
                    // there would be stranded. Back to the form.
                    if (state.formSubmitted && state.selection.tierId) {
                        preparePaymentStep();
                        return false;
                    }
                    return apply({ type: 'GO_TO', step: 'register' });

                case 'creating':
                    if (!form) return apply({ type: 'GO_TO', step: 'register' });
                    if (!claim('creating', state.token)) return false;
                    runSignup(state.token);
                    return false;

                case 'packCheckout':
                    if (!claim('packCheckout', state.token)) return false;
                    runPackCheckout(state.token);
                    return false;

                case 'done':
                    return notifyEntitlements();

                default:
                    return false;
            }
        }

        function runTokenStep() {
            // A rejected token is a form error: back to the form, with the server's words above it.
            if (state.tokenError) return apply({ type: 'GO_TO', step: 'register' });

            if (!state.tokenChecking) {
                var carries = form && form.token;
                return apply(carries ? { type: 'TOKEN_CHECK_STARTED' } : { type: 'TOKEN_SKIPPED' });
            }

            if (!claim('token', state.token)) return false;
            checkToken(state.token);
            return false;
        }

        /**
         * Reads what the token grants. A NULL answer (a 502 from the proxy, an older server) means
         * the details could not be READ, not that the token is invalid: the signup carries on as
         * an ordinary one, and the server has the last word at registration.
         */
        function checkToken(stepToken) {
            var value = (form && form.token) || '';

            proxyPost('/token-details', { Token: value, AppId: appId })
                .then(function (details) { return details; })
                .catch(function () { return null; })
                .then(function (details) {
                    if (detached) return;

                    if (details && details.isValid === false) {
                        var message = details.errorMessage || labels.tokenRejected;
                        report(CODE_TOKEN_REJECTED, message);
                        dispatch({ type: 'TOKEN_REJECTED', token: stepToken, message: message });
                        return;
                    }

                    tokenGrant = findGrantForApp(details, appId);
                    if (tokenGrant) {
                        selectedPacks = withoutGranted(selectedPacks, tokenGrant.addOnIds || []);
                    }
                    paintTokenPlan();

                    dispatch({
                        type: 'TOKEN_ACCEPTED',
                        token: stepToken,
                        value: value,
                        grant: toMachineGrant(tokenGrant)
                    });
                });
        }

        // ----- The payment step --------------------------------------------------------------------

        /**
         * Hands the payment component the buyer's email before it takes a card. The plan itself was
         * priced on the server, so nothing here touches an amount.
         */
        function preparePaymentStep() {
            if (!paymentCid || !window.wwPayment) return;

            var paymentRoot = document.getElementById('ww-payment-' + paymentCid);
            if (!paymentRoot) return;

            paymentRoot.setAttribute('data-customer-email', (form && form.email) || '');
            window.wwPayment.update(paymentCid);
        }

        // ----- Creating the account ------------------------------------------------------------------

        function setProcessingStatus(text) {
            setText(q('[data-ww-processing-status]'), text);
        }

        /**
         * Register, sign in, link the plan's payment and start the subscription. Each sub-step is
         * skipped when the attempt record says it already happened, so Try Again resumes.
         */
        function runSignup(stepToken) {
            settling = true;

            registerStep()
                .then(function () { return loginStep(); })
                .then(function () { return linkPaymentStep(); })
                .then(function () { return subscribeStep(); })
                .then(function () {
                    settling = false;
                    if (detached) return;
                    dispatch({
                        type: 'ACCOUNT_CREATED',
                        token: stepToken,
                        userId: attempt.userId,
                        requiresDisclaimers: attempt.requiresDisclaimers && hasRenderedDisclaimers()
                    });
                })
                .catch(function (error) {
                    settling = false;
                    if (detached) return;
                    var message = (error && error.message) || labels.signupFailed;
                    report((error && error.code) || CODE_SIGNUP_FAILED, message);
                    dispatch({ type: 'ACCOUNT_FAILED', token: stepToken, message: message });
                });
        }

        function refusal(message, code) {
            var error = new Error(message);
            error.code = code;
            return error;
        }

        function registerStep() {
            if (attempt.registered) return Promise.resolve();

            setProcessingStatus(labels.statusCreatingAccount);

            var body = {
                FirstName: form.firstName,
                LastName: form.lastName,
                Username: form.username,
                Email: form.email,
                Password: form.password,
                Token: form.token || null,
                Attribution: readAttribution()
            };

            return proxyPost(appQuery('/register'), body).then(function (result) {
                if (!result || !result.success) {
                    throw refusal(refusalMessage(result, labels.signupFailed), 'registration_refused');
                }
                attempt.registered = true;
                clearAttribution();
            });
        }

        function loginStep() {
            if (attempt.loggedIn) return Promise.resolve();

            setProcessingStatus(labels.statusSigningIn);

            return proxyPost(appQuery('/login'), {
                Username: form.username,
                Email: form.email,
                Password: form.password
            }).then(function (result) {
                if (!result || !result.success) {
                    throw refusal(refusalMessage(result, labels.signupFailed), 'login_failed');
                }
                attempt.loggedIn = true;
                attempt.userId = result.userId || '';
                attempt.requiresDisclaimers = result.requiresDisclaimerAcceptance === true;

                // The password has done its work; it does not stay in memory afterwards.
                form.password = '';
            });
        }

        /**
         * Attaches the plan's payment to the account that now exists. NON-FATAL: the money is
         * taken and the account exists, so a failed link is something to repair.
         */
        function linkPaymentStep() {
            var linkId = paymentIds.externalId || paymentIds.transactionId;
            if (!linkId || !attempt.userId) return Promise.resolve();

            return proxyPost('/link-transaction', {
                ExternalTransactionId: linkId,
                UserId: attempt.userId
            }).catch(function () { /* repairable; never fatal to a signup */ });
        }

        /**
         * Starts the plan. Skipped entirely when a registration token already granted one -
         * subscribing over it REPLACES the subscription the token just created. A refusal is never
         * fatal: the success copy says the plan's activation is pending.
         */
        function subscribeStep() {
            if (tokenGrant || !state.selection.tierId) return Promise.resolve();

            setProcessingStatus(labels.statusActivatingPlan);

            return proxyPost(appQuery('/subscribe'), {
                TierId: state.selection.tierId,
                PricingId: state.selection.pricingId || null,
                PaymentTransactionId: paymentIds.transactionId || null
            }).then(function (result) {
                if (!result || result.success !== true) attempt.subscriptionFailed = true;
            }).catch(function () {
                attempt.subscriptionFailed = true;
            });
        }

        /**
         * The campaign touches the attribution engine captured, read from the SAME global
         * token-registration.js and signup-subscription.js read, so a signup through this view is
         * attributed exactly like one through the old ones. Absent engine, absent payload.
         */
        function readAttribution() {
            if (window.wildwoodAttribution
                && typeof window.wildwoodAttribution.getForRegistration === 'function') {
                try { return window.wildwoodAttribution.getForRegistration(); } catch (e) { return null; }
            }
            return null;
        }

        /** Recorded with the account: a later signup from this browser must not reuse the touches. */
        function clearAttribution() {
            if (window.wildwoodAttribution && typeof window.wildwoodAttribution.clear === 'function') {
                try { window.wildwoodAttribution.clear(); } catch (e) { /* recorded already */ }
            }
        }

        // ----- Disclaimers ------------------------------------------------------------------------

        function hasRenderedDisclaimers() {
            return qa('[data-ww-disclaimer]').length > 0;
        }

        /**
         * Shows only the disclaimers the signed-in account is actually still asked for. The markup
         * was rendered by the server (a disclaimer whose content format is HTML has to be rendered
         * AS HTML, which no script here may do), so this only decides which blocks are visible.
         */
        function prepareDisclaimers() {
            var blocks = qa('[data-ww-disclaimer]');
            if (blocks.length === 0) {
                dispatch({ type: 'DISCLAIMERS_ACCEPTED' });
                return;
            }

            proxyGet(appQuery('/disclaimers/pending'))
                .catch(function () { return null; })
                .then(function (pending) {
                    if (detached) return;

                    // Unreadable means "carry on": a signup that has already created an account
                    // and taken a card must not dead-end on a list that did not load.
                    var wanted = null;
                    if (pending && pending.disclaimers) {
                        wanted = [];
                        for (var i = 0; i < pending.disclaimers.length; i++) {
                            wanted.push(pending.disclaimers[i].disclaimerId);
                        }
                    }

                    var visible = 0;
                    for (var b = 0; b < blocks.length; b++) {
                        var id = blocks[b].getAttribute('data-ww-disclaimer');
                        var keep = wanted === null || wanted.indexOf(id) !== -1;
                        blocks[b].hidden = !keep;
                        if (keep) visible++;
                    }

                    if (visible === 0) {
                        dispatch({ type: 'DISCLAIMERS_ACCEPTED' });
                        return;
                    }

                    updateDisclaimerButton();
                });
        }

        function updateDisclaimerButton() {
            var button = q('[data-ww-action="accept-disclaimers"]');
            if (!button) return;

            var blocks = qa('[data-ww-disclaimer]');
            var ready = true;
            for (var i = 0; i < blocks.length; i++) {
                if (blocks[i].hidden) continue;
                var box = blocks[i].querySelector('[data-ww-disclaimer-check]');
                if (box && box.required && !box.checked) { ready = false; break; }
            }
            button.disabled = !ready;
        }

        function acceptDisclaimers() {
            var acceptances = [];
            var blocks = qa('[data-ww-disclaimer]');
            for (var i = 0; i < blocks.length; i++) {
                if (blocks[i].hidden) continue;
                var box = blocks[i].querySelector('[data-ww-disclaimer-check]');
                if (!box || !box.checked) continue;
                acceptances.push({
                    CompanyDisclaimerId: blocks[i].getAttribute('data-ww-disclaimer'),
                    CompanyDisclaimerVersionId: blocks[i].getAttribute('data-ww-disclaimer-version')
                });
            }

            var error = q('[data-ww-disclaimer-error]');
            show(error, false);

            proxyPost(appQuery('/disclaimers/accept'), { Acceptances: acceptances })
                .then(function (result) {
                    if (detached) return;
                    if (!result || result.success !== true) {
                        setText(error, refusalMessage(result, labels.signupFailed));
                        show(error, true);
                        return;
                    }
                    dispatch({ type: 'DISCLAIMERS_ACCEPTED' });
                })
                .catch(function (e) {
                    if (detached) return;
                    setText(error, (e && e.message) || labels.signupFailed);
                    show(error, true);
                });
        }

        // ----- Pack checkout ---------------------------------------------------------------------

        function packDispatch(event) {
            var next = machines.packCheckoutTransition(packState, event);
            if (next === packState) return false;
            packState = next;
            paintPackCheckout();
            return true;
        }

        function paintPackCheckout() {
            var status = q('[data-ww-pack-status]');
            var step = packState.step;

            if (step === 'authenticating' || step === 'completing') {
                var item = machines.currentPackCheckoutItem(packState);
                var name = item ? (packNames[item.addOnId] || item.addOnId) : '';
                setText(status, formatLabel(labels.authenticatingPack, 'name', name));
            } else if (step === 'quoting' || step === 'checkingOut') {
                setText(status, labels.buyingPacks);
            } else {
                setText(status, '');
            }

            show(q('[data-ww-pack-card-form]'), step === 'collectingCard');

            var error = q('[data-ww-pack-error]');
            setText(error, packState.error || '');
            show(error, Boolean(packState.error));
            show(q('[data-ww-pack-actions]'), step === 'failed');

            paintQuote();
        }

        function paintQuote() {
            var panel = q('[data-ww-pack-summary]');
            if (!panel) return;

            if (!packQuote || !packQuote.success) {
                show(panel, false);
                return;
            }

            var lines = q('[data-ww-pack-summary-lines]');
            if (lines) {
                while (lines.firstChild) lines.removeChild(lines.firstChild);
                for (var i = 0; i < (packQuote.lines || []).length; i++) {
                    var line = packQuote.lines[i];
                    var row = document.createElement('div');
                    row.className = 'ww-payment-summary-row';
                    row.setAttribute('data-ww-pack', line.addOnId);

                    var nameEl = document.createElement('span');
                    nameEl.textContent = packNames[line.addOnId] || line.name || line.addOnId;
                    row.appendChild(nameEl);

                    // The server quoted these amounts as numbers; the formatted total below is the
                    // only money this panel shows, and it comes straight off the quote.
                    lines.appendChild(row);
                }
            }

            var saved = q('[data-ww-pack-saved-card]');
            if (saved) {
                if (packQuote.savedCard && packQuote.savedCard.last4) {
                    var text = formatLabel(labels.savedCardOnFile, 'brand', packQuote.savedCard.brand || '');
                    setText(saved, formatLabel(text, 'last4', packQuote.savedCard.last4));
                    show(saved, true);
                } else {
                    show(saved, false);
                }
            }

            show(panel, true);
        }

        /**
         * Buys the basket AS THE SIGNED-IN USER: one quote, one card when there is none on file,
         * one checkout call, and then each pack the bank wants authenticated walked in order.
         */
        function runPackCheckout(stepToken) {
            var items = checkoutItems(state.packsToBuy);
            packState = machines.initialPackCheckoutState({ appId: appId, items: items });
            packQuote = null;

            packDispatch({ type: 'QUOTE_REQUESTED', appId: appId, items: items });
            quote(stepToken, items);
        }

        function quote(stepToken, items) {
            var token = packState.token;
            settling = true;

            proxyPost(appQuery('/checkout/quote'), {
                Items: items.map(function (item) { return { AddOnId: item.addOnId }; })
            })
                .then(function (result) {
                    packQuote = result;
                    if (!result || !result.success) {
                        report(CODE_PACK_QUOTE, refusalMessage(result, labels.packsUnavailable));
                    }
                    packDispatch({ type: 'QUOTE_RECEIVED', token: token, quote: result });
                    afterQuote(stepToken);
                })
                .catch(function (error) {
                    report(CODE_PACK_QUOTE, (error && error.message) || labels.packsUnavailable);
                    packDispatch({
                        type: 'QUOTE_FAILED',
                        token: token,
                        message: (error && error.message) || labels.packsUnavailable
                    });
                    settling = false;
                });
        }

        function afterQuote(stepToken) {
            if (packState.step !== 'quoted') { settling = false; return; }

            if (packQuote.requiresPaymentMethod) {
                packDispatch({ type: 'CARD_REQUESTED' });
                collectCard(stepToken);
                return;
            }

            packDispatch({ type: 'CHECKOUT_REQUESTED' });
            checkout(stepToken);
        }

        /** The SetupIntent: one card, for a basket of any size. */
        function collectCard(stepToken) {
            var token = packState.token;

            if (!publishableKey) {
                packDispatch({ type: 'CARD_INTENT_FAILED', token: token, message: CARD_UNAVAILABLE_MESSAGE });
                report(CODE_PACK_CARD, CARD_UNAVAILABLE_MESSAGE);
                settling = false;
                return;
            }

            Promise.all([
                proxyPost(appQuery('/checkout/payment-method'), {
                    ProviderId: packQuote.providerId || root.dataset.wwPaymentProvider || ''
                }),
                mountCard()
            ])
                .then(function (answers) {
                    var intent = answers[0];
                    if (!intent || !intent.success || !intent.clientSecret) {
                        throw new Error(refusalMessage(intent, CARD_UNAVAILABLE_MESSAGE));
                    }
                    packDispatch({
                        type: 'CARD_INTENT_RECEIVED',
                        token: token,
                        clientSecret: intent.clientSecret,
                        paymentTransactionId: intent.paymentTransactionId
                    });
                    // Waits for the visitor now: the Save card button confirms the intent.
                    packCheckoutStepToken = stepToken;
                    settling = false;
                })
                .catch(function (error) {
                    var message = (error && error.message) || CARD_UNAVAILABLE_MESSAGE;
                    packDispatch({ type: 'CARD_INTENT_FAILED', token: token, message: message });
                    report(CODE_PACK_CARD, message);
                    settling = false;
                });
        }

        function mountCard() {
            if (stripeCard) return Promise.resolve();

            return loadScript(STRIPE_SRC).then(function () {
                if (typeof window.Stripe === 'undefined') throw new Error(CARD_UNAVAILABLE_MESSAGE);
                var host = document.getElementById('ww-regsub-card-' + cid);
                if (!host) throw new Error(CARD_UNAVAILABLE_MESSAGE);

                stripe = window.Stripe(publishableKey);
                stripeCard = stripe.elements().create('card');
                stripeCard.mount(host);
            });
        }

        function confirmCard() {
            var token = packState.token;
            if (packState.step !== 'collectingCard' || !packState.cardClientSecret || !stripe) return;

            var error = q('[data-ww-pack-card-error]');
            show(error, false);
            settling = true;

            stripe.confirmCardSetup(packState.cardClientSecret, { payment_method: { card: stripeCard } })
                .then(function (answer) {
                    if (answer.error || !answer.setupIntent || answer.setupIntent.status !== 'succeeded') {
                        var message = (answer.error && answer.error.message) || CARD_UNAVAILABLE_MESSAGE;
                        setText(error, message);
                        show(error, true);
                        settling = false;
                        return;
                    }
                    packDispatch({ type: 'CARD_CONFIRMED', token: token });
                    checkout(packCheckoutStepToken);
                })
                .catch(function (e) {
                    setText(error, (e && e.message) || CARD_UNAVAILABLE_MESSAGE);
                    show(error, true);
                    settling = false;
                });
        }

        function checkout(stepToken) {
            var token = packState.token;
            settling = true;

            proxyPost(appQuery('/checkout'), {
                CheckoutId: packQuote.checkoutId,
                ProviderId: packQuote.providerId || '',
                PaymentTransactionId: packState.paymentTransactionId || null,
                UseSavedCard: packState.useSavedCard,
                Items: packState.items.map(function (item) { return { AddOnId: item.addOnId }; })
            })
                .then(function (result) {
                    if (!result || !result.results || result.results.length === 0) {
                        report(CODE_PACK_CHECKOUT, refusalMessage(result, labels.packsUnavailable));
                    }
                    packDispatch({ type: 'CHECKOUT_RECEIVED', token: token, result: result });
                    walkPending(stepToken);
                })
                .catch(function (error) {
                    var message = (error && error.message) || labels.packsUnavailable;
                    report(CODE_PACK_CHECKOUT, message);
                    packDispatch({ type: 'CHECKOUT_FAILED', token: token, message: message });
                    settling = false;
                });
        }

        /**
         * Each pack the bank wants authenticated, strictly one at a time and in order. One pack
         * failing never stops the others - a basket is a basket, not a transaction.
         *
         * The authenticate and the complete are ONE chain on purpose: a teardown between them
         * would leave a charge the customer authenticated with nothing recorded against it.
         */
        function walkPending(stepToken) {
            if (packState.step === 'done') { settling = false; finishPacks(stepToken); return; }
            if (packState.step !== 'authenticating') { settling = false; return; }

            var token = packState.token;
            var item = machines.currentPackCheckoutItem(packState);

            if (!item || !item.clientSecret || !stripe) {
                // Nothing to confirm with: this pack is lost, the rest of the basket is not.
                packDispatch({
                    type: 'ITEM_AUTH_FAILED',
                    token: token,
                    message: item && item.errorMessage ? item.errorMessage : CARD_UNAVAILABLE_MESSAGE
                });
                walkPending(stepToken);
                return;
            }

            settling = true;

            stripe.confirmCardPayment(item.clientSecret)
                .then(function (answer) {
                    if (answer.error) {
                        packDispatch({
                            type: 'ITEM_AUTH_FAILED',
                            token: token,
                            message: answer.error.message || CARD_UNAVAILABLE_MESSAGE
                        });
                        walkPending(stepToken);
                        return null;
                    }

                    packDispatch({ type: 'ITEM_AUTHENTICATED', token: token });

                    // The money has moved. Completing it on the server is owed even if the view
                    // has gone in the meantime, so it is awaited here rather than pumped.
                    var completingToken = packState.token;
                    return proxyPost(appQuery('/checkout/complete'), {
                        PaymentTransactionId: item.paymentTransactionId
                    }).then(function (completed) {
                        var merged = completed || {};
                        if (!merged.addOnId) merged.addOnId = item.addOnId;
                        packDispatch({ type: 'ITEM_COMPLETED', token: completingToken, result: merged });
                        walkPending(stepToken);
                    }).catch(function (error) {
                        packDispatch({
                            type: 'ITEM_COMPLETED',
                            token: completingToken,
                            result: {
                                addOnId: item.addOnId,
                                status: 'failed',
                                errorMessage: (error && error.message) || labels.packsUnavailable
                            }
                        });
                        walkPending(stepToken);
                    });
                })
                .catch(function (error) {
                    packDispatch({
                        type: 'ITEM_AUTH_FAILED',
                        token: token,
                        message: (error && error.message) || CARD_UNAVAILABLE_MESSAGE
                    });
                    walkPending(stepToken);
                });
        }

        function finishPacks(stepToken) {
            var outcomes = toOutcomes(
                packState.items, packState.results, packQuote, packNames, labels.packsUnavailable);

            dispatch({ type: 'PACK_CHECKOUT_FINISHED', token: stepToken, packs: outcomes });
        }

        /** "Skip for now": every requested pack is reported as failed and the signup still finishes. */
        function skipPacks(stepToken) {
            var outcomes = [];
            for (var i = 0; i < packState.items.length; i++) {
                var addOnId = packState.items[i].addOnId;
                outcomes.push({
                    addOnId: addOnId,
                    name: packNames[addOnId] || addOnId,
                    status: 'failed',
                    errorMessage: packState.error || labels.packsUnavailable
                });
            }
            dispatch({ type: 'PACK_CHECKOUT_FINISHED', token: stepToken, packs: outcomes });
        }

        // ----- Finishing --------------------------------------------------------------------------

        function notifyEntitlements() {
            if (entitlementsNotified) return false;
            entitlementsNotified = true;
            raise(EVT_ENTITLEMENTS, { appId: appId, reason: 'signup' });
            return false;
        }

        function completeSignup() {
            var proceed = raise(EVT_COMPLETE, state.outcome, true);
            if (proceed && completeUrl) window.location.assign(completeUrl);
        }

        // ----- The register form ----------------------------------------------------------------

        function readForm() {
            var values = {};
            var fields = qa('[data-ww-field]');
            for (var i = 0; i < fields.length; i++) {
                values[fields[i].getAttribute('data-ww-field')] = fields[i].value || '';
            }
            return values;
        }

        function submitRegister() {
            var values = readForm();
            var error = q('[data-ww-form-error]');
            show(error, false);

            var required = qa('[data-ww-field][required]');
            for (var i = 0; i < required.length; i++) {
                if (!required[i].value) {
                    required[i].focus();
                    return;
                }
            }

            if (values.password !== values.confirmPassword) {
                // The browser's own validity message, so the wording is the visitor's locale and
                // nothing English lives here.
                var confirm = q('[data-ww-field="confirmPassword"]');
                if (confirm) {
                    confirm.setCustomValidity(' ');
                    confirm.reportValidity();
                    confirm.setCustomValidity('');
                }
                return;
            }

            form = {
                firstName: values.firstName || '',
                lastName: values.lastName || '',
                username: values.username || values.email || '',
                email: values.email || '',
                password: values.password || '',
                token: values.token || ''
            };

            // A new set of details is a new attempt: nothing from the previous one is resumed.
            attempt.registered = false;
            attempt.loggedIn = false;
            attempt.subscriptionFailed = false;
            attempt.userId = '';
            runs = {};

            dispatch({ type: 'REGISTER_SUBMITTED', email: form.email });
        }

        // ----- The plan step -----------------------------------------------------------------------

        /**
         * Choosing a plan. When it is the plan the server already priced, the flow simply carries
         * on. When it is a different PAYABLE plan the page navigates to itself with the choice in
         * the query, because the card form, the order summary and every amount around them were
         * formatted by the server for one plan - re-pricing them in the browser is exactly what
         * this package does not do. The navigation raises ww-regsub-select first, so a host can
         * take it over.
         */
        function chooseTier(card) {
            var billing = currentBilling();
            var pricingId = card.getAttribute(
                billing === 'annual' ? 'data-ww-pricing-annual' : 'data-ww-pricing-monthly') || '';
            var paid = card.getAttribute(billing === 'annual' ? 'data-ww-paid-annual' : 'data-ww-paid-monthly') === 'true';
            var selection = {
                tierId: card.getAttribute('data-ww-tier') || null,
                pricingId: pricingId || null,
                billing: billing,
                addOnIds: selectedPacks.slice()
            };

            if (!paid || isRenderedPlan(renderedPlan, selection)) {
                dispatch({
                    type: 'PLAN_CHOSEN',
                    tierId: selection.tierId || undefined,
                    pricingId: selection.pricingId || undefined,
                    requiresPayment: paid
                });
                return;
            }

            var proceed = raise('ww-regsub-select', selection, true);
            if (proceed) window.location.assign(buildPlanUrl(window.location, selection));
        }

        function currentBilling() {
            var toggle = q('[data-ww-action="toggle-billing"]');
            return toggle && toggle.classList.contains('ww-toggle-on') ? 'annual' : 'monthly';
        }

        function applyBilling(next) {
            var blocks = qa('[data-ww-step="plan"] [data-ww-billing]');
            for (var i = 0; i < blocks.length; i++) {
                blocks[i].hidden = blocks[i].getAttribute('data-ww-billing') !== next;
            }
            var captions = qa('[data-ww-billing-label]');
            for (var j = 0; j < captions.length; j++) {
                captions[j].classList.toggle(
                    'ww-billing-active', captions[j].getAttribute('data-ww-billing-label') === next);
            }
            var toggle = q('[data-ww-action="toggle-billing"]');
            if (toggle) {
                toggle.classList.toggle('ww-toggle-on', next === 'annual');
                toggle.setAttribute('aria-pressed', next === 'annual' ? 'true' : 'false');
            }
        }

        // ----- Wiring --------------------------------------------------------------------------------

        root.addEventListener('click', function (e) {
            var trigger = e.target.closest ? e.target.closest('[data-ww-action]') : null;
            if (!trigger || !root.contains(trigger)) return;

            switch (trigger.getAttribute('data-ww-action')) {
                case 'submit-register':
                    submitRegister();
                    break;

                case 'change-plan':
                case 'back-to-plan':
                    dispatch({ type: 'GO_TO', step: 'plan' });
                    break;

                case 'back-to-register':
                    dispatch({ type: 'GO_TO', step: 'register' });
                    break;

                case 'toggle-billing':
                    applyBilling(currentBilling() === 'annual' ? 'monthly' : 'annual');
                    break;

                case 'select-tier': {
                    var card = trigger.closest('[data-ww-tier]');
                    if (card) chooseTier(card);
                    break;
                }

                case 'toggle-pack': {
                    var pack = trigger.closest('[data-ww-pack]');
                    if (!pack) break;
                    selectedPacks = toggleSelection(
                        packOrder, selectedPacks, pack.getAttribute('data-ww-pack'), packMax);
                    paintPacks();
                    break;
                }

                case 'continue-packs':
                    dispatch({ type: 'PACKS_CHOSEN', addOnIds: selectedPacks.slice() });
                    break;

                case 'skip-packs':
                    dispatch({ type: 'PACKS_CHOSEN', addOnIds: [] });
                    break;

                case 'accept-disclaimers':
                    acceptDisclaimers();
                    break;

                case 'confirm-card':
                    confirmCard();
                    break;

                case 'retry-packs':
                    packDispatch({ type: 'RETRY' });
                    resumePackCheckout();
                    break;

                case 'skip-packs-checkout':
                    skipPacks(state.token);
                    break;

                case 'retry':
                    dispatch({ type: 'RETRY' });
                    break;

                case 'start-over':
                    startOver();
                    break;

                case 'complete':
                    completeSignup();
                    break;

                case 'cancel':
                    raise(EVT_CANCEL, {});
                    break;

                default:
                    break;
            }
        });

        root.addEventListener('change', function (e) {
            if (e.target && e.target.hasAttribute && e.target.hasAttribute('data-ww-disclaimer-check')) {
                updateDisclaimerButton();
            }
        });

        // The plan's card, taken before the account exists. PaymentComponent fires this ONCE.
        root.addEventListener('ww-payment-success', function (e) {
            var detail = e.detail || {};
            paymentIds.transactionId = detail.transactionId || detail.paymentIntentId || null;
            paymentIds.externalId = detail.paymentIntentId || null;

            if (!paymentIds.transactionId) return;
            dispatch({ type: 'PAYMENT_COMPLETED', paymentTransactionId: paymentIds.transactionId });
        });

        function resumePackCheckout() {
            if (packState.step === 'quoting') { quote(state.token, packState.items); return; }
            if (packState.step === 'collectingCard') { collectCard(state.token); return; }
            if (packState.step === 'checkingOut') { checkout(state.token); return; }
            if (packState.step === 'authenticating') { walkPending(state.token); }
        }

        /**
         * Start Over throws the attempt away. It refuses while a money-moving call is in flight:
         * the charge would carry on with nothing left to record it against.
         */
        function startOver() {
            if (settling) return;

            attempt.registered = false;
            attempt.loggedIn = false;
            attempt.subscriptionFailed = false;
            attempt.userId = '';
            attempt.requiresDisclaimers = false;
            form = null;
            tokenGrant = null;
            paymentIds = { transactionId: null, externalId: null };
            selectedPacks = parseIdList(data.wwAddons);
            packQuote = null;
            packState = machines.initialPackCheckoutState({ appId: appId });
            runs = {};

            paintTokenPlan();
            paintPacks();
            dispatch({ type: 'RESET' });

            // A reset goes back to `loading`, and the server's answers are still on the element.
            seed();
        }

        /** Feeds the machine what the server already answered. */
        function seed() {
            dispatch({
                type: 'MODE_LOADED',
                mode: {
                    closed: data.wwModeClosed === 'true',
                    requireToken: data.wwModeRequireToken === 'true',
                    allowOpenRegistration: data.wwModeAllowOpen === 'true',
                    showOptionalTokenEntry: data.wwModeOptionalToken === 'true'
                }
            });

            if (!catalogOk) {
                dispatch({ type: 'LOAD_FAILED', message: data.wwCatalogError || '' });
                report(CODE_CATALOG, data.wwCatalogError || '');
                return;
            }

            dispatch({
                type: 'SELECTION_RESOLVED',
                tierId: data.wwTier || undefined,
                pricingId: data.wwPricing || undefined,
                addOnIds: parseIdList(data.wwAddons),
                requiresPayment: data.wwPlanRequiresPayment === 'true'
            });

            dispatch({ type: 'CATALOG_LOADED', names: { tiers: tierNames, addOns: packNames } });
        }

        // ----- Start ---------------------------------------------------------------------------------

        dispatch({ type: 'INIT', signedIn: data.wwSignedIn === 'true' });

        if (state.alreadySignedInLatched) {
            if (!signedInNotified) {
                signedInNotified = true;
                var proceedSignedIn = raise(EVT_SIGNED_IN, { appId: appId }, true);
                if (proceedSignedIn && alreadySignedInUrl) window.location.assign(alreadySignedInUrl);
            }
        } else {
            seed();
            paintPacks();
        }

        instances[cid] = {
            root: root,

            /** The machine's state, for a host that wants to watch the flow. */
            getState: function () { return state; },

            /**
             * DETACHES this instance: no more rendering and no new step work. Nothing already
             * under way is cut short - a pack whose bank confirmation has been issued is still
             * completed on the server by the chain that started it.
             */
            destroy: function () {
                detached = true;
                if (stripeCard) {
                    try { stripeCard.destroy(); } catch (e) { /* already gone */ }
                    stripeCard = null;
                }
                delete instances[cid];
            }
        };

        // The disclaimers gate needs preparing the moment the machine lands on its step; the pump
        // reports every step change, so this is the one listener that watches it.
        root.addEventListener(EVT_STEP, function (e) {
            if (e.detail && e.detail.step === 'disclaimers') prepareDisclaimers();
        });

        return instances[cid];
    }

    // ===== AUTO-INIT =============================================================================

    function initAll() {
        var roots = document.querySelectorAll('[data-ww-view="signup"]');
        for (var i = 0; i < roots.length; i++) initInstance(roots[i]);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAll);
    } else {
        initAll();
    }

    window.wwRegSubSignup = {
        init: initInstance,
        initAll: initAll,
        getInstance: function (componentId) { return instances[componentId] || null; },

        // Exposed for hosts building their own links; the same rules the clicks use.
        buildPlanUrl: buildPlanUrl
    };
})();
