// Self-test for the speech decisions in WildwoodComponents.Razor/wwwroot/js/ai-chat.js.
//
// There is no JavaScript test harness in this repository, and recorded voice input genuinely
// lives in the browser: MediaRecorder, getUserMedia and SpeechRecognition have no C# counterpart
// to test. What CAN be tested without a browser is every decision those mechanisms turn on, which
// is exactly the part an implementer gets subtly wrong - so ai-chat.js keeps them as pure
// functions and this file replays them:
//
//   * the ONE gate: with speech-to-text disabled, nothing speech-related may run
//   * mode detection, native -> recorder -> none, including Razor's extra "can the clip be
//     uploaded at all" condition
//   * which Web Speech error codes downgrade to the recorder, and that the ordinary ones
//     (no-speech, aborted, not-allowed, ...) do NOT
//   * the mime preference order, and what an old MediaRecorder with no isTypeSupported gets
//   * the 25 MB cap
//   * the media-type table that names the upload, which must stay equal to
//     WildwoodComponents.Shared/Utilities/SpeechAudioFormats.cs
//
// Run it directly (`node ai-chat-speech.selftest.mjs`) or through
// AIChatSpeechSelfTestRunnerTests, which shells out to node and asserts exit code 0.

import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const require = createRequire(import.meta.url);
const here = path.dirname(fileURLToPath(import.meta.url));
const scripts = path.resolve(here, '../../../WildwoodComponents.Razor/wwwroot/js');

// Requiring the component script at all is half the point: it must load with no DOM, which is
// only true while the auto-initialize at the bottom stays guarded by `typeof document`.
const m = require(path.join(scripts, 'ai-chat.js'));

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

// -- the gate ---------------------------------------------------------------------------------
//
// Everything speech-related in the component sits behind this one call. The bug it replaces read
// `enableSTT && A || B`, and && binds tighter than ||, so the whole block ran whenever
// window.SpeechRecognition existed - microphone and all - with voice input switched OFF.

equal(m.speechEnabled({ enableStt: true }), true, 'enabled when data-enable-stt is true');
equal(m.speechEnabled({ enableStt: false }), false, 'disabled when data-enable-stt is false');
equal(m.speechEnabled({}), false, 'disabled when the flag is absent');
equal(m.speechEnabled(null), false, 'disabled for a null config');
equal(m.speechEnabled(undefined), false, 'disabled for a missing config');
// The dataset gives strings; the component compares to 'true' itself, so a raw string here would
// mean a host attribute of any value switched voice input on.
equal(m.speechEnabled({ enableStt: 'true' }), false, 'the flag is a boolean, not a truthy string');
equal(m.speechEnabled({ enableStt: 1 }), false, 'a truthy number does not enable voice input');
// A disabled component has no upload URL either, and that still must not enable anything.
equal(m.speechEnabled({ enableStt: false, sttUrl: '/api/wildwood-stt/transcribe' }), false,
    'an upload URL does not enable voice input on its own');

// -- mode detection ---------------------------------------------------------------------------

const FULL = { speechRecognition: true, getUserMedia: true, mediaRecorder: true, canUpload: true };

equal(m.detectSpeechMode(FULL), 'native', 'Web Speech wins when it exists');
equal(m.detectSpeechMode({ ...FULL, speechRecognition: false }), 'recorder',
    'no Web Speech falls back to the recorder');
equal(m.detectSpeechMode({ speechRecognition: false, getUserMedia: false, mediaRecorder: true, canUpload: true }),
    'none', 'no getUserMedia leaves nothing');
equal(m.detectSpeechMode({ speechRecognition: false, getUserMedia: true, mediaRecorder: false, canUpload: true }),
    'none', 'no MediaRecorder leaves nothing');
// Razor-specific: the recorder needs a same-origin route to POST the clip to. A mic button whose
// only mechanism cannot reach a server is worse than no button.
equal(m.detectSpeechMode({ speechRecognition: false, getUserMedia: true, mediaRecorder: true, canUpload: false }),
    'none', 'the recorder needs somewhere to upload');
equal(m.detectSpeechMode({ ...FULL, canUpload: false }), 'native',
    'native needs no upload route, so it survives a missing one');
equal(m.detectSpeechMode(null), 'none', 'a missing environment is none');

// -- the runtime downgrade ----------------------------------------------------------------------

equal(m.shouldDowngrade('network'), true, 'network downgrades');
equal(m.shouldDowngrade('service-not-allowed'), true, 'service-not-allowed downgrades');
equal(m.shouldDowngrade('language-not-supported'), true, 'language-not-supported downgrades');
equal(m.shouldDowngrade('no-speech'), false, 'no-speech does not downgrade');
equal(m.shouldDowngrade('aborted'), false, 'aborted does not downgrade');
equal(m.shouldDowngrade('not-allowed'), false, 'a denied microphone does not downgrade');
equal(m.shouldDowngrade('audio-capture'), false, 'a missing microphone does not downgrade');
equal(m.shouldDowngrade(''), false, 'an empty code does not downgrade');
equal(m.shouldDowngrade(undefined), false, 'a missing code does not downgrade');

equal(m.isSilentSpeechError('no-speech'), true, 'nothing said is not an error the user reads');
equal(m.isSilentSpeechError('aborted'), true, 'we stopped it ourselves');
equal(m.isSilentSpeechError('not-allowed'), false, 'a denied microphone is reported');
equal(m.isSilentSpeechError('network'), false, 'a downgrade code is not silent by this rule');

// -- mime preference ----------------------------------------------------------------------------

const supports = (...types) => (candidate) => types.includes(candidate);

equal(m.pickMimeType(supports('audio/webm;codecs=opus', 'audio/webm')), 'audio/webm;codecs=opus',
    'Chrome/Edge get webm/opus');
equal(m.pickMimeType(supports('audio/ogg;codecs=opus', 'audio/webm')), 'audio/ogg;codecs=opus',
    'ogg/opus outranks plain webm');
equal(m.pickMimeType(supports('audio/mp4')), 'audio/mp4', 'Safari gets mp4');
equal(m.pickMimeType(supports('audio/webm')), 'audio/webm', 'plain webm is the last choice');
equal(m.pickMimeType(supports('audio/flac')), '', 'nothing preferred means let MediaRecorder choose');
equal(m.pickMimeType(null), '', 'an old MediaRecorder with no isTypeSupported chooses for itself');
equal(m.pickMimeType(undefined), '', 'a missing predicate chooses for itself');
equal(m.RECORDING_MIME_PREFERENCE.join('|'),
    'audio/webm;codecs=opus|audio/ogg;codecs=opus|audio/mp4|audio/webm',
    'the preference order matches Blazor and React');

// -- the size cap ---------------------------------------------------------------------------------

equal(m.MAX_RECORDING_BYTES, 25 * 1024 * 1024, 'the cap is the server-side 25 MB');
equal(m.MAX_RECORDING_SECONDS, 60, 'a clip auto-stops after 60 seconds');
equal(m.exceedsCap(m.MAX_RECORDING_BYTES), false, 'exactly the cap is allowed');
equal(m.exceedsCap(m.MAX_RECORDING_BYTES + 1), true, 'one byte over is refused');
equal(m.exceedsCap(0), false, 'an empty clip is not too large');
equal(m.exceedsCap(2048, 1024), true, 'a host-supplied cap is honoured');
equal(m.exceedsCap(512, 1024), false, 'under a host-supplied cap is fine');
equal(m.exceedsCap(2048, 0), false, 'a nonsense cap falls back to the default, which 2 KB is under');
equal(m.exceedsCap(undefined), false, 'an unknown size is never too large');
equal(m.exceedsCap('lots'), false, 'a non-numeric size is never too large');
check(m.MESSAGES.tooLarge.includes('25 MB'), 'the too-large message states the cap it enforces');

// -- the media-type table ---------------------------------------------------------------------
//
// This table must stay equal to WildwoodComponents.Shared/Utilities/SpeechAudioFormats.cs: the
// browser names the upload, and the Razor speech proxy validates it against the same list.

equal(m.bareMediaType('audio/webm;codecs=opus'), 'audio/webm', 'a codec parameter is dropped');
equal(m.bareMediaType('  AUDIO/WEBM  '), 'audio/webm', 'case and padding are normalised');
equal(m.bareMediaType(''), '', 'a blank type is blank');
equal(m.bareMediaType(null), '', 'a missing type is blank');
equal(m.bareMediaType(undefined), '', 'an undefined type is blank');

equal(m.extensionFor('audio/webm;codecs=opus'), '.webm', 'webm');
equal(m.extensionFor('audio/ogg;codecs=opus'), '.ogg', 'ogg');
equal(m.extensionFor('audio/mp4'), '.mp4', 'mp4');
equal(m.extensionFor('audio/x-m4a'), '.m4a', 'x-m4a');
equal(m.extensionFor('audio/m4a'), '.m4a', 'm4a');
equal(m.extensionFor('audio/mpeg'), '.mp3', 'mpeg');
equal(m.extensionFor('audio/mp3'), '.mp3', 'mp3');
equal(m.extensionFor('audio/wav'), '.wav', 'wav');
equal(m.extensionFor('audio/x-wav'), '.wav', 'x-wav');
equal(m.extensionFor('audio/wave'), '.wav', 'wave');
equal(m.extensionFor('audio/aac'), '', 'an unsupported container gets no extension');
equal(m.extensionFor('constructor'), '', 'a prototype key is not a media type');
equal(m.extensionFor(''), '', 'a blank type gets no extension');

equal(m.fileNameFor('audio/webm;codecs=opus'), 'speech.webm', 'the upload is speech.<ext>');
equal(m.fileNameFor('audio/aac'), 'speech', 'an unknown container uploads as a bare "speech"');

// -- messages -----------------------------------------------------------------------------------

equal(m.transcriptionStatusMessage(502), 'Transcription failed (502).',
    'a status-only failure reads the same as the .NET clients');
equal(m.MESSAGES.unsupported, "Voice input isn't supported in this browser.",
    'the unsupported wording matches Blazor');
equal(m.MESSAGES.transcriptionFailed, 'Transcription failed. Please try again.',
    'the generic failure matches Blazor');
equal(m.speechErrorMessage('not-allowed'), m.MESSAGES.permissionDenied,
    'a denied microphone says so');
equal(m.speechErrorMessage('audio-capture'), m.MESSAGES.noMicrophone,
    'a missing microphone says so');
check(m.speechErrorMessage('bad-grammar').includes('bad-grammar'),
    'an unmapped code is reported with its code, not swallowed');
equal(m.microphoneErrorMessage('NotFoundError'), m.MESSAGES.noMicrophone, 'NotFoundError');
equal(m.microphoneErrorMessage('NotReadableError'), m.MESSAGES.microphoneBusy, 'NotReadableError');
equal(m.microphoneErrorMessage('TrackStartError'), m.MESSAGES.microphoneBusy, 'legacy TrackStartError');
equal(m.microphoneErrorMessage('NotAllowedError'), m.MESSAGES.permissionDenied, 'NotAllowedError');

// ── result ───────────────────────────────────────────────────────────────────────────────────

if (failures > 0) {
    console.error('\n' + failures + ' of ' + checks + ' checks failed.');
    process.exit(1);
}

console.log(checks + ' checks passed.');
