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

  async initialize(baseUrl, appId, options) {
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
  }

  async _applyCategories(cats, method) {
    if (this.config.honorGpc && this.state.gpcPresent) {
      for (const c of GPC_FORCED_OFF) cats[c] = false;
    }
    this.state.categories = cats;
    this.state.decided = true;
    this._injectConsented();
    await this._persist(method);
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
