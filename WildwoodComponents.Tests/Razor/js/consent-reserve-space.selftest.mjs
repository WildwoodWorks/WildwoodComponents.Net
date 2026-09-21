// Self-test for the reserve-space bookkeeping in WildwoodComponents.Razor/wwwroot/js/consent.js.
//
// There is no JavaScript test harness in this repository, and keeping a fixed banner off the
// page's own edge-anchored UI genuinely lives in the browser: the measurement, the ResizeObserver
// and the writes to `document.body.style` have no C# counterpart to test. What CAN be tested
// without a browser is every decision those mechanisms turn on - which position pads at all, how
// much, whose padding it is when two banners are up, and what the page gets back afterwards -
// which is exactly the part an implementer gets subtly wrong. So consent.js keeps them as pure
// functions and this file replays them:
//
//   * a `corner` card pads NOTHING (it is a ~420px inset box, not a full-width bar) while still
//     publishing its height, and the same for a host that opted out and for a position the
//     script does not recognise
//   * the two bars pad opposite edges
//   * the page's OWN padding is added to, not replaced, and is read exactly ONCE per edge -
//     before the first claim - so a second banner never measures against our own reservation
//   * two banners on one edge reserve the TALLEST, not the sum; dropping one leaves the other's
//   * the last release restores the page's inline padding byte for byte, and a page that had
//     none gets `null` - the instruction to REMOVE the declaration rather than zero it over a
//     stylesheet rule
//   * releasing twice, and releasing a banner that never reserved, write nothing and disturb
//     nobody else's reservation
//   * a banner that changes position settles the edge it leaves
//
// Run it directly (`node consent-reserve-space.selftest.mjs`) or through
// ConsentReserveSpaceSelfTestRunnerTests, which shells out to node and asserts exit code 0.

import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const require = createRequire(import.meta.url);
const here = path.dirname(fileURLToPath(import.meta.url));
const scripts = path.resolve(here, '../../../WildwoodComponents.Razor/wwwroot/js');

// Requiring the component script at all is half the point: it must load with no DOM, which is
// only true while the auto-initialize at the bottom stays guarded by `typeof document`.
const m = require(path.join(scripts, 'consent.js'));

let failures = 0;
let checks = 0;

function check(condition, what) {
    checks += 1;
    if (!condition) {
        failures += 1;
        console.error('FAIL: ' + what);
    }
}

function equal(actual, expected, what) {
    check(actual === expected, what + ' (expected ' + JSON.stringify(expected) + ', got ' + JSON.stringify(actual) + ')');
}

function deepEqual(actual, expected, what) {
    equal(JSON.stringify(actual), JSON.stringify(expected), what);
}

/** A page whose body has `base` px of computed padding and `previous` as its inline value. */
function page(base, previous) {
    const reader = () => {
        reader.reads += 1;
        return { base: base, previous: previous === undefined ? '' : previous };
    };
    reader.reads = 0;
    return reader;
}

// ---- Which positions pad the page at all --------------------------------------------------

equal(m.reservedEdge('bottomBar'), 'paddingBottom', 'a bottom bar pads the bottom');
equal(m.reservedEdge('topBar'), 'paddingTop', 'a top bar pads the top');
equal(m.reservedEdge('corner'), null, 'a corner card pads nothing - it is an inset box, not a bar');
equal(m.reservedEdge('somethingNew'), null, 'an unrecognised position pads nothing');
equal(m.reservedEdge('bottomBar', false), null, 'a host that opted out pads nothing');
equal(m.reservedEdge('topBar', false), null, 'the opt-out beats the top bar too');
equal(m.reservedEdge('bottomBar', true), 'paddingBottom', 'an explicit true still pads');
equal(m.reservedEdge('bottomBar', undefined), 'paddingBottom', 'omitting the flag still pads');

equal(m.edgeProperty('paddingTop'), 'padding-top', 'the top edge removes padding-top');
equal(m.edgeProperty('paddingBottom'), 'padding-bottom', 'the bottom edge removes padding-bottom');

equal(m.positionOf({ classList: ['ww-consent-banner', 'ww-consent-pos-corner'] }), 'corner',
    'the position comes off the banner class');
equal(m.positionOf({ classList: ['ww-consent-banner'] }), 'bottomBar',
    'a banner with no position class is a bottom bar');
equal(m.positionOf(null), 'bottomBar', 'no element at all is still a bottom bar');

// ---- One banner ----------------------------------------------------------------------------

{
    const ledger = m.createSpaceLedger();
    const read = page(20, '20px');

    const first = m.reserveInLedger(ledger, 'a', 'paddingBottom', 40, read);
    equal(first.height, '40px', 'the height is published as measured');
    deepEqual(first.padding, [{ edge: 'paddingBottom', value: '60px' }],
        "the page's own 20px is ADDED to, not replaced");
    equal(read.reads, 1, "the page's padding is read once");

    const resized = m.reserveInLedger(ledger, 'a', 'paddingBottom', 90, read);
    deepEqual(resized.padding, [{ edge: 'paddingBottom', value: '110px' }], 'a resize re-reserves');
    equal(resized.height, '90px', 'and republishes the height');
    equal(read.reads, 1, 'and does NOT read our own reservation back as the page base');

    const gone = m.releaseFromLedger(ledger, 'a');
    equal(gone.height, null, 'the last banner takes the published height with it');
    deepEqual(gone.padding, [{ edge: 'paddingBottom', value: '20px' }],
        "the page's own inline padding comes back exactly as found");
}

// ---- A page with NO inline padding -----------------------------------------------------------

{
    const ledger = m.createSpaceLedger();
    // 16px of padding, all of it from a stylesheet: nothing inline to give back.
    const read = page(16, '');

    const write = m.reserveInLedger(ledger, 'a', 'paddingTop', 50, read);
    deepEqual(write.padding, [{ edge: 'paddingTop', value: '66px' }], 'computed padding is added to');

    const gone = m.releaseFromLedger(ledger, 'a');
    deepEqual(gone.padding, [{ edge: 'paddingTop', value: null }],
        'and the declaration is REMOVED, not zeroed over the stylesheet');
}

// ---- A corner card, and a host that places the room itself -----------------------------------

{
    const ledger = m.createSpaceLedger();
    const read = page(0, '');

    const corner = m.reserveInLedger(ledger, 'a', m.reservedEdge('corner'), 120, read);
    equal(corner.height, '120px', 'a corner card still publishes its height for the host to use');
    deepEqual(corner.padding, [], 'but reserves no page padding at all');
    equal(read.reads, 0, "and never even reads the page's padding");

    const gone = m.releaseFromLedger(ledger, 'a');
    equal(gone.height, null, 'and takes the height with it when it goes');
    deepEqual(gone.padding, [], 'with nothing to give back');
}

{
    const ledger = m.createSpaceLedger();
    const read = page(0, '');
    const optedOut = m.reserveInLedger(ledger, 'a', m.reservedEdge('bottomBar', false), 44, read);
    equal(optedOut.height, '44px', 'an opted-out host still gets the measurement');
    deepEqual(optedOut.padding, [], 'and keeps its own padding untouched');
}

// ---- Two banners on the same edge ------------------------------------------------------------

{
    const ledger = m.createSpaceLedger();
    const read = page(10, '10px');

    m.reserveInLedger(ledger, 'a', 'paddingBottom', 40, read);
    const both = m.reserveInLedger(ledger, 'b', 'paddingBottom', 70, read);
    deepEqual(both.padding, [{ edge: 'paddingBottom', value: '80px' }],
        'two banners on one edge overlap: the page reserves the TALLEST, not the sum');
    equal(both.height, '70px', 'and the published height is the tallest too');
    equal(read.reads, 1, "the second banner does not re-read the page - it would read OUR padding");

    const short = m.releaseFromLedger(ledger, 'a');
    deepEqual(short.padding, [{ edge: 'paddingBottom', value: '80px' }],
        "dropping the shorter one leaves the taller one's reservation standing");
    equal(short.height, '70px', 'and the taller one still owns the published height');

    const tall = m.releaseFromLedger(ledger, 'b');
    deepEqual(tall.padding, [{ edge: 'paddingBottom', value: '10px' }],
        'only the LAST one gives the page its own padding back');
    equal(tall.height, null, 'and only then is the height unpublished');

    // Captured once per occupancy: the edge was drained, so the next banner reads the page fresh.
    const later = page(10, '10px');
    m.reserveInLedger(ledger, 'c', 'paddingBottom', 30, later);
    equal(later.reads, 1, 'a banner arriving after the edge drained reads the page again');
}

// ---- Two banners on OPPOSITE edges -----------------------------------------------------------

{
    const ledger = m.createSpaceLedger();
    const bottom = page(0, '');
    const top = page(0, '');

    deepEqual(m.reserveInLedger(ledger, 'a', 'paddingBottom', 40, bottom).padding,
        [{ edge: 'paddingBottom', value: '40px' }], 'the bottom bar pads the bottom');
    deepEqual(m.reserveInLedger(ledger, 'b', 'paddingTop', 25, top).padding,
        [{ edge: 'paddingTop', value: '25px' }], 'the top bar pads the top');

    deepEqual(m.releaseFromLedger(ledger, 'a').padding, [{ edge: 'paddingBottom', value: null }],
        'and each gives back only its own edge');
    equal(m.publishedHeight(ledger), '25px', 'the survivor owns the published height');
}

// ---- Releasing what nobody holds ---------------------------------------------------------------

{
    const ledger = m.createSpaceLedger();
    const read = page(0, '');
    m.reserveInLedger(ledger, 'a', 'paddingBottom', 40, read);

    equal(m.releaseFromLedger(ledger, 'never-reserved'), null,
        'releasing a banner that never reserved writes nothing');
    deepEqual(m.paddingWrite(ledger, 'paddingBottom'), { edge: 'paddingBottom', value: '40px' },
        "and leaves the live banner's reservation exactly where it was");

    check(m.releaseFromLedger(ledger, 'a') !== null, 'the real release writes');
    equal(m.releaseFromLedger(ledger, 'a'), null, 'releasing the same banner twice is a no-op');
    equal(m.publishedHeight(ledger), null, 'and the ledger is empty');
}

// ---- A banner that changes position ------------------------------------------------------------

{
    const ledger = m.createSpaceLedger();
    const bottom = page(5, '5px');
    const top = page(0, '');

    m.reserveInLedger(ledger, 'a', 'paddingBottom', 40, bottom);
    const moved = m.reserveInLedger(ledger, 'a', 'paddingTop', 40, top);
    deepEqual(moved.padding,
        [{ edge: 'paddingBottom', value: '5px' }, { edge: 'paddingTop', value: '40px' }],
        'a banner that moves settles the edge it leaves before claiming the new one');

    const corner = m.reserveInLedger(ledger, 'a', null, 40, top);
    deepEqual(corner.padding, [{ edge: 'paddingTop', value: null }],
        'and one that stops reserving altogether hands the page back too');
    equal(corner.height, '40px', 'while still publishing its height');
}

// ---- Report -------------------------------------------------------------------------------------

if (failures > 0) {
    console.error('\n' + failures + ' of ' + checks + ' consent reserve-space checks failed.');
    process.exit(1);
}

console.log('consent reserve-space self-test: ' + checks + ' checks passed.');
