using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Consent
{
    /// <summary>
    /// Consent banner + preferences modal. Blocks gated third-party scripts until the visitor
    /// consents to the matching category. Honors GPC and exposes the CCPA opt-out surfaces.
    /// </summary>
    public partial class ConsentBanner : BaseWildwoodComponent
    {
        [Inject] private IConsentService ConsentService { get; set; } = default!;

        /// <summary>The app whose consent config + script registry to load.</summary>
        [Parameter] public string AppId { get; set; } = string.Empty;

        /// <summary>Optional base URL override for the WildwoodAPI host.</summary>
        [Parameter] public string? BaseUrl { get; set; }

        /// <summary>Fires whenever the visitor's consent state changes.</summary>
        [Parameter] public EventCallback<ConsentStateModel> OnConsentChanged { get; set; }

        /// <summary>Render a footer "Privacy choices" link so the visitor can reopen preferences.</summary>
        [Parameter] public bool ShowReopenLink { get; set; } = true;

        /// <summary>
        /// Render standalone CCPA opt-out footer links ("Do Not Sell or Share", "Limit Use of
        /// Sensitive PI") when the config enables those surfaces - one-click, without the modal.
        /// </summary>
        [Parameter] public bool ShowFooterOptOut { get; set; } = true;

        protected ConsentConfigModel? Config;
        protected ConsentStateModel? State;
        protected bool ShowBanner;
        protected bool ShowPreferences;
        protected bool Initialized;
        protected readonly Dictionary<string, bool> Selection = new();

        private ElementReference _modalRef;
        private bool _focusTrapped;

        protected static readonly string[] NonNecessaryCategories = { "Functional", "Analytics", "Advertising", "Sensitive" };

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            // Initialize as soon as an AppId is available (not only on the first render): a host that
            // binds AppId asynchronously would otherwise never initialize. JS interop is only safe
            // after a render, so this stays in OnAfterRenderAsync; the Initialized guard makes it run once.
            if (!Initialized && !string.IsNullOrEmpty(AppId))
            {
                Initialized = true;
                var result = await ConsentService.InitializeAsync(AppId, BaseUrl);
                Config = result.Config;
                State = result.State;
                ShowBanner = result.ShouldShowBanner;
                StateHasChanged();
            }

            // Trap focus while the preferences dialog is open; release when it closes.
            if (ShowPreferences && !_focusTrapped)
            {
                _focusTrapped = true;
                await ConsentService.TrapFocusAsync(_modalRef);
            }
            else if (!ShowPreferences && _focusTrapped)
            {
                _focusTrapped = false;
                await ConsentService.ReleaseFocusAsync();
            }
        }

        protected void OnModalKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Escape")
                ClosePreferences();
        }

        protected bool CategoryActive(string category)
        {
            if (Config?.Categories == null) return false;
            foreach (var c in Config.Categories)
            {
                if (c == category) return true;
            }
            return false;
        }

        protected bool CategoryLocked(string category) => category == "StrictlyNecessary";

        protected async Task AcceptAll()
        {
            State = await ConsentService.AcceptAllAsync();
            await FinishDecision();
        }

        protected async Task RejectAll()
        {
            State = await ConsentService.RejectAllAsync();
            await FinishDecision();
        }

        protected void OpenPreferences()
        {
            Selection.Clear();
            foreach (var category in NonNecessaryCategories)
            {
                if (!CategoryActive(category)) continue;
                var on = State?.Categories != null && State.Categories.TryGetValue(category, out var v) && v;
                Selection[category] = on;
            }
            ShowPreferences = true;
        }

        protected void ClosePreferences() => ShowPreferences = false;

        protected void ToggleSelection(string category, bool value)
        {
            Selection[category] = value;
        }

        protected async Task SavePreferences()
        {
            State = await ConsentService.SetCategoriesAsync(new Dictionary<string, bool>(Selection));
            await FinishDecision();
        }

        /// <summary>
        /// One-click CCPA opt-out: turn a category off against the visitor's current state and post
        /// the decision immediately (no separate Save).
        /// </summary>
        protected async Task OptOut(string category)
        {
            var next = new Dictionary<string, bool>();
            foreach (var c in NonNecessaryCategories)
            {
                if (!CategoryActive(c)) continue;
                var on = State?.Categories != null && State.Categories.TryGetValue(c, out var v) && v;
                next[c] = c == category ? false : on;
            }
            State = await ConsentService.SetCategoriesAsync(next);
            await FinishDecision();
        }

        /// <summary>
        /// Public entry point so host app code (e.g. a footer "Privacy choices" / "Do Not Sell" link)
        /// can reopen the preferences dialog at any time. Capture the component with @ref to call it.
        /// Also used by the built-in footer link.
        /// </summary>
        public void ReopenPreferences()
        {
            OpenPreferences();
            StateHasChanged();
        }

        private async Task FinishDecision()
        {
            ShowBanner = false;
            ShowPreferences = false;
            if (State != null)
                await OnConsentChanged.InvokeAsync(State);
            StateHasChanged();
        }

        protected string BannerTitle => Config?.BannerText?.Title ?? "We value your privacy";
        protected string BannerBody => Config?.BannerText?.Body
            ?? "We use cookies and similar technologies. Choose which categories to allow. Necessary items are always on.";
        protected string AcceptLabel => Config?.BannerText?.AcceptAll ?? "Accept all";
        protected string RejectLabel => Config?.BannerText?.RejectAll ?? "Reject all";
        protected string ManageLabel => Config?.BannerText?.Manage ?? "Manage preferences";
        protected string BannerPosition => Config?.Appearance?.Position ?? "bottomBar";
    }
}
