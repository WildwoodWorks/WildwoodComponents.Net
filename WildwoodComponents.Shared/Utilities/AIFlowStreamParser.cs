using System.Text;
using System.Text.Json;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Parses the api/ai/flows SSE run stream ("event:"/"data:" frames) into an
/// <see cref="AIFlowRunResult"/>, invoking a callback per frame. Shared by the Blazor
/// AIFlowService and the Razor WildwoodAIFlowService, which keep their auth/URL
/// handling local and delegate the stream parsing here.
/// </summary>
public static class AIFlowStreamParser
{
    /// <summary>
    /// Reads SSE frames off <paramref name="stream"/> until it ends, updating
    /// <paramref name="result"/> from run_started/usage metadata and the terminal
    /// events (done/interrupt/error), and invoking <paramref name="onEvent"/> for
    /// every frame. A final frame without a trailing blank line is still dispatched.
    /// </summary>
    public static async Task ParseAsync(
        Stream stream,
        AIFlowRunResult result,
        Func<AIFlowRunEvent, Task>? onEvent,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);

        string? eventName = null;
        var dataBuilder = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                dataBuilder.Append(line["data: ".Length..]);
            }
            else if (line.Length == 0 && eventName != null)
            {
                await DispatchAsync(eventName, dataBuilder.ToString(), onEvent, result);
                eventName = null;
                dataBuilder.Clear();
            }
        }

        // Dispatch a final event that arrived without a trailing blank line.
        if (eventName != null)
            await DispatchAsync(eventName, dataBuilder.ToString(), onEvent, result);
    }

    private static async Task DispatchAsync(
        string eventName, string dataText, Func<AIFlowRunEvent, Task>? onEvent, AIFlowRunResult result)
    {
        JsonElement data = default;
        if (dataText.Length > 0)
        {
            try
            {
                // Dispose the document so its pooled buffer is recycled; Clone() detaches
                // the element we keep past the using scope.
                using var doc = JsonDocument.Parse(dataText);
                data = doc.RootElement.Clone();
            }
            catch (JsonException) { /* leave default (ValueKind.Undefined) */ }
        }

        // A truncated/unparseable frame leaves data as Undefined; TryGetProperty throws on
        // anything but an Object, so guard every property read.
        var obj = data.ValueKind == JsonValueKind.Object;
        switch (eventName)
        {
            case "run_started":
                // Server emits this first, carrying the run + thread ids the client needs
                // for resume and thread continuity.
                if (obj)
                {
                    if (data.TryGetProperty("runId", out var runIdEl) && runIdEl.ValueKind == JsonValueKind.String)
                        result.RunId = runIdEl.GetString();
                    if (data.TryGetProperty("threadId", out var threadIdEl) && threadIdEl.ValueKind == JsonValueKind.String)
                        result.ThreadId = threadIdEl.GetString();
                }
                break;
            case "done":
                result.Status = obj && data.TryGetProperty("status", out var s) ? s.GetString() ?? "succeeded" : "succeeded";
                if (obj && data.TryGetProperty("output", out var output) && output.ValueKind != JsonValueKind.Null)
                    result.OutputJson = output.GetRawText();
                break;
            case "interrupt":
                result.Status = "interrupted";
                if (obj && data.TryGetProperty("payload", out var payload))
                    result.InterruptPayloadJson = payload.GetRawText();
                break;
            case "error":
                result.Status = "failed";
                result.ErrorMessage = obj && data.TryGetProperty("message", out var m) ? m.GetString() : "Run failed";
                break;
            case "usage":
                if (obj && data.TryGetProperty("totalTokens", out var t) && t.TryGetInt32(out var tokens))
                    result.TotalTokens += tokens;
                break;
        }

        if (onEvent != null)
            await onEvent(new AIFlowRunEvent { Event = eventName, Data = data });
    }
}
