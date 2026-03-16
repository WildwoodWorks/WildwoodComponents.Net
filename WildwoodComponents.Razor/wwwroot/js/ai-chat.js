/**
 * WildwoodComponents.Razor - AI Chat Component JavaScript
 * Handles message sending, session management, file uploads, TTS playback,
 * and speech-to-text input.
 * Razor Pages equivalent of the Blazor AIChatComponent interactivity.
 */
(function () {
    'use strict';

    var roots = document.querySelectorAll('.ww-ai-chat-component');
    for (var r = 0; r < roots.length; r++) {
        initAIChat(roots[r]);
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

        // ===== SPEECH-TO-TEXT =====

        if (enableSTT && 'webkitSpeechRecognition' in window || 'SpeechRecognition' in window) {
            var sttBtn = root.querySelector('.ww-stt-btn');
            var SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
            var recognition = null;
            var isListening = false;

            if (sttBtn) {
                sttBtn.addEventListener('click', function () {
                    if (isListening) {
                        if (recognition) recognition.stop();
                        return;
                    }
                    recognition = new SpeechRecognition();
                    recognition.continuous = false;
                    recognition.interimResults = false;
                    recognition.lang = 'en-US';

                    recognition.onstart = function () {
                        isListening = true;
                        sttBtn.classList.add('btn-danger');
                        sttBtn.classList.remove('btn-outline-secondary');
                    };

                    recognition.onresult = function (e) {
                        var transcript = e.results[0][0].transcript;
                        if (chatInput) {
                            chatInput.value += (chatInput.value ? ' ' : '') + transcript;
                            updateSendButton();
                        }
                    };

                    recognition.onend = function () {
                        isListening = false;
                        sttBtn.classList.remove('btn-danger');
                        sttBtn.classList.add('btn-outline-secondary');
                    };

                    recognition.onerror = function () {
                        isListening = false;
                        sttBtn.classList.remove('btn-danger');
                        sttBtn.classList.add('btn-outline-secondary');
                    };

                    recognition.start();
                });
            }
        }

        // Initialize
        updateSendButton();
    }
})();
