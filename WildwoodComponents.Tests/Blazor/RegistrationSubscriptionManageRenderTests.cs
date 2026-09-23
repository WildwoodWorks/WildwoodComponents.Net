using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts;
using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

// BL0005: setting a component parameter from outside the component is exactly how a component is
// put into a given state here - there is no renderer to pass a ParameterView through. Scoped to
// this file, so the advisory still applies to every component that ships.
#pragma warning disable BL0005

/// <summary>
/// What the manage view and its two new parts actually put on the page: the sections a viewer is
/// allowed to see, the layout they are arranged in, the modals that open over them, and the exact
/// words every stack has to say.
/// </summary>
/// <remarks>
/// Same technique as <see cref="RegistrationSubscriptionSignupRenderTests"/>: there is no bUnit
/// renderer in this repo, so <c>BuildRenderTree</c> is called on a bare instance, which reads
/// initialised fields only. Where the markup depends on the plan change, the state is reached by
/// DRIVING a real <c>PlanChangeDriver</c> over a scripted transport and then handing it to the
/// component — the state under test is one the flow can actually be in.
/// </remarks>
public class RegistrationSubscriptionManageRenderTests
{
    #region Reflection + render helpers

    private static void SetField(object component, string name, object? value)
    {
        component.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);
    }

    private static object? GetProperty(object component, string name)
    {
        return component.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(component);
    }

    /// <summary>
    /// Fills an <c>[Inject]</c> property by hand. Nothing here runs a lifecycle, so DI never
    /// happens; the card modal reads the session while it renders, and a bare instance would have
    /// none.
    /// </summary>
    private static void SetService(object component, string name, object value)
    {
        component.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);
    }

    // BL0006: reading RenderTree frames is exactly what these tests are for - the components have
    // no bUnit renderer, so the compiled markup is the only thing there is to assert against.
#pragma warning disable BL0006
    private static ArrayRange<RenderTreeFrame> Frames(object component)
    {
        var builder = new RenderTreeBuilder();
        component.GetType()
            .GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, new object[] { builder });

        return builder.GetFrames();
    }

    /// <summary>Only what a reader would see: text and raw markup, no attribute values.</summary>
    private static string RenderText(object component)
    {
        var frames = Frames(component);
        var text = new StringBuilder();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Text) text.Append(frame.TextContent).Append(' ');
            else if (frame.FrameType == RenderTreeFrameType.Markup) text.Append(frame.MarkupContent).Append(' ');
        }

        return text.ToString();
    }

    /// <summary>Text, markup and every attribute as <c>name="value"</c>, for the test hooks.</summary>
    private static string RenderAll(object component)
    {
        var frames = Frames(component);
        var text = new StringBuilder();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Text:
                    text.Append(frame.TextContent).Append(' ');
                    break;
                case RenderTreeFrameType.Markup:
                    text.Append(frame.MarkupContent).Append(' ');
                    break;
                case RenderTreeFrameType.Attribute:
                    text.Append(frame.AttributeName)
                        .Append("=\"")
                        .Append(frame.AttributeValue as string ?? string.Empty)
                        .Append("\" ");
                    break;
            }
        }

        return text.ToString();
    }

    private static List<Type> ChildComponents(object component)
    {
        var frames = Frames(component);
        var types = new List<Type>();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Component) types.Add(frame.ComponentType);
        }

        return types;
    }

    /// <summary>How many times a section element or tab button names that section.</summary>
    private static int Count(string markup, string needle)
    {
        var hits = 0;
        var at = markup.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            hits++;
            at = markup.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return hits;
    }

    #endregion

    #region Driving a plan change to a step

    private const string PreviewOk =
        """
        {"success":true,"isUpgrade":true,"paymentRequired":false,"paymentProviderAvailable":true,
         "newTierName":"Pro","currency":"USD","allowImmediateChange":true}
        """;

    private const string PreviewNeedsCard =
        """
        {"success":true,"isUpgrade":true,"paymentRequired":true,"paymentProviderAvailable":true,
         "newTierName":"Pro","currency":"USD","allowImmediateChange":true}
        """;

    private static TierSelectedEventArgs Pro()
    {
        return new TierSelectedEventArgs
        {
            TierId = "tier-pro",
            TierName = "Pro",
            PricingId = "price-pro-monthly",
            PricingModelId = "pm-monthly",
            Price = 79m,
            TrialDays = 14,
            IsChange = true
        };
    }

    /// <summary>A driver over a scripted transport, left wherever the caller drove it.</summary>
    private static PlanChangeDriver Driver(
        string preview,
        Func<PaymentRequiredArgs, Task<string?>>? paymentRequested = null)
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.On("preview-change", preview).On("my-subscription/change", """{"success":true}""");

        var appTier = new AppTierComponentService(
            handler.CreateClient(), NullLogger<AppTierComponentService>.Instance);

        return new PlanChangeDriver(
            appTier,
            new FakePaymentProviderService(),
            new NoPaymentActions(),
            new PlanChangeSettings { AppId = "app-1", CompleteRetryDelay = TimeSpan.Zero })
        {
            PaymentRequested = paymentRequested
        };
    }

    /// <summary>A challenge nobody answers: these tests never get as far as one.</summary>
    private sealed class NoPaymentActions : IPaymentActionAdapter
    {
        public Task<PaymentActionOutcome> ConfirmPaymentAsync(string clientSecret, string? publishableKey)
            => Task.FromResult(PaymentActionOutcome.Failed(null));
    }

    /// <summary>The view, with a flow already driven to wherever the caller left it.</summary>
    private static RegistrationSubscriptionManage View(
        PlanChangeDriver? flow = null,
        Action<RegistrationSubscriptionManage>? configure = null)
    {
        var view = new RegistrationSubscriptionManage { AppId = "app-1" };
        configure?.Invoke(view);
        if (flow is not null) SetField(view, "_flow", flow);
        return view;
    }

    #endregion

    #region The view's own hooks

    [Fact]
    public void The_root_carries_the_view_hook_and_the_cross_stack_classes()
    {
        var markup = RenderAll(View());

        Assert.Contains("data-ww-view=\"manage\"", markup);
        Assert.Contains("ww-regsub ww-regsub-manage", markup);
    }

    /// <summary>The root names the plan change's step, which is what a live suite waits on.</summary>
    [Fact]
    public void The_root_names_the_step_the_plan_change_is_on()
    {
        Assert.Contains("data-ww-step=\"idle\"", RenderAll(View()));
    }

    [Fact]
    public async Task The_step_follows_the_flow()
    {
        var flow = Driver(PreviewOk);
        await flow.SelectTierAsync(Pro());

        Assert.Contains("data-ww-step=\"confirm\"", RenderAll(View(flow)));
    }

    [Fact]
    public void Without_an_app_the_view_says_so_and_renders_no_panels()
    {
        var view = new RegistrationSubscriptionManage();

        var markup = RenderText(view);

        Assert.Contains(ManageViewDecisions.AppIdRequired, markup);
        Assert.Empty(ChildComponents(view));
    }

    #endregion

    #region Sections and layout

    [Fact]
    public void Every_section_the_viewer_may_see_is_offered_as_a_tab()
    {
        var labels = RegistrationSubscriptionLabels.Defaults;
        var markup = RenderAll(View());

        foreach (var title in new[]
                 {
                     labels.SectionStatus, labels.SectionPlans, labels.SectionFeatures,
                     labels.SectionPacks, labels.SectionUsage
                 })
        {
            Assert.Contains(title, markup);
        }

        // Overrides is an admin panel and this viewer is not one.
        Assert.DoesNotContain("data-ww-section=\"overrides\"", markup);
    }

    [Fact]
    public void An_admin_is_shown_the_overrides_section()
    {
        var markup = RenderAll(View(configure: view => view.IsAdmin = true));

        Assert.Contains("data-ww-section=\"overrides\"", markup);
    }

    [Fact]
    public void Only_the_sections_the_host_asked_for_are_rendered()
    {
        var markup = RenderAll(View(configure: view =>
            view.Sections = new List<ManageSection> { ManageSection.Plans, ManageSection.Usage }));

        Assert.Contains("data-ww-section=\"plans\"", markup);
        Assert.Contains("data-ww-section=\"usage\"", markup);
        Assert.DoesNotContain("data-ww-section=\"subscription\"", markup);
        Assert.DoesNotContain("data-ww-section=\"addOns\"", markup);
    }

    [Fact]
    public void Turning_packs_off_drops_the_packs_section()
    {
        var markup = RenderAll(View(configure: view => view.ShowAddOns = false));

        Assert.DoesNotContain("data-ww-section=\"addOns\"", markup);
    }

    [Fact]
    public void The_subscription_card_can_be_lifted_above_the_tabs_instead_of_behind_one()
    {
        var view = View(configure: manage => manage.ShowStatusAboveTabs = true);
        var markup = RenderAll(view);

        Assert.Contains("ww-sub-status-card", markup);
        Assert.DoesNotContain("data-ww-section=\"subscription\"", markup);

        // The card is still rendered, just not as one of the tabs.
        Assert.Contains(typeof(SubscriptionStatusPanel), ChildComponents(view));
    }

    [Fact]
    public void A_stacked_layout_renders_every_section_down_the_page()
    {
        var view = View(configure: manage => manage.Layout = ManageLayout.Stacked);
        var markup = RenderAll(view);

        Assert.Equal(5, Count(markup, "ww-regsub-manage-section\""));
        Assert.DoesNotContain("ww-sub-admin-tabs", markup);
        Assert.Contains("ww-regsub-manage-section-title", markup);
    }

    /// <summary>
    /// The tabbed layout mounts ONE panel: a tab nobody is looking at must not fetch anything.
    /// </summary>
    [Fact]
    public void A_tabbed_layout_mounts_only_the_panel_on_screen()
    {
        var panels = ChildComponents(View());

        Assert.Contains(typeof(SubscriptionStatusPanel), panels);
        Assert.DoesNotContain(typeof(TierPlansPanel), panels);
        Assert.DoesNotContain(typeof(UsageLimitsPanel), panels);
    }

    #endregion

    #region The modals

    /// <summary>
    /// Every layout confirms. A plan picked in a stacked layout previewed and then showed nothing
    /// when only the tabbed return carried the modal.
    /// </summary>
    [Fact]
    public async Task A_plan_change_is_confirmed_in_the_stacked_layout_too()
    {
        var flow = Driver(PreviewOk);
        await flow.SelectTierAsync(Pro());

        var view = View(flow, manage => manage.Layout = ManageLayout.Stacked);

        Assert.Contains(typeof(TierChangeConfirmationModal), ChildComponents(view));
    }

    [Fact]
    public async Task The_component_opens_its_own_card_modal_when_the_host_brought_none()
    {
        var flow = Driver(PreviewNeedsCard);
        await flow.SelectTierAsync(Pro());
        await flow.ConfirmAsync(new TierChangeConfirmOptions { Immediate = true });

        Assert.Equal(PlanChangeStep.CollectingPayment, flow.Step);
        Assert.Contains(typeof(PaymentModal), ChildComponents(View(flow)));
    }

    /// <summary>
    /// The host's own modal wins, and the built-in one never appears over it — the flow answers
    /// with no payment request at all while a host handler is running.
    /// </summary>
    [Fact]
    public async Task The_built_in_card_modal_stays_shut_when_the_host_brought_its_own()
    {
        var pending = new TaskCompletionSource<string?>();
        var flow = Driver(PreviewNeedsCard, _ => pending.Task);
        await flow.SelectTierAsync(Pro());
        var confirming = flow.ConfirmAsync(new TierChangeConfirmOptions { Immediate = true });

        Assert.Equal(PlanChangeStep.CollectingPayment, flow.Step);
        Assert.DoesNotContain(typeof(PaymentModal), ChildComponents(View(flow)));

        pending.SetResult(null);
        await confirming;
    }

    [Fact]
    public void The_pack_picker_opens_from_the_packs_panel()
    {
        var view = View(configure: manage =>
        {
            manage.AllowPackSelfService = true;
            manage.Sections = new List<ManageSection> { ManageSection.AddOns };
        });

        Assert.DoesNotContain(typeof(PackPicker), ChildComponents(view));

        SetField(view, "_pickingPacks", true);

        Assert.Contains(typeof(PackPicker), ChildComponents(view));
    }

    #endregion

    #region Copy

    /// <summary>
    /// The pack rows' words come from the shared labels, so the complimentary row, its
    /// confirmation copy and the reactivate button say the same on every stack.
    /// </summary>
    [Fact]
    public void The_packs_panel_is_given_the_shared_pack_copy()
    {
        var labels = RegistrationSubscriptionLabels.Defaults;
        var packLabels = (AddOnsPanelLabels)GetProperty(View(), "PackLabels")!;

        Assert.Equal(labels.PackIncluded, packLabels.Included);
        Assert.Equal(labels.PackCancelIncluded, packLabels.CancelIncluded);
        Assert.Equal(labels.PackCancelBilled, packLabels.CancelBilled);
        Assert.Equal(labels.PackCancelConfirm, packLabels.CancelConfirm);
        Assert.Equal(labels.PackCancelKeep, packLabels.CancelKeep);
        Assert.Equal(labels.PackReactivate, packLabels.Reactivate);
        Assert.Equal(labels.AddPacks, packLabels.AddPacks);
    }

    /// <summary>A host that replaces one string keeps the shipped word for every other.</summary>
    [Fact]
    public void A_host_may_replace_one_word_without_losing_the_rest()
    {
        var view = View(configure: manage =>
            manage.Labels = new RegistrationSubscriptionLabels { SectionPacks = "Add-ons" });

        var markup = RenderAll(view);

        Assert.Contains("Add-ons", markup);
        Assert.Contains(RegistrationSubscriptionLabels.Defaults.SectionUsage, markup);
    }

    /// <summary>
    /// A cancellation says what it did, in the words every stack uses. The failed one says nothing
    /// here: it belongs in the error alert, not in a notice that reads like confirmation.
    /// </summary>
    [Theory]
    [InlineData(true, "Your cancellation is scheduled")]
    [InlineData(false, "Your subscription has been cancelled.")]
    public void A_cancellation_says_what_it_did(bool scheduled, string expected)
    {
        var view = View();
        SetField(view, "_cancelResult", new AppTierCancelResultModel
        {
            Success = true,
            IsScheduled = scheduled
        });

        Assert.Contains(expected, RenderText(view));
    }

    [Fact]
    public void A_refused_cancellation_shows_no_notice()
    {
        var view = View();
        SetField(view, "_cancelResult", new AppTierCancelResultModel { Success = false });

        Assert.DoesNotContain("ww-sub-cancel-notice", RenderAll(view));
    }

    /// <summary>
    /// A store keeps billing after the platform's own cancellation, so the instructions and the
    /// link travel with the notice.
    /// </summary>
    [Fact]
    public void A_store_billed_cancellation_carries_the_store_instructions()
    {
        var view = View();
        SetField(view, "_cancelResult", new AppTierCancelResultModel
        {
            Success = true,
            RequiresUserAction = true,
            UserActionUrl = "https://apps.apple.test/account"
        });

        var markup = RenderAll(view);

        Assert.Contains(ManageViewDecisions.StoreCancelInstructions, markup);
        Assert.Contains(ManageViewDecisions.StoreCancelLink, markup);
        Assert.Contains("https://apps.apple.test/account", markup);
    }

    #endregion

    #region What every mutation says

    /// <summary>
    /// A manage view wired to a scripted transport, so the handlers a panel calls can be exercised
    /// without a renderer. Nothing in the paths under test re-renders, so no render handle is
    /// needed.
    /// </summary>
    private sealed class Wired
    {
        public ScriptedHttpMessageHandler Handler { get; } = new();

        public List<EntitlementsChangedEventArgs> Entitlements { get; } = new();

        public List<string> Reasons { get; } = new();

        public List<ComponentErrorEventArgs> Errors { get; } = new();

        public int SubscriptionChanges { get; private set; }

        public RegistrationSubscriptionManage View { get; private set; } = default!;

        public RegistrationSubscriptionManage Build(object receiver)
        {
            Handler.On("my-subscription/cancel", """{"success":true,"isScheduled":true}""");
            Handler.DefaultJson = "{}";

            var appTier = new AppTierComponentService(
                Handler.CreateClient(), NullLogger<AppTierComponentService>.Instance);
            var entitlements = new FeatureEntitlementService(
                appTier, NullLogger<FeatureEntitlementService>.Instance);
            entitlements.EntitlementsChangedDetailed += args => Entitlements.Add(args);

            View = new RegistrationSubscriptionManage
            {
                AppId = "app-1",
                OnEntitlementsChanged = EventCallback.Factory.Create<string>(receiver, reason => Reasons.Add(reason)),
                OnSubscriptionChanged = EventCallback.Factory.Create(receiver, () => SubscriptionChanges++),
                OnError = EventCallback.Factory.Create<ComponentErrorEventArgs>(receiver, args => Errors.Add(args))
            };

            SetService(View, "AppTierService", appTier);
            SetService(View, "PaymentProviderService", new FakePaymentProviderService());
            SetService(View, "EntitlementService", entitlements);

            return View;
        }

        public Task InvokeAsync(string method, params object?[] args)
        {
            return (Task)typeof(RegistrationSubscriptionManage)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(View, args)!;
        }
    }

    /// <summary>
    /// Buying packs, cancelling one and taking a cancellation back are three different things, and
    /// each names its own reason: a host that cannot tell them apart cannot refresh the right
    /// thing.
    /// </summary>
    [Theory]
    [InlineData(EntitlementsChangedReasons.Cancel)]
    [InlineData(EntitlementsChangedReasons.Reactivate)]
    [InlineData(EntitlementsChangedReasons.AddOn)]
    public async Task Every_pack_action_names_its_own_reason_and_reloads(string reason)
    {
        var wired = new Wired();
        wired.Build(this);

        await wired.InvokeAsync("HandlePackChangedAsync", reason);

        Assert.Equal(reason, Assert.Single(wired.Entitlements).Reason);
        Assert.Equal(new[] { reason }, wired.Reasons);
        Assert.Equal(1, wired.SubscriptionChanges);
    }

    [Fact]
    public async Task Packs_bought_through_the_picker_are_announced_as_a_pack_purchase()
    {
        var wired = new Wired();
        wired.Build(this);

        await wired.InvokeAsync("HandlePacksBoughtAsync", new List<SignupPackOutcome>());

        Assert.Equal(EntitlementsChangedReasons.AddOn, Assert.Single(wired.Entitlements).Reason);
        Assert.Equal(1, wired.SubscriptionChanges);
    }

    [Fact]
    public async Task Cancelling_the_subscription_says_what_happened_and_drops_the_cache()
    {
        var wired = new Wired();
        var view = wired.Build(this);

        await wired.InvokeAsync("HandleCancelSubscriptionAsync");

        Assert.Equal(1, wired.Handler.Count("my-subscription/cancel"));
        Assert.Equal(EntitlementsChangedReasons.Cancel, Assert.Single(wired.Entitlements).Reason);
        Assert.Contains("Your cancellation is scheduled", RenderText(view));
        Assert.Empty(wired.Errors);
    }

    /// <summary>A refused cancellation must not look successful.</summary>
    [Fact]
    public async Task A_refused_cancellation_is_reported_and_changes_nothing()
    {
        var wired = new Wired();

        // The service reads a cancellation's outcome from the STATUS, not the body: any 2xx is a
        // cancellation that happened.
        wired.Handler.On("my-subscription/cancel", System.Net.HttpStatusCode.BadRequest, "Not yours to cancel.");
        var view = wired.Build(this);

        await wired.InvokeAsync("HandleCancelSubscriptionAsync");

        var error = Assert.Single(wired.Errors);
        Assert.Equal(ManageViewDecisions.CancelFailedCode, error.Context);
        Assert.Contains("Not yours to cancel.", error.Exception.Message);
        Assert.Empty(wired.Entitlements);
        Assert.DoesNotContain("ww-sub-cancel-notice", RenderAll(view));
    }

    #endregion

    #region The plan-change notice

    [Theory]
    [InlineData(PlanChangeStep.Authenticating, "Confirming the charge with your bank...")]
    [InlineData(PlanChangeStep.Completing, "Applying your new plan...")]
    public void The_notice_says_what_the_change_is_waiting_on(PlanChangeStep step, string expected)
    {
        var notice = new PlanChangeNotice { Step = step };

        var markup = RenderAll(notice);

        Assert.Contains(expected, markup);
        Assert.Contains("ww-regsub-plan-change", markup);
        Assert.Contains("role=\"status\"", markup);
    }

    [Fact]
    public void A_failed_change_is_never_silent()
    {
        var notice = new PlanChangeNotice
        {
            Step = PlanChangeStep.Failed,
            Error = "Your card was declined.",
            CanRetry = true
        };

        var markup = RenderAll(notice);

        Assert.Contains(RegistrationSubscriptionLabels.Defaults.PlanChangeFailed, markup);
        Assert.Contains("Your card was declined.", markup);
        Assert.Contains(RegistrationSubscriptionLabels.Defaults.TryAgain, markup);
        Assert.Contains("ww-regsub-plan-change-failed", markup);
    }

    [Fact]
    public void A_failure_that_cannot_be_retried_offers_only_the_dismissal()
    {
        var notice = new PlanChangeNotice { Step = PlanChangeStep.Failed, Error = "Gone." };

        var markup = RenderAll(notice);

        Assert.DoesNotContain(RegistrationSubscriptionLabels.Defaults.TryAgain, markup);
        Assert.Contains("ww-alert-dismiss", markup);
    }

    [Theory]
    [InlineData(PlanChangeStep.Idle)]
    [InlineData(PlanChangeStep.Confirm)]
    [InlineData(PlanChangeStep.Done)]
    public void The_notice_says_nothing_while_nothing_is_happening(PlanChangeStep step)
    {
        Assert.Equal(string.Empty, RenderAll(new PlanChangeNotice { Step = step }).Trim());
    }

    #endregion

    #region The card modal

    /// <summary>The modal with a session behind it, as the renderer would give it one.</summary>
    private static PaymentModal CardModal(PaymentRequiredArgs request, bool stacked = false)
    {
        var modal = new PaymentModal { AppId = "app-1", Request = request, Stacked = stacked };
        SetService(modal, "SessionManager", new FakeBlazorSessionManager());
        return modal;
    }

    [Fact]
    public void The_card_modal_names_the_plan_and_carries_the_test_hooks()
    {
        var modal = CardModal(new PaymentRequiredArgs { TierId = "tier-pro", TierName = "Pro" });

        var markup = RenderAll(modal);

        Assert.Contains("Upgrade to Pro", markup);
        Assert.Contains("data-ww-modal=\"payment\"", markup);
        Assert.Contains("ww-regsub-payment-modal", markup);
        Assert.Contains(RegistrationSubscriptionLabels.Defaults.ClosePayment, markup);
        Assert.Contains(typeof(WildwoodComponents.Blazor.Components.Payment.PaymentComponent), ChildComponents(modal));
    }

    /// <summary>
    /// Stacked, it renders over a confirmation that is still on screen. Off by default, exactly as
    /// React leaves its own <c>stacked</c> prop off.
    /// </summary>
    [Fact]
    public void The_card_modal_can_be_stacked_over_another()
    {
        var request = new PaymentRequiredArgs { TierName = "Pro" };

        Assert.DoesNotContain("ww-modal-overlay--stacked", RenderAll(CardModal(request)));
        Assert.Contains("ww-modal-overlay--stacked", RenderAll(CardModal(request, stacked: true)));
    }

    #endregion

    #region A feature an override grants

    private static FeaturesPanel Features(bool overridden, bool overrideEnabled = true)
    {
        var panel = new FeaturesPanel { AppId = "app-1" };

        SetField(panel, "_features", new List<AppFeatureDefinitionModel>
        {
            new AppFeatureDefinitionModel
            {
                FeatureCode = "REPORTS",
                DisplayName = "Reports",
                Category = "Analytics",
                IsEnabled = true
            }
        });
        SetField(panel, "_categories", new List<string> { "Analytics" });

        var overrides = new Dictionary<string, AppFeatureOverrideModel>();
        if (overridden)
        {
            overrides["REPORTS"] = new AppFeatureOverrideModel
            {
                FeatureCode = "REPORTS",
                IsEnabled = overrideEnabled
            };
        }

        SetField(panel, "_overrideMap", overrides);
        return panel;
    }

    /// <summary>
    /// A feature an override GRANTS is not part of the plan, so "Enabled" alone reads as if the
    /// plan carried it. Everyone is told — not just an admin.
    /// </summary>
    [Fact]
    public void A_feature_an_override_grants_reads_as_included()
    {
        var markup = RenderAll(Features(overridden: true));

        Assert.Contains(RegistrationSubscriptionLabels.Defaults.FeatureIncluded, markup);
        Assert.Contains("ww-badge ww-badge-info ww-feature-included", markup);
    }

    [Fact]
    public void A_feature_the_plan_itself_carries_says_nothing_about_inclusion()
    {
        Assert.DoesNotContain("ww-feature-included", RenderAll(Features(overridden: false)));
    }

    /// <summary>An override that takes a feature AWAY is not an inclusion.</summary>
    [Fact]
    public void A_revoking_override_is_not_an_inclusion()
    {
        Assert.DoesNotContain(
            "ww-feature-included", RenderAll(Features(overridden: true, overrideEnabled: false)));
    }

    [Fact]
    public void The_included_badge_takes_the_hosts_word_for_it()
    {
        var panel = Features(overridden: true);
        panel.IncludedLabel = "On your account";

        Assert.Contains("On your account", RenderAll(panel));
    }

    #endregion
}
