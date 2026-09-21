/**
 * WildwoodComponents.Razor - Consent banner & preferences modal.
 *
 * Vanilla (non-ESM) IIFE port of @wildwood/core's ConsentService and the Blazor
 * wwwroot/js/wildwood-consent.js engine. Auto-initializes every .ww-consent-component on the page.
 * Owns the browser-only concerns: fetch the merged config + script registry, read/write the
 * first-party consent cookie, read GPC, apply the show/suppress decision table, and inject scripts
 * BY CATEGORY only after consent. Block-before-consent: no gated script loads until its category is
 * consented to. StrictlyNecessary may load immediately.
 *
 * Razor Pages equivalent of the Blazor ConsentBanner component.
 */
(function () {
    'use strict';

    var NON_NECESSARY = ['Functional', 'Analytics', 'Advertising', 'Sensitive'];
    var GPC_FORCED_OFF = ['Advertising', 'Sensitive'];

    // Registered component instances, exposed via window.wildwoodConsent for host-app control
    // (reopen preferences, withdraw, gate features on isGranted). Shared on `window` so that multiple
    // <script src> includes (one per ViewComponent instance) all register into the SAME registry —
    // otherwise each IIFE run gets its own empty array and the last one to define window.wildwoodConsent
    // (whose initConsent calls all short-circuit on the init guard) would expose an empty registry.
    var instances = (typeof window !== 'undefined')
        ? (window.__wildwoodConsentInstances = window.__wildwoodConsentInstances || [])
        : [];

    function emptyCategories() {
        return { StrictlyNecessary: true, Functional: false, Analytics: false, Advertising: false, Sensitive: false };
    }

    // ===== Keeping the fixed banner off the page's own content ================
    //
    // The banner is `position: fixed` against an edge of the viewport with a very high z-index, so
    // anything the host anchors to that edge sits underneath it. It was found in the React package
    // covering the send button of a host's AI assistant: the button was visible and enabled, and
    // clicking it did nothing, because the banner was taking the pointer events.
    //
    // The host cannot leave room for it on its own - the height depends on the configured copy and
    // on how that copy wraps - so the banner measures itself, publishes `--ww-consent-height`, and
    // by default adds its height to the page's padding on the edge it is anchored to. All of it is
    // undone when the banner goes. Ported from @wildwood/react's ConsentBanner `reserveSpace`
    // effect; which edge - and whether there is one at all - is a decision only .NET has to make,
    // because this banner has three positions (bottomBar, topBar, corner) where React's has one.
    //
    // The bookkeeping between those two paragraphs is not one line, so it is kept as pure
    // functions with no DOM in them, LINE FOR LINE IDENTICAL to the copy in
    // WildwoodComponents.Blazor/wwwroot/js/wildwood-consent.js (an ES module, which cannot share
    // this file's CommonJS hook). This copy is the one Node can require, so it carries the
    // self-test - WildwoodComponents.Tests/Razor/js/consent-reserve-space.selftest.mjs - and
    // AttributionConsentGateSourceTests asserts the two are the same text. Change one, change both.

    var CONSENT_HEIGHT_VAR = '--ww-consent-height';
    var SPACE_KEY_ATTR = 'data-ww-consent-space';

    // ---- BEGIN shared reserve-space bookkeeping ----
    /**
     * The padding property a banner in this position occupies, or null when it occupies none.
     *
     * Only the two BARS push the page around: each spans the full width against an edge, so anything
     * the page anchors there ends up underneath it. A `corner` card is a ~420px box inset from the
     * bottom-right, and padding the whole page for it would leave a full-width blank strip below the
     * content for as long as the banner is up - a worse bug than the one this fixes. So the corner,
     * and any position this script does not recognise, is measured and published but never padded. A
     * host that opted out is in the same place: it asked to place the room itself.
     */
    function reservedEdge(position, reserve) {
        if (reserve === false) return null;
        if (position === 'topBar') return 'paddingTop';
        if (position === 'bottomBar') return 'paddingBottom';
        return null;
    }

    /** The CSS property behind a style-object edge, for removing the declaration outright. */
    function edgeProperty(edge) {
        return edge === 'paddingTop' ? 'padding-top' : 'padding-bottom';
    }

    /** The position class the banner carries, as the bare position name. */
    function positionOf(el) {
        var classes = (el && el.classList) || [];
        for (var i = 0; i < classes.length; i++) {
            if (classes[i].indexOf('ww-consent-pos-') === 0) return classes[i].slice('ww-consent-pos-'.length);
        }
        return 'bottomBar';
    }

    /** A page's ledger of what every live banner is asking of it. */
    function createSpaceLedger() {
        return { banners: [], edges: {} };
    }

    /** Where a banner sits in the ledger, or -1 when it holds nothing there. */
    function findBanner(ledger, key) {
        for (var i = 0; i < ledger.banners.length; i++) {
            if (ledger.banners[i].key === key) return i;
        }
        return -1;
    }

    /**
     * The value for `--ww-consent-height`: the TALLEST live banner, because the variable carries one
     * number and a host placing the room by hand needs the worst case. null once none is left, which
     * removes the property rather than leaving a stale height behind.
     */
    function publishedHeight(ledger) {
        if (ledger.banners.length === 0) return null;
        var tallest = 0;
        for (var i = 0; i < ledger.banners.length; i++) {
            if (ledger.banners[i].height > tallest) tallest = ledger.banners[i].height;
        }
        return tallest + 'px';
    }

    /**
     * What the page's inline padding on one edge should now be, or null for an edge nobody claims.
     *
     * Two banners on the same edge overlap rather than stack, so the page reserves the TALLEST of
     * them and not their sum. When the last one goes the page gets back exactly the inline value it
     * had before the first one arrived - and a `value` of null means it had none, so the declaration
     * is REMOVED rather than zeroed over whatever the stylesheet asks for.
     */
    function paddingWrite(ledger, edge) {
        if (!edge) return null;
        var record = ledger.edges[edge];
        if (!record) return null;

        var tallest = -1;
        for (var i = 0; i < ledger.banners.length; i++) {
            if (ledger.banners[i].edge === edge && ledger.banners[i].height > tallest) {
                tallest = ledger.banners[i].height;
            }
        }
        if (tallest >= 0) return { edge: edge, value: (record.base + tallest) + 'px' };

        // Drained: hand the page's own padding back and forget it, so the next banner to arrive reads
        // the page fresh rather than measuring against a reservation that has been gone for an hour.
        delete ledger.edges[edge];
        return { edge: edge, value: record.previous === '' ? null : record.previous };
    }

    /**
     * Records one banner's measurement - or re-records it after a resize - and answers everything the
     * page should now carry. `readPage` supplies the page's OWN padding on the edge and is called at
     * most once per edge, BEFORE the first claim on it lands, so a second banner never reads our own
     * reservation back as the page's base. Re-recording an existing banner replaces its claim instead
     * of stacking another, which is what makes a repeated call harmless.
     */
    function reserveInLedger(ledger, key, edge, height, readPage) {
        var writes = [];
        var index = findBanner(ledger, key);
        if (index >= 0 && ledger.banners[index].edge !== edge) {
            // Not the edge it was on: settle the one it leaves before it claims the new one.
            var left = ledger.banners[index].edge;
            ledger.banners.splice(index, 1);
            index = -1;
            var vacated = paddingWrite(ledger, left);
            if (vacated) writes.push(vacated);
        }
        if (index < 0) ledger.banners.push({ key: key, edge: edge, height: height });
        else ledger.banners[index].height = height;

        if (edge && !ledger.edges[edge]) {
            var page = readPage ? readPage() : null;
            ledger.edges[edge] = { base: (page && page.base) || 0, previous: (page && page.previous) || '' };
        }
        var claimed = paddingWrite(ledger, edge);
        if (claimed) writes.push(claimed);
        return { height: publishedHeight(ledger), padding: writes };
    }

    /**
     * Drops one banner's claim. A key holding nothing - released twice, or never reserved at all -
     * answers null: nothing to write, and nothing of anybody else's disturbed.
     */
    function releaseFromLedger(ledger, key) {
        var index = findBanner(ledger, key);
        if (index < 0) return null;
        var edge = ledger.banners[index].edge;
        ledger.banners.splice(index, 1);
        var write = paddingWrite(ledger, edge);
        return { height: publishedHeight(ledger), padding: write ? [write] : [] };
    }
    // ---- END shared reserve-space bookkeeping ----

    // ---- The DOM half: measuring, observing, and writing what the ledger decided ----

    // Page-scoped, and keyed PER BANNER rather than held in one "the last call wins" variable.
    // Shared on `window` for the same reason the instance registry is: several
    // <vc:consent-banner> can sit on one page, each self-including this script, and every IIFE run
    // has to see the SAME ledger or they will restore the page's padding over each other.
    var _space = (typeof window !== 'undefined')
        ? (window.__wildwoodConsentSpace = window.__wildwoodConsentSpace
            || { ledger: createSpaceLedger(), held: {}, seed: 0 })
        : { ledger: createSpaceLedger(), held: {}, seed: 0 };

    function nextSpaceKey() {
        _space.seed += 1;
        return 'wwcs-' + _space.seed;
    }

    /** The page's OWN padding on an edge: what to add to, and the inline value to give back. */
    function readPageEdge(edge) {
        return function () {
            var body = document.body;
            return { base: parseFloat(getComputedStyle(body)[edge]) || 0, previous: body.style[edge] || '' };
        };
    }

    /** Writes what the ledger decided: the published height, and the padding for any edge it moved. */
    function applySpace(write) {
        var root = document.documentElement;
        var body = document.body;
        if (write.height === null) root.style.removeProperty(CONSENT_HEIGHT_VAR);
        else root.style.setProperty(CONSENT_HEIGHT_VAR, write.height);

        for (var i = 0; i < write.padding.length; i++) {
            var p = write.padding[i];
            if (p.value === null) body.style.removeProperty(edgeProperty(p.edge));
            else body.style[p.edge] = p.value;
        }
    }

    /**
     * Publishes the banner's measured height and, unless it is a corner card or the host opted out,
     * reserves that much room at the edge it is anchored to. Returns the token the reservation is
     * held under; the element is tagged with it too.
     *
     * Idempotent per banner: calling it again for the same element re-measures that one reservation
     * instead of stacking a second, so a repeated call cannot strand padding.
     */
    function reserveBannerSpace(el, reserve, key) {
        if (!el || typeof document === 'undefined') return null;

        var tagged = el.getAttribute(SPACE_KEY_ATTR);
        var token = key || tagged || nextSpaceKey();
        if (tagged && tagged !== token) releaseBannerSpace(tagged);
        el.setAttribute(SPACE_KEY_ATTR, token);

        var held = _space.held[token];
        if (held && held.observer) held.observer.disconnect();

        var edge = reservedEdge(positionOf(el), reserve);
        var measure = function () {
            applySpace(reserveInLedger(_space.ledger, token, edge, el.offsetHeight, readPageEdge(edge)));
        };

        measure();
        // Guarded: older runtimes have no ResizeObserver, and a consent banner must never be the
        // reason a host's page fails to render. Without one the measurement above simply stands alone.
        var observer = null;
        if (typeof ResizeObserver !== 'undefined') {
            observer = new ResizeObserver(measure);
            observer.observe(el);
        }
        _space.held[token] = { el: el, observer: observer };
        return token;
    }

    /**
     * Gives one banner's reservation back: its observer, its share of the page's padding, and -
     * once it was the last one up - the published height and the page's own inline padding,
     * restored exactly as found. A token that holds nothing is a no-op, so releasing twice, or
     * releasing a banner that never reserved, cannot disturb another banner that is still up.
     */
    function releaseBannerSpace(key) {
        if (typeof document === 'undefined' || !key) return;
        var token = typeof key === 'string' ? key : (key.getAttribute ? key.getAttribute(SPACE_KEY_ATTR) : null);
        if (!token) return;

        var held = _space.held[token];
        if (held) {
            if (held.observer) held.observer.disconnect();
            if (held.el && held.el.removeAttribute) held.el.removeAttribute(SPACE_KEY_ATTR);
            delete _space.held[token];
        }
        var write = releaseFromLedger(_space.ledger, token);
        if (write) applySpace(write);
    }

    // ===== Consent engine =====================================================

    function ConsentEngine(baseUrl, appId, proxyUrl) {
        this.baseUrl = (baseUrl || '').replace(/\/+$/, '');
        // Optional same-origin proxy base; when set the engine calls {proxy}/config and {proxy}/record
        // instead of the API directly (avoids cross-origin CORS). See ConsentBanner README. This is the
        // single normalization point for trailing slashes.
        this.proxyUrl = (proxyUrl || '').replace(/\/+$/, '');
        this.appId = appId;
        this.cookieName = 'ww_consent';
        this.cookieDays = 180;
        this.config = null;
        this.state = null;
        this.injectedIds = {};
    }

    /**
     * Initializes consent and then announces the state, restored or defaulted, exactly once. Without
     * that announcement a returning visitor with a valid cookie never produces a change event, so a
     * consumer gated on consent (attribution.js's persistence) would wait forever. Mirrors
     * @wildwood/core ConsentService.initialize(); later changes emit from _applyCategories and withdraw.
     */
    ConsentEngine.prototype.initialize = function () {
        var self = this;
        return this._initializeState().then(function (result) {
            self._emitChange();
            return result;
        });
    };

    ConsentEngine.prototype._initializeState = function () {
        var self = this;
        return this._fetchConfig().then(function (config) {
            self.config = config;
            self.injectedIds = {};

            var gpcPresent = self._readGpc();
            var cookie = self._readCookie();
            var visitorKey = (cookie && cookie.visitorKey) || self._generateVisitorKey();
            var hasValidCookie = !!cookie && cookie.configVersion === config.version && config.enabled;

            var categories = emptyCategories();
            var decided = false;
            if (hasValidCookie) {
                categories = self._decode(cookie.consentString);
                decided = true;
            }
            if (config.enabled && config.honorGpc && gpcPresent) {
                for (var i = 0; i < GPC_FORCED_OFF.length; i++) categories[GPC_FORCED_OFF[i]] = false;
            }
            self.state = { visitorKey: visitorKey, categories: categories, configVersion: config.version, decided: decided, gpcPresent: gpcPresent };

            if (!config.enabled) return self._result(false);

            self._injectCategory('StrictlyNecessary');

            if (decided) {
                self._injectConsented();
                return self._result(false);
            }

            if (!self._shouldShowBanner()) {
                return self._applyNonTargetDefault(gpcPresent).then(function () { return self._result(false); });
            }
            // GPC is already applied to the in-memory state; the decision is recorded when the visitor
            // acts. Do NOT persist here, or the banner would be suppressed before the visitor chose.
            return self._result(true);
        });
    };

    ConsentEngine.prototype.acceptAll = function () {
        var cats = emptyCategories();
        var active = this._activeCategories();
        for (var i = 0; i < active.length; i++) cats[active[i]] = true;
        return this._applyCategories(cats, 'AcceptAll');
    };

    ConsentEngine.prototype.rejectAll = function () {
        return this._applyCategories(emptyCategories(), 'RejectAll');
    };

    ConsentEngine.prototype.setCategories = function (selection) {
        var cats = emptyCategories();
        var active = this._activeCategories();
        for (var i = 0; i < active.length; i++) cats[active[i]] = !!(selection && selection[active[i]] === true);
        return this._applyCategories(cats, 'Custom');
    };

    ConsentEngine.prototype.withdraw = function () {
        var self = this;
        if (!this.config || !this.state) return Promise.resolve(null);
        this.state.categories = emptyCategories();
        this.state.decided = false;
        return this._record('RejectAll', this._encode(this.state.categories)).then(function () {
            self._clearCookie();
            self._emitChange();
            return self.state;
        });
    };

    ConsentEngine.prototype.getState = function () { return this.state; };

    ConsentEngine.prototype.isGranted = function (category) {
        if (category === 'StrictlyNecessary') return true;
        return !!(this.state && this.state.categories[category] === true);
    };

    ConsentEngine.prototype._result = function (shouldShowBanner) {
        return { config: this.config, state: this.state, shouldShowBanner: shouldShowBanner };
    };

    ConsentEngine.prototype._fetchConfig = function () {
        var url = this.proxyUrl
            ? this.proxyUrl + '/config?appId=' + encodeURIComponent(this.appId)
            : this.baseUrl + '/api/consent/config?appId=' + encodeURIComponent(this.appId);
        return fetch(url, {
            method: 'GET',
            headers: { Accept: 'application/json' }
        }).then(function (res) {
            if (!res.ok) throw new Error('consent config failed: ' + res.status);
            return res.json();
        });
    };

    ConsentEngine.prototype._shouldShowBanner = function () {
        if (!this.config.geo || !this.config.geo.aware) return true;
        return this.config.geo.inTarget === true;
    };

    // Tells consent-gated consumers (attribution.js) that the decision changed, via a DOM event so they
    // need no reference to this script.
    ConsentEngine.prototype._emitChange = function () {
        if (typeof document === 'undefined' || typeof CustomEvent !== 'function') return;
        try {
            document.dispatchEvent(new CustomEvent('wildwood:consent-change', { detail: this.state }));
        } catch (e) { /* best-effort */ }
    };

    ConsentEngine.prototype._applyNonTargetDefault = function (gpcPresent) {
        var cats = emptyCategories();
        if (this.config.nonTargetDefault === 'LoadAll') {
            var active = this._activeCategories();
            for (var i = 0; i < active.length; i++) cats[active[i]] = true;
        }
        if (this.config.honorGpc && gpcPresent) {
            for (var j = 0; j < GPC_FORCED_OFF.length; j++) cats[GPC_FORCED_OFF[j]] = false;
        }
        this.state.categories = cats;
        this.state.decided = true;
        this._injectConsented();
        // No _emitChange here: this runs only inside initialize(), which emits once for every path.
        return this._persist(gpcPresent ? 'Gpc' : 'NonTargetDefault');
    };

    ConsentEngine.prototype._applyCategories = function (cats, method) {
        if (this.config.honorGpc && this.state.gpcPresent) {
            for (var i = 0; i < GPC_FORCED_OFF.length; i++) cats[GPC_FORCED_OFF[i]] = false;
        }
        this.state.categories = cats;
        this.state.decided = true;
        this._injectConsented();
        var self = this;
        return this._persist(method).then(function () {
            self._emitChange();
            return self.state;
        });
    };

    ConsentEngine.prototype._persist = function (method) {
        var consentString = this._encode(this.state.categories);
        this._writeCookie({ visitorKey: this.state.visitorKey, consentString: consentString, configVersion: this.config.version });
        return this._record(method, consentString);
    };

    ConsentEngine.prototype._record = function (method, consentString) {
        var self = this;
        var url = this.proxyUrl
            ? this.proxyUrl + '/record?appId=' + encodeURIComponent(this.config.appId)
            : this.baseUrl + '/api/consent/record?appId=' + encodeURIComponent(this.config.appId);
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                appId: self.config.appId,
                visitorKey: self.state.visitorKey,
                consentString: consentString,
                method: method,
                gpcPresent: self.state.gpcPresent,
                configVersion: self.config.version
            })
        }).catch(function (e) {
            // Recording is best-effort; never block the UI on it.
            console.warn('[ww-consent] failed to record decision', e);
        });
    };

    // ---- Script injection ----------------------------------------------------

    ConsentEngine.prototype._injectConsented = function () {
        for (var i = 0; i < NON_NECESSARY.length; i++) {
            if (this.state.categories[NON_NECESSARY[i]]) this._injectCategory(NON_NECESSARY[i]);
        }
    };

    ConsentEngine.prototype._injectCategory = function (category) {
        if (!this.config || !this.config.scripts) return;
        for (var i = 0; i < this.config.scripts.length; i++) {
            var s = this.config.scripts[i];
            if (s.category === category) this._injectScript(s);
        }
    };

    ConsentEngine.prototype._injectScript = function (s) {
        if (typeof document === 'undefined') return;
        if (this.injectedIds[s.id]) return;
        this.injectedIds[s.id] = true;
        var target = s.loadPosition === 'BodyEnd' ? document.body : document.head;
        if (!target) return;

        if (s.injectionMode === 'ExternalSrc' && s.src) {
            var el = document.createElement('script');
            el.src = s.src;
            if (s.loadStrategy === 'Async') el.async = true;
            else if (s.loadStrategy === 'Defer') el.defer = true;
            el.setAttribute('data-ww-consent', s.category);
            target.appendChild(el);
        } else if (s.injectionMode === 'InlineSnippet' && s.snippet) {
            this._injectSnippet(s.snippet, target, s.category);
        }
    };

    ConsentEngine.prototype._injectSnippet = function (snippet, target, category) {
        var template = document.createElement('template');
        template.innerHTML = String(snippet).trim();
        var nodes = Array.prototype.slice.call(template.content.childNodes);
        if (nodes.length === 0) {
            var el = document.createElement('script');
            el.textContent = snippet;
            el.setAttribute('data-ww-consent', category);
            target.appendChild(el);
            return;
        }
        for (var i = 0; i < nodes.length; i++) {
            var node = nodes[i];
            if (node.nodeName === 'SCRIPT') {
                var script = document.createElement('script');
                for (var a = 0; a < node.attributes.length; a++) {
                    script.setAttribute(node.attributes[a].name, node.attributes[a].value);
                }
                script.textContent = node.textContent;
                script.setAttribute('data-ww-consent', category);
                target.appendChild(script);
            } else {
                target.appendChild(node.cloneNode(true));
            }
        }
    };

    // ---- Cookie / GPC / encoding --------------------------------------------

    ConsentEngine.prototype._readGpc = function () {
        return typeof navigator !== 'undefined' && navigator.globalPrivacyControl === true;
    };

    ConsentEngine.prototype._readCookie = function () {
        if (typeof document === 'undefined') return null;
        var prefix = this.cookieName + '=';
        var rows = document.cookie.split('; ');
        for (var i = 0; i < rows.length; i++) {
            if (rows[i].indexOf(prefix) === 0) {
                try {
                    var parsed = JSON.parse(decodeURIComponent(rows[i].substring(prefix.length)));
                    return parsed && typeof parsed.visitorKey === 'string' ? parsed : null;
                } catch (e) {
                    return null;
                }
            }
        }
        return null;
    };

    ConsentEngine.prototype._writeCookie = function (cookie) {
        if (typeof document === 'undefined') return;
        var value = encodeURIComponent(JSON.stringify(cookie));
        var maxAge = this.cookieDays * 24 * 60 * 60;
        var secure = typeof location !== 'undefined' && location.protocol === 'https:' ? '; Secure' : '';
        document.cookie = this.cookieName + '=' + value + '; Max-Age=' + maxAge + '; Path=/; SameSite=Lax' + secure;
    };

    ConsentEngine.prototype._clearCookie = function () {
        if (typeof document === 'undefined') return;
        document.cookie = this.cookieName + '=; Max-Age=0; Path=/; SameSite=Lax';
    };

    ConsentEngine.prototype._generateVisitorKey = function () {
        if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID();
        var perf = typeof performance !== 'undefined' ? performance.now() : 0;
        return 'v-' + Date.now().toString(36) + '-' + perf.toString(36);
    };

    ConsentEngine.prototype._encode = function (categories) {
        return NON_NECESSARY.filter(function (c) { return categories[c]; }).join(',');
    };

    ConsentEngine.prototype._decode = function (consentString) {
        var cats = emptyCategories();
        if (!consentString) return cats;
        var parts = consentString.split(',');
        for (var i = 0; i < parts.length; i++) {
            var c = parts[i].trim();
            if (NON_NECESSARY.indexOf(c) >= 0) cats[c] = true;
        }
        return cats;
    };

    ConsentEngine.prototype._activeCategories = function () {
        var active = (this.config && this.config.categories) || [];
        return NON_NECESSARY.filter(function (c) { return active.indexOf(c) >= 0; });
    };

    // ===== UI wiring ==========================================================

    function initConsent(root) {
        // Guard against the IIFE running twice (e.g. the script self-included once per instance).
        if (root.getAttribute('data-ww-consent-init') === 'true') return;
        root.setAttribute('data-ww-consent-init', 'true');

        var appId = root.dataset.appId;
        var baseUrl = root.dataset.baseUrl || '';
        var proxyUrl = root.dataset.proxyUrl || ''; // normalized in the ConsentEngine constructor
        var showReopenLink = root.dataset.showReopenLink === 'true';
        var showFooterOptOut = root.dataset.showFooterOptOut === 'true';
        // Opt-out, not opt-in: a host that says nothing gets a banner that cannot cover its page.
        var reserveSpace = root.dataset.reserveSpace !== 'false';
        if (!appId) return;

        var banner = root.querySelector('.ww-consent-banner');
        var overlay = root.querySelector('.ww-consent-modal-overlay');
        var footer = root.querySelector('.ww-consent-footer-links');
        var categoriesEl = root.querySelector('.ww-consent-categories');
        var rightsEl = root.querySelector('.ww-consent-rights');
        var linksEl = root.querySelector('.ww-consent-links');

        var engine = new ConsentEngine(baseUrl, appId, proxyUrl);
        var spaceKey = null;   // this banner's own reservation, given back once it has gone

        // Expose this instance for host-app control (reopen / withdraw / gating).
        instances.push({
            appId: appId,
            engine: engine,
            reopen: function () { if (engine.config) openModal(engine.config); },
            withdraw: function () { return engine.withdraw(); }
        });

        engine.initialize().then(function (result) {
            if (!result || !result.config || !result.config.enabled) return;
            var config = result.config;

            applyText(config);
            wireButtons(config);

            if (result.shouldShowBanner) {
                if (banner) {
                    banner.style.display = '';
                    // Measured only now: the banner was display:none until this line, the position
                    // class applyText chose decides whether it pads at all, and the height depends
                    // on the copy applyText just wrote and on how it wraps.
                    spaceKey = reserveBannerSpace(banner, reserveSpace);
                }
            } else {
                renderFooter(config);
            }
        }).catch(function (e) {
            console.warn('[ww-consent] initialization failed', e);
        });

        // ---- text + dynamic content ----

        function text(config) { return (config && config.bannerText) || {}; }

        function applyText(config) {
            var t = text(config);
            setText(root.querySelector('.ww-consent-title'), t.title || 'We value your privacy');
            setText(root.querySelector('.ww-consent-body-text'),
                t.body || 'We use cookies and similar technologies. Choose which categories to allow. Necessary items are always on.');
            each(root.querySelectorAll('.ww-consent-manage'), function (el) { el.textContent = t.manage || 'Manage preferences'; });
            each(root.querySelectorAll('.ww-consent-reject'), function (el) { el.textContent = t.rejectAll || 'Reject all'; });
            each(root.querySelectorAll('.ww-consent-accept'), function (el) { el.textContent = t.acceptAll || 'Accept all'; });

            var bannerPos = (config.appearance && config.appearance.position) || 'bottomBar';
            if (banner) {
                banner.classList.remove('ww-consent-pos-bottomBar', 'ww-consent-pos-topBar', 'ww-consent-pos-corner');
                banner.classList.add('ww-consent-pos-' + bannerPos);
            }

            var privacy = root.querySelector('.ww-consent-privacy-link');
            if (privacy && config.privacyPolicyUrl) {
                privacy.href = config.privacyPolicyUrl;
                privacy.style.display = '';
            }
        }

        function buildCategoryRows() {
            if (!categoriesEl) return;
            categoriesEl.textContent = '';
            var active = engine._activeCategories();
            var state = engine.getState();
            for (var i = 0; i < active.length; i++) {
                var category = active[i];
                var row = document.createElement('div');
                row.className = 'ww-consent-category';

                var labelWrap = document.createElement('div');
                labelWrap.className = 'ww-consent-category-label';
                var strong = document.createElement('strong');
                strong.textContent = category;
                labelWrap.appendChild(strong);

                var sw = document.createElement('label');
                sw.className = 'ww-consent-switch';
                var input = document.createElement('input');
                input.type = 'checkbox';
                input.className = 'ww-consent-category-checkbox';
                input.setAttribute('data-category', category);
                input.setAttribute('aria-label', category);
                input.checked = !!(state && state.categories[category] === true);
                var slider = document.createElement('span');
                slider.className = 'ww-consent-slider';
                sw.appendChild(input);
                sw.appendChild(slider);

                row.appendChild(labelWrap);
                row.appendChild(sw);
                categoriesEl.appendChild(row);
            }
        }

        function buildRights(config) {
            if (!rightsEl) return;
            rightsEl.textContent = '';
            var any = false;
            if (config.showDoNotSell) {
                rightsEl.appendChild(makeButton('ww-consent-btn ww-consent-btn-secondary',
                    'Do Not Sell or Share My Personal Information', function () { optOut('Advertising'); }));
                any = true;
            }
            if (config.showLimitSensitive) {
                rightsEl.appendChild(makeButton('ww-consent-btn ww-consent-btn-secondary',
                    'Limit the Use of My Sensitive Personal Information', function () { optOut('Sensitive'); }));
                any = true;
            }
            rightsEl.style.display = any ? '' : 'none';
            var divider = root.querySelector('.ww-consent-divider');
            if (divider) divider.style.display = any ? '' : 'none';
        }

        function buildLinks(config) {
            if (!linksEl) return;
            linksEl.textContent = '';
            if (config.privacyPolicyUrl) linksEl.appendChild(makeLink(config.privacyPolicyUrl, 'Privacy Policy'));
            if (config.accessibilityUrl) linksEl.appendChild(makeLink(config.accessibilityUrl, 'Accessibility'));
        }

        function renderFooter(config) {
            if (!footer) return;
            footer.textContent = '';
            var any = false;
            if (showReopenLink) {
                footer.appendChild(makeButton('ww-consent-reopen', 'Privacy choices', function () { openModal(config); }));
                any = true;
            }
            if (showFooterOptOut && config.showDoNotSell) {
                footer.appendChild(makeButton('ww-consent-reopen', 'Do Not Sell or Share', function () { optOut('Advertising'); }));
                any = true;
            }
            if (showFooterOptOut && config.showLimitSensitive) {
                footer.appendChild(makeButton('ww-consent-reopen', 'Limit Use of Sensitive PI', function () { optOut('Sensitive'); }));
                any = true;
            }
            footer.style.display = any ? '' : 'none';
        }

        // ---- actions ----

        // Run an engine action, then finish() regardless of outcome. Promise.resolve().then(fn)
        // captures any *synchronous* throw from the engine call so the click handler never leaks an
        // uncaught error and the banner is never left stuck on screen. The two-argument then() (rather
        // than .then().catch()) ensures a throw from finish() in the success path is NOT also routed
        // to the failure handler — finish() runs exactly once.
        function runAction(fn) {
            Promise.resolve().then(fn).then(
                function () { finish(engine.config); },
                function (e) {
                    console.warn('[ww-consent] action failed', e);
                    finish(engine.config);
                }
            );
        }

        function wireButtons(config) {
            each(root.querySelectorAll('.ww-consent-accept'), function (el) {
                el.addEventListener('click', function () { runAction(function () { return engine.acceptAll(); }); });
            });
            each(root.querySelectorAll('.ww-consent-reject'), function (el) {
                el.addEventListener('click', function () { runAction(function () { return engine.rejectAll(); }); });
            });
            each(root.querySelectorAll('.ww-consent-manage'), function (el) {
                el.addEventListener('click', function () { openModal(config); });
            });
            var saveBtn = root.querySelector('.ww-consent-save');
            if (saveBtn) saveBtn.addEventListener('click', function () { savePreferences(); });
            var closeBtn = root.querySelector('.ww-consent-close');
            if (closeBtn) closeBtn.addEventListener('click', closeModal);
            if (overlay) {
                overlay.addEventListener('click', function (e) { if (e.target === overlay) closeModal(); });
            }
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && overlay && overlay.style.display !== 'none') closeModal();
            });
        }

        function openModal(config) {
            buildCategoryRows();
            buildRights(config);
            buildLinks(config);
            if (overlay) overlay.style.display = '';
        }

        function closeModal() {
            if (overlay) overlay.style.display = 'none';
        }

        function savePreferences() {
            var selection = {};
            each(root.querySelectorAll('.ww-consent-category-checkbox'), function (cb) {
                selection[cb.getAttribute('data-category')] = cb.checked;
            });
            runAction(function () { return engine.setCategories(selection); });
        }

        // One-click CCPA opt-out: turn the category off against current state, post immediately.
        function optOut(category) {
            var next = {};
            var active = engine._activeCategories();
            var state = engine.getState();
            for (var i = 0; i < active.length; i++) {
                next[active[i]] = active[i] === category ? false : !!(state && state.categories[active[i]] === true);
            }
            runAction(function () { return engine.setCategories(next); });
        }

        function finish(config) {
            if (banner) banner.style.display = 'none';
            // This banner has gone, so the room reserved for IT goes with it - and only that:
            // another banner still up on the same edge keeps the page padded for as long as it is
            // there, and the page's own inline padding comes back restored rather than zeroed.
            if (spaceKey) {
                releaseBannerSpace(spaceKey);
                spaceKey = null;
            }
            closeModal();
            renderFooter(config);
            root.dispatchEvent(new CustomEvent('ww-consent-changed', {
                detail: { state: engine.getState() },
                bubbles: true
            }));
        }
    }

    // ---- helpers -------------------------------------------------------------

    function each(nodeList, fn) {
        for (var i = 0; i < nodeList.length; i++) fn(nodeList[i]);
    }

    function setText(el, value) {
        if (el) el.textContent = value;
    }

    function makeButton(className, label, onClick) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = className;
        btn.textContent = label;
        btn.addEventListener('click', onClick);
        return btn;
    }

    function makeLink(href, label) {
        var a = document.createElement('a');
        a.className = 'ww-consent-link';
        a.href = href;
        a.target = '_blank';
        a.rel = 'noopener';
        a.textContent = label;
        return a;
    }

    // Resolve a registered instance by appId (or the first one when omitted).
    function pickInstance(appId) {
        if (!appId) return instances[0] || null;
        for (var i = 0; i < instances.length; i++) {
            if (instances[i].appId === appId) return instances[i];
        }
        return instances[0] || null;
    }

    // Public API for host apps: reopen the preferences dialog, withdraw consent (clears the cookie;
    // prompt a reload to fully clear already-injected scripts), gate features on isGranted, read state.
    // Mirrors the Blazor engine's exports and the disclaimer widget's window.wwDisclaimer convention.
    if (typeof window !== 'undefined') {
        window.wildwoodConsent = {
            reopen: function (appId) {
                var inst = pickInstance(appId);
                if (inst) inst.reopen();
            },
            withdraw: function (appId) {
                var inst = pickInstance(appId);
                return inst ? inst.withdraw() : Promise.resolve(null);
            },
            isGranted: function (category, appId) {
                var inst = pickInstance(appId);
                return inst ? inst.engine.isGranted(category) : false;
            },
            getState: function (appId) {
                var inst = pickInstance(appId);
                return inst ? inst.engine.getState() : null;
            }
        };
    }

    // Auto-initialize every banner on the page. Guarded so this file can also be required by the
    // Node self-test, which needs the pure bookkeeping above and no DOM at all.
    if (typeof document !== 'undefined') {
        var roots = document.querySelectorAll('.ww-consent-component');
        for (var r = 0; r < roots.length; r++) initConsent(roots[r]);
    }

    // The self-test's hook. Guarded so the browser build never sees it: there is no `module` in a
    // classic script, and the check costs one typeof. Only the pure half is exported - the rest
    // needs a DOM, and what a second surface can silently get wrong is all in here.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            reservedEdge: reservedEdge,
            edgeProperty: edgeProperty,
            positionOf: positionOf,
            createSpaceLedger: createSpaceLedger,
            publishedHeight: publishedHeight,
            paddingWrite: paddingWrite,
            reserveInLedger: reserveInLedger,
            releaseFromLedger: releaseFromLedger
        };
    }
})();
