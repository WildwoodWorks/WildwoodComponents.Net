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
 * Funnel tracking: when the app config turns it on, window.wildwoodAttribution.track(name, { label, value }),
 * trackCta(label) and flush() send funnel events to api/attribution/events, and the auto listeners record
 * page views, scroll depth, engagement and [data-ww-cta] clicks. The tracker is the shared block marked
 * "BEGIN shared funnel tracker", the same text as in the Blazor engine; the registration scripts
 * (token-registration.js, regsub-signup.js) report the signup steps through track().
 *
 * Consent: touches are persisted only once the app's consent category is granted, and the gate has
 * THREE states, not two (mirrors @wildwood/core AttributionService.persistIfAllowed):
 *   granted  -> write the blob;
 *   decided-and-not-granted (declined, withdrawn, or not answered since the consent config changed)
 *            -> memory only AND remove any stored blob;
 *   UNDECIDED (no consent state to read yet) -> memory only, and leave a stored blob alone.
 * The decision comes from consent.js's state (window.wildwoodConsent), which has already applied the
 * config version, config.enabled and the GPC forced-off categories. With no engine state the ww_consent
 * cookie is the only evidence, and it counts only when its configVersion matches the app's CURRENT
 * consent config (fetched once, lazily) — a stale cookie means the visitor has not answered since the
 * config changed, which is undecided, not granted. Every decision is re-checked on the
 * 'wildwood:consent-change' event consent.js dispatches, including from its initialize(). Nothing here
 * throws into the page.
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
    var GPC_FORCED_OFF = ['Advertising', 'Sensitive'];
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
        var config = {
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
        return applyFunnelConfig(config, data);
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
            var session = sanitizeSessionFields(parsed);
            return {
                visitorKey: parsed.visitorKey,
                first: sanitizeStoredTouch(parsed.first),
                last: sanitizeStoredTouch(parsed.last),
                sessionKey: session.sessionKey,
                lastActivityAt: session.lastActivityAt,
                sessionCount: session.sessionCount
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

    /** consent.js's state for this app, or null when it is absent or has not initialized yet. */
    function readConsentEngineState(appId) {
        try {
            var engine = window.wildwoodConsent;
            var state = engine && typeof engine.getState === 'function' ? engine.getState(appId) : null;
            return state && state.categories ? state : null;
        } catch (e) {
            return null;
        }
    }

    /** The raw ww_consent cookie, or null when absent or unreadable. */
    function readConsentCookie() {
        try {
            var rows = document.cookie.split('; ');
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].indexOf(CONSENT_COOKIE + '=') !== 0) continue;
                var cookie = JSON.parse(decodeURIComponent(rows[i].substring(CONSENT_COOKIE.length + 1)));
                return cookie && typeof cookie.consentString === 'string' ? cookie : null;
            }
            return null;
        } catch (e) {
            return null;
        }
    }

    /** Granted categories as { Name: true } from a consent string. */
    function decodeConsentString(consentString) {
        var categories = {};
        var parts = String(consentString || '').split(',');
        for (var i = 0; i < parts.length; i++) {
            var name = parts[i].trim();
            if (CONSENT_CATEGORIES.indexOf(name) >= 0) categories[name] = true;
        }
        return categories;
    }

    /** The standardized Global Privacy Control DOM signal. */
    function readGpc() {
        try {
            return typeof navigator !== 'undefined' && navigator.globalPrivacyControl === true;
        } catch (e) {
            return false;
        }
    }

    /** The three fields of the consent config a cookie has to be judged against, or null. */
    function normalizeConsentConfig(data) {
        if (!data || typeof data !== 'object') return null;
        return {
            enabled: data.enabled === true,
            version: typeof data.version === 'number' ? data.version : -1,
            honorGpc: data.honorGpc === true
        };
    }

    function apiRoot(baseUrl) {
        var root = String(baseUrl || '').replace(/\/+$/, '');
        return /\/api$/i.test(root) ? root : root + '/api';
    }

    // ---- BEGIN shared funnel tracker ----
    // Port of @wildwood/core attribution/funnelTracker.ts, the pre-consent sessionStorage mirror from
    // attributionService.ts and the signup error categories from react-shared signupFunnel.ts. This block
    // is the SAME TEXT in the Blazor engine (wwwroot/js/wildwood-attribution.js, an ES module) and the
    // Razor engine (wwwroot/js/attribution.js, a classic script): AttributionFunnelSourceTests compares
    // the two line by line (trimmed), so change both. It is written without ES2015 syntax so it runs
    // unchanged in either file, and it leans only on what both files define above it: SCHEMA_VERSION,
    // PATH_MAX, VISITOR_KEY, generateVisitorKey, normalizeToken and sanitizeStoredTouch.
    //
    // Like the rest of attribution it never throws into the host page, and it touches no DOM until the
    // app config turns funnel tracking on.

    var SESSION_STORAGE_KEY = 'ww_attribution_session';
    var SESSION_TIMEOUT_MS = 30 * 60 * 1000;
    var FLUSH_INTERVAL_MS = 5000;
    var MAX_EVENTS_PER_REQUEST = 25;
    var MAX_PENDING_BEFORE_CONFIG = 50;
    var MAX_QUEUED_EVENTS = 200;
    var ENGAGED_VISIBLE_MS = 10000;
    var SCROLL_MILESTONES = [25, 50, 75, 100];
    var MAX_TIME_ON_PAGE_SECONDS = 86400;
    var FUNNEL_LABEL_MAX = 100;
    var MAX_BODY_CHARS = 30000;
    var SCROLL_THROTTLE_MS = 150;
    var MAX_CUSTOM_EVENT_NAMES = 50;
    var EVENTS_PATH = 'attribution/events';
    var FUNNEL_EVENT_NAME = /^[a-z0-9_]{1,40}$/;
    var FUNNEL_CLIENT_EVENTS = [
      'page_view', 'engaged', 'scroll_depth', 'time_on_page', 'cta_click',
      'signup_view', 'signup_start', 'signup_submit', 'signup_error', 'plan_selected', 'checkout_start'
    ];
    var FUNNEL_SERVER_ONLY_EVENTS = ['signup_complete', 'trial_started', 'purchase'];
    // Sent at most once per session per label (the server dedups these too).
    var ONE_SHOT_PER_LABEL = ['signup_view', 'signup_start', 'signup_submit', 'plan_selected', 'checkout_start'];
    var SIGNUP_STEP_EVENTS = ['signup_view', 'signup_start', 'signup_submit', 'signup_error'];
    // Codes compared with case and separators removed ("USERNAME_EXISTS" and "UsernameExists" match).
    var SIGNUP_ERROR_CODES = {
      USERNAMEEXISTS: 'username_taken',
      USERNAMETAKEN: 'username_taken',
      USEREXISTS: 'email_taken',
      EMAILEXISTS: 'email_taken',
      EMAILTAKEN: 'email_taken',
      DUPLICATEEMAIL: 'email_taken',
      PASSWORDINVALID: 'password_policy',
      PASSWORDPOLICY: 'password_policy',
      INVALIDTOKEN: 'invalid_token',
      REGISTRATIONTOKENREJECTED: 'invalid_token',
      VALIDATIONERROR: 'validation',
      VALIDATION: 'validation',
      EMAILREQUIRED: 'validation',
      USERNAMEREQUIRED: 'validation',
      REGISTRATIONNOTALLOWED: 'registration_closed',
      FORBIDDEN: 'registration_closed',
      RATELIMITED: 'rate_limited',
      RATELIMITEXCEEDED: 'rate_limited',
      NETWORKERROR: 'network',
      TIMEOUT: 'network',
      SERVERERROR: 'server',
      INTERNALERROR: 'server',
      DATABASEERROR: 'server',
      EXECUTIONSTRATEGYERROR: 'server',
      USERCREATIONFAILED: 'server'
    };

    /** Adds the funnel fields to a normalized config. Funnel tracking is opt-in: every flag defaults off. */
    function applyFunnelConfig(config, data) {
      config.funnelTrackingEnabled = config.isEnabled === true && data.funnelTrackingEnabled === true;
      config.trackScrollDepth = data.trackScrollDepth === true;
      config.trackEngagement = data.trackEngagement === true;
      config.autoTrackCtaClicks = data.autoTrackCtaClicks === true;
      config.trackSignupSteps = data.trackSignupSteps === true;
      config.customEventNames = normalizeCustomEventNames(data.customEventNames);
      config.sessionStoragePersistenceBeforeConsent = data.sessionStoragePersistenceBeforeConsent === true;
      return config;
    }

    /** Custom names that are well-formed, not a standard client event and not a server-only one. */
    function normalizeCustomEventNames(value) {
      var names = [];
      if (!Array.isArray(value)) return names;
      for (var i = 0; i < value.length && names.length < MAX_CUSTOM_EVENT_NAMES; i++) {
        if (typeof value[i] !== 'string') continue;
        var name = value[i].trim().toLowerCase();
        if (FUNNEL_EVENT_NAME.test(name) && FUNNEL_CLIENT_EVENTS.indexOf(name) < 0 &&
          FUNNEL_SERVER_ONLY_EVENTS.indexOf(name) < 0 && names.indexOf(name) < 0) {
          names.push(name);
        }
      }
      return names;
    }

    /** The funnel session fields of a stored blob or mirror, each null when unusable. */
    function sanitizeSessionFields(value) {
      var source = value && typeof value === 'object' ? value : {};
      var count = source.sessionCount;
      return {
        sessionKey: typeof source.sessionKey === 'string' && VISITOR_KEY.test(source.sessionKey) ? source.sessionKey : null,
        lastActivityAt: typeof source.lastActivityAt === 'number' && isFinite(source.lastActivityAt) ? source.lastActivityAt : null,
        sessionCount: typeof count === 'number' && Math.floor(count) === count && count > 0 ? count : null
      };
    }

    /**
     * The persisted blob (ww_attribution in localStorage) and the pre-consent mirror
     * (ww_attribution_session in sessionStorage) share this shape, field for field @wildwood/core's.
     */
    function buildStoredBlob(visitorKey, first, last, session) {
      return {
        v: SCHEMA_VERSION,
        visitorKey: visitorKey,
        first: first,
        last: last,
        updatedAt: new Date().toISOString(),
        sessionKey: session ? session.sessionKey : null,
        lastActivityAt: session ? session.lastActivityAt : null,
        sessionCount: session ? session.sessionCount : null
      };
    }

    // ---- The pre-consent sessionStorage mirror. Every access is guarded: sessionStorage throws in some
    // private modes and when site data is blocked.

    function sessionStore() {
      try {
        var store = typeof sessionStorage !== 'undefined' ? sessionStorage : null;
        return store && typeof store.getItem === 'function' ? store : null;
      } catch (e) {
        return null;
      }
    }

    function readMirror() {
      try {
        var store = sessionStore();
        var raw = store ? store.getItem(SESSION_STORAGE_KEY) : null;
        if (!raw) return null;
        var parsed = JSON.parse(raw);
        if (!parsed || parsed.v !== SCHEMA_VERSION || typeof parsed.visitorKey !== 'string' ||
          !VISITOR_KEY.test(parsed.visitorKey)) {
          return null;
        }
        var session = sanitizeSessionFields(parsed);
        return {
          v: SCHEMA_VERSION,
          visitorKey: parsed.visitorKey,
          first: sanitizeStoredTouch(parsed.first),
          last: sanitizeStoredTouch(parsed.last),
          updatedAt: typeof parsed.updatedAt === 'string' ? parsed.updatedAt : '',
          sessionKey: session.sessionKey,
          lastActivityAt: session.lastActivityAt,
          sessionCount: session.sessionCount
        };
      } catch (e) {
        return null;
      }
    }

    function writeMirror(mirror) {
      try {
        var store = sessionStore();
        if (store) store.setItem(SESSION_STORAGE_KEY, JSON.stringify(mirror));
      } catch (e) {
        /* quota or private mode: best-effort */
      }
    }

    function removeMirror() {
      try {
        var store = sessionStore();
        if (store) store.removeItem(SESSION_STORAGE_KEY);
      } catch (e) {
        /* best-effort */
      }
    }

    /**
     * The mirror rule (@wildwood/core persistIfAllowed). Granted: the mirror goes, localStorage holds the
     * data now. A decision that follows a grant in this page load is a withdrawal: no mirror until the next
     * load. Otherwise the mirror is written only while the visitor is UNDECIDED (no decision yet, or a
     * consent state that has not been answered) and the app opted in; a declined visitor gets none.
     * `gate` carries consentWasGranted and mirrorBlocked between calls.
     */
    function syncSessionMirror(gate, decision, hasData, optedIn, build) {
      try {
        if (decision && decision.granted) {
          gate.consentWasGranted = true;
          removeMirror();
          return;
        }
        if (decision && gate.consentWasGranted) {
          gate.consentWasGranted = false;
          gate.mirrorBlocked = true;
        }
        var undecided = !decision || decision.undecided === true;
        if (optedIn && undecided && !gate.mirrorBlocked && hasData) writeMirror(build());
        else removeMirror();
      } catch (e) {
        /* best-effort */
      }
    }

    // ---- Signup error categories (react-shared signupFunnel.ts). A category never carries what the
    // visitor typed or a server's message.

    /** The signup_error category for an error code, falling back on the HTTP status (0 = network). */
    function signupErrorCategory(code, status) {
      var key = String(code === null || code === undefined ? '' : code).toUpperCase().replace(/[^A-Z0-9]/g, '');
      if (key.length > 0) {
        if (Object.prototype.hasOwnProperty.call(SIGNUP_ERROR_CODES, key)) return SIGNUP_ERROR_CODES[key];
        if (key.indexOf('CAPTCHA') >= 0) return 'captcha';
        if (key.indexOf('TOKEN') === 0) return 'invalid_token';
        if (key.indexOf('PASSWORD') === 0) return 'password_policy';
        if (key.indexOf('REGISTRATIONDISABLED') >= 0 || key.indexOf('NOTALLOWED') >= 0) return 'registration_closed';
      }
      if (typeof status === 'number') {
        if (status === 0) return 'network';
        if (status === 429) return 'rate_limited';
        if (status >= 500) return 'server';
        if (status === 400 || status === 422) return 'validation';
      }
      return 'unknown';
    }

    /** A signup_error label reduced to the server's category shape (^[a-z0-9_]{1,40}$). */
    function signupErrorLabel(label) {
      var category = String(label === null || label === undefined ? '' : label)
        .toLowerCase()
        .replace(/[^a-z0-9_]+/g, '_')
        .replace(/^_+|_+$/g, '')
        .slice(0, 40)
        .replace(/_+$/, '');
      return category.length > 0 ? category : 'unknown';
    }

    // ---- Helpers

    function funnelPathOf(href) {
      try {
        var path = new URL(href).pathname || '/';
        return path.length <= PATH_MAX ? path : path.slice(0, PATH_MAX);
      } catch (e) {
        return null;
      }
    }

    /** A caller-supplied page path: no query or fragment, a leading "/", capped. Null when empty. */
    function funnelScreenPath(raw) {
      var cut = String(raw).split(/[?#]/, 1)[0].trim();
      if (cut.length === 0) return null;
      var path = cut.charAt(0) === '/' ? cut : '/' + cut;
      return path.length <= PATH_MAX ? path : path.slice(0, PATH_MAX);
    }

    function findCta(target) {
      var node = target;
      if (node && typeof node.closest !== 'function') node = node.parentElement || null;
      if (!node || typeof node.closest !== 'function') return null;
      return node.closest('[data-ww-cta]');
    }

    function scrollPercent() {
      var root = document.documentElement;
      var height = Math.max(root && root.scrollHeight ? root.scrollHeight : 0,
        document.body && document.body.scrollHeight ? document.body.scrollHeight : 0);
      var viewport = window.innerHeight;
      if (!(height > 0) || typeof viewport !== 'number') return null;
      var top = typeof window.scrollY === 'number' ? window.scrollY : (root && root.scrollTop ? root.scrollTop : 0);
      return Math.min(100, ((top + viewport) / height) * 100);
    }

    /** sendBeacon with a text/plain Blob (no CORS preflight), then a keepalive fetch as the fallback. */
    function sendUnloadSafe(url, json) {
      var type = 'text/plain;charset=UTF-8';
      try {
        if (typeof navigator !== 'undefined' && navigator && typeof navigator.sendBeacon === 'function' &&
          typeof Blob !== 'undefined') {
          if (navigator.sendBeacon(url, new Blob([json], { type: type }))) return;
        }
      } catch (e) {
        /* fall back to fetch */
      }
      try {
        if (typeof fetch !== 'function') return;
        fetch(url, {
          method: 'POST',
          body: json,
          headers: { 'Content-Type': type },
          keepalive: true,
          credentials: 'omit'
        }).catch(function () { return undefined; });
      } catch (e) {
        /* analytics: drop */
      }
    }

    // ---- The tracker. `host` supplies getAppId, getVisitorKey, getLastTouch, post(path, body),
    // resolveUrl(path), captureNavigation(href) and sessionChanged().

    function FunnelTracker(host) {
      this.host = host;
      this.config = null;
      this.configResolved = false;
      this.session = null;
      this.returning = false;
      this.pending = [];
      this.queue = [];
      this.flushTimer = null;
      this.started = false;
      this.listeners = [];
      this.currentPath = null;
      this.oneShots = {};
      this.engagedSession = null;
      this.pageMilestones = {};
      this.pageMaxScroll = 0;
      this.interacted = false;
      this.visibleSince = null;
      this.pageVisibleMs = 0;
      this.loadVisibleMs = 0;
      this.engageTimer = null;
      this.scrollTimer = null;
    }

    /**
     * Applies the loaded config (null when it could not be fetched). With funnel tracking on, events
     * buffered before now are replayed and the auto listeners start; otherwise the buffer is dropped.
     */
    FunnelTracker.prototype.setConfig = function (config) {
      this.config = config;
      this.configResolved = true;
      var pending = this.pending;
      this.pending = [];
      if (!this.isEnabled()) return;
      for (var i = 0; i < pending.length; i++) {
        this._accept(pending[i].name, pending[i].options, pending[i].at, pending[i].path);
      }
      this.start();
    };

    FunnelTracker.prototype.isEnabled = function () {
      return !!this.config && this.config.isEnabled === true && this.config.funnelTrackingEnabled === true;
    };

    /** Restores a stored session. `priorVisit` marks a stored visitor that predates session tracking. */
    FunnelTracker.prototype.restoreSession = function (stored, priorVisit) {
      var s = stored || {};
      var count = typeof s.sessionCount === 'number' && Math.floor(s.sessionCount) === s.sessionCount && s.sessionCount > 0
        ? s.sessionCount
        : (priorVisit ? 1 : 0);
      var at = s.lastActivityAt;
      if (typeof s.sessionKey === 'string' && VISITOR_KEY.test(s.sessionKey) && typeof at === 'number' && isFinite(at)) {
        this.session = { sessionKey: s.sessionKey, lastActivityAt: at, sessionCount: Math.max(count, 1) };
      } else if (count > 0) {
        // Force a new session on next use, counting on from the stored one.
        this.session = { sessionKey: '', lastActivityAt: -Infinity, sessionCount: count };
      }
    };

    /** The current session, starting a new one when there is none or the last activity is 30+ minutes old. */
    FunnelTracker.prototype.getSession = function (now, bump) {
      var at = typeof now === 'number' ? now : Date.now();
      var current = this.session;
      if (!current || !current.sessionKey || at - current.lastActivityAt > SESSION_TIMEOUT_MS) {
        var next = {
          sessionKey: generateVisitorKey(),
          lastActivityAt: at,
          sessionCount: (current ? current.sessionCount : 0) + 1
        };
        this.session = next;
        this.oneShots = {};
        this.engagedSession = null;
        this._sessionChanged();
        return next;
      }
      if (bump && at > current.lastActivityAt) current.lastActivityAt = at;
      return current;
    };

    /** The session as held, without starting one. */
    FunnelTracker.prototype.peekSession = function () {
      return this.session && this.session.sessionKey ? this.session : null;
    };

    FunnelTracker.prototype.setReturning = function (value) {
      this.returning = value === true;
    };

    /** Viewport/pointer bucket: < 768 mobile, < 1024 tablet (a coarse pointer stretches it to 1280). */
    FunnelTracker.prototype.getDeviceClass = function () {
      try {
        if (typeof window !== 'undefined') {
          var width = window.innerWidth;
          if (typeof width === 'number' && width > 0) {
            var coarse = typeof window.matchMedia === 'function' && window.matchMedia('(pointer: coarse)').matches === true;
            if (width < 768) return 'mobile';
            if (width < 1024 || (coarse && width < 1280)) return 'tablet';
            return 'desktop';
          }
        }
      } catch (e) {
        /* fall through */
      }
      return 'desktop';
    };

    /** Queues a funnel event. Before the config loads it is buffered; invalid or disallowed names are dropped. */
    FunnelTracker.prototype.track = function (name, options) {
      try {
        var explicitPath = options && options.path !== null && options.path !== undefined
          ? funnelScreenPath(options.path)
          : null;
        // A page_view naming its page is a navigation.
        if (name === 'page_view' && explicitPath !== null) {
          this._notifyPath(explicitPath);
          return;
        }
        var path = explicitPath !== null ? explicitPath : this.currentPath;
        var at = Date.now();
        if (!this.configResolved) {
          if (this.pending.length < MAX_PENDING_BEFORE_CONFIG) {
            this.pending.push({ name: name, options: options, at: at, path: path });
          }
          return;
        }
        if (!this.isEnabled()) return;
        this._accept(name, options, at, path);
      } catch (e) {
        /* never throw into the host page */
      }
    };

    FunnelTracker.prototype.trackCta = function (label) {
      this.track('cta_click', { label: label });
    };

    /**
     * A navigation to `href` (captureUrl or the history hooks): a page_view when the path changed, closing
     * the previous page's time_on_page and scroll milestones first.
     */
    FunnelTracker.prototype.notifyNavigation = function (href) {
      try {
        var path = funnelPathOf(href);
        if (path !== null) this._notifyPath(path);
      } catch (e) {
        /* best-effort */
      }
    };

    FunnelTracker.prototype._notifyPath = function (path) {
      if (path === this.currentPath) return;
      if (this.currentPath !== null) this._emitTimeOnPage();
      this.currentPath = path;
      this.pageMilestones = {};
      this.pageMaxScroll = 0;
      this.pageVisibleMs = 0;
      if (this.visibleSince !== null) this.visibleSince = Date.now();
      this.track('page_view');
    };

    /** Posts every queued event now. Never rejects. */
    FunnelTracker.prototype.flush = function () {
      var self = this;
      try {
        this._clearFlushTimer();
        var batches = this._takeBatches();
        if (batches.length === 0) return Promise.resolve();
        this._sessionChanged();
        var sends = [];
        for (var i = 0; i < batches.length; i++) {
          try {
            sends.push(Promise.resolve(self.host.post(self._eventsPath(batches[i].appId), batches[i]))
              .catch(function () { return undefined; }));
          } catch (e) {
            /* analytics: drop */
          }
        }
        return Promise.all(sends).then(function () { return undefined; });
      } catch (e) {
        return Promise.resolve();
      }
    };

    /** Sends every queued event with sendBeacon (keepalive fetch as the fallback), for a page being hidden. */
    FunnelTracker.prototype.flushWithBeacon = function () {
      try {
        this._clearFlushTimer();
        var batches = this._takeBatches();
        if (batches.length === 0) return;
        this._sessionChanged();
        for (var i = 0; i < batches.length; i++) {
          try {
            var path = this._eventsPath(batches[i].appId);
            var url = this.host.resolveUrl(path);
            if (!url) {
              Promise.resolve(this.host.post(path, batches[i])).catch(function () { return undefined; });
              continue;
            }
            sendUnloadSafe(url, JSON.stringify(batches[i]));
          } catch (e) {
            /* analytics: drop */
          }
        }
      } catch (e) {
        /* analytics: drop */
      }
    };

    /** Attaches the auto listeners. Idempotent; a no-op until the config enables tracking. */
    FunnelTracker.prototype.start = function () {
      if (this.started || !this.isEnabled()) return;
      this.started = true;
      if (!this._hasDom()) return;
      try {
        this._attachListeners();
        // The landing page_view (unless captureUrl already recorded this path).
        this.notifyNavigation(window.location.href);
      } catch (e) {
        /* best-effort */
      }
    };

    FunnelTracker.prototype._sessionChanged = function () {
      try {
        this.host.sessionChanged();
      } catch (e) {
        /* best-effort */
      }
    };

    FunnelTracker.prototype._accept = function (name, options, at, path) {
      var config = this.config;
      if (!config) return;
      if (typeof name !== 'string' || !FUNNEL_EVENT_NAME.test(name) || FUNNEL_SERVER_ONLY_EVENTS.indexOf(name) >= 0) return;
      if (FUNNEL_CLIENT_EVENTS.indexOf(name) < 0 && (config.customEventNames || []).indexOf(name) < 0) return;
      if (SIGNUP_STEP_EVENTS.indexOf(name) >= 0 && !config.trackSignupSteps) return;

      var label = options && options.label !== null && options.label !== undefined
        ? normalizeToken(String(options.label), FUNNEL_LABEL_MAX, false)
        : null;
      var value = options && typeof options.value === 'number' && isFinite(options.value) ? options.value : null;

      if (name === 'cta_click') {
        if (label === null) return;
      } else if (name === 'signup_error') {
        label = signupErrorLabel(label);
      } else if (name === 'scroll_depth') {
        if (value === null || SCROLL_MILESTONES.indexOf(value) < 0 || this.pageMilestones[value]) return;
        this.pageMilestones[value] = true;
      } else if (name === 'time_on_page') {
        if (value === null || value < 0) return;
        value = Math.min(MAX_TIME_ON_PAGE_SECONDS, Math.round(value));
      }

      // time_on_page closes out time already spent; it never starts or extends a session.
      var session = name === 'time_on_page' ? (this.peekSession() || this.getSession(at)) : this.getSession(at, true);

      if (name === 'engaged') {
        if (this.engagedSession === session.sessionKey) return;
        this.engagedSession = session.sessionKey;
      } else if (ONE_SHOT_PER_LABEL.indexOf(name) >= 0) {
        var key = name + '|' + (label === null ? '' : label);
        if (this.oneShots[key]) return;
        this.oneShots[key] = true;
      }

      var event = { name: name };
      if (label !== null) event.label = label;
      if (value !== null) event.value = value;
      if (path !== null) event.path = path;
      event.clientTimestamp = new Date(at).toISOString();

      if (this.queue.length >= MAX_QUEUED_EVENTS) return;
      this.queue.push({ sessionKey: session.sessionKey, event: event });
      if (this.queue.length >= MAX_EVENTS_PER_REQUEST) this.flush();
      else this._ensureFlushTimer();
    };

    FunnelTracker.prototype._takeBatches = function () {
      var queued = this.queue;
      this.queue = [];
      var appId = this.host.getAppId();
      if (queued.length === 0 || !appId) return [];

      var base = {
        appId: appId,
        visitorKey: this.host.getVisitorKey(),
        isReturning: this.returning,
        deviceClass: this.getDeviceClass(),
        platform: 'web',
        touch: this.host.getLastTouch() || null
      };
      var batches = [];
      var i = 0;
      while (i < queued.length) {
        var sessionKey = queued[i].sessionKey;
        var size = 0;
        while (i + size < queued.length && size < MAX_EVENTS_PER_REQUEST && queued[i + size].sessionKey === sessionKey) {
          size++;
        }
        var body;
        for (;;) {
          var events = [];
          for (var j = i; j < i + size; j++) events.push(queued[j].event);
          body = {
            appId: base.appId,
            visitorKey: base.visitorKey,
            sessionKey: sessionKey,
            isReturning: base.isReturning,
            deviceClass: base.deviceClass,
            platform: base.platform,
            touch: base.touch,
            events: events
          };
          if (size === 1 || JSON.stringify(body).length <= MAX_BODY_CHARS) break;
          size = Math.ceil(size / 2);
        }
        if (JSON.stringify(body).length <= MAX_BODY_CHARS) batches.push(body);
        i += size;
      }
      return batches;
    };

    FunnelTracker.prototype._eventsPath = function (appId) {
      // appId rides in the query too: the server's rate-limit partition reads ?appId=.
      return EVENTS_PATH + '?appId=' + encodeURIComponent(appId);
    };

    FunnelTracker.prototype._ensureFlushTimer = function () {
      var self = this;
      if (this.flushTimer !== null) return;
      this.flushTimer = setTimeout(function () {
        self.flushTimer = null;
        self.flush();
      }, FLUSH_INTERVAL_MS);
    };

    FunnelTracker.prototype._clearFlushTimer = function () {
      if (this.flushTimer !== null) {
        clearTimeout(this.flushTimer);
        this.flushTimer = null;
      }
    };

    FunnelTracker.prototype._clearTimer = function (name) {
      if (this[name] !== null) clearTimeout(this[name]);
      this[name] = null;
    };

    FunnelTracker.prototype._hasDom = function () {
      return typeof window !== 'undefined' && typeof document !== 'undefined' &&
        typeof window.addEventListener === 'function' && typeof document.addEventListener === 'function';
    };

    FunnelTracker.prototype._listen = function (target, type, listener, options) {
      var safe = function (event) {
        try {
          listener(event);
        } catch (e) {
          /* never throw into the host page */
        }
      };
      var opts = options === undefined ? { passive: true } : options;
      target.addEventListener(type, safe, opts);
      this.listeners.push([target, type, safe, opts]);
    };

    FunnelTracker.prototype._attachListeners = function () {
      var self = this;
      var config = this.config;
      if (document.visibilityState !== 'hidden') this.visibleSince = Date.now();

      this._listen(document, 'visibilitychange', function () {
        if (document.visibilityState === 'hidden') {
          self._pauseVisible();
          self.flushWithBeacon();
        } else {
          self._resumeVisible();
        }
      });
      this._listen(window, 'pagehide', function () {
        self._pauseVisible();
        self._emitTimeOnPage();
        self.flushWithBeacon();
      });
      this._listen(window, 'pageshow', function () {
        if (document.visibilityState !== 'hidden') self._resumeVisible();
      });

      if (config.trackScrollDepth || config.trackEngagement) {
        this._listen(window, 'scroll', function () {
          self._markInteraction();
          if (self.scrollTimer !== null) return;
          self.scrollTimer = setTimeout(function () {
            self.scrollTimer = null;
            self._checkScroll();
          }, SCROLL_THROTTLE_MS);
        });
      }
      if (config.trackEngagement) {
        this._listen(document, 'keydown', function () { self._markInteraction(); }, { capture: true, passive: true });
        this._listen(document, 'touchstart', function () { self._markInteraction(); }, { capture: true, passive: true });
      }
      if (config.trackEngagement || config.autoTrackCtaClicks) {
        this._listen(document, 'click', function (event) {
          self._markInteraction();
          if (!self.config || !self.config.autoTrackCtaClicks) return;
          var cta = findCta(event ? event.target : null);
          var label = cta ? cta.getAttribute('data-ww-cta') : null;
          if (label !== null && label !== undefined) self.trackCta(label);
        }, { capture: true, passive: true });
      }

      this._hookHistory();
      this._listen(window, 'popstate', function () { self._onHistoryNavigation(); });
    };

    FunnelTracker.prototype._hookHistory = function () {
      var self = this;
      var history = window.history;
      if (!history || typeof history.pushState !== 'function' || typeof history.replaceState !== 'function') return;
      var wrap = function (original) {
        return function () {
          var result = original.apply(this, arguments);
          try {
            self._onHistoryNavigation();
          } catch (e) {
            /* best-effort */
          }
          return result;
        };
      };
      history.pushState = wrap(history.pushState);
      history.replaceState = wrap(history.replaceState);
    };

    FunnelTracker.prototype._onHistoryNavigation = function () {
      if (!this.started) return;
      var href = window.location ? window.location.href : null;
      if (typeof href !== 'string' || href.length === 0) return;
      this.host.captureNavigation(href);
      this.notifyNavigation(href);
    };

    FunnelTracker.prototype._markInteraction = function () {
      if (!this.config || !this.config.trackEngagement || this.interacted) return;
      this.interacted = true;
      this._checkEngaged();
    };

    FunnelTracker.prototype._checkScroll = function () {
      var percent = scrollPercent();
      if (percent === null) return;
      this.pageMaxScroll = Math.max(this.pageMaxScroll, percent);
      if (this.config && this.config.trackScrollDepth) {
        for (var i = 0; i < SCROLL_MILESTONES.length; i++) {
          var milestone = SCROLL_MILESTONES[i];
          var reached = milestone === 100 ? percent >= 99 : percent >= milestone;
          if (reached && !this.pageMilestones[milestone]) this.track('scroll_depth', { value: milestone });
        }
      }
      this._checkEngaged();
    };

    FunnelTracker.prototype._checkEngaged = function () {
      var self = this;
      this._clearTimer('engageTimer');
      if (!this.config || !this.config.trackEngagement || !this.started) return;
      var session = this.peekSession();
      if (session && this.engagedSession === session.sessionKey &&
        Date.now() - session.lastActivityAt <= SESSION_TIMEOUT_MS) {
        return;
      }
      var visible = this.loadVisibleMs + (this.visibleSince !== null ? Date.now() - this.visibleSince : 0);
      if (this.pageMaxScroll >= 50 || (this.interacted && visible >= ENGAGED_VISIBLE_MS)) {
        this.track('engaged');
        return;
      }
      if (this.interacted && this.visibleSince !== null) {
        this.engageTimer = setTimeout(function () {
          self.engageTimer = null;
          self._checkEngaged();
        }, ENGAGED_VISIBLE_MS - visible);
      }
    };

    FunnelTracker.prototype._pauseVisible = function () {
      if (this.visibleSince === null) return;
      var elapsed = Math.max(0, Date.now() - this.visibleSince);
      this.pageVisibleMs += elapsed;
      this.loadVisibleMs += elapsed;
      this.visibleSince = null;
      this._clearTimer('engageTimer');
    };

    FunnelTracker.prototype._resumeVisible = function () {
      if (this.visibleSince !== null || !this.started) return;
      this.visibleSince = Date.now();
      this._checkEngaged();
    };

    FunnelTracker.prototype._emitTimeOnPage = function () {
      if (!this.config || !this.config.trackEngagement || !this.started) return;
      var ms = this.pageVisibleMs;
      if (this.visibleSince !== null) {
        var now = Date.now();
        ms += now - this.visibleSince;
        this.loadVisibleMs += now - this.visibleSince;
        this.visibleSince = now;
      }
      this.pageVisibleMs = 0;
      var seconds = Math.round(ms / 1000);
      if (seconds >= 1) this.track('time_on_page', { value: seconds });
    };
    // ---- END shared funnel tracker ----

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
        this.consentConfig = null;
        this.consentConfigPromise = null;
        // The last URL captured, so a history-hook navigation never re-captures what captureUrl just did.
        this.lastCapturedHref = null;
        // consentWasGranted / mirrorBlocked for the pre-consent sessionStorage mirror (syncSessionMirror).
        this.mirrorGate = { consentWasGranted: false, mirrorBlocked: false };
        var self = this;
        this.tracker = new FunnelTracker({
            getAppId: function () { return self.appId; },
            getVisitorKey: function () { return self.visitorKey; },
            getLastTouch: function () { return self.last; },
            post: function (path, body) {
                return fetch(self.apiRoot + '/' + path, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                });
            },
            resolveUrl: function (path) { return self.apiRoot + '/' + path; },
            captureNavigation: function (href) { self._captureNavigation(href); },
            sessionChanged: function () {
                if (self.config) self._persistIfAllowed(null);
            }
        });
    }

    AttributionEngine.prototype.initialize = function (baseUrl, appId) {
        var self = this;
        if (!this.initPromise) {
            // Read the URL before anything awaits: the page may navigate as soon as it renders.
            var landing = { href: window.location.href, referrer: document.referrer || null };
            this.apiRoot = apiRoot(baseUrl);
            this.appId = appId || '';
            // Armed before the config fetch, so a consent decision made while that is in flight is not missed.
            this._listenForConsent();
            this.initPromise = this._run(landing).catch(function () { return undefined; });
        }
        return this.initPromise.then(function () { return self.getState(); });
    };

    AttributionEngine.prototype.captureUrl = function (url, referrer) {
        try {
            if (this.config && !this.config.isEnabled) return null;
            var touch = this._capture(url, referrer || null);
            this.lastCapturedHref = url;
            // A page_view when the path changed (a no-op until the config turns funnel tracking on).
            this.tracker.notifyNavigation(url);
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
        // The funnel session joins the new account to its funnel events.
        var session = this.tracker.getSession(Date.now(), true);
        return {
            version: SCHEMA_VERSION,
            visitorKey: this.visitorKey,
            firstTouch: this.first,
            lastTouch: this.last,
            platform: 'web',
            sdk: 'dotnet',
            sessionKey: session.sessionKey,
            deviceClass: this.tracker.getDeviceClass(),
            sessionCount: session.sessionCount
        };
    };

    AttributionEngine.prototype.clear = function () {
        this.first = null;
        this.last = null;
        this.persisted = false;
        removeStored();
        removeMirror();
        // With funnel tracking on, the visitor and session keys stay stored where consent allows.
        if (this.config && this.config.funnelTrackingEnabled) this._persistIfAllowed(null);
    };

    /**
     * Tracks a funnel event (page_view, cta_click, signup_start, a configured custom name...). Buffered
     * until the config loads; dropped when funnel tracking is off, the name is not allowed, or it is a
     * one-shot already sent this session. Never throws.
     */
    AttributionEngine.prototype.track = function (name, options) {
        this.tracker.track(name, options || undefined);
    };

    /** Tracks a cta_click with this label. */
    AttributionEngine.prototype.trackCta = function (label) {
        this.tracker.trackCta(label);
    };

    /** Sends the queued funnel events now. Never rejects. */
    AttributionEngine.prototype.flush = function () {
        return this.tracker.flush();
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
                // Attribution is off for this app: hold nothing, and remove what an earlier visit stored.
                self.first = null;
                self.last = null;
                self.persisted = false;
                self.tracker.setConfig(config);
                removeStored();
                removeMirror();
                return;
            }
            // The localStorage blob wins; the sessionStorage mirror stands in before consent (same-tab reload).
            var mirror = !stored && config && config.sessionStoragePersistenceBeforeConsent ? readMirror() : null;
            var restored = stored || mirror;
            if (restored) {
                if (VISITOR_KEY.test(restored.visitorKey)) self.visitorKey = restored.visitorKey;
                var anchor = restored.first || restored.last;
                if (anchor && !isTouchExpired(anchor, self._windowDays(), Date.now())) {
                    self.first = restored.first || self.first;
                    self.last = self.last || restored.last;
                }
                self.tracker.restoreSession(restored, stored !== null);
            }
            // Returning = a visitor known from localStorage (not the same-tab mirror) starting another session.
            var session = self.tracker.getSession();
            self.tracker.setReturning(stored !== null && session.sessionCount >= 2);

            var touch = self._capture(landing.href, landing.referrer);
            self.lastCapturedHref = landing.href;
            self._persistIfAllowed(null);
            if (touch) self._beacon(touch);
            // Starts the funnel listeners (and the landing page_view) when the app has funnel tracking on.
            self.tracker.setConfig(config);
        });
    };

    /** A navigation seen by the funnel history hooks; skips the URL captureUrl() already captured. */
    AttributionEngine.prototype._captureNavigation = function (href) {
        if (!this.config || !this.config.isEnabled || href === this.lastCapturedHref) return;
        this.lastCapturedHref = href;
        var touch = this._capture(href, null);
        if (!touch) return;
        this._persistIfAllowed(null);
        this._beacon(touch);
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
        var self = this;
        if (!config || !config.isEnabled) return;

        // With funnel tracking on, a direct visitor's visitor and session keys are worth keeping too.
        var hasData = !!(this.first || this.last || config.funnelTrackingEnabled);

        // Nothing captured and nothing stored: no decision is needed, so no consent config is fetched
        // for a visitor who never arrived on a campaign.
        if (!hasData && !readStored()) {
            removeMirror();
            return;
        }

        var decision = this._consentDecision(config.persistenceConsentCategory, consentState);
        // The same-tab sessionStorage mirror: kept only while UNDECIDED and when the app opted in.
        syncSessionMirror(this.mirrorGate, decision, hasData, config.sessionStoragePersistenceBeforeConsent, function () {
            return buildStoredBlob(self.visitorKey, self.first, self.last, self.tracker.peekSession());
        });
        if (!decision) return; // UNDECIDED: memory only, and a stored blob is left alone

        if (decision.granted) {
            if (hasData) {
                writeStored(buildStoredBlob(this.visitorKey, this.first, this.last, this.tracker.peekSession()));
                this.persisted = true;
            } else {
                removeStored();
                this.persisted = false;
            }
            return;
        }

        // Declined, withdrawn, or not answered since the consent config changed: nothing left behind.
        removeStored();
        this.persisted = false;
    };

    /**
     * The persistence decision: `{ granted }` when the visitor's consent state is known, and null while
     * it is UNDECIDED. Mirrors @wildwood/core, where `consent.getState() === null` is the undecided case.
     */
    AttributionEngine.prototype._consentDecision = function (category, consentState) {
        if (category === 'StrictlyNecessary') return { granted: true };

        // 1. consent.js is authoritative: its state already applies the config version, config.enabled
        //    and the GPC forced-off categories.
        var state = consentState && consentState.categories ? consentState : readConsentEngineState(this.appId);
        // `undecided` marks a state the visitor has not answered yet: no localStorage, but the mirror may run.
        if (state) return { granted: state.categories[category] === true, undecided: state.decided === false };

        // 2. No engine state (consent is not on this page, or it has not initialized): the ww_consent
        //    cookie is the only evidence, and it is judged against the app's current consent config.
        var consentConfig = this.consentConfig;
        if (!consentConfig) {
            this._loadConsentConfig();
            return null; // cannot tell a current cookie from a stale one yet
        }
        if (!consentConfig.enabled) return { granted: false }; // consent is off for the app: nothing is granted
        var cookie = readConsentCookie();
        // No cookie, or one written against an older consent config: the visitor has not answered this one.
        if (!cookie || cookie.configVersion !== consentConfig.version) return { granted: false, undecided: true };

        var categories = decodeConsentString(cookie.consentString);
        if (consentConfig.honorGpc && readGpc()) {
            for (var i = 0; i < GPC_FORCED_OFF.length; i++) categories[GPC_FORCED_OFF[i]] = false;
        }
        return { granted: categories[category] === true };
    };

    /** Loads the consent config once per page load, then re-runs the gate with what it learned. */
    AttributionEngine.prototype._loadConsentConfig = function () {
        var self = this;
        if (this.consentConfigPromise || !this.appId) return;
        try {
            this.consentConfigPromise = fetch(this.apiRoot + '/consent/config?appId=' + encodeURIComponent(this.appId), {
                method: 'GET',
                headers: { Accept: 'application/json' }
            }).then(function (response) {
                return response.ok ? response.json() : null;
            }).then(function (data) {
                self.consentConfig = normalizeConsentConfig(data);
            }).catch(function () {
                return undefined;
            }).then(function () {
                // A failure leaves the config null, so the gate stays UNDECIDED: fail closed for persistence.
                self._persistIfAllowed(null);
            });
        } catch (e) { /* best-effort: persistence stays memory-only */ }
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
        getState: function () { return engine.getState(); },
        /** Tracks a funnel event: track('demo_booked', { label: 'pricing', value: 1 }). Never throws. */
        track: function (name, options) { try { engine.track(name, options); } catch (e) { /* best-effort */ } },
        /** Tracks a cta_click with this label. */
        trackCta: function (label) { try { engine.trackCta(label); } catch (e) { /* best-effort */ } },
        /** Sends the queued funnel events now (before a hard navigation). Never rejects. */
        flush: function () {
            try { return engine.flush(); } catch (e) { return Promise.resolve(); }
        },
        /**
         * The signup_error category for an error code (WildwoodAPI's errorCode, e.g. USERNAME_EXISTS),
         * falling back on the HTTP status (0 for a network failure). Never reads a message.
         */
        signupErrorCategory: function (code, status) { return signupErrorCategory(code, status); }
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
