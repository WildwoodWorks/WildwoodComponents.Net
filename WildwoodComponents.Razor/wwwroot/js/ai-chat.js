/**
 * WildwoodComponents.Razor - AI Chat Component JavaScript
 * Handles message sending, session management, file uploads, TTS playback,
 * and speech-to-text input.
 * Razor Pages equivalent of the Blazor AIChatComponent interactivity.
 *
 * Voice input has TWO mechanisms behind one mic button, picked once per component, exactly as
 * Blazor's AIChatComponent.SpeechRecorder.cs and @wildwood/react's useSpeechInput.ts do:
 *
 *   'native'    the Web Speech API (Chrome, Edge, Safari with dictation on). Live interim
 *               results, no server call, no audio leaves the machine.
 *   'recorder'  MediaRecorder + a clip POSTed to WildwoodSpeechProxyController, which forwards it
 *               to stt/transcribe (Firefox, Brave/Opera/Vivaldi, WebView2, Safari with dictation
 *               off). One server call per clip, so it only ever records on an explicit tap.
 *   'none'      neither is available; no mic button is rendered at all.
 *
 * A native session can also fail at RUNTIME in an engine that merely EXPOSES the Web Speech API
 * without a recognition backend. `network`, `service-not-allowed` and `language-not-supported`
 * all mean "this will never work here", so they downgrade to the recorder for the rest of the
 * component's life and carry straight on into a recording; every other code (`no-speech`,
 * `aborted`, ...) is an ordinary end of a session and downgrades nothing.
 *
 * The decisions the two mechanisms turn on are pure functions at the top of this file, exported
 * through a guarded `module.exports` and covered by
 * WildwoodComponents.Tests/Razor/js/ai-chat-speech.selftest.mjs.
 */
(function () {
    'use strict';

    // ===== SPEECH: CONSTANTS =====

    /** Longest clip the recorder captures before it stops itself and transcribes. */
    var MAX_RECORDING_SECONDS = 60;

    /** Largest clip that is uploaded at all - the server's transcription limit. */
    var MAX_RECORDING_BYTES = 25 * 1024 * 1024;

    /**
     * First container the browser can record, most to least preferred:
     * Chrome/Edge -> webm/opus, Firefox -> webm or ogg, Safari -> mp4.
     */
    var RECORDING_MIME_PREFERENCE = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/mp4', 'audio/webm'];

    /** Web Speech error codes that mean "recognition will never run here" - see the file header. */
    var DOWNGRADE_ERROR_CODES = ['network', 'service-not-allowed', 'language-not-supported'];

    /** Ordinary ends of a session, not failures: nothing was said, or we stopped it ourselves. */
    var SILENT_ERROR_CODES = ['no-speech', 'aborted'];

    /** Upload extension per recorded format. Mirrors Shared/Utilities/SpeechAudioFormats.cs. */
    var AUDIO_EXTENSIONS = {
        'audio/webm': '.webm',
        'audio/ogg': '.ogg',
        'audio/mp4': '.mp4',
        'audio/x-m4a': '.m4a',
        'audio/m4a': '.m4a',
        'audio/mpeg': '.mp3',
        'audio/mp3': '.mp3',
        'audio/wav': '.wav',
        'audio/x-wav': '.wav',
        'audio/wave': '.wav'
    };

    /** 'stop' normally follows within milliseconds; this only exists so a caller never hangs. */
    var STOP_EVENT_TIMEOUT_MS = 3000;

    /** Chunking is what lets the size cap be enforced DURING a recording. */
    var RECORDER_TIMESLICE_MS = 1000;

    /**
     * Every visible string this script produces for voice input, in one place. The wording is
     * Blazor's, character for character, so the two stacks say the same thing (the ellipsis is
     * written as an escape so an encoding change cannot quietly rewrite it).
     */
    var MESSAGES = {
        unsupported: "Voice input isn't supported in this browser.",
        recordingUnsupported: 'Voice recording is not supported in this browser.',
        permissionDenied: 'Microphone permission was denied. Please enable microphone access to use voice input.',
        noMicrophone: 'No microphone was found on this device.',
        microphoneBusy: 'The microphone is in use by another application. Close it and try again.',
        couldNotStart: 'Voice recording could not start: ',
        tooLarge: 'Voice input is limited to 25 MB. Please record a shorter clip.',
        transcriptionFailed: 'Transcription failed. Please try again.',
        processingFailed: 'Voice input could not be processed. Please try again.',
        recording: 'Recording… tap the mic to stop',
        transcribing: 'Transcribing…',
        listening: 'Listening... '
    };

    // ===== SPEECH: PURE DECISIONS =====
    //
    // No DOM, no globals, no I/O. The self-test drives these directly.

    /**
     * The ONE voice-input gate. Nothing speech-related - no capability detection, no mic button,
     * no getUserMedia, no SpeechRecognition - happens unless this returns true. `enableStt` is the
     * component's data-enable-stt, which the ViewComponent renders from its enableSTT parameter.
     */
    function speechEnabled(config) {
        return !!config && config.enableStt === true;
    }

    /**
     * Which voice input this browser can do. `canUpload` is Razor-specific: the recorder needs a
     * same-origin route to POST the clip to, and a mic button whose only mechanism cannot reach a
     * server is worse than no button.
     */
    function detectSpeechMode(env) {
        if (!env) return 'none';
        if (env.speechRecognition) return 'native';
        if (env.getUserMedia && env.mediaRecorder && env.canUpload) return 'recorder';
        return 'none';
    }

    /** Whether a Web Speech error code means "give up on recognition and record instead". */
    function shouldDowngrade(errorCode) {
        return DOWNGRADE_ERROR_CODES.indexOf(errorCode) !== -1;
    }

    /** Whether a Web Speech error code is an ordinary end of a session, shown to nobody. */
    function isSilentSpeechError(errorCode) {
        return SILENT_ERROR_CODES.indexOf(errorCode) !== -1;
    }

    /**
     * The first container in RECORDING_MIME_PREFERENCE the browser will record, or '' to let
     * MediaRecorder choose (older implementations have no isTypeSupported).
     */
    function pickMimeType(isTypeSupported) {
        if (typeof isTypeSupported !== 'function') return '';
        for (var i = 0; i < RECORDING_MIME_PREFERENCE.length; i++) {
            if (isTypeSupported(RECORDING_MIME_PREFERENCE[i])) return RECORDING_MIME_PREFERENCE[i];
        }
        return '';
    }

    /** Whether a clip is past the upload cap. A non-numeric size is never "too large". */
    function exceedsCap(bytes, cap) {
        if (typeof bytes !== 'number' || !isFinite(bytes)) return false;
        var limit = (typeof cap === 'number' && isFinite(cap) && cap > 0) ? cap : MAX_RECORDING_BYTES;
        return bytes > limit;
    }

    /** 'audio/webm;codecs=opus' -> 'audio/webm'. '' for a blank input. */
    function bareMediaType(type) {
        if (typeof type !== 'string') return '';
        var separator = type.indexOf(';');
        return (separator >= 0 ? type.slice(0, separator) : type).trim().toLowerCase();
    }

    /** The upload's extension, including the dot, or '' for a format this library does not send. */
    function extensionFor(mediaType) {
        var bare = bareMediaType(mediaType);
        return Object.prototype.hasOwnProperty.call(AUDIO_EXTENSIONS, bare) ? AUDIO_EXTENSIONS[bare] : '';
    }

    /** 'speech.webm', 'speech.mp4', ... - the name the transcription provider reads the container from. */
    function fileNameFor(mediaType) {
        return 'speech' + extensionFor(mediaType);
    }

    /** What to show for a Web Speech error code that is neither silent nor a downgrade. */
    function speechErrorMessage(code) {
        if (code === 'not-allowed') return MESSAGES.permissionDenied;
        if (code === 'audio-capture') return MESSAGES.noMicrophone;
        return code ? 'Voice input failed (' + code + '). Please try again.' : MESSAGES.processingFailed;
    }

    /** What to show for a getUserMedia rejection, by DOMException name. */
    function microphoneErrorMessage(errorName) {
        if (errorName === 'NotFoundError' || errorName === 'DevicesNotFoundError') return MESSAGES.noMicrophone;
        // The device exists and permission was given, but something else holds it (a call,
        // another tab). TrackStartError is the legacy Chrome spelling of the same condition.
        if (errorName === 'NotReadableError' || errorName === 'TrackStartError') return MESSAGES.microphoneBusy;
        return MESSAGES.permissionDenied;
    }

    /** The message for a non-2xx transcription answer that carried no message of its own. */
    function transcriptionStatusMessage(status) {
        return 'Transcription failed (' + status + ').';
    }

    /** A data attribute read as a positive number, falling back when it is missing or junk. */
    function positiveNumber(value, fallback) {
        var parsed = parseInt(value, 10);
        return (isFinite(parsed) && parsed > 0) ? parsed : fallback;
    }

    function initAIChat(root) {
        var cid = root.dataset.componentId;
        var proxyUrl = root.dataset.proxyUrl;
        var configId = root.dataset.configurationId || '';
        var enableTTS = root.dataset.enableTts === 'true';
        var enableSTT = root.dataset.enableStt === 'true';
        var enableFileUpload = root.dataset.enableFileUpload === 'true';

        var messagesEl = document.getElementById('ww-messages-' + cid);
        var chatInput = root.querySelector('.ww-chat-input');
        var sendBtn = root.querySelector('.ww-send-btn');
        var typingIndicator = root.querySelector('.ww-typing-indicator');
        var configSelector = root.querySelector('.ww-config-selector');

        var currentSessionId = null;
        var pendingFile = null;
        var isProcessing = false;

        // ===== HELPERS =====

        function apiCall(method, path, body) {
            var opts = {
                method: method,
                headers: { 'Content-Type': 'application/json' }
            };
            if (body) opts.body = JSON.stringify(body);
            return fetch(proxyUrl + path, opts).then(function (r) {
                var ct = r.headers.get('content-type') || '';
                if (ct.indexOf('application/json') !== -1) {
                    return r.json().then(function (data) {
                        if (!r.ok) throw new Error(data.errorMessage || data.error || 'Request failed');
                        return data;
                    });
                }
                if (!r.ok) throw new Error('Request failed (' + r.status + ')');
                return r.text();
            });
        }

        function scrollToBottom() {
            if (messagesEl) messagesEl.scrollTop = messagesEl.scrollHeight;
        }

        function clearEmptyState() {
            var empty = messagesEl.querySelector('.ww-empty-state');
            if (empty) empty.remove();
        }

        function addMessage(role, content, timestamp) {
            clearEmptyState();
            var div = document.createElement('div');
            div.className = 'ww-message ' + role;

            var bubble = document.createElement('div');
            bubble.className = 'ww-message-bubble';
            bubble.textContent = content;
            div.appendChild(bubble);

            var meta = document.createElement('div');
            meta.className = 'ww-message-meta';
            var time = timestamp ? new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
            meta.textContent = time;

            // TTS button for assistant messages
            if (role === 'assistant' && enableTTS) {
                var ttsBtn = document.createElement('button');
                ttsBtn.className = 'ww-tts-btn ms-2';
                ttsBtn.innerHTML = '<i class="bi bi-volume-up"></i>';
                ttsBtn.title = 'Read aloud';
                ttsBtn.addEventListener('click', function () {
                    synthesizeSpeech(content);
                });
                meta.appendChild(ttsBtn);
            }

            div.appendChild(meta);
            messagesEl.appendChild(div);
            scrollToBottom();
        }

        // ===== SEND MESSAGE =====

        function sendMessage() {
            if (isProcessing) return;
            var text = chatInput.value.trim();
            if (!text && !pendingFile) return;

            isProcessing = true;
            sendBtn.disabled = true;

            addMessage('user', text || '(file attachment)');
            chatInput.value = '';
            updateSendButton();

            if (typingIndicator) typingIndicator.style.display = '';

            var request = {
                configurationId: getActiveConfigId(),
                sessionId: currentSessionId,
                message: text,
                saveToSession: true
            };

            // Handle file attachment
            if (pendingFile) {
                request.fileBase64 = pendingFile.base64;
                request.fileMediaType = pendingFile.mediaType;
                request.fileName = pendingFile.name;
                clearFilePreview();
            }

            apiCall('POST', '/chat', request)
                .then(function (result) {
                    if (result.isError) {
                        addMessage('assistant', 'Error: ' + (result.errorMessage || 'Unknown error'));
                    } else {
                        addMessage('assistant', result.response, result.createdAt);
                        if (result.sessionId && !currentSessionId) {
                            currentSessionId = result.sessionId;
                        }
                    }
                })
                .catch(function (err) {
                    addMessage('assistant', 'Error: ' + err.message);
                })
                .finally(function () {
                    isProcessing = false;
                    if (typingIndicator) typingIndicator.style.display = 'none';
                    updateSendButton();
                    chatInput.focus();
                });
        }

        // ===== INPUT HANDLING =====

        function updateSendButton() {
            if (sendBtn) {
                sendBtn.disabled = isProcessing || (!chatInput.value.trim() && !pendingFile);
            }
        }

        if (chatInput) {
            chatInput.addEventListener('input', function () {
                updateSendButton();
                // Auto-resize
                this.style.height = 'auto';
                this.style.height = Math.min(this.scrollHeight, 150) + 'px';
            });
            chatInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendMessage();
                }
            });
        }

        if (sendBtn) {
            sendBtn.addEventListener('click', sendMessage);
        }

        // ===== CONFIGURATION SELECTOR =====

        function getActiveConfigId() {
            if (configSelector) return configSelector.value;
            return configId;
        }

        if (configSelector) {
            configSelector.addEventListener('change', function () {
                configId = this.value;
                currentSessionId = null;
                // Reload sessions for new config
                loadSessions(configId);
            });
        }

        // ===== SESSION MANAGEMENT =====

        // New chat button
        var newSessionBtn = root.querySelector('.ww-new-session-btn');
        if (newSessionBtn) {
            newSessionBtn.addEventListener('click', function () {
                currentSessionId = null;
                if (messagesEl) {
                    messagesEl.innerHTML = '<div class="ww-empty-state text-center text-muted py-5">' +
                        '<i class="bi bi-chat-dots" style="font-size: 3rem;"></i>' +
                        '<p class="mt-2">Start a conversation</p></div>';
                }
            });
        }

        // Session item click - load session messages
        root.addEventListener('click', function (e) {
            var sessionItem = e.target.closest('.ww-session-item');
            if (!sessionItem || e.target.closest('.ww-session-menu') || e.target.closest('.dropdown-item')) return;

            var sessionId = sessionItem.dataset.sessionId;
            loadSession(sessionId);

            // Update active state
            var items = root.querySelectorAll('.ww-session-item');
            for (var i = 0; i < items.length; i++) items[i].classList.remove('active');
            sessionItem.classList.add('active');
        });

        // Session actions (rename, end, delete) via event delegation
        root.addEventListener('click', function (e) {
            var action = e.target.closest('.ww-rename-session, .ww-end-session, .ww-delete-session');
            if (!action) return;
            e.preventDefault();
            var sessionId = action.dataset.sessionId;

            if (action.classList.contains('ww-rename-session')) {
                var newName = prompt('Enter new session name:');
                if (newName) {
                    apiCall('PUT', '/sessions/' + sessionId + '/name', { newName: newName })
                        .then(function () { window.location.reload(); })
                        .catch(function (err) { alert('Failed to rename: ' + err.message); });
                }
            } else if (action.classList.contains('ww-end-session')) {
                apiCall('POST', '/sessions/' + sessionId + '/end')
                    .then(function () { window.location.reload(); })
                    .catch(function (err) { alert('Failed to end session: ' + err.message); });
            } else if (action.classList.contains('ww-delete-session')) {
                if (confirm('Delete this session? This cannot be undone.')) {
                    apiCall('DELETE', '/sessions/' + sessionId)
                        .then(function () { window.location.reload(); })
                        .catch(function (err) { alert('Failed to delete: ' + err.message); });
                }
            }
        });

        function loadSession(sessionId) {
            currentSessionId = sessionId;
            apiCall('GET', '/sessions/' + sessionId)
                .then(function (session) {
                    if (messagesEl) messagesEl.innerHTML = '';
                    if (session.messages && session.messages.length > 0) {
                        for (var i = 0; i < session.messages.length; i++) {
                            var msg = session.messages[i];
                            addMessage(msg.role, msg.content, msg.createdAt || msg.timestamp);
                        }
                    } else {
                        clearEmptyState();
                    }
                })
                .catch(function (err) {
                    addMessage('assistant', 'Failed to load session: ' + err.message);
                });
        }

        function loadSessions(cfgId) {
            var url = '/sessions';
            if (cfgId) url += '?configurationId=' + encodeURIComponent(cfgId);
            apiCall('GET', url)
                .then(function (sessions) {
                    var list = root.querySelector('.ww-session-list');
                    if (!list) return;
                    if (sessions.length === 0) {
                        list.innerHTML = '<p class="text-muted small text-center">No sessions yet</p>';
                        return;
                    }
                    list.innerHTML = '';
                    sessions.sort(function (a, b) {
                        return new Date(b.lastAccessedAt || b.createdAt) - new Date(a.lastAccessedAt || a.createdAt);
                    });
                    for (var i = 0; i < sessions.length; i++) {
                        var s = sessions[i];
                        var div = document.createElement('div');
                        div.className = 'ww-session-item p-2 rounded mb-1' + (!s.isActive ? ' text-muted' : '');
                        div.dataset.sessionId = s.id;
                        div.setAttribute('role', 'button');
                        div.innerHTML = '<div class="d-flex justify-content-between align-items-start">' +
                            '<div class="text-truncate small fw-medium">' + (s.sessionName || s.name || 'Chat ' + new Date(s.createdAt).toLocaleDateString()) + '</div>' +
                            '</div>' +
                            '<div class="text-truncate text-muted" style="font-size: 0.75rem;">' + (s.lastMessagePreview || 'Empty') + '</div>';
                        list.appendChild(div);
                    }
                })
                .catch(function () { /* non-critical: session list load failed */ });
        }

        // ===== FILE UPLOAD =====

        if (enableFileUpload) {
            var attachBtn = root.querySelector('.ww-attach-btn');
            var fileInput = root.querySelector('.ww-file-input');
            var filePreview = root.querySelector('.ww-file-preview');
            var fileName = root.querySelector('.ww-file-name');
            var removeFileBtn = root.querySelector('.ww-remove-file');

            if (attachBtn && fileInput) {
                attachBtn.addEventListener('click', function () { fileInput.click(); });
                fileInput.addEventListener('change', function () {
                    var file = this.files[0];
                    if (!file) return;
                    var reader = new FileReader();
                    reader.onload = function (e) {
                        var base64 = e.target.result.split(',')[1];
                        var ext = file.name.split('.').pop().toLowerCase();
                        var mediaTypes = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif', webp: 'image/webp', pdf: 'application/pdf', txt: 'text/plain', csv: 'text/csv' };
                        pendingFile = {
                            base64: base64,
                            name: file.name,
                            mediaType: mediaTypes[ext] || 'application/octet-stream'
                        };
                        if (fileName) fileName.textContent = file.name;
                        if (filePreview) filePreview.style.display = '';
                        updateSendButton();
                    };
                    reader.readAsDataURL(file);
                });
            }

            function clearFilePreview() {
                pendingFile = null;
                if (filePreview) filePreview.style.display = 'none';
                if (fileInput) fileInput.value = '';
                updateSendButton();
            }

            if (removeFileBtn) {
                removeFileBtn.addEventListener('click', clearFilePreview);
            }
        }

        // ===== TTS =====

        var currentAudio = null;

        function synthesizeSpeech(text) {
            apiCall('POST', '/tts/synthesize', { text: text, voice: '', speed: 1.0, configurationId: getActiveConfigId() })
                .then(function (result) {
                    if (result.audioBase64) {
                        if (currentAudio) currentAudio.pause();
                        currentAudio = new Audio('data:' + (result.contentType || 'audio/mpeg') + ';base64,' + result.audioBase64);
                        currentAudio.play();
                    }
                })
                .catch(function () { /* non-critical: TTS playback failed */ });
        }

        function stopPlayback() {
            if (currentAudio) {
                try { currentAudio.pause(); } catch (error) { /* already stopped */ }
            }
        }

        // ===== SPEECH-TO-TEXT =====
        //
        // The gate below was `enableSTT && A || B`, which && binds tighter than ||: the block ran
        // whenever window.SpeechRecognition existed, speech-to-text off or not. It is one call to
        // speechEnabled() now, and every speech entry point lives inside setupSpeechInput.

        var speechConfig = {
            enableStt: enableSTT,
            sttUrl: root.dataset.sttUrl || '',
            language: root.dataset.sttLanguage || '',
            maxSeconds: positiveNumber(root.dataset.sttMaxSeconds, MAX_RECORDING_SECONDS),
            maxBytes: positiveNumber(root.dataset.sttMaxBytes, MAX_RECORDING_BYTES)
        };

        if (speechEnabled(speechConfig)) {
            setupSpeechInput(speechConfig);
        }

        function setupSpeechInput(config) {
            var sttBtn = root.querySelector('.ww-stt-btn');
            if (!sttBtn) return;

            var statusEl = root.querySelector('.ww-stt-status');
            var errorEl = root.querySelector('.ww-stt-error');

            var mode = detectSpeechMode({
                speechRecognition: ('SpeechRecognition' in window) || ('webkitSpeechRecognition' in window),
                getUserMedia: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
                mediaRecorder: typeof window.MediaRecorder !== 'undefined',
                canUpload: config.sttUrl.length > 0
            });
            if (mode === 'none') return;

            // Rendered hidden: the button appears only once a mechanism is known to exist.
            sttBtn.style.display = '';

            var recognition = null;
            var session = null;
            var isListening = false;
            var isTranscribing = false;
            /**
             * Raised SYNCHRONOUSLY before the getUserMedia await. Without it a double tap during
             * the browser's permission prompt runs startRecording twice, and the second session
             * overwrites the first - an open microphone with no timer, no UI and no way to stop it.
             */
            var starting = false;
            /** Set when a stop lands during that window: the stream that arrives late is released. */
            var cancelStart = false;
            var finalizing = null;
            var tornDown = false;
            var interim = '';

            // --- surface ---------------------------------------------------------------------

            function show(el, visible) {
                if (el) el.style.display = visible ? '' : 'none';
            }

            function showError(message) {
                if (!errorEl || !message) return;
                errorEl.textContent = message;
                errorEl.style.display = '';
            }

            function hideError() {
                if (!errorEl) return;
                errorEl.textContent = '';
                errorEl.style.display = 'none';
            }

            function render() {
                var busy = isTranscribing || starting;
                sttBtn.disabled = busy;
                sttBtn.setAttribute('aria-pressed', isListening ? 'true' : 'false');
                sttBtn.classList.toggle('ww-recording', isListening);
                show(sttBtn.querySelector('.ww-stt-busy'), busy);
                show(sttBtn.querySelector('.ww-stt-recording'), !busy && isListening);
                show(sttBtn.querySelector('.ww-stt-idle'), !busy && !isListening);

                if (!statusEl) return;
                var status = '';
                if (isTranscribing) status = MESSAGES.transcribing;
                else if (isListening) status = (mode === 'recorder') ? MESSAGES.recording : (MESSAGES.listening + interim);
                statusEl.textContent = status;
                show(statusEl, status.length > 0);
            }

            function appendTranscript(text) {
                var trimmed = (text || '').trim();
                if (!trimmed || !chatInput) return;
                chatInput.value += (chatInput.value ? ' ' : '') + trimmed;
                updateSendButton();
            }

            // --- recorder --------------------------------------------------------------------

            function stopTracks(stream) {
                if (!stream) return;
                var tracks = stream.getTracks();
                for (var i = 0; i < tracks.length; i++) {
                    try { tracks[i].stop(); } catch (error) { /* already ended */ }
                }
            }

            /** The microphone is released here and nowhere else, so every path can call it. */
            function releaseStream(target) {
                if (!target || !target.stream) return;
                var stream = target.stream;
                target.stream = null;
                stopTracks(stream);
            }

            function stopRecorder(target) {
                try {
                    if (target.recorder.state !== 'inactive') target.recorder.stop();
                } catch (error) { /* already inactive */ }
            }

            /** Resolves on the recorder's 'stop', or on STOP_EVENT_TIMEOUT_MS - never hangs. */
            function awaitStop(target) {
                return new Promise(function (resolve) {
                    var settled = false;
                    var timer = setTimeout(function () {
                        if (settled) return;
                        settled = true;
                        resolve();
                    }, STOP_EVENT_TIMEOUT_MS);
                    target.stopped.then(function () {
                        if (settled) return;
                        settled = true;
                        clearTimeout(timer);
                        resolve();
                    });
                });
            }

            function uploadClip(blob) {
                var form = new FormData();
                form.append('file', blob, fileNameFor(blob.type));
                var activeConfig = getActiveConfigId();
                if (activeConfig) form.append('configurationId', activeConfig);
                if (config.language) form.append('language', config.language);

                return fetch(config.sttUrl, {
                    method: 'POST',
                    body: form,
                    credentials: 'same-origin'
                }).then(function (response) {
                    return response.json().catch(function () { return null; }).then(function (data) {
                        if (response.ok && data && data.success) {
                            return { success: true, text: data.text || '' };
                        }
                        return {
                            success: false,
                            errorMessage: (data && data.errorMessage) ||
                                (response.ok ? MESSAGES.transcriptionFailed : transcriptionStatusMessage(response.status))
                        };
                    });
                });
            }

            function finalizeCore(target) {
                if (target.timer) {
                    clearTimeout(target.timer);
                    target.timer = null;
                }
                stopRecorder(target);

                return awaitStop(target).then(function () {
                    releaseStream(target);
                    isListening = false;

                    if (target.oversize) {
                        render();
                        showError(MESSAGES.tooLarge);
                        return null;
                    }

                    var mimeType = target.recorder.mimeType ||
                        (target.chunks.length > 0 ? target.chunks[0].type : '') ||
                        target.requestedMimeType || 'audio/webm';
                    var blob = new Blob(target.chunks, { type: mimeType });
                    target.chunks = [];

                    // A stop with nothing recorded is a mis-tap, not a failure - Blazor is silent too.
                    if (blob.size === 0) {
                        render();
                        return null;
                    }
                    if (exceedsCap(blob.size, config.maxBytes)) {
                        render();
                        showError(MESSAGES.tooLarge);
                        return null;
                    }

                    isTranscribing = true;
                    render();
                    return uploadClip(blob).then(function (result) {
                        if (result.success) appendTranscript(result.text);
                        else showError(result.errorMessage || MESSAGES.transcriptionFailed);
                        return null;
                    });
                }).catch(function () {
                    showError(MESSAGES.processingFailed);
                }).then(function () {
                    releaseStream(target);
                    isTranscribing = false;
                    isListening = false;
                    render();
                });
            }

            /**
             * Stops the active recording, transcribes it and appends the text. Safe to call from
             * several places at once (mic tap, the auto-stop, the size cap): concurrent callers
             * share the single in-flight finalize, as Blazor's _finalizeRecordingTask does.
             */
            function finalizeRecording() {
                if (finalizing) return finalizing;
                var target = session;
                if (!target) return Promise.resolve();
                session = null;

                finalizing = finalizeCore(target).then(function () {
                    finalizing = null;
                }, function () {
                    finalizing = null;
                });
                return finalizing;
            }

            function startRecording() {
                // The guard goes up before anything can await - see `starting` above.
                if (starting) return Promise.resolve();
                if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia ||
                    typeof window.MediaRecorder === 'undefined') {
                    showError(MESSAGES.recordingUnsupported);
                    return Promise.resolve();
                }

                cancelStart = false;
                starting = true;
                hideError();
                render();

                // Recording while the assistant is talking would capture it; the tap means "my turn".
                stopPlayback();

                return navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } })
                    .then(function (stream) {
                        // Nothing left to record into: the page went away, or the user tapped stop,
                        // while permission was being granted. The microphone goes straight back.
                        if (tornDown || cancelStart) {
                            cancelStart = false;
                            starting = false;
                            stopTracks(stream);
                            render();
                            return;
                        }

                        var requestedMimeType = pickMimeType(
                            typeof window.MediaRecorder.isTypeSupported === 'function'
                                ? function (candidate) { return window.MediaRecorder.isTypeSupported(candidate); }
                                : null);

                        var recorder;
                        try {
                            recorder = requestedMimeType
                                ? new window.MediaRecorder(stream, { mimeType: requestedMimeType })
                                : new window.MediaRecorder(stream);
                        } catch (error) {
                            starting = false;
                            stopTracks(stream);
                            render();
                            showError(MESSAGES.couldNotStart + (error && error.message ? error.message : 'unknown error'));
                            return;
                        }

                        var resolveStopped;
                        var target = {
                            recorder: recorder,
                            stream: stream,
                            chunks: [],
                            bytes: 0,
                            requestedMimeType: requestedMimeType,
                            timer: null,
                            oversize: false,
                            stopped: new Promise(function (resolve) { resolveStopped = resolve; })
                        };

                        recorder.ondataavailable = function (event) {
                            var data = event && event.data;
                            if (!data || data.size === 0 || target.oversize) return;
                            target.bytes += data.size;
                            if (exceedsCap(target.bytes, config.maxBytes)) {
                                // Refuse the clip rather than post what the server will reject.
                                target.oversize = true;
                                target.chunks = [];
                                stopRecorder(target);
                                finalizeRecording();
                                return;
                            }
                            target.chunks.push(data);
                        };
                        recorder.onerror = function () {
                            // The 'stop' that follows drives the release; finalize reports it.
                            stopRecorder(target);
                            finalizeRecording();
                        };
                        recorder.addEventListener('stop', function () {
                            releaseStream(target);
                            resolveStopped();
                        });

                        try {
                            recorder.start(RECORDER_TIMESLICE_MS);
                        } catch (error) {
                            starting = false;
                            stopTracks(stream);
                            render();
                            showError(MESSAGES.couldNotStart + (error && error.message ? error.message : 'unknown error'));
                            return;
                        }

                        target.timer = setTimeout(function () {
                            target.timer = null;
                            if (session !== target) return;
                            finalizeRecording();
                        }, config.maxSeconds * 1000);

                        session = target;
                        isListening = true;
                        // Last, so there is never a tick in which neither flag is set.
                        starting = false;
                        render();
                    })
                    .catch(function (error) {
                        starting = false;
                        render();
                        showError(microphoneErrorMessage(error && error.name));
                    });
            }

            // --- native ----------------------------------------------------------------------

            /** Drops a recognition's handlers so a trailing onend cannot touch a later session. */
            function detach(instance) {
                instance.onresult = null;
                instance.onerror = null;
                instance.onend = null;
                if (recognition === instance) recognition = null;
            }

            function startNative() {
                var Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
                if (!Recognition) {
                    showError(MESSAGES.unsupported);
                    return;
                }

                var instance;
                try {
                    instance = new Recognition();
                } catch (error) {
                    showError(MESSAGES.unsupported);
                    return;
                }

                instance.continuous = true;
                instance.interimResults = true;
                instance.lang = config.language || 'en-US';

                instance.onresult = function (event) {
                    var finalText = '';
                    var interimText = '';
                    for (var i = event.resultIndex; i < event.results.length; i++) {
                        var result = event.results[i];
                        var transcript = (result && result[0] && result[0].transcript) || '';
                        if (result && result.isFinal) finalText += transcript;
                        else interimText += transcript;
                    }
                    if (finalText.trim()) {
                        appendTranscript(finalText);
                        interim = '';
                    } else {
                        interim = interimText;
                    }
                    render();
                };

                instance.onerror = function (event) {
                    var code = (event && event.error) || '';

                    if (shouldDowngrade(code)) {
                        // The engine exposes recognition but cannot run it here. Detach first so
                        // the trailing onend never lands on the recording that follows.
                        detach(instance);
                        try {
                            if (instance.abort) instance.abort(); else instance.stop();
                        } catch (error) { /* nothing to abort */ }
                        isListening = false;
                        interim = '';

                        var next = detectSpeechMode({
                            speechRecognition: false,
                            getUserMedia: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
                            mediaRecorder: typeof window.MediaRecorder !== 'undefined',
                            canUpload: config.sttUrl.length > 0
                        });
                        if (next !== 'recorder') {
                            // Nothing left to downgrade TO: hide the button rather than leave one
                            // that can only ever say "unsupported".
                            mode = 'none';
                            render();
                            sttBtn.style.display = 'none';
                            showError(MESSAGES.unsupported);
                            return;
                        }

                        mode = 'recorder';
                        render();
                        startRecording();
                        return;
                    }

                    detach(instance);
                    isListening = false;
                    interim = '';
                    render();
                    if (!isSilentSpeechError(code)) showError(speechErrorMessage(code));
                };

                instance.onend = function () {
                    if (recognition === instance) recognition = null;
                    isListening = false;
                    interim = '';
                    render();
                };

                try {
                    instance.start();
                } catch (error) {
                    detach(instance);
                    showError(MESSAGES.couldNotStart + (error && error.message ? error.message : 'unknown error'));
                    return;
                }

                recognition = instance;
                isListening = true;
                interim = '';
                render();
            }

            function stopNative() {
                var instance = recognition;
                if (!instance) return;
                detach(instance);
                try { instance.stop(); } catch (error) { /* already stopped */ }
                isListening = false;
                interim = '';
                render();
            }

            // --- public surface ---------------------------------------------------------------

            function start() {
                if (isListening || isTranscribing || starting || tornDown) return;
                hideError();
                if (mode === 'recorder') {
                    startRecording();
                    return;
                }
                startNative();
            }

            function stop() {
                if (mode === 'recorder') {
                    // A stop during the permission prompt cancels the pending start: the stream
                    // that eventually resolves is released without ever reaching a recorder.
                    if (starting) cancelStart = true;
                    finalizeRecording();
                    return;
                }
                stopNative();
            }

            /**
             * Drops the recognition, stops the recorder and releases the microphone. The clip is
             * discarded - the page is going away, so there is nothing left to append it to.
             */
            function teardown() {
                if (tornDown) return;
                tornDown = true;
                starting = false;
                cancelStart = false;

                var instance = recognition;
                if (instance) {
                    detach(instance);
                    try {
                        if (instance.abort) instance.abort(); else instance.stop();
                    } catch (error) { /* already gone */ }
                }

                var target = session;
                session = null;
                if (target) {
                    if (target.timer) {
                        clearTimeout(target.timer);
                        target.timer = null;
                    }
                    target.chunks = [];
                    stopRecorder(target);
                    releaseStream(target);
                }
            }

            sttBtn.addEventListener('click', function () {
                if (isListening) stop();
                else start();
            });

            // The microphone is released whichever way this page ends: a navigation, a bfcache
            // suspend, or a host that disposes the component by dispatching ww-ai-chat-teardown.
            window.addEventListener('pagehide', teardown);
            root.addEventListener('ww-ai-chat-teardown', teardown);

            render();
        }

        // Initialize
        updateSendButton();
    }

    // Auto-initialize every chat on the page. Guarded so this file can also be required by the
    // Node self-test, which needs the pure decisions above and no DOM at all.
    if (typeof document !== 'undefined') {
        var roots = document.querySelectorAll('.ww-ai-chat-component');
        for (var r = 0; r < roots.length; r++) {
            initAIChat(roots[r]);
        }
    }

    var api = {
        MAX_RECORDING_SECONDS: MAX_RECORDING_SECONDS,
        MAX_RECORDING_BYTES: MAX_RECORDING_BYTES,
        RECORDING_MIME_PREFERENCE: RECORDING_MIME_PREFERENCE,
        MESSAGES: MESSAGES,

        speechEnabled: speechEnabled,
        detectSpeechMode: detectSpeechMode,
        shouldDowngrade: shouldDowngrade,
        isSilentSpeechError: isSilentSpeechError,
        pickMimeType: pickMimeType,
        exceedsCap: exceedsCap,
        bareMediaType: bareMediaType,
        extensionFor: extensionFor,
        fileNameFor: fileNameFor,
        speechErrorMessage: speechErrorMessage,
        microphoneErrorMessage: microphoneErrorMessage,
        transcriptionStatusMessage: transcriptionStatusMessage
    };

    if (typeof window !== 'undefined') {
        window.wwAIChatSpeech = api;
    }

    // The self-test's hook. Guarded so the browser build never sees it: there is no `module` in a
    // classic script, and the check costs one typeof.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = api;
    }
})();
