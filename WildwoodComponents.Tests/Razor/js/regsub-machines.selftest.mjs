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
//   * the plan change: a parked 3-D Secure change, `processing` re-asked and bounded, and a
//     stale answer ignored
//   * which proxy a plan change posts to, and that SupportsPaymentAction is sent by the SELF
//     scope and nothing else
//   * that a priced self-service change is POSTED rather than refused for costing money, and that
//     the payment-required path is the SERVER's coded refusal and nothing else
//
// Run it directly (`node regsub-machines.selftest.mjs`) or through
// RegSubMachineSelfTestRunnerTests, which shells out to node and asserts exit code 0.

import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const require = createRequire(import.meta.url);
const here = path.dirname(fileURLToPath(import.meta.url));
const scripts = path.resolve(here, '../../../WildwoodComponents.Razor/wwwroot/js');

const m = require(path.join(scripts, 'regsub-machines.js'));

// The two SHARED drivers. Their sequencing needs a browser, but the decisions that decide where a
// request goes, what it carries and what a refusal is called are pure - and those are exactly the
// ones a second surface can silently disagree with, so they are covered here.
const packs = require(path.join(scripts, 'regsub-packcheckout.js'));
const planChange = require(path.join(scripts, 'regsub-planchange.js'));

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


// ── Plan-change machine ─────────────────────────────────────────────────────

/** A priced, confirmable change. */
const PREVIEW_OK = {
    success: true,
    isUpgrade: true,
    newTierName: 'Pro',
    paymentRequired: false,
    currency: 'USD'
};

function previewed(preview) {
    let s = m.initialPlanChangeState({ appId: 'app-1' });
    s = m.planChangeTransition(s, { type: 'PREVIEW_REQUESTED', appId: 'app-1', tierId: 'tier-pro' });
    s = m.planChangeTransition(s, { type: 'PREVIEW_RECEIVED', token: s.token, preview: preview || PREVIEW_OK });
    return s;
}

(function planChangeHappyPath() {
    let s = m.initialPlanChangeState({ appId: 'app-1' });
    equal(s.step, 'idle', 'a plan change starts idle');
    equal(s.immediate, true, 'and immediate unless told otherwise');

    s = m.planChangeTransition(s, {
        type: 'PREVIEW_REQUESTED', appId: 'app-1', tierId: 'tier-pro', pricingId: 'price-1'
    });
    equal(s.step, 'previewing', 'choosing a plan prices it first');
    check(Boolean(s.token), 'and the pricing carries a step token');

    s = m.planChangeTransition(s, { type: 'PREVIEW_RECEIVED', token: s.token, preview: PREVIEW_OK });
    equal(s.step, 'confirm', 'a priced change is confirmed before anyone is billed');
    equal(s.token, null, 'the confirmation waits on a person, so it holds no token');

    s = m.planChangeTransition(s, { type: 'CONFIRMED', collectPayment: false });
    equal(s.step, 'changing', 'confirming posts the change');

    s = m.planChangeTransition(s, {
        type: 'CHANGE_RESULT', token: s.token, result: { success: true }
    });
    equal(s.step, 'done', 'and a successful change is done');
    equal(s.error, null, 'with nothing to report');
})();

(function planChangePreviewRefused() {
    let s = m.initialPlanChangeState({ appId: 'app-1' });
    s = m.planChangeTransition(s, { type: 'PREVIEW_REQUESTED', appId: 'app-1', tierId: 'tier-pro' });
    s = m.planChangeTransition(s, {
        type: 'PREVIEW_RECEIVED',
        token: s.token,
        preview: { success: false, errorMessage: 'That plan is not available to you.' }
    });
    equal(s.step, 'failed', 'a preview the server refused fails the change');
    equal(s.error, 'That plan is not available to you.', 'with the servers own words');
    equal(s.retryFrom, 'previewing', 'and a retry prices it again');
})();

(function planChangeNeedsPaymentFirst() {
    let s = previewed({ success: true, paymentRequired: true, newTierName: 'Pro' });
    s = m.planChangeTransition(s, { type: 'CONFIRMED' });
    equal(s.step, 'collectingPayment', 'a change needing a NEW card asks for one first');

    let t = previewed({ success: true, paymentRequired: true, paymentBypassAllowed: true, newTierName: 'Pro' });
    t = m.planChangeTransition(t, { type: 'CONFIRMED' });
    equal(t.step, 'changing', 'an admin-bypassable one does not');
})();

(function planChangeParksOnABankChallenge() {
    let s = previewed();
    s = m.planChangeTransition(s, { type: 'CONFIRMED', collectPayment: false });

    // requiresAction arrives with success:false and is NOT a failure.
    s = m.planChangeTransition(s, {
        type: 'CHANGE_RESULT',
        token: s.token,
        result: {
            success: false,
            requiresAction: true,
            clientSecret: 'pi_secret',
            pendingChangeId: 'pending-1'
        }
    });
    equal(s.step, 'authenticating', 'a parked change asks the bank rather than failing');
    equal(s.clientSecret, 'pi_secret', 'and holds the secret to confirm');
    equal(s.pendingChangeId, 'pending-1', 'and the parked change to finish');
    equal(s.error, null, 'requiresAction is not an error');

    s = m.planChangeTransition(s, { type: 'AUTHENTICATED', token: s.token });
    equal(s.step, 'completing', 'an authenticated charge completes the parked change');
    equal(s.completeAttempts, 0, 'with a fresh completion budget');

    s = m.planChangeTransition(s, { type: 'COMPLETE_RESULT', token: s.token, result: { success: true } });
    equal(s.step, 'done', 'and the plan has moved');
})();

(function planChangeAuthRefusedIsAFailure() {
    let s = previewed();
    s = m.planChangeTransition(s, { type: 'CONFIRMED', collectPayment: false });
    s = m.planChangeTransition(s, {
        type: 'CHANGE_RESULT',
        token: s.token,
        result: { success: false, requiresAction: true, clientSecret: 'pi', pendingChangeId: 'p1' }
    });
    s = m.planChangeTransition(s, { type: 'AUTH_FAILED', token: s.token, message: 'Card declined.' });
    equal(s.step, 'failed', 'a refused bank challenge fails the change');
    equal(s.retryFrom, 'authenticating', 'and a retry asks the bank again');
})();

(function planChangeProcessingIsReAskedAndBounded() {
    let s = previewed();
    s = m.planChangeTransition(s, { type: 'CONFIRMED', collectPayment: false });
    s = m.planChangeTransition(s, {
        type: 'CHANGE_RESULT',
        token: s.token,
        result: { success: false, requiresAction: true, clientSecret: 'pi', pendingChangeId: 'p1' }
    });
    s = m.planChangeTransition(s, { type: 'AUTHENTICATED', token: s.token });

    const processing = { success: false, processing: true };

    for (let attempt = 1; attempt < m.MAX_PLAN_CHANGE_COMPLETE_ATTEMPTS; attempt++) {
        s = m.planChangeTransition(s, { type: 'COMPLETE_RESULT', token: s.token, result: processing });
        equal(s.step, 'completing', 'a processing answer is asked again, not reported (' + attempt + ')');
        equal(s.completeAttempts, attempt, 'and the budget is counted (' + attempt + ')');
    }

    s = m.planChangeTransition(s, { type: 'COMPLETE_RESULT', token: s.token, result: processing });
    equal(s.step, 'failed', 'the fifth processing answer gives up');
    equal(s.completeAttempts, m.MAX_PLAN_CHANGE_COMPLETE_ATTEMPTS, 'at exactly the bounded budget');
    equal(s.retryFrom, 'completing', 'and a retry finishes the parked change rather than re-posting it');

    s = m.planChangeTransition(s, { type: 'RETRY' });
    equal(s.step, 'completing', 'Try Again completes it again');
    equal(s.completeAttempts, 0, 'with the budget started over - it belongs to one automatic run');
})();

(function planChangeCarriesTheServersRefusalCode() {
    for (const code of ['pending_change_superseded', 'pending_change_expired']) {
        let s = previewed();
        s = m.planChangeTransition(s, { type: 'CONFIRMED', collectPayment: false });
        s = m.planChangeTransition(s, {
            type: 'CHANGE_RESULT',
            token: s.token,
            result: { success: false, errorCode: code, errorMessage: 'no' }
        });
        equal(s.step, 'failed', code + ' fails the change');
        equal(s.errorCode, code, 'and the code travels for the message lookup');
        equal(
            planChange.messageForCode({ planChangeSuperseded: 'S', planChangeExpired: 'E' }, s.errorCode, 'fallback'),
            code === 'pending_change_superseded' ? 'S' : 'E',
            'which maps to the label for ' + code);
    }
})();

(function planChangeIgnoresAStaleAnswer() {
    let s = previewed();
    s = m.planChangeTransition(s, { type: 'CONFIRMED', collectPayment: false });

    const same = m.planChangeTransition(s, {
        type: 'CHANGE_RESULT', token: 'step-stale', result: { success: true }
    });
    check(same === s, 'an answer carrying another step token returns the SAME state object');

    const alsoSame = m.planChangeTransition(s, { type: 'PAYMENT_COMPLETED', paymentTransactionId: 'tx' });
    check(alsoSame === s, 'and so does an event for a step the machine is not on');
})();

(function planChangeResetKeepsTheChoice() {
    let s = m.initialPlanChangeState({ appId: 'app-1' });
    s = m.planChangeTransition(s, {
        type: 'PREVIEW_REQUESTED', appId: 'app-1', tierId: 'tier-pro', pricingId: 'price-1', immediate: false
    });
    s = m.planChangeTransition(s, { type: 'PREVIEW_RECEIVED', token: s.token, preview: PREVIEW_OK });

    const reset = m.planChangeTransition(s, { type: 'RESET' });
    equal(reset.step, 'idle', 'RESET forgets the attempt');
    equal(reset.preview, null, 'and the price it was quoted');
    equal(reset.tierId, 'tier-pro', 'but keeps the plan it was about');
    equal(reset.pricingId, 'price-1', 'and its pricing option');
    equal(reset.immediate, false, 'and the timing the customer chose');
})();

// ── The shared plan-change driver's pure decisions ──────────────────────────

(function planChangeRoutesByScope() {
    const ctx = { tierId: 'tier-pro', pricingId: 'price-1', isChange: true };

    const self = planChange.changeRequest('self', 'app-1', null, null, ctx, true, null);
    equal(self.transport, 'regsub', 'the customers own change goes to the SHIPPED proxy');
    equal(self.path, '/tier-change', 'at the self route');
    equal(self.body.SupportsPaymentAction, true, 'and says it can answer a bank challenge');

    const user = planChange.changeRequest('user', 'app-1', 'user-9', null, ctx, true, null);
    equal(user.transport, 'host', 'an admins change for one user goes to the HOST proxy');
    equal(user.path, 'change-tier', 'at the admin route');
    equal(user.body.SupportsPaymentAction, undefined, 'and never claims it can answer a challenge');
    equal(user.body.UserId, 'user-9', 'carrying whose subscription it is');

    const company = planChange.changeRequest('company', 'app-1', null, 'co-7', ctx, false, null);
    equal(company.transport, 'host', 'a company change goes to the HOST proxy');
    equal(company.path, 'app-1/change-tier/company', 'at the company route');
    equal(company.body.SupportsPaymentAction, undefined, 'and never claims it can answer a challenge');
    equal(company.body.Immediate, false, 'carrying the timing chosen');

    const first = planChange.changeRequest('self', 'app-1', null, null, { tierId: 't', isChange: false }, true, 'tx-1');
    equal(first.path, '/subscribe', 'a FIRST subscription is not a change');
    equal(first.body.PaymentTransactionId, 'tx-1', 'and carries the payment it was bought with');
    equal(first.body.SupportsPaymentAction, undefined, 'a subscribe cannot be parked, so it claims nothing');
})();

(function planChangePreviewsWhereItCan() {
    const ctx = { tierId: 'tier-pro', pricingId: 'price-1', isChange: true };

    equal(planChange.previewRequest('self', 'app-1', null, ctx).transport, 'regsub',
        'the self preview is a shipped route');
    equal(planChange.previewRequest('user', 'app-1', 'user-9', ctx).path, 'app-1/admin/preview-change/user-9',
        'an admin previews against that user');
    equal(planChange.previewRequest('company', 'app-1', null, ctx).path, 'app-1/my-subscription/preview-change',
        'and company mode falls back to the self preview - there is no company-scoped one');

    equal(planChange.completePath('pending 1'), '/tier-change/pending%201/complete',
        'the completion path escapes the parked change id');
})();

(function planChangeNamesItsFailures() {
    equal(planChange.codeForFailure({ retryFrom: 'previewing' }), 'tier_preview_failed', 'a preview failure');
    equal(planChange.codeForFailure({ retryFrom: 'authenticating' }), 'tier_change_authentication_failed', 'a bank failure');
    equal(planChange.codeForFailure({ retryFrom: 'completing' }), 'tier_change_completion_failed', 'a completion failure');
    equal(planChange.codeForFailure({ retryFrom: 'changing' }), 'tier_change_failed', 'anything else');
    equal(planChange.codeForFailure({ retryFrom: 'changing', errorCode: 'pending_change_expired' }),
        'pending_change_expired', 'and the servers own code wins');

    check(planChange.canRetry({ step: 'failed', retryFrom: 'previewing' }), 'a priced failure may be retried');
    check(!planChange.canRetry({ step: 'failed', retryFrom: 'collectingPayment' }),
        'a change wanting a card this package does not collect may NOT: the same click asks the same question');
    check(!planChange.canRetry({ step: 'confirm', retryFrom: 'previewing' }), 'and nothing is retried mid-flight');
})();

(function razorPostsAPricedChangeRatherThanRefusingIt() {
    // Razor has no plan-change payment form (standing decision), so it collects no card - but
    // "this change costs money" is NOT "this account has no card". paymentRequired is true for
    // essentially every priced upgrade (it is what draws the "Today's charge" line) and
    // paymentBypassAllowed is an ADMIN override, so a plain customer with a card on file has
    // exactly these inputs, and the old subscription-admin.js posted the change for them.
    equal(planChange.razorNeedsPaymentCollection(), false,
        'this package never collects a card before posting a plan change');

    const paid = { success: true, isUpgrade: true, newTierName: 'Pro', paymentRequired: true, currency: 'USD' };

    const event = planChange.confirmEvent({ immediate: true, bypassPayment: false });
    equal(event.type, 'CONFIRMED', 'confirming dispatches CONFIRMED');
    equal(event.collectPayment, false, 'naming collectPayment false, which routes around collectingPayment');
    equal(event.immediate, true, 'and carrying the timing the customer chose');

    let s = previewed(paid);
    s = m.planChangeTransition(s, event);
    equal(s.step, 'changing', 'so a priced self-service change POSTS the change');
    check(Boolean(s.token), 'under one step token, so a doubled confirm posts once');

    // The bypass toggle is an admin's view of the confirmation, never a wire field: the old
    // script never sent one, and neither does this.
    let b = previewed({ ...paid, paymentBypassAllowed: true });
    b = m.planChangeTransition(b, planChange.confirmEvent({ immediate: false, bypassPayment: true }));
    equal(b.step, 'changing', 'and so does a bypassed one');
    equal(b.immediate, false, 'keeping its timing');

    const request = planChange.changeRequest(
        'self', 'app-1', null, null, { tierId: 'tier-pro', pricingId: 'price-1', isChange: true }, true, null);
    deepEqual(
        request.body,
        {
            NewAppTierId: 'tier-pro',
            NewAppTierPricingId: 'price-1',
            Immediate: true,
            PaymentTransactionId: null,
            SupportsPaymentAction: true
        },
        'and what it posts is the old body plus the two fields the parked-payment path needs');
})();

(function razorReachesTheParkedChangeOnAPricedUpgrade() {
    // The regression this pins: the 3-D Secure park/complete path is only ever reached by a change
    // that was actually posted, which is the common case - a priced upgrade, no bypass.
    let s = previewed({ success: true, isUpgrade: true, newTierName: 'Pro', paymentRequired: true });
    s = m.planChangeTransition(s, planChange.confirmEvent({ immediate: true }));
    equal(s.step, 'changing', 'a priced upgrade is posted');

    s = m.planChangeTransition(s, {
        type: 'CHANGE_RESULT',
        token: s.token,
        result: { success: false, requiresAction: true, clientSecret: 'pi_secret', pendingChangeId: 'pending-1' }
    });
    equal(s.step, 'authenticating', 'and its bank challenge parks the change rather than failing it');

    s = m.planChangeTransition(s, { type: 'AUTHENTICATED', token: s.token });
    s = m.planChangeTransition(s, { type: 'COMPLETE_RESULT', token: s.token, result: { success: true } });
    equal(s.step, 'done', 'and the parked change is completed');
})();

(function razorAsksForACardOnlyWhenTheServerSaysThereIsNone() {
    const noCard = {
        success: false,
        errorCode: 'PaymentMethodRequired',
        errorMessage: 'Please enter a card to continue'
    };

    check(planChange.isNoPaymentMethodRefusal(noCard), 'the servers PaymentMethodRequired code is the payment-required path');
    check(!planChange.isNoPaymentMethodRefusal({ success: true }), 'a success is not one');
    check(!planChange.isNoPaymentMethodRefusal({ success: false, requiresAction: true, errorCode: 'PaymentMethodRequired' }),
        'nor is a parked change - requiresAction is not a refusal at all');
    check(!planChange.isNoPaymentMethodRefusal({ success: false, processing: true, errorCode: 'PaymentMethodRequired' }),
        'nor is one still being applied');
    check(!planChange.isNoPaymentMethodRefusal({ success: false, errorCode: 'ProviderNotAvailable' }),
        'and an app with no processor at all is an ordinary failure - a host cannot collect a card for it either');

    let s = previewed({ success: true, isUpgrade: true, newTierName: 'Pro', paymentRequired: true });
    s = m.planChangeTransition(s, planChange.confirmEvent({ immediate: true }));
    s = m.planChangeTransition(s, { type: 'CHANGE_RESULT', token: s.token, result: noCard });

    equal(s.step, 'failed', 'a change the server refused for want of a card fails');
    equal(s.errorCode, 'PaymentMethodRequired', 'carrying the code that says why');
    check(!planChange.canRetry(s), 'and Try Again is withheld - the same click asks the same question');
    equal(
        planChange.paymentRequiredMessage(s.error, planChange.DEFAULT_TEXT.paymentMethodRequired),
        'Please enter a card to continue This change needs a payment method.',
        "with the servers own words plus this package's sentence");
    equal(
        planChange.paymentRequiredMessage('', 'Add a card in Billing first.'),
        'Add a card in Billing first.',
        'and the replaceable sentence alone when the server said nothing');
})();

(function razorTreatsEveryOtherRefusalAsRetryable() {
    // What WildwoodAPI actually answers a paid change it will not make: BadRequest { error }, with
    // NO code, which AppTierActionMapper labels RequestFailed. That is the old script's behaviour -
    // the server's message, and Try Again - and nothing here reads the English to decide otherwise.
    const refused = {
        success: false,
        errorCode: 'RequestFailed',
        errorMessage: 'Payment is required to upgrade to a higher-priced tier'
    };

    check(!planChange.isNoPaymentMethodRefusal(refused), 'an uncoded refusal is not the payment-required path');
    check(!planChange.isNoPaymentMethodCode(undefined), 'nor is a refusal with no code at all');

    let s = previewed({ success: true, isUpgrade: true, newTierName: 'Pro', paymentRequired: true });
    s = m.planChangeTransition(s, planChange.confirmEvent({ immediate: true }));
    s = m.planChangeTransition(s, { type: 'CHANGE_RESULT', token: s.token, result: refused });

    equal(s.step, 'failed', 'it is an ordinary failure');
    equal(s.error, 'Payment is required to upgrade to a higher-priced tier', 'showing what the server said');
    equal(s.retryFrom, 'changing', 'from the change itself');
    check(planChange.canRetry(s), 'and Try Again is offered');

    s = m.planChangeTransition(s, { type: 'RETRY' });
    equal(s.step, 'changing', 'which posts the change again');
})();

(function planChangeSaysWhatAPaymentWouldNeed() {
    const detail = planChange.paymentRequiredDetail(
        { tierId: 'tier-pro', tierName: 'Pro', pricingId: 'price-1', pricingModelId: 'pm-1', price: 0, trialDays: 14 },
        { newPrice: 25, currency: 'USD' },
        'USD');

    equal(detail.tierId, 'tier-pro', 'the host is told which plan');
    equal(detail.pricingModelId, 'pm-1', 'and which pricing model to charge on');
    equal(detail.price, 25, 'the plans own price, not the proration');
    equal(detail.trialDays, 14, 'and the trial it would start');
    check(detail.priceText.length > 0, 'with the amount formatted once, by the shared formatter');
})();

// ── The shared pack-checkout driver's pure decisions ────────────────────────

(function packOutcomesAreOnePerRequestedItem() {
    const items = [{ addOnId: 'a' }, { addOnId: 'b' }, { addOnId: 'c' }];
    const results = [
        { addOnId: 'a', status: 'trialing', trialEnd: '2026-10-01' },
        { addOnId: 'b', status: 'failed', errorMessage: 'Card declined.' }
    ];
    const quote = { lines: [{ addOnId: 'c', name: 'Quoted C' }] };

    const outcomes = packs.toOutcomes(items, results, quote, { a: 'Pack A' }, 'Your packs could not be bought.');

    equal(outcomes.length, 3, 'one outcome per REQUESTED pack, answered or not');
    equal(outcomes[0].name, 'Pack A', 'named from the catalog when it is known');
    equal(outcomes[0].status, 'trialing', 'a started trial passes through');
    equal(outcomes[0].trialEnd, '2026-10-01', 'with its end date');
    equal(outcomes[1].errorMessage, 'Card declined.', 'a refusal keeps the servers words');
    equal(outcomes[2].name, 'Quoted C', 'a pack the catalog did not name falls back to the quotes name');
    equal(outcomes[2].status, 'failed', 'and a pack nothing answered for is a failure, never a silent success');
    equal(outcomes[2].errorMessage, 'Your packs could not be bought.', 'with the shipped fallback');
})();

(function packSkipReportsEveryPack() {
    const outcomes = packs.failedOutcomes([{ addOnId: 'a' }, { addOnId: 'b' }], { a: 'Pack A' }, 'Skipped.');
    equal(outcomes.length, 2, 'Skip for now still answers for every pack');
    equal(outcomes[0].status, 'failed', 'as a failure');
    equal(outcomes[1].name, 'b', 'and an unnamed pack is named by its id');
})();

(function packSelectionIsCappedAndOrdered() {
    const order = ['a', 'b', 'c'];

    deepEqual(packs.toggleSelection(order, [], 'c', 25), ['c'], 'ticking one selects it');
    deepEqual(packs.toggleSelection(order, ['c'], 'a', 25), ['a', 'c'], 'and a basket reads in CATALOG order');
    deepEqual(packs.toggleSelection(order, ['a', 'c'], 'c', 25), ['a'], 'ticking again unticks');
    deepEqual(packs.toggleSelection(order, ['a', 'b'], 'c', 2), ['a', 'b'], 'the cap refuses one more');
    deepEqual(packs.toggleSelection(order, ['a', 'b'], 'b', 2), ['a'],
        'but unticking is always allowed, cap or no cap');
    deepEqual(packs.checkoutItems(['a', 'b']), [{ addOnId: 'a' }, { addOnId: 'b' }], 'and a basket becomes items');
})();

// ── Result ──────────────────────────────────────────────────────────────────

if (failures > 0) {
    console.error(failures + ' of ' + checks + ' checks failed.');
    process.exit(1);
}

console.log('regsub-machines self-test: ' + checks + ' checks passed.');
