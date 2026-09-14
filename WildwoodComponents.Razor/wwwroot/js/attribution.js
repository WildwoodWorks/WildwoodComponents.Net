/*
 * WildwoodComponents.Razor - Campaign Attribution engine (window.wildwoodAttribution)
 *
 * Classic-script (non-ESM) port of @wildwood/core's AttributionService and the Blazor
 * wwwroot/js/wildwood-attribution.js engine: the same storage key (ww_attribution), capture and
 * normalization rules, and endpoints. Keep the three in step.
 *
 * Include once in the layout, as early as possible, so the landing URL is read before anything
 * navigates:
 *
 *   <script src="~/_content/WildwoodComponents.Razor/js/attribution.js"
 *           data-app-id="your-app-id" data-base-url="https://api.wildwoodworks.io"></script>
 *
 * With data-app-id it initializes itself; otherwise call window.wildwoodAttribution.initialize(baseUrl, appId).
 * token-registration.js attaches window.wildwoodAttribution.getForRegistration() to registrations.
 *
 * Consent: touches are persisted only once the app's consent category is granted, read from the
 * ww_consent cookie and re-checked on every 'wildwood:consent-change' event consent.js dispatches. When
 * consent state exists and does not grant the category, any stored blob is removed. Nothing here throws
 * into the page.
 */
(function () {
    'use strict';

    if (typeof window === 'undefined' || window.wildwoodAttribution) return;

    var STORAGE_KEY = 'ww_attribution';
    var CONSENT_COOKIE = 'ww_consent';
    var CONSENT_EVENT = 'wildwood:consent-change';
    var SCHEMA_VERSION = 1;
    var DAY_MS = 24 * 60 * 60 * 1000;
    var DEFAULT_WINDOW_DAYS = 30;
    var SOURCE_MEDIUM_MAX = 100;
    var CAMPAIGN_TERM_CONTENT_MAX = 200;
    var HOST_MAX = 253;
    var PATH_MAX = 500;
    var EXTRA_PARAMS_JSON_MAX = 2000;
    var MAX_EXTRA_PARAM_NAMES = 10;
    var CLICK_ID_PARAMS = ['gclid', 'gbraid', 'wbraid', 'fbclid', 'msclkid', 'ttclid', 'li_fat_id', 'twclid', 'rdt_cid'];
    var CONSENT_CATEGORIES = ['StrictlyNecessary', 'Functional', 'Analytics', 'Advertising', 'Sensitive'];
    var PARAM_NAME = /^[a-z0-9_]{1,32}$/;
    var CLICK_ID_VALUE = /^[A-Za-z0-9._~-]{1,200}$/;
    var VISITOR_KEY = /^[A-Za-z0-9_-]{8,100}$/;
    var CONTROL_OR_FORMAT = (function () {
        try {
            return new RegExp('[\\p{Cc}\\p{Cf}]', 'u');
        } catch (e) {
            return null;
        }
    })();
    var fallbackCounter = 0;

    // ===== Rules (mirrors @wildwood/core attributionRules.ts) =====================================

    function hasControlOrFormat(text) {
        if (CONTROL_OR_FORMAT) return CONTROL_OR_FORMAT.test(text);
        for (var i = 0; i < text.length; i++) {
            var c = text.charCodeAt(i);
            if (c <= 0x1f || (c >= 0x7f && c <= 0x9f) || c === 0xad || (c >= 0x200b && c <= 0x200f) ||
                (c >= 0x202a && c <= 0x202e) || (c >= 0x2060 && c <= 0x206f) || c === 0xfeff) {
                return true;
            }
        }
        return false;
    }

    function truncate(value, max) {
        if (value.length <= max) return value;
        var cut = max;
        var code = value.charCodeAt(cut - 1);
        if (code >= 0xd800 && code <= 0xdbff) cut -= 1;
        return value.slice(0, cut);
    }

    function normalizeToken(value, max, lowercase) {
        if (value === null || value === undefined) return null;
        var token = String(value).trim();
        if (token.length === 0 || hasControlOrFormat(token)) return null;
        if (lowercase) token = token.toLowerCase();
        token = truncate(token, max).replace(/\s+$/, '');
        return token.length === 0 ? null : token;
    }

    function clampWindowDays(value, fallback) {
        var days = typeof value === 'number' && isFinite(value) ? Math.floor(value) : fallback;
        return Math.min(365, Math.max(1, days));
    }

    function isTouchExpired(touch, windowDays, nowMs) {
        var at = Date.parse(touch.occurredAt);
        return !isFinite(at) || at < nowMs - windowDays * DAY_MS;
    }

    function generateVisitorKey() {
        var c = typeof crypto !== 'undefined' ? crypto : null;
        if (c && typeof c.randomUUID === 'function') return c.randomUUID();
        if (c && typeof c.getRandomValues === 'function') {
            var bytes = c.getRandomValues(new Uint8Array(16));
            var hex = '';
            for (var i = 0; i < bytes.length; i++) hex += (bytes[i] < 16 ? '0' : '') + bytes[i].toString(16);
            return hex;
        }
        fallbackCounter += 1;
        return 'wv-' + Date.now().toString(36) + '-' + fallbackCounter.toString(36);
    }

    function parseUrl(value) {
        try {
            return new URL(value);
        } catch (e) {
            return null;
        }
    }

    function hostName(url, stripWww, includePort) {
        var host = (url.hostname || '').toLowerCase().replace(/\.$/, '');
        if (host.length === 0) return null;
        if (stripWww && host.indexOf('www.') === 0 && host.length > 4) host = host.slice(4);
        if (includePort && url.port) host = host + ':' + url.port;
        return host.length <= HOST_MAX ? host : null;
    }

    function normalizePath(pathname) {
        var path = pathname || '/';
        if (path.charAt(0) !== '/') path = '/' + path;
        return truncate(path, PATH_MAX);
    }

    function readExtraParams(params, names) {
        var kept = {};
        var count = 0;
        var list = (names || []).slice(0, MAX_EXTRA_PARAM_NAMES);
        for (var i = 0; i < list.length; i++) {
            var name = String(list[i]).trim().toLowerCase();
            if (!PARAM_NAME.test(name) || Object.prototype.hasOwnProperty.call(kept, name)) continue;
            var value = normalizeToken(params.get(name), CAMPAIGN_TERM_CONTENT_MAX, false);
            if (value !== null) {
                kept[name] = value;
                count++;
            }
        }
        if (count === 0) return null;
        return JSON.stringify(kept).length <= EXTRA_PARAMS_JSON_MAX ? kept : null;
    }

    function parseTouch(href, referrer, options) {
        var url = parseUrl(href);
        if (!url) return null;
        var params = url.searchParams;
        var source = normalizeToken(params.get('utm_source'), SOURCE_MEDIUM_MAX, true);
        var medium = normalizeToken(params.get('utm_medium'), SOURCE_MEDIUM_MAX, true);
        var campaign = normalizeToken(params.get('utm_campaign'), CAMPAIGN_TERM_CONTENT_MAX, false);
        var term = normalizeToken(params.get('utm_term'), CAMPAIGN_TERM_CONTENT_MAX, false);
        var content = normalizeToken(params.get('utm_content'), CAMPAIGN_TERM_CONTENT_MAX, false);

        var clickIdName = null;
        var clickIdValue = null;
        if (options.captureClickIds) {
            for (var i = 0; i < CLICK_ID_PARAMS.length; i++) {
                var raw = params.get(CLICK_ID_PARAMS[i]);
                var value = raw === null ? '' : raw.trim();
                if (value && CLICK_ID_VALUE.test(value)) {
                    clickIdName = CLICK_ID_PARAMS[i];
                    clickIdValue = value;
                    break;
                }
            }
        }

        var extraParams = readExtraParams(params, options.extraAllowedParamNames);

        var referrerHost = null;
        if (options.captureReferrer && referrer) {
            var ref = parseUrl(referrer);
            var refHost = ref && (ref.protocol === 'http:' || ref.protocol === 'https:') ? hostName(ref, true, false) : null;
            if (refHost !== null && refHost !== hostName(url, true, false)) referrerHost = refHost;
        }

        var hasUtm = source !== null || medium !== null || campaign !== null || term !== null || content !== null;
        if (!hasUtm && referrerHost !== null) {
            source = truncate(referrerHost, SOURCE_MEDIUM_MAX);
            medium = 'referral';
        }
        if (!hasUtm && clickIdName === null && referrerHost === null && extraParams === null) return null;

        return {
            source: source,
            medium: medium,
            campaign: campaign,
            term: term,
            content: content,
            clickIdName: clickIdName,
            clickIdValue: clickIdValue,
            referrerHost: referrerHost,
            landingHost: hostName(url, false, true),
            landingPath: normalizePath(url.pathname),
            extraParams: extraParams,
            occurredAt: new Date().toISOString()
        };
    }

    function normalizeConfig(data, fallbackAppId) {
        if (!data || typeof data !== 'object') return null;
        var isEnabled = data.isEnabled === true;
        var names = [];
        var source = Array.isArray(data.extraAllowedParamNames) ? data.extraAllowedParamNames : [];
        for (var i = 0; i < source.length && names.length < MAX_EXTRA_PARAM_NAMES; i++) {
            if (typeof source[i] !== 'string') continue;
            var name = source[i].trim().toLowerCase();
            if (PARAM_NAME.test(name)) names.push(name);
        }
        return {
            appId: typeof data.appId === 'string' && data.appId ? data.appId : fallbackAppId,
            isEnabled: isEnabled,
            captureFirstTouch: data.captureFirstTouch !== false,
            captureLastTouch: data.captureLastTouch !== false,
            attributionWindowDays: clampWindowDays(data.attributionWindowDays, DEFAULT_WINDOW_DAYS),
            // An unrecognized category falls back to Analytics: persistence then waits for opt-in.
            persistenceConsentCategory: CONSENT_CATEGORIES.indexOf(data.persistenceConsentCategory) >= 0
                ? data.persistenceConsentCategory
                : 'Analytics',
            captureClickIds: data.captureClickIds !== false,
            captureReferrer: data.captureReferrer !== false,
            extraAllowedParamNames: names,
            beaconEnabled: isEnabled && data.beaconEnabled === true
        };
    }

    function sanitizeStoredTouch(value) {
        if (!value || typeof value !== 'object') return null;
        if (typeof value.occurredAt !== 'string' || !isFinite(Date.parse(value.occurredAt))) return null;
        function str(key) {
            return typeof value[key] === 'string' ? value[key] : null;
        }
        var extras = null;
        if (value.extraParams && typeof value.extraParams === 'object' && !Array.isArray(value.extraParams)) {
            var keys = Object.keys(value.extraParams);
            for (var i = 0; i < keys.length && i < MAX_EXTRA_PARAM_NAMES; i++) {
                if (PARAM_NAME.test(keys[i]) && typeof value.extraParams[keys[i]] === 'string') {
                    extras = extras || {};
                    extras[keys[i]] = value.extraParams[keys[i]];
                }
            }
        }
        return {
            source: str('source'),
            medium: str('medium'),
            campaign: str('campaign'),
            term: str('term'),
            content: str('content'),
            clickIdName: str('clickIdName'),
            clickIdValue: str('clickIdValue'),
            referrerHost: str('referrerHost'),
            landingHost: str('landingHost'),
            landingPath: str('landingPath'),
            extraParams: extras,
            occurredAt: value.occurredAt
        };
    }

    // ===== Storage, consent, landing ================================================================

    function readStored() {
        try {
            var raw = window.localStorage.getItem(STORAGE_KEY);
            if (!raw) return null;
            var parsed = JSON.parse(raw);
            if (!parsed || parsed.v !== SCHEMA_VERSION || typeof parsed.visitorKey !== 'string') return null;
            return {
                visitorKey: parsed.visitorKey,
                first: sanitizeStoredTouch(parsed.first),
                last: sanitizeStoredTouch(parsed.last)
            };
        } catch (e) {
            return null;
        }
    }

    function writeStored(blob) {
        try {
            window.localStorage.setItem(STORAGE_KEY, JSON.stringify(blob));
        } catch (e) { /* persistence is best-effort */ }
    }

    function removeStored() {
        try {
            window.localStorage.removeItem(STORAGE_KEY);
        } catch (e) { /* best-effort */ }
    }

    /** Granted categories as { Name: true } from the ww_consent cookie, or null when there is none. */
    function readConsentCategories() {
        try {
            var rows = document.cookie.split('; ');
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].indexOf(CONSENT_COOKIE + '=') !== 0) continue;
                var cookie = JSON.parse(decodeURIComponent(rows[i].substring(CONSENT_COOKIE.length + 1)));
                if (!cookie || typeof cookie.consentString !== 'string') return null;
                var categories = {};
                var parts = cookie.consentString.split(',');
                for (var j = 0; j < parts.length; j++) {
                    var name = parts[j].trim();
                    if (CONSENT_CATEGORIES.indexOf(name) >= 0) categories[name] = true;
                }
                return categories;
            }
            return null;
        } catch (e) {
            return null;
        }
    }

    function apiRoot(baseUrl) {
        var root = String(baseUrl || '').replace(/\/+$/, '');
        return /\/api$/i.test(root) ? root : root + '/api';
    }

    // ===== Engine =====================================================================================

    function AttributionEngine() {
        this.apiRoot = '/api';
        this.appId = '';
        this.config = null;
        this.visitorKey = generateVisitorKey();
        this.first = null;
        this.last = null;
        this.persisted = false;
        this.initPromise = null;
        this.beaconed = {};
        this.listening = false;
    }

    AttributionEngine.prototype.initialize = function (baseUrl, appId) {
        var self = this;
        if (!this.initPromise) {
            // Read the URL before anything awaits: the page may navigate as soon as it renders.
            var landing = { href: window.location.href, referrer: document.referrer || null };
            this.apiRoot = apiRoot(baseUrl);
            this.appId = appId || '';
            this.initPromise = this._run(landing).catch(function () { return undefined; });
        }
        return this.initPromise.then(function () { return self.getState(); });
    };

    AttributionEngine.prototype.captureUrl = function (url, referrer) {
        try {
            if (this.config && !this.config.isEnabled) return null;
            var touch = this._capture(url, referrer || null);
            if (!touch) return null;
            this._persistIfAllowed(null);
            this._beacon(touch);
            return touch;
        } catch (e) {
            return null;
        }
    };

    AttributionEngine.prototype.getForRegistration = function () {
        if (this.config && !this.config.isEnabled) return null;
        if (!this.first && !this.last) return null;
        return {
            version: SCHEMA_VERSION,
            visitorKey: this.visitorKey,
            firstTouch: this.first,
            lastTouch: this.last,
            platform: 'web',
            sdk: 'dotnet'
        };
    };

    AttributionEngine.prototype.clear = function () {
        this.first = null;
        this.last = null;
        this.persisted = false;
        removeStored();
    };

    AttributionEngine.prototype.getState = function () {
        return {
            visitorKey: this.visitorKey,
            first: this.first,
            last: this.last,
            persisted: this.persisted,
            config: this.config
        };
    };

    AttributionEngine.prototype._run = function (landing) {
        var self = this;
        var stored = readStored();
        var configPromise = this.appId ? this._fetchConfig() : Promise.resolve(null);
        return configPromise.then(function (config) {
            self.config = config;
            if (config && !config.isEnabled) {
                self.first = null;
                self.last = null;
                self.persisted = false;
                removeStored();
                return;
            }
            if (stored) {
                if (VISITOR_KEY.test(stored.visitorKey)) self.visitorKey = stored.visitorKey;
                var anchor = stored.first || stored.last;
                if (anchor && !isTouchExpired(anchor, self._windowDays(), Date.now())) {
                    self.first = stored.first || self.first;
                    self.last = self.last || stored.last;
                }
            }
            var touch = self._capture(landing.href, landing.referrer);
            self._listenForConsent();
            self._persistIfAllowed(null);
            if (touch) self._beacon(touch);
        });
    };

    AttributionEngine.prototype._fetchConfig = function () {
        var self = this;
        try {
            return fetch(this.apiRoot + '/attribution/config?appId=' + encodeURIComponent(this.appId), {
                method: 'GET',
                headers: { Accept: 'application/json' }
            }).then(function (res) {
                return res.ok ? res.json() : null;
            }).then(function (data) {
                return data ? normalizeConfig(data, self.appId) : null;
            }).catch(function () {
                return null;
            });
        } catch (e) {
            return Promise.resolve(null);
        }
    };

    AttributionEngine.prototype._windowDays = function () {
        return this.config ? this.config.attributionWindowDays : DEFAULT_WINDOW_DAYS;
    };

    AttributionEngine.prototype._capture = function (href, referrer) {
        var config = this.config;
        var touch = parseTouch(href, referrer, {
            captureClickIds: config ? config.captureClickIds : true,
            captureReferrer: config ? config.captureReferrer : true,
            extraAllowedParamNames: config ? config.extraAllowedParamNames : []
        });
        if (!touch) return null; // a direct visit never overwrites the stored touches
        if (this.first && isTouchExpired(this.first, this._windowDays(), Date.now())) {
            this.first = null;
            this.last = null;
        }
        this.last = touch;
        this.first = this.first || touch;
        return touch;
    };

    AttributionEngine.prototype._persistIfAllowed = function (consentState) {
        var config = this.config;
        if (!config || !config.isEnabled) return;
        var category = config.persistenceConsentCategory;
        var allowed = category === 'StrictlyNecessary';
        var known = allowed;
        if (!allowed) {
            var categories = consentState && consentState.categories ? consentState.categories : readConsentCategories();
            if (categories) {
                known = true;
                allowed = categories[category] === true;
            }
        }
        if (allowed) {
            if (this.first || this.last) {
                writeStored({
                    v: SCHEMA_VERSION,
                    visitorKey: this.visitorKey,
                    first: this.first,
                    last: this.last,
                    updatedAt: new Date().toISOString()
                });
                this.persisted = true;
            } else {
                removeStored();
                this.persisted = false;
            }
            return;
        }
        if (known) {
            // Declined, withdrawn, or not answered since the consent config changed: nothing left behind.
            removeStored();
            this.persisted = false;
        }
    };

    AttributionEngine.prototype._listenForConsent = function () {
        var self = this;
        if (this.listening || typeof document === 'undefined') return;
        this.listening = true;
        document.addEventListener(CONSENT_EVENT, function (event) {
            try {
                self._persistIfAllowed(event ? event.detail : null);
            } catch (e) { /* best-effort */ }
        });
    };

    AttributionEngine.prototype._beacon = function (touch) {
        var config = this.config;
        if (!config || !config.isEnabled || !config.beaconEnabled || !this.appId) return;
        var key = this.visitorKey + '|' + (touch.landingPath || '');
        if (this.beaconed[key]) return;
        this.beaconed[key] = true;
        try {
            // appId rides in the query string too: the server's rate-limit partition reads it and it must match the body.
            fetch(this.apiRoot + '/attribution/touch?appId=' + encodeURIComponent(this.appId), {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ appId: this.appId, visitorKey: this.visitorKey, touch: touch, platform: 'web' }),
                keepalive: true
            }).catch(function () { return undefined; });
        } catch (e) { /* best-effort */ }
    };

    var engine = new AttributionEngine();

    // ===== Claim for a signed-in session =========================================================
    // A Razor app keeps the user's JWT in the server session, so the payload goes to the same-origin proxy
    // (WildwoodAttributionProxyController), which forwards it with the session token. That is how a sign-in
    // provider signup, which has no registration request to carry the payload, is attributed. The touches are
    // cleared only when the proxy answers OK. A marker records an attempt that did not succeed, so a failing claim
    // is not re-sent on every page for the rest of the browser session.
    var CLAIM_MARKER_KEY = 'ww_attribution_claim_attempted';

    function readClaimMarker() {
        try { return window.sessionStorage.getItem(CLAIM_MARKER_KEY) === '1'; } catch (e) { return false; }
    }

    function writeClaimMarker(attempted) {
        try {
            if (attempted) window.sessionStorage.setItem(CLAIM_MARKER_KEY, '1');
            else window.sessionStorage.removeItem(CLAIM_MARKER_KEY);
        } catch (e) { /* storage blocked: best-effort */ }
    }

    function claimThroughProxy(claimUrl) {
        try {
            if (!claimUrl || typeof fetch !== 'function' || readClaimMarker()) return;
            var payload = engine.getForRegistration();
            if (!payload) return;
            writeClaimMarker(true);
            fetch(claimUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(payload)
            }).then(function (response) {
                if (response && response.ok) {
                    engine.clear();
                    writeClaimMarker(false);
                }
            }).catch(function () { return undefined; });
        } catch (e) { /* best-effort */ }
    }

    window.wildwoodAttribution = {
        initialize: function (baseUrl, appId) { return engine.initialize(baseUrl, appId); },
        captureUrl: function (url, referrer) { return engine.captureUrl(url, referrer); },
        getForRegistration: function () { return engine.getForRegistration(); },
        clear: function () { engine.clear(); },
        getState: function () { return engine.getState(); }
    };

    var script = document.currentScript;
    var scriptAppId = script ? script.getAttribute('data-app-id') : null;
    if (scriptAppId) {
        var scriptClaimUrl = script.getAttribute('data-claim-url');
        var started = window.wildwoodAttribution.initialize(script.getAttribute('data-base-url') || '', scriptAppId);
        if (scriptClaimUrl) {
            started.then(function () { claimThroughProxy(scriptClaimUrl); }, function () { return undefined; });
        }
    }
})();
