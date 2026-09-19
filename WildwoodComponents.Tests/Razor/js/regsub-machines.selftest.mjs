// Self-test for WildwoodComponents.Razor/wwwroot/js/regsub-machines.js.
//
// There is no JavaScript test harness in this repository, and the Razor signup's flow genuinely
// lives in the browser: the machines are the one piece of it that cannot be covered by an xUnit
// test over C# decisions. So this file stands in for one. It is plain Node, no dependencies, and
// it replays a representative subset of the cases the TypeScript suite covers for
// packages/wildwood-react-shared/src/registrationSubscription/{signupMachine,packCheckoutMachine}.ts
// - the ones whose tables an implementer is most likely to get subtly wrong:
//
//   * the form order, and every skip rule
//   * a token whose grant removes the granted packs and skips the plan AND the payment
//   * a stale result returning the SAME state object
//   * invite redemption (tokenMode 'required')
//   * the finished outcome listing granted packs first
//   * a pack basket walked one requires_action item at a time
//   * one item failing its bank confirmation without stopping the basket
//   * RETRY from the card step dropping the SetupIntent it was holding
//
// Run it directly (`node regsub-machines.selftest.mjs`) or through
// RegSubMachineSelfTestRunnerTests, which shells out to node and asserts exit code 0.

import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const require = createRequire(import.meta.url);
const here = path.dirname(fileURLToPath(import.meta.url));
const machinePath = path.resolve(here, '../../../WildwoodComponents.Razor/wwwroot/js/regsub-machines.js');

const m = require(machinePath);

let failures = 0;
let checks = 0;

function check(condition, what) {
    checks += 1;
    if (!condition) {
        failures += 1;
        console.error('FAIL: ' + what);
    }
}

function equal(actual, expected, what) {
    check(actual === expected, what + ' (expected ' + JSON.stringify(expected) + ', got ' + JSON.stringify(actual) + ')');
}

function deepEqual(actual, expected, what) {
    equal(JSON.stringify(actual), JSON.stringify(expected), what);
}

// ── Fixtures ────────────────────────────────────────────────────────────────

/** The mode an app with open registration and the optional token card resolves to. */
const OPEN_WITH_TOKEN = {
    closed: false,
    requireToken: false,
    allowOpenRegistration: true,
    showOptionalTokenEntry: true
};

/** Open registration with no token path at all. */
const OPEN_ONLY = {
    closed: false,
    requireToken: false,
    allowOpenRegistration: true,
    showOptionalTokenEntry: false
};

/** Invite redemption: the token step is the only way in. */
const REQUIRE_TOKEN = {
    closed: false,
    requireToken: true,
    allowOpenRegistration: false,
    showOptionalTokenEntry: false
};

function started(options, mode, names) {
    let state = m.initialSignupState(options);
    state = m.signupTransition(state, { type: 'INIT', signedIn: false });
    state = m.signupTransition(state, { type: 'MODE_LOADED', mode: mode });
    state = m.signupTransition(state, { type: 'CATALOG_LOADED', names: names || {} });
    return state;
}

// ── 1. The form order, and the skip rules ───────────────────────────────────

(function formOrder() {
    deepEqual(m.SIGNUP_FORM_ORDER, ['register', 'token', 'plan', 'packs', 'payment', 'creating'],
        'FORM_ORDER is the six steps, in order');

    let s = started({ planSelection: 'choose', packSelection: 'choose' }, OPEN_WITH_TOKEN);
    equal(s.step, 'register', 'both answers in opens the form');

    s = m.signupTransition(s, {
        type: 'SELECTION_RESOLVED', addOnIds: ['pack-a'], requiresPayment: true
    });
    deepEqual(s.packsToBuy, ['pack-a'], 'a link-chosen pack is carried');
    equal(s.planPreset, false, 'no tier in the link means no preset plan');

    s = m.signupTransition(s, { type: 'REGISTER_SUBMITTED', email: 'a@b.test' });
    equal(s.step, 'token', 'the optional token card is walked when the mode offers it');
    equal(s.formSubmitted, true, 'the form gate is satisfied');

    s = m.signupTransition(s, { type: 'TOKEN_SKIPPED' });
    equal(s.step, 'plan', 'no token means straight to the plan');

    s = m.signupTransition(s, {
        type: 'PLAN_CHOSEN', tierId: 'tier-pro', pricingId: 'price-m', requiresPayment: true
    });
    equal(s.step, 'packs', 'a chosen plan leads to the packs');
    equal(s.planPreset, true, 'chosen is chosen: the plan step is not offered again');

    s = m.signupTransition(s, { type: 'PACKS_CHOSEN', addOnIds: ['pack-a', 'pack-a', 'pack-b'] });
    equal(s.step, 'payment', 'a paid plan takes a card BEFORE the account exists');
    deepEqual(s.packsToBuy, ['pack-a', 'pack-b'], 'the basket is de-duplicated');

    s = m.signupTransition(s, { type: 'PAYMENT_COMPLETED', paymentTransactionId: 'txn-1' });
    equal(s.step, 'creating', 'the account is created after the card');
    equal(typeof s.token, 'string', 'creating is an async step and carries a token');

    s = m.signupTransition(s, {
        type: 'ACCOUNT_CREATED', token: s.token, userId: 'user-1', requiresDisclaimers: false
    });
    equal(s.step, 'packCheckout', 'packs are bought after the sign-in');

    s = m.signupTransition(s, { type: 'PACK_CHECKOUT_FINISHED', token: s.token, packs: [] });
    equal(s.step, 'done', 'and then it is done');
    equal(m.signupStepName(s.step), 'success', 'done is spelled success in the DOM');
})();

(function skipRules() {
    // No token path at all.
    let s = started({ planSelection: 'skip', packSelection: 'none' }, OPEN_ONLY);
    s = m.signupTransition(s, { type: 'REGISTER_SUBMITTED', email: 'a@b.test' });
    equal(s.step, 'creating', 'no token, no plan, no packs and no paid plan goes straight to creating');

    // A free plan takes no card.
    let free = started({ planSelection: 'choose', packSelection: 'none' }, OPEN_ONLY);
    free = m.signupTransition(free, {
        type: 'SELECTION_RESOLVED', tierId: 'tier-free', requiresPayment: false
    });
    equal(free.planPreset, true, 'a tier in the link presets the plan');
    free = m.signupTransition(free, { type: 'REGISTER_SUBMITTED', email: 'a@b.test' });
    equal(free.step, 'creating', 'a free plan skips the payment step');

    // packSelection 'none' removes the STEP but not the packs.
    let noStep = started({ planSelection: 'skip', packSelection: 'none' }, OPEN_ONLY);
    noStep = m.signupTransition(noStep, { type: 'SELECTION_RESOLVED', addOnIds: ['pack-a'] });
    noStep = m.signupTransition(noStep, { type: 'REGISTER_SUBMITTED', email: 'a@b.test' });
    equal(noStep.step, 'creating', 'the pack step is gone');
    deepEqual(noStep.packsToBuy, ['pack-a'], "but a link's packs are still bought");
})();

(function theFormGate() {
    // "Change plan" from an empty form: choosing there goes BACK to the form, never onward.
    let s = started({ planSelection: 'choose', packSelection: 'none' }, OPEN_ONLY);
    s = m.signupTransition(s, { type: 'GO_TO', step: 'plan' });
    equal(s.step, 'plan', 'the plan grid is reachable before the form is filled in');

    s = m.signupTransition(s, { type: 'PLAN_CHOSEN', tierId: 'tier-pro', requiresPayment: true });
    equal(s.step, 'register', 'an unsubmitted form comes first, always');
})();

(function closedAndLoadFailure() {
    let closed = m.initialSignupState({});
    closed = m.signupTransition(closed, {
        type: 'MODE_LOADED',
        mode: { closed: true, requireToken: true, allowOpenRegistration: false, showOptionalTokenEntry: false }
    });
    closed = m.signupTransition(closed, { type: 'CATALOG_LOADED', names: {} });
    equal(closed.step, 'closed', 'a closed configuration never shows a form');

    let failed = m.initialSignupState({});
    failed = m.signupTransition(failed, { type: 'MODE_LOADED', mode: OPEN_ONLY });
    failed = m.signupTransition(failed, { type: 'LOAD_FAILED', message: 'no catalog' });
    equal(failed.step, 'failed', 'an unreadable catalog fails the flow');
    equal(failed.retryFrom, 'loading', 'and a retry goes back to loading');

    const retried = m.signupTransition(failed, { type: 'RETRY' });
    equal(retried.step, 'loading', 'RETRY from loading re-reads rather than re-entering a step');
    equal(retried.token, null, 'and issues no token, because loading starts no step work');
})();

// ── 2. A token grant skips the plan and the payment ─────────────────────────

(function tokenGrant() {
    let s = started({ planSelection: 'choose', packSelection: 'none' }, OPEN_WITH_TOKEN);
    s = m.signupTransition(s, {
        type: 'SELECTION_RESOLVED', addOnIds: ['pack-a', 'pack-b'], requiresPayment: true
    });
    s = m.signupTransition(s, { type: 'REGISTER_SUBMITTED', email: 'a@b.test' });
    equal(s.step, 'token', 'the token card is walked');

    s = m.signupTransition(s, { type: 'TOKEN_CHECK_STARTED' });
    equal(s.tokenChecking, true, 'the check is in flight');
    const inFlight = s.token;
    equal(typeof inFlight, 'string', 'a token check issues a step token');

    // A stale answer changes nothing, and returns the SAME object.
    const stale = m.signupTransition(s, {
        type: 'TOKEN_ACCEPTED', token: 'step-not-mine', value: 'T', grant: undefined
    });
    check(stale === s, 'a stale token result returns the same state object');

    s = m.signupTransition(s, {
        type: 'TOKEN_ACCEPTED',
        token: inFlight,
        value: 'T',
        grant: { tierId: 'tier-granted', addOnIds: ['pack-a'], featureCodes: ['F'] }
    });
    equal(s.step, 'creating', 'a grant skips the plan AND the payment');
    deepEqual(s.packsToBuy, ['pack-b'], 'a granted pack is not bought again');
    equal(s.tokenValue, 'T', 'the token value is kept');

    // A rejected token is a form error, not a failed flow.
    let rejected = started({}, OPEN_WITH_TOKEN);
    rejected = m.signupTransition(rejected, { type: 'REGISTER_SUBMITTED', email: 'a@b.test' });
    rejected = m.signupTransition(rejected, { type: 'TOKEN_CHECK_STARTED' });
    rejected = m.signupTransition(rejected, {
        type: 'TOKEN_REJECTED', token: rejected.token, message: 'Invalid or expired registration token'
    });
    equal(rejected.step, 'token', 'a rejected token does not fail the flow');
    equal(rejected.tokenError, 'Invalid or expired registration token', 'it is a form-level message');
})();

// ── 3. Invite redemption ────────────────────────────────────────────────────

(function inviteMode() {
    let s = started({ tokenMode: 'required', planSelection: 'choose', packSelection: 'choose' }, REQUIRE_TOKEN);
    s = m.signupTransition(s, { type: 'REGISTER_SUBMITTED', email: 'a@b.test' });
    equal(s.step, 'token', 'an invite always walks the token step');

    const ignored = m.signupTransition(s, { type: 'TOKEN_SKIPPED' });
    check(ignored === s, 'a required token cannot be skipped');

    s = m.signupTransition(s, { type: 'TOKEN_CHECK_STARTED' });
    s = m.signupTransition(s, {
        type: 'TOKEN_ACCEPTED',
        token: s.token,
        value: 'INVITE',
        grant: { tierId: 'tier-invited', addOnIds: [], featureCodes: [] }
    });
    equal(s.step, 'creating', 'an invite skips the plan and the packs entirely');
})();

// ── 4. The outcome lists granted packs first ────────────────────────────────

(function outcomeOrdering() {
    const names = { tiers: { 'tier-granted': 'Granted Plan' }, addOns: { g1: 'Pack One' } };

    let s = started({ packSelection: 'none' }, OPEN_WITH_TOKEN, names);
    s = m.signupTransition(s, { type: 'SELECTION_RESOLVED', addOnIds: ['bought-1'] });
    s = m.signupTransition(s, { type: 'REGISTER_SUBMITTED', email: 'a@b.test' });
    s = m.signupTransition(s, { type: 'TOKEN_CHECK_STARTED' });
    s = m.signupTransition(s, {
        type: 'TOKEN_ACCEPTED',
        token: s.token,
        value: 'T',
        grant: { tierId: 'tier-granted', addOnIds: ['g1', 'g2'], featureCodes: [] }
    });
    s = m.signupTransition(s, {
        type: 'ACCOUNT_CREATED', token: s.token, userId: 'user-9', requiresDisclaimers: false
    });
    equal(s.step, 'packCheckout', 'the un-granted pack is still bought');

    s = m.signupTransition(s, {
        type: 'PACK_CHECKOUT_FINISHED',
        token: s.token,
        packs: [{ addOnId: 'bought-1', name: 'Bought', status: 'active' }]
    });

    equal(s.step, 'done', 'the signup finishes');
    equal(s.outcome.userId, 'user-9', 'the outcome carries the new account');
    equal(s.outcome.tier.tierId, 'tier-granted', "the grant's tier wins over the selection");
    equal(s.outcome.tier.name, 'Granted Plan', 'and is named from the catalog');
    equal(s.outcome.packs.length, 3, 'one outcome per pack');
    deepEqual(
        s.outcome.packs.map(function (p) { return p.addOnId + ':' + p.status; }),
        ['g1:granted', 'g2:granted', 'bought-1:active'],
        'granted packs are listed first');
    equal(s.outcome.packs[0].name, 'Pack One', 'a granted pack is named from the catalog');
    equal(s.outcome.packs[1].name, 'g2', 'and falls back to its id when the catalog does not name it');
    equal(s.outcome.planActivationPending, undefined, 'an ordinary outcome carries no dead flag');
})();

// ── 5. Pack checkout: one card, then each item in order ─────────────────────

(function packCheckoutSavedCard() {
    const items = [{ addOnId: 'x' }, { addOnId: 'y' }];
    let p = m.initialPackCheckoutState({ appId: 'app-1', items: items });
    equal(p.step, 'idle', 'the checkout starts idle');

    p = m.packCheckoutTransition(p, { type: 'QUOTE_REQUESTED', appId: 'app-1', items: items });
    equal(p.step, 'quoting', 'a quote is asked for');

    const stale = m.packCheckoutTransition(p, {
        type: 'QUOTE_RECEIVED', token: 'step-not-mine', quote: { success: true }
    });
    check(stale === p, 'a stale quote returns the same state object');

    p = m.packCheckoutTransition(p, {
        type: 'QUOTE_RECEIVED',
        token: p.token,
        quote: { success: true, checkoutId: 'co-1', requiresPaymentMethod: false, lines: [] }
    });
    equal(p.step, 'quoted', 'the basket is priced');
    equal(p.useSavedCard, true, 'a quote that needs no new card uses the one on file');

    p = m.packCheckoutTransition(p, { type: 'CHECKOUT_REQUESTED' });
    equal(p.step, 'checkingOut', 'the basket is bought in one call');

    p = m.packCheckoutTransition(p, {
        type: 'CHECKOUT_RECEIVED',
        token: p.token,
        result: {
            results: [
                { addOnId: 'x', status: 'requires_action', clientSecret: 'cs-x', paymentTransactionId: 'ptx-x' },
                { addOnId: 'y', status: 'requires_action', clientSecret: 'cs-y', paymentTransactionId: 'ptx-y' }
            ]
        }
    });
    equal(p.step, 'authenticating', 'a pack the bank wants authenticated stops the walk there');
    deepEqual(p.pendingIndexes, [0, 1], 'both items are pending, in order');
    equal(m.currentPackCheckoutItem(p).addOnId, 'x', 'the first pending item is first');

    // Item one: authenticated and completed.
    p = m.packCheckoutTransition(p, { type: 'ITEM_AUTHENTICATED', token: p.token });
    equal(p.step, 'completing', 'an authenticated item is completed on the server');
    p = m.packCheckoutTransition(p, {
        type: 'ITEM_COMPLETED', token: p.token, result: { addOnId: 'x', status: 'active' }
    });
    equal(p.step, 'authenticating', 'and then the walk moves to the next one');
    equal(m.currentPackCheckoutItem(p).addOnId, 'y', 'strictly one pack at a time, in order');
    equal(p.results[0].status, 'active', "the completed item's result is merged in place");

    // Item two: the bank refuses. Only that pack is lost.
    p = m.packCheckoutTransition(p, {
        type: 'ITEM_AUTH_FAILED', token: p.token, message: 'Your card was declined.'
    });
    equal(p.step, 'done', 'the walk finishes even though one pack failed');
    equal(p.results[0].status, 'active', 'the pack that worked is untouched');
    equal(p.results[1].status, 'failed', 'the pack that did not is marked failed');
    equal(p.results[1].errorMessage, 'Your card was declined.', 'with the bank own words');
})();

(function packCheckoutCardOnce() {
    const items = [{ addOnId: 'x' }];
    let p = m.initialPackCheckoutState({ appId: 'app-1', items: items });
    p = m.packCheckoutTransition(p, { type: 'QUOTE_REQUESTED', appId: 'app-1', items: items });
    p = m.packCheckoutTransition(p, {
        type: 'QUOTE_RECEIVED',
        token: p.token,
        quote: { success: true, checkoutId: 'co-1', requiresPaymentMethod: true, lines: [] }
    });
    equal(p.useSavedCard, false, 'no card on file means one has to be collected');

    p = m.packCheckoutTransition(p, { type: 'CARD_REQUESTED' });
    equal(p.step, 'collectingCard', 'the card form is shown once, for the whole basket');

    p = m.packCheckoutTransition(p, {
        type: 'CARD_INTENT_RECEIVED', token: p.token, clientSecret: 'seti_1', paymentTransactionId: 'ptx-1'
    });
    equal(p.step, 'collectingCard', 'receiving the intent does not move the step');
    equal(p.cardClientSecret, 'seti_1', 'the SetupIntent secret is held for the form');

    p = m.packCheckoutTransition(p, { type: 'CARD_CONFIRMED', token: p.token });
    equal(p.step, 'checkingOut', 'a confirmed card buys the basket');
    equal(p.paymentTransactionId, 'ptx-1', 'the collected card travels with the purchase');
    equal(p.useSavedCard, false, 'and it is not the saved one');
})();

(function packCheckoutRetryDropsTheIntent() {
    let p = m.initialPackCheckoutState({ appId: 'app-1', items: [{ addOnId: 'x' }] });
    p = m.packCheckoutTransition(p, { type: 'QUOTE_REQUESTED', appId: 'app-1', items: [{ addOnId: 'x' }] });
    p = m.packCheckoutTransition(p, {
        type: 'QUOTE_RECEIVED',
        token: p.token,
        quote: { success: true, checkoutId: 'co-1', requiresPaymentMethod: true, lines: [] }
    });
    p = m.packCheckoutTransition(p, { type: 'CARD_REQUESTED' });
    p = m.packCheckoutTransition(p, {
        type: 'CARD_INTENT_RECEIVED', token: p.token, clientSecret: 'seti_1', paymentTransactionId: 'ptx-1'
    });
    p = m.packCheckoutTransition(p, { type: 'CARD_FAILED', token: p.token, message: 'declined' });
    equal(p.step, 'failed', 'a refused card fails the checkout');
    equal(p.retryFrom, 'collectingCard', 'and a retry goes back to the card');

    p = m.packCheckoutTransition(p, { type: 'RETRY' });
    equal(p.step, 'collectingCard', 'the card is collected again');
    equal(p.cardClientSecret, undefined, 'the SetupIntent of the failed attempt is dropped');
    equal(p.paymentTransactionId, undefined, 'and so is the transaction it belonged to');
})();

(function packCheckoutRefusedQuote() {
    let p = m.initialPackCheckoutState({ appId: 'app-1', items: [{ addOnId: 'x' }] });
    p = m.packCheckoutTransition(p, { type: 'QUOTE_REQUESTED', appId: 'app-1', items: [{ addOnId: 'x' }] });
    p = m.packCheckoutTransition(p, {
        type: 'QUOTE_RECEIVED',
        token: p.token,
        quote: { success: false, errorCode: 'AlreadyOwned', errorMessage: 'You already own that pack.' }
    });
    equal(p.step, 'failed', 'a refused quote fails the checkout');
    equal(p.error, 'You already own that pack.', "with the server's own words");
    equal(p.errorCode, 'AlreadyOwned', 'and its code');
    equal(p.retryFrom, 'quoting', 'a retry re-quotes');
})();

// ── Result ──────────────────────────────────────────────────────────────────

if (failures > 0) {
    console.error(failures + ' of ' + checks + ' checks failed.');
    process.exit(1);
}

console.log('regsub-machines self-test: ' + checks + ' checks passed.');
