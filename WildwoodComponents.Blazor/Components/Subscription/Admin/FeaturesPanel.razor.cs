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

        private List<AppFeatureDefinitionModel> _features = new();
        private List<string> _categories = new();

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
                if (!string.IsNullOrEmpty(CompanyId))
                {
                    featureStatus = await AppTierService.GetCompanyFeaturesAsync(AppId, CompanyId);
                }
                else
                {
                    featureStatus = await AppTierService.GetUserFeaturesAsync(AppId);
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
