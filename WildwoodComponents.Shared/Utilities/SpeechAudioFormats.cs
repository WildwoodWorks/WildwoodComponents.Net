using System;
using System.Collections.Generic;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// The pure parts of recorded voice input (speech-to-text): which container a recorded clip is,
/// what the upload is called, what the caps are, and the user-presentable failure messages.
/// </summary>
/// <remarks>
/// <para>
/// Both .NET stacks post the same multipart request to <c>api/stt/transcribe</c> — Blazor from
/// <c>AIService.TranscribeAudioAsync</c>, Razor from <c>WildwoodAIChatService.TranscribeAudioAsync</c>
/// — and the Razor speech proxy validates an upload against the SAME table before it forwards
/// anything. Three copies of one media-type map is three places for the supported formats to drift
/// apart, so the table lives here and all three read it.
/// </para>
/// <para>
/// The browser halves carry their own copy of these rules
/// (<c>WildwoodComponents.Blazor/wwwroot/js/ai-chat.js</c>,
/// <c>WildwoodComponents.Razor/wwwroot/js/ai-chat.js</c>, and
/// <c>@wildwood/react</c>'s <c>useSpeechInput.ts</c>) because they run before any server call.
/// Keep them in step with this file.
/// </para>
/// <para>
/// netstandard2.0-safe (WebForms consumes <c>WildwoodComponents.Shared</c>) and LINQ-free (Blazor
/// consumes it, and LINQ breaks iOS/MAUI at runtime). Nothing here throws.
/// </para>
/// </remarks>
public static class SpeechAudioFormats
{
    /// <summary>Longest clip a recorder captures before it stops itself and transcribes.</summary>
    public const int MaxRecordingSeconds = 60;

    /// <summary>Largest clip the server will transcribe. Bigger audio is refused, not posted.</summary>
    public const long MaxAudioBytes = 25L * 1024 * 1024;

    /// <summary>Shown when a stop produced no audio at all.</summary>
    public const string NoAudioMessage = "No audio was recorded.";

    /// <summary>The generic failure, used when nothing more specific is known.</summary>
    public const string GenericFailureMessage = "Transcription failed. Please try again.";

    /// <summary>Shown when a clip is past <see cref="MaxAudioBytes"/>.</summary>
    public const string TooLargeMessage = "Voice input is limited to 25 MB. Please record a shorter clip.";

    /// <summary>Shown when the upload is not recorded audio this library knows how to send.</summary>
    public const string UnsupportedFormatMessage = "That audio format cannot be transcribed.";

    /// <summary>Shown when voice input is used without a signed-in session.</summary>
    public const string NotSignedInMessage = "Sign in to use voice input.";

    /// <summary>Shown when the upload is not a single audio file part.</summary>
    public const string SingleAudioFileMessage = "Send one recorded audio file.";

    // Upload extension per recorded format - the server's transcription provider infers the
    // container from the file name. Mirrors WildwoodAPI's STTAudioFormats, and doubles as the
    // allow-list the Razor speech proxy validates an upload's content type against.
    private static readonly Dictionary<string, string> ExtensionByMediaType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "audio/webm", ".webm" },
            { "audio/ogg", ".ogg" },
            { "audio/mp4", ".mp4" },
            { "audio/x-m4a", ".m4a" },
            { "audio/m4a", ".m4a" },
            { "audio/mpeg", ".mp3" },
            { "audio/mp3", ".mp3" },
            { "audio/wav", ".wav" },
            { "audio/x-wav", ".wav" },
            { "audio/wave", ".wav" }
        };

    /// <summary>
    /// <c>"audio/webm;codecs=opus"</c> becomes <c>"audio/webm"</c>. Null for a blank input.
    /// </summary>
    /// <remarks>
    /// The part carries the bare type because the server ignores codec parameters anyway, and a
    /// parameter is enough to make a strict media-type parser refuse the header.
    /// </remarks>
    public static string? BareMediaType(string? contentType)
    {
        if (contentType is null) return null;

        var separator = contentType.IndexOf(';');
        var bare = (separator >= 0 ? contentType.Substring(0, separator) : contentType).Trim().ToLowerInvariant();
        return bare.Length == 0 ? null : bare;
    }

    /// <summary>
    /// The file extension for a recorded clip, including the dot, or <see cref="string.Empty"/>
    /// when the format is not one this library sends. Accepts a raw or a bare content type.
    /// </summary>
    public static string ExtensionFor(string? contentType)
    {
        var bare = BareMediaType(contentType);
        if (bare is null) return string.Empty;

        // `is not null` rather than a bare ternary: netstandard2.0's Dictionary has no
        // [MaybeNullWhen] on TryGetValue, so flow analysis needs telling on that leg.
        return ExtensionByMediaType.TryGetValue(bare, out var found) && found is not null
            ? found
            : string.Empty;
    }

    /// <summary>
    /// The upload's file name: <c>speech.webm</c>, <c>speech.mp4</c>, ... — or a bare
    /// <c>speech</c> when the format is unknown, which is what the server sees as "let the
    /// provider sniff it".
    /// </summary>
    public static string FileNameFor(string? contentType) => "speech" + ExtensionFor(contentType);

    /// <summary>
    /// Whether this content type is recorded audio the transcription route accepts. The speech
    /// proxy uses it to refuse anything else, so the route cannot become a generic file relay.
    /// </summary>
    public static bool IsSupportedMediaType(string? contentType)
    {
        var bare = BareMediaType(contentType);
        return bare is not null && ExtensionByMediaType.ContainsKey(bare);
    }

    /// <summary>The message for a non-2xx answer that carried no readable failure of its own.</summary>
    public static string FailureMessageForStatus(int statusCode) => $"Transcription failed ({statusCode}).";

    /// <summary>A failed result carrying <paramref name="message"/>. Never null, never empty.</summary>
    public static SpeechTranscriptionResult Failure(string? message)
    {
        return new SpeechTranscriptionResult
        {
            Success = false,
            ErrorMessage = message is { Length: > 0 } ? message : GenericFailureMessage
        };
    }

    /// <summary>A successful result. A clip with no speech in it is a success with empty text.</summary>
    public static SpeechTranscriptionResult Success(string? text)
    {
        return new SpeechTranscriptionResult
        {
            Success = true,
            Text = text ?? string.Empty
        };
    }
}
