using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The pure half of recorded voice input, shared by the Blazor client, the Razor client and the
/// Razor speech proxy — so the format a browser records, the name it is uploaded under and the
/// list the proxy validates against cannot drift apart.
/// </summary>
public class SpeechAudioFormatsTests
{
    [Theory]
    [InlineData("audio/webm;codecs=opus", "audio/webm")]
    [InlineData("audio/ogg;codecs=opus", "audio/ogg")]
    [InlineData("audio/mp4;codecs=mp4a.40.2", "audio/mp4")]
    [InlineData("  AUDIO/WEBM  ", "audio/webm")]
    [InlineData("audio/wav", "audio/wav")]
    public void BareMediaType_drops_parameters_and_normalises(string input, string expected)
    {
        Assert.Equal(expected, SpeechAudioFormats.BareMediaType(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";codecs=opus")]
    public void BareMediaType_is_null_when_there_is_no_type(string? input)
    {
        Assert.Null(SpeechAudioFormats.BareMediaType(input));
    }

    [Theory]
    [InlineData("audio/webm;codecs=opus", ".webm")]
    [InlineData("audio/ogg", ".ogg")]
    [InlineData("audio/mp4", ".mp4")]
    [InlineData("audio/x-m4a", ".m4a")]
    [InlineData("audio/m4a", ".m4a")]
    [InlineData("audio/mpeg", ".mp3")]
    [InlineData("audio/mp3", ".mp3")]
    [InlineData("audio/wav", ".wav")]
    [InlineData("audio/x-wav", ".wav")]
    [InlineData("audio/wave", ".wav")]
    public void ExtensionFor_covers_every_recordable_container(string contentType, string expected)
    {
        Assert.Equal(expected, SpeechAudioFormats.ExtensionFor(contentType));
    }

    [Theory]
    [InlineData("audio/aac")]
    [InlineData("application/pdf")]
    [InlineData("")]
    [InlineData(null)]
    public void ExtensionFor_is_empty_for_anything_else(string? contentType)
    {
        Assert.Equal(string.Empty, SpeechAudioFormats.ExtensionFor(contentType));
    }

    [Fact]
    public void FileNameFor_names_the_container_so_the_provider_can_read_it()
    {
        Assert.Equal("speech.webm", SpeechAudioFormats.FileNameFor("audio/webm;codecs=opus"));
        Assert.Equal("speech.mp4", SpeechAudioFormats.FileNameFor("audio/mp4"));
        // An unknown container still uploads — the provider is left to sniff it.
        Assert.Equal("speech", SpeechAudioFormats.FileNameFor("audio/aac"));
    }

    [Theory]
    [InlineData("audio/webm;codecs=opus", true)]
    [InlineData("AUDIO/WAV", true)]
    [InlineData("audio/aac", false)]
    [InlineData("image/png", false)]
    [InlineData("application/octet-stream", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedMediaType_is_the_proxy_allow_list(string? contentType, bool expected)
    {
        Assert.Equal(expected, SpeechAudioFormats.IsSupportedMediaType(contentType));
    }

    [Fact]
    public void Failure_never_produces_an_empty_message()
    {
        Assert.Equal("Nope.", SpeechAudioFormats.Failure("Nope.").ErrorMessage);
        Assert.Equal(SpeechAudioFormats.GenericFailureMessage, SpeechAudioFormats.Failure(null).ErrorMessage);
        Assert.Equal(SpeechAudioFormats.GenericFailureMessage, SpeechAudioFormats.Failure(string.Empty).ErrorMessage);
        Assert.False(SpeechAudioFormats.Failure("x").Success);
    }

    [Fact]
    public void Success_treats_a_silent_clip_as_a_success_with_no_text()
    {
        var quiet = SpeechAudioFormats.Success(null);

        Assert.True(quiet.Success);
        Assert.Equal(string.Empty, quiet.Text);
        Assert.Null(quiet.ErrorMessage);
    }

    [Fact]
    public void The_caps_are_the_ones_the_browsers_enforce()
    {
        Assert.Equal(60, SpeechAudioFormats.MaxRecordingSeconds);
        Assert.Equal(25L * 1024 * 1024, SpeechAudioFormats.MaxAudioBytes);
        Assert.Contains("25 MB", SpeechAudioFormats.TooLargeMessage);
    }

    [Fact]
    public void FailureMessageForStatus_reads_the_same_in_both_stacks()
    {
        Assert.Equal("Transcription failed (502).", SpeechAudioFormats.FailureMessageForStatus(502));
    }
}
