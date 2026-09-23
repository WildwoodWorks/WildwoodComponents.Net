using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Controllers;

/// <summary>
/// Same-origin upload route for recorded voice input. <c>wwwroot/js/ai-chat.js</c> records a clip
/// with <c>MediaRecorder</c> where the browser has no working Web Speech API, and posts it here;
/// <see cref="IWildwoodAIChatService.TranscribeAudioAsync"/> forwards it to WildwoodAPI's
/// <c>stt/transcribe</c> with the session's bearer.
///
/// It ships WITH the library, like <see cref="WildwoodAttributionProxyController"/>,
/// <see cref="WildwoodNotificationsProxyController"/> and
/// <see cref="WildwoodRegistrationSubscriptionProxyController"/>, because a Razor app keeps the
/// user's JWT in the server session: the script cannot call WildwoodAPI itself, and leaving this
/// one route to every host would make voice input 404 silently in browsers that need it.
///
/// Host wiring: register MVC controllers (<c>builder.Services.AddControllers()</c> +
/// <c>app.MapControllers()</c>) and have server-side session available, as for the other three
/// proxies. If your host restricts controller discovery, add this assembly explicitly:
/// <c>AddControllers().AddApplicationPart(typeof(WildwoodSpeechProxyController).Assembly)</c>.
///
/// <para><b>The relay rule.</b> Every answer — success, refusal or precondition failure — is a
/// <see cref="SpeechTranscriptionResult"/>, so the script parses ONE shape whatever the status.
/// A <b>transcription</b> failure is data and comes back <b>200</b> with
/// <c>success:false</c> and the provider's message, because "speech-to-text is not configured"
/// has to read differently from "that route is missing". Only this proxy's OWN preconditions
/// produce 4xx, and nothing is forwarded when one fails: <b>401</b> with no signed-in session,
/// <b>415</b> for a body that is not <c>multipart/form-data</c> or a part that is not recorded
/// audio, <b>400</b> for anything but exactly one file part, and <b>413</b> for a clip past
/// <see cref="SpeechAudioFormats.MaxAudioBytes"/>. This is the same split the registration and
/// subscription proxy documents.</para>
///
/// <para><b>Not a file relay.</b> The part's content type is checked against
/// <see cref="SpeechAudioFormats.IsSupportedMediaType"/> — the same table that names the upload —
/// so this route forwards recorded audio and nothing else. Oversize bodies are refused by
/// <see cref="RequestSizeLimitAttribute"/> and <see cref="RequestFormLimitsAttribute"/> before the
/// form is buffered, and again by an explicit length check afterwards, so a clip past the cap
/// costs a rejection rather than an exception or 25 MB of someone else's memory.</para>
///
/// <para><b>Anti-forgery.</b> None, matching the other three shipped proxies — do not invent a
/// different scheme here. A host that wants CSRF tokens should apply its own filter or middleware
/// uniformly across all four.</para>
/// </summary>
[ApiController]
[Route("api/wildwood-stt")]
[Produces("application/json")]
public class WildwoodSpeechProxyController : ControllerBase
{
    /// <summary>
    /// The largest request body accepted at all: the audio cap plus a megabyte of margin for the
    /// multipart boundaries, the part headers and the two small text fields.
    /// </summary>
    public const long MaxRequestBytes = SpeechAudioFormats.MaxAudioBytes + (1024 * 1024);

    private readonly IWildwoodAIChatService _aiChatService;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodSpeechProxyController> _logger;

    public WildwoodSpeechProxyController(
        IWildwoodAIChatService aiChatService,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodSpeechProxyController> logger)
    {
        _aiChatService = aiChatService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/wildwood-stt/transcribe — transcribes one recorded clip for the signed-in user.
    /// </summary>
    /// <remarks>
    /// The form is read by hand rather than model-bound so that a body past the limits is a
    /// structured 413 instead of the framework's exception page, and so the 401 gate runs before
    /// anything is buffered at all.
    /// </remarks>
    [HttpPost("transcribe")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Transcribe()
    {
        if (!_sessionManager.IsAuthenticated)
        {
            return StatusCode(StatusCodes.Status401Unauthorized,
                SpeechAudioFormats.Failure(SpeechAudioFormats.NotSignedInMessage));
        }

        var contentType = Request.ContentType ?? string.Empty;
        if (!contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                SpeechAudioFormats.Failure(SpeechAudioFormats.SingleAudioFileMessage));
        }

        // A declared length past the limit is refused before a byte of it is buffered.
        if (Request.ContentLength is > MaxRequestBytes)
        {
            return TooLarge();
        }

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            // The body outgrew RequestSizeLimit / MultipartBodyLengthLimit mid-read. That is the
            // cap doing its job, so it is an answer, not a 500.
            _logger.LogInformation(ex, "Rejected an oversize speech upload");
            return TooLarge();
        }

        if (form.Files.Count != 1)
        {
            return BadRequest(SpeechAudioFormats.Failure(SpeechAudioFormats.SingleAudioFileMessage));
        }

        var file = form.Files[0];
        if (!SpeechAudioFormats.IsSupportedMediaType(file.ContentType))
        {
            _logger.LogInformation("Refused a speech upload of type {ContentType}", file.ContentType);
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                SpeechAudioFormats.Failure(SpeechAudioFormats.UnsupportedFormatMessage));
        }

        if (file.Length > SpeechAudioFormats.MaxAudioBytes)
        {
            return TooLarge();
        }
        if (file.Length <= 0)
        {
            return BadRequest(SpeechAudioFormats.Failure(SpeechAudioFormats.NoAudioMessage));
        }

        byte[] audio;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            using var content = file.OpenReadStream();
            await content.CopyToAsync(buffer, HttpContext.RequestAborted);
            audio = buffer.ToArray();
        }

        // A stream longer than its declared length is still capped here.
        if (audio.Length > SpeechAudioFormats.MaxAudioBytes)
        {
            return TooLarge();
        }

        var result = await _aiChatService.TranscribeAudioAsync(
            audio,
            file.ContentType,
            Field(form, "configurationId"),
            Field(form, "language"));

        // 200 even for success:false — the relay rule above.
        return Ok(result ?? SpeechAudioFormats.Failure(SpeechAudioFormats.GenericFailureMessage));
    }

    private IActionResult TooLarge()
        => StatusCode(StatusCodes.Status413PayloadTooLarge,
            SpeechAudioFormats.Failure(SpeechAudioFormats.TooLargeMessage));

    /// <summary>An optional text field, or null when it is absent or blank.</summary>
    private static string? Field(IFormCollection form, string name)
    {
        var value = form[name].ToString();
        return value.Length == 0 ? null : value;
    }
}
