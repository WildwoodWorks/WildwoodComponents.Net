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

        /// <summary>
        /// While the banner is up, add its height to the page's padding at the edge the banner is
        /// anchored to, so it does not cover anything the host anchors there. Default true.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The banner is <c>position: fixed</c> with a very high z-index, so without this it
        /// silently sits on top of chat composers, sticky action bars and the like - the host has
        /// no way to know how much room to leave, because the height depends on the configured
        /// copy and on how it wraps. Either way the measured height is published as
        /// <c>--ww-consent-height</c> on the document element, so a host that would rather place
        /// the room itself can set this false and use the variable (it is removed again when the
        /// banner goes). Ported from the React <c>ConsentBanner</c>'s <c>reserveSpace</c> prop.
        /// </para>
        /// <para>
        /// Only the two BAR positions reserve padding: <c>bottomBar</c> pads the bottom and
        /// <c>topBar</c> the top. A <c>corner</c> card is a small box inset from the bottom-right,
        /// so padding the whole page for it would leave a full-width blank strip under the content
        /// for as long as the banner is up; it publishes the height and pads nothing.
        /// </para>
        /// </remarks>
        [Parameter] public bool ReserveSpace { get; set; } = true;

        protected ConsentConfigModel? Config;
        protected ConsentStateModel? State;
        protected bool ShowBanner;
        protected bool ShowPreferences;
        protected bool Initialized;
        protected readonly Dictionary<string, bool> Selection = new();

        private ElementReference _modalRef;
        private bool _focusTrapped;
        private ElementReference _bannerRef;
        private bool _spaceReserved;

        /// <summary>
        /// This instance's own reservation token. Two <c>ConsentBanner</c>s in one circuit share
        /// the scoped <see cref="IConsentService"/>, and so one cached copy of the JS engine: the
        /// engine keys every reservation by this, so neither can release or double-count the
        /// other's. It also outlives the banner's DOM node, which is what lets
        /// <see cref="Dispose(bool)"/> give the room back after the element has gone.
        /// </summary>
        private readonly string _spaceKey = "ww-consent-" + System.Guid.NewGuid().ToString("N");

        protected static readonly string[] NonNecessaryCategories = { "Functional", "Analytics", "Advertising", "Sensitive" };

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            // Initialize as soon as an AppId is available (not only on the first render): a host that
            // binds AppId asynchronously would otherwise never initialize. JS interop is only safe
            // after a render, so this stays in OnAfterRenderAsync; the Initialized guard makes it run once.
            var justInitialized = false;
            if (!Initialized && !string.IsNullOrEmpty(AppId))
            {
                Initialized = true;
                var result = await ConsentService.InitializeAsync(AppId, BaseUrl);
                Config = result.Config;
                State = result.State;
                ShowBanner = result.ShouldShowBanner;
                StateHasChanged();
                justInitialized = true;
            }

            // Keep the fixed banner off the page's own edge-anchored UI while it is up, and give
            // every bit of it back the moment it goes. Never on the pass that just initialized:
            // StateHasChanged only QUEUES the render that puts the banner on the page, so its
            // element reference is not captured yet and there would be nothing to measure. The
            // render that follows brings this method straight back with the banner in hand.
            if (!justInitialized && ShowBanner && !_spaceReserved)
            {
                _spaceReserved = true;
                await ConsentService.ReserveBannerSpaceAsync(_bannerRef, ReserveSpace, _spaceKey);
            }
            else if (!ShowBanner && _spaceReserved)
            {
                _spaceReserved = false;
                await ConsentService.ReleaseBannerSpaceAsync(_spaceKey);
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

        /// <summary>
        /// A host that removes the component while the banner is up takes the banner with it, so
        /// the page's padding has to come back too.
        /// </summary>
        /// <remarks>
        /// Not awaited, and it cannot be: <see cref="System.IDisposable.Dispose"/> is
        /// synchronous. The service swallows a disconnected circuit (the page is gone, and with it
        /// the padding), so this is a call that either lands or is moot. It releases by token
        /// rather than by element because the banner's DOM node has already gone by now - and
        /// only this instance's token, so another banner still up keeps its own room.
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _spaceReserved)
            {
                _spaceReserved = false;
                _ = ConsentService.ReleaseBannerSpaceAsync(_spaceKey);
            }

            base.Dispose(disposing);
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
