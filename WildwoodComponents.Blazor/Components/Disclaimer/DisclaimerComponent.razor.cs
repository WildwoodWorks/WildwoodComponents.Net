using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Blazor.Components.Base;

namespace WildwoodComponents.Blazor.Components.Disclaimer;

public partial class DisclaimerComponent : BaseWildwoodComponent
{
    [Parameter] public string AppId { get; set; } = string.Empty;
    [Parameter] public string? UserId { get; set; }
    [Parameter] public string Mode { get; set; } = "login"; // "login" or "registration"
    [Parameter] public List<PendingDisclaimerModel>? PendingDisclaimers { get; set; }
    [Parameter] public bool ShowCancelButton { get; set; } = true;

    [Parameter] public EventCallback<List<DisclaimerAcceptanceResult>> OnDisclaimersAccepted { get; set; }
    [Parameter] public EventCallback OnDisclaimersCancelled { get; set; }
    [Parameter] public EventCallback<string> OnError { get; set; }

    [Inject] private IDisclaimerService? DisclaimerService { get; set; }

    private List<PendingDisclaimerModel> _disclaimers = new();
    private bool _isLoading = true;
    private bool _isSubmitting = false;
    private string? _errorMessage;

    private bool CanSubmit => _disclaimers.All(d => d.IsAccepted || !d.IsRequired);

    protected override async Task OnInitializedAsync()
    {
        await LoadDisclaimers();
    }

    private async Task LoadDisclaimers()
    {
        _isLoading = true;
        _errorMessage = null;

        try
        {
            if (PendingDisclaimers != null && PendingDisclaimers.Any())
            {
                _disclaimers = PendingDisclaimers;
            }
            else if (DisclaimerService != null)
            {
                var result = await DisclaimerService.GetPendingDisclaimersAsync(AppId, UserId, Mode);
                if (result.ErrorMessage != null)
                {
                    _errorMessage = result.ErrorMessage;
                    await OnError.InvokeAsync(_errorMessage);
                }
                else
                {
                    _disclaimers = result.Disclaimers;
                }
            }
        }
        catch (Exception ex)
        {
            _errorMessage = "An unexpected error occurred while loading disclaimers.";
            Logger?.LogError(ex, "Unexpected error loading disclaimers for app {AppId}", AppId);
            await OnError.InvokeAsync(_errorMessage);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnDisclaimerChecked(PendingDisclaimerModel disclaimer, bool isChecked)
    {
        disclaimer.IsAccepted = isChecked;
        _errorMessage = null;
        StateHasChanged();
    }

    private async Task HandleAcceptAll()
    {
        if (!CanSubmit) return;

        _isSubmitting = true;
        _errorMessage = null;

        try
        {
            var acceptances = _disclaimers
                .Where(d => d.IsAccepted)
                .Select(d => new DisclaimerAcceptanceResult
                {
                    CompanyDisclaimerId = d.DisclaimerId,
                    CompanyDisclaimerVersionId = d.VersionId
                })
                .ToList();

            await OnDisclaimersAccepted.InvokeAsync(acceptances);
        }
        catch (Exception ex)
        {
            _errorMessage = "Failed to submit disclaimer acceptances. Please try again.";
            Logger?.LogError(ex, "Error accepting disclaimers for app {AppId}", AppId);
            await OnError.InvokeAsync(_errorMessage);
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    private async Task HandleCancel()
    {
        await OnDisclaimersCancelled.InvokeAsync();
    }
}
