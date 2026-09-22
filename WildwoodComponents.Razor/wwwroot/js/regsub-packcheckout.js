/*
 * WildwoodComponents.Razor - Registration & Subscription: the pack-checkout driver
 *
 * ONE quote, ONE card for a basket of any size, ONE checkout call, and then each pack the bank
 * wants authenticated walked in order. Classic script, no modules, no build step. Load
 * regsub-machines.js FIRST:
 *
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-packcheckout.js"></script>
 *
 * This file exists because TWO surfaces buy packs as the signed-in user - the signup's pack step
 * and the manage view's pack picker - and a second copy of this sequence is a second place for the
 * 3-D Secure walk, the SetupIntent reuse and the "never abandon an authenticated charge" rule to
 * drift. regsub-signup.js and regsub-manage.js both call it; neither owns it.
 *
 * Three rules it keeps:
 *
 *   1. NO ENGLISH, NO MONEY, NO MARKUP. Every visible string is passed in from the server-rendered
 *      label bundle. Text is written with textContent; no markup-writing property is ever
 *      assigned, and a source guard greps this file for one. Amounts are the server's; this file
 *      formats none.
 *   2. NO WORK IS DONE TWICE. Every async step is claimed by the machine's step token before it
 *      starts, so a double click and a doubled payment callback cannot buy twice.
 *   3. NOTHING WHOSE MONEY-MOVING CALL WAS ISSUED IS ABANDONED. Inside the page - Start Over, a
 *      picker being closed, a host removing the component - an item already authenticated with the
 *      bank is completed on the server before this instance lets go. The authenticate-then-complete
 *      pair is therefore ONE awaited chain rather than two separately pumped steps.
 *
 * The caller owns the markup and the proxy. The driver only asks for a `scope` element carrying
 * the same data hooks in both surfaces:
 *
 *   [data-ww-pack-summary] / [data-ww-pack-summary-lines] / [data-ww-pack-saved-card]
 *   [data-ww-pack-card-form] / [data-ww-pack-card-host] / [data-ww-pack-card-error]
 *   [data-ww-pack-status] / [data-ww-pack-error] / [data-ww-pack-actions]
 */
(function () {
    'use strict';

    var STRIPE_SRC = 'https://js.stripe.com/v3/';

    /** Codes, matching the other stacks exactly. A host's analytics switches on these. */
    var CODE_PACK_QUOTE = 'pack_quote_failed';
    var CODE_PACK_CARD = 'pack_card_failed';
    var CODE_PACK_CHECKOUT = 'pack_checkout_failed';

    /**
     * The one sentence React hard-codes inside its pack checkout rather than putting it in the
     * label bundle. Reproduced verbatim so the stacks read the same; every OTHER string here comes
     * off the server.
     */
    var CARD_UNAVAILABLE_MESSAGE = 'A card could not be collected right now.';

    // ===== Pure decisions (no DOM, no state, no I/O) ==============================================

    /** RegistrationSubscriptionLabels.Format: fills one {key} slot; an unmatched slot is left as is. */
    function formatLabel(template, key, value) {
        if (!template) return '';
        return String(template).split('{' + key + '}').join(String(value));
    }

    /** The message a refused call is reported with: the server's words, else the shipped fallback. */
    function refusalMessage(result, fallback) {
        if (result && result.errorMessage) return result.errorMessage;
        if (result && result.message) return result.message;
        return fallback;
    }

    /** Every requested pack as a checkout item. */
    function checkoutItems(addOnIds) {
        var items = [];
        if (!addOnIds) return items;
        for (var i = 0; i < addOnIds.length; i++) items.push({ addOnId: addOnIds[i] });
        return items;
    }

    /**
     * PackCheckout's toOutcomes: ONE outcome per requested item, in the order they were asked for.
     * A status that is not trialing/active is a failure, with the server's own words when it sent
     * any.
     */
    function toOutcomes(items, results, quote, names, fallbackMessage) {
        var outcomes = [];
        var lines = (quote && quote.lines) || [];
        var lookup = names || {};

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

            var name = lookup[addOnId] || (quoted && quoted.name) || addOnId;
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

    /** A basket in CATALOG order rather than click order, so it reads like the grid it came from. */
    function orderSelection(order, selected) {
        var ordered = [];
        for (var i = 0; i < order.length; i++) {
            if (selected.indexOf(order[i]) !== -1) ordered.push(order[i]);
        }
        return ordered;
    }

    /**
     * Tick or untick one pack in a grid. Unticking is always allowed, cap or no cap: a visitor who
     * is over the cap has to be able to get back under it.
     */
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

    /** Every requested pack reported as failed - "Skip for now", and a checkout that never ran. */
    function failedOutcomes(items, names, message) {
        var outcomes = [];
        var lookup = names || {};

        for (var i = 0; i < items.length; i++) {
            var addOnId = items[i].addOnId;
            outcomes.push({
                addOnId: addOnId,
                name: lookup[addOnId] || addOnId,
                status: 'failed',
                errorMessage: message
            });
        }

        return outcomes;
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

    /**
     * One pack-checkout run, bound to one scope element.
     *
     * @param options.machines      the reducers (defaults to window.wwRegSubMachines)
     * @param options.appId         the app the packs belong to
     * @param options.scope         the element holding the data-ww-pack-* hooks
     * @param options.post          post(path, body) against the shipped proxy, app id already on it
     * @param options.labels        server-rendered copy: buyingPacks, authenticatingPack,
     *                              packsUnavailable, savedCardOnFile, cardDetails
     * @param options.packNames     { addOnId: name }, for the status line and the outcomes
     * @param options.publishableKey the Stripe key for the SetupIntent and the 3-D Secure walk
     * @param options.providerId    fallback provider when the quote names none
     * @param options.onError       onError(code, message)
     * @param options.onFinished    onFinished(outcomes) - once, unless the caller detached first
     */
    function create(options) {
        var settings = options || {};
        var machines = settings.machines || (typeof window !== 'undefined' ? window.wwRegSubMachines : null);
        if (!machines) return null;

        var scope = settings.scope;
        var appId = settings.appId || '';
        var post = settings.post;
        var labels = settings.labels || {};
        var packNames = settings.packNames || {};
        var publishableKey = settings.publishableKey || '';
        var providerId = settings.providerId || '';

        var state = machines.initialPackCheckoutState({ appId: appId });
        var quote = null;
        var stripe = null;
        var stripeCard = null;
        var detached = false;
        var settling = false;

        function q(selector) { return scope ? scope.querySelector(selector) : null; }

        function setText(el, text) {
            if (el) el.textContent = text || '';
        }

        function show(el, visible) {
            if (el) el.hidden = !visible;
        }

        function report(code, message) {
            if (typeof settings.onError === 'function') settings.onError(code, message);
        }

        function dispatch(event) {
            var next = machines.packCheckoutTransition(state, event);
            if (next === state) return false;
            state = next;
            paint();
            return true;
        }

        function paint() {
            if (detached || !scope) return;

            var status = q('[data-ww-pack-status]');
            var step = state.step;

            if (step === 'authenticating' || step === 'completing') {
                var item = machines.currentPackCheckoutItem(state);
                var name = item ? (packNames[item.addOnId] || item.addOnId) : '';
                setText(status, formatLabel(labels.authenticatingPack, 'name', name));
            } else if (step === 'quoting' || step === 'checkingOut') {
                setText(status, labels.buyingPacks);
            } else {
                setText(status, '');
            }

            show(q('[data-ww-pack-card-form]'), step === 'collectingCard');

            var error = q('[data-ww-pack-error]');
            setText(error, state.error || '');
            show(error, Boolean(state.error));
            show(q('[data-ww-pack-actions]'), step === 'failed');

            paintQuote();
        }

        function paintQuote() {
            var panel = q('[data-ww-pack-summary]');
            if (!panel) return;

            if (!quote || !quote.success) {
                show(panel, false);
                return;
            }

            var lines = q('[data-ww-pack-summary-lines]');
            if (lines) {
                while (lines.firstChild) lines.removeChild(lines.firstChild);
                for (var i = 0; i < (quote.lines || []).length; i++) {
                    var line = quote.lines[i];
                    var row = document.createElement('div');
                    row.className = 'ww-payment-summary-row';
                    row.setAttribute('data-ww-pack', line.addOnId);

                    var nameEl = document.createElement('span');
                    nameEl.textContent = packNames[line.addOnId] || line.name || line.addOnId;
                    row.appendChild(nameEl);

                    // The server quoted these amounts as numbers; nothing in this file formats one.
                    lines.appendChild(row);
                }
            }

            var saved = q('[data-ww-pack-saved-card]');
            if (saved) {
                if (quote.savedCard && quote.savedCard.last4) {
                    var text = formatLabel(labels.savedCardOnFile, 'brand', quote.savedCard.brand || '');
                    setText(saved, formatLabel(text, 'last4', quote.savedCard.last4));
                    show(saved, true);
                } else {
                    show(saved, false);
                }
            }

            show(panel, true);
        }

        // ----- The sequence ----------------------------------------------------------------------

        function start(items) {
            state = machines.initialPackCheckoutState({ appId: appId, items: items });
            quote = null;

            dispatch({ type: 'QUOTE_REQUESTED', appId: appId, items: items });
            runQuote();
        }

        function runQuote() {
            var token = state.token;
            settling = true;

            post('/checkout/quote', {
                Items: state.items.map(function (item) { return { AddOnId: item.addOnId }; })
            })
                .then(function (result) {
                    quote = result;
                    if (!result || !result.success) {
                        report(CODE_PACK_QUOTE, refusalMessage(result, labels.packsUnavailable));
                    }
                    dispatch({ type: 'QUOTE_RECEIVED', token: token, quote: result });
                    afterQuote();
                })
                .catch(function (error) {
                    var message = (error && error.message) || labels.packsUnavailable;
                    report(CODE_PACK_QUOTE, message);
                    dispatch({ type: 'QUOTE_FAILED', token: token, message: message });
                    settling = false;
                });
        }

        function afterQuote() {
            if (state.step !== 'quoted') { settling = false; return; }

            if (quote.requiresPaymentMethod) {
                dispatch({ type: 'CARD_REQUESTED' });
                collectCard();
                return;
            }

            dispatch({ type: 'CHECKOUT_REQUESTED' });
            runCheckout();
        }

        /** The SetupIntent: one card, for a basket of any size. */
        function collectCard() {
            var token = state.token;

            if (!publishableKey) {
                dispatch({ type: 'CARD_INTENT_FAILED', token: token, message: CARD_UNAVAILABLE_MESSAGE });
                report(CODE_PACK_CARD, CARD_UNAVAILABLE_MESSAGE);
                settling = false;
                return;
            }

            Promise.all([
                post('/checkout/payment-method', { ProviderId: quote.providerId || providerId }),
                mountCard()
            ])
                .then(function (answers) {
                    var intent = answers[0];
                    if (!intent || !intent.success || !intent.clientSecret) {
                        throw new Error(refusalMessage(intent, CARD_UNAVAILABLE_MESSAGE));
                    }
                    dispatch({
                        type: 'CARD_INTENT_RECEIVED',
                        token: token,
                        clientSecret: intent.clientSecret,
                        paymentTransactionId: intent.paymentTransactionId
                    });
                    // Waits for the visitor now: the Save card button confirms the intent.
                    settling = false;
                })
                .catch(function (error) {
                    var message = (error && error.message) || CARD_UNAVAILABLE_MESSAGE;
                    dispatch({ type: 'CARD_INTENT_FAILED', token: token, message: message });
                    report(CODE_PACK_CARD, message);
                    settling = false;
                });
        }

        function mountCard() {
            if (stripeCard) return Promise.resolve();

            return loadScript(STRIPE_SRC).then(function () {
                if (typeof window.Stripe === 'undefined') throw new Error(CARD_UNAVAILABLE_MESSAGE);
                var host = q('[data-ww-pack-card-host]');
                if (!host) throw new Error(CARD_UNAVAILABLE_MESSAGE);

                stripe = window.Stripe(publishableKey);
                stripeCard = stripe.elements().create('card');
                stripeCard.mount(host);
            });
        }

        /** The Save card button: confirms the SetupIntent, then buys the basket. */
        function confirmCard() {
            var token = state.token;
            if (state.step !== 'collectingCard' || !state.cardClientSecret || !stripe) return;

            var error = q('[data-ww-pack-card-error]');
            show(error, false);
            settling = true;

            stripe.confirmCardSetup(state.cardClientSecret, { payment_method: { card: stripeCard } })
                .then(function (answer) {
                    if (answer.error || !answer.setupIntent || answer.setupIntent.status !== 'succeeded') {
                        var message = (answer.error && answer.error.message) || CARD_UNAVAILABLE_MESSAGE;
                        setText(error, message);
                        show(error, true);
                        settling = false;
                        return;
                    }
                    dispatch({ type: 'CARD_CONFIRMED', token: token });
                    runCheckout();
                })
                .catch(function (e) {
                    setText(error, (e && e.message) || CARD_UNAVAILABLE_MESSAGE);
                    show(error, true);
                    settling = false;
                });
        }

        function runCheckout() {
            var token = state.token;
            settling = true;

            post('/checkout', {
                CheckoutId: quote.checkoutId,
                ProviderId: quote.providerId || '',
                PaymentTransactionId: state.paymentTransactionId || null,
                UseSavedCard: state.useSavedCard,
                Items: state.items.map(function (item) { return { AddOnId: item.addOnId }; })
            })
                .then(function (result) {
                    if (!result || !result.results || result.results.length === 0) {
                        report(CODE_PACK_CHECKOUT, refusalMessage(result, labels.packsUnavailable));
                    }
                    dispatch({ type: 'CHECKOUT_RECEIVED', token: token, result: result });
                    walkPending();
                })
                .catch(function (error) {
                    var message = (error && error.message) || labels.packsUnavailable;
                    report(CODE_PACK_CHECKOUT, message);
                    dispatch({ type: 'CHECKOUT_FAILED', token: token, message: message });
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
        function walkPending() {
            if (state.step === 'done') { settling = false; finish(); return; }
            if (state.step !== 'authenticating') { settling = false; return; }

            var token = state.token;
            var item = machines.currentPackCheckoutItem(state);

            if (!item || !item.clientSecret || !stripe) {
                // Nothing to confirm with: this pack is lost, the rest of the basket is not.
                dispatch({
                    type: 'ITEM_AUTH_FAILED',
                    token: token,
                    message: item && item.errorMessage ? item.errorMessage : CARD_UNAVAILABLE_MESSAGE
                });
                walkPending();
                return;
            }

            settling = true;

            stripe.confirmCardPayment(item.clientSecret)
                .then(function (answer) {
                    if (answer.error) {
                        dispatch({
                            type: 'ITEM_AUTH_FAILED',
                            token: token,
                            message: answer.error.message || CARD_UNAVAILABLE_MESSAGE
                        });
                        walkPending();
                        return null;
                    }

                    dispatch({ type: 'ITEM_AUTHENTICATED', token: token });

                    // The money has moved. Completing it on the server is owed even if the view
                    // has gone in the meantime, so it is awaited here rather than pumped.
                    var completingToken = state.token;
                    return post('/checkout/complete', {
                        PaymentTransactionId: item.paymentTransactionId
                    }).then(function (completed) {
                        var merged = completed || {};
                        if (!merged.addOnId) merged.addOnId = item.addOnId;
                        dispatch({ type: 'ITEM_COMPLETED', token: completingToken, result: merged });
                        walkPending();
                    }).catch(function (error) {
                        dispatch({
                            type: 'ITEM_COMPLETED',
                            token: completingToken,
                            result: {
                                addOnId: item.addOnId,
                                status: 'failed',
                                errorMessage: (error && error.message) || labels.packsUnavailable
                            }
                        });
                        walkPending();
                    });
                })
                .catch(function (error) {
                    dispatch({
                        type: 'ITEM_AUTH_FAILED',
                        token: token,
                        message: (error && error.message) || CARD_UNAVAILABLE_MESSAGE
                    });
                    walkPending();
                });
        }

        function finish() {
            if (detached) return;
            if (typeof settings.onFinished === 'function') {
                settings.onFinished(toOutcomes(state.items, state.results, quote, packNames, labels.packsUnavailable));
            }
        }

        /** "Skip for now": every requested pack is reported as failed and the caller carries on. */
        function skip() {
            if (detached) return;
            if (typeof settings.onFinished === 'function') {
                settings.onFinished(
                    failedOutcomes(state.items, packNames, state.error || labels.packsUnavailable));
            }
        }

        /** Runs the failed step again, from wherever it stopped. */
        function retry() {
            if (!dispatch({ type: 'RETRY' })) return;

            if (state.step === 'quoting') { runQuote(); return; }
            if (state.step === 'collectingCard') { collectCard(); return; }
            if (state.step === 'checkingOut') { runCheckout(); return; }
            if (state.step === 'authenticating') { walkPending(); }
        }

        return {
            start: start,
            confirmCard: confirmCard,
            retry: retry,
            skip: skip,

            getState: function () { return state; },
            getQuote: function () { return quote; },

            /** Whether a money-moving call is in flight. A caller must not throw the run away now. */
            isSettling: function () { return settling; },

            /**
             * DETACHES this run: no more painting and no outcome callback. Nothing already under
             * way is cut short - a pack whose bank confirmation has been issued is still completed
             * on the server by the chain that started it.
             */
            destroy: function () {
                detached = true;
                if (stripeCard) {
                    try { stripeCard.destroy(); } catch (e) { /* already gone */ }
                    stripeCard = null;
                }
            }
        };
    }

    var api = {
        create: create,

        // Pure, and exported so the Node self-test can cover them.
        toOutcomes: toOutcomes,
        failedOutcomes: failedOutcomes,
        checkoutItems: checkoutItems,
        orderSelection: orderSelection,
        toggleSelection: toggleSelection,
        refusalMessage: refusalMessage,
        formatLabel: formatLabel,

        CARD_UNAVAILABLE_MESSAGE: CARD_UNAVAILABLE_MESSAGE,
        CODE_PACK_QUOTE: CODE_PACK_QUOTE,
        CODE_PACK_CARD: CODE_PACK_CARD,
        CODE_PACK_CHECKOUT: CODE_PACK_CHECKOUT
    };

    if (typeof window !== 'undefined') {
        window.wwRegSubPackCheckout = api;
    }

    // The self-test's hook. Guarded so the browser build never sees it: there is no `module` in a
    // classic script, and the check costs one typeof.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = api;
    }
})();
