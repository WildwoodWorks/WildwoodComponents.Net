using System;
using System.Collections.Generic;

namespace WildwoodComponents.Shared.Models
{
    /// <summary>A published AI Flow (LangGraph) runnable by an app user.</summary>
    public class AIFlow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconClass { get; set; } = "fas fa-diagram-project";
        public List<AIFlowInputField> InputFields { get; set; } = new();
    }

    /// <summary>A state channel the run can be seeded with (drives the input form).</summary>
    public class AIFlowInputField
    {
        public string Name { get; set; } = string.Empty;
        public string Reducer { get; set; } = "overwrite";
    }

    /// <summary>One SSE frame from a flow run stream.</summary>
    public class AIFlowRunEvent
    {
        /// <summary>node_start | node_end | token | usage | interrupt | done | error</summary>
        public string Event { get; set; } = string.Empty;

        /// <summary>Raw event JSON payload (already deserialized to a dictionary-like object by the caller).</summary>
        public System.Text.Json.JsonElement Data { get; set; }
    }

    /// <summary>Terminal outcome of a flow run, surfaced to the component.</summary>
    public class AIFlowRunResult
    {
        public string Status { get; set; } = "unknown"; // succeeded | failed | cancelled | interrupted
        public string? OutputJson { get; set; }
        public string? ErrorMessage { get; set; }
        public int TotalTokens { get; set; }

        /// <summary>Set when the run paused for human review; the payload to display.</summary>
        public string? InterruptPayloadJson { get; set; }

        /// <summary>Run id (for resume/history). Populated once the run row is known.</summary>
        public string? RunId { get; set; }
        public string? ThreadId { get; set; }
    }

    public class AIFlowRunSummary
    {
        public string Id { get; set; } = string.Empty;
        public string FlowId { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string TriggerType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long? DurationMs { get; set; }
        public int TotalTokens { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>Display/behavior settings for AIFlowComponent.</summary>
    public class AIFlowSettings
    {
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string? AppId { get; set; }

        /// <summary>Fixed flow to run; when null the component shows a flow picker.</summary>
        public string? FlowId { get; set; }

        public bool ShowFlowPicker { get; set; } = true;
        public bool ShowLiveProgress { get; set; } = true;
        public bool ShowDebugInfo { get; set; } = false;
        public string Title { get; set; } = "AI Flows";
        public string RunLabel { get; set; } = "Run";
    }
}
