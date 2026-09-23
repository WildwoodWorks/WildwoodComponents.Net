/*
 * WildwoodComponents.Razor - Registration & Subscription: pricing view
 *
 * The client half of <vc:registration-subscription-pricing />. Classic script, no modules, no
 * build step, matching every other file in this folder.
 *
 * What it does NOT do is the point. The catalog was read on the server and both billing cycles'
 * prices were formatted there, so this file never fetches a catalog, never formats money and never
 * writes a price. It swaps which of two server-rendered price blocks is hidden, tracks a pack
 * basket, and raises one event.
 *
 * The event: `ww-regsub-select`, bubbling and CANCELABLE, with
 *   detail = { tierId, pricingId, billing, addOnIds }
 * — the Razor analog of React's `onSelect`. A listener that calls preventDefault() stops the
 * navigation and keeps the choice; otherwise, when the component was given a `select-url`, the
 * browser goes there with `tier`, `pricing` and `addons` in the query. The host's own query
 * parameters were already merged server-side, so all this file does is append.
 *
 * Retry (shown only when the catalog could not be read) reloads the page. The component is
 * server-rendered, so the server is the single source of truth for its markup; and a failed
 * catalog load is never cached, so the reload really does ask again. This mirrors how
 * subscription-admin.js refreshes its panels.
 *
 * No English lives here: the "Continue with N packs" wording comes from `{count}` templates the
 * server rendered onto the root element.
 */
(function () {
    'use strict';

    var instances = {};
    var SELECT_EVENT = 'ww-regsub-select';
    var DEFAULT_PACK_MAX = 25;

    // ===== Pure decisions (no DOM, no state) =====================================================

    /** Fills the one {count} slot in a server-rendered label template. */
    function formatCount(template, count) {
        if (!template) return '';
        return String(template).split('{count}').join(String(count));
    }

    /** The Continue button's wording: the singular template at exactly one pack. */
    function continueLabel(count, oneTemplate, manyTemplate) {
        if (count === 1 && oneTemplate) return oneTemplate;
        return formatCount(manyTemplate, count);
    }

    /**
     * A basket in CATALOG order rather than click order, so what leaves here reads the same as the
     * grid it was picked from and matches a basket built from a link.
     */
    function orderSelection(order, selected) {
        var ordered = [];
        for (var i = 0; i < order.length; i++) {
            if (selected.indexOf(order[i]) !== -1) ordered.push(order[i]);
        }
        return ordered;
    }

    /**
     * Tick or untick one pack. The cap is the server's (25): a signup link is not a shopping cart.
     * Unticking is always allowed, cap or no cap.
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

    /**
     * application/x-www-form-urlencoded, matching the QueryParameters encoder the .NET side writes
     * links with: a space is '+', everything else goes through encodeURIComponent.
     */
    function encodeFormValue(value) {
        return encodeURIComponent(String(value)).split('%20').join('+');
    }

    /**
     * The selection link. `query` already has the host's own parameters with tier/pricing/addons
     * stripped out server-side, so this only ever appends.
     */
    function buildSelectUrl(parts, selection) {
        var query = parts.query || '';
        var appended = [];

        if (selection.tierId) appended.push('tier=' + encodeFormValue(selection.tierId));
        if (selection.pricingId) appended.push('pricing=' + encodeFormValue(selection.pricingId));
        if (selection.addOnIds && selection.addOnIds.length > 0) {
            appended.push('addons=' + encodeFormValue(selection.addOnIds.join(',')));
        }

        if (appended.length > 0) {
            var tail = appended.join('&');
            query = query.length > 0 ? query + '&' + tail : tail;
        }

        return (parts.path || '') + (query.length > 0 ? '?' + query : '') + (parts.hash || '');
    }

    /** The ids the server listed, in catalog order. */
    function parsePackOrder(value) {
        var order = [];
        if (!value) return order;

        var parts = String(value).split(',');
        for (var i = 0; i < parts.length; i++) {
            var id = parts[i].trim();
            if (id.length > 0) order.push(id);
        }
        return order;
    }

    /** A positive integer attribute, or the shipped default when the attribute is missing or junk. */
    function parseMax(value) {
        var parsed = parseInt(value, 10);
        return isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_PACK_MAX;
    }

    // ===== Instance ==============================================================================

    function initInstance(root) {
        if (!root) return null;

        var cid = root.getAttribute('data-component-id');
        if (!cid || instances[cid]) return instances[cid] || null;

        var packOrder = parsePackOrder(root.getAttribute('data-ww-pack-order'));
        var packMax = parseMax(root.getAttribute('data-ww-pack-max'));
        var labelOne = root.getAttribute('data-ww-packs-label-one') || '';
        var labelMany = root.getAttribute('data-ww-packs-label-many') || '';
        var hasSelectUrl = root.hasAttribute('data-ww-select-path');
        var selectParts = {
            path: root.getAttribute('data-ww-select-path') || '',
            query: root.getAttribute('data-ww-select-query') || '',
            hash: root.getAttribute('data-ww-select-hash') || ''
        };

        var billing = root.getAttribute('data-ww-billing') === 'annual' ? 'annual' : 'monthly';
        var selected = [];

        // ----- Billing toggle: show one of the two server-rendered blocks, format nothing --------

        function applyBilling(next) {
            billing = next === 'annual' ? 'annual' : 'monthly';
            root.setAttribute('data-ww-billing', billing);

            var blocks = root.querySelectorAll('[data-ww-billing]');
            for (var i = 0; i < blocks.length; i++) {
                var block = blocks[i];
                if (block === root) continue;
                block.hidden = block.getAttribute('data-ww-billing') !== billing;
            }

            var captions = root.querySelectorAll('[data-ww-billing-label]');
            for (var j = 0; j < captions.length; j++) {
                var caption = captions[j];
                var active = caption.getAttribute('data-ww-billing-label') === billing;
                caption.classList.toggle('ww-billing-active', active);
            }

            var toggle = root.querySelector('[data-ww-action="toggle-billing"]');
            if (toggle) {
                var isAnnual = billing === 'annual';
                toggle.classList.toggle('ww-toggle-on', isAnnual);
                toggle.setAttribute('aria-pressed', isAnnual ? 'true' : 'false');
            }
        }

        // ----- Pack basket ----------------------------------------------------------------------

        function paintPacks() {
            var cards = root.querySelectorAll('[data-ww-pack]');
            for (var i = 0; i < cards.length; i++) {
                var card = cards[i];
                var isSelected = selected.indexOf(card.getAttribute('data-ww-pack')) !== -1;
                card.classList.toggle('ww-pack-card--selected', isSelected);
                if (card.hasAttribute('aria-pressed')) {
                    card.setAttribute('aria-pressed', isSelected ? 'true' : 'false');
                }
            }

            var summary = root.querySelector('[data-ww-action="continue-packs"]');
            if (summary) {
                summary.textContent = continueLabel(selected.length, labelOne, labelMany);
                summary.disabled = selected.length === 0;
            }
        }

        // ----- Selection --------------------------------------------------------------------

        function raiseSelect(selection) {
            var event;
            try {
                event = new CustomEvent(SELECT_EVENT, {
                    bubbles: true,
                    cancelable: true,
                    detail: selection
                });
            } catch (e) {
                // Very old browsers: no constructor. Nothing can be cancelled, so nothing navigates.
                return;
            }

            var proceed = root.dispatchEvent(event);
            if (!proceed || !hasSelectUrl) return;

            window.location.assign(buildSelectUrl(selectParts, selection));
        }

        function selectTier(card) {
            var pricingId = card.getAttribute(
                billing === 'annual' ? 'data-ww-pricing-annual' : 'data-ww-pricing-monthly');

            raiseSelect({
                tierId: card.getAttribute('data-ww-tier') || null,
                pricingId: pricingId && pricingId.length > 0 ? pricingId : null,
                billing: billing,
                // A plan's call to action carries whatever packs are ticked, so one click buys the
                // whole basket.
                addOnIds: selected.slice()
            });
        }

        function choosePack(addOnId) {
            raiseSelect({ tierId: null, pricingId: null, billing: billing, addOnIds: [addOnId] });
        }

        function continueWithPacks() {
            if (selected.length === 0) return;
            raiseSelect({ tierId: null, pricingId: null, billing: billing, addOnIds: selected.slice() });
        }

        // ----- Wiring -----------------------------------------------------------------------

        root.addEventListener('click', function (e) {
            var trigger = e.target.closest ? e.target.closest('[data-ww-action]') : null;
            if (!trigger || !root.contains(trigger)) return;

            var action = trigger.getAttribute('data-ww-action');

            if (action === 'retry') {
                window.location.reload();
                return;
            }

            if (action === 'toggle-billing') {
                applyBilling(billing === 'annual' ? 'monthly' : 'annual');
                return;
            }

            if (action === 'select-tier') {
                var card = trigger.closest('[data-ww-tier]');
                if (card) selectTier(card);
                return;
            }

            if (action === 'toggle-pack') {
                var toggled = trigger.closest('[data-ww-pack]');
                if (!toggled) return;
                selected = toggleSelection(packOrder, selected, toggled.getAttribute('data-ww-pack'), packMax);
                paintPacks();
                return;
            }

            if (action === 'choose-pack') {
                var chosen = trigger.closest('[data-ww-pack]');
                if (chosen) choosePack(chosen.getAttribute('data-ww-pack'));
                return;
            }

            if (action === 'continue-packs') {
                continueWithPacks();
            }
        });

        paintPacks();

        instances[cid] = {
            root: root,
            getBilling: function () { return billing; },
            setBilling: applyBilling,
            getSelectedPackIds: function () { return selected.slice(); }
        };

        return instances[cid];
    }

    // ===== AUTO-INIT =============================================================================

    function initAll() {
        var roots = document.querySelectorAll('[data-ww-view="pricing"]');
        for (var i = 0; i < roots.length; i++) {
            initInstance(roots[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAll);
    } else {
        initAll();
    }

    window.wwRegSubPricing = {
        init: initInstance,
        initAll: initAll,
        getInstance: function (componentId) {
            return instances[componentId] || null;
        },
        // Exposed for hosts building their own links; the same rules the clicks use.
        buildSelectUrl: buildSelectUrl,
        continueLabel: continueLabel
    };
})();
