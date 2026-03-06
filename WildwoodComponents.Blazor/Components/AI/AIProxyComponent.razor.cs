using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Blazor.Components.Base;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// General-purpose AI proxy component for single request/response interactions.
/// Unlike AIChatComponent, this does NOT provide a conversational chat UI.
/// Use for form generation, document analysis, suggestions, and any one-shot AI task.
/// </summary>
public partial class AIProxyComponent : BaseWildwoodComponent
{
    [Inject] private IAIService AIService { get; set; } = default!;

    #region Parameters

    /// <summary>
    /// The AI configuration ID to use for requests.
    /// </summary>
    [Parameter] public string? ConfigurationId { get; set; }

    /// <summary>
    /// The AI configuration name to resolve at runtime.
    /// Used when ConfigurationId is not known ahead of time.
    /// </summary>
    [Parameter] public string? ConfigurationName { get; set; }

    /// <summary>
    /// Authentication token for WildwoodAPI calls.
    /// </summary>
    [Parameter] public string? AuthToken { get; set; }

    /// <summary>
    /// Placeholder text for the prompt input area.
    /// </summary>
    [Parameter] public string Placeholder { get; set; } = "Describe what you need...";

    /// <summary>
    /// Title displayed above the component.
    /// </summary>
    [Parameter] public string Title { get; set; } = "AI Assistant";

    /// <summary>
    /// Label for the submit button.
    /// </summary>
    [Parameter] public string SubmitLabel { get; set; } = "Generate";

    /// <summary>
    /// Whether to allow file uploads alongside the prompt.
    /// </summary>
    [Parameter] public bool AllowFileUpload { get; set; }

    /// <summary>
    /// Maximum file size in bytes (default 10MB).
    /// </summary>
    [Parameter] public long MaxFileSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Callback invoked when the AI returns a response.
    /// </summary>
    [Parameter] public EventCallback<AIChatResponse> OnResponse { get; set; }

    /// <summary>
    /// Callback invoked when an error occurs.
    /// </summary>
    [Parameter] public EventCallback<string> OnError { get; set; }

    #endregion

    #region Private Fields

    private string _promptText = string.Empty;
    private string _responseText = string.Empty;
    private bool _isProcessing;
    private bool _hasResponse;
    private string? _errorMessage;
    private string? _resolvedConfigurationId;
    private IBrowserFile? _selectedFile;
    private string? _selectedFileName;

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        if (!string.IsNullOrEmpty(AuthToken))
        {
            AIService.SetAuthToken(AuthToken);
        }

        await ResolveConfigurationAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (!string.IsNullOrEmpty(AuthToken))
        {
            AIService.SetAuthToken(AuthToken);
        }
    }

    #endregion

    #region Configuration Resolution

    private async Task ResolveConfigurationAsync()
    {
        if (!string.IsNullOrEmpty(ConfigurationId))
        {
            _resolvedConfigurationId = ConfigurationId;
            return;
        }

        if (!string.IsNullOrEmpty(ConfigurationName))
        {
            try
            {
                var configs = await AIService.GetConfigurationsAsync();
                var match = configs.FirstOrDefault(c =>
                    c.Name.Equals(ConfigurationName, StringComparison.OrdinalIgnoreCase) && c.IsActive);

                if (match != null)
                {
                    _resolvedConfigurationId = match.Id;
                }
                else
                {
                    _errorMessage = $"AI configuration '{ConfigurationName}' not found or inactive.";
                    Logger?.LogWarning("AI configuration '{ConfigurationName}' not found", ConfigurationName);
                }
            }
            catch (Exception ex)
            {
                _errorMessage = "Failed to load AI configurations.";
                Logger?.LogError(ex, "Error resolving AI configuration by name '{ConfigurationName}'", ConfigurationName);
            }
        }
    }

    #endregion

    #region Request Handling

    private async Task SubmitRequestAsync()
    {
        if (string.IsNullOrWhiteSpace(_promptText) || _isProcessing)
            return;

        if (string.IsNullOrEmpty(_resolvedConfigurationId))
        {
            _errorMessage = "No AI configuration available.";
            return;
        }

        _isProcessing = true;
        _errorMessage = null;
        _responseText = string.Empty;
        _hasResponse = false;
        StateHasChanged();

        try
        {
            var request = new AIChatRequest
            {
                ConfigurationId = _resolvedConfigurationId,
                Message = _promptText,
                SaveToSession = false
            };

            var response = await AIService.SendMessageAsync(request);

            if (response.IsError)
            {
                _errorMessage = response.ErrorMessage ?? "An error occurred processing your request.";
                if (OnError.HasDelegate)
                    await OnError.InvokeAsync(_errorMessage);
            }
            else
            {
                _responseText = response.Response;
                _hasResponse = true;

                if (OnResponse.HasDelegate)
                    await OnResponse.InvokeAsync(response);
            }
        }
        catch (Exception ex)
        {
            _errorMessage = "Failed to process request. Please try again.";
            Logger?.LogError(ex, "Error sending AI proxy request");

            if (OnError.HasDelegate)
                await OnError.InvokeAsync(_errorMessage);
        }
        finally
        {
            _isProcessing = false;
            StateHasChanged();
        }
    }

    #endregion

    #region File Handling

    private void OnFileSelected(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file.Size > MaxFileSize)
        {
            _errorMessage = $"File exceeds maximum size of {MaxFileSize / (1024 * 1024)}MB.";
            _selectedFile = null;
            _selectedFileName = null;
            return;
        }

        _selectedFile = file;
        _selectedFileName = file.Name;
        _errorMessage = null;
        StateHasChanged();
    }

    private void ClearFile()
    {
        _selectedFile = null;
        _selectedFileName = null;
        StateHasChanged();
    }

    #endregion

    #region UI Helpers

    private void ClearResponse()
    {
        _responseText = string.Empty;
        _hasResponse = false;
        _errorMessage = null;
        StateHasChanged();
    }

    private async Task CopyResponseAsync()
    {
        if (!string.IsNullOrEmpty(_responseText))
        {
            try
            {
                await InvokeJSVoidAsync("navigator.clipboard.writeText", _responseText);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to copy to clipboard");
            }
        }
    }

    #endregion
}
