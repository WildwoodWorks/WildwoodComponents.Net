using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>
    /// The pricing surface of the registration + subscription component: what the app sells, at
    /// the price the server is quoting right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from packages/wildwood-react/src/components/registrationSubscription/views/PricingView.tsx,
    /// and named after it so the same surface is called the same thing on every stack.
    /// </para>
    /// <para>
    /// Three rules shape it. Every price comes off the live public catalog — there is no fallback
    /// price, no remembered price and no "from" price anywhere in this folder, and a source-guard
    /// test greps for one; while the catalog loads the visitor sees shapes, and if it cannot be
    /// read they see "pricing is unavailable" and a Retry. The plan grid is the platform's
    /// existing tier-card markup, calls to action unchanged, because live sites' end-to-end suites
    /// locate plans by exactly those. And the view never navigates: a selection is handed to
    /// <see cref="OnSelect"/> and the host decides where it leads.
    /// </para>
    /// <para>
    /// A catalog failure is reported through the INHERITED <see cref="BaseWildwoodComponent.OnError"/>
    /// callback, with <see cref="PricingViewDecisions.CatalogErrorCode"/> as the
    /// <c>ComponentErrorEventArgs.Context</c> — the .NET spelling of JS's
    /// <c>{ code: 'catalog_unavailable', message }</c>. It is never redeclared here: two
    /// <c>[Parameter]</c> properties of one name make a component impossible to render at all.
    /// </para>
    /// </remarks>
    public partial class RegistrationSubscriptionPricing : BaseWildwoodComponent
    {
        [Inject] private IPublicCatalogService CatalogService { get; set; } = default!;

        #region Parameters — common to every view

        /// <summary>The app whose catalog to show.</summary>
        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;

        /// <summary>
        /// Overrides the currency the catalog is quoted in. Rarely needed: the server names the
        /// currency, and this only wins for display.
        /// </summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>
        /// Copy the host may replace. An instance the host only partially set keeps the shipped
        /// word for every string it left alone.
        /// </summary>
        [Parameter] public RegistrationSubscriptionLabels? Labels { get; set; }

        /// <summary>Where "contact us" and enterprise plans point.</summary>
        [Parameter] public string? ContactUrl { get; set; }

        #endregion

        #region Parameters — the pricing view

        /// <summary>Show the plan grid.</summary>
        [Parameter] public bool ShowPlans { get; set; } = true;

        /// <summary>Show the pack grid. Off by default — most pricing pages sell plans only.</summary>
        [Parameter] public bool ShowAddOns { get; set; }

        /// <summary>Keep free plans in the grid. False hides every free tier.</summary>
        [Parameter] public bool OfferFreeTierChoice { get; set; } = true;

        /// <summary>Whether a visitor may tick several packs and continue once.</summary>
        [Parameter] public PricingPackSelection PackSelection { get; set; } = PricingPackSelection.None;

        /// <summary>
        /// Headings to file packs under, matched on the add-on's catalog category. Empty groups
        /// are dropped; packs matching no group get a trailing "More packs" group.
        /// </summary>
        [Parameter] public IReadOnlyList<AddOnGroup>? AddOnGroups { get; set; }

        /// <summary>
        /// Host marketing copy for a pack, replacing the catalog's own description. The Blazor
        /// analog of React's <c>describeAddOn</c>: a template rather than a callback, because
        /// markup is what it returns.
        /// </summary>
        [Parameter] public RenderFragment<AppTierAddOnModel>? DescribeAddOn { get; set; }

        /// <summary>Show the monthly/annual toggle when any plan is priced annually.</summary>
        [Parameter] public bool ShowBillingToggle { get; set; } = true;

        /// <summary>Which side the toggle starts on.</summary>
        [Parameter] public PricingBilling DefaultBilling { get; set; } = PricingBilling.Monthly;

        /// <summary>Show each plan's feature list.</summary>
        [Parameter] public bool ShowFeatureComparison { get; set; } = true;

        /// <summary>Show each plan's usage limits.</summary>
        [Parameter] public bool ShowLimits { get; set; } = true;

        /// <summary>Marks one plan as the visitor's current choice. Matched case-insensitively.</summary>
        [Parameter] public string? HighlightTierId { get; set; }

        /// <summary>Emit schema.org offers for the live catalog.</summary>
        [Parameter] public bool IncludeJsonLd { get; set; }

        /// <summary>Canonical URL applied to every emitted offer.</summary>
        [Parameter] public string? JsonLdUrl { get; set; }

        /// <summary>
        /// A catalog the host already has — read during a server prerender, or shared with another
        /// surface. Given one, the view renders real prices on the first paint and asks the server
        /// for nothing. The Blazor analog of React's <c>initialCatalog</c>, and the same idea as
        /// <c>PricingDisplayComponent.PreloadedTiers</c>.
        /// </summary>
        [Parameter] public PublicCatalog? PreloadedCatalog { get; set; }

        /// <summary>Replaces the built-in loading placeholder.</summary>
        [Parameter] public RenderFragment? LoadingFallback { get; set; }

        /// <summary>Replaces the built-in "pricing is unavailable" panel, Retry included.</summary>
        [Parameter] public RenderFragment? ErrorFallback { get; set; }

        /// <summary>Raised when the visitor picks a plan, a pack, or a set of packs.</summary>
        [Parameter] public EventCallback<PricingSelection> OnSelect { get; set; }

        #endregion

        #region State

        private PublicCatalog? _catalog;

        /// <summary>
        /// Starts TRUE. The base class awaits the theme before the component's own initialisation
        /// runs, so a render can happen first; without this the visitor would see "pricing is
        /// unavailable" flash before the request the view is about to make has even been sent.
        /// </summary>
        private bool _catalogLoading = true;

        private string? _catalogError;

        /// <summary>The last failure handed to <c>OnError</c>, so one failure is reported once.</summary>
        private string? _reportedError;

        private PricingBilling _billing;
        private List<string> _selectedPackIds = new List<string>();

        /// <summary>The appId the catalog on screen was loaded for, so a change reloads it.</summary>
        private string? _loadedAppId;

        /// <summary>The currency override the catalog on screen was loaded with.</summary>
        private string? _loadedCurrency;

        #endregion

        #region Derived state

        /// <summary>
        /// The catalog to render: the host's, when it supplied one, otherwise whatever the last
        /// load returned.
        /// </summary>
        private PublicCatalog? Catalog
        {
            get { return PreloadedCatalog ?? _catalog; }
        }

        /// <summary>
        /// The copy the markup renders: the host's instance, or the shipped words. Never
        /// <see cref="Labels"/> directly — that parameter is nullable by design.
        /// </summary>
        private RegistrationSubscriptionLabels ResolvedLabels
        {
            get { return RegistrationSubscriptionLabels.Resolve(Labels); }
        }

        private PricingBodyKind Body
        {
            get
            {
                return PricingViewDecisions.BodyKind(
                    Catalog, _catalogLoading, _catalogError, ShowPlans, ShowAddOns, OfferFreeTierChoice);
            }
        }

        private string DisplayCurrency
        {
            get { return PricingViewDecisions.ResolveDisplayCurrency(Currency, Catalog); }
        }

        private List<AppTierModel> VisibleTiers
        {
            get { return PricingViewDecisions.VisibleTiers(Catalog, OfferFreeTierChoice); }
        }

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            _billing = DefaultBilling;
            await LoadCatalogAsync(false);
        }

        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();

            // The app or the currency moved under us (a host switching apps on one page): the
            // catalog on screen belongs to the old one, so it is replaced rather than kept.
            if (_loadedAppId is null) return;
            if (string.Equals(_loadedAppId, AppId, StringComparison.Ordinal) &&
                string.Equals(_loadedCurrency ?? string.Empty, Currency ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            _catalog = null;
            await LoadCatalogAsync(false);
        }

        #endregion

        #region Loading

        /// <summary>
        /// Reads the live catalog, unless the host handed one in. A failure leaves NO prices on
        /// screen and is reported once per distinct message — a Retry that fails again is news, a
        /// re-render is not.
        /// </summary>
        private async Task LoadCatalogAsync(bool forceRefresh)
        {
            if (!PricingViewDecisions.ShouldLoadCatalog(PreloadedCatalog, AppId))
            {
                _catalogLoading = false;

                // No app to ask about and no catalog to show: the unavailable panel, never a price.
                if (PreloadedCatalog is null)
                {
                    await SetCatalogErrorAsync(new InvalidOperationException(
                        "An appId is required to load the public catalog."));
                }

                return;
            }

            _loadedAppId = AppId;
            _loadedCurrency = Currency;
            _catalogLoading = true;
            _catalogError = null;
            StateHasChanged();

            try
            {
                _catalog = await CatalogService.GetAsync(AppId, Currency, forceRefresh);
                _catalogError = null;
                _reportedError = null;
            }
            catch (Exception ex)
            {
                _catalog = null;
                await SetCatalogErrorAsync(ex);
            }
            finally
            {
                _catalogLoading = false;
                StateHasChanged();
            }
        }

        private async Task SetCatalogErrorAsync(Exception exception)
        {
            var message = exception.Message;
            _catalogError = message is { Length: > 0 } ? message : "Failed to load the catalog";

            if (!PricingViewDecisions.ShouldReportError(_reportedError, _catalogError)) return;

            _reportedError = _catalogError;
            await InvokeOnErrorAsync(exception, PricingViewDecisions.CatalogErrorCode);
        }

        /// <summary>Retries the catalog request, bypassing the shared cache's TTL.</summary>
        private Task RetryAsync()
        {
            return LoadCatalogAsync(true);
        }

        #endregion

        #region Selection

        private Task HandleBillingChangeAsync(PricingBilling billing)
        {
            _billing = billing;
            StateHasChanged();
            return Task.CompletedTask;
        }

        private Task HandleSelectTierAsync(AppTierModel tier)
        {
            return RaiseSelectAsync(
                PricingViewDecisions.PlanSelectionPayload(tier, _billing, _selectedPackIds));
        }

        private Task HandleTogglePackAsync(AppTierAddOnModel addOn)
        {
            _selectedPackIds = PricingViewDecisions.TogglePackSelection(Catalog, _selectedPackIds, addOn.Id);
            StateHasChanged();
            return Task.CompletedTask;
        }

        private Task HandleChoosePackAsync(AppTierAddOnModel addOn)
        {
            return RaiseSelectAsync(PricingViewDecisions.PackSelectionPayload(addOn.Id, _billing));
        }

        private Task HandleContinueWithPacksAsync()
        {
            return RaiseSelectAsync(PricingViewDecisions.PacksContinuePayload(_billing, _selectedPackIds));
        }

        private Task RaiseSelectAsync(PricingSelection selection)
        {
            return OnSelect.HasDelegate ? OnSelect.InvokeAsync(selection) : Task.CompletedTask;
        }

        #endregion
    }
}
