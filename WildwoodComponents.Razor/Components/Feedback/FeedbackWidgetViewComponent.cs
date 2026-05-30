using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.Feedback;

/// <summary>
/// ViewComponent that renders a floating feedback button and a slide-out feedback form
/// (type/title/description), with optional screenshot capture, attachments, browser
/// console-context capture, debounced duplicate detection + upvote, and submission to the
/// Wildwood feedback API. The server renders the shell and resolves the widget configuration;
/// client-side JavaScript handles all interactivity and AJAX (via a thin server-side proxy so
/// the Bearer token stays server-side).
///
/// Razor Pages equivalent of WildwoodComponents.Blazor FeedbackWidgetComponent.
/// Usage: <c>&lt;vc:feedback-widget app-id="my-app" /&gt;</c>.
/// </summary>
public class FeedbackWidgetViewComponent : ViewComponent
{
    private readonly IWildwoodFeedbackService _feedbackService;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<FeedbackWidgetViewComponent> _logger;

    public FeedbackWidgetViewComponent(
        IWildwoodFeedbackService feedbackService,
        IWildwoodSessionManager sessionManager,
        ILogger<FeedbackWidgetViewComponent> logger)
    {
        _feedbackService = feedbackService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Renders the feedback widget.
    /// </summary>
    /// <param name="appId">Required. The application ID the feedback belongs to.</param>
    /// <param name="proxyBaseUrl">Base URL for the feedback proxy endpoints (default: /api/wildwood-feedback)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-feedback")
    {
        var model = new FeedbackWidgetViewModel
        {
            AppId = appId ?? string.Empty,
            ProxyBaseUrl = (proxyBaseUrl ?? "/api/wildwood-feedback").TrimEnd('/'),
            IsAuthenticated = _sessionManager.IsAuthenticated
        };

        if (string.IsNullOrEmpty(appId))
        {
            _logger.LogWarning("FeedbackWidgetViewComponent requires an appId; widget will not render.");
            model.Config = new FeedbackWidgetConfig { IsEnabled = false };
            return View(model);
        }

        FeedbackWidgetConfig? config = null;
        try
        {
            config = await _feedbackService.GetWidgetConfigAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load feedback widget config for app {AppId}", appId);
        }

        // When config cannot be loaded, render nothing rather than guessing — matches the
        // Blazor component which only renders when a config was returned and IsEnabled.
        model.Config = config ?? new FeedbackWidgetConfig { IsEnabled = false };
        model.FeedbackTypes = BuildFeedbackTypes(model.Config);

        return View(model);
    }

    /// <summary>
    /// Builds the type dropdown list from config, falling back to the canonical defaults
    /// when the configured list is empty. No LINQ (library convention shared with Blazor).
    /// </summary>
    private static List<string> BuildFeedbackTypes(FeedbackWidgetConfig config)
    {
        var types = new List<string>();
        if (config.FeedbackTypes != null)
        {
            foreach (var type in config.FeedbackTypes)
            {
                if (!string.IsNullOrWhiteSpace(type))
                {
                    types.Add(type);
                }
            }
        }

        if (types.Count == 0)
        {
            types.Add("Bug");
            types.Add("FeatureRequest");
            types.Add("Improvement");
            types.Add("Other");
        }

        return types;
    }

    /// <summary>
    /// Inserts spaces before interior capitals, e.g. "FeatureRequest" -> "Feature Request".
    /// Exposed for the view to render friendly type labels.
    /// </summary>
    public static string FormatTypeLabel(string type)
    {
        if (string.IsNullOrEmpty(type))
            return type;

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
}
