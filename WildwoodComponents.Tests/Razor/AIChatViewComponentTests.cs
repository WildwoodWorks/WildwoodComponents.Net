using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Components.AIChat;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The view model AIChatViewComponent hands its view, and the data attributes the view turns it
/// into. The .cshtml is not rendered (see <see cref="NoViewEngine"/>), so the markup half is
/// pinned by reading the file — enough to catch a speech attribute that stops being conditional
/// on <c>EnableSTT</c>, which is what would let voice input run while it is switched off.
/// </summary>
public class AIChatViewComponentTests
{
    private sealed class NoViewEngine : ICompositeViewEngine
    {
        public IReadOnlyList<IViewEngine> ViewEngines => Array.Empty<IViewEngine>();

        public ViewEngineResult FindView(ActionContext context, string viewName, bool isMainPage) =>
            ViewEngineResult.NotFound(viewName, Array.Empty<string>());

        public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage) =>
            ViewEngineResult.NotFound(viewPath, Array.Empty<string>());
    }

    /// <summary>Answers the two loads the component makes on invoke, and nothing else.</summary>
    private sealed class StubAIChatService : IWildwoodAIChatService
    {
        public Task<List<AIConfiguration>> GetConfigurationsAsync(string? configurationType = null)
            => Task.FromResult(new List<AIConfiguration>());

        public Task<List<AISessionSummary>> GetSessionsAsync(string? configurationId = null)
            => Task.FromResult(new List<AISessionSummary>());

        public Task<AIConfiguration?> GetConfigurationAsync(string configurationId) => throw new NotSupportedException();
        public Task<AIChatResponse> SendMessageAsync(AIChatRequest request) => throw new NotSupportedException();
        public Task<AIChatResponse> SendMessageWithFileAsync(AIChatRequest request, byte[] fileBytes, string fileName) => throw new NotSupportedException();
        public Task<AISession?> CreateSessionAsync(string configurationId, string? sessionName = null) => throw new NotSupportedException();
        public Task<AISession?> GetSessionAsync(string sessionId) => throw new NotSupportedException();
        public Task<bool> EndSessionAsync(string sessionId) => throw new NotSupportedException();
        public Task<bool> DeleteSessionAsync(string sessionId) => throw new NotSupportedException();
        public Task<bool> RenameSessionAsync(string sessionId, string newName) => throw new NotSupportedException();
        public Task<List<TTSVoice>> GetTTSVoicesAsync(string? configurationId = null) => throw new NotSupportedException();
        public Task<(string AudioBase64, string ContentType)?> SynthesizeSpeechAsync(string text, string voice, double speed = 1.0, string? configurationId = null) => throw new NotSupportedException();
        public Task<SpeechTranscriptionResult> TranscribeAudioAsync(byte[] audio, string contentType, string? configurationId = null, string? language = null) => throw new NotSupportedException();
    }

    private static AIChatViewComponent Create()
    {
        return new AIChatViewComponent(new StubAIChatService(), NullLogger<AIChatViewComponent>.Instance)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext
                {
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                }
            },
            ViewEngine = new NoViewEngine()
        };
    }

    private static async Task<AIChatViewModel> ModelOf(Task<IViewComponentResult> invocation)
    {
        var view = Assert.IsType<ViewViewComponentResult>(await invocation);
        return Assert.IsType<AIChatViewModel>(view.ViewData!.Model);
    }

    private static string ViewSource()
    {
        return File.ReadAllText(Path.Combine(
            NodeSelfTest.RepoRoot(), "WildwoodComponents.Razor", "Views", "Shared", "Components",
            "AIChat", "Default.cshtml"));
    }

    [Fact]
    public async Task The_speech_defaults_are_the_shipped_proxy_and_the_shared_caps()
    {
        var model = await ModelOf(Create().InvokeAsync());

        Assert.True(model.EnableSTT);
        Assert.Equal("/api/wildwood-stt/transcribe", model.SpeechProxyUrl);
        Assert.Null(model.SpeechLanguage);
        Assert.Equal(SpeechAudioFormats.MaxRecordingSeconds, model.MaxRecordingSeconds);
        Assert.Equal(SpeechAudioFormats.MaxAudioBytes, model.MaxRecordingBytes);
    }

    [Fact]
    public async Task A_host_can_move_the_proxy_and_set_the_language()
    {
        var model = await ModelOf(Create().InvokeAsync(
            speechProxyUrl: "/internal/stt/transcribe/", speechLanguage: " fr-CA "));

        // The trailing slash would make the posted URL ".../transcribe/", which routes nowhere.
        Assert.Equal("/internal/stt/transcribe", model.SpeechProxyUrl);
        Assert.Equal("fr-CA", model.SpeechLanguage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_blank_proxy_url_falls_back_to_the_shipped_route(string? supplied)
    {
        var model = await ModelOf(Create().InvokeAsync(speechProxyUrl: supplied!));

        Assert.Equal("/api/wildwood-stt/transcribe", model.SpeechProxyUrl);
    }

    [Fact]
    public async Task Speech_to_text_can_be_switched_off()
    {
        var model = await ModelOf(Create().InvokeAsync(enableSTT: false));

        Assert.False(model.EnableSTT);
    }

    /// <summary>
    /// Razor omits an attribute whose value expression is null, so every speech attribute is
    /// written as a <c>Model.EnableSTT ? ... : null</c> ternary. With voice input off the root
    /// therefore carries no <c>data-stt-url</c>, and the script has nothing to post to.
    /// </summary>
    [Theory]
    [InlineData("data-stt-url")]
    [InlineData("data-stt-language")]
    [InlineData("data-stt-max-seconds")]
    [InlineData("data-stt-max-bytes")]
    public void Every_speech_data_attribute_is_conditional_on_EnableSTT(string attribute)
    {
        var source = ViewSource();

        Assert.Contains(attribute + "=\"@(Model.EnableSTT ? ", source);
    }

    [Fact]
    public void The_mic_button_and_its_states_are_rendered_only_when_speech_is_on()
    {
        var source = ViewSource();

        // The button, its three states, the status line and the error region.
        Assert.Contains("ww-stt-btn", source);
        Assert.Contains("ww-stt-idle", source);
        Assert.Contains("ww-stt-recording", source);
        Assert.Contains("ww-stt-busy", source);
        Assert.Contains("ww-stt-status", source);
        Assert.Contains("ww-stt-error", source);

        // The button lives inside @if (Model.EnableSTT) { ... } and starts hidden: the script
        // shows it only once it knows this browser has a mechanism at all.
        var buttonIndex = source.IndexOf("ww-stt-btn", StringComparison.Ordinal);
        var gateIndex = source.LastIndexOf("@if (Model.EnableSTT)", buttonIndex, StringComparison.Ordinal);
        Assert.True(gateIndex >= 0, "The mic button must sit inside an @if (Model.EnableSTT) block.");
        Assert.Contains("style=\"display: none;\"",
            source.Substring(buttonIndex, Math.Min(400, source.Length - buttonIndex)));
    }

    /// <summary>The error region is written with textContent, never innerHTML — see ai-chat.js.</summary>
    [Fact]
    public void The_error_region_is_an_empty_element_the_script_fills()
    {
        Assert.Contains("class=\"ww-stt-error ww-alert ww-alert-danger mx-3 mt-2\" role=\"status\" style=\"display: none;\"></div>",
            ViewSource());
    }
}
