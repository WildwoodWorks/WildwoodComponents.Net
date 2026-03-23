using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    public partial class OverridesPanel : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;
        [Parameter] public string? CompanyId { get; set; }
        [Parameter] public string? UserId { get; set; }
        [Parameter] public bool IsCompanyMode { get; set; }
        [Parameter] public EventCallback OnOverrideRemoved { get; set; }

        private List<AppFeatureOverrideModel> _overrides = new();
        private string? _processingFeatureCode;

        private bool UseUserScope => !IsCompanyMode && !string.IsNullOrEmpty(UserId);

        protected override async Task OnComponentInitializedAsync()
        {
            await LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            try
            {
                await SetLoadingAsync(true);
                string? scopeUserId = UseUserScope ? UserId : null;
                _overrides = await AppTierService.GetFeatureOverridesAsync(AppId, scopeUserId);
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading overrides");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        private async Task RemoveOverride(AppFeatureOverrideModel ov)
        {
            if (_processingFeatureCode != null) return;
            _processingFeatureCode = ov.FeatureCode;
            StateHasChanged();

            try
            {
                string? scopeUserId = UseUserScope ? UserId : null;
                bool success = await AppTierService.RemoveFeatureOverrideAsync(AppId, scopeUserId, ov.FeatureCode);

                if (success)
                {
                    _overrides.Remove(ov);
                    if (OnOverrideRemoved.HasDelegate)
                    {
                        await OnOverrideRemoved.InvokeAsync();
                    }
                }
                else
                {
                    await HandleErrorAsync(new Exception("Failed to remove override"), "Removing override");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Removing override");
            }
            finally
            {
                _processingFeatureCode = null;
                StateHasChanged();
            }
        }

        private async Task MakePermanent(AppFeatureOverrideModel ov)
        {
            if (_processingFeatureCode != null) return;
            _processingFeatureCode = ov.FeatureCode;
            StateHasChanged();

            try
            {
                string? scopeUserId = UseUserScope ? UserId : null;

                // Re-set the override with no expiration
                bool success = await AppTierService.SetFeatureOverrideAsync(
                    AppId, scopeUserId, ov.FeatureCode, ov.IsEnabled, ov.Reason, null);

                if (success)
                {
                    ov.ExpiresAt = null;
                    if (OnOverrideRemoved.HasDelegate)
                    {
                        await OnOverrideRemoved.InvokeAsync();
                    }
                }
                else
                {
                    await HandleErrorAsync(new Exception("Failed to update override"), "Making override permanent");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Making override permanent");
            }
            finally
            {
                _processingFeatureCode = null;
                StateHasChanged();
            }
        }

        private bool IsProcessing(string featureCode)
        {
            return _processingFeatureCode == featureCode;
        }

        private string FormatDate(DateTime? date)
        {
            if (!date.HasValue) return "";
            return date.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        }

        private string FormatDate(DateTime date)
        {
            return date.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        }
    }
}
