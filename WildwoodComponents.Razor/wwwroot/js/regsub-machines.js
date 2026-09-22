/*
 * WildwoodComponents.Razor - Registration & Subscription: the state machines
 *
 * Classic script, no modules, no build step, matching every other file in this folder. Load it
 * BEFORE everything that drives it - the two shared drivers and the three views:
 *
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-packcheckout.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-planchange.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-signup.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-manage.js"></script>
 *
 * This file is PURE. No DOM, no fetch, no timers, no English a visitor ever reads. It is the
 * line-by-line port of
 *   packages/wildwood-react-shared/src/registrationSubscription/signupMachine.ts
 *   packages/wildwood-react-shared/src/registrationSubscription/packCheckoutMachine.ts
 *   packages/wildwood-react-shared/src/registrationSubscription/planChangeMachine.ts
 *   packages/wildwood-react-shared/src/registrationSubscription/stepTokens.ts
 * and of the C# twins in WildwoodComponents.Shared/RegistrationSubscription. The transition tables
 * are identical on purpose: the same signup that runs in React runs here, so a rule fixed in one
 * stack is a rule fixed in all of them.
 *
 * Two invariants the rest of the signup leans on:
 *
 *   1. An IGNORED event returns the SAME object. Callers compare with === to know whether anything
 *      happened, exactly as React's useReducer skips a re-render.
 *   2. Every state that starts async work is issued a fresh step token on entry. The result event
 *      carries the token it was started with, and a result whose token is not the one the state is
 *      waiting on is dropped. That single rule covers a double-click, a payment SDK callback that
 *      fires twice, and a retry superseding the attempt it replaced.
 *
 * `paymentOrder` is kept for table parity with the other stacks but Razor is a WEB stack, so the
 * signup driver always passes 'beforeAccount': the plan's card is taken before the account exists,
 * because a declined card then leaves nothing behind.
 *
 * Tested by WildwoodComponents.Tests/Razor/js/regsub-machines.selftest.mjs, which this file
 * exports to through the guarded module hook at the bottom. There is no JS test harness in this
 * repo, so that self-test is what stands in for one.
 */
(function () {
    'use strict';

    // ===== Step tokens ===========================================================================

    var tokenCounter = 0;

    /** A fresh handle for one async step. Monotonic, so a token is never reused. */
    function issueStepToken() {
        tokenCounter += 1;
        return 'step-' + tokenCounter;
    }

    /**
     * Whether a result belongs to the step the state is waiting on. A null on either side means
     * "not waiting", which is never a match: a late result is stale by definition.
     */
    function isCurrentStep(stateToken, eventToken) {
        return Boolean(stateToken) && stateToken === eventToken;
    }

    // ===== Small pure helpers ====================================================================

    /** A shallow copy, standing in for TS's object spread. */
    function copy(state) {
        var next = {};
        for (var key in state) {
            if (Object.prototype.hasOwnProperty.call(state, key)) next[key] = state[key];
        }
        return next;
    }

    /** TS `[...new Set(ids ?? [])]`: unique, in the order they were given. */
    function dedupe(ids) {
        var unique = [];
        if (!ids) return unique;
        for (var i = 0; i < ids.length; i++) {
            if (unique.indexOf(ids[i]) === -1) unique.push(ids[i]);
        }
        return unique;
    }

    function without(ids, excluded) {
        if (!excluded || excluded.length === 0) return ids;
        var kept = [];
        for (var i = 0; i < ids.length; i++) {
            if (excluded.indexOf(ids[i]) === -1) kept.push(ids[i]);
        }
        return kept;
    }

    function grantedAddOnIds(grant) {
        return grant && grant.addOnIds ? grant.addOnIds.slice() : [];
    }

    // ===== Signup machine ========================================================================

    /** The steps the flow walks in order. Everything before `creating` may be skipped. */
    var FORM_ORDER = ['register', 'token', 'plan', 'packs', 'payment', 'creating'];

    /**
     * The `data-ww-step` value for a machine step. The machine's `done` is spelled `success` in the
     * DOM, exactly as React spells it, because that is the hook live end-to-end suites locate the
     * finished signup by.
     */
    function signupStepName(step) {
        return step === 'done' ? 'success' : step;
    }

    function initialSignupState(options) {
        var source = options || {};
        var selection = source.selection || {};
        var addOnIds = dedupe(selection.addOnIds);

        return {
            step: 'loading',
            token: null,
            options: {
                tokenMode: source.tokenMode || 'auto',
                planSelection: source.planSelection || 'choose',
                packSelection: source.packSelection || 'choose',
                paymentOrder: source.paymentOrder || 'beforeAccount'
            },
            mode: null,
            modeReady: false,
            catalogReady: false,
            names: {},
            selection: {
                tierId: selection.tierId,
                pricingId: selection.pricingId,
                addOnIds: addOnIds
            },
            planRequiresPayment: false,
            planPreset: false,
            formSubmitted: false,
            email: '',
            userId: '',
            paymentTransactionId: undefined,
            paymentAfterAccount: false,
            pendingDisclaimers: false,
            planActivationPending: false,
            tokenValue: undefined,
            tokenGrant: undefined,
            tokenChecking: false,
            tokenError: null,
            packsToBuy: addOnIds,
            alreadySignedInLatched: false,
            initialized: false,
            outcome: null,
            error: null,
            retryFrom: null
        };
    }

    /**
     * Whether the plan still has to be paid for - the one question both payment positions ask, so
     * the pay-first step and the account-first one can never disagree about whether there is a card
     * to take. A granted plan is paid for by whoever issued the token, and a free plan takes no card.
     */
    function signupPlanNeedsPayment(state) {
        if (!state) return false;
        return state.planRequiresPayment
            && state.tokenGrant == null
            && Boolean(state.selection && state.selection.tierId);
    }

    /** Whether a step is passed over for this flow. */
    function isSkipped(state, step) {
        if (step === 'token') {
            // No token path at all: neither required nor offered as an option.
            return !(state.mode && (state.mode.requireToken || state.mode.showOptionalTokenEntry));
        }

        if (step === 'plan') {
            // A grant already names the plan, a resolved link already chose it, and invite
            // redemption is "take what the invite gives" - in none of those is there anything to
            // choose.
            return state.options.planSelection === 'skip'
                || state.options.tokenMode === 'required'
                || state.planPreset
                || state.tokenGrant != null;
        }

        if (step === 'packs') {
            // Invite redemption is "take what the invite gives", not a shopping trip.
            return state.options.packSelection === 'none' || state.options.tokenMode === 'required';
        }

        if (step === 'payment') {
            // Account-first: the card is taken after `creating`, not inside the form's order, so
            // the form never walks a payment step at all.
            if (state.options.paymentOrder === 'afterAccount') return true;
            return !signupPlanNeedsPayment(state);
        }

        return false;
    }

    /** Move into a step, issuing a token when that step starts async work. */
    function signupEnter(state, step) {
        var startsWork = step === 'creating' || step === 'packCheckout';
        var next = copy(state);
        next.step = step;
        next.token = startsWork ? issueStepToken() : null;
        next.tokenChecking = false;
        next.error = null;
        next.retryFrom = null;
        return next;
    }

    function signupFail(state, message, retryFrom) {
        var next = copy(state);
        next.step = 'failed';
        next.token = null;
        next.tokenChecking = false;
        next.error = message;
        next.retryFrom = retryFrom;
        return next;
    }

    /** The next step after `from`, skipping whatever this flow does not need. */
    function advance(state, from) {
        // The form comes first, always. A visitor who followed "change plan" out of the form and
        // chose there is sent back to it with their new choice, rather than on towards a card form
        // or an account creation that has no details to work with.
        if (!state.formSubmitted) return signupEnter(state, 'register');

        var start = FORM_ORDER.indexOf(from);
        for (var i = start + 1; i < FORM_ORDER.length; i++) {
            if (!isSkipped(state, FORM_ORDER[i])) return signupEnter(state, FORM_ORDER[i]);
        }
        return signupEnter(state, 'creating');
    }

    function resolveTier(state) {
        var tierId = state.tokenGrant && state.tokenGrant.tierId
            ? state.tokenGrant.tierId
            : state.selection.tierId;
        if (!tierId) return null;

        var names = state.names && state.names.tiers ? state.names.tiers : {};
        var pricingId = state.tokenGrant && state.tokenGrant.pricingId
            ? state.tokenGrant.pricingId
            : state.selection.pricingId;

        return {
            tierId: tierId,
            name: names[tierId] || tierId,
            pricingId: pricingId
        };
    }

    /** Everything is done: build the outcome, granted packs first. */
    function finish(state, bought) {
        var names = state.names && state.names.addOns ? state.names.addOns : {};
        var packs = [];

        var granted = grantedAddOnIds(state.tokenGrant);
        for (var i = 0; i < granted.length; i++) {
            packs.push({ addOnId: granted[i], name: names[granted[i]] || granted[i], status: 'granted' });
        }

        if (bought) {
            for (var j = 0; j < bought.length; j++) packs.push(bought[j]);
        }

        var next = copy(state);
        next.step = 'done';
        next.token = null;
        next.error = null;
        next.retryFrom = null;
        next.outcome = {
            userId: state.userId,
            tier: resolveTier(state),
            packs: packs,
            tokenGrant: state.tokenGrant,
            // Left off entirely when it did not happen, so the ordinary outcome carries no dead flag.
            planActivationPending: state.planActivationPending ? true : undefined
        };
        return next;
    }

    /** After the account exists and the disclaimers are out of the way. */
    function afterDisclaimers(state) {
        if (state.packsToBuy.length > 0) return signupEnter(state, 'packCheckout');
        return finish(state, []);
    }

    /**
     * Leaving the ACCOUNT-FIRST payment step, whether the card was given or walked away from: on to
     * the disclaimers ACCOUNT_CREATED asked for, or straight past them - exactly where that event
     * would have gone had there been no card to take.
     */
    function afterAccountPayment(state) {
        var next = copy(state);
        next.paymentAfterAccount = false;
        if (next.pendingDisclaimers) return signupEnter(next, 'disclaimers');
        return afterDisclaimers(next);
    }

    /** Both the mode and the catalog are in: open the form, or say sign-up is closed. */
    function startForm(state) {
        if (!state.modeReady || !state.catalogReady) return state;

        if (state.mode && state.mode.closed) {
            var closed = copy(state);
            closed.step = 'closed';
            closed.token = null;
            return closed;
        }

        return signupEnter(state, 'register');
    }

    function isBackNavigable(step) {
        return step === 'register'
            || step === 'token'
            || step === 'plan'
            || step === 'packs'
            || step === 'payment';
    }

    /**
     * The signup reducer. Pure apart from issuing step tokens, and it returns the SAME state object
     * for an event it ignores.
     */
    function signupTransition(state, event) {
        var next;

        switch (event.type) {
            case 'INIT':
                // Latched once: a mid-flow login must not look like "you were already signed in".
                if (state.initialized) return state;
                next = copy(state);
                next.initialized = true;
                next.alreadySignedInLatched = Boolean(event.signedIn);
                return next;

            case 'MODE_LOADED':
                next = copy(state);
                next.mode = event.mode;
                next.modeReady = true;
                if (state.step !== 'loading') return next;
                return startForm(next);

            case 'CATALOG_LOADED':
                next = copy(state);
                next.names = event.names || state.names;
                next.catalogReady = true;
                if (state.step !== 'loading') return next;
                return startForm(next);

            case 'SELECTION_RESOLVED': {
                // Only while the form is still ahead: once it has been submitted the plan and pack
                // steps own the selection, and a catalog that reloads underneath must not rewrite
                // what was chosen.
                if (state.step !== 'loading' && state.step !== 'register') return state;

                var granted = grantedAddOnIds(state.tokenGrant);
                var addOnIds = event.addOnIds ? dedupe(event.addOnIds) : state.selection.addOnIds;
                var plannedTier = event.tierId != null;

                next = copy(state);
                next.planPreset = state.planPreset || plannedTier;
                next.planRequiresPayment = plannedTier
                    ? event.requiresPayment === true
                    : state.planRequiresPayment;
                next.selection = {
                    tierId: event.tierId != null ? event.tierId : state.selection.tierId,
                    pricingId: event.pricingId != null ? event.pricingId : state.selection.pricingId,
                    addOnIds: addOnIds
                };
                next.packsToBuy = without(addOnIds, granted);
                return next;
            }

            case 'LOAD_FAILED':
                if (state.step !== 'loading') return state;
                return signupFail(state, event.message, 'loading');

            case 'REGISTER_SUBMITTED':
                if (state.step !== 'register') return state;
                next = copy(state);
                next.email = event.email;
                next.formSubmitted = true;
                return advance(next, 'register');

            case 'TOKEN_CHECK_STARTED':
                if (state.step !== 'token') return state;
                next = copy(state);
                next.token = issueStepToken();
                next.tokenChecking = true;
                next.tokenError = null;
                return next;

            case 'TOKEN_ACCEPTED': {
                if (state.step !== 'token' || !isCurrentStep(state.token, event.token)) return state;

                // A grant covers its own packs, so they drop out of what still has to be bought.
                var grantedByToken = grantedAddOnIds(event.grant);

                next = copy(state);
                next.token = null;
                next.tokenChecking = false;
                next.tokenError = null;
                next.tokenValue = event.value;
                next.tokenGrant = event.grant;
                next.packsToBuy = without(state.packsToBuy, grantedByToken);
                return advance(next, 'token');
            }

            case 'TOKEN_REJECTED':
                if (state.step !== 'token' || !isCurrentStep(state.token, event.token)) return state;
                next = copy(state);
                next.token = null;
                next.tokenChecking = false;
                next.tokenError = event.message;
                return next;

            case 'TOKEN_SKIPPED':
                // A required token cannot be skipped; the server would refuse the registration anyway.
                if (state.step !== 'token' || (state.mode && state.mode.requireToken)) return state;
                next = copy(state);
                next.token = null;
                next.tokenChecking = false;
                next.tokenError = null;
                return advance(next, 'token');

            case 'PLAN_CHOSEN':
                if (state.step !== 'plan') return state;
                next = copy(state);
                next.selection = {
                    tierId: event.tierId,
                    pricingId: event.pricingId,
                    addOnIds: state.selection.addOnIds
                };
                next.planRequiresPayment = event.requiresPayment === true;
                // Chosen is chosen: coming back through the form must not ask for a plan again.
                next.planPreset = true;
                return advance(next, 'plan');

            case 'PACKS_CHOSEN': {
                if (state.step !== 'packs') return state;
                var chosen = dedupe(event.addOnIds);
                next = copy(state);
                next.selection = {
                    tierId: state.selection.tierId,
                    pricingId: state.selection.pricingId,
                    addOnIds: chosen
                };
                next.packsToBuy = without(chosen, grantedAddOnIds(state.tokenGrant));
                return advance(next, 'packs');
            }

            case 'PAYMENT_COMPLETED':
                if (state.step !== 'payment') return state;
                next = copy(state);
                next.paymentTransactionId = event.paymentTransactionId;
                // Account-first: the account is already made, so the card is the last thing before
                // the disclaimers. A card that goes through also un-pends the plan: this may be the
                // second visit to the step, after a first one the customer walked away from.
                if (state.paymentAfterAccount) {
                    next.planActivationPending = false;
                    return afterAccountPayment(next);
                }
                return advance(next, 'payment');

            case 'PAYMENT_ABANDONED':
                // Pay-first has nothing to abandon INTO: no account exists yet, so backing out of
                // the card is a GO_TO, not this.
                if (state.step !== 'payment' || !state.paymentAfterAccount) return state;
                next = copy(state);
                next.planActivationPending = true;
                return afterAccountPayment(next);

            case 'ACCOUNT_CREATED': {
                if (state.step !== 'creating' || !isCurrentStep(state.token, event.token)) return state;

                // Default true: the plan's order puts disclaimers after account creation, and a
                // host that knows there are none passes false rather than rendering an empty step.
                var requiresDisclaimers = event.requiresDisclaimers !== false;

                next = copy(state);
                next.token = null;
                next.userId = event.userId;
                // Remembered, because an account-first card step runs between here and there.
                next.pendingDisclaimers = requiresDisclaimers;

                if (state.options.paymentOrder === 'afterAccount'
                    && !state.paymentTransactionId
                    && signupPlanNeedsPayment(state)) {
                    next.paymentAfterAccount = true;
                    return signupEnter(next, 'payment');
                }

                if (requiresDisclaimers) return signupEnter(next, 'disclaimers');
                return afterDisclaimers(next);
            }

            case 'ACCOUNT_FAILED':
                if (state.step !== 'creating' || !isCurrentStep(state.token, event.token)) return state;
                return signupFail(state, event.message, 'creating');

            case 'DISCLAIMERS_ACCEPTED':
                if (state.step !== 'disclaimers') return state;
                return afterDisclaimers(state);

            case 'PACK_CHECKOUT_FINISHED':
                if (state.step !== 'packCheckout' || !isCurrentStep(state.token, event.token)) return state;
                next = copy(state);
                next.token = null;
                return finish(next, event.packs);

            case 'PACK_CHECKOUT_FAILED':
                if (state.step !== 'packCheckout' || !isCurrentStep(state.token, event.token)) return state;
                return signupFail(state, event.message, 'packCheckout');

            case 'GO_TO':
                if (state.step === 'loading' || state.step === 'closed' || state.step === 'done') return state;

                // TypeScript restricts the target to the five form steps in the event's own type;
                // a plain string cannot, so anything else is ignored rather than entered.
                if (!isBackNavigable(event.step)) return state;

                if (event.step === 'payment' && state.options.paymentOrder === 'afterAccount') {
                    // There is no pre-account card step in that order, so the card can only be
                    // re-opened where the machine itself put it.
                    if (state.step !== 'payment' && state.step !== 'disclaimers') return state;
                    if (!state.userId || state.paymentTransactionId || !signupPlanNeedsPayment(state)) {
                        return state;
                    }
                    next = copy(state);
                    next.tokenError = null;
                    next.paymentAfterAccount = true;
                    return signupEnter(next, 'payment');
                }

                next = copy(state);
                next.tokenError = null;
                return signupEnter(next, event.step);

            case 'RETRY':
                if (state.step !== 'failed' || !state.retryFrom) return state;
                if (state.retryFrom === 'loading') {
                    next = copy(state);
                    next.step = 'loading';
                    next.token = null;
                    next.error = null;
                    next.retryFrom = null;
                    return next;
                }
                // retryFrom is only ever an async step (loading, creating, packCheckout), so
                // re-entering it never lands on a payment step.
                return signupEnter(state, state.retryFrom);

            case 'RESET': {
                var fresh = initialSignupState({
                    tokenMode: state.options.tokenMode,
                    planSelection: state.options.planSelection,
                    packSelection: state.options.packSelection,
                    paymentOrder: state.options.paymentOrder
                });
                // The latch belongs to the visit, not the attempt: starting over must not suddenly
                // claim they were already signed in.
                fresh.initialized = state.initialized;
                fresh.alreadySignedInLatched = state.alreadySignedInLatched;
                return fresh;
            }

            default:
                return state;
        }
    }

    // ===== Pack-checkout machine =================================================================

    function initialPackCheckoutState(options) {
        var source = options || {};
        return {
            step: 'idle',
            token: null,
            appId: source.appId || '',
            items: source.items || [],
            quote: null,
            cardClientSecret: undefined,
            paymentTransactionId: undefined,
            useSavedCard: false,
            results: [],
            pendingIndexes: [],
            pendingPosition: 0,
            error: null,
            errorCode: undefined,
            retryFrom: null
        };
    }

    function packEnter(state, step) {
        var startsWork = step === 'quoting'
            || step === 'collectingCard'
            || step === 'checkingOut'
            || step === 'authenticating'
            || step === 'completing';

        var next = copy(state);
        next.step = step;
        next.token = startsWork ? issueStepToken() : null;
        next.error = null;
        next.errorCode = undefined;
        next.retryFrom = null;
        return next;
    }

    function packFail(state, message, retryFrom, errorCode) {
        var next = copy(state);
        next.step = 'failed';
        next.token = null;
        next.error = message;
        next.errorCode = errorCode;
        next.retryFrom = retryFrom;
        return next;
    }

    /** The pack currently being authenticated/completed, or undefined when the walk is over. */
    function currentPackCheckoutItem(state) {
        var index = state.pendingIndexes[state.pendingPosition];
        return index == null ? undefined : state.results[index];
    }

    /** Move to the next pack needing 3-D Secure, or finish. */
    function nextPending(state) {
        var advanced = copy(state);
        advanced.pendingPosition = state.pendingPosition + 1;

        if (advanced.pendingPosition >= advanced.pendingIndexes.length) {
            advanced.step = 'done';
            advanced.token = null;
            advanced.error = null;
            advanced.errorCode = undefined;
            advanced.retryFrom = null;
            return advanced;
        }

        return packEnter(advanced, 'authenticating');
    }

    /** Replace the current pack's result, keeping its place in `results`. */
    function replaceCurrent(state, patch) {
        var index = state.pendingIndexes[state.pendingPosition];
        if (index == null) return state.results;

        var results = [];
        for (var i = 0; i < state.results.length; i++) {
            if (i !== index) {
                results.push(state.results[i]);
                continue;
            }
            var merged = copy(state.results[i]);
            for (var key in patch) {
                if (Object.prototype.hasOwnProperty.call(patch, key)) merged[key] = patch[key];
            }
            results.push(merged);
        }
        return results;
    }

    /**
     * The pack-checkout reducer. Pure apart from issuing step tokens, and it returns the SAME state
     * object for an event it ignores.
     */
    function packCheckoutTransition(state, event) {
        var next;

        switch (event.type) {
            case 'QUOTE_REQUESTED':
                // Re-quoting while a quote is in flight is allowed and supersedes it; the older
                // answer is then dropped as stale.
                if (state.step !== 'idle'
                    && state.step !== 'failed'
                    && state.step !== 'quoted'
                    && state.step !== 'quoting') {
                    return state;
                }
                next = copy(state);
                next.appId = event.appId;
                next.items = event.items;
                next.quote = null;
                return packEnter(next, 'quoting');

            case 'QUOTE_RECEIVED': {
                if (state.step !== 'quoting' || !isCurrentStep(state.token, event.token)) return state;

                if (!event.quote || !event.quote.success) {
                    var refused = copy(state);
                    refused.quote = event.quote || null;
                    return packFail(
                        refused,
                        (event.quote && event.quote.errorMessage) || 'The packs could not be priced.',
                        'quoting',
                        event.quote ? event.quote.errorCode : undefined);
                }

                next = copy(state);
                next.quote = event.quote;
                next = packEnter(next, 'quoted');
                // A quote that needs no new card is bought against the one already on file.
                next.useSavedCard = !event.quote.requiresPaymentMethod;
                return next;
            }

            case 'QUOTE_FAILED':
                if (state.step !== 'quoting' || !isCurrentStep(state.token, event.token)) return state;
                return packFail(state, event.message, 'quoting', event.errorCode);

            case 'CARD_REQUESTED':
                if (state.step !== 'quoted') return state;
                next = copy(state);
                next.cardClientSecret = undefined;
                return packEnter(next, 'collectingCard');

            case 'CARD_INTENT_RECEIVED':
                if (state.step !== 'collectingCard' || !isCurrentStep(state.token, event.token)) return state;
                next = copy(state);
                next.cardClientSecret = event.clientSecret;
                next.paymentTransactionId = event.paymentTransactionId;
                return next;

            case 'CARD_INTENT_FAILED':
                if (state.step !== 'collectingCard' || !isCurrentStep(state.token, event.token)) return state;
                return packFail(state, event.message, 'collectingCard', event.errorCode);

            case 'CARD_CONFIRMED':
                if (state.step !== 'collectingCard' || !isCurrentStep(state.token, event.token)) return state;
                next = copy(state);
                next.paymentTransactionId = event.paymentTransactionId != null
                    ? event.paymentTransactionId
                    : state.paymentTransactionId;
                next.useSavedCard = false;
                return packEnter(next, 'checkingOut');

            case 'CARD_FAILED':
                if (state.step !== 'collectingCard' || !isCurrentStep(state.token, event.token)) return state;
                return packFail(state, event.message, 'collectingCard');

            case 'CHECKOUT_REQUESTED':
                if (state.step !== 'quoted') return state;
                return packEnter(state, 'checkingOut');

            case 'CHECKOUT_RECEIVED': {
                if (state.step !== 'checkingOut' || !isCurrentStep(state.token, event.token)) return state;

                var results = (event.result && event.result.results) || [];
                if (results.length === 0) {
                    return packFail(
                        state,
                        (event.result && event.result.errorMessage) || 'The packs could not be bought.',
                        'checkingOut',
                        event.result ? event.result.errorCode : undefined);
                }

                var pendingIndexes = [];
                for (var i = 0; i < results.length; i++) {
                    if (results[i] && results[i].status === 'requires_action') pendingIndexes.push(i);
                }

                next = copy(state);
                next.results = results;
                next.pendingIndexes = pendingIndexes;
                next.pendingPosition = 0;

                if (pendingIndexes.length === 0) {
                    next.step = 'done';
                    next.token = null;
                    next.error = null;
                    next.errorCode = undefined;
                    next.retryFrom = null;
                    return next;
                }

                return packEnter(next, 'authenticating');
            }

            case 'CHECKOUT_FAILED':
                if (state.step !== 'checkingOut' || !isCurrentStep(state.token, event.token)) return state;
                return packFail(state, event.message, 'checkingOut', event.errorCode);

            case 'ITEM_AUTHENTICATED':
                if (state.step !== 'authenticating' || !isCurrentStep(state.token, event.token)) return state;
                return packEnter(state, 'completing');

            case 'ITEM_AUTH_FAILED':
                if (state.step !== 'authenticating' || !isCurrentStep(state.token, event.token)) return state;
                // This pack is lost; the rest of the basket is not.
                next = copy(state);
                next.results = replaceCurrent(state, { status: 'failed', errorMessage: event.message });
                return nextPending(next);

            case 'ITEM_COMPLETED':
                if (state.step !== 'completing' || !isCurrentStep(state.token, event.token)) return state;
                next = copy(state);
                next.results = replaceCurrent(state, event.result);
                return nextPending(next);

            case 'RETRY':
                if (state.step !== 'failed' || !state.retryFrom) return state;
                if (state.retryFrom === 'collectingCard') {
                    // Back to the card form means collecting a NEW SetupIntent: the secret this
                    // state is holding belongs to the attempt that just failed.
                    next = copy(state);
                    next.cardClientSecret = undefined;
                    next.paymentTransactionId = undefined;
                    return packEnter(next, 'collectingCard');
                }
                return packEnter(state, state.retryFrom);

            case 'RESET':
                return initialPackCheckoutState({ appId: state.appId, items: state.items });

            default:
                return state;
        }
    }

    // ===== Plan-change machine ===================================================================
    //
    //   idle -> previewing -> confirm -> [collectingPayment] -> changing
    //        -> [authenticating -> completing] -> done
    //
    // Every layout confirms: the preview says what the change costs today and what it gains or
    // loses, and nobody is billed without seeing it. From there the change either applies straight
    // away, or the processor wants the prorated charge authenticated (3-D Secure) - which is a
    // "not yet", not a refusal: confirm the clientSecret, then complete the parked change by its
    // pendingChangeId. `processing` means the money is in and the server is still applying the
    // change, so completion is re-asked rather than reported as a failure.

    /** How many times a `processing` answer is re-asked before the flow gives up and says so. */
    var MAX_PLAN_CHANGE_COMPLETE_ATTEMPTS = 5;

    function initialPlanChangeState(options) {
        var source = options || {};
        return {
            step: 'idle',
            token: null,
            appId: source.appId || '',
            tierId: source.tierId || '',
            pricingId: source.pricingId,
            immediate: source.immediate === undefined ? true : source.immediate,
            preview: null,
            paymentTransactionId: undefined,
            clientSecret: undefined,
            pendingChangeId: undefined,
            result: null,
            completeAttempts: 0,
            error: null,
            errorCode: undefined,
            retryFrom: null
        };
    }

    function planEnter(state, step) {
        var startsWork = step === 'previewing'
            || step === 'changing'
            || step === 'authenticating'
            || step === 'completing';

        var next = copy(state);
        next.step = step;
        next.token = startsWork ? issueStepToken() : null;
        next.error = null;
        next.errorCode = undefined;
        next.retryFrom = null;
        return next;
    }

    function planFail(state, message, retryFrom, errorCode) {
        var next = copy(state);
        next.step = 'failed';
        next.token = null;
        next.error = message;
        next.errorCode = errorCode;
        next.retryFrom = retryFrom;
        return next;
    }

    /** Whether the change has to be paid for up front rather than through the 3-D Secure path. */
    function planNeedsPaymentFirst(state) {
        if (state.paymentTransactionId) return false;
        var preview = state.preview;
        if (!preview) return false;
        return preview.paymentRequired === true && preview.paymentBypassAllowed !== true;
    }

    /** Read the server's answer to a change or a completion and route on it. */
    function applyChangeResult(state, result, from) {
        var next = copy(state);
        next.result = result;

        if (result && result.success) {
            next.step = 'done';
            next.token = null;
            next.error = null;
            next.errorCode = undefined;
            next.retryFrom = null;
            return next;
        }

        // "Not yet", not "no": the processor accepted the change and is waiting on the customer.
        if (result && result.requiresAction && result.clientSecret && result.pendingChangeId) {
            next.clientSecret = result.clientSecret;
            next.pendingChangeId = result.pendingChangeId;
            next.completeAttempts = 0;
            return planEnter(next, 'authenticating');
        }

        // The money is in; the server is still applying the change. Ask again shortly.
        if (result && result.processing) {
            var attempts = state.completeAttempts + 1;
            next.completeAttempts = attempts;

            if (attempts >= MAX_PLAN_CHANGE_COMPLETE_ATTEMPTS) {
                return planFail(
                    next,
                    (result.errorMessage)
                        || 'The payment went through but the plan change is still being applied. Refresh in a moment.',
                    'completing',
                    result.errorCode);
            }

            if (result.pendingChangeId) next.pendingChangeId = result.pendingChangeId;
            return planEnter(next, 'completing');
        }

        return planFail(
            next,
            (result && result.errorMessage) || 'The plan change was refused.',
            from,
            result ? result.errorCode : undefined);
    }

    /**
     * The plan-change reducer. Pure apart from issuing step tokens, and it returns the SAME state
     * object for an event it ignores.
     */
    function planChangeTransition(state, event) {
        var next;

        switch (event.type) {
            case 'PREVIEW_REQUESTED':
                // Re-previewing while one is in flight supersedes it (the customer flipping the
                // billing frequency, or a doubled click); the older answer is dropped as stale.
                if (state.step !== 'idle'
                    && state.step !== 'failed'
                    && state.step !== 'done'
                    && state.step !== 'confirm'
                    && state.step !== 'previewing') {
                    return state;
                }

                next = copy(state);
                next.appId = event.appId;
                next.tierId = event.tierId;
                next.pricingId = event.pricingId;
                next.immediate = event.immediate === undefined ? state.immediate : event.immediate;
                next.preview = null;
                next.result = null;
                next.completeAttempts = 0;
                next.clientSecret = undefined;
                next.pendingChangeId = undefined;
                return planEnter(next, 'previewing');

            case 'PREVIEW_RECEIVED':
                if (state.step !== 'previewing' || !isCurrentStep(state.token, event.token)) return state;

                if (!event.preview || !event.preview.success) {
                    next = copy(state);
                    next.preview = event.preview || null;
                    return planFail(
                        next,
                        (event.preview && event.preview.errorMessage) || 'The plan change could not be priced.',
                        'previewing');
                }

                next = copy(state);
                next.preview = event.preview;
                return planEnter(next, 'confirm');

            case 'PREVIEW_FAILED':
                if (state.step !== 'previewing' || !isCurrentStep(state.token, event.token)) return state;
                return planFail(state, event.message, 'previewing');

            case 'CONFIRMED': {
                if (state.step !== 'confirm') return state;

                var collect = event.collectPayment === undefined
                    ? planNeedsPaymentFirst(state)
                    : event.collectPayment;

                var confirmed = state;
                if (event.immediate !== undefined) {
                    confirmed = copy(state);
                    confirmed.immediate = event.immediate;
                }

                return planEnter(confirmed, collect ? 'collectingPayment' : 'changing');
            }

            case 'PAYMENT_COMPLETED':
                if (state.step !== 'collectingPayment') return state;
                next = copy(state);
                next.paymentTransactionId = event.paymentTransactionId;
                return planEnter(next, 'changing');

            case 'PAYMENT_FAILED':
                if (state.step !== 'collectingPayment') return state;
                return planFail(state, event.message, 'collectingPayment');

            case 'PAYMENT_CANCELLED':
                if (state.step !== 'collectingPayment') return state;
                return planEnter(state, 'confirm');

            case 'CHANGE_RESULT':
                if (state.step !== 'changing' || !isCurrentStep(state.token, event.token)) return state;
                next = copy(state);
                next.token = null;
                return applyChangeResult(next, event.result, 'changing');

            case 'CHANGE_FAILED':
                if (state.step !== 'changing' || !isCurrentStep(state.token, event.token)) return state;
                return planFail(state, event.message, 'changing');

            case 'AUTHENTICATED':
                if (state.step !== 'authenticating' || !isCurrentStep(state.token, event.token)) return state;
                next = copy(state);
                next.completeAttempts = 0;
                return planEnter(next, 'completing');

            case 'AUTH_FAILED':
                if (state.step !== 'authenticating' || !isCurrentStep(state.token, event.token)) return state;
                return planFail(state, event.message, 'authenticating');

            case 'COMPLETE_RESULT':
                if (state.step !== 'completing' || !isCurrentStep(state.token, event.token)) return state;
                next = copy(state);
                next.token = null;
                return applyChangeResult(next, event.result, 'completing');

            case 'COMPLETE_FAILED':
                if (state.step !== 'completing' || !isCurrentStep(state.token, event.token)) return state;
                return planFail(state, event.message, 'completing');

            case 'RETRY':
                if (state.step !== 'failed' || !state.retryFrom) return state;
                // The completion budget belongs to one automatic run of retries, not to the
                // customer: a manual "Try Again" that inherited an exhausted count would give up
                // on its first answer.
                next = copy(state);
                next.completeAttempts = 0;
                return planEnter(next, state.retryFrom);

            case 'RESET':
                return initialPlanChangeState({
                    appId: state.appId,
                    tierId: state.tierId,
                    pricingId: state.pricingId,
                    immediate: state.immediate
                });

            default:
                return state;
        }
    }

    // ===== Exports ===============================================================================

    var api = {
        issueStepToken: issueStepToken,
        isCurrentStep: isCurrentStep,

        SIGNUP_FORM_ORDER: FORM_ORDER,
        signupStepName: signupStepName,
        initialSignupState: initialSignupState,
        signupTransition: signupTransition,
        signupPlanNeedsPayment: signupPlanNeedsPayment,

        initialPackCheckoutState: initialPackCheckoutState,
        packCheckoutTransition: packCheckoutTransition,
        currentPackCheckoutItem: currentPackCheckoutItem,

        MAX_PLAN_CHANGE_COMPLETE_ATTEMPTS: MAX_PLAN_CHANGE_COMPLETE_ATTEMPTS,
        initialPlanChangeState: initialPlanChangeState,
        planChangeTransition: planChangeTransition
    };

    if (typeof window !== 'undefined') {
        window.wwRegSubMachines = api;
    }

    // The self-test's hook. Guarded so the browser build never sees it: there is no `module` in a
    // classic script, and the check costs one typeof.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = api;
    }
})();
