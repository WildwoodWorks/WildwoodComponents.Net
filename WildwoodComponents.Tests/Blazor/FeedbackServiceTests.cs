using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class FeedbackServiceTests
{
    private static (FeedbackService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new FeedbackService(
            handler.CreateClient("https://api.test/"),
            NullLogger<FeedbackService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task GetWidgetConfigAsync_ReturnsNull_AndSkipsRequest_ForEmptyAppId()
    {
        var (service, handler) = CreateService();

        var config = await service.GetWidgetConfigAsync("");

        Assert.Null(config);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetWidgetConfigAsync_DeserializesConfig_OnSuccess()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("AppComponentConfigurations/app-1/feedback/widget", """{"feedbackTypes":["Bug","Idea"]}""");

        var config = await service.GetWidgetConfigAsync("app-1");

        Assert.NotNull(config);
        Assert.Equal(new[] { "Bug", "Idea" }, config!.FeedbackTypes);
    }

    [Fact]
    public async Task GetWidgetConfigAsync_ReturnsNull_OnNonSuccess()
    {
        var (service, handler) = CreateService();
        handler.When("feedback/widget", HttpStatusCode.NotFound, "{}");

        Assert.Null(await service.GetWidgetConfigAsync("app-1"));
    }

    [Fact]
    public async Task SubmitFeedbackAsync_PostsToSystemFeedback_AndSucceeds()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("SystemFeedback", "{}");

        var result = await service.SubmitFeedbackAsync(new FeedbackSubmissionRequest
        {
            AppId = "app-1",
            Title = "Bug",
            Description = "It broke",
            FeedbackType = "Bug",
        });

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/SystemFeedback", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        // JsonContent.Create serializes with web (camelCase) defaults.
        Assert.Equal("Bug", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task SubmitFeedbackAsync_FlagsRateLimited_On429()
    {
        var (service, handler) = CreateService();
        handler.When("SystemFeedback", HttpStatusCode.TooManyRequests, "{}");

        var result = await service.SubmitFeedbackAsync(new FeedbackSubmissionRequest { Title = "x" });

        Assert.False(result.Success);
        Assert.True(result.RateLimited);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_SurfacesServerErrorMessage()
    {
        var (service, handler) = CreateService();
        handler.When("SystemFeedback", HttpStatusCode.BadRequest, """{"error":"Title is required"}""");

        var result = await service.SubmitFeedbackAsync(new FeedbackSubmissionRequest { Title = "" });

        Assert.False(result.Success);
        Assert.Equal("Title is required", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckDuplicateAsync_ReturnsEmpty_AndSkipsRequest_ForBlankTitle()
    {
        var (service, handler) = CreateService();

        var result = await service.CheckDuplicateAsync("   ", "app-1");

        Assert.False(result.HasPotentialDuplicate);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CheckDuplicateAsync_AppendsAppId_AndReturnsResult()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("duplicate-check", """{"hasPotentialDuplicate":true,"duplicateVoteCount":3}""");

        var result = await service.CheckDuplicateAsync("My title", "app-1");

        Assert.True(result.HasPotentialDuplicate);
        Assert.Equal(3, result.DuplicateVoteCount);
        var request = Assert.Single(handler.Requests);
        // Uri.ToString() unescapes %20 back to a space in the recorded URL.
        Assert.Contains("duplicate-check?title=My title", request.Url);
        Assert.Contains("appId=app-1", request.Url);
    }

    [Fact]
    public async Task VoteAsync_ReturnsFailure_ForEmptyId_WithoutCallingApi()
    {
        var (service, handler) = CreateService();

        var result = await service.VoteAsync("");

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task VoteAsync_PostsToVoteEndpoint_AndSucceeds()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("SystemFeedback/fb-1/vote", """{"voteCount":7}""");

        var result = await service.VoteAsync("fb-1");

        Assert.True(result.Success);
        Assert.Equal(7, result.VoteCount);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/SystemFeedback/fb-1/vote", request.Url);
    }
}
