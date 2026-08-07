using System.Text;
using System.Timers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.Feedback;

/// <summary>
/// A reusable floating feedback widget. Renders a floating button and a slide-out form that
/// lets a user pick a feedback type/title/description, optionally capture a screenshot,
/// attach files, include browser console context, check for duplicates, and submit to the
/// Wildwood feedback API. The Blazor-native equivalent of the vanilla feedback widget.
/// </summary>
public partial class FeedbackWidgetComponent : BaseWildwoodComponent
{
    /// <summary>The app the feedback belongs to (required).</summary>
    [Parameter] public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base URL override. Defaults to the library's configured
    /// <c>WildwoodComponentsOptions.BaseUrl</c> when null.
    /// </summary>
    [Parameter] public string? BaseUrl { get; set; }

    /// <summary>Optional Bearer token for authenticated submitters. When set, the email/name fields are hidden.</summary>
    [Parameter] public string? AuthToken { get; set; }

    /// <summary>Raised after feedback is successfully submitted.</summary>
    [Parameter] public EventCallback OnFeedbackSubmitted { get; set; }

    [Inject] private IFeedbackService? FeedbackService { get; set; }
    [Inject] private WildwoodComponentsOptions? Options { get; set; }

    private const string ModulePath = "./_content/WildwoodComponents.Blazor/js/feedback-component.js";

    private FeedbackWidgetConfig? _config;
    private List<string> _feedbackTypes = new();
    private bool _isAuthenticated;

    private bool _panelOpen;
    private string _feedbackType = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string? _submitterEmail;
    private string? _submitterName;
    private string? _screenshotData;
    private readonly List<FeedbackAttachment> _attachments = new();

    private bool _titleInvalid;
    private bool _descriptionInvalid;
    private string? _formError;

    private bool _isSubmitting;
    private bool _isCapturing;
    private bool _isVoting;

    private FeedbackDuplicateCheckResult? _duplicate;
    private System.Timers.Timer? _duplicateTimer;

    private string? _toastMessage;
    private bool _toastIsError;
    private System.Timers.Timer? _toastTimer;

    private ElementReference _buttonRef;
    private IJSObjectReference? _module;
    private IJSObjectReference? _controller;
    private DotNetObjectReference<FeedbackWidgetComponent>? _selfRef;

    private bool IsBottomLeft =>
        string.Equals(_config?.WidgetPosition, "bottom-left", StringComparison.OrdinalIgnoreCase);

    protected override async Task OnComponentInitializedAsync()
    {
        if (string.IsNullOrEmpty(AppId))
        {
            Logger?.LogWarning("FeedbackWidgetComponent requires an AppId; widget will not render.");
            return;
        }

        _isAuthenticated = !string.IsNullOrEmpty(AuthToken);

        if (FeedbackService != null)
        {
            var resolvedBaseUrl = ResolveBaseUrl();
            FeedbackService.SetApiBaseUrl(resolvedBaseUrl);
            FeedbackService.SetAuthToken(AuthToken);

            _config = await FeedbackService.GetWidgetConfigAsync(AppId);
        }

        if (_config != null)
        {
            // Honor AllowAnonymous: if the viewer isn't authenticated and the app doesn't permit
            // anonymous feedback, render nothing instead of showing a form the API would reject.
            // Mirrors the Razor FeedbackWidgetViewComponent gating so the two stay at parity.
            if (_config.IsEnabled && !_isAuthenticated && !_config.AllowAnonymous)
            {
                _config.IsEnabled = false;
            }

            BuildFeedbackTypes();
        }
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;
        if (Options != null && !string.IsNullOrEmpty(Options.BaseUrl))
            return Options.BaseUrl;
        return string.Empty;
    }

    private void BuildFeedbackTypes()
    {
        _feedbackTypes = new List<string>();
        if (_config?.FeedbackTypes != null && _config.FeedbackTypes.Count > 0)
        {
            foreach (var type in _config.FeedbackTypes)
            {
                if (!string.IsNullOrWhiteSpace(type))
                {
                    _feedbackTypes.Add(type);
                }
            }
        }

        if (_feedbackTypes.Count == 0)
        {
            _feedbackTypes.Add("Bug");
            _feedbackTypes.Add("FeatureRequest");
            _feedbackTypes.Add("Improvement");
            _feedbackTypes.Add("Other");
        }

        _feedbackType = _feedbackTypes[0];
    }

    protected override async Task OnComponentFirstRenderAsync()
    {
        if (_config == null || !_config.IsEnabled)
            return;

        try
        {
            _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _selfRef = DotNetObjectReference.Create(this);

            var options = new { color = _config.WidgetColor, position = _config.WidgetPosition };
            _controller = await _module.InvokeAsync<IJSObjectReference>("initController", _buttonRef, _selfRef, options);
        }
        catch (Exception ex)
        {
            // Drag/console features are progressive enhancements; the widget still works without JS.
            Logger?.LogWarning(ex, "Failed to initialize feedback component JS module");
        }
    }

    /// <summary>
    /// Invoked from JS on a no-move touch tap of the floating button. Touch taps cannot rely on the
    /// native <c>@onclick</c> because <c>touchstart.preventDefault()</c> (needed to drag) suppresses
    /// the synthesized click. Mouse and keyboard activation go through <c>@onclick</c> directly.
    /// </summary>
    [JSInvokable]
    public async Task OnButtonActivated()
    {
        await InvokeAsync(() =>
        {
            TogglePanel();
            StateHasChanged();
        });
    }

    private string GetAccentStyle()
    {
        var color = string.IsNullOrEmpty(_config?.WidgetColor) ? "#4A90D9" : _config!.WidgetColor;
        return $"--ww-feedback-color: {color};";
    }

    private static string FormatTypeLabel(string type)
    {
        if (string.IsNullOrEmpty(type))
            return type;

        // Insert spaces before interior capitals, e.g. "FeatureRequest" -> "Feature Request".
        var sb = new StringBuilder(type.Length + 4);
        for (int i = 0; i < type.Length; i++)
        {
            var c = type[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes}B";
        return $"{(int)Math.Round(bytes / 1024.0)}KB";
    }

    private void TogglePanel()
    {
        if (_panelOpen)
        {
            ClosePanel();
        }
        else
        {
            _panelOpen = true;
        }
    }

    private void ClosePanel()
    {
        _panelOpen = false;
    }

    private async Task OnTitleInput(ChangeEventArgs e)
    {
        _title = e.Value?.ToString() ?? string.Empty;
        _titleInvalid = false;

        if (_config != null && _config.EnableDuplicateDetection)
        {
            ScheduleDuplicateCheck();
        }

        await Task.CompletedTask;
    }

    private void ScheduleDuplicateCheck()
    {
        _duplicateTimer?.Stop();
        _duplicateTimer?.Dispose();

        _duplicateTimer = new System.Timers.Timer(600) { AutoReset = false };
        _duplicateTimer.Elapsed += async (_, _) =>
        {
            try
            {
                await RunDuplicateCheckAsync();
            }
            catch (ObjectDisposedException)
            {
                // Circuit/component was torn down before the debounce fired — safe to ignore.
            }
        };
        _duplicateTimer.Start();
    }

    private async Task RunDuplicateCheckAsync()
    {
        if (FeedbackService == null)
            return;

        var title = _title;
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < 5)
        {
            _duplicate = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        var result = await FeedbackService.CheckDuplicateAsync(title, AppId);
        _duplicate = result;
        await InvokeAsync(StateHasChanged);
    }

    private async Task VoteForDuplicate()
    {
        if (FeedbackService == null || _duplicate == null || string.IsNullOrEmpty(_duplicate.DuplicateId))
            return;

        _isVoting = true;
        StateHasChanged();

        var result = await FeedbackService.VoteAsync(_duplicate.DuplicateId);
        _isVoting = false;

        if (result.Success)
        {
            ShowToast($"Vote recorded! ({result.VoteCount} total)", isError: false);
            ClosePanel();
        }
        else
        {
            ShowToast(result.ErrorMessage ?? "Could not record your vote.", isError: true);
        }

        StateHasChanged();
    }

    private async Task CaptureArea() => await CaptureScreenshotAsync("area");

    private async Task CaptureFullPage() => await CaptureScreenshotAsync("full");

    private async Task CaptureScreenshotAsync(string mode)
    {
        if (_module == null || _config == null)
            return;

        _isCapturing = true;
        // Hide the panel while the user selects/annotates so it does not obscure the page.
        _panelOpen = false;
        StateHasChanged();

        try
        {
            var result = await _module.InvokeAsync<CaptureResult?>(
                "captureScreenshot", mode, _config.ScreenshotQuality, _config.ScreenshotMaxSizeKb, _buttonRef);

            if (!string.IsNullOrEmpty(result?.Data))
            {
                _screenshotData = result.Data;
            }
            else if (result?.Reason is { Length: > 0 } reason)
            {
                Logger?.LogWarning("Screenshot capture failed ({Reason}): {Message}", reason, result.Message);
                var text = CaptureFailureText(reason, result.Message);
                // No text means the user made a choice — declining the share picker — and
                // reporting a decision back as an error would just nag them for making it.
                if (text != null)
                {
                    ShowToast(text, isError: true);
                }
            }
            // Otherwise the user cancelled (Escape, or a selection too small to be a
            // screenshot). Nothing happened, so nothing is said.
        }
        catch (Exception ex)
        {
            // The module itself failed to answer — a disposed circuit, a stale reference.
            Logger?.LogWarning(ex, "Screenshot capture failed");
            ShowToast("Failed to capture screenshot.", isError: true);
        }
        finally
        {
            _isCapturing = false;
            _panelOpen = true;
            StateHasChanged();
        }
    }

    /// <summary>
    /// What to tell the user about each way a capture can fail, or <c>null</c> to say nothing.
    /// The widget used to render one string — "Failed to capture screenshot." — for every
    /// cause, including causes whoever runs the site could have acted on, and including a
    /// deliberate cancel.
    /// </summary>
    private static string? CaptureFailureText(string reason, string? message) => reason switch
    {
        // The user declined the browser's share prompt — or the page is not permitted to ask.
        // The two are indistinguishable (both are NotAllowedError) and by far the common one
        // is a deliberate "no", so this stays silent, exactly like pressing Escape.
        "permission" => null,
        // Naming the likely policy matters: a CSP that blocks the library is fixable by
        // whoever runs the site (map this package's static web assets, or point
        // window.__WW_HTML2CANVAS_SRC__ at a copy they serve) and by nobody else. It is only
        // LIKELY, though — script.onerror fires for an unreachable network just as it does
        // for a refusal — so the copy claims no more than that.
        "library-blocked" => "Couldn't take a screenshot — the screenshot library isn't available. "
            + "It may be blocked by this site's security policy, or unreachable.",
        "library-timeout" => "Timed out loading the screenshot library. Check your connection and try again.",
        // Not produced by this widget today — full-page capture uses whatever frame the user
        // shares, and only the area-cropping path in the JS SDK has to refuse a foreign
        // surface. Carried so the table stays exhaustive over the module's documented reasons
        // and a new one added there is a missing case here, not silent generic copy.
        "wrong-surface" => "Please choose \"This Tab\" when the browser asks what to share.",
        "unsupported" => "This browser can't take screenshots.",
        // 'failed' and anything a future version adds: the thrower knows more than this table.
        _ => string.IsNullOrWhiteSpace(message) ? "Failed to capture screenshot." : message,
    };

    /// <summary>Outcome of a <c>captureScreenshot</c> interop call. See the module's JSDoc.</summary>
    private sealed record CaptureResult(string? Data, string? Reason, string? Message);

    private void RemoveScreenshot()
    {
        _screenshotData = null;
    }

    private async Task OnAttachmentsSelected(InputFileChangeEventArgs e)
    {
        if (_config == null)
            return;

        var allowed = ParseAllowedTypes(_config.AllowedAttachmentTypes);
        var maxBytes = (long)(_config.MaxAttachmentSizeKb > 0 ? _config.MaxAttachmentSizeKb : 2048) * 1024;

        foreach (var file in e.GetMultipleFiles(maximumFileCount: 20))
        {
            var ext = GetExtension(file.Name);
            if (allowed.Count > 0 && !ListContainsIgnoreCase(allowed, ext))
            {
                ShowToast($"File type {ext} is not allowed.", isError: true);
                continue;
            }

            if (file.Size > maxBytes)
            {
                ShowToast($"{file.Name} exceeds the {_config.MaxAttachmentSizeKb}KB limit.", isError: true);
                continue;
            }

            try
            {
                using var stream = file.OpenReadStream(maxAllowedSize: maxBytes);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var base64 = Convert.ToBase64String(ms.ToArray());
                var contentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;

                _attachments.Add(new FeedbackAttachment
                {
                    Name = file.Name,
                    ContentType = contentType,
                    Size = file.Size,
                    Data = $"data:{contentType};base64,{base64}"
                });
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to read attachment {FileName}", file.Name);
                ShowToast($"Could not read {file.Name}.", isError: true);
            }
        }

        StateHasChanged();
    }

    private void RemoveAttachment(int index)
    {
        if (index >= 0 && index < _attachments.Count)
        {
            _attachments.RemoveAt(index);
        }
    }

    private async Task SubmitFeedback()
    {
        if (FeedbackService == null || _config == null)
            return;

        _formError = null;
        _titleInvalid = false;
        _descriptionInvalid = false;

        if (string.IsNullOrWhiteSpace(_title))
        {
            _titleInvalid = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(_description))
        {
            _descriptionInvalid = true;
            return;
        }

        if (_config.RequireScreenshot && string.IsNullOrEmpty(_screenshotData))
        {
            _formError = "A screenshot is required.";
            return;
        }

        _isSubmitting = true;
        StateHasChanged();

        try
        {
            var request = new FeedbackSubmissionRequest
            {
                AppId = AppId,
                Title = _title.Trim(),
                Description = _description.Trim(),
                FeedbackType = _feedbackType,
                PageUrl = await GetPageUrlAsync(),
                ScreenshotData = _screenshotData,
                Attachments = BuildAttachmentsJson(),
                BrowserContext = await GetBrowserContextAsync(),
                SubmitterEmail = string.IsNullOrWhiteSpace(_submitterEmail) ? null : _submitterEmail!.Trim(),
                SubmitterName = string.IsNullOrWhiteSpace(_submitterName) ? null : _submitterName!.Trim()
            };

            var result = await FeedbackService.SubmitFeedbackAsync(request);

            if (result.Success)
            {
                ShowToast("Thank you! Your feedback has been submitted.", isError: false);
                ResetForm();
                ClosePanel();
                if (OnFeedbackSubmitted.HasDelegate)
                {
                    await OnFeedbackSubmitted.InvokeAsync();
                }
            }
            else
            {
                _formError = result.ErrorMessage ?? "Failed to submit feedback.";
                if (result.RateLimited)
                {
                    ShowToast(_formError, isError: true);
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error submitting feedback for app {AppId}", AppId);
            _formError = "An unexpected error occurred. Please try again.";
        }
        finally
        {
            _isSubmitting = false;
            StateHasChanged();
        }
    }

    private string? BuildAttachmentsJson()
    {
        if (_attachments.Count == 0)
            return null;

        return System.Text.Json.JsonSerializer.Serialize(_attachments);
    }

    private async Task<string?> GetPageUrlAsync()
    {
        if (_module == null)
            return null;
        try
        {
            return await _module.InvokeAsync<string?>("getPageUrl");
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "Failed to read page URL");
            return null;
        }
    }

    private async Task<string?> GetBrowserContextAsync()
    {
        if (_module == null)
            return null;
        try
        {
            return await _module.InvokeAsync<string?>("getBrowserContext");
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "Failed to collect browser context");
            return null;
        }
    }

    private void ResetForm()
    {
        _title = string.Empty;
        _description = string.Empty;
        _submitterEmail = null;
        _submitterName = null;
        _screenshotData = null;
        _attachments.Clear();
        _duplicate = null;
        _titleInvalid = false;
        _descriptionInvalid = false;
        _formError = null;
        if (_feedbackTypes.Count > 0)
        {
            _feedbackType = _feedbackTypes[0];
        }
    }

    private void ShowToast(string message, bool isError)
    {
        _toastMessage = message;
        _toastIsError = isError;

        _toastTimer?.Stop();
        _toastTimer?.Dispose();
        _toastTimer = new System.Timers.Timer(3000) { AutoReset = false };
        _toastTimer.Elapsed += async (_, _) =>
        {
            try
            {
                _toastMessage = null;
                await InvokeAsync(StateHasChanged);
            }
            catch (ObjectDisposedException)
            {
                // Circuit/component was torn down before the toast expired — safe to ignore.
            }
        };
        _toastTimer.Start();
    }

    // ----- small allocation-free helpers (no LINQ per library convention) -----

    private static List<string> ParseAllowedTypes(string? csv)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(csv))
            return result;

        var parts = csv.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                result.Add(trimmed);
            }
        }
        return result;
    }

    private static string GetExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0)
            return string.Empty;
        return fileName.Substring(dot);
    }

    private static bool ListContainsIgnoreCase(List<string> list, string value)
    {
        foreach (var item in list)
        {
            if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _duplicateTimer?.Stop();
            _duplicateTimer?.Dispose();
            _toastTimer?.Stop();
            _toastTimer?.Dispose();

            // Best-effort async JS cleanup; fire-and-forget since Dispose is synchronous.
            if (_controller != null && _module != null)
            {
                _ = DisposeJsAsync();
            }

            _selfRef?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task DisposeJsAsync()
    {
        try
        {
            if (_module != null && _controller != null)
            {
                await _module.InvokeVoidAsync("disposeController", _controller);
            }
            if (_controller != null)
            {
                await _controller.DisposeAsync();
            }
            if (_module != null)
            {
                await _module.DisposeAsync();
            }
        }
        catch
        {
            // Circuit may already be gone during disposal; ignore.
        }
    }
}
