using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>
    /// One entry point for everything a customer does with money on this platform: see the price
    /// list, sign up and buy, and manage what they bought.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from packages/wildwood-react/src/components/registrationSubscription/RegistrationAndSubscriptionComponent.tsx.
    /// It is a switch over <see cref="View"/> and nothing more: no state, no service call, no
    /// markup of its own. The three views stay first-class components, so a landing page that
    /// wants the price list alone keeps using
    /// <see cref="RegistrationSubscriptionPricing"/> directly — this component exists for hosts
    /// that would rather set a parameter than pick a component.
    /// </para>
    /// <para>
    /// <b>The parameter surface.</b> React discriminates its props union on <c>view</c>, so a
    /// prop belonging to another view is a compile error there. Blazor has no discriminated-union
    /// parameters, so the shell instead declares the UNION of the three views' parameters and
    /// forwards to the active view only the ones that view actually has. Every parameter below
    /// says which view or views it reaches; one set for a view that is not active is simply not
    /// forwarded — never an exception, never a render error. Hosts that want the compiler to
    /// catch a misplaced parameter use the view components directly.
    /// </para>
    /// <para>
    /// <b>Why this is hand-written and not reflective.</b> Forwarding through
    /// <c>DynamicComponent</c> and a dictionary would drop the type of every parameter and every
    /// callback at the shell's boundary, turning a wrong type from a compile error into a runtime
    /// one. The union is written out instead, and
    /// <c>RegistrationAndSubscriptionShellTests</c> compares it against the three views by
    /// reflection on every build: a parameter added to a view and not to the shell fails the
    /// suite, and so does a default value that drifts apart from the view's own.
    /// </para>
    /// <para>
    /// <b>Defaults.</b> A shell parameter carries the same default as the view parameter it feeds.
    /// <see cref="ShowAddOns"/> is the one exception — the pricing view defaults it off and the
    /// manage view defaults it on — so the shell takes it as a <c>bool?</c> whose null means
    /// "leave the active view's own default alone".
    /// </para>
    /// <para>
    /// Errors, the root CSS class and any extra HTML attribute are the base class's, and are
    /// forwarded to the active view, which owns the root element.
    /// </para>
    /// </remarks>
    public partial class RegistrationAndSubscriptionComponent : BaseWildwoodComponent
    {
        #region Parameters — the switch

        /// <summary>
        /// Which surface to render. Defaults to <see cref="RegistrationSubscriptionView.Pricing"/>,
        /// as in JS.
        /// </summary>
        [Parameter] public RegistrationSubscriptionView View { get; set; } = RegistrationSubscriptionView.Pricing;

        #endregion

        #region Parameters — common to every view

        /// <summary>The app being priced, signed up for, or managed. <b>All three views.</b></summary>
        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;

        /// <summary>
        /// Overrides the currency the catalog is quoted in. Rarely needed: the server names the
        /// currency, and this only wins for display. <b>All three views.</b>
        /// </summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>
        /// Copy the host may replace. An instance the host only partially set keeps the shipped
        /// word for every string it left alone. <b>All three views.</b>
        /// </summary>
        [Parameter] public RegistrationSubscriptionLabels? Labels { get; set; }

        /// <summary>Where "contact us" and enterprise plans point. <b>All three views.</b></summary>
        [Parameter] public string? ContactUrl { get; set; }

        #endregion

        #region Parameters — shared by more than one view

        /// <summary>
        /// Whether packs are offered at all. <b>Pricing and manage.</b> Null, the default, keeps
        /// each view's own answer: off on the pricing page, on in the manage view.
        /// </summary>
        [Parameter] public bool? ShowAddOns { get; set; }

        /// <summary>
        /// Whether the visitor may tick several packs and continue once. <b>Pricing and signup.</b>
        /// </summary>
        [Parameter] public PricingPackSelection PackSelection { get; set; } = PricingPackSelection.None;

        /// <summary>
        /// Headings to file packs under, matched on the add-on's catalog category.
        /// <b>Pricing and signup.</b>
        /// </summary>
        [Parameter] public IReadOnlyList<AddOnGroup>? AddOnGroups { get; set; }

        /// <summary>
        /// Host marketing copy for a pack, replacing the catalog's own description.
        /// <b>Pricing and signup.</b>
        /// </summary>
        [Parameter] public RenderFragment<AppTierAddOnModel>? DescribeAddOn { get; set; }

        /// <summary>
        /// A catalog the host already has — read during a server prerender, or shared with another
        /// surface. Given one, the view renders real prices on the first paint and asks the server
        /// for nothing. <b>Pricing and signup.</b>
        /// </summary>
        [Parameter] public PublicCatalog? PreloadedCatalog { get; set; }

        /// <summary>
        /// Where the host means to send the visitor afterwards. CARRIED, never navigated to.
        /// <b>Signup and manage.</b>
        /// </summary>
        [Parameter] public string? ReturnUrl { get; set; }

        /// <summary>
        /// Raised after the account's entitlements change, carrying one of
        /// <see cref="EntitlementsChangedReasons"/>. <b>Signup and manage.</b>
        /// </summary>
        [Parameter] public EventCallback<string> OnEntitlementsChanged { get; set; }

        #endregion

        #region Parameters — the pricing view

        /// <summary>Show the plan grid. <b>Pricing only.</b></summary>
        [Parameter] public bool ShowPlans { get; set; } = true;

        /// <summary>Keep free plans in the grid. <b>Pricing only.</b></summary>
        [Parameter] public bool OfferFreeTierChoice { get; set; } = true;

        /// <summary>Show the monthly/annual toggle when any plan is priced annually. <b>Pricing only.</b></summary>
        [Parameter] public bool ShowBillingToggle { get; set; } = true;

        /// <summary>Which side the toggle starts on. <b>Pricing only.</b></summary>
        [Parameter] public PricingBilling DefaultBilling { get; set; } = PricingBilling.Monthly;

        /// <summary>Show each plan's feature list. <b>Pricing only.</b></summary>
        [Parameter] public bool ShowFeatureComparison { get; set; } = true;

        /// <summary>Show each plan's usage limits. <b>Pricing only.</b></summary>
        [Parameter] public bool ShowLimits { get; set; } = true;

        /// <summary>Marks one plan as the visitor's current choice. <b>Pricing only.</b></summary>
        [Parameter] public string? HighlightTierId { get; set; }

        /// <summary>Emit schema.org offers for the live catalog. <b>Pricing only.</b></summary>
        [Parameter] public bool IncludeJsonLd { get; set; }

        /// <summary>Canonical URL applied to every emitted offer. <b>Pricing only.</b></summary>
        [Parameter] public string? JsonLdUrl { get; set; }

        /// <summary>Replaces the built-in loading placeholder. <b>Pricing only.</b></summary>
        [Parameter] public RenderFragment? LoadingFallback { get; set; }

        /// <summary>
        /// Replaces the built-in "pricing is unavailable" panel, Retry included. <b>Pricing only.</b>
        /// </summary>
        [Parameter] public RenderFragment? ErrorFallback { get; set; }

        /// <summary>
        /// Raised when the visitor picks a plan, a pack, or a set of packs. <b>Pricing only.</b>
        /// </summary>
        [Parameter] public EventCallback<PricingSelection> OnSelect { get; set; }

        #endregion

        #region Parameters — the signup view

        /// <summary>The plan a pricing page already chose. <b>Signup only.</b></summary>
        [Parameter] public string? PreSelectedTierId { get; set; }

        /// <summary>The pricing option within that plan. <b>Signup only.</b></summary>
        [Parameter] public string? PreSelectedPricingId { get; set; }

        /// <summary>Packs a pricing page already chose. <b>Signup only.</b></summary>
        [Parameter] public IReadOnlyList<string>? PreSelectedAddOnIds { get; set; }

        /// <summary>An invitation token from the signup link. <b>Signup only.</b></summary>
        [Parameter] public string? RegistrationToken { get; set; }

        /// <summary>Pre-fills the username and email fields. <b>Signup only.</b></summary>
        [Parameter] public string? PrefillEmail { get; set; }

        /// <summary>
        /// <see cref="SignupPlanSelection.Skip"/> leaves the plan to the app. <b>Signup only.</b>
        /// </summary>
        [Parameter] public SignupPlanSelection PlanSelection { get; set; } = SignupPlanSelection.Choose;

        /// <summary>
        /// <see cref="SignupPlanDefault.Free"/> opens the plan step on the app's free plan — a
        /// suggestion, not a choice. <b>Signup only.</b>
        /// </summary>
        [Parameter] public SignupPlanDefault PlanDefault { get; set; } = SignupPlanDefault.None;

        /// <summary>
        /// <see cref="SignupTokenMode.Required"/> is invite redemption. <b>Signup only.</b>
        /// </summary>
        [Parameter] public SignupTokenMode TokenMode { get; set; } = SignupTokenMode.Auto;

        /// <summary>Collect a billing address with the card. <b>Signup only.</b></summary>
        [Parameter] public bool RequireBillingAddress { get; set; }

        /// <summary>Replaces the built-in "registration is closed" notice. <b>Signup only.</b></summary>
        [Parameter] public RenderFragment<RegistrationClosedContext>? RenderClosed { get; set; }

        /// <summary>
        /// Raised once when the visitor turned out to have a session already. <b>Signup only.</b>
        /// </summary>
        [Parameter] public EventCallback OnAlreadySignedIn { get; set; }

        /// <summary>
        /// Raised exactly once, when the visitor leaves the finished signup. <b>Signup only.</b>
        /// </summary>
        [Parameter] public EventCallback<SignupOutcome> OnSignupComplete { get; set; }

        /// <summary>Raised when the visitor backs out of the flow. <b>Signup only.</b></summary>
        [Parameter] public EventCallback OnCancel { get; set; }

        #endregion

        #region Parameters — the manage view

        /// <summary>Tabs (the default) or every section down the page. <b>Manage only.</b></summary>
        [Parameter] public ManageLayout Layout { get; set; } = ManageLayout.Tabs;

        /// <summary>Which panels to render, in what order. <b>Manage only.</b></summary>
        [Parameter] public IReadOnlyList<ManageSection>? Sections { get; set; }

        /// <summary>
        /// Lift the subscription card out of the section list and above the tab bar. <b>Manage only.</b>
        /// </summary>
        [Parameter] public bool ShowStatusAboveTabs { get; set; }

        /// <summary>
        /// Whether the viewer may see overrides and edit usage limits. <b>Manage only.</b>
        /// </summary>
        [Parameter] public bool IsAdmin { get; set; }

        /// <summary>An admin acting on one user's subscription. <b>Manage only.</b></summary>
        [Parameter] public string? UserId { get; set; }

        /// <summary>An admin acting on a company's subscription. <b>Manage only.</b></summary>
        [Parameter] public string? CompanyId { get; set; }

        /// <summary>
        /// Whether the customer may buy packs for themselves, through the pack checkout.
        /// <b>Manage only.</b>
        /// </summary>
        [Parameter] public bool AllowPackSelfService { get; set; }

        /// <summary>
        /// Whether this surface offers cancelling the subscription and its packs at all.
        /// <b>Manage only.</b>
        /// </summary>
        [Parameter] public bool AllowCancel { get; set; } = true;

        /// <summary>
        /// The host's own real-time usage, overlaid on the server's statuses. <b>Manage only.</b>
        /// </summary>
        [Parameter]
        public Func<List<AppTierLimitStatusModel>, UserTierSubscriptionModel?, Task<List<AppTierLimitStatusModel>>>?
            OnMergeUsage { get; set; }

        /// <summary>
        /// The host's own card modal. Given one, it is used INSTEAD of the built-in modal.
        /// <b>Manage only.</b>
        /// </summary>
        [Parameter] public Func<PaymentRequiredArgs, Task<string?>>? OnPaymentRequired { get; set; }

        /// <summary>
        /// Raised after every mutation that changed what the subscription is. <b>Manage only.</b>
        /// </summary>
        [Parameter] public EventCallback OnSubscriptionChanged { get; set; }

        #endregion

        #region Forwarding

        /// <summary>
        /// The pricing view's answer for <see cref="ShowAddOns"/>: off unless the host said
        /// otherwise, which is <see cref="RegistrationSubscriptionPricing.ShowAddOns"/>'s own default.
        /// </summary>
        private bool PricingShowAddOns => ShowAddOns ?? false;

        /// <summary>
        /// The manage view's answer for <see cref="ShowAddOns"/>: on unless the host said
        /// otherwise, which is <see cref="RegistrationSubscriptionManage.ShowAddOns"/>'s own default.
        /// </summary>
        private bool ManageShowAddOns => ShowAddOns ?? true;

        #endregion
    }
}
