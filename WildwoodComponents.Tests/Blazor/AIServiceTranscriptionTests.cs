using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// AIService.TranscribeAudioAsync — the client half of server-side speech-to-text
/// (POST /api/stt/transcribe), used by browsers without a working Web Speech API.
/// </summary>
public class AIServiceTranscriptionTests
{
    private static readonly byte[] Audio = { 1, 2, 3, 4 };

    private static (AIService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new AIService(
            handler.CreateClient("https://api.test/"),
            new FakeLocalStorageService(),
            NullLogger<AIService>.Instance);
        service.SetApiBaseUrl("https://api.test/api");
        service.SetAuthToken("jwt-1");
        return (service, handler);
    }

    // Normalize quotes so the checks hold whether the runtime emits name=file or name="file"
    // (same convention as DocumentServiceTests).
    private static string Unquoted(string? body) => (body ?? string.Empty).Replace("\"", string.Empty);

    [Fact]
    public async Task TranscribeAudioAsync_PostsMultipartToSttEndpoint_AndReturnsText()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":true,"text":"hello coach","language":"en"}""");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm;codecs=opus", "cfg-1", "en");

        Assert.True(result.Success);
        Assert.Equal("hello coach", result.Text);
        Assert.Null(result.ErrorMessage);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.test/api/stt/transcribe", request.Url);
        Assert.Equal("Bearer jwt-1", request.Authorization);
        var body = Unquoted(request.Body);
        Assert.Contains("name=file", body);
        Assert.Contains("filename=speech.webm", body);
        Assert.Contains("Content-Type: audio/webm", body);
        Assert.DoesNotContain("codecs=", body);
        Assert.Contains("name=configurationId", body);
        Assert.Contains("cfg-1", body);
        Assert.Contains("name=language", body);
    }

    [Theory]
    [InlineData("audio/webm;codecs=opus", "speech.webm", "audio/webm")]
    [InlineData("audio/ogg;codecs=opus", "speech.ogg", "audio/ogg")]
    [InlineData("audio/mp4", "speech.mp4", "audio/mp4")]
    [InlineData("audio/mp4;codecs=mp4a.40.2", "speech.mp4", "audio/mp4")]
    public async Task TranscribeAudioAsync_FilePartMatchesRecordedFormat(string contentType, string expectedFileName, string expectedPartType)
    {
        var (service, handler) = CreateService();
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
        var (service, handler) = CreateService();
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
        var (service, handler) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":true}""");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task TranscribeAudioAsync_ServerReportsFailure_SurfacesItsMessage()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("stt/transcribe", """{"success":false,"errorMessage":"Speech-to-text is not configured."}""");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.False(result.Success);
        Assert.Equal("Speech-to-text is not configured.", result.ErrorMessage);
    }

    [Fact]
    public async Task TranscribeAudioAsync_BadRequestWithBody_SurfacesItsMessage()
    {
        var (service, handler) = CreateService();
        handler.When("stt/transcribe", HttpStatusCode.BadRequest,
            """{"success":false,"errorMessage":"Unsupported audio format 'audio/aac'."}""");

        var result = await service.TranscribeAudioAsync(Audio, "audio/aac");

        Assert.False(result.Success);
        Assert.Equal("Unsupported audio format 'audio/aac'.", result.ErrorMessage);
    }

    [Fact]
    public async Task TranscribeAudioAsync_ServerErrorWithoutJson_ReportsStatus()
    {
        var (service, handler) = CreateService();
        handler.When("stt/transcribe", HttpStatusCode.InternalServerError, "oops");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.False(result.Success);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task TranscribeAudioAsync_Unauthorized_RaisesAuthenticationFailed_AndDoesNotThrow()
    {
        var (service, handler) = CreateService();
        var fired = 0;
        service.AuthenticationFailed += (_, _) => fired++;
        handler.When("stt/transcribe", HttpStatusCode.Unauthorized, "{}");

        var result = await service.TranscribeAudioAsync(Audio, "audio/webm");

        Assert.False(result.Success);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task TranscribeAudioAsync_NoAudio_FailsWithoutCallingServer()
    {
        var (service, handler) = CreateService();

        var result = await service.TranscribeAudioAsync(Array.Empty<byte>(), "audio/webm");

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
    }
}
