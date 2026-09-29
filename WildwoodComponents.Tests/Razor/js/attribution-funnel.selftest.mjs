// Self-test for the Campaign Attribution funnel tracker in BOTH browser engines:
//   WildwoodComponents.Blazor/wwwroot/js/wildwood-attribution.js (an ES module)
//   WildwoodComponents.Razor/wwwroot/js/attribution.js (a classic script)
//
// There is no JavaScript test harness in this repository, and the funnel genuinely lives in the
// browser: history hooks, sendBeacon, scroll and visibility listeners, localStorage and
// sessionStorage. So each engine is loaded into its own node:vm context with a small fake browser
// (a clock and timers we drive, storage maps, a fetch and a sendBeacon that record) and the same
// scenarios run against both. The last check replays one script on each and requires the two to send
// the same events, which is what keeps the near-duplicate engines honest.
//
// Covered, ported from @wildwood/core's funnelTracker and attributionService tests:
//   * config gating (funnel off drops everything) and the pre-config buffer
//   * the batched queue: at most 25 per request, flushed at 25 and every 5 s, split per session
//   * sendBeacon with a text/plain Blob on pagehide and on visibilitychange hidden
//   * the landing page_view, history pushState / replaceState / popstate, one touch per navigation
//   * scroll_depth milestones, the engaged rule and time_on_page
//   * delegated [data-ww-cta] clicks, track / trackCta / flush
//   * the custom-name allowlist, server-only names dropped, one-shot signup steps, signup_error labels
//   * the session key with its 30-minute rollover, sessionCount and isReturning
//   * device class buckets
//   * the sessionStorage mirror: written only while undecided and opted in, restored on reload,
//     moved to localStorage on grant, removed (and blocked) on decline and withdrawal
//   * getForRegistration carries sessionKey, deviceClass and sessionCount
//
// Run it directly (`node attribution-funnel.selftest.mjs`) or through
// AttributionFunnelSelfTestRunnerTests, which shells out to node and asserts exit code 0.

import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../../..');
const BLAZOR = fs.readFileSync(path.join(root, 'WildwoodComponents.Blazor/wwwroot/js/wildwood-attribution.js'), 'utf8');
const RAZOR = fs.readFileSync(path.join(root, 'WildwoodComponents.Razor/wwwroot/js/attribution.js'), 'utf8');

const API = 'https://api.example.test';
const EVENTS_URL = API + '/api/attribution/events?appId=app-1';
const KEY = /^[A-Za-z0-9_-]{8,100}$/;
const START = Date.parse('2026-09-28T12:00:00.000Z');

let failures = 0;
let checks = 0;
let scope = '';

function check(condition, what) {
    checks += 1;
    if (!condition) {
        failures += 1;
        console.error('FAIL [' + scope + ']: ' + what);
    }
}

function equal(actual, expected, what) {
    check(actual === expected, what + ' (expected ' + JSON.stringify(expected) + ', got ' + JSON.stringify(actual) + ')');
}

function deepEqual(actual, expected, what) {
    equal(JSON.stringify(actual), JSON.stringify(expected), what);
}

function mapStorage(map, opts) {
    return {
        getItem(k) {
            if (opts && opts.throws) throw new Error('blocked');
            return map.has(k) ? map.get(k) : null;
        },
        setItem(k, v) {
            if (opts && opts.throws) throw new Error('blocked');
            map.set(k, String(v));
        },
        removeItem(k) {
            if (opts && opts.throws) throw new Error('blocked');
            map.delete(k);
        },
    };
}

/** A fake browser with one engine loaded into it. */
function launch(kind, opts) {
    const o = opts || {};
    const clock = { now: o.now || START };
    const timers = new Map();
    let timerId = 0;
    const listeners = [];
    const fetches = [];
    const beacons = [];
    const local = o.local || new Map();
    const session = o.session || new Map();

    const g = {};
    g.window = g;
    g.__clock = clock;
    g.console = console;
    g.URL = URL;
    g.crypto = globalThis.crypto;
    g.innerWidth = o.width || 1280;
    g.innerHeight = 800;
    g.scrollY = 0;
    g.matchMedia = () => ({ matches: o.coarse === true });
    g.location = { href: o.href || 'https://app.example.test/pricing?utm_source=Reddit&utm_medium=paid' };
    g.localStorage = mapStorage(local);
    if (o.sessionThrows) {
        Object.defineProperty(g, 'sessionStorage', { get() { throw new Error('blocked'); } });
    } else {
        g.sessionStorage = mapStorage(session);
    }
    g.addEventListener = (type, fn) => listeners.push({ target: 'window', type, fn });
    g.removeEventListener = () => undefined;
    g.document = {
        cookie: o.cookie || '',
        referrer: o.referrer || '',
        visibilityState: 'visible',
        documentElement: { scrollHeight: 4000, scrollTop: 0 },
        body: { scrollHeight: 4000 },
        currentScript: null,
        addEventListener: (type, fn) => listeners.push({ target: 'document', type, fn }),
        removeEventListener: () => undefined,
    };
    g.history = {
        pushState(state, title, url) { g.location.href = new URL(url, g.location.href).href; },
        replaceState(state, title, url) { g.location.href = new URL(url, g.location.href).href; },
    };
    g.navigator = {
        sendBeacon(url, blob) {
            beacons.push({ url, type: blob.type, body: JSON.parse(blob.parts.join('')) });
            return true;
        },
    };
    g.Blob = class {
        constructor(parts, options) {
            this.parts = parts;
            this.type = options && options.type;
        }
    };
    g.setTimeout = (fn, ms) => {
        timerId += 1;
        timers.set(timerId, { at: clock.now + (ms || 0), fn });
        return timerId;
    };
    g.clearTimeout = (id) => timers.delete(id);
    g.fetch = (url, init) => {
        fetches.push({ url, init });
        let data = {};
        let ok = true;
        if (url.indexOf('/attribution/config') >= 0) data = o.config === undefined ? null : o.config;
        else if (url.indexOf('/consent/config') >= 0) data = o.consentConfig || null;
        if (data === null) ok = false;
        return Promise.resolve({ ok, json: () => Promise.resolve(data) });
    };
    if (o.consent) g.wildwoodConsent = { getState: () => o.consent.state };

    const context = vm.createContext(g);
    vm.runInContext('Date.now = function () { return __clock.now; };', context);

    let api;
    if (kind === 'blazor') {
        // An ES module: its exports become the returned object so the same harness can drive it.
        const names = [];
        const body = BLAZOR.replace(/^export function (\w+)/gm, (_, name) => {
            names.push(name);
            return 'function ' + name;
        });
        api = vm.runInContext('(function () {\n"use strict";\n' + body + '\nreturn {' + names.join(', ') + '};\n})()', context);
    } else {
        vm.runInContext(RAZOR, context);
        api = g.wildwoodAttribution;
    }

    function fire(target, type, event) {
        for (const l of listeners.slice()) {
            if (l.target === target && l.type === type) l.fn(event || {});
        }
    }

    function advance(ms) {
        const end = clock.now + ms;
        for (;;) {
            let next = null;
            for (const [id, t] of timers) {
                if (t.at <= end && (next === null || t.at < next[1].at)) next = [id, t];
            }
            if (next === null) break;
            timers.delete(next[0]);
            clock.now = Math.max(clock.now, next[1].at);
            next[1].fn();
        }
        clock.now = end;
    }

    function posted() {
        return fetches.filter((f) => f.url.indexOf('/attribution/events') >= 0).map((f) => ({ url: f.url, body: JSON.parse(f.init.body) }));
    }

    function events() {
        const all = [];
        for (const p of posted()) for (const e of p.body.events) all.push(e);
        for (const b of beacons) for (const e of b.body.events) all.push(e);
        return all;
    }

    return { g, api, clock, fire, advance, posted, beacons, events, local, session, fetches, listeners };
}

async function settle() {
    for (let i = 0; i < 20; i++) await new Promise((r) => setImmediate(r));
}

function funnelConfig(extra) {
    return Object.assign({
        appId: 'app-1',
        isEnabled: true,
        persistenceConsentCategory: 'StrictlyNecessary',
        funnelTrackingEnabled: true,
        trackScrollDepth: true,
        trackEngagement: true,
        autoTrackCtaClicks: true,
        trackSignupSteps: true,
        customEventNames: ['demo_booked', 'PAGE_VIEW', 'purchase', 'bad name'],
        sessionStoragePersistenceBeforeConsent: false,
    }, extra || {});
}

async function start(kind, opts) {
    const b = launch(kind, opts);
    await b.api.initialize(API, 'app-1');
    await settle();
    return b;
}

function names(list) {
    return list.map((e) => e.name + (e.label !== undefined ? ':' + e.label : '') + (e.value !== undefined ? '=' + e.value : ''));
}

// ---- The scenarios, each run against both engines -------------------------------------------------

async function funnelOffSendsNothing(kind) {
    const b = await start(kind, { config: funnelConfig({ funnelTrackingEnabled: false }) });
    b.api.track('demo_booked');
    b.api.trackCta('nav_pricing');
    await b.api.flush();
    b.fire('window', 'pagehide');
    await settle();
    equal(b.events().length, 0, 'funnel tracking off sends no events');
    equal(b.listeners.filter((l) => l.type === 'scroll').length, 0, 'no auto listeners while funnel tracking is off');
    const payload = b.api.getForRegistration();
    check(payload && KEY.test(payload.sessionKey), 'the registration payload still carries a session key');
    equal(payload.deviceClass, 'desktop', 'the registration payload carries the device class');
    equal(payload.sessionCount, 1, 'the registration payload carries the session count');
    equal(payload.sdk, 'dotnet', 'the payload keeps the dotnet sdk tag');
}

async function landingAndBuffer(kind) {
    const b = launch(kind, { config: funnelConfig() });
    const init = b.api.initialize(API, 'app-1');
    b.api.track('signup_view'); // before the config loads: buffered, then replayed
    await init;
    await settle();
    await b.api.flush();
    await settle();
    const posts = b.posted();
    equal(posts.length, 1, 'one batch');
    const body = posts[0].body;
    equal(posts[0].url, EVENTS_URL, 'posted to api/attribution/events with the appId in the query');
    equal(body.appId, 'app-1', 'body appId');
    equal(body.visitorKey, b.api.getState().visitorKey, 'body visitorKey is the engine visitor');
    check(KEY.test(body.sessionKey), 'body sessionKey is a key');
    equal(body.isReturning, false, 'a first visit is not returning');
    equal(body.deviceClass, 'desktop', 'body deviceClass');
    equal(body.platform, 'web', 'body platform');
    equal(body.touch && body.touch.source, 'reddit', 'body carries the current last touch');
    deepEqual(names(body.events), ['signup_view', 'page_view'], 'the buffered event is replayed, then the landing page_view');
    equal(body.events[1].path, '/pricing', 'page_view carries the path, never the query');
    check(typeof body.events[0].clientTimestamp === 'string', 'events carry a client timestamp');

    const blob = JSON.parse(b.local.get('ww_attribution'));
    equal(blob.sessionKey, body.sessionKey, 'the stored blob keeps the session key');
    equal(blob.sessionCount, 1, 'the stored blob keeps the session count');
    check(typeof blob.lastActivityAt === 'number', 'the stored blob keeps the last activity time');
}

async function allowlistAndOneShots(kind) {
    const b = await start(kind, { config: funnelConfig() });
    b.api.track('purchase'); // server-only
    b.api.track('signup_complete'); // server-only
    b.api.track('not_configured'); // not a standard or custom name
    b.api.track('Bad Name');
    b.api.track('demo_booked', { label: '  pricing  ', value: 2 });
    b.api.trackCta('');
    b.api.trackCta('nav_pricing');
    b.api.track('signup_view');
    b.api.track('signup_view');
    b.api.track('signup_error', { label: 'USERNAME EXISTS!' });
    b.api.track('signup_error', { label: null });
    b.api.track('plan_selected', { label: 'tier-1' });
    b.api.track('plan_selected', { label: 'tier-1' });
    b.api.track('plan_selected', { label: 'tier-2' });
    b.api.track('scroll_depth', { value: 33 });
    b.api.track('time_on_page', { value: -1 });
    b.api.track('page_view', { path: '/pricing?x=1' }); // same page: no second page_view
    await b.api.flush();
    await settle();
    const got = names(b.events());
    deepEqual(got, [
        'page_view',
        'demo_booked:pricing=2',
        'cta_click:nav_pricing',
        'signup_view',
        'signup_error:username_exists',
        'signup_error:unknown',
        'plan_selected:tier-1',
        'plan_selected:tier-2',
    ], 'server-only and unknown names drop, custom names pass, one-shots send once');
    return got;
}

async function signupStepsGated(kind) {
    const b = await start(kind, { config: funnelConfig({ trackSignupSteps: false }) });
    b.api.track('signup_view');
    b.api.track('signup_start');
    b.api.track('signup_error', { label: 'validation' });
    b.api.track('plan_selected', { label: 'tier-1' });
    await b.api.flush();
    deepEqual(names(b.events()), ['page_view', 'plan_selected:tier-1'], 'signup steps are dropped while trackSignupSteps is off');
}

async function batching(kind) {
    const b = await start(kind, { config: funnelConfig({ trackEngagement: false }) });
    for (let i = 0; i < 29; i++) b.api.track('demo_booked', { value: i }); // + the landing page_view = 30
    await settle();
    equal(b.posted().length, 1, 'the 25th queued event flushes at once');
    equal(b.posted()[0].body.events.length, 25, 'a batch holds at most 25 events');
    b.advance(4999);
    await settle();
    equal(b.posted().length, 1, 'the rest waits for the 5 s timer');
    b.advance(1);
    await settle();
    equal(b.posted().length, 2, 'the 5 s timer flushes the rest');
    equal(b.posted()[1].body.events.length, 5, 'the second batch has the remaining 5');
}

async function beaconOnHide(kind) {
    const b = await start(kind, { config: funnelConfig() });
    b.api.track('demo_booked');
    b.advance(3000);
    b.fire('window', 'pagehide');
    equal(b.beacons.length, 1, 'pagehide sends one beacon');
    equal(b.beacons[0].url, EVENTS_URL, 'the beacon goes to the events endpoint');
    equal(b.beacons[0].type, 'text/plain;charset=UTF-8', 'the beacon is a text/plain Blob');
    deepEqual(names(b.beacons[0].body.events), ['page_view', 'demo_booked', 'time_on_page=3'], 'pagehide closes time_on_page');
    equal(b.posted().length, 0, 'nothing went by fetch');

    b.api.track('demo_booked', { label: 'again' });
    b.g.document.visibilityState = 'hidden';
    b.fire('document', 'visibilitychange');
    equal(b.beacons.length, 2, 'visibilitychange hidden sends a beacon');
}

async function beaconFallsBackToFetch(kind) {
    const b = await start(kind, { config: funnelConfig() });
    b.g.navigator.sendBeacon = () => false;
    b.api.track('demo_booked');
    b.fire('window', 'pagehide');
    const keepalive = b.fetches.filter((f) => f.url === EVENTS_URL && f.init.keepalive === true);
    equal(keepalive.length, 1, 'a refused beacon falls back to a keepalive fetch');
    equal(keepalive[0].init.headers['Content-Type'], 'text/plain;charset=UTF-8', 'the fallback is text/plain too');
}

async function history(kind) {
    const b = await start(kind, { config: funnelConfig({ trackEngagement: false }) });
    b.g.history.pushState({}, '', '/signup?utm_source=google&utm_medium=cpc');
    equal(b.api.getState().last.source, 'google', 'pushState applies the new URL as a touch');
    b.g.history.replaceState({}, '', '/signup?utm_source=google&utm_medium=cpc');
    b.g.location.href = 'https://app.example.test/features';
    b.fire('window', 'popstate');
    b.api.captureUrl('https://app.example.test/features');
    b.api.captureUrl('https://app.example.test/blog');
    await b.api.flush();
    deepEqual(b.events().map((e) => e.path), ['/pricing', '/signup', '/features', '/blog'], 'one page_view per path');
    const touches = b.fetches.filter((f) => f.url.indexOf('/attribution/touch') >= 0);
    equal(touches.length, 0, 'no beacon without beaconEnabled');
}

async function ctaAndScroll(kind) {
    const b = await start(kind, { config: funnelConfig() });
    const button = { closest: (sel) => (sel === '[data-ww-cta]' ? { getAttribute: () => 'hero_start' } : null) };
    b.fire('document', 'click', { target: button });
    b.fire('document', 'click', { target: { closest: () => null } });
    b.g.scrollY = 1600; // (1600 + 800) / 4000 = 60%
    b.fire('window', 'scroll');
    b.fire('window', 'scroll');
    b.advance(150);
    b.g.scrollY = 3200; // 100%
    b.fire('window', 'scroll');
    b.advance(150);
    await b.api.flush();
    deepEqual(names(b.events()), [
        'page_view',
        'cta_click:hero_start',
        'scroll_depth=25',
        'scroll_depth=50',
        'engaged',
        'scroll_depth=75',
        'scroll_depth=100',
    ], 'a delegated CTA click, milestones once each, engaged once');
}

async function engagedByTime(kind) {
    const b = await start(kind, { config: funnelConfig({ trackScrollDepth: false }) });
    b.fire('document', 'keydown');
    b.advance(9999);
    await b.api.flush();
    deepEqual(names(b.events()), ['page_view'], 'not engaged before 10 s visible');
    b.advance(1);
    await b.api.flush();
    deepEqual(names(b.events()), ['page_view', 'engaged'], 'an interaction plus 10 s visible is engaged');
}

async function sessionRollover(kind) {
    const b = await start(kind, { config: funnelConfig({ trackEngagement: false }) });
    const first = b.api.getForRegistration().sessionKey;
    b.api.track('demo_booked');
    b.advance(31 * 60 * 1000);
    b.api.track('demo_booked', { label: 'later' });
    await b.api.flush();
    const posts = b.posted();
    const keys = posts.map((p) => p.body.sessionKey);
    check(keys.length >= 2 && keys[0] === first && keys[keys.length - 1] !== first, 'a 30-minute gap starts a new session');
    const payload = b.api.getForRegistration();
    equal(payload.sessionCount, 2, 'the session count goes up');
    equal(payload.sessionKey, keys[keys.length - 1], 'registration joins the current session');
}

async function returning(kind) {
    const local = new Map();
    local.set('ww_attribution', JSON.stringify({
        v: 1,
        visitorKey: 'visitor-key-0001',
        first: null,
        last: null,
        updatedAt: '2026-09-28T09:00:00.000Z',
        sessionKey: 'session-key-0001',
        lastActivityAt: START - 2 * 60 * 60 * 1000,
        sessionCount: 3,
    }));
    const b = await start(kind, { config: funnelConfig(), local });
    await b.api.flush();
    const body = b.posted()[0].body;
    equal(body.visitorKey, 'visitor-key-0001', 'the stored visitor is kept');
    equal(body.isReturning, true, 'a stored visitor in a new session is returning');
    check(body.sessionKey !== 'session-key-0001', 'a stale session is not continued');
    equal(b.api.getForRegistration().sessionCount, 4, 'the session count continues');

    const recent = new Map();
    recent.set('ww_attribution', JSON.stringify({
        v: 1, visitorKey: 'visitor-key-0002', first: null, last: null, updatedAt: '',
        sessionKey: 'session-key-0002', lastActivityAt: START - 60 * 1000, sessionCount: 2,
    }));
    const c = await start(kind, { config: funnelConfig(), local: recent });
    await c.api.flush();
    equal(c.posted()[0].body.sessionKey, 'session-key-0002', 'a session active under 30 minutes ago continues');

    const legacy = new Map();
    legacy.set('ww_attribution', JSON.stringify({ v: 1, visitorKey: 'visitor-key-0003', first: null, last: null, updatedAt: '' }));
    const d = await start(kind, { config: funnelConfig(), local: legacy });
    await d.api.flush();
    equal(d.posted()[0].body.isReturning, true, 'a blob from before session tracking is a returning visitor');
    equal(d.api.getForRegistration().sessionCount, 2, 'and this is its second session');
}

async function deviceClasses(kind) {
    const cases = [[500, false, 'mobile'], [767, false, 'mobile'], [800, false, 'tablet'], [1100, true, 'tablet'], [1100, false, 'desktop'], [1300, true, 'desktop']];
    for (const [width, coarse, expected] of cases) {
        const b = await start(kind, { config: funnelConfig(), width, coarse });
        equal(b.api.getForRegistration().deviceClass, expected, 'width ' + width + (coarse ? ' coarse' : ''));
    }
}

function mirrorConfig(extra) {
    return funnelConfig(Object.assign({ persistenceConsentCategory: 'Analytics', sessionStoragePersistenceBeforeConsent: true }, extra || {}));
}

async function mirrorLifecycle(kind) {
    const local = new Map();
    const session = new Map();
    const consentConfig = { enabled: true, version: 3, honorGpc: false };
    const b = await start(kind, { config: mirrorConfig(), consentConfig, local, session });
    check(session.has('ww_attribution_session'), 'undecided and opted in: the mirror is written');
    check(!local.has('ww_attribution'), 'undecided: nothing in localStorage');
    const mirror = JSON.parse(session.get('ww_attribution_session'));
    equal(mirror.v, 1, 'mirror schema version');
    equal(mirror.visitorKey, b.api.getState().visitorKey, 'mirror visitorKey');
    equal(mirror.last && mirror.last.source, 'reddit', 'mirror keeps the touch');
    check(KEY.test(mirror.sessionKey), 'mirror keeps the session key');

    // A reload in the same tab: a direct landing, the mirror restores the visitor and the touch.
    const r = await start(kind, { config: mirrorConfig(), consentConfig, local, session, href: 'https://app.example.test/' });
    equal(r.api.getState().visitorKey, mirror.visitorKey, 'the reload keeps the visitor');
    equal(r.api.getState().last && r.api.getState().last.source, 'reddit', 'the reload keeps the touch');
    equal(r.api.getForRegistration().sessionKey, mirror.sessionKey, 'the reload continues the session');

    // Granted: the data moves to localStorage and the mirror goes.
    const consent = { state: { categories: { Analytics: true }, decided: true } };
    r.g.wildwoodConsent = { getState: () => consent.state };
    r.fire('document', 'wildwood:consent-change', { detail: null });
    check(local.has('ww_attribution'), 'granted: the blob is written');
    check(!session.has('ww_attribution_session'), 'granted: the mirror is removed');
    equal(JSON.parse(local.get('ww_attribution')).visitorKey, mirror.visitorKey, 'the migrated blob keeps the visitor');

    // Withdrawn: both copies go, and no mirror for the rest of the page load.
    consent.state = { categories: { Analytics: false }, decided: false };
    r.fire('document', 'wildwood:consent-change', { detail: null });
    check(!local.has('ww_attribution'), 'withdrawn: the blob is removed');
    check(!session.has('ww_attribution_session'), 'withdrawn: no mirror for the rest of the load');
    r.api.captureUrl('https://app.example.test/?utm_source=bing');
    check(!session.has('ww_attribution_session'), 'still no mirror after a new touch');
}

async function mirrorRules(kind) {
    const consentConfig = { enabled: true, version: 3, honorGpc: false };

    const off = new Map();
    await start(kind, { config: mirrorConfig({ sessionStoragePersistenceBeforeConsent: false }), consentConfig, session: off });
    check(!off.has('ww_attribution_session'), 'not opted in: no mirror');

    const declined = new Map();
    const declinedLocal = new Map();
    await start(kind, {
        config: mirrorConfig(), consentConfig, session: declined, local: declinedLocal,
        consent: { state: { categories: { Analytics: false }, decided: true } },
    });
    check(!declined.has('ww_attribution_session'), 'declined: no mirror');
    check(!declinedLocal.has('ww_attribution'), 'declined: no blob');

    const pending = new Map();
    await start(kind, {
        config: mirrorConfig(), consentConfig, session: pending,
        consent: { state: { categories: { Analytics: false }, decided: false } },
    });
    check(pending.has('ww_attribution_session'), 'a consent engine that has not been answered is undecided');

    const stale = new Map();
    await start(kind, {
        config: mirrorConfig(), consentConfig, session: stale,
        cookie: 'ww_consent=' + encodeURIComponent(JSON.stringify({ consentString: 'Analytics', configVersion: 2 })),
    });
    check(stale.has('ww_attribution_session'), 'a cookie from an older consent config is undecided');

    const blocked = await start(kind, { config: mirrorConfig(), consentConfig, sessionThrows: true });
    check(blocked.api.getState().last !== null, 'a throwing sessionStorage costs nothing');

    const cleared = new Map();
    const c = await start(kind, { config: mirrorConfig({ funnelTrackingEnabled: false }), consentConfig, session: cleared });
    check(cleared.has('ww_attribution_session'), 'the mirror holds a touch even with funnel tracking off');
    c.api.clear();
    check(!cleared.has('ww_attribution_session'), 'clear() removes the mirror');
}

async function clearKeepsKeys(kind) {
    const local = new Map();
    const b = await start(kind, { config: funnelConfig(), local });
    b.api.clear();
    const blob = JSON.parse(local.get('ww_attribution'));
    equal(blob.first, null, 'clear() drops the touches');
    equal(blob.visitorKey, b.api.getState().visitorKey, 'with funnel tracking on the visitor key stays stored');
    equal(b.api.getForRegistration(), null, 'nothing to register after clear()');
}

async function disabledApp(kind) {
    const local = new Map();
    local.set('ww_attribution', JSON.stringify({ v: 1, visitorKey: 'visitor-key-0001', first: null, last: null, updatedAt: '' }));
    const session = new Map();
    session.set('ww_attribution_session', '{"v":1}');
    const b = await start(kind, { config: funnelConfig({ isEnabled: false }), local, session });
    b.api.track('demo_booked');
    await b.api.flush();
    equal(b.events().length, 0, 'attribution off: no events');
    check(!local.has('ww_attribution') && !session.has('ww_attribution_session'), 'attribution off: both copies removed');
}

async function failedConfig(kind) {
    const b = await start(kind, { config: null });
    b.api.track('demo_booked');
    await b.api.flush();
    equal(b.events().length, 0, 'a failed config fetch drops the buffer');
    check(b.api.getForRegistration() !== null, 'capture still works for registration');
}

async function helpers(kind) {
    if (kind !== 'razor') return;
    const b = launch(kind, { config: funnelConfig() });
    const cat = b.api.signupErrorCategory;
    equal(cat('USERNAME_EXISTS'), 'username_taken', 'USERNAME_EXISTS');
    equal(cat('UserExists'), 'email_taken', 'UserExists');
    equal(cat('PASSWORD_TOO_SHORT'), 'password_policy', 'PASSWORD prefix');
    equal(cat('HCAPTCHA_FAILED'), 'captcha', 'CAPTCHA anywhere');
    equal(cat('TOKEN_EXPIRED'), 'invalid_token', 'TOKEN prefix');
    equal(cat('registration_token_rejected'), 'invalid_token', 'flow code');
    equal(cat('SELF_REGISTRATION_NOT_ALLOWED'), 'registration_closed', 'NOTALLOWED');
    equal(cat(null, 0), 'network', 'status 0');
    equal(cat('', 429), 'rate_limited', '429');
    equal(cat('SOMETHING', 503), 'server', '5xx');
    equal(cat(null, 422), 'validation', '422');
    equal(cat('SOMETHING'), 'unknown', 'unknown');
}

const scenarios = [
    funnelOffSendsNothing, landingAndBuffer, allowlistAndOneShots, signupStepsGated, batching, beaconOnHide,
    beaconFallsBackToFetch, history, ctaAndScroll, engagedByTime, sessionRollover, returning, deviceClasses,
    mirrorLifecycle, mirrorRules, clearKeepsKeys, disabledApp, failedConfig, helpers,
];

const transcripts = {};
for (const kind of ['blazor', 'razor']) {
    for (const scenario of scenarios) {
        scope = kind + ' ' + scenario.name;
        try {
            const result = await scenario(kind);
            if (scenario === allowlistAndOneShots) transcripts[kind] = result;
        } catch (err) {
            failures += 1;
            console.error('FAIL [' + scope + ']: threw ' + (err && err.stack ? err.stack : err));
        }
    }
}

scope = 'parity';
deepEqual(transcripts.blazor, transcripts.razor, 'the two engines send the same events for the same calls');

if (failures > 0) {
    console.error(failures + ' of ' + checks + ' checks failed.');
    process.exit(1);
}
console.log('attribution funnel self-test: ' + checks + ' checks passed.');
