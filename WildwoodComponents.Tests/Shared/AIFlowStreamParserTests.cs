using System.Text;
using System.Text.Json;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

public class AIFlowStreamParserTests
{
    private static async Task<(AIFlowRunResult Result, List<AIFlowRunEvent> Events)> ParseAsync(string sse)
    {
        var result = new AIFlowRunResult();
        var events = new List<AIFlowRunEvent>();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        await AIFlowStreamParser.ParseAsync(stream, result, evt =>
        {
            events.Add(evt);
            return Task.CompletedTask;
        });
        return (result, events);
    }

    [Fact]
    public async Task ParseAsync_MapsFrames_ToResultAndCallbacks()
    {
        var sse =
            "event: run_started\ndata: {\"runId\":\"run-1\",\"threadId\":\"thread-1\"}\n\n" +
            "event: token\ndata: {\"content\":\"Hi\"}\n\n" +
            "event: usage\ndata: {\"totalTokens\":42}\n\n" +
            "event: done\ndata: {\"status\":\"succeeded\",\"output\":{\"answer\":1}}\n\n";

        var (result, events) = await ParseAsync(sse);

        Assert.Equal("succeeded", result.Status);
        Assert.Equal("run-1", result.RunId);
        Assert.Equal("thread-1", result.ThreadId);
        Assert.Equal(42, result.TotalTokens);
        Assert.Equal("""{"answer":1}""", result.OutputJson);
        Assert.Equal(new[] { "run_started", "token", "usage", "done" },
            events.ConvertAll(e => e.Event).ToArray());
    }

    [Fact]
    public async Task ParseAsync_DispatchesFinalFrame_WithoutTrailingBlankLine_WithItsData()
    {
        // The terminal frame may end the stream without its trailing blank line — it must
        // still be dispatched WITH the accumulated data (mirrored by ai-flow.js and the
        // @wildwood/core stream reader).
        var sse =
            "event: token\ndata: {\"content\":\"Hi\"}\n\n" +
            "event: done\ndata: {\"status\":\"failed\"}";

        var (result, events) = await ParseAsync(sse);

        Assert.Equal("failed", result.Status);
        Assert.Equal(2, events.Count);
        Assert.Equal("done", events[1].Event);
        Assert.Equal(JsonValueKind.Object, events[1].Data.ValueKind);
    }

    [Fact]
    public async Task ParseAsync_MapsInterrupt_WithPayload()
    {
        var sse = "event: interrupt\ndata: {\"payload\":{\"question\":\"approve?\"}}\n\n";

        var (result, _) = await ParseAsync(sse);

        Assert.Equal("interrupted", result.Status);
        Assert.Equal("""{"question":"approve?"}""", result.InterruptPayloadJson);
    }
}
