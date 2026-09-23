/*
 * WildwoodComponents.Razor - Registration & Subscription: the plan-change driver
 *
 * Price it, confirm it, post it, answer the bank if it asks, and finish the parked change.
 * Classic script, no modules, no build step. Load regsub-machines.js FIRST:
 *
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>
 *   <script src="~/_content/WildwoodComponents.Razor/js/regsub-planchange.js"></script>
 *
 * TWO surfaces change a plan - <vc:registration-subscription-manage /> and the older
 * <vc:subscription-admin /> - and they must not drift, because the part that can drift is the part
 * that moves money: whether SupportsPaymentAction is sent, whether a 3-D Secure challenge is put
 * to the customer, and whether the parked change is completed afterwards. So the sequence lives
 * here once; regsub-manage.js and subscription-admin.js both call it and neither owns it.
 *
 *   idle -> previewing -> confirm -> changing -> [authenticating -> completing] -> done
 *
 * Three rules:
 *
 *   1. EVERY LAYOUT CONFIRMS. The preview says what the change costs today and what it gains or
 *      loses, and nobody is billed without seeing it. The confirmation modal is built here, so
 *      both surfaces show the same one.
 *   2. A PARKED CHANGE IS FINISHED, NOT ABANDONED. `requiresAction` and `processing` arrive with
 *      success:false and are NOT failures: the charge is confirmed with the bank and the change
 *      completed by its pendingChangeId, re-asked on the machine's bounded budget. Once
 *      confirmCardPayment has been issued the completion runs even if the surface is being torn
 *      down inside the page - the alternative is a customer who paid a proration and never got
 *      the plan.
 *   3. NO CARD IS COLLECTED HERE, AND NO CHANGE IS REFUSED FOR THAT. Razor has no plan-change
 *      payment form by standing decision (Sync CLAUDE.md / plan Decision 6), so a confirmed change
 *      is POSTED - always - and the money is settled server-side: by the admin bypass, or by the
 *      card already on file, which is how essentially every priced upgrade is paid for. "This
 *      change costs money" is NOT "this account has no card", and the preview's paymentRequired
 *      says only the first. Only the SERVER can say the second, and only after the change has been
 *      put to it; when it does - by error CODE, never by reading its English - the customer is told,
 *      ww-regsub-payment-required is raised with everything a host needs to mount <vc:payment />
 *      itself, and Try Again is withheld because the same click would ask the same question.
 *      A change that needs the card ALREADY ON FILE authenticated is a different thing again, and
 *      that one this driver finishes.
 *
 * SCOPE decides the transport, and it is the whole of the difference between the two surfaces'
 * requests:
 *
 *   self     -> the SHIPPED proxy (/api/wildwood-regsub). SupportsPaymentAction: true.
 *   user     -> the HOST's app-tier proxy, admin-scoped. Never SupportsPaymentAction.
 *   company  -> the HOST's app-tier proxy, company-scoped. Never SupportsPaymentAction.
 */
(function () {
    'use strict';

    /** TS COMPLETE_RETRY_DELAY_MS: only a "processing" answer waits; the first attempt is immediate. */
    var COMPLETE_RETRY_DELAY_MS = 500;

    /** A ceiling on one pump. Every iteration either advances the machine or stops. */
    var MAX_PUMP_ITERATIONS = 64;

    var STRIPE_SRC = 'https://js.stripe.com/v3/';

    /** The onError codes, by the step that failed. TS codeForFailure. */
    var CODE_PREVIEW_FAILED = 'tier_preview_failed';
    var CODE_AUTH_FAILED = 'tier_change_authentication_failed';
    var CODE_COMPLETION_FAILED = 'tier_change_completion_failed';
    var CODE_CHANGE_FAILED = 'tier_change_failed';

    /** Said when the server would not price the change and sent no reason. */
    var PREVIEW_REFUSED = 'The plan change could not be priced.';

    /** Said when the change was refused and no reason came with it. */
    var CHANGE_REFUSED = 'The plan change was refused.';

    /** Said when the completion threw rather than answering. */
    var COMPLETION_REFUSED = 'The plan change could not be completed.';

    /** Said when the bank challenge could not be put to the customer, or came back refused. */
    var CHARGE_UNCONFIRMED = 'The charge could not be confirmed with your bank. Your plan has not changed.';

    /** The wire codes WildwoodAPI refuses a parked change with (Shared TierChangeErrorCodes). */
    var ERROR_CODES = {
        notFound: 'pending_change_not_found',
        expired: 'pending_change_expired',
        paymentFailed: 'pending_change_payment_failed',
        superseded: 'pending_change_superseded',
        alreadyInProgress: 'tier_change_already_in_progress'
    };

    /**
     * The wire codes that mean "there is no payment method to charge for this" - the ONLY thing
     * that makes a refused change a payment-required one rather than an ordinary failure.
     *
     * One code, and it is the ecosystem's own: Shared `AddOnCheckoutErrorCodes.PaymentMethodRequired`
     * (Models/AppTierCheckoutModels.cs) = the JS `AddOnCheckoutErrorCode` union member of the same
     * name = WildwoodAPI's `AddOnCheckoutErrors.PaymentMethodRequired`.
     *
     * Deliberately NOT in this list:
     *
     *   - `ProviderNotAvailable` - the APP has no card processor at all. Handing that to a host as
     *     "collect a card yourself" is advice it cannot take; the confirmation already warns about
     *     it, and it is an ordinary failure.
     *   - `pending_change_payment_failed` - a card that WAS charged and declined, on the parked
     *     path, with its own label and its own retry.
     *
     * The tier-change routes refuse a paid change today with a bare `BadRequest { error }` carrying
     * NO code, which AppTierActionMapper labels `RequestFailed`: that lands on the fallback below -
     * the server's own sentence, shown as an ordinary failure with Try Again, exactly as the old
     * subscription-admin.js showed it. Nothing here reads an English message to decide.
     */
    var NO_PAYMENT_METHOD_CODES = ['PaymentMethodRequired'];

    /**
     * The modal's copy. Every string is a named default rather than an inline literal, so a
     * surface with a server-rendered label bundle can replace the ones its contract names
     * (the manage view passes `keepPlan`, `upgradeTitle` and `processing`); the older admin panel
     * passes none and keeps the words it has always shown.
     */
    var DEFAULT_TEXT = {
        upgradeTitle: 'Upgrade to {tier}',
        downgradeTitle: 'Downgrade to {tier}',
        close: 'Close',
        currentLabel: 'Current',
        newLabel: 'New',
        noPlan: 'None',
        unknownPlan: 'Unknown',
        savings: 'Save {percent}% — {amount}/mo billed {frequency}',
        todaysCharge: "Today's charge",
        creditLine: 'Credit ({days} unused days on {tier})',
        newPlanLine: '{tier} ({days} days)',
        netCharge: 'Net charge today',
        nextBilling: 'Next billing: {date} — {amount}/{frequency}',
        downgradeCredit: '{amount} credit will be applied to your next bill.',
        featuresGained: "You'll gain:",
        featuresLost: "You'll lose access to:",
        timingQuestion: 'When should this take effect?',
        endOfPeriodDated: 'End of billing period ({date})',
        endOfPeriod: 'End of billing period',
        keepUntilThen: 'Keep {tier} features until then.',
        creditNote: '{amount} credit on your next bill.',
        immediately: 'Immediately',
        switchNow: 'Switch to {tier} now.',
        noProvider: 'Payment processing is not configured for this application. Contact your administrator.',
        bypass: 'Bypass payment (admin override)',
        keepPlan: 'Keep Current Plan',
        processing: 'Processing...',
        applyNoCharge: 'Apply change (no charge)',
        upgradeFor: 'Upgrade for {amount}',
        confirmDowngrade: 'Confirm Downgrade',
        switchTo: 'Switch to {tier}',

        /** Said when the change needs a card this package will not ask for. */
        paymentMethodRequired: 'This change needs a payment method.'
    };

    // ===== Pure decisions (no DOM, no state, no I/O) ==============================================

    /** Fills one {key} slot; an unmatched slot is left as written. */
    function formatLabel(template, key, value) {
        if (!template) return '';
        return String(template).split('{' + key + '}').join(String(value));
    }

    /**
     * Money for display. The same helper payment.js, payment-form.js, token-registration.js and
     * subscription-admin.js each carry: the JS SDK's own formatMoney
     * (packages/wildwood-core/src/features/catalog.ts), matching C# FormatHelpers.FormatMoney, with
     * the locale fixed at en-US so a server-rendered amount and a browser-rendered one read the
     * same. The preview's figures arrive from a live call, so this is the one surface in the
     * registration/subscription set whose amounts the browser has to format.
     */
    function wwFormatMoney(amount, currency) {
        var code = (typeof currency === 'string' ? currency.trim() : '');
        code = (code.length > 0 ? code : 'USD').toUpperCase();
        var value = Number(amount);
        if (!isFinite(value)) value = 0;
        try {
            return new Intl.NumberFormat('en-US', { style: 'currency', currency: code }).format(value);
        } catch (e) {
            // Intl throws on anything that is not a three-letter code; say the amount and the code
            // rather than nothing - and never a dollar sign for a currency that is not dollars.
            return code + ' ' + value.toFixed(2);
        }
    }

    /** Whose subscription is being changed. A user id wins, as the JS view's scope chain does. */
    function resolveScope(settings) {
        var source = settings || {};
        if (!source.isCompanyMode && source.userId) return 'user';
        if (source.isCompanyMode && source.companyId) return 'company';
        return 'self';
    }

    /** Whether the change is authorised server-side, so no card is ever collected for it. */
    function isAdminScoped(scope) {
        return scope === 'user' || scope === 'company';
    }

    /**
     * Whether THIS package collects a card before posting a plan change. It never does, in any
     * scope, for any price - standing decision (Sync CLAUDE.md; Razor has no plan-change payment
     * form), and the equivalent of React's unwired onPaymentRequired default.
     *
     * The reducer has a `collectingPayment` state because React and Blazor DO collect one there.
     * Razor routes around it by answering this question at the confirmation, rather than by
     * editing a table that three stacks share: CONFIRMED is dispatched with
     * `collectPayment: false`, which the reducer already accepts, and the change goes straight to
     * `changing`. Named and exported so the intent is explicit and the self-test can pin it.
     */
    function razorNeedsPaymentCollection() {
        return false;
    }

    /** Whether an error code is one of the "no payment method" codes. */
    function isNoPaymentMethodCode(errorCode) {
        if (!errorCode) return false;
        for (var i = 0; i < NO_PAYMENT_METHOD_CODES.length; i++) {
            if (NO_PAYMENT_METHOD_CODES[i] === errorCode) return true;
        }
        return false;
    }

    /**
     * Whether the SERVER's answer to a POSTED change refuses it for want of a payment method.
     *
     * `requiresAction` and `processing` are excluded because they are not refusals at all, and a
     * success obviously is not one. Everything else is decided by the code alone.
     */
    function isNoPaymentMethodRefusal(result) {
        if (!result || result.success) return false;
        if (result.requiresAction || result.processing) return false;
        return isNoPaymentMethodCode(result.errorCode);
    }

    /**
     * The event a confirmed change dispatches. Always CONFIRMED, always straight to `changing`.
     * `immediate` carries the timing the customer picked in the confirmation.
     */
    function confirmEvent(chosen) {
        var picked = chosen || {};
        return {
            type: 'CONFIRMED',
            collectPayment: razorNeedsPaymentCollection(),
            immediate: picked.immediate
        };
    }

    /** What is said when the server refused the change for want of a payment method. */
    function paymentRequiredMessage(serverMessage, label) {
        var said = typeof serverMessage === 'string' ? serverMessage.trim() : '';
        var needs = label || DEFAULT_TEXT.paymentMethodRequired;
        return said.length > 0 ? said + ' ' + needs : needs;
    }

    /**
     * Where the preview is asked for. There is no company-scoped preview endpoint, so company mode
     * falls back to the self one on the HOST proxy - the same known limitation @wildwood/core and
     * React's useSubscriptionAdmin carry, and only inaccurate when an admin previews a company
     * other than their own.
     */
    function previewRequest(scope, appId, userId, ctx) {
        if (scope === 'user') {
            return {
                transport: 'host',
                path: appId + '/admin/preview-change/' + userId,
                body: { NewAppTierId: ctx.tierId, NewAppTierPricingId: ctx.pricingId || null }
            };
        }

        if (scope === 'company') {
            return {
                transport: 'host',
                path: appId + '/my-subscription/preview-change',
                body: { NewAppTierId: ctx.tierId, NewAppTierPricingId: ctx.pricingId || null }
            };
        }

        return {
            transport: 'regsub',
            path: '/tier-change/preview',
            body: { NewAppTierId: ctx.tierId, NewAppTierPricingId: ctx.pricingId || null }
        };
    }

    /**
     * Where the change is posted, and with what body.
     *
     * SupportsPaymentAction is sent by the SELF change and nothing else: it tells the server it may
     * park a change on a bank challenge instead of refusing it, and only this driver, acting as the
     * signed-in user, can answer that challenge and complete the parked change afterwards.
     */
    function changeRequest(scope, appId, userId, companyId, ctx, immediate, paymentTransactionId) {
        if (ctx.isChange) {
            if (scope === 'company') {
                return {
                    transport: 'host',
                    path: appId + '/change-tier/company',
                    body: {
                        CompanyId: companyId,
                        NewAppTierId: ctx.tierId,
                        NewAppTierPricingId: ctx.pricingId || null,
                        Immediate: immediate
                    }
                };
            }

            if (scope === 'user') {
                return {
                    transport: 'host',
                    path: 'change-tier',
                    body: {
                        UserId: userId,
                        AppId: appId,
                        NewAppTierId: ctx.tierId,
                        NewAppTierPricingId: ctx.pricingId || null,
                        Immediate: immediate
                    }
                };
            }

            return {
                transport: 'regsub',
                path: '/tier-change',
                body: {
                    NewAppTierId: ctx.tierId,
                    NewAppTierPricingId: ctx.pricingId || null,
                    Immediate: immediate,
                    PaymentTransactionId: paymentTransactionId || null,
                    SupportsPaymentAction: true
                }
            };
        }

        if (scope === 'company') {
            return {
                transport: 'host',
                path: appId + '/subscribe/company',
                body: { CompanyId: companyId, AppTierId: ctx.tierId, AppTierPricingId: ctx.pricingId || null }
            };
        }

        if (scope === 'user') {
            return {
                transport: 'host',
                path: 'subscribe',
                body: {
                    UserId: userId,
                    AppId: appId,
                    AppTierId: ctx.tierId,
                    AppTierPricingId: ctx.pricingId || null
                }
            };
        }

        return {
            transport: 'regsub',
            path: '/subscribe',
            body: {
                TierId: ctx.tierId,
                PricingId: ctx.pricingId || null,
                PaymentTransactionId: paymentTransactionId || null
            }
        };
    }

    /** Where a parked change is finished. Always the shipped proxy: it is a self-scoped change. */
    function completePath(pendingChangeId) {
        return '/tier-change/' + encodeURIComponent(pendingChangeId) + '/complete';
    }

    /** The message for a refusal the server gave a code for. TS messageForCode. */
    function messageForCode(labels, errorCode, fallback) {
        var copy = labels || {};

        switch (errorCode) {
            case ERROR_CODES.expired:
                return copy.planChangeExpired || fallback;

            case ERROR_CODES.paymentFailed:
                return copy.planChangePaymentFailed || fallback;

            case ERROR_CODES.superseded:
                return copy.planChangeSuperseded || fallback;

            case ERROR_CODES.notFound:
                return copy.planChangeNotFound || fallback;

            case ERROR_CODES.alreadyInProgress:
                return copy.planChangeInProgress || fallback;

            default:
                return fallback;
        }
    }

    /** The onError code for a failure, preferring the server's own. */
    function codeForFailure(state) {
        if (state.errorCode) return state.errorCode;

        switch (state.retryFrom) {
            case 'previewing':
                return CODE_PREVIEW_FAILED;

            case 'authenticating':
                return CODE_AUTH_FAILED;

            case 'completing':
                return CODE_COMPLETION_FAILED;

            default:
                return CODE_CHANGE_FAILED;
        }
    }

    /**
     * Whether a failure may be retried from where it stopped. A change the SERVER refused for want
     * of a payment method may NOT: re-posting it would ask the same question and get the same
     * answer until a card exists. (`collectingPayment` is the same rule for a state this driver no
     * longer routes to - see razorNeedsPaymentCollection - kept because the reducer still has it.)
     */
    function canRetry(state) {
        return state.step === 'failed'
            && Boolean(state.retryFrom)
            && state.retryFrom !== 'collectingPayment'
            && !isNoPaymentMethodCode(state.errorCode);
    }

    /** What a host needs to mount a payment component of its own for this change. */
    function paymentRequiredDetail(ctx, preview, currency) {
        var priced = preview || {};
        var amount = ctx.price;
        if (!(typeof amount === 'number' && isFinite(amount) && amount > 0)) {
            amount = priced.newPrice;
        }
        if (!(typeof amount === 'number' && isFinite(amount))) {
            amount = priced.proratedChargeToday;
        }
        if (!(typeof amount === 'number' && isFinite(amount))) amount = 0;

        return {
            tierId: ctx.tierId,
            tierName: ctx.tierName || priced.newTierName || '',
            pricingId: ctx.pricingId || null,
            pricingModelId: ctx.pricingModelId || null,
            price: amount,
            priceText: wwFormatMoney(amount, priced.currency || currency),
            trialDays: ctx.trialDays || 0,
            currency: priced.currency || currency || ''
        };
    }

    // ===== The confirmation modal =================================================================

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function clear(node) {
        while (node.firstChild) node.removeChild(node.firstChild);
    }

    /**
     * The gain/lose tick and cross, built as real SVG nodes. They used to be a markup string
     * assigned into the DOM; nothing in this surface writes markup, so the icons are now created
     * like every other element.
     */
    function icon(gained) {
        var ns = 'http://www.w3.org/2000/svg';
        var svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('width', '14');
        svg.setAttribute('height', '14');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('fill', 'none');
        svg.setAttribute('stroke', 'currentColor');
        svg.setAttribute('stroke-width', '2.5');

        if (gained) {
            var tick = document.createElementNS(ns, 'polyline');
            tick.setAttribute('points', '20 6 9 17 4 12');
            svg.appendChild(tick);
            return svg;
        }

        var down = document.createElementNS(ns, 'line');
        down.setAttribute('x1', '18');
        down.setAttribute('y1', '6');
        down.setAttribute('x2', '6');
        down.setAttribute('y2', '18');
        svg.appendChild(down);

        var up = document.createElementNS(ns, 'line');
        up.setAttribute('x1', '6');
        up.setAttribute('y1', '6');
        up.setAttribute('x2', '18');
        up.setAttribute('y2', '18');
        svg.appendChild(up);

        return svg;
    }

    /**
     * The tier-change confirmation, in every layout. Returns a handle the driver re-renders as the
     * change progresses and removes when it is over.
     *
     * @param container the element the overlay is appended to
     * @param preview   the server's priced answer
     * @param settings  { currency, text, onConfirm({immediate, bypassPayment}), onClose() }
     */
    function openTierChangeModal(container, preview, settings) {
        var options = settings || {};
        var text = options.text || DEFAULT_TEXT;
        var currency = options.currency || 'USD';

        // Downgrades default to end-of-period; upgrades and everything else to immediate.
        var state = { immediate: !preview.isDowngrade, bypassPayment: false, loading: false };

        var overlay = el('div', 'ww-modal-overlay');
        overlay.setAttribute('data-ww-modal', 'tier-change');
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay && !state.loading) close();
        });

        function close() {
            if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
            if (typeof options.onClose === 'function') options.onClose();
        }

        function money(amount, ccy) {
            return wwFormatMoney(amount === null || amount === undefined ? 0 : amount, ccy || currency);
        }

        function submit() {
            state.loading = true;
            render();
            if (typeof options.onConfirm === 'function') {
                options.onConfirm({ immediate: state.immediate, bypassPayment: state.bypassPayment });
            }
        }

        function buildFeatureList(label, features, gained) {
            var wrap = el('div', 'ww-tier-change-features');
            wrap.appendChild(el('div', 'ww-tier-change-features-label', label));
            var list = el('ul', 'ww-tier-change-features-list');
            for (var i = 0; i < features.length; i++) {
                var li = el('li', gained ? 'ww-tier-change-feature-gained' : 'ww-tier-change-feature-lost');
                var iconSpan = document.createElement('span');
                iconSpan.appendChild(icon(gained));
                li.appendChild(iconSpan);
                li.appendChild(document.createTextNode(features[i]));
                list.appendChild(li);
            }
            wrap.appendChild(list);
            return wrap;
        }

        function buildTimingOption(titleText, descText, checked, onSelect) {
            var label = el('label', 'ww-tier-change-timing-option');
            var input = document.createElement('input');
            input.type = 'radio';
            input.name = 'ww-tier-change-timing';
            input.checked = checked;
            input.disabled = state.loading;
            input.addEventListener('change', onSelect);
            label.appendChild(input);
            var content = el('div');
            content.appendChild(el('strong', null, titleText));
            content.appendChild(el('span', 'ww-tier-change-timing-desc', descText));
            label.appendChild(content);
            return label;
        }

        function render() {
            var ccy = preview.currency || currency;
            var effectivePaymentRequired = preview.paymentRequired && !state.bypassPayment;
            var showPaymentBypass = preview.paymentBypassAllowed && preview.paymentRequired;

            clear(overlay);

            var modal = el('div', 'ww-modal ww-tier-change-modal');
            modal.addEventListener('click', function (e) { e.stopPropagation(); });

            // Header
            var header = el('div', 'ww-modal-header');
            var title = formatLabel(
                preview.isUpgrade ? text.upgradeTitle : text.downgradeTitle,
                'tier',
                preview.newTierName || '');
            header.appendChild(el('h3', 'ww-modal-title', title));
            var closeBtn = el('button', 'ww-modal-close', '×');
            closeBtn.type = 'button';
            closeBtn.setAttribute('aria-label', text.close);
            closeBtn.disabled = state.loading;
            closeBtn.addEventListener('click', close);
            header.appendChild(closeBtn);
            modal.appendChild(header);

            // Body
            var body = el('div', 'ww-modal-body');

            var comparison = el('div', 'ww-tier-change-comparison');
            var curPlan = el('div', 'ww-tier-change-plan');
            curPlan.appendChild(el('span', 'ww-tier-change-plan-label', text.currentLabel));
            curPlan.appendChild(el('span', 'ww-tier-change-plan-name', preview.currentTierName || text.noPlan));
            if (preview.currentPrice !== null && preview.currentPrice !== undefined) {
                curPlan.appendChild(el('span', 'ww-tier-change-plan-price',
                    money(preview.currentPrice, ccy) + '/' + (preview.currentBillingFrequency || 'mo').toLowerCase()));
            }
            comparison.appendChild(curPlan);
            comparison.appendChild(el('span', 'ww-tier-change-arrow', '→'));
            var newPlan = el('div', 'ww-tier-change-plan');
            newPlan.appendChild(el('span', 'ww-tier-change-plan-label', text.newLabel));
            newPlan.appendChild(el('span', 'ww-tier-change-plan-name', preview.newTierName || text.unknownPlan));
            if (preview.newPrice !== null && preview.newPrice !== undefined) {
                newPlan.appendChild(el('span', 'ww-tier-change-plan-price',
                    money(preview.newPrice, ccy) + '/' + (preview.newBillingFrequency || 'mo').toLowerCase()));
            }
            comparison.appendChild(newPlan);
            body.appendChild(comparison);

            // Billing frequency savings
            if (preview.isBillingFrequencyChange
                && preview.monthlyEquivalentCurrent != null
                && preview.monthlyEquivalentNew != null
                && preview.monthlyEquivalentNew < preview.monthlyEquivalentCurrent) {
                var pct = Math.round(
                    ((preview.monthlyEquivalentCurrent - preview.monthlyEquivalentNew)
                        / preview.monthlyEquivalentCurrent) * 100);
                var savings = formatLabel(text.savings, 'percent', pct);
                savings = formatLabel(savings, 'amount', money(preview.monthlyEquivalentNew, ccy));
                savings = formatLabel(savings, 'frequency', (preview.newBillingFrequency || '').toLowerCase());
                body.appendChild(el('div', 'ww-tier-change-savings', savings));
            }

            // Proration for upgrades
            if (preview.isUpgrade && effectivePaymentRequired && preview.proratedChargeToday != null) {
                var charge = el('div', 'ww-tier-change-charge');
                charge.appendChild(el('div', 'ww-tier-change-charge-header', text.todaysCharge));

                if (preview.creditAmount != null && preview.creditAmount > 0) {
                    var creditText = formatLabel(text.creditLine, 'days', preview.daysRemainingInPeriod);
                    creditText = formatLabel(creditText, 'tier', preview.currentTierName || '');
                    var creditLine = el('div', 'ww-tier-change-line-item');
                    creditLine.appendChild(el('span', null, creditText));
                    creditLine.appendChild(el('span', 'ww-tier-change-credit', '-' + money(preview.creditAmount, ccy)));
                    charge.appendChild(creditLine);
                }

                var newText = formatLabel(text.newPlanLine, 'tier', preview.newTierName || '');
                newText = formatLabel(newText, 'days', preview.daysRemainingInPeriod);
                var newLine = el('div', 'ww-tier-change-line-item');
                newLine.appendChild(el('span', null, newText));
                newLine.appendChild(el('span', null,
                    money((preview.proratedChargeToday || 0) + (preview.creditAmount || 0), ccy)));
                charge.appendChild(newLine);

                var total = el('div', 'ww-tier-change-total');
                total.appendChild(el('span', null, text.netCharge));
                total.appendChild(el('span', null, money(preview.proratedChargeToday, ccy)));
                charge.appendChild(total);

                if (preview.nextBillingDate) {
                    var next = formatLabel(text.nextBilling, 'date',
                        new Date(preview.nextBillingDate).toLocaleDateString());
                    next = formatLabel(next, 'amount', money(preview.nextBillingAmount, ccy));
                    next = formatLabel(next, 'frequency', (preview.newBillingFrequency || 'mo').toLowerCase());
                    charge.appendChild(el('div', 'ww-tier-change-next-billing', next));
                }

                body.appendChild(charge);
            }

            // Downgrade credit
            if (preview.isDowngrade && preview.creditAmount != null && preview.creditAmount > 0) {
                body.appendChild(el('div', 'ww-tier-change-credit-info',
                    formatLabel(text.downgradeCredit, 'amount', money(preview.creditAmount, ccy))));
            }

            if (preview.featuresGained && preview.featuresGained.length > 0) {
                body.appendChild(buildFeatureList(text.featuresGained, preview.featuresGained, true));
            }
            if (preview.featuresLost && preview.featuresLost.length > 0) {
                body.appendChild(buildFeatureList(text.featuresLost, preview.featuresLost, false));
            }

            // Downgrade timing choice
            if (preview.isDowngrade && preview.allowScheduledChange) {
                var timing = el('div', 'ww-tier-change-timing');
                timing.appendChild(el('div', 'ww-tier-change-timing-label', text.timingQuestion));

                var endLabel = preview.nextBillingDate
                    ? formatLabel(text.endOfPeriodDated, 'date', new Date(preview.nextBillingDate).toLocaleDateString())
                    : text.endOfPeriod;
                var creditNote = (preview.creditAmount != null && preview.creditAmount > 0)
                    ? ' ' + formatLabel(text.creditNote, 'amount', money(preview.creditAmount, ccy))
                    : '';

                timing.appendChild(buildTimingOption(
                    endLabel,
                    formatLabel(text.keepUntilThen, 'tier', preview.currentTierName || '') + creditNote,
                    !state.immediate,
                    function () { state.immediate = false; render(); }));

                timing.appendChild(buildTimingOption(
                    text.immediately,
                    formatLabel(text.switchNow, 'tier', preview.newTierName || '') + creditNote,
                    state.immediate,
                    function () { state.immediate = true; render(); }));

                body.appendChild(timing);
            }

            // No payment provider warning
            if (effectivePaymentRequired && !preview.paymentProviderAvailable && !preview.paymentBypassAllowed) {
                body.appendChild(el('div', 'ww-alert ww-alert-warning', text.noProvider));
            }

            // Admin bypass toggle
            if (showPaymentBypass) {
                var bypassLabel = el('label', 'ww-tier-change-bypass');
                var bypassInput = document.createElement('input');
                bypassInput.type = 'checkbox';
                bypassInput.checked = state.bypassPayment;
                bypassInput.disabled = state.loading;
                bypassInput.addEventListener('change', function () {
                    state.bypassPayment = bypassInput.checked;
                    render();
                });
                bypassLabel.appendChild(bypassInput);
                bypassLabel.appendChild(el('span', null, text.bypass));
                body.appendChild(bypassLabel);
            }

            modal.appendChild(body);

            // Footer
            var footer = el('div', 'ww-modal-footer');
            var cancelBtn = el('button', 'ww-btn ww-btn-outline btn btn-outline-secondary', text.keepPlan);
            cancelBtn.type = 'button';
            cancelBtn.disabled = state.loading;
            cancelBtn.addEventListener('click', close);
            footer.appendChild(cancelBtn);

            var confirmLabel = state.loading
                ? text.processing
                : state.bypassPayment
                    ? text.applyNoCharge
                    : (preview.isUpgrade && preview.proratedChargeToday)
                        ? formatLabel(text.upgradeFor, 'amount', money(preview.proratedChargeToday, ccy))
                        : preview.isDowngrade
                            ? text.confirmDowngrade
                            : formatLabel(text.switchTo, 'tier', preview.newTierName || '');

            var confirmBtn = el(
                'button',
                'ww-btn btn ' + (preview.isUpgrade ? 'ww-btn-primary btn-primary' : 'ww-btn-outline btn-outline-primary'),
                confirmLabel);
            confirmBtn.type = 'button';
            confirmBtn.disabled = state.loading
                || (effectivePaymentRequired && !preview.paymentProviderAvailable && !preview.paymentBypassAllowed);
            confirmBtn.addEventListener('click', submit);
            footer.appendChild(confirmBtn);

            modal.appendChild(footer);
            overlay.appendChild(modal);
        }

        render();
        container.appendChild(overlay);

        return {
            /** Keeps the modal on screen while the change runs, with its buttons disabled. */
            setBusy: function (busy) {
                if (state.loading === busy) return;
                state.loading = busy;
                render();
            },

            /** Takes the modal down WITHOUT calling back - the driver already knows. */
            dismiss: function () {
                if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
            }
        };
    }

    // ===== The driver =============================================================================

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

    function delay(ms) {
        return new Promise(function (resolve) { setTimeout(resolve, ms); });
    }

    /**
     * One plan-change flow.
     *
     * @param options.machines    the reducers (defaults to window.wwRegSubMachines)
     * @param options.appId       the app whose subscription is changing
     * @param options.userId      set for an admin acting on one user
     * @param options.companyId   set for an admin acting on a company
     * @param options.isCompanyMode whether the app tracks subscriptions per company
     * @param options.currency    display currency for the confirmation
     * @param options.labels      the five planChange* refusal sentences, server-rendered
     * @param options.text        confirmation-modal copy overrides
     * @param options.container   where the confirmation overlay is appended
     * @param options.hostPost    post(path, body) against the HOST app-tier proxy
     * @param options.regsubPost  post(path, body) against the SHIPPED proxy
     * @param options.publishableKey Stripe key for the 3-D Secure confirmation
     * @param options.onState     onState(view) after every change - render from it
     * @param options.onError     onError(code, message), once per failure
     * @param options.onChanged   onChanged() when a change has landed
     * @param options.onEntitlements onEntitlements('tierChange') after onChanged
     * @param options.onPaymentRequired onPaymentRequired(detail) - a card this package will not ask for
     * @param options.confirmSubscribe confirmSubscribe(ctx) -> bool, for a NEW subscription
     */
    function create(options) {
        var settings = options || {};
        var machines = settings.machines || (typeof window !== 'undefined' ? window.wwRegSubMachines : null);
        if (!machines) return null;

        var appId = settings.appId || '';
        var scope = resolveScope(settings);
        var labels = settings.labels || {};
        var text = {};
        for (var key in DEFAULT_TEXT) {
            if (Object.prototype.hasOwnProperty.call(DEFAULT_TEXT, key)) text[key] = DEFAULT_TEXT[key];
        }
        if (settings.text) {
            for (var override in settings.text) {
                if (Object.prototype.hasOwnProperty.call(settings.text, override)
                    && settings.text[override]) {
                    text[override] = settings.text[override];
                }
            }
        }

        var state = machines.initialPlanChangeState({ appId: appId });
        var selection = null;
        var runs = {};
        var modal = null;
        var stripe = null;
        var detached = false;
        var pumping = false;
        var doneHandled = false;
        var failureReported = false;
        var paymentAsked = false;

        function post(request) {
            return request.transport === 'regsub'
                ? settings.regsubPost(request.path, request.body)
                : settings.hostPost(request.path, request.body);
        }

        function busy() {
            return state.step === 'previewing'
                || state.step === 'changing'
                || state.step === 'authenticating'
                || state.step === 'completing';
        }

        /** Why the change stopped, in words. Null unless the flow failed. */
        function errorText() {
            if (state.step !== 'failed') return null;
            if (isNoPaymentMethodCode(state.errorCode)) {
                return paymentRequiredMessage(state.error, text.paymentMethodRequired);
            }
            return messageForCode(labels, state.errorCode, state.error);
        }

        function view() {
            return {
                step: state.step,
                busy: busy(),
                error: errorText(),
                errorCode: state.errorCode || null,
                canRetry: canRetry(state),
                preview: state.step === 'confirm' ? state.preview : null
            };
        }

        function notify() {
            if (detached) return;
            if (typeof settings.onState === 'function') settings.onState(view());
        }

        function report(code, message) {
            if (typeof settings.onError === 'function') settings.onError(code, message);
        }

        function apply(event) {
            var next = machines.planChangeTransition(state, event);
            if (next === state) return false;

            state = next;

            // The steps the machine does not tokenise are claimed once PER ENTRY, so leaving one
            // arms it again.
            if (state.step !== 'collectingPayment') paymentAsked = false;
            if (state.step !== 'done') doneHandled = false;
            if (state.step !== 'failed') failureReported = false;

            syncModal();
            return true;
        }

        /** The confirmation is mounted while the change is being decided, and taken down after. */
        function syncModal() {
            var wanted = state.step === 'confirm'
                || state.step === 'changing'
                || state.step === 'authenticating'
                || state.step === 'completing';

            if (!wanted || detached) {
                if (modal) { modal.dismiss(); modal = null; }
                return;
            }

            if (state.step === 'confirm' && !modal && state.preview && settings.container) {
                modal = openTierChangeModal(settings.container, state.preview, {
                    currency: settings.currency,
                    text: text,
                    onConfirm: function (chosen) { confirm(chosen); },
                    onClose: function () { modal = null; cancel(); }
                });
                return;
            }

            if (modal) modal.setBusy(busy());
        }

        function dispatch(event) {
            // Every dispatch comes from the SURFACE (a plan click, a confirmation, a retry).
            // Detached, there is no surface, so nothing can raise one; a step's own result is
            // applied straight onto the state instead.
            if (detached) return Promise.resolve();

            apply(event);
            if (pumping) return Promise.resolve();
            return pump();
        }

        function pump() {
            pumping = true;

            return (function step(iteration) {
                if (iteration >= MAX_PUMP_ITERATIONS || !mayRunNextStep()) return Promise.resolve();

                notify();

                return Promise.resolve(runStepWork()).then(function (moved) {
                    if (!moved) return null;
                    return step(iteration + 1);
                });
            })(0).catch(function (error) {
                // A drain outlives the surface, so an escaping throw would be an unhandled
                // rejection nobody sees.
                if (window.console) window.console.error('[wwRegSubPlanChange] ' + (error && error.message));
            }).then(function () {
                pumping = false;
                notify();
            });
        }

        /**
         * Whether the step the machine has landed on may run now. Attached, always. Detached, only
         * the two steps that are pure consequences of money that has ALREADY moved: completing (the
         * bank said yes to the prorated charge, so skipping it is how a customer pays for an
         * upgrade they never get) and done.
         */
        function mayRunNextStep() {
            if (!detached) return true;
            return state.step === 'completing' || state.step === 'done';
        }

        /** One run per step token: a doubled dispatch carries a token already claimed. */
        function claim(key, token) {
            if (!token) return false;
            if (runs[key] === token) return false;
            runs[key] = token;
            return true;
        }

        function runStepWork() {
            switch (state.step) {
                case 'previewing':
                    if (!claim('preview', state.token)) return false;
                    return runPreview(state.token);

                case 'collectingPayment':
                    if (paymentAsked) return false;
                    paymentAsked = true;
                    return refusePaymentCollection();

                case 'changing':
                    if (!claim('change', state.token)) return false;
                    return runChange(state.token, state.immediate, state.paymentTransactionId);

                case 'authenticating':
                    if (!claim('authenticate', state.token)) return false;
                    return runAuthenticate(state.token, state.clientSecret);

                case 'completing':
                    if (!claim('complete', state.token)) return false;
                    return runComplete(state.token, state.pendingChangeId, state.completeAttempts);

                case 'done':
                    if (doneHandled) return false;
                    doneHandled = true;
                    return runDone();

                case 'failed':
                    if (failureReported) return false;
                    failureReported = true;
                    report(codeForFailure(state), errorText() || '');
                    return false;

                default:
                    return false;
            }
        }

        function runPreview(token) {
            var chosen = selection;
            if (!chosen) return false;

            return post(previewRequest(scope, appId, settings.userId, chosen))
                .then(function (preview) {
                    if (!preview || preview.success === false) {
                        apply({
                            type: 'PREVIEW_FAILED',
                            token: token,
                            message: (preview && preview.errorMessage) || PREVIEW_REFUSED
                        });
                        return true;
                    }
                    apply({ type: 'PREVIEW_RECEIVED', token: token, preview: preview });
                    return true;
                })
                .catch(function (error) {
                    apply({
                        type: 'PREVIEW_FAILED',
                        token: token,
                        message: (error && error.message) || PREVIEW_REFUSED
                    });
                    return true;
                });
        }

        /** Hands the host everything it needs to collect a card for this change itself. */
        function askForPayment() {
            if (typeof settings.onPaymentRequired !== 'function') return;
            settings.onPaymentRequired(paymentRequiredDetail(selection || {}, state.preview, settings.currency));
        }

        /**
         * A stall guard, not a step this driver uses. `confirm` always names collectPayment (false,
         * per razorNeedsPaymentCollection), so nothing here reaches `collectingPayment` - but the
         * reducer can still land on it from a CONFIRMED that names nothing, and a machine parked on
         * a step no one will ever answer is a hung modal. So it fails out the same way a refused
         * change does: the host is asked for a card and the flow stops.
         */
        function refusePaymentCollection() {
            askForPayment();
            apply({ type: 'PAYMENT_FAILED', message: text.paymentMethodRequired });
            return true;
        }

        function runChange(token, immediate, paymentTransactionId) {
            var chosen = selection;
            if (!chosen) return false;

            var request = changeRequest(
                scope, appId, settings.userId, settings.companyId, chosen, immediate, paymentTransactionId);

            return post(request)
                .then(function (result) {
                    var answer = result || {};

                    // The change WAS put to the server and the server says there is nothing to
                    // charge. That - and only that, by code - is the payment-required path: the
                    // host is handed what a payment component of its own would need. Once per
                    // posted change, because one step token buys one run of this step.
                    if (isNoPaymentMethodRefusal(answer)) askForPayment();

                    apply({ type: 'CHANGE_RESULT', token: token, result: answer });
                    return true;
                })
                .catch(function (error) {
                    apply({
                        type: 'CHANGE_FAILED',
                        token: token,
                        message: (error && error.message) || CHANGE_REFUSED
                    });
                    return true;
                });
        }

        /**
         * Puts the prorated charge's 3-D Secure to the customer, on the card ALREADY ON FILE. No
         * card form is involved, which is why this one step is Razor's, and the parked change is
         * completed on the other side of it.
         */
        function runAuthenticate(token, clientSecret) {
            if (!clientSecret || !settings.publishableKey) {
                apply({ type: 'AUTH_FAILED', token: token, message: CHARGE_UNCONFIRMED });
                return Promise.resolve(true);
            }

            return loadScript(STRIPE_SRC)
                .then(function () {
                    if (typeof window.Stripe === 'undefined') throw new Error(CHARGE_UNCONFIRMED);
                    if (!stripe) stripe = window.Stripe(settings.publishableKey);
                    return stripe.confirmCardPayment(clientSecret);
                })
                .then(function (answer) {
                    if (answer && answer.error) {
                        apply({
                            type: 'AUTH_FAILED',
                            token: token,
                            message: answer.error.message || CHARGE_UNCONFIRMED
                        });
                        return true;
                    }
                    apply({ type: 'AUTHENTICATED', token: token });
                    return true;
                })
                .catch(function (error) {
                    apply({
                        type: 'AUTH_FAILED',
                        token: token,
                        message: (error && error.message) || CHARGE_UNCONFIRMED
                    });
                    return true;
                });
        }

        /**
         * Finishes the parked change. Safe to repeat - the server asks the processor whether the
         * invoice really paid before it moves anything - so a `processing` answer is asked again
         * rather than reported, on the machine's bounded budget.
         */
        function runComplete(token, pendingChangeId, attempt) {
            if (!pendingChangeId) {
                apply({
                    type: 'COMPLETE_FAILED',
                    token: token,
                    message: labels.planChangeNotFound || COMPLETION_REFUSED
                });
                return Promise.resolve(true);
            }

            // Only a "processing" answer waits: the first attempt asks immediately.
            var wait = attempt > 0 ? delay(COMPLETE_RETRY_DELAY_MS) : Promise.resolve();

            return wait
                .then(function () { return settings.regsubPost(completePath(pendingChangeId), {}); })
                .then(function (result) {
                    apply({ type: 'COMPLETE_RESULT', token: token, result: result || {} });
                    return true;
                })
                .catch(function (error) {
                    apply({
                        type: 'COMPLETE_FAILED',
                        token: token,
                        message: (error && error.message) || COMPLETION_REFUSED
                    });
                    return true;
                });
        }

        function runDone() {
            var changed = typeof settings.onChanged === 'function'
                ? Promise.resolve(settings.onChanged())
                : Promise.resolve();

            return changed
                .catch(function () { /* the host's own refresh failing is not this flow's failure */ })
                .then(function () {
                    if (typeof settings.onEntitlements === 'function') settings.onEntitlements('tierChange');
                    return false;
                });
        }

        // ----- What the surface calls -------------------------------------------------------------

        /**
         * A plan was clicked. A CHANGE is priced and confirmed; a first subscription is not a
         * change, has nothing to prorate and cannot be parked on a bank challenge, so it takes the
         * caller's own confirmation and goes straight out.
         */
        function selectTier(ctx) {
            if (!ctx) return Promise.resolve();

            selection = ctx;

            if (!ctx.isChange) return subscribe(ctx);

            return dispatch({
                type: 'PREVIEW_REQUESTED',
                appId: appId,
                tierId: ctx.tierId,
                pricingId: ctx.pricingId
            });
        }

        /** A first subscription: confirmed by the caller, posted once, then the surface refreshes. */
        function subscribe(ctx) {
            if (typeof settings.confirmSubscribe === 'function' && !settings.confirmSubscribe(ctx)) {
                return Promise.resolve();
            }

            var request = changeRequest(scope, appId, settings.userId, settings.companyId, ctx, true, null);

            return post(request)
                .then(function (result) {
                    if (result && result.success === false) {
                        report(CODE_CHANGE_FAILED, result.errorMessage || CHANGE_REFUSED);
                        return null;
                    }
                    return runDone();
                })
                .catch(function (error) {
                    report(CODE_CHANGE_FAILED, (error && error.message) || CHANGE_REFUSED);
                    return null;
                });
        }

        /**
         * The customer said yes. The change is POSTED - in every scope, at every price, bypass
         * ticked or not - because Razor collects no card and the server settles the money against
         * the admin override or the card on file. The bypass toggle is what it has always been
         * here: an admin's view of the confirmation (what it charges, what the button says), not a
         * wire field - the old subscription-admin.js never sent one either.
         */
        function confirm(chosen) {
            return dispatch(confirmEvent(chosen));
        }

        function cancel() {
            return dispatch({ type: 'RESET' });
        }

        function retry() {
            return dispatch({ type: 'RETRY' });
        }

        return {
            selectTier: selectTier,
            confirm: confirm,
            cancel: cancel,
            retry: retry,
            reset: cancel,

            getState: function () { return state; },
            getView: view,

            /**
             * DETACHES the flow: no re-render, no host callback, and no new step that needs a
             * person. A change the bank has already authenticated still drains through its
             * completion - see mayRunNextStep.
             */
            destroy: function () {
                if (detached) return;
                detached = true;
                if (modal) { modal.dismiss(); modal = null; }
            }
        };
    }

    var api = {
        create: create,
        openTierChangeModal: openTierChangeModal,

        // Pure, and exported so the Node self-test can cover them.
        resolveScope: resolveScope,
        isAdminScoped: isAdminScoped,
        razorNeedsPaymentCollection: razorNeedsPaymentCollection,
        confirmEvent: confirmEvent,
        isNoPaymentMethodCode: isNoPaymentMethodCode,
        isNoPaymentMethodRefusal: isNoPaymentMethodRefusal,
        paymentRequiredMessage: paymentRequiredMessage,
        previewRequest: previewRequest,
        changeRequest: changeRequest,
        completePath: completePath,
        messageForCode: messageForCode,
        codeForFailure: codeForFailure,
        canRetry: canRetry,
        paymentRequiredDetail: paymentRequiredDetail,
        formatMoney: wwFormatMoney,
        formatLabel: formatLabel,

        DEFAULT_TEXT: DEFAULT_TEXT,
        ERROR_CODES: ERROR_CODES,
        NO_PAYMENT_METHOD_CODES: NO_PAYMENT_METHOD_CODES,
        COMPLETE_RETRY_DELAY_MS: COMPLETE_RETRY_DELAY_MS,
        CODE_PREVIEW_FAILED: CODE_PREVIEW_FAILED,
        CODE_AUTH_FAILED: CODE_AUTH_FAILED,
        CODE_COMPLETION_FAILED: CODE_COMPLETION_FAILED,
        CODE_CHANGE_FAILED: CODE_CHANGE_FAILED,
        CHARGE_UNCONFIRMED: CHARGE_UNCONFIRMED,
        CHANGE_REFUSED: CHANGE_REFUSED,
        PREVIEW_REFUSED: PREVIEW_REFUSED
    };

    if (typeof window !== 'undefined') {
        window.wwRegSubPlanChange = api;
    }

    // The self-test's hook. Guarded so the browser build never sees it.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = api;
    }
})();
