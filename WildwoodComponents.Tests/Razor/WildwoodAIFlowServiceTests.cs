using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

public class WildwoodAIFlowServiceTests
{
    private static (WildwoodAIFlowService Service, FakeHttpMessageHandler Handler, FakeSessionManager Session) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var session = new FakeSessionManager();
        var service = new WildwoodAIFlowService(
            handler.CreateClient("https://api.test/api/"),
            session,
            NullLogger<WildwoodAIFlowService>.Instance);
        return (service, handler, session);
    }

    [Fact]
    public async Task GetFlowsAsync_CallsFlowsEndpoint_WithAuthHeader()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("ai/flows", """[{"id":"flow-1","name":"Summarize","inputFields":[{"name":"text"}]}]""");

        var flows = await service.GetFlowsAsync();

        var flow = Assert.Single(flows);
        Assert.Equal("Summarize", flow.Name);
        Assert.Equal("text", Assert.Single(flow.InputFields).Name);
        Assert.True(session.ApplyAuthorizationHeaderCalls > 0);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("ai/flows", request.Url);
        Assert.DoesNotContain("requestedAppId", request.Url);
    }

    [Fact]
    public async Task GetFlowsAsync_ForwardsRequestedAppId()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("ai/flows", "[]");

        await service.GetFlowsAsync("app-1");

        Assert.Contains("ai/flows?requestedAppId=app-1", handler.Requests[0].Url);
    }

    [Fact]
    public async Task GetFlowsAsync_ReturnsEmpty_OnError()
    {
        var (service, handler, _) = CreateService();
        handler.When("ai/flows", System.Net.HttpStatusCode.Forbidden, """{"error":"no access"}""");

        var flows = await service.GetFlowsAsync();

        Assert.Empty(flows);
    }

    [Fact]
    public async Task GetThreadRunsAsync_CallsThreadRunsEndpoint()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("threads/thread-1/runs",
            """[{"id":"run-1","flowId":"flow-1","threadId":"thread-1","status":"succeeded","totalTokens":42}]""");

        var runs = await service.GetThreadRunsAsync("thread-1", "app-1");

        var run = Assert.Single(runs);
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(42, run.TotalTokens);
        Assert.Contains("ai/flows/threads/thread-1/runs?requestedAppId=app-1", handler.Requests[0].Url);
    }

    [Fact]
    public async Task ResolveInterruptAsync_MapsNonSseResponse_ToCancelled()
    {
        // The reject path responds with a plain JSON body, not an SSE stream — the service
        // maps it to a terminal "cancelled" result.
        var (service, handler, _) = CreateService();
        handler.WhenOk("runs/run-1/resume", """{"status":"cancelled"}""");

        var result = await service.ResolveInterruptAsync("run-1", approve: false, valueJson: null, appId: null, onEvent: null);

        Assert.Equal("cancelled", result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("ai/flows/runs/run-1/resume", request.Url);
        Assert.Contains("\"action\":\"reject\"", request.Body);
    }
}
