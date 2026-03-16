/**
 * WildwoodComponents.Razor - AI Flow Component JavaScript
 * Handles flow selection, dynamic form generation, execution, status polling,
 * and result display.
 * Razor Pages equivalent of the Blazor AIFlowComponent interactivity.
 */
(function () {
    'use strict';

    var roots = document.querySelectorAll('.ww-ai-flow-component');
    for (var r = 0; r < roots.length; r++) {
        initAIFlow(roots[r]);
    }

    function initAIFlow(root) {
        var cid = root.dataset.componentId;
        var appId = root.dataset.appId;
        var proxyUrl = root.dataset.proxyUrl;
        var messageEl = document.getElementById('ww-flow-message-' + cid);

        var selectionView = root.querySelector('.ww-flow-selection');
        var inputView = root.querySelector('.ww-flow-input');
        var progressView = root.querySelector('.ww-flow-progress');

        var flowForm = root.querySelector('.ww-flow-form');
        var flowFields = root.querySelector('.ww-flow-fields');
        var flowNameEl = root.querySelector('.ww-flow-name');
        var flowDescEl = root.querySelector('.ww-flow-description');
        var stepList = root.querySelector('.ww-step-list');
        var resultArea = root.querySelector('.ww-flow-result');
        var outputEl = root.querySelector('.ww-flow-output');
        var cancelBtn = root.querySelector('.ww-cancel-execution-btn');

        var currentFlow = null;
        var pollingTimer = null;

        // ===== HELPERS =====

        function showView(view) {
            if (selectionView) selectionView.style.display = view === 'selection' ? '' : 'none';
            if (inputView) inputView.style.display = view === 'input' ? '' : 'none';
            if (progressView) progressView.style.display = view === 'progress' ? '' : 'none';
        }

        function showMessage(text, type) {
            if (!messageEl) return;
            messageEl.textContent = text;
            messageEl.className = 'ww-alert ww-alert-' + type;
            messageEl.style.display = '';
        }

        function clearMessage() {
            if (messageEl) messageEl.style.display = 'none';
        }

        function setSubmitLoading(loading) {
            var btn = flowForm ? flowForm.querySelector('button[type="submit"]') : null;
            if (!btn) return;
            var text = btn.querySelector('.ww-btn-text');
            var spinner = btn.querySelector('.ww-btn-spinner');
            if (text) text.style.display = loading ? 'none' : '';
            if (spinner) spinner.style.display = loading ? '' : 'none';
            btn.disabled = loading;
        }

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
                        if (!r.ok) throw new Error(data.error || data.message || 'Request failed');
                        return data;
                    });
                }
                if (!r.ok) throw new Error('Request failed (' + r.status + ')');
                return r.text();
            });
        }

        // ===== FLOW SELECTION =====

        root.addEventListener('click', function (e) {
            var card = e.target.closest('.ww-flow-card');
            if (!card) return;
            var flowId = card.dataset.flowId;
            clearMessage();
            loadFlowDefinition(flowId);
        });

        function loadFlowDefinition(flowId) {
            apiCall('GET', '/definitions/' + appId + '/' + flowId)
                .then(function (flow) {
                    currentFlow = flow;
                    if (flowNameEl) flowNameEl.textContent = flow.name || '';
                    if (flowDescEl) flowDescEl.textContent = flow.description || '';
                    buildInputForm(flow);
                    showView('input');
                })
                .catch(function (err) {
                    showMessage('Failed to load flow: ' + err.message, 'danger');
                });
        }

        // ===== DYNAMIC FORM =====

        function buildInputForm(flow) {
            if (!flowFields) return;
            flowFields.innerHTML = '';

            var fields = flow.inputFields || [];
            if (fields.length === 0 && flow.inputSchemaJson) {
                fields = parseInputSchema(flow.inputSchemaJson);
            }

            if (fields.length === 0) {
                flowFields.innerHTML = '<p class="text-muted">This flow has no input parameters.</p>';
                return;
            }

            for (var i = 0; i < fields.length; i++) {
                var field = fields[i];
                var group = document.createElement('div');
                group.className = 'mb-3';

                var label = document.createElement('label');
                label.className = 'form-label';
                label.textContent = field.label || field.name;
                if (field.required) {
                    var req = document.createElement('span');
                    req.className = 'text-danger ms-1';
                    req.textContent = '*';
                    label.appendChild(req);
                }
                group.appendChild(label);

                var input;
                if (field.type === 'checkbox') {
                    var check = document.createElement('div');
                    check.className = 'form-check';
                    input = document.createElement('input');
                    input.type = 'checkbox';
                    input.className = 'form-check-input';
                    input.name = field.name;
                    if (field.defaultValue === 'true' || field.defaultValue === 'True') input.checked = true;
                    check.appendChild(input);
                    var checkLabel = document.createElement('label');
                    checkLabel.className = 'form-check-label';
                    checkLabel.textContent = field.label || field.name;
                    check.appendChild(checkLabel);
                    group.innerHTML = '';
                    group.appendChild(check);
                } else if (field.type === 'textarea') {
                    input = document.createElement('textarea');
                    input.className = 'form-control';
                    input.name = field.name;
                    input.rows = 3;
                    input.placeholder = field.placeholder || '';
                    if (field.defaultValue) input.value = field.defaultValue;
                    if (field.required) input.required = true;
                    group.appendChild(input);
                } else {
                    input = document.createElement('input');
                    input.type = field.type || 'text';
                    input.className = 'form-control';
                    input.name = field.name;
                    input.placeholder = field.placeholder || '';
                    if (field.defaultValue) input.value = field.defaultValue;
                    if (field.required) input.required = true;
                    group.appendChild(input);
                }

                flowFields.appendChild(group);
            }
        }

        function parseInputSchema(schemaJson) {
            try {
                var schema = JSON.parse(schemaJson);
                var fields = [];
                var required = schema.required || [];
                var props = schema.properties || {};
                for (var name in props) {
                    if (!props.hasOwnProperty(name)) continue;
                    var prop = props[name];
                    var type = 'text';
                    if (prop.type === 'integer' || prop.type === 'number') type = 'number';
                    else if (prop.type === 'boolean') type = 'checkbox';
                    fields.push({
                        name: name,
                        label: prop.title || name,
                        placeholder: prop.description || '',
                        type: type,
                        required: required.indexOf(name) !== -1,
                        defaultValue: prop.default !== undefined ? String(prop.default) : ''
                    });
                }
                return fields;
            } catch (e) {
                return [];
            }
        }

        // ===== FORM SUBMISSION =====

        if (flowForm) {
            flowForm.addEventListener('submit', function (e) {
                e.preventDefault();
                if (!currentFlow) return;

                // Gather inputs
                var inputs = {};
                var elements = flowForm.elements;
                for (var i = 0; i < elements.length; i++) {
                    var el = elements[i];
                    if (!el.name) continue;
                    if (el.type === 'checkbox') {
                        inputs[el.name] = el.checked ? 'true' : 'false';
                    } else {
                        inputs[el.name] = el.value;
                    }
                }

                clearMessage();
                setSubmitLoading(true);

                apiCall('POST', '/execute/' + appId + '/' + currentFlow.id, { inputs: inputs })
                    .then(function (result) {
                        showView('progress');
                        if (cancelBtn) cancelBtn.style.display = '';
                        if (resultArea) resultArea.style.display = 'none';

                        var executionId = result.executionId || result.id;
                        if (executionId) {
                            startPolling(executionId);
                        } else {
                            // Synchronous result
                            displayResult(result);
                        }
                    })
                    .catch(function (err) {
                        showMessage('Execution failed: ' + err.message, 'danger');
                    })
                    .finally(function () {
                        setSubmitLoading(false);
                    });
            });
        }

        // ===== STATUS POLLING =====

        function startPolling(executionId) {
            if (pollingTimer) clearInterval(pollingTimer);

            pollingTimer = setInterval(function () {
                apiCall('GET', '/status/' + appId + '/' + executionId)
                    .then(function (status) {
                        updateProgress(status);

                        if (status.status === 'Completed' || status.status === 'Failed' || status.status === 'Cancelled') {
                            clearInterval(pollingTimer);
                            pollingTimer = null;
                            if (cancelBtn) cancelBtn.style.display = 'none';

                            if (status.status === 'Completed') {
                                displayResult(status);
                            } else if (status.status === 'Failed') {
                                showMessage('Flow execution failed: ' + (status.errorMessage || 'Unknown error'), 'danger');
                            } else {
                                showMessage('Flow execution was cancelled.', 'warning');
                            }
                        }
                    })
                    .catch(function () {
                        clearInterval(pollingTimer);
                        pollingTimer = null;
                        showMessage('Lost connection to execution status.', 'danger');
                    });
            }, 2000);
        }

        function updateProgress(status) {
            if (!stepList) return;
            var steps = status.steps || [];
            if (steps.length === 0) {
                stepList.innerHTML = '<div class="ww-step-item running">' +
                    '<span class="spinner-border spinner-border-sm me-2"></span>' +
                    (status.currentStep || 'Processing...') + '</div>';
                return;
            }

            stepList.innerHTML = '';
            for (var i = 0; i < steps.length; i++) {
                var step = steps[i];
                var stepDiv = document.createElement('div');
                var stepStatus = (step.status || '').toLowerCase();
                stepDiv.className = 'ww-step-item ' + stepStatus;

                var icon = stepStatus === 'completed' ? '<i class="bi bi-check-circle-fill text-success me-2"></i>' :
                    stepStatus === 'running' ? '<span class="spinner-border spinner-border-sm me-2"></span>' :
                        stepStatus === 'failed' ? '<i class="bi bi-x-circle-fill text-danger me-2"></i>' :
                            '<i class="bi bi-circle text-muted me-2"></i>';

                stepDiv.innerHTML = icon + (step.name || step.description || 'Step ' + (i + 1));
                stepList.appendChild(stepDiv);
            }
        }

        function displayResult(result) {
            if (resultArea) resultArea.style.display = '';
            if (outputEl) {
                var output = result.output || result.result || result;
                if (typeof output === 'object') {
                    outputEl.textContent = JSON.stringify(output, null, 2);
                } else {
                    outputEl.textContent = String(output);
                }
            }
        }

        // ===== CANCEL EXECUTION =====

        if (cancelBtn) {
            cancelBtn.addEventListener('click', function () {
                if (pollingTimer) {
                    clearInterval(pollingTimer);
                    pollingTimer = null;
                }
                // Attempt API cancel (best effort)
                if (currentFlow) {
                    apiCall('POST', '/cancel/' + appId + '/' + currentFlow.id).catch(function () { /* best-effort cancel */ });
                }
                cancelBtn.style.display = 'none';
                showMessage('Execution cancelled.', 'warning');
            });
        }

        // ===== BACK BUTTONS =====

        var backBtns = root.querySelectorAll('.ww-flow-back-btn');
        for (var i = 0; i < backBtns.length; i++) {
            backBtns[i].addEventListener('click', function () {
                if (pollingTimer) {
                    clearInterval(pollingTimer);
                    pollingTimer = null;
                }
                currentFlow = null;
                clearMessage();
                showView('selection');
            });
        }
    }
})();
