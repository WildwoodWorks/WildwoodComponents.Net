using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    public partial class FeaturesPanel : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;
        [Parameter] public string? CompanyId { get; set; }
        [Parameter] public string? UserId { get; set; }
        [Parameter] public bool IsCompanyMode { get; set; }
        [Parameter] public bool IsAdmin { get; set; }
        [Parameter] public EventCallback OnOverrideToggled { get; set; }

        private List<AppFeatureDefinitionModel> _features = new();
        private List<string> _categories = new();
        private bool _isProcessing;
        private string? _processingFeatureCode;

        // Override tracking
        private Dictionary<string, AppFeatureOverrideModel> _overrideMap = new();

        // Confirmation state
        private string? _confirmingFeatureCode;
        private string _confirmExpiration = "";
        private string _confirmReason = "";

        private bool UseCompanyScope => IsCompanyMode && !string.IsNullOrEmpty(CompanyId);
        private bool UseUserScope => !IsCompanyMode && !string.IsNullOrEmpty(UserId);

        protected override async Task OnComponentInitializedAsync()
        {
            await LoadFeaturesAsync();
        }

        public async Task LoadFeaturesAsync()
        {
            try
            {
                await SetLoadingAsync(true);

                // Load feature definitions
                var definitions = await AppTierService.GetFeatureDefinitionsAsync(AppId);

                // Load company or user feature access status
                Dictionary<string, bool> featureStatus;
                if (UseCompanyScope)
                {
                    featureStatus = await AppTierService.GetCompanyFeaturesAsync(AppId, CompanyId!);
                }
                else if (UseUserScope)
                {
                    featureStatus = await AppTierService.GetUserFeaturesAdminAsync(AppId, UserId!);
                }
                else
                {
                    featureStatus = await AppTierService.GetUserFeaturesAsync(AppId);
                }

                // Load overrides if admin
                if (IsAdmin)
                {
                    await LoadOverridesAsync();
                }

                // Merge definitions with status
                _features = new List<AppFeatureDefinitionModel>();
                foreach (var def in definitions)
                {
                    bool isEnabled = false;
                    if (featureStatus.ContainsKey(def.FeatureCode))
                    {
                        isEnabled = featureStatus[def.FeatureCode];
                    }

                    _features.Add(new AppFeatureDefinitionModel
                    {
                        FeatureCode = def.FeatureCode,
                        DisplayName = def.DisplayName,
                        Description = def.Description,
                        Category = def.Category,
                        IconClass = def.IconClass,
                        DisplayOrder = def.DisplayOrder,
                        IsEnabled = isEnabled
                    });
                }

                // Sort by category then display order
                _features.Sort((a, b) =>
                {
                    int catCompare = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
                    if (catCompare != 0) return catCompare;
                    return a.DisplayOrder.CompareTo(b.DisplayOrder);
                });

                // Extract unique categories
                _categories = new List<string>();
                foreach (var f in _features)
                {
                    bool found = false;
                    foreach (var c in _categories)
                    {
                        if (string.Equals(c, f.Category, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found && !string.IsNullOrEmpty(f.Category))
                    {
                        _categories.Add(f.Category);
                    }
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading features");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        private async Task LoadOverridesAsync()
        {
            try
            {
                string? scopeUserId = UseUserScope ? UserId : null;
                var overrides = await AppTierService.GetFeatureOverridesAsync(AppId, scopeUserId);
                _overrideMap = new Dictionary<string, AppFeatureOverrideModel>();
                foreach (var ov in overrides)
                {
                    _overrideMap[ov.FeatureCode] = ov;
                }
            }
            catch (Exception)
            {
                // Non-critical — override indicators are informational only.
                // Feature toggle still works without override data loaded.
                _overrideMap = new Dictionary<string, AppFeatureOverrideModel>();
            }
        }

        private void RequestToggle(AppFeatureDefinitionModel feature)
        {
            if (_isProcessing) return;
            _confirmingFeatureCode = feature.FeatureCode;
            _confirmExpiration = "";
            _confirmReason = "";
            StateHasChanged();
        }

        private void CancelToggle()
        {
            _confirmingFeatureCode = null;
            StateHasChanged();
        }

        private async Task ConfirmToggle(AppFeatureDefinitionModel feature)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            _processingFeatureCode = feature.FeatureCode;
            _confirmingFeatureCode = null;
            StateHasChanged();

            try
            {
                bool newState = !feature.IsEnabled;
                string? scopeUserId = UseUserScope ? UserId : null;

                DateTime? expiresAt = ParseExpiration(_confirmExpiration);
                string? reason = string.IsNullOrWhiteSpace(_confirmReason) ? null : _confirmReason.Trim();

                bool success = await AppTierService.SetFeatureOverrideAsync(
                    AppId, scopeUserId, feature.FeatureCode, newState, reason, expiresAt);

                if (success)
                {
                    feature.IsEnabled = newState;
                    // Update override map
                    if (!_overrideMap.ContainsKey(feature.FeatureCode))
                    {
                        _overrideMap[feature.FeatureCode] = new AppFeatureOverrideModel();
                    }
                    _overrideMap[feature.FeatureCode].FeatureCode = feature.FeatureCode;
                    _overrideMap[feature.FeatureCode].IsEnabled = newState;
                    if (expiresAt.HasValue)
                    {
                        _overrideMap[feature.FeatureCode].ExpiresAt = expiresAt.Value;
                    }

                    // Notify parent so OverridesPanel can refresh
                    if (OnOverrideToggled.HasDelegate)
                    {
                        await OnOverrideToggled.InvokeAsync();
                    }
                }
                else
                {
                    await HandleErrorAsync(new Exception("Failed to toggle feature override"), "Toggling feature");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Toggling feature override");
            }
            finally
            {
                _isProcessing = false;
                _processingFeatureCode = null;
                StateHasChanged();
            }
        }

        private DateTime? ParseExpiration(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var now = DateTime.UtcNow;
            if (value == "1h") return now.AddHours(1);
            if (value == "1d") return now.AddDays(1);
            if (value == "7d") return now.AddDays(7);
            if (value == "30d") return now.AddDays(30);
            return null;
        }

        private bool IsFeatureProcessing(string featureCode)
        {
            return _isProcessing && _processingFeatureCode == featureCode;
        }

        private bool IsConfirming(string featureCode)
        {
            return _confirmingFeatureCode == featureCode;
        }

        private bool HasOverride(string featureCode)
        {
            return _overrideMap.ContainsKey(featureCode);
        }

        private string GetOverrideTooltip(string featureCode)
        {
            if (!_overrideMap.ContainsKey(featureCode)) return "";
            var ov = _overrideMap[featureCode];
            var parts = new List<string>();
            parts.Add("Admin override");
            if (!string.IsNullOrEmpty(ov.Reason))
            {
                parts.Add(ov.Reason);
            }
            if (ov.ExpiresAt.HasValue)
            {
                parts.Add("Expires: " + ov.ExpiresAt.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt"));
            }
            return string.Join(" | ", parts);
        }

        private List<AppFeatureDefinitionModel> GetFeaturesForCategory(string category)
        {
            var result = new List<AppFeatureDefinitionModel>();
            foreach (var f in _features)
            {
                if (string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(f);
                }
            }
            return result;
        }

        private int GetEnabledCount()
        {
            int count = 0;
            foreach (var f in _features)
            {
                if (f.IsEnabled) count++;
            }
            return count;
        }
    }
}
