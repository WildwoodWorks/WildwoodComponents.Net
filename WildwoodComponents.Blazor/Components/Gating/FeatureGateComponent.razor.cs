using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.Gating
{
    /// <summary>
    /// Renders <see cref="ChildContent"/> only when the authenticated user's plan includes the
    /// feature. Backed by <see cref="IFeatureEntitlementService"/> — one shared bulk fetch per
    /// app, so gating many surfaces is cheap.
    ///
    /// Client-side gating is UX only: the server must still enforce the entitlement.
    /// Accordingly the gate FAILS OPEN when entitlements can't be loaded (transient error) —
    /// children render and the server remains the enforcement point — and shows
    /// <see cref="LoadingFallback"/> while loading.
    /// </summary>
    public partial class FeatureGateComponent : BaseWildwoodComponent
    {
        [Inject] private IFeatureEntitlementService EntitlementService { get; set; } = default!;

        /// <summary>Feature code defined in the app's tier catalog (case-insensitive), e.g. "AI_ASSISTANT".</summary>
        [Parameter, EditorRequired] public string Feature { get; set; } = string.Empty;

        /// <summary>App to check entitlements for. Defaults to the configured AppId.</summary>
        [Parameter] public string? AppId { get; set; }

        [Parameter] public RenderFragment? ChildContent { get; set; }

        /// <summary>Rendered when the user's plan does NOT include the feature (e.g. an upgrade prompt).</summary>
        [Parameter] public RenderFragment? Fallback { get; set; }

        /// <summary>Rendered while entitlements are loading. Defaults to nothing (avoids flashing locked UI).</summary>
        [Parameter] public RenderFragment? LoadingFallback { get; set; }

        private bool _loading = true;
        private bool _hasFeature = true; // fail open until entitlements are known
        private bool _entitlementsSubscribed;

        protected override async Task OnComponentInitializedAsync()
        {
            EntitlementService.EntitlementsChanged += OnEntitlementsChanged;
            _entitlementsSubscribed = true;
            await LoadAsync();
        }

        protected override async Task OnParametersSetAsync()
        {
            // A changed feature code or app must re-evaluate against the (cached) map.
            if (!_loading)
            {
                await LoadAsync();
            }
        }

        private async Task LoadAsync()
        {
            _hasFeature = await EntitlementService.HasFeatureAsync(Feature, AppId);
            _loading = false;
        }

        private async void OnEntitlementsChanged()
        {
            // Entitlements were invalidated (auth change or subscription mutation) — reload
            // through the shared cache so N mounted gates trigger one refetch, not N.
            try
            {
                await InvokeAsync(async () =>
                {
                    await LoadAsync();
                    StateHasChanged();
                });
            }
            catch (ObjectDisposedException)
            {
                // Component was disposed before the callback completed — safe to ignore.
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _entitlementsSubscribed)
            {
                _entitlementsSubscribed = false;
                EntitlementService.EntitlementsChanged -= OnEntitlementsChanged;
            }
            base.Dispose(disposing);
        }
    }
}
