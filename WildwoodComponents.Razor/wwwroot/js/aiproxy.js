/**
 * WildwoodComponents.Razor - AI Proxy Component JavaScript
 *
 * Handles prompt submission, file uploads, and response display for the
 * AIProxyViewComponent. Communicates with the consuming app's proxy endpoints.
 *
 * Supports:
 * - Standard JSON responses (single request/response)
 * - Progressive text rendering (word-by-word reveal)
 * - SSE streaming responses (when server supports text/event-stream)
 */
(function () {
    'use strict';

    const component = document.getElementById('ww-ai-proxy');
    if (!component) return;

    const proxyUrl = component.dataset.proxyUrl;
    const configurationId = component.dataset.configurationId;
    const configurationName = component.dataset.configurationName;
    const allowFileUpload = component.dataset.allowFileUpload === 'true';
    const onCompleteCallback = component.dataset.onComplete;
    const streamingEnabled = component.dataset.streaming !== 'false'; // default: true

    const messageEl = document.getElementById('ww-ai-message');
    const promptEl = document.getElementById('ww-ai-prompt');
    const submitBtn = document.getElementById('ww-ai-submit');
    const responseArea = document.getElementById('ww-ai-response-area');
    const responseContent = document.getElementById('ww-ai-response-content');
    const usageEl = document.getElementById('ww-ai-usage');

    let selectedFile = null;
    let currentAbortController = null;
    let renderAnimationId = null;

    // ===== Message display =====
    function showMessage(text, type) {
        messageEl.textContent = text;
        messageEl.className = 'ww-alert ww-alert-' + type;
        messageEl.style.display = 'block';
    }

    function hideMessage() {
        messageEl.style.display = 'none';
    }

    // ===== Loading state =====
    function setLoading(loading) {
        var textEl = submitBtn.querySelector('.ww-btn-text');
        var spinnerEl = submitBtn.querySelector('.ww-btn-spinner');
        if (textEl) textEl.style.display = loading ? 'none' : 'inline';
        if (spinnerEl) spinnerEl.style.display = loading ? 'inline' : 'none';
        submitBtn.disabled = loading;
        promptEl.disabled = loading;
    }

    // ===== Progressive text renderer =====
    function renderProgressiveText(text, targetEl, options) {
        options = options || {};
        var speed = options.speed || 12; // ms per character
        var chunkSize = options.chunkSize || 3; // characters per tick
        var onComplete = options.onComplete;

        // Cancel any existing animation
        cancelProgressiveRender();

        targetEl.textContent = '';
        responseArea.style.display = 'block';

        var index = 0;
        var totalLength = text.length;

        function renderTick() {
            if (index >= totalLength) {
                renderAnimationId = null;
                if (onComplete) onComplete();
                return;
            }

            var end = Math.min(index + chunkSize, totalLength);
            targetEl.textContent += text.substring(index, end);
            index = end;

            // Auto-scroll the response area if it has a scrollbar
            var parent = targetEl.closest('.ww-ai-response-body, .ww-ai-response-area');
            if (parent) parent.scrollTop = parent.scrollHeight;

            renderAnimationId = setTimeout(renderTick, speed);
        }

        renderTick();
    }

    function cancelProgressiveRender() {
        if (renderAnimationId) {
            clearTimeout(renderAnimationId);
            renderAnimationId = null;
        }
    }

    // ===== File upload handling =====
    if (allowFileUpload) {
        var uploadZone = document.getElementById('ww-ai-upload-zone');
        var fileInput = document.getElementById('ww-ai-file-input');
        var fileInfo = document.getElementById('ww-ai-file-info');
        var fileName = document.getElementById('ww-ai-file-name');
        var fileRemove = document.getElementById('ww-ai-file-remove');

        if (uploadZone && fileInput) {
            uploadZone.addEventListener('click', function () {
                fileInput.click();
            });

            uploadZone.addEventListener('dragover', function (e) {
                e.preventDefault();
                uploadZone.classList.add('ww-ai-upload-active');
            });

            uploadZone.addEventListener('dragleave', function () {
                uploadZone.classList.remove('ww-ai-upload-active');
            });

            uploadZone.addEventListener('drop', function (e) {
                e.preventDefault();
                uploadZone.classList.remove('ww-ai-upload-active');
                if (e.dataTransfer.files.length > 0) {
                    selectFile(e.dataTransfer.files[0]);
                }
            });

            fileInput.addEventListener('change', function () {
                if (fileInput.files.length > 0) {
                    selectFile(fileInput.files[0]);
                }
            });

            if (fileRemove) {
                fileRemove.addEventListener('click', function () {
                    clearFile();
                });
            }
        }
    }

    function selectFile(file) {
        selectedFile = file;
        var fileInfo = document.getElementById('ww-ai-file-info');
        var fileName = document.getElementById('ww-ai-file-name');
        if (fileName) fileName.textContent = file.name;
        if (fileInfo) fileInfo.style.display = 'block';
    }

    function clearFile() {
        selectedFile = null;
        var fileInput = document.getElementById('ww-ai-file-input');
        var fileInfo = document.getElementById('ww-ai-file-info');
        if (fileInput) fileInput.value = '';
        if (fileInfo) fileInfo.style.display = 'none';
    }

    // ===== SSE Streaming support =====
    async function sendStreamingRequest(prompt) {
        currentAbortController = new AbortController();

        var response;
        try {
            response = await fetch(proxyUrl + '/request', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'text/event-stream, application/json'
                },
                body: JSON.stringify({
                    configurationId: configurationId,
                    configurationName: configurationName,
                    prompt: prompt,
                    stream: true
                }),
                signal: currentAbortController.signal
            });
        } catch (err) {
            if (err.name === 'AbortError') return null;
            throw new Error('Unable to reach AI service. Please check your connection.');
        }

        if (!response.ok) {
            if (response.status >= 500) {
                throw new Error('AI service is temporarily unavailable. Please try again in a moment.');
            }
            throw new Error('Request failed. Please try again.');
        }

        var contentType = response.headers.get('content-type') || '';

        // If server returned SSE stream, process it
        if (contentType.includes('text/event-stream')) {
            return await processSSEStream(response);
        }

        // Otherwise fall back to standard JSON with progressive rendering
        var result = await response.json();
        return { type: 'json', data: result };
    }

    async function processSSEStream(response) {
        var reader = response.body.getReader();
        var decoder = new TextDecoder();
        var buffer = '';
        var fullContent = '';
        var lastData = {};

        responseContent.textContent = '';
        responseArea.style.display = 'block';

        try {
            while (true) {
                var readResult = await reader.read();
                if (readResult.done) break;

                buffer += decoder.decode(readResult.value, { stream: true });

                // Process complete SSE events (separated by double newline)
                var events = buffer.split('\n\n');
                buffer = events.pop(); // keep incomplete event in buffer

                for (var i = 0; i < events.length; i++) {
                    var event = events[i].trim();
                    if (!event) continue;

                    var lines = event.split('\n');
                    var eventType = '';
                    var eventData = '';

                    for (var j = 0; j < lines.length; j++) {
                        var line = lines[j];
                        if (line.startsWith('event:')) {
                            eventType = line.substring(6).trim();
                        } else if (line.startsWith('data:')) {
                            eventData = line.substring(5).trim();
                        }
                    }

                    if (eventData === '[DONE]') {
                        continue;
                    }

                    if (eventData) {
                        try {
                            var parsed = JSON.parse(eventData);
                            if (parsed.content || parsed.delta || parsed.text) {
                                var chunk = parsed.content || parsed.delta || parsed.text || '';
                                fullContent += chunk;
                                responseContent.textContent = fullContent;

                                // Auto-scroll
                                var parent = responseContent.closest('.ww-ai-response-body, .ww-ai-response-area');
                                if (parent) parent.scrollTop = parent.scrollHeight;
                            }
                            // Preserve metadata from the final chunk
                            if (parsed.tokensUsed || parsed.model) {
                                lastData = parsed;
                            }
                        } catch (parseErr) {
                            // Non-JSON data line — treat as plain text chunk
                            fullContent += eventData;
                            responseContent.textContent = fullContent;
                        }
                    }
                }
            }
        } catch (err) {
            if (err.name === 'AbortError') return null;
            throw err;
        }

        return {
            type: 'stream',
            data: {
                succeeded: true,
                content: fullContent,
                model: lastData.model || '',
                usage: lastData.usage || { totalTokens: lastData.tokensUsed || 0 }
            }
        };
    }

    // ===== Standard API calls =====
    async function sendRequest(prompt) {
        currentAbortController = new AbortController();

        var response;
        try {
            response = await fetch(proxyUrl + '/request', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    configurationId: configurationId,
                    configurationName: configurationName,
                    prompt: prompt
                }),
                signal: currentAbortController.signal
            });
        } catch (err) {
            if (err.name === 'AbortError') return null;
            throw new Error('Unable to reach AI service. Please check your connection.');
        }

        if (!response.ok) {
            if (response.status >= 500) {
                throw new Error('AI service is temporarily unavailable. Please try again in a moment.');
            }
            throw new Error('Request failed. Please try again.');
        }

        return await response.json();
    }

    async function sendRequestWithFile(prompt, file) {
        currentAbortController = new AbortController();

        var formData = new FormData();
        formData.append('configurationId', configurationId || '');
        formData.append('configurationName', configurationName || '');
        formData.append('prompt', prompt);
        formData.append('file', file);

        var response;
        try {
            response = await fetch(proxyUrl + '/request-with-file', {
                method: 'POST',
                body: formData,
                signal: currentAbortController.signal
            });
        } catch (err) {
            if (err.name === 'AbortError') return null;
            throw new Error('Unable to reach AI service. Please check your connection.');
        }

        if (!response.ok) {
            if (response.status >= 500) {
                throw new Error('AI service is temporarily unavailable. Please try again in a moment.');
            }
            throw new Error('Request failed. Please try again.');
        }

        return await response.json();
    }

    // ===== Display response =====
    function displayResult(result) {
        if (!result) return;

        if (result.succeeded) {
            var content = result.content || '';

            if (streamingEnabled && content.length > 0) {
                renderProgressiveText(content, responseContent, {
                    speed: 8,
                    chunkSize: 4,
                    onComplete: function () {
                        showUsage(result);
                        invokeCallback(result);
                    }
                });
            } else {
                responseContent.textContent = content;
                responseArea.style.display = 'block';
                showUsage(result);
                invokeCallback(result);
            }
        } else {
            showMessage(result.errorMessage || 'AI request failed.', 'danger');
        }
    }

    function showUsage(result) {
        if (result.usage && result.usage.totalTokens > 0) {
            usageEl.textContent = 'Tokens used: ' + result.usage.totalTokens;
            if (result.model) usageEl.textContent += ' | Model: ' + result.model;
            usageEl.style.display = 'block';
        }
    }

    function invokeCallback(result) {
        if (onCompleteCallback && typeof window[onCompleteCallback] === 'function') {
            window[onCompleteCallback](result);
        }
    }

    // ===== Submit handler =====
    submitBtn.addEventListener('click', async function () {
        var prompt = promptEl.value.trim();
        if (!prompt && !selectedFile) {
            showMessage('Please enter a prompt or upload a file.', 'warning');
            return;
        }

        setLoading(true);
        hideMessage();
        cancelProgressiveRender();

        try {
            var result;

            if (selectedFile) {
                // File uploads always use standard JSON response
                result = await sendRequestWithFile(prompt, selectedFile);
                if (result) displayResult(result);
            } else if (streamingEnabled) {
                // Try streaming request (will fall back to JSON if server doesn't support SSE)
                var streamResult = await sendStreamingRequest(prompt);
                if (!streamResult) return; // aborted

                if (streamResult.type === 'stream') {
                    // SSE stream already rendered content progressively
                    showUsage(streamResult.data);
                    invokeCallback(streamResult.data);
                } else {
                    // Server returned JSON — use progressive text rendering
                    displayResult(streamResult.data);
                }
            } else {
                result = await sendRequest(prompt);
                if (result) displayResult(result);
            }
        } catch (err) {
            showMessage(err.message || 'An error occurred. Please try again.', 'danger');
        } finally {
            setLoading(false);
            currentAbortController = null;
        }
    });

    // ===== Keyboard shortcut: Ctrl+Enter to submit =====
    promptEl.addEventListener('keydown', function (e) {
        if (e.ctrlKey && e.key === 'Enter') {
            submitBtn.click();
        }
    });

    // ===== Copy button =====
    var copyBtn = document.getElementById('ww-ai-copy');
    if (copyBtn) {
        copyBtn.addEventListener('click', function () {
            var text = responseContent.textContent;
            navigator.clipboard.writeText(text).then(function () {
                copyBtn.innerHTML = '<i class="bi bi-check"></i> Copied';
                setTimeout(function () {
                    copyBtn.innerHTML = '<i class="bi bi-clipboard"></i> Copy';
                }, 2000);
            });
        });
    }

    // ===== Clear button =====
    var clearBtn = document.getElementById('ww-ai-clear');
    if (clearBtn) {
        clearBtn.addEventListener('click', function () {
            cancelProgressiveRender();
            if (currentAbortController) {
                currentAbortController.abort();
                currentAbortController = null;
            }
            responseArea.style.display = 'none';
            responseContent.textContent = '';
            usageEl.style.display = 'none';
            promptEl.value = '';
            clearFile();
            hideMessage();
            setLoading(false);
        });
    }
})();
