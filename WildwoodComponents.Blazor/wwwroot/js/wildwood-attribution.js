// Campaign Attribution engine for WildwoodComponents.Blazor, loaded through JS isolation by
// AttributionService. An ES-module port of @wildwood/core's AttributionService and attributionRules:
// the same storage key (ww_attribution), capture and normalization rules, and endpoints. The Razor
// attribution.js is the same engine as a classic script. Keep the three in step.
//
// Consent: touches are persisted only once the app's consent category is granted, and the gate has
// THREE states, not two (mirrors @wildwood/core AttributionService.persistIfAllowed):
//   granted  -> write the blob;
//   decided-and-not-granted (declined, withdrawn, or not answered since the consent config changed)
//            -> memory only AND remove any stored blob;
//   UNDECIDED (no consent state to read yet) -> memory only, and leave a stored blob alone.
// The decision comes from the consent engine's state (window.wildwoodConsent), which has already applied
// the config version, config.enabled and the GPC forced-off categories. With no engine state the
// ww_consent cookie is the only evidence, and it counts only when its configVersion matches the app's
// CURRENT consent config (fetched once, lazily) — a stale cookie means the visitor has not answered since
// the config changed, which is undecided, not granted. Every decision is re-checked on the
// 'wildwood:consent-change' event the consent engines dispatch, including from their initialize().
//
// Nothing here throws into the host: attribution is measurement, and a failure must never cost a page
// load or a signup.

const STORAGE_KEY = 'ww_attribution';
const CONSENT_COOKIE = 'ww_consent';
const CONSENT_EVENT = 'wildwood:consent-change';
const SCHEMA_VERSION = 1;
const DAY_MS = 24 * 60 * 60 * 1000;
const DEFAULT_WINDOW_DAYS = 30;
const SOURCE_MEDIUM_MAX = 100;
const CAMPAIGN_TERM_CONTENT_MAX = 200;
const HOST_MAX = 253;
const PATH_MAX = 500;
const EXTRA_PARAMS_JSON_MAX = 2000;
const MAX_EXTRA_PARAM_NAMES = 10;
const CLICK_ID_PARAMS = ['gclid', 'gbraid', 'wbraid', 'fbclid', 'msclkid', 'ttclid', 'li_fat_id', 'twclid', 'rdt_cid'];
const CONSENT_CATEGORIES = ['StrictlyNecessary', 'Functional', 'Analytics', 'Advertising', 'Sensitive'];
const GPC_FORCED_OFF = ['Advertising', 'Sensitive'];
const CONTROL_OR_FORMAT = /[\p{Cc}\p{Cf}]/u;
const PARAM_NAME = /^[a-z0-9_]{1,32}$/;
const CLICK_ID_VALUE = /^[A-Za-z0-9._~-]{1,200}$/;
const VISITOR_KEY = /^[A-Za-z0-9_-]{8,100}$/;

let fallbackCounter = 0;

// ---- Rules (mirrors @wildwood/core attributionRules.ts) -----------------------------------------------

function truncate(value, max) {
  if (value.length <= max) return value;
  let cut = max;
  const code = value.charCodeAt(cut - 1);
  if (code >= 0xd800 && code <= 0xdbff) cut -= 1;
  return value.slice(0, cut);
}

function normalizeToken(value, max, lowercase) {
  if (value === null || value === undefined) return null;
  let token = String(value).trim();
  if (token.length === 0 || CONTROL_OR_FORMAT.test(token)) return null;
  if (lowercase) token = token.toLowerCase();
  token = truncate(token, max).trimEnd();
  return token.length === 0 ? null : token;
}

function clampWindowDays(value, fallback) {
  const days = typeof value === 'number' && Number.isFinite(value) ? Math.trunc(value) : fallback;
  return Math.min(365, Math.max(1, days));
}

function isTouchExpired(touch, windowDays, nowMs) {
  const at = Date.parse(touch.occurredAt);
  return !Number.isFinite(at) || at < nowMs - windowDays * DAY_MS;
}

function generateVisitorKey() {
  const c = typeof crypto !== 'undefined' ? crypto : null;
  if (c && typeof c.randomUUID === 'function') return c.randomUUID();
  if (c && typeof c.getRandomValues === 'function') {
    return Array.from(c.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
  }
  fallbackCounter += 1;
  return `wv-${Date.now().toString(36)}-${fallbackCounter.toString(36)}`;
}

function parseHttpUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url : null;
  } catch {
    return null;
  }
}

function hostName(url, stripWww, includePort) {
  let host = (url.hostname || '').toLowerCase().replace(/\.$/, '');
  if (host.length === 0) return null;
  if (stripWww && host.startsWith('www.') && host.length > 4) host = host.slice(4);
  if (includePort && url.port) host = `${host}:${url.port}`;
  return host.length <= HOST_MAX ? host : null;
}

function normalizePath(pathname) {
  let path = pathname || '/';
  if (!path.startsWith('/')) path = `/${path}`;
  return truncate(path, PATH_MAX);
}

function readExtraParams(params, names) {
  const kept = {};
  let count = 0;
  for (const raw of (names || []).slice(0, MAX_EXTRA_PARAM_NAMES)) {
    const name = String(raw).trim().toLowerCase();
    if (!PARAM_NAME.test(name) || Object.prototype.hasOwnProperty.call(kept, name)) continue;
    const value = normalizeToken(params.get(name), CAMPAIGN_TERM_CONTENT_MAX, false);
    if (value !== null) {
      kept[name] = value;
      count += 1;
    }
  }
  if (count === 0) return null;
  return JSON.stringify(kept).length <= EXTRA_PARAMS_JSON_MAX ? kept : null;
}

/** One campaign touch from a landing URL and referrer, or null for a direct visit. */
function parseTouch(href, referrer, options) {
  let url;
  try {
    url = new URL(href);
  } catch {
    return null;
  }

  const params = url.searchParams;
  let source = normalizeToken(params.get('utm_source'), SOURCE_MEDIUM_MAX, true);
  let medium = normalizeToken(params.get('utm_medium'), SOURCE_MEDIUM_MAX, true);
  const campaign = normalizeToken(params.get('utm_campaign'), CAMPAIGN_TERM_CONTENT_MAX, false);
  const term = normalizeToken(params.get('utm_term'), CAMPAIGN_TERM_CONTENT_MAX, false);
  const content = normalizeToken(params.get('utm_content'), CAMPAIGN_TERM_CONTENT_MAX, false);

  let clickIdName = null;
  let clickIdValue = null;
  if (options.captureClickIds) {
    for (const name of CLICK_ID_PARAMS) {
      const raw = params.get(name);
      const value = raw === null ? '' : raw.trim();
      if (value && CLICK_ID_VALUE.test(value)) {
        clickIdName = name;
        clickIdValue = value;
        break;
      }
    }
  }

  const extraParams = readExtraParams(params, options.extraAllowedParamNames);

  let referrerHost = null;
  if (options.captureReferrer && referrer) {
    const ref = parseHttpUrl(referrer);
    const host = ref ? hostName(ref, true, false) : null;
    if (host !== null && host !== hostName(url, true, false)) referrerHost = host;
  }

  const hasUtm = source !== null || medium !== null || campaign !== null || term !== null || content !== null;
  if (!hasUtm && referrerHost !== null) {
    source = truncate(referrerHost, SOURCE_MEDIUM_MAX);
    medium = 'referral';
  }

  if (!hasUtm && clickIdName === null && referrerHost === null && extraParams === null) return null;

  return {
    source,
    medium,
    campaign,
    term,
    content,
    clickIdName,
    clickIdValue,
    referrerHost,
    landingHost: hostName(url, false, true),
    landingPath: normalizePath(url.pathname),
    extraParams,
    occurredAt: new Date().toISOString(),
  };
}

function normalizeConfig(data, fallbackAppId) {
  if (!data || typeof data !== 'object') return null;
  const isEnabled = data.isEnabled === true;
  const names = Array.isArray(data.extraAllowedParamNames) ? data.extraAllowedParamNames : [];
  return {
    appId: typeof data.appId === 'string' && data.appId ? data.appId : fallbackAppId,
    isEnabled,
    captureFirstTouch: data.captureFirstTouch !== false,
    captureLastTouch: data.captureLastTouch !== false,
    attributionWindowDays: clampWindowDays(data.attributionWindowDays, DEFAULT_WINDOW_DAYS),
    // An unrecognized category falls back to Analytics: persistence then waits for opt-in, never the reverse.
    persistenceConsentCategory: CONSENT_CATEGORIES.includes(data.persistenceConsentCategory)
      ? data.persistenceConsentCategory
      : 'Analytics',
    captureClickIds: data.captureClickIds !== false,
    captureReferrer: data.captureReferrer !== false,
    extraAllowedParamNames: names
      .filter((n) => typeof n === 'string')
      .map((n) => n.trim().toLowerCase())
      .filter((n) => PARAM_NAME.test(n))
      .slice(0, MAX_EXTRA_PARAM_NAMES),
    beaconEnabled: isEnabled && data.beaconEnabled === true,
  };
}

function sanitizeExtras(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const kept = {};
  let count = 0;
  for (const key of Object.keys(value)) {
    if (count >= MAX_EXTRA_PARAM_NAMES) break;
    if (PARAM_NAME.test(key) && typeof value[key] === 'string') {
      kept[key] = value[key];
      count += 1;
    }
  }
  return count > 0 ? kept : null;
}

function sanitizeStoredTouch(value) {
  if (!value || typeof value !== 'object') return null;
  if (typeof value.occurredAt !== 'string' || !Number.isFinite(Date.parse(value.occurredAt))) return null;
  const str = (key) => (typeof value[key] === 'string' ? value[key] : null);
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
    extraParams: sanitizeExtras(value.extraParams),
    occurredAt: value.occurredAt,
  };
}

// ---- Browser storage, consent and the landing URL ----------------------------------------------------

function readStored() {
  try {
    if (typeof localStorage === 'undefined') return null;
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed || parsed.v !== SCHEMA_VERSION || typeof parsed.visitorKey !== 'string') return null;
    return {
      visitorKey: parsed.visitorKey,
      first: sanitizeStoredTouch(parsed.first),
      last: sanitizeStoredTouch(parsed.last),
    };
  } catch {
    return null;
  }
}

function writeStored(blob) {
  try {
    if (typeof localStorage !== 'undefined') localStorage.setItem(STORAGE_KEY, JSON.stringify(blob));
  } catch {
    /* persistence is best-effort */
  }
}

function removeStored() {
  try {
    if (typeof localStorage !== 'undefined') localStorage.removeItem(STORAGE_KEY);
  } catch {
    /* best-effort */
  }
}

/** The consent engine's state, or null when no engine is loaded or it has not initialized yet. */
function readConsentEngineState() {
  try {
    const engine = typeof window !== 'undefined' ? window.wildwoodConsent : null;
    const state = engine && typeof engine.getState === 'function' ? engine.getState() : null;
    return state && state.categories ? state : null;
  } catch {
    return null;
  }
}

/** The raw ww_consent cookie, or null when absent or unreadable. */
function readConsentCookie() {
  try {
    if (typeof document === 'undefined') return null;
    const row = document.cookie.split('; ').find((r) => r.startsWith(`${CONSENT_COOKIE}=`));
    if (!row) return null;
    const cookie = JSON.parse(decodeURIComponent(row.substring(CONSENT_COOKIE.length + 1)));
    return cookie && typeof cookie.consentString === 'string' ? cookie : null;
  } catch {
    return null;
  }
}

/** Granted categories as { Name: true } from a consent string. */
function decodeConsentString(consentString) {
  const categories = {};
  for (const part of String(consentString || '').split(',')) {
    const name = part.trim();
    if (CONSENT_CATEGORIES.includes(name)) categories[name] = true;
  }
  return categories;
}

/** The standardized Global Privacy Control DOM signal. */
function readGpc() {
  try {
    return typeof navigator !== 'undefined' && navigator.globalPrivacyControl === true;
  } catch {
    return false;
  }
}

/** The three fields of the consent config a cookie has to be judged against, or null. */
function normalizeConsentConfig(data) {
  if (!data || typeof data !== 'object') return null;
  return {
    enabled: data.enabled === true,
    version: typeof data.version === 'number' ? data.version : -1,
    honorGpc: data.honorGpc === true,
  };
}

function readLanding() {
  try {
    if (typeof window === 'undefined' || !window.location || typeof window.location.href !== 'string') return null;
    const referrer = typeof document !== 'undefined' && typeof document.referrer === 'string' ? document.referrer : '';
    return { href: window.location.href, referrer: referrer || null };
  } catch {
    return null;
  }
}

/** The API root, whether the configured base URL already ends in /api or not. */
function apiRoot(baseUrl) {
  const root = String(baseUrl || '').replace(/\/+$/, '');
  return /\/api$/i.test(root) ? root : `${root}/api`;
}

// ---- Engine --------------------------------------------------------------------------------------------

class AttributionEngine {
  constructor() {
    this.apiRoot = '/api';
    this.appId = '';
    this.config = null;
    this.visitorKey = generateVisitorKey();
    this.first = null;
    this.last = null;
    this.persisted = false;
    this.initPromise = null;
    this.beaconed = new Set();
    this.listening = false;
    this.consentConfig = null;
    this.consentConfigPromise = null;
  }

  initialize(baseUrl, appId) {
    if (!this.initPromise) {
      // Read the URL before anything awaits: the app may navigate as soon as it renders.
      const landing = readLanding();
      this.apiRoot = apiRoot(baseUrl);
      this.appId = appId || '';
      // Armed before the config fetch, so a consent decision made while that is in flight is not missed.
      this._listenForConsent();
      this.initPromise = this._run(landing).catch(() => undefined);
    }
    return this.initPromise.then(() => this.getState());
  }

  captureUrl(url, referrer) {
    try {
      if (this.config && !this.config.isEnabled) return null;
      const touch = this._capture(url, referrer || null);
      if (!touch) return null;
      this._persistIfAllowed(null);
      this._beacon(touch);
      return touch;
    } catch {
      return null;
    }
  }

  getForRegistration() {
    if (this.config && !this.config.isEnabled) return null;
    if (!this.first && !this.last) return null;
    return {
      version: SCHEMA_VERSION,
      visitorKey: this.visitorKey,
      firstTouch: this.first,
      lastTouch: this.last,
      platform: 'web',
      sdk: 'dotnet',
    };
  }

  clear() {
    this.first = null;
    this.last = null;
    this.persisted = false;
    removeStored();
  }

  getState() {
    return {
      visitorKey: this.visitorKey,
      first: this.first,
      last: this.last,
      persisted: this.persisted,
      config: this.config,
    };
  }

  async _run(landing) {
    const configPromise = this.appId ? this._fetchConfig() : Promise.resolve(null);
    const stored = readStored();
    this.config = await configPromise;

    if (this.config && !this.config.isEnabled) {
      // Attribution is off for this app: hold nothing, and remove what an earlier visit stored.
      this.first = null;
      this.last = null;
      this.persisted = false;
      removeStored();
      return;
    }

    if (stored) {
      if (VISITOR_KEY.test(stored.visitorKey)) this.visitorKey = stored.visitorKey;
      const anchor = stored.first || stored.last;
      if (anchor && !isTouchExpired(anchor, this._windowDays(), Date.now())) {
        this.first = stored.first || this.first;
        this.last = this.last || stored.last;
      }
    }

    const touch = landing ? this._capture(landing.href, landing.referrer) : null;
    this._persistIfAllowed(null);
    if (touch) this._beacon(touch);
  }

  async _fetchConfig() {
    try {
      const response = await fetch(`${this.apiRoot}/attribution/config?appId=${encodeURIComponent(this.appId)}`, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) return null;
      return normalizeConfig(await response.json(), this.appId);
    } catch {
      // Fail open for capture (registration still carries the touch) and closed for persistence.
      return null;
    }
  }

  _windowDays() {
    return this.config ? this.config.attributionWindowDays : DEFAULT_WINDOW_DAYS;
  }

  _capture(href, referrer) {
    const config = this.config;
    const touch = parseTouch(href, referrer, {
      captureClickIds: config ? config.captureClickIds : true,
      captureReferrer: config ? config.captureReferrer : true,
      extraAllowedParamNames: config ? config.extraAllowedParamNames : [],
    });
    if (!touch) return null; // a direct visit never overwrites the stored touches
    if (this.first && isTouchExpired(this.first, this._windowDays(), Date.now())) {
      this.first = null;
      this.last = null;
    }
    this.last = touch;
    this.first = this.first || touch;
    return touch;
  }

  _persistIfAllowed(consentState) {
    const config = this.config;
    if (!config || !config.isEnabled) return; // no config (or attribution off): memory only

    // Nothing captured and nothing stored: no decision is needed, so no consent config is fetched
    // for a visitor who never arrived on a campaign.
    if (!this.first && !this.last && !readStored()) return;

    const decision = this._consentDecision(config.persistenceConsentCategory, consentState);
    if (!decision) return; // UNDECIDED: memory only, and a stored blob is left alone

    if (decision.granted) {
      if (this.first || this.last) {
        writeStored({
          v: SCHEMA_VERSION,
          visitorKey: this.visitorKey,
          first: this.first,
          last: this.last,
          updatedAt: new Date().toISOString(),
        });
        this.persisted = true;
      } else {
        removeStored();
        this.persisted = false;
      }
      return;
    }

    // Declined, withdrawn, or not answered since the consent config changed: memory only, nothing left behind.
    removeStored();
    this.persisted = false;
  }

  /**
   * The persistence decision: `{ granted }` when the visitor's consent state is known, and null while
   * it is UNDECIDED. Mirrors @wildwood/core, where `consent.getState() === null` is the undecided case.
   */
  _consentDecision(category, consentState) {
    if (category === 'StrictlyNecessary') return { granted: true };

    // 1. The consent engine is authoritative: its state already applies the config version,
    //    config.enabled and the GPC forced-off categories.
    const state = consentState && consentState.categories ? consentState : readConsentEngineState();
    if (state) return { granted: state.categories[category] === true };

    // 2. No engine state (consent is not on this page, or it has not initialized): the ww_consent
    //    cookie is the only evidence, and it is judged against the app's current consent config.
    const consentConfig = this.consentConfig;
    if (!consentConfig) {
      this._loadConsentConfig();
      return null; // cannot tell a current cookie from a stale one yet
    }
    if (!consentConfig.enabled) return { granted: false }; // consent is off for the app: nothing is granted
    const cookie = readConsentCookie();
    if (!cookie || cookie.configVersion !== consentConfig.version) return { granted: false };

    const categories = decodeConsentString(cookie.consentString);
    if (consentConfig.honorGpc && readGpc()) {
      for (const c of GPC_FORCED_OFF) categories[c] = false;
    }
    return { granted: categories[category] === true };
  }

  /** Loads the consent config once per page load, then re-runs the gate with what it learned. */
  _loadConsentConfig() {
    if (this.consentConfigPromise || !this.appId) return;
    try {
      this.consentConfigPromise = fetch(`${this.apiRoot}/consent/config?appId=${encodeURIComponent(this.appId)}`, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      })
        .then((response) => (response.ok ? response.json() : null))
        .then((data) => {
          this.consentConfig = normalizeConsentConfig(data);
        })
        .catch(() => undefined)
        .then(() => {
          // A failure leaves the config null, so the gate stays UNDECIDED: fail closed for persistence.
          this._persistIfAllowed(null);
        });
    } catch {
      /* best-effort: persistence stays memory-only */
    }
  }

  _listenForConsent() {
    if (this.listening || typeof document === 'undefined') return;
    this.listening = true;
    document.addEventListener(CONSENT_EVENT, (event) => {
      try {
        this._persistIfAllowed(event ? event.detail : null);
      } catch {
        /* best-effort */
      }
    });
  }

  _beacon(touch) {
    const config = this.config;
    if (!config || !config.isEnabled || !config.beaconEnabled || !this.appId) return;
    const key = `${this.visitorKey}|${touch.landingPath || ''}`;
    if (this.beaconed.has(key)) return;
    this.beaconed.add(key);
    try {
      // appId rides in the query string too: the server's rate-limit partition reads it, and it must match the body.
      fetch(`${this.apiRoot}/attribution/touch?appId=${encodeURIComponent(this.appId)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ appId: this.appId, visitorKey: this.visitorKey, touch, platform: 'web' }),
        keepalive: true,
      }).catch(() => undefined);
    } catch {
      /* best-effort */
    }
  }
}

const engine = new AttributionEngine();

export function initialize(baseUrl, appId) {
  return engine.initialize(baseUrl, appId);
}
export function captureUrl(url, referrer) {
  return engine.captureUrl(url, referrer);
}
export function getForRegistration() {
  return engine.getForRegistration();
}
export function clear() {
  engine.clear();
}
export function getState() {
  return engine.getState();
}
