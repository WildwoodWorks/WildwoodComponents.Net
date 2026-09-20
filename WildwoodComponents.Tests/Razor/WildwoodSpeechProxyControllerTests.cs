using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Controllers;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The speech proxy's HTTP contract toward ai-chat.js: one answer shape whatever the status,
/// 4xx only for this proxy's own preconditions (and nothing forwarded when one fails), 200 with
/// the DTO for everything the upstream actually answered.
/// </summary>
public class WildwoodSpeechProxyControllerTests
{
    private const string Multipart = "multipart/form-data; boundary=----ww";

    /// <summary>Records what reached the service, and answers whatever the test set.</summary>
    private sealed class FakeAIChatService : IWildwoodAIChatService
    {
        public SpeechTranscriptionResult Result { get; set; } = new() { Success = true, Text = "hello" };
        public int Calls { get; private set; }
        public byte[]? Audio { get; private set; }
        public string? ContentType { get; private set; }
        public string? ConfigurationId { get; private set; }
        public string? Language { get; private set; }

        public Task<SpeechTranscriptionResult> TranscribeAudioAsync(byte[] audio, string contentType, string? configurationId = null, string? language = null)
        {
            Calls++;
            Audio = audio;
            ContentType = contentType;
            ConfigurationId = configurationId;
            Language = language;
            return Task.FromResult(Result);
        }

        // Not reachable from this controller.
        public Task<List<AIConfiguration>> GetConfigurationsAsync(string? configurationType = null) => throw new NotSupportedException();
        public Task<AIConfiguration?> GetConfigurationAsync(string configurationId) => throw new NotSupportedException();
        public Task<AIChatResponse> SendMessageAsync(AIChatRequest request) => throw new NotSupportedException();
        public Task<AIChatResponse> SendMessageWithFileAsync(AIChatRequest request, byte[] fileBytes, string fileName) => throw new NotSupportedException();
        public Task<AISession?> CreateSessionAsync(string configurationId, string? sessionName = null) => throw new NotSupportedException();
        public Task<AISession?> GetSessionAsync(string sessionId) => throw new NotSupportedException();
        public Task<List<AISessionSummary>> GetSessionsAsync(string? configurationId = null) => throw new NotSupportedException();
        public Task<bool> EndSessionAsync(string sessionId) => throw new NotSupportedException();
        public Task<bool> DeleteSessionAsync(string sessionId) => throw new NotSupportedException();
        public Task<bool> RenameSessionAsync(string sessionId, string newName) => throw new NotSupportedException();
        public Task<List<TTSVoice>> GetTTSVoicesAsync(string? configurationId = null) => throw new NotSupportedException();
        public Task<(string AudioBase64, string ContentType)?> SynthesizeSpeechAsync(string text, string voice, double speed = 1.0, string? configurationId = null) => throw new NotSupportedException();
    }

    private static IFormFile FilePart(string contentType, byte[]? bytes = null, long? declaredLength = null)
    {
        var content = bytes ?? new byte[] { 1, 2, 3, 4 };
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, declaredLength ?? content.Length, "file", "speech.webm")
        {
            Headers = new HeaderDictionary { ["Content-Type"] = contentType }
        };
    }

    private static WildwoodSpeechProxyController CreateController(
        FakeAIChatService service,
        string? accessToken = "jwt-1",
        string contentType = Multipart,
        IFormFile[]? files = null,
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues>? fields = null,
        long? contentLength = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = contentType;
        if (contentLength.HasValue) context.Request.ContentLength = contentLength;

        if (files is not null || fields is not null)
        {
            var collection = new FormFileCollection();
            foreach (var file in files ?? Array.Empty<IFormFile>()) collection.Add(file);
            context.Request.Form = new FormCollection(
                fields ?? new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
                collection);
        }

        return new WildwoodSpeechProxyController(
            service, new FakeSessionManager(accessToken), NullLogger<WildwoodSpeechProxyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static SpeechTranscriptionResult BodyOf(IActionResult result)
    {
        var value = result switch
        {
            ObjectResult objectResult => objectResult.Value,
            _ => null
        };
        return Assert.IsType<SpeechTranscriptionResult>(value);
    }

    private static int StatusOf(IActionResult result) =>
        Assert.IsAssignableFrom<ObjectResult>(result).StatusCode ?? StatusCodes.Status200OK;

    [Fact]
    public async Task A_signed_out_session_is_rejected_without_forwarding_anything()
    {
        var service = new FakeAIChatService();
        var controller = CreateController(service, accessToken: null, files: [FilePart("audio/webm")]);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
        Assert.Equal(0, service.Calls);
        // Even the refusal is a SpeechTranscriptionResult, so the script parses one shape.
        Assert.False(BodyOf(result).Success);
        Assert.Equal(SpeechAudioFormats.NotSignedInMessage, BodyOf(result).ErrorMessage);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("")]
    public async Task A_body_that_is_not_multipart_is_refused(string contentType)
    {
        var service = new FakeAIChatService();
        var controller = CreateController(service, contentType: contentType, files: [FilePart("audio/webm")]);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, StatusOf(result));
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task A_part_that_is_not_recorded_audio_is_refused_so_this_is_not_a_file_relay()
    {
        var service = new FakeAIChatService();
        var controller = CreateController(service, files: [FilePart("application/pdf")]);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, StatusOf(result));
        Assert.Equal(SpeechAudioFormats.UnsupportedFormatMessage, BodyOf(result).ErrorMessage);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task No_file_part_is_a_bad_request()
    {
        var service = new FakeAIChatService();
        var controller = CreateController(service, files: []);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task More_than_one_file_part_is_a_bad_request()
    {
        var service = new FakeAIChatService();
        var controller = CreateController(service, files: [FilePart("audio/webm"), FilePart("audio/webm")]);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task A_clip_past_the_cap_is_a_structured_refusal_not_an_exception()
    {
        var service = new FakeAIChatService();
        var controller = CreateController(service,
            files: [FilePart("audio/webm", declaredLength: SpeechAudioFormats.MaxAudioBytes + 1)]);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, StatusOf(result));
        Assert.Equal(SpeechAudioFormats.TooLargeMessage, BodyOf(result).ErrorMessage);
        Assert.Equal(0, service.Calls);
    }

    /// <summary>A declared body past the limit is refused before the form is buffered at all.</summary>
    [Fact]
    public async Task An_oversize_content_length_is_refused_before_the_form_is_read()
    {
        var service = new FakeAIChatService();
        // No form is attached: reaching ReadFormAsync at all would throw here.
        var controller = CreateController(service, contentLength: WildwoodSpeechProxyController.MaxRequestBytes + 1);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, StatusOf(result));
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task An_empty_clip_is_a_bad_request()
    {
        var service = new FakeAIChatService();
        var controller = CreateController(service, files: [FilePart("audio/webm", bytes: Array.Empty<byte>())]);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(SpeechAudioFormats.NoAudioMessage, BodyOf(result).ErrorMessage);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task The_happy_path_relays_the_clip_and_its_fields()
    {
        var service = new FakeAIChatService { Result = new SpeechTranscriptionResult { Success = true, Text = "hello coach" } };
        var audio = Encoding.UTF8.GetBytes("opus-ish");
        var controller = CreateController(service,
            files: [FilePart("audio/webm;codecs=opus", audio)],
            fields: new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["configurationId"] = "cfg-1",
                ["language"] = "en"
            });

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        var body = BodyOf(result);
        Assert.True(body.Success);
        Assert.Equal("hello coach", body.Text);

        Assert.Equal(1, service.Calls);
        Assert.Equal(audio, service.Audio);
        Assert.Equal("audio/webm;codecs=opus", service.ContentType);
        Assert.Equal("cfg-1", service.ConfigurationId);
        Assert.Equal("en", service.Language);
    }

    [Fact]
    public async Task Absent_optional_fields_are_forwarded_as_null()
    {
        var service = new FakeAIChatService();
        var controller = CreateController(service, files: [FilePart("audio/webm")]);

        await controller.Transcribe();

        Assert.Null(service.ConfigurationId);
        Assert.Null(service.Language);
    }

    /// <summary>
    /// A refusal from the transcription provider is DATA — 200 with <c>success:false</c> — so the
    /// script can word "speech-to-text is not configured" differently from "that route is gone".
    /// </summary>
    [Fact]
    public async Task A_transcription_failure_is_relayed_as_a_two_hundred()
    {
        var service = new FakeAIChatService
        {
            Result = new SpeechTranscriptionResult { Success = false, ErrorMessage = "Speech-to-text is not configured." }
        };
        var controller = CreateController(service, files: [FilePart("audio/webm")]);

        var result = await controller.Transcribe();

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        var body = BodyOf(result);
        Assert.False(body.Success);
        Assert.Equal("Speech-to-text is not configured.", body.ErrorMessage);
    }

    [Fact]
    public void The_request_cap_leaves_room_for_the_multipart_envelope()
    {
        Assert.True(WildwoodSpeechProxyController.MaxRequestBytes > SpeechAudioFormats.MaxAudioBytes);
        Assert.Equal(SpeechAudioFormats.MaxAudioBytes + (1024 * 1024), WildwoodSpeechProxyController.MaxRequestBytes);
    }
}
