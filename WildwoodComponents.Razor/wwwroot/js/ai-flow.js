/**
 * WildwoodComponents.Razor - AI Flow Component JavaScript
 * Runs published "AI Flows with LangChain": flow selection, dynamic input form,
 * SSE run streaming with live progress, human-in-the-loop approve/edit/reject,
 * thread run history, and cancellation.
 * Razor Pages equivalent of the Blazor AIFlowComponent interactivity.
 */
(function () {
    'use strict';

    var roots = document.querySelectorAll('.ww-flow-component');
    for (var r = 0; r < roots.length; r++) {
        if (!roots[r]._wwFlowInit) {
            roots[r]._wwFlowInit = true;
            initAIFlow(roots[r]);
        }
    }

    function initAIFlow(root) {
        var proxyUrl = (root.dataset.proxyUrl || '').replace(/\/+$/, '');
        var appId = root.dataset.appId || '';
        var fixedFlowId = root.dataset.flowId || '';
        var runLabel = root.dataset.runLabel || 'Run';
        var showPicker = root.dataset.showPicker === 'true';
        var showProgress = root.dataset.showProgress === 'true';
        var showDebug = root.dataset.showDebug === 'true';
        var showHistory = root.dataset.showHistory === 'true';

        var els = {
            loading: root.querySelector('.ww-flow-loading'),
            empty: root.querySelector('.ww-flow-empty'),
            picker: root.querySelector('.ww-flow-picker'),
            select: root.querySelector('.ww-flow-select'),
            description: root.querySelector('.ww-flow-description'),
            inputs: root.querySelector('.ww-flow-inputs'),
            runActions: root.querySelector('.ww-flow-run-actions'),
            runBtn: root.querySelector('.ww-flow-run-btn'),
            stopBtn: root.querySelector('.ww-flow-stop-btn'),
            error: root.querySelector('.ww-flow-error'),
            progress: root.querySelector('.ww-flow-progress'),
            activeNode: root.querySelector('.ww-flow-active-node'),
            stream: root.querySelector('.ww-flow-stream'),
            streamText: root.querySelector('.ww-flow-stream-text'),
            review: root.querySelector('.ww-flow-review'),
            reviewPayload: root.querySelector('.ww-flow-review-payload'),
            reviewEdit: root.querySelector('.ww-flow-review-edit'),
            reviewActions: root.querySelector('.ww-flow-review-actions'),
            resumeValue: root.querySelector('.ww-flow-resume-value'),
            resumeEditBtn: root.querySelector('.ww-flow-resume-edit-btn'),
            cancelEditBtn: root.querySelector('.ww-flow-cancel-edit-btn'),
            approveBtn: root.querySelector('.ww-flow-approve-btn'),
            editBtn: root.querySelector('.ww-flow-edit-btn'),
            rejectBtn: root.querySelector('.ww-flow-reject-btn'),
            result: root.querySelector('.ww-flow-result'),
            resultStatus: root.querySelector('.ww-flow-result-status'),
            resultTokens: root.querySelector('.ww-flow-result-tokens'),
            resultError: root.querySelector('.ww-flow-result-error'),
            resultOutput: root.querySelector('.ww-flow-result-output'),
            history: root.querySelector('.ww-flow-history'),
            historyRows: root.querySelector('.ww-flow-history-rows'),
            events: root.querySelector('.ww-flow-events')
        };

        var flows = [];
        var selectedFlow = null;
        var running = false;
        var threadId = null;      // conversation thread carried across runs of one flow
        var activeRunId = null;   // run id for resume/history
        var abortController = null;
        var streamBuffer = '';    // token accumulator; flushed to the DOM throttled
        var lastRenderTime = 0;
        var totalTokens = 0;
        var eventCount = 0;

        // ===== HELPERS =====

        function appQuery() {
            return appId ? '?requestedAppId=' + encodeURIComponent(appId) : '';
        }

        function show(el, visible) {
            if (el) el.style.display = visible ? '' : 'none';
        }

        function showError(message) {
            if (!els.error) return;
            els.error.textContent = message;
            show(els.error, !!message);
        }

        // Maps HTTP failures to user-facing messages. Only 401 means the session is gone;
        // 403 is a permission/feature denial (e.g. tier lacks AI_FLOWS).
        function httpErrorMessage(status, bodyText) {
            if (status === 401) return 'Your session has expired. Please sign in again.';
            if (status === 403) return "You don't have access to this flow.";
            return status + ': ' + (bodyText || 'Request failed');
        }

        function apiGet(path) {
            return fetch(proxyUrl + path, {
                method: 'GET',
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            }).then(function (r) {
                if (!r.ok) {
                    return r.text().catch(function () { return ''; }).then(function (text) {
                        throw new Error(httpErrorMessage(r.status, text));
                    });
                }
                return r.json();
            });
        }

        // ===== FLOW LIST =====

        function setFlows(list) {
            flows = list || [];
            show(els.loading, false);
            show(els.empty, flows.length === 0);

            if (els.select) {
                els.select.innerHTML = '';
                var placeholder = document.createElement('option');
                placeholder.value = '';
                placeholder.textContent = 'Choose a flow…';
                els.select.appendChild(placeholder);
                for (var i = 0; i < flows.length; i++) {
                    var opt = document.createElement('option');
                    opt.value = flows[i].id;
                    opt.textContent = flows[i].name;
                    els.select.appendChild(opt);
                }
            }
            show(els.picker, showPicker && !fixedFlowId && flows.length > 0);

            // Auto-select a fixed flow, or the only flow.
            var initialId = fixedFlowId || (flows.length === 1 ? flows[0].id : '');
            if (initialId) {
                if (els.select) els.select.value = initialId;
                selectFlow(initialId);
            }
        }

        function loadFlows() {
            apiGet('/flows' + appQuery())
                .then(setFlows)
                .catch(function (err) {
                    show(els.loading, false);
                    show(els.empty, flows.length === 0);
                    showError('Failed to load flows: ' + err.message);
                });
        }

        function selectFlow(flowId) {
            selectedFlow = null;
            // A thread's checkpoint holds one flow's state — switching flows must start a
            // fresh thread, not resume the previous flow's checkpoint.
            threadId = null;
            activeRunId = null;
            if (els.historyRows) els.historyRows.innerHTML = '';
            show(els.history, false);
            resetRunState();

            for (var i = 0; i < flows.length; i++) {
                if (flows[i].id === flowId) {
                    selectedFlow = flows[i];
                    break;
                }
            }

            if (els.description) {
                els.description.textContent = selectedFlow ? (selectedFlow.description || '') : '';
                show(els.description, !!(selectedFlow && selectedFlow.description));
            }

            if (selectedFlow) {
                buildInputForm(selectedFlow);
                show(els.inputs, true);
                show(els.runActions, true);
            } else {
                show(els.inputs, false);
                show(els.runActions, false);
            }
        }

        if (els.select) {
            els.select.addEventListener('change', function () {
                if (!running) selectFlow(this.value);
            });
        }

        // ===== DYNAMIC INPUT FORM =====

        function buildInputForm(flow) {
            if (!els.inputs) return;
            els.inputs.innerHTML = '';

            var fields = flow.inputFields || [];
            if (fields.length > 0) {
                for (var i = 0; i < fields.length; i++) {
                    var wrap = document.createElement('div');
                    wrap.className = 'ww-flow-field';
                    var label = document.createElement('label');
                    label.textContent = fields[i].name;
                    var input = document.createElement('input');
                    input.type = 'text';
                    input.className = 'ww-flow-input-field';
                    input.dataset.fieldName = fields[i].name;
                    wrap.appendChild(label);
                    wrap.appendChild(input);
                    els.inputs.appendChild(wrap);
                }
            } else {
                // Free-form JSON fallback when the flow declares no input channels.
                var rawWrap = document.createElement('div');
                rawWrap.className = 'ww-flow-field';
                var rawLabel = document.createElement('label');
                rawLabel.textContent = 'Input (JSON)';
                var rawInput = document.createElement('textarea');
                rawInput.rows = 3;
                rawInput.className = 'ww-flow-raw-input';
                rawInput.value = '{}';
                rawWrap.appendChild(rawLabel);
                rawWrap.appendChild(rawInput);
                els.inputs.appendChild(rawWrap);
            }
        }

        // Parses a raw field value into a typed JSON value where possible: bools/null/
        // whole-string numbers/JSON objects+arrays pass through typed; anything else stays
        // a string (so "5 apples" is not mangled into a number).
        function parseInputValue(raw) {
            var trimmed = raw.trim();
            if (trimmed === 'true') return true;
            if (trimmed === 'false') return false;
            if (trimmed === 'null') return null;
            if (/^-?\d+$/.test(trimmed) || /^-?\d*\.\d+(e[+-]?\d+)?$/i.test(trimmed)) {
                var num = Number(trimmed);
                if (!isNaN(num)) return num;
            }
            if ((trimmed.charAt(0) === '{' && trimmed.charAt(trimmed.length - 1) === '}') ||
                (trimmed.charAt(0) === '[' && trimmed.charAt(trimmed.length - 1) === ']')) {
                try { return JSON.parse(trimmed); } catch (e) { return raw; }
            }
            return raw;
        }

        // Builds the run's inputJson. Returns null when free-form JSON is invalid or not
        // an object (the run seeds state channels — only an object makes sense).
        function buildInputJson() {
            var fieldInputs = els.inputs ? els.inputs.querySelectorAll('.ww-flow-input-field') : [];
            if (fieldInputs.length > 0) {
                var obj = {};
                for (var i = 0; i < fieldInputs.length; i++) {
                    var value = fieldInputs[i].value;
                    if (value) obj[fieldInputs[i].dataset.fieldName] = parseInputValue(value);
                }
                return JSON.stringify(obj);
            }

            var rawInput = els.inputs ? els.inputs.querySelector('.ww-flow-raw-input') : null;
            var raw = rawInput && rawInput.value.trim() ? rawInput.value : '{}';
            try {
                var parsed = JSON.parse(raw);
                if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return null;
                return raw;
            } catch (e) {
                return null;
            }
        }

        // ===== RUN STATE =====

        function resetRunState() {
            streamBuffer = '';
            totalTokens = 0;
            eventCount = 0;
            if (els.streamText) els.streamText.textContent = '';
            show(els.stream, false);
            show(els.progress, false);
            show(els.review, false);
            show(els.reviewEdit, false);
            show(els.reviewActions, true);
            if (els.resumeValue) els.resumeValue.value = '';
            show(els.result, false);
            if (els.events) els.events.innerHTML = '';
            show(els.events, false);
            showError(null);
        }

        function setRunning(isRunning) {
            running = isRunning;
            if (els.runBtn) {
                els.runBtn.textContent = runLabel;
                show(els.runBtn, !isRunning);
            }
            show(els.stopBtn, isRunning);
            if (els.select) els.select.disabled = isRunning;
            var inputs = els.inputs ? els.inputs.querySelectorAll('input, textarea') : [];
            for (var i = 0; i < inputs.length; i++) inputs[i].disabled = isRunning;
            var reviewBtns = [els.approveBtn, els.editBtn, els.rejectBtn, els.resumeEditBtn, els.cancelEditBtn];
            for (var j = 0; j < reviewBtns.length; j++) {
                if (reviewBtns[j]) reviewBtns[j].disabled = isRunning;
            }
        }

        // Streamed tokens accumulate in streamBuffer; the DOM is updated at most ~10/s so a
        // long stream doesn't thrash layout. Structural events and run end flush immediately.
        function renderStream(force) {
            var now = Date.now();
            if (!force && now - lastRenderTime < 100) return;
            lastRenderTime = now;
            if (streamBuffer && els.streamText) {
                els.streamText.textContent = streamBuffer;
                show(els.stream, true);
            }
        }

        function logDebugEvent(eventName, dataText) {
            if (!showDebug || !els.events) return;
            eventCount++;
            var line = document.createElement('div');
            line.className = 'ww-flow-event';
            var shortData = (dataText || '').length > 120 ? dataText.substring(0, 120) + '…' : (dataText || '');
            line.textContent = eventName + ': ' + shortData;
            els.events.appendChild(line);
            while (els.events.childNodes.length > 200) {
                els.events.removeChild(els.events.firstChild);
            }
            show(els.events, true);
        }

        // ===== SSE STREAMING =====

        // POSTs to an SSE endpoint and parses "event:"/"data:" frames off the response body
        // reader (EventSource can't POST). Resolves with the terminal result object.
        function streamRun(path, body) {
            abortController = new AbortController();
            var result = { status: 'unknown', totalTokens: 0, errorMessage: null, outputJson: null, interruptPayload: null };
            var terminal = false;

            return fetch(proxyUrl + path, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Accept': 'text/event-stream' },
                credentials: 'same-origin',
                body: JSON.stringify(body),
                signal: abortController.signal
            }).then(function (response) {
                if (!response.ok) {
                    return response.text().catch(function () { return ''; }).then(function (text) {
                        result.status = 'failed';
                        result.errorMessage = httpErrorMessage(response.status, text);
                        return result;
                    });
                }

                // The reject path (and any non-approve resolution) responds with a plain
                // JSON body, not an SSE stream — map it to a terminal "cancelled" result.
                var contentType = response.headers.get('content-type') || '';
                if (contentType.indexOf('text/event-stream') !== 0) {
                    result.status = 'cancelled';
                    return result;
                }

                var reader = response.body.getReader();
                var decoder = new TextDecoder();
                var pending = '';     // undelivered text (a frame may span chunks)
                var eventName = null;
                var dataText = '';

                function dispatch(name, text) {
                    var data = null;
                    if (text) {
                        try { data = JSON.parse(text); } catch (e) { /* unparseable frame — ignore payload */ }
                    }
                    logDebugEvent(name, text);

                    switch (name) {
                        case 'run_started':
                            // Server emits this first, carrying the run + thread ids the
                            // client needs for resume and thread continuity.
                            if (data && data.runId) activeRunId = data.runId;
                            if (data && data.threadId) threadId = data.threadId;
                            break;
                        case 'node_start':
                            if (showProgress && els.activeNode) {
                                els.activeNode.textContent = (data && data.node) || '';
                                show(els.progress, true);
                            }
                            renderStream(true);
                            break;
                        case 'node_end':
                            if (els.activeNode && data && els.activeNode.textContent === data.node) {
                                show(els.progress, false);
                            }
                            renderStream(true);
                            break;
                        case 'token':
                            streamBuffer += (data && data.content) || '';
                            renderStream(false);
                            break;
                        case 'usage':
                            if (data && typeof data.totalTokens === 'number') {
                                totalTokens += data.totalTokens;
                                result.totalTokens = totalTokens;
                            }
                            break;
                        case 'interrupt':
                            result.status = 'interrupted';
                            result.interruptPayload = data && data.payload !== undefined ? data.payload : null;
                            renderStream(true);
                            break;
                        case 'done':
                            result.status = (data && data.status) || 'succeeded';
                            if (data && data.output !== undefined && data.output !== null) {
                                result.outputJson = JSON.stringify(data.output, null, 2);
                            }
                            terminal = true;
                            break;
                        case 'error':
                            result.status = 'failed';
                            result.errorMessage = (data && data.message) || 'Run failed';
                            terminal = true;
                            break;
                    }
                }

                function pump() {
                    return reader.read().then(function (chunk) {
                        if (chunk.done) return result;

                        pending += decoder.decode(chunk.value, { stream: true });
                        var lines = pending.split(/\r?\n/);
                        pending = lines.pop(); // last element may be a partial line

                        for (var i = 0; i < lines.length; i++) {
                            var line = lines[i];
                            if (line.indexOf('event: ') === 0) {
                                eventName = line.substring('event: '.length);
                            } else if (line.indexOf('data: ') === 0) {
                                dataText += line.substring('data: '.length);
                            } else if (line.length === 0 && eventName) {
                                dispatch(eventName, dataText);
                                eventName = null;
                                dataText = '';
                                if (terminal) {
                                    // Stop at the first terminal event — nothing meaningful
                                    // follows and cancelling frees the connection.
                                    reader.cancel().catch(function () { /* already closed */ });
                                    return result;
                                }
                            }
                        }
                        return pump();
                    });
                }

                return pump().then(function () {
                    // Dispatch a final event that arrived without a trailing blank line.
                    if (!terminal && eventName) dispatch(eventName, dataText);
                    return result;
                });
            }).catch(function (err) {
                if (err && err.name === 'AbortError') {
                    if (result.status === 'unknown') result.status = 'cancelled';
                } else {
                    result.status = 'failed';
                    result.errorMessage = err.message;
                }
                return result;
            });
        }

        function finishRun(result) {
            renderStream(true); // flush tokens that arrived since the last throttled render
            show(els.progress, false);
            setRunning(false);
            abortController = null;

            if (result.status === 'interrupted') {
                showReview(result.interruptPayload);
            } else {
                show(els.review, false);
                showResult(result);
            }

            root.dispatchEvent(new CustomEvent('ww-flow-run-completed', {
                detail: { status: result.status, totalTokens: totalTokens, errorMessage: result.errorMessage },
                bubbles: true
            }));

            loadHistory();
        }

        function showResult(result) {
            if (!els.result) return;
            els.result.className = 'ww-flow-result ww-flow-result--' + result.status;
            if (els.resultStatus) els.resultStatus.textContent = 'Result — ' + result.status;
            if (els.resultTokens) els.resultTokens.textContent = totalTokens > 0 ? ' · ' + totalTokens + ' tokens' : '';
            if (els.resultError) {
                els.resultError.textContent = result.errorMessage || '';
                show(els.resultError, !!result.errorMessage);
            }
            if (els.resultOutput) {
                els.resultOutput.textContent = result.outputJson || '';
                show(els.resultOutput, !result.errorMessage && !!result.outputJson);
            }
            show(els.result, true);
        }

        // ===== RUN / STOP =====

        function runFlow() {
            if (!selectedFlow || running) return;
            resetRunState();

            var inputJson = buildInputJson();
            if (inputJson === null) {
                showError('Input must be valid JSON.');
                return;
            }

            setRunning(true);
            streamRun('/flows/' + encodeURIComponent(selectedFlow.id) + '/runs/stream' + appQuery(),
                { inputJson: inputJson, threadId: threadId })
                .then(finishRun);
        }

        if (els.runBtn) els.runBtn.addEventListener('click', runFlow);

        if (els.stopBtn) {
            els.stopBtn.addEventListener('click', function () {
                if (abortController) abortController.abort();
            });
        }

        // ===== HUMAN REVIEW (INTERRUPT) =====

        function showReview(payload) {
            if (!els.review) return;
            if (els.reviewPayload) {
                els.reviewPayload.textContent = payload === null || payload === undefined
                    ? ''
                    : (typeof payload === 'string' ? payload : JSON.stringify(payload, null, 2));
            }
            show(els.reviewEdit, false);
            show(els.reviewActions, true);
            show(els.review, true);
        }

        function resolveInterrupt(approve, valueJson) {
            if (!activeRunId || running) return;
            show(els.review, false);
            showError(null);
            setRunning(true);
            streamRun('/runs/' + encodeURIComponent(activeRunId) + '/resume' + appQuery(),
                { action: approve ? 'approve' : 'reject', valueJson: valueJson || null })
                .then(finishRun);
        }

        if (els.approveBtn) {
            els.approveBtn.addEventListener('click', function () { resolveInterrupt(true, null); });
        }

        if (els.rejectBtn) {
            els.rejectBtn.addEventListener('click', function () { resolveInterrupt(false, null); });
        }

        if (els.editBtn) {
            els.editBtn.addEventListener('click', function () {
                // Start empty: an unchanged submit falls back to the server's default
                // resolution, which is shape-correct for BOTH agent HITL and plain interrupt
                // nodes. The textarea's placeholder shows an example for crafted values.
                if (els.resumeValue) els.resumeValue.value = '';
                showError(null);
                show(els.reviewActions, false);
                show(els.reviewEdit, true);
            });
        }

        if (els.cancelEditBtn) {
            els.cancelEditBtn.addEventListener('click', function () {
                show(els.reviewEdit, false);
                show(els.reviewActions, true);
            });
        }

        if (els.resumeEditBtn) {
            els.resumeEditBtn.addEventListener('click', function () {
                var trimmed = els.resumeValue ? els.resumeValue.value.trim() : '';
                if (!trimmed) {
                    resolveInterrupt(true, null); // empty edit → default approve
                    return;
                }
                try {
                    JSON.parse(trimmed); // fail fast on malformed JSON
                } catch (e) {
                    showError('Edited resume value must be valid JSON.');
                    return;
                }
                resolveInterrupt(true, trimmed);
            });
        }

        // ===== RUN HISTORY =====

        // Best-effort: history is an enrichment and a lookup failure must never disturb
        // the run result already on screen.
        function loadHistory() {
            if (!showHistory || !threadId || !els.historyRows) return;
            apiGet('/flows/threads/' + encodeURIComponent(threadId) + '/runs' + appQuery())
                .then(function (runs) {
                    els.historyRows.innerHTML = '';
                    for (var i = 0; i < runs.length; i++) {
                        var run = runs[i];
                        var row = document.createElement('div');
                        row.className = 'ww-flow-history-row ww-flow-history-row--' + run.status;

                        var status = document.createElement('span');
                        status.className = 'ww-flow-history-status';
                        status.textContent = run.status;
                        row.appendChild(status);

                        var time = document.createElement('span');
                        time.className = 'ww-flow-history-time';
                        time.textContent = new Date(run.createdAt).toLocaleString([], {
                            month: 'numeric', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit'
                        });
                        row.appendChild(time);

                        var meta = document.createElement('span');
                        meta.className = 'ww-flow-history-meta';
                        meta.textContent = run.totalTokens + ' tokens' +
                            (run.durationMs != null ? ' · ' + (run.durationMs / 1000).toFixed(1) + 's' : '');
                        row.appendChild(meta);

                        if (run.errorMessage) {
                            var warn = document.createElement('span');
                            warn.className = 'ww-flow-history-error';
                            warn.title = run.errorMessage;
                            warn.textContent = '⚠';
                            row.appendChild(warn);
                        }

                        els.historyRows.appendChild(row);
                    }
                    show(els.history, runs.length > 0);
                })
                .catch(function () { /* keep whatever history was shown before */ });
        }

        // ===== INITIALIZE =====

        // Server-rendered flows (data-flows) seed the picker; reload client-side when the
        // server render happened unauthenticated and came back empty.
        var preloaded = [];
        try {
            preloaded = JSON.parse(root.dataset.flows || '[]');
        } catch (e) { /* fall through to a client-side load */ }

        if (preloaded.length > 0) {
            setFlows(preloaded);
        } else {
            loadFlows();
        }
    }
})();
