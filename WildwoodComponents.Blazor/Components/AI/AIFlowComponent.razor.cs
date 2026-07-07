using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.AI
{
    /// <summary>
    /// Runs published "AI Flows with LangChain" for an app user: flow picker (or
    /// fixed FlowId), an auto-generated input form from the flow's state channels,
    /// live streamed progress, human-in-the-loop approval, and output rendering.
    /// </summary>
    public partial class AIFlowComponent : BaseWildwoodComponent
    {
        [Inject] private IAIFlowService FlowService { get; set; } = default!;

        [Parameter] public AIFlowSettings Settings { get; set; } = new();

        /// <summary>JWT for the current app user. Required to run flows.</summary>
        [Parameter] public string? AuthToken { get; set; }

        /// <summary>Raised with the run's terminal result (succeeded/failed/cancelled).</summary>
        [Parameter] public EventCallback<AIFlowRunResult> OnRunCompleted { get; set; }

        private readonly List<AIFlow> _flows = new();
        private readonly Dictionary<string, string> _inputs = new();
        private readonly List<string> _events = new();
        private readonly List<AIFlowRunSummary> _history = new();

        private bool _loadingFlows = true;
        private bool _running;
        private string? _selectedFlowId;
        private AIFlow? _selectedFlow;
        private string _rawInput = "{}";
        private string _streamText = string.Empty;
        // Streamed tokens accumulate here (O(1) append); _streamText is materialized
        // from it only on a throttled render to avoid O(n²) string concat + a render
        // per token on a long stream.
        private readonly StringBuilder _streamBuilder = new();
        private int _lastRenderTick;
        private string? _activeNode;
        private string? _pendingInterrupt;
        private bool _editingResume;
        private string _resumeEditValue = string.Empty;
        private string? _error;
        private string? _threadId;
        private string? _activeRunId;
        private string _lastAuthToken = string.Empty;
        private AIFlowRunResult? _result;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            FlowService.AuthenticationFailed += OnAuthFailed;

            if (!string.IsNullOrEmpty(Settings.ApiBaseUrl))
                FlowService.SetApiBaseUrl(Settings.ApiBaseUrl);
            if (!string.IsNullOrEmpty(Settings.AppId))
                FlowService.SetAppId(Settings.AppId);
            if (!string.IsNullOrEmpty(AuthToken))
                FlowService.SetAuthToken(AuthToken);
            _lastAuthToken = AuthToken ?? string.Empty;

            await LoadFlowsAsync();
        }

        protected override async Task OnParametersSetAsync()
        {
            var token = AuthToken ?? string.Empty;
            if (token == _lastAuthToken) return;

            var wasEmpty = string.IsNullOrEmpty(_lastAuthToken);
            _lastAuthToken = token;
            if (!string.IsNullOrEmpty(token))
                FlowService.SetAuthToken(token);

            // A token arriving after the initial (unauthenticated) load must
            // trigger a reload — otherwise the picker stays empty.
            if (wasEmpty && !string.IsNullOrEmpty(token))
                await LoadFlowsAsync();
        }

        private async Task LoadFlowsAsync()
        {
            _loadingFlows = true;
            _flows.Clear();
            var flows = await FlowService.GetFlowsAsync();
            _flows.AddRange(flows);
            _loadingFlows = false;

            // Auto-select a fixed flow, or the only flow.
            string? initialId = null;
            if (!string.IsNullOrEmpty(Settings.FlowId))
                initialId = Settings.FlowId;
            else if (_flows.Count == 1)
                initialId = _flows[0].Id;

            if (!string.IsNullOrEmpty(initialId))
                SelectFlow(initialId);
        }

        private void OnFlowSelected(ChangeEventArgs e) => SelectFlow(e.Value?.ToString());

        private void SelectFlow(string? flowId)
        {
            _selectedFlowId = flowId;
            _selectedFlow = null;
            _inputs.Clear();
            _rawInput = "{}";
            // A thread's checkpoint holds one flow's state — switching flows must
            // start a fresh thread, not resume the previous flow's checkpoint.
            _threadId = null;
            _activeRunId = null;
            _history.Clear();
            ResetRunState();

            if (string.IsNullOrEmpty(flowId)) return;
            foreach (var flow in _flows)
            {
                if (flow.Id == flowId)
                {
                    _selectedFlow = flow;
                    break;
                }
            }
        }

        private string GetInput(string name) => _inputs.TryGetValue(name, out var v) ? v : string.Empty;

        private void SetInput(string name, string? value) => _inputs[name] = value ?? string.Empty;

        private async Task RunAsync()
        {
            if (_selectedFlow == null) return;
            ResetRunState();

            var inputJson = BuildInputJson();
            if (inputJson == null)
            {
                _error = "Input must be valid JSON.";
                return;
            }

            _running = true;
            ResetCts();
            try
            {
                _result = await FlowService.RunFlowAsync(_selectedFlow.Id, inputJson, _threadId, HandleEventAsync, _cts!.Token);
                await FinishRunAsync();
            }
            finally
            {
                _running = false;
                SafeStateHasChanged();
            }
        }

        private Task ResolveAsync(bool approve) => ResolveAsync(approve, null);

        private async Task ResolveAsync(bool approve, string? valueJson)
        {
            if (string.IsNullOrEmpty(_activeRunId)) return;
            // Keep the payload so a failed resume can restore the review panel.
            var interruptPayload = _pendingInterrupt;
            _pendingInterrupt = null;
            _editingResume = false;
            _running = true;
            ResetCts();
            try
            {
                _result = await FlowService.ResolveInterruptAsync(_activeRunId, approve, valueJson, HandleEventAsync, _cts!.Token);
                await FinishRunAsync();
                // A failed resume leaves the interrupt unresolved server-side — restore
                // the review panel so Approve/Reject can be retried.
                if (_result?.Status == "failed")
                    _pendingInterrupt = interruptPayload;
            }
            finally
            {
                _running = false;
                SafeStateHasChanged();
            }
        }

        private void StartResumeEdit()
        {
            _editingResume = true;
            _error = null;
            // Start empty: an unchanged submit falls back to the server's default
            // resolution (BuildApprovalResolution), which is shape-correct for BOTH
            // agent HITL and plain interrupt nodes. The textarea shows an example
            // via its placeholder for anyone who does want to craft a value.
            _resumeEditValue = string.Empty;
        }

        private void CancelResumeEdit() => _editingResume = false;

        private async Task SubmitResumeEditAsync()
        {
            var trimmed = _resumeEditValue?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                await ResolveAsync(true, null); // empty edit → default approve
                return;
            }
            try
            {
                using var _ = JsonDocument.Parse(trimmed); // fail fast on malformed JSON
            }
            catch (JsonException)
            {
                _error = "Edited resume value must be valid JSON.";
                return;
            }
            await ResolveAsync(true, trimmed);
        }

        private async Task HandleEventAsync(AIFlowRunEvent evt)
        {
            switch (evt.Event)
            {
                case "run_started":
                    _activeRunId = ReadString(evt.Data, "runId") ?? _activeRunId;
                    var threadId = ReadString(evt.Data, "threadId");
                    if (!string.IsNullOrEmpty(threadId)) _threadId = threadId;
                    break;
                case "node_start":
                    _activeNode = ReadString(evt.Data, "node");
                    break;
                case "node_end":
                    if (_activeNode == ReadString(evt.Data, "node")) _activeNode = null;
                    break;
                case "token":
                    _streamBuilder.Append(ReadString(evt.Data, "content") ?? string.Empty);
                    break;
                case "interrupt":
                    if (evt.Data.ValueKind == JsonValueKind.Object &&
                        evt.Data.TryGetProperty("payload", out var payload))
                    {
                        _pendingInterrupt = payload.ToString();
                    }
                    break;
            }

            if (Settings.ShowDebugInfo)
            {
                _events.Add($"{evt.Event}: {ShortData(evt)}");
                if (_events.Count > 200) _events.RemoveAt(0);
            }

            // Render immediately for structural events; throttle token-only renders
            // to ~10/s so a long stream doesn't fire thousands of SignalR diffs.
            var isToken = evt.Event == "token";
            var now = Environment.TickCount;
            if (!isToken || now - _lastRenderTick >= 100)
            {
                _lastRenderTick = now;
                _streamText = _streamBuilder.ToString();
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task FinishRunAsync()
        {
            // Materialize any tokens that arrived since the last throttled render.
            _streamText = _streamBuilder.ToString();
            if (_result != null)
            {
                if (!string.IsNullOrEmpty(_result.RunId)) _activeRunId = _result.RunId;
                if (!string.IsNullOrEmpty(_result.ThreadId)) _threadId = _result.ThreadId;
                _activeNode = null;
                if (_result.Status != "interrupted")
                    _pendingInterrupt = null;
                if (OnRunCompleted.HasDelegate && _result.Status != "interrupted")
                    await OnRunCompleted.InvokeAsync(_result);
            }
            await LoadHistoryAsync();
        }

        /// <summary>
        /// Refreshes the current thread's run history (this run + prior runs on
        /// the same conversation). Best-effort: history is an enrichment and a
        /// lookup failure must never disturb the run result already on screen.
        /// </summary>
        private async Task LoadHistoryAsync()
        {
            if (!Settings.ShowRunHistory || string.IsNullOrEmpty(_threadId)) return;
            try
            {
                var runs = await FlowService.GetThreadRunsAsync(_threadId);
                _history.Clear();
                _history.AddRange(runs);
            }
            catch
            {
                // keep whatever history was shown before
            }
        }

        private void Cancel()
        {
            _cts?.Cancel();
        }

        private void ResetCts()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }

        private void SafeStateHasChanged()
        {
            if (!_disposed) StateHasChanged();
        }

        private void ResetRunState()
        {
            _streamText = string.Empty;
            _streamBuilder.Clear();
            _activeNode = null;
            _pendingInterrupt = null;
            _editingResume = false;
            _resumeEditValue = string.Empty;
            _error = null;
            _result = null;
            _events.Clear();
        }

        private string? BuildInputJson()
        {
            if (_selectedFlow != null && _selectedFlow.InputFields.Count > 0)
            {
                // Preserve JSON types: numbers/bools/null/objects entered in a
                // field pass through as typed values; everything else is a string.
                var obj = new Dictionary<string, object?>();
                foreach (var field in _selectedFlow.InputFields)
                {
                    if (_inputs.TryGetValue(field.Name, out var v) && !string.IsNullOrEmpty(v))
                        obj[field.Name] = ParseInputValue(v);
                }
                return JsonSerializer.Serialize(obj);
            }

            // Free-form JSON input must be a valid object.
            var raw = string.IsNullOrWhiteSpace(_rawInput) ? "{}" : _rawInput;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                return raw;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private void OnAuthFailed(object? sender, EventArgs e)
        {
            _error = "Your session has expired. Please sign in again.";
            _running = false;
            InvokeAsync(SafeStateHasChanged);
        }

        /// <summary>Parses a raw field value into a typed JSON value where possible.</summary>
        private static object? ParseInputValue(string raw)
        {
            var trimmed = raw.Trim();
            if (trimmed == "true") return true;
            if (trimmed == "false") return false;
            if (trimmed == "null") return null;
            // Only treat as a number when the whole string is numeric (not "5 apples").
            if (long.TryParse(trimmed, out var l)) return l;
            if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            // JSON object/array typed through verbatim; anything else stays a string.
            if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
                (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
            {
                try { return JsonSerializer.Deserialize<JsonElement>(trimmed); }
                catch (JsonException) { return raw; }
            }
            return raw;
        }

        private static string? ReadString(JsonElement data, string property)
        {
            if (data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        private static string ShortData(AIFlowRunEvent evt)
        {
            var text = evt.Data.ValueKind == JsonValueKind.Undefined ? string.Empty : evt.Data.ToString();
            return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                FlowService.AuthenticationFailed -= OnAuthFailed;
                _cts?.Cancel();
                _cts?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
