using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// WildwoodAIChatService.TranscribeAudioAsync — the Razor client half of server-side
/// speech-to-text (POST api/stt/transcribe), wire-identical to Blazor's
/// <c>AIService.TranscribeAudioAsync</c> (see <c>AIServiceTranscriptionTests</c>).
/// </summary>
public class WildwoodAIChatServiceTranscriptionTests
{
    private static readonly byte[] Audio = { 1, 2, 3, 4 };

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));
    }

    private static (WildwoodAIChatService Service, FakeHttpMessageHandler Handler, FakeSessionManager Session) CreateService(
        string? accessToken = "test-jwt")
    {
        var handler = new FakeHttpMessageHandler();
        var session = new FakeSessionManager(accessToken);
        var service = new WildwoodAIChatService(
            handler.CreateClient("https://api.test/api/"),
            session,
            NullLogger<WildwoodAIChatService>.Instance);
        return (service, handler, session);
    }

    // Normalize quotes so the checks hold whether the runtime emits name=file or name="file"
    // (same convention as the Blazor transcription tests).
    private static string Unquoted(string? body) => (body ?? string.Empty).Replace("\"", string.Empty);

    [Fact]
    public async Task TranscribeAudioAsync_PostsMultipartToSttEndpoint_AndReturnsText()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":true,"text":"hello coach","language":"en"}""");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm;codecs=opus", "cfg-1", "en");

        Assert.True(result.Success);
        Assert.Equal("hello coach", result.Text);
        Assert.Null(result.ErrorMessage);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.test/api/stt/transcribe", request.Url);
        var body = Unquoted(request.Body);
        Assert.Contains("name=file", body);
        Assert.Contains("filename=speech.webm", body);
        Assert.Contains("Content-Type: audio/webm", body);
        Assert.DoesNotContain("codecs=", body);
        Assert.Contains("name=configurationId", body);
        Assert.Contains("cfg-1", body);
        Assert.Contains("name=language", body);
    }

    /// <summary>
    /// The bearer rides on the REQUEST. <c>ApplyAuthorizationHeader</c> would put it on the named
    /// client every other service in the scope shares — the hazard WebForms rule 4 forbids and the
    /// September sync deferred, which new code must not extend.
    /// </summary>
    [Fact]
    public async Task TranscribeAudioAsync_PutsTheBearerOnTheRequest_NotOnTheSharedClient()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":true,"text":"ok"}""");

        await service.TranscribeAudioAsync(Audio, "audio/webm");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer test-jwt", request.Authorization);
        Assert.Equal(0, session.ApplyAuthorizationHeaderCalls);
    }

    [Theory]
    [InlineData("audio/webm;codecs=opus", "speech.webm", "audio/webm")]
    [InlineData("audio/ogg;codecs=opus", "speech.ogg", "audio/ogg")]
    [InlineData("audio/mp4", "speech.mp4", "audio/mp4")]
    [InlineData("audio/mp4;codecs=mp4a.40.2", "speech.mp4", "audio/mp4")]
    [InlineData("audio/wav", "speech.wav", "audio/wav")]
    public async Task TranscribeAudioAsync_FilePartMatchesRecordedFormat(string contentType, string expectedFileName, string expectedPartType)
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":true,"text":"ok"}""");

        await service.TranscribeAudioAsync(Audio, contentType);

        var request = Assert.Single(handler.Requests);
        var body = Unquoted(request.Body);
        Assert.Contains($"filename={expectedFileName}", body);
        Assert.Contains($"Content-Type: {expectedPartType}", body);
    }

    [Fact]
    public async Task TranscribeAudioAsync_OmitsOptionalFields_WhenNotProvided()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":true,"text":"ok"}""");

        await service.TranscribeAudioAsync(Audio, "audio/ogg");

        var request = Assert.Single(handler.Requests);
        var body = Unquoted(request.Body);
        Assert.DoesNotContain("name=configurationId", body);
        Assert.DoesNotContain("name=language", body);
    }

    [Fact]
    public async Task TranscribeAudioAsync_EmptyTranscript_IsStillSuccess()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":true}""");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task TranscribeAudioAsync_ServerReportsFailure_SurfacesItsMessage()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":false,"errorMessage":"Speech-to-text is not configured."}""");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.False(result.Success);
        Assert.Equal("Speech-to-text is not configured.", result.ErrorMessage);
    }

    [Fact]
    public async Task TranscribeAudioAsync_BadRequestWithBody_SurfacesItsMessage()
    {
        var (service, handler, _) = CreateService();
        handler.When("stt/transcribe", HttpStatusCode.BadRequest,
            """{"success":false,"errorMessage":"Unsupported audio format 'audio/aac'."}""");

        var result = await service.TranscribeAudioAsync(Audio, "audio/aac");

        Assert.False(result.Success);
        Assert.Equal("Unsupported audio format 'audio/aac'.", result.ErrorMessage);
    }

    [Fact]
    public async Task TranscribeAudioAsync_ServerErrorWithoutJson_ReportsStatus()
    {
        var (service, handler, _) = CreateService();
        handler.When("stt/transcribe", HttpStatusCode.InternalServerError, "oops");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.False(result.Success);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task TranscribeAudioAsync_NoAudio_FailsWithoutCallingServer()
    {
        var (service, handler, _) = CreateService();

        var result = await service.TranscribeAudioAsync(Array.Empty<byte>(), "audio/webm");

        Assert.False(result.Success);
        Assert.Equal("No audio was recorded.", result.ErrorMessage);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TranscribeAudioAsync_NetworkFailure_DoesNotThrow()
    {
        var service = new WildwoodAIChatService(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://api.test/api/") },
            new FakeSessionManager(),
            NullLogger<WildwoodAIChatService>.Instance);

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.False(result.Success);
        Assert.Equal("Transcription failed. Please try again.", result.ErrorMessage);
    }

    /// <summary>A signed-out session still posts; the server decides, and a 401 is not an exception.</summary>
    [Fact]
    public async Task TranscribeAudioAsync_Unauthorized_IsAFailureNotAThrow()
    {
        var (service, handler, _) = CreateService(accessToken: null);
        handler.When("stt/transcribe", HttpStatusCode.Unauthorized, "{}");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.False(result.Success);
        Assert.Contains("401", result.ErrorMessage);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
    }
}
