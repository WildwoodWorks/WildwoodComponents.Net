// Wildwood Consent engine (Blazor JS-isolation ES module).
// Faithful vanilla-JS port of @wildwood/core ConsentService. Owns the browser-only concerns the
// Blazor wrapper cannot do in C#: fetch the merged config + script registry, read/write the
// first-party consent cookie, read GPC, apply the show/suppress decision table, and inject scripts
// BY CATEGORY only after consent. Block-before-consent: no gated script loads until its category is
// consented to. StrictlyNecessary may load immediately.

const NON_NECESSARY = ['Functional', 'Analytics', 'Advertising', 'Sensitive'];
const GPC_FORCED_OFF = ['Advertising', 'Sensitive'];

function emptyCategories() {
  return { StrictlyNecessary: true, Functional: false, Analytics: false, Advertising: false, Sensitive: false };
}

class ConsentEngine {
  constructor() {
    this.baseUrl = '';
    this.appId = '';
    this.cookieName = 'ww_consent';
    this.cookieDays = 180;
    this.config = null;
    this.state = null;
    this.injectedIds = new Set();
  }

  /**
   * Initializes consent and then announces the state, restored or defaulted, exactly once. Without that
   * announcement a returning visitor with a valid cookie never produces a change event, so a consumer
   * gated on consent (Campaign Attribution's persistence) would wait forever. Mirrors @wildwood/core
   * ConsentService.initialize(); later changes emit from _applyCategories and withdraw.
   */
  async initialize(baseUrl, appId, options) {
    const result = await this._initializeState(baseUrl, appId, options);
    this._emitChange();
    return result;
  }

  async _initializeState(baseUrl, appId, options) {
    this.baseUrl = (baseUrl || '').replace(/\/$/, '');
    this.appId = appId;
    if (options && options.cookieName) this.cookieName = options.cookieName;
    if (options && options.cookieDays != null) this.cookieDays = options.cookieDays;
    this.injectedIds = new Set();

    this.config = await this._fetchConfig();
    const gpcPresent = this._readGpc();
    const cookie = this._readCookie();
    const visitorKey = (cookie && cookie.visitorKey) || this._generateVisitorKey();
    const hasValidCookie = !!cookie && cookie.configVersion === this.config.version && this.config.enabled;

    let categories = emptyCategories();
    let decided = false;
    if (hasValidCookie) {
      categories = this._decode(cookie.consentString);
      decided = true;
    }
    if (this.config.enabled && this.config.honorGpc && gpcPresent) {
      for (const c of GPC_FORCED_OFF) categories[c] = false;
    }
    this.state = { visitorKey, categories, configVersion: this.config.version, decided, gpcPresent };

    if (!this.config.enabled) return this._result(false);

    this._injectCategory('StrictlyNecessary');

    if (decided) {
      this._injectConsented();
      return this._result(false);
    }

    const shouldShow = this._shouldShowBanner();
    if (!shouldShow) {
      await this._applyNonTargetDefault(gpcPresent);
      return this._result(false);
    }
    // GPC is already applied to the in-memory state (Advertising + Sensitive off); the decision is
    // recorded when the visitor acts. Do NOT persist a cookie here, or the banner would be
    // suppressed on the next load before the visitor ever chose.
    return this._result(true);
  }

  async acceptAll() {
    const cats = emptyCategories();
    for (const c of this._activeCategories()) cats[c] = true;
    return await this._applyCategories(cats, 'AcceptAll');
  }

  async rejectAll() {
    return await this._applyCategories(emptyCategories(), 'RejectAll');
  }

  async setCategories(selection) {
    const cats = emptyCategories();
    for (const c of this._activeCategories()) cats[c] = !!(selection && selection[c] === true);
    return await this._applyCategories(cats, 'Custom');
  }

  async withdraw() {
    if (!this.config || !this.state) return null; // not initialized
    // Record the withdrawal (reject-all), then clear the stored cookie so a reload re-prompts.
    this.state.categories = emptyCategories();
    this.state.decided = false;
    await this._record('RejectAll', this._encode(this.state.categories));
    this._clearCookie();
    this._emitChange();
    return this.state;
  }

  getState() {
    return this.state;
  }

  _result(shouldShowBanner) {
    return { config: this.config, state: this.state, shouldShowBanner };
  }

  async _fetchConfig() {
    const res = await fetch(`${this.baseUrl}/api/consent/config?appId=${encodeURIComponent(this.appId)}`, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });
    if (!res.ok) throw new Error(`consent config failed: ${res.status}`);
    return await res.json();
  }

  _shouldShowBanner() {
    if (!this.config.geo || !this.config.geo.aware) return true;
    return this.config.geo.inTarget === true;
  }

  /**
   * Tells consent-gated consumers (the Campaign Attribution engine) that the decision changed. A DOM event,
   * so they need no reference to this module.
   */
  _emitChange() {
    if (typeof document === 'undefined' || typeof CustomEvent !== 'function') return;
    try {
      document.dispatchEvent(new CustomEvent('wildwood:consent-change', { detail: this.state }));
    } catch {
      /* best-effort */
    }
  }

  async _applyNonTargetDefault(gpcPresent) {
    const cats = emptyCategories();
    if (this.config.nonTargetDefault === 'LoadAll') {
      for (const c of this._activeCategories()) cats[c] = true;
    }
    if (this.config.honorGpc && gpcPresent) {
      for (const c of GPC_FORCED_OFF) cats[c] = false;
    }
    this.state.categories = cats;
    this.state.decided = true;
    this._injectConsented();
    await this._persist(gpcPresent ? 'Gpc' : 'NonTargetDefault');
    // No _emitChange here: this runs only inside initialize(), which emits once for every path.
  }

  async _applyCategories(cats, method) {
    if (this.config.honorGpc && this.state.gpcPresent) {
      for (const c of GPC_FORCED_OFF) cats[c] = false;
    }
    this.state.categories = cats;
    this.state.decided = true;
    this._injectConsented();
    await this._persist(method);
    this._emitChange();
    return this.state;
  }

  async _persist(method) {
    const consentString = this._encode(this.state.categories);
    this._writeCookie({ visitorKey: this.state.visitorKey, consentString, configVersion: this.config.version });
    await this._record(method, consentString);
  }

  async _record(method, consentString) {
    try {
      await fetch(`${this.baseUrl}/api/consent/record?appId=${encodeURIComponent(this.config.appId)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          appId: this.config.appId,
          visitorKey: this.state.visitorKey,
          consentString,
          method,
          gpcPresent: this.state.gpcPresent,
          configVersion: this.config.version,
        }),
      });
    } catch (e) {
      console.warn('[wildwoodConsent] failed to record decision', e);
    }
  }

  _injectConsented() {
    for (const c of NON_NECESSARY) {
      if (this.state.categories[c]) this._injectCategory(c);
    }
  }

  _injectCategory(category) {
    if (!this.config || !this.config.scripts) return;
    for (const s of this.config.scripts) {
      if (s.category === category) this._injectScript(s);
    }
  }

  _injectScript(s) {
    if (typeof document === 'undefined') return;
    if (this.injectedIds.has(s.id)) return;
    this.injectedIds.add(s.id);
    const target = s.loadPosition === 'BodyEnd' ? document.body : document.head;
    if (!target) return;

    if (s.injectionMode === 'ExternalSrc' && s.src) {
      const el = document.createElement('script');
      el.src = s.src;
      if (s.loadStrategy === 'Async') el.async = true;
      else if (s.loadStrategy === 'Defer') el.defer = true;
      el.setAttribute('data-ww-consent', s.category);
      target.appendChild(el);
    } else if (s.injectionMode === 'InlineSnippet' && s.snippet) {
      this._injectSnippet(s.snippet, target, s.category);
    }
  }

  _injectSnippet(snippet, target, category) {
    const template = document.createElement('template');
    template.innerHTML = String(snippet).trim();
    const nodes = Array.from(template.content.childNodes);
    if (nodes.length === 0) {
      const el = document.createElement('script');
      el.textContent = snippet;
      el.setAttribute('data-ww-consent', category);
      target.appendChild(el);
      return;
    }
    for (const node of nodes) {
      if (node.nodeName === 'SCRIPT') {
        const el = document.createElement('script');
        for (const attr of Array.from(node.attributes)) el.setAttribute(attr.name, attr.value);
        el.textContent = node.textContent;
        el.setAttribute('data-ww-consent', category);
        target.appendChild(el);
      } else {
        target.appendChild(node.cloneNode(true));
      }
    }
  }

  _readGpc() {
    return typeof navigator !== 'undefined' && navigator.globalPrivacyControl === true;
  }

  _readCookie() {
    if (typeof document === 'undefined') return null;
    const row = document.cookie.split('; ').find((r) => r.startsWith(this.cookieName + '='));
    if (!row) return null;
    try {
      const parsed = JSON.parse(decodeURIComponent(row.substring(this.cookieName.length + 1)));
      return parsed && typeof parsed.visitorKey === 'string' ? parsed : null;
    } catch {
      return null;
    }
  }

  _writeCookie(cookie) {
    if (typeof document === 'undefined') return;
    const value = encodeURIComponent(JSON.stringify(cookie));
    const maxAge = this.cookieDays * 24 * 60 * 60;
    const secure = typeof location !== 'undefined' && location.protocol === 'https:' ? '; Secure' : '';
    document.cookie = `${this.cookieName}=${value}; Max-Age=${maxAge}; Path=/; SameSite=Lax${secure}`;
  }

  _clearCookie() {
    if (typeof document === 'undefined') return;
    document.cookie = `${this.cookieName}=; Max-Age=0; Path=/; SameSite=Lax`;
  }

  _generateVisitorKey() {
    if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID();
    // Match the canonical TS engine's no-crypto fallback: add sub-millisecond entropy so two
    // visitors landing in the same millisecond don't collide on the same visitorKey.
    const perf = typeof performance !== 'undefined' ? performance.now() : 0;
    return 'v-' + Date.now().toString(36) + '-' + perf.toString(36);
  }

  _encode(categories) {
    return NON_NECESSARY.filter((c) => categories[c]).join(',');
  }

  _decode(consentString) {
    const cats = emptyCategories();
    if (!consentString) return cats;
    for (const part of consentString.split(',')) {
      const c = part.trim();
      if (NON_NECESSARY.indexOf(c) >= 0) cats[c] = true;
    }
    return cats;
  }

  _activeCategories() {
    const active = (this.config && this.config.categories) || [];
    return NON_NECESSARY.filter((c) => active.indexOf(c) >= 0);
  }
}

const engine = new ConsentEngine();
if (typeof window !== 'undefined') window.wildwoodConsent = engine;

export function initialize(baseUrl, appId, options) {
  return engine.initialize(baseUrl, appId, options);
}
export function acceptAll() {
  return engine.acceptAll();
}
export function rejectAll() {
  return engine.rejectAll();
}
export function setCategories(selection) {
  return engine.setCategories(selection);
}
export function withdraw() {
  return engine.withdraw();
}
export function getState() {
  return engine.getState();
}

// ---- Keeping the fixed banner off the page's own content ----
//
// The banner is `position: fixed` against an edge of the viewport with a very high z-index, so
// anything the host anchors to that edge sits underneath it. It was found in the React package
// covering the send button of a host's AI assistant: the button was visible and enabled, and
// clicking it did nothing, because the banner was taking the pointer events.
//
// The host cannot leave room for it on its own - the height depends on the configured copy and on
// how that copy wraps - so the banner measures itself, publishes `--ww-consent-height`, and by
// default adds its height to the page's padding on the edge it is anchored to. All of it is undone
// when the banner goes. Ported from @wildwood/react's ConsentBanner `reserveSpace` effect; which
// edge - and whether there is one at all - is a decision only .NET has to make, because the
// Blazor/Razor banner has three positions (bottomBar, topBar, corner) where React's has one.
//
// The bookkeeping between those two paragraphs is not one line, so it is kept as pure functions
// with no DOM in them, LINE FOR LINE IDENTICAL to the copy in
// WildwoodComponents.Razor/wwwroot/js/consent.js. That copy is the one Node can require, so it
// carries the self-test (WildwoodComponents.Tests/Razor/js/consent-reserve-space.selftest.mjs)
// and AttributionConsentGateSourceTests asserts the two are the same text. Change one, change both.
const CONSENT_HEIGHT_VAR = '--ww-consent-height';
const SPACE_KEY_ATTR = 'data-ww-consent-space';

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

// Page-scoped, and keyed PER BANNER rather than held in one "the last call wins" variable: two
// <ConsentBanner> in the same circuit share one scoped IConsentService, and so one cached copy of
// this module, and neither may release or double-count the other's reservation.
const _space = { ledger: createSpaceLedger(), held: Object.create(null), seed: 0 };

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
 * instead of stacking a second, so an interop call that arrives twice cannot strand padding.
 */
export function reserveBannerSpace(el, reserve, key) {
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
    // Guarded: older runtimes have no ResizeObserver, and a consent banner must never be the reason
    // a host's page fails to render. Without one the measurement above simply stands alone.
    var observer = null;
    if (typeof ResizeObserver !== 'undefined') {
        observer = new ResizeObserver(measure);
        observer.observe(el);
    }
    _space.held[token] = { el: el, observer: observer };
    return token;
}

/**
 * Gives one banner's reservation back: its observer, its share of the page's padding, and - once
 * it was the last one up - the published height and the page's own inline padding, restored
 * exactly as found. A token that holds nothing is a no-op, so releasing twice, or releasing a
 * banner that never reserved, cannot disturb another banner that is still up.
 *
 * Takes the token, not the element, so it still works from Blazor's Dispose: by then the banner's
 * DOM node is gone and an ElementReference no longer resolves. An element is accepted too, for a
 * caller that has the banner but not its token.
 */
export function releaseBannerSpace(key) {
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

// ---- Focus trap (WCAG 2.2 AA: the preferences modal must be focus-trapped while open) ----
const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
let _trapEl = null;
let _trapHandler = null;
let _trapPrev = null;

export function trapFocus(el) {
  if (!el || typeof document === 'undefined') return;
  releaseFocus();
  _trapEl = el;
  _trapPrev = document.activeElement;
  const focusables = () => Array.from(el.querySelectorAll(FOCUSABLE));
  const first = focusables()[0];
  if (first) first.focus();
  _trapHandler = (e) => {
    if (e.key !== 'Tab') return;
    const items = focusables();
    if (items.length === 0) return;
    const f = items[0];
    const l = items[items.length - 1];
    if (e.shiftKey && document.activeElement === f) {
      e.preventDefault();
      l.focus();
    } else if (!e.shiftKey && document.activeElement === l) {
      e.preventDefault();
      f.focus();
    }
  };
  el.addEventListener('keydown', _trapHandler);
}

export function releaseFocus() {
  if (_trapEl && _trapHandler) _trapEl.removeEventListener('keydown', _trapHandler);
  if (_trapPrev && _trapPrev.focus) _trapPrev.focus();
  _trapEl = null;
  _trapHandler = null;
  _trapPrev = null;
}
