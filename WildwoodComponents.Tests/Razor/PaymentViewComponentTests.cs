using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Components.Payment;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The view model PaymentViewComponent hands its view. The .cshtml is not rendered (see
/// <see cref="NoViewEngine"/>), so these tests pin the server-side decisions: the free-trial copy,
/// and the fact that every amount the component shows — including the labels
/// <c>wwwroot/js/payment.js</c> swaps between — is formatted here rather than in the browser.
/// </summary>
public class PaymentViewComponentTests
{
    /// <summary>
    /// Stands in for the composite view engine. <c>ViewComponent.View(...)</c> otherwise resolves
    /// one from <c>HttpContext.RequestServices</c>, which no request-free test has.
    /// </summary>
    private sealed class NoViewEngine : ICompositeViewEngine
    {
        public IReadOnlyList<IViewEngine> ViewEngines => Array.Empty<IViewEngine>();

        public ViewEngineResult FindView(ActionContext context, string viewName, bool isMainPage) =>
            ViewEngineResult.NotFound(viewName, Array.Empty<string>());

        public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage) =>
            ViewEngineResult.NotFound(viewPath, Array.Empty<string>());
    }

    /// <summary>
    /// Only provider discovery matters here; every write method throws so a test that reaches one
    /// fails loudly instead of silently passing.
    /// </summary>
    private sealed class FakePaymentService : IWildwoodPaymentService
    {
        private readonly PlatformFilteredProvidersDto? _providers;

        public FakePaymentService(PlatformFilteredProvidersDto? providers) => _providers = providers;

        public Task<AppPaymentConfigurationDto?> GetAppPaymentConfigurationAsync(string appId) =>
            Task.FromResult<AppPaymentConfigurationDto?>(null);

        public Task<PlatformFilteredProvidersDto?> GetAvailableProvidersAsync(string appId) =>
            Task.FromResult(_providers);

        public Task<InitiatePaymentResponse> InitiatePaymentAsync(InitiatePaymentRequest request) =>
            throw new NotSupportedException("The ViewComponent never initiates a payment.");

        public Task<PaymentCompletionResult> ConfirmPaymentAsync(string paymentIntentId, PaymentProviderType providerType) =>
            throw new NotSupportedException("The ViewComponent never confirms a payment.");

        public Task<List<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(string customerId) =>
            Task.FromResult(new List<SavedPaymentMethodDto>());

        public Task<bool> DeleteSavedPaymentMethodAsync(string paymentMethodId) => Task.FromResult(false);

        public Task<bool> SetDefaultPaymentMethodAsync(string paymentMethodId) => Task.FromResult(false);

        public Task<PaymentCompletionResult?> GetPaymentStatusAsync(string transactionId) =>
            Task.FromResult<PaymentCompletionResult?>(null);

        public Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null) =>
            Task.FromResult(false);
    }

    private static PaymentViewComponent CreateComponent(PlatformFilteredProvidersDto? providers = null)
    {
        return new PaymentViewComponent(
            new FakePaymentService(providers),
            NullLogger<PaymentViewComponent>.Instance)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext
                {
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                }
            },
            ViewEngine = new NoViewEngine()
        };
    }

    private static async Task<PaymentViewModel> InvokeAsync(
        decimal amount = 99m,
        int? trialDays = null,
        string currency = "USD",
        string? pricingModelId = null,
        bool isSubscription = false)
    {
        var stripe = new PaymentProviderDto
        {
            Id = "provider-1",
            Name = "Stripe",
            ProviderType = 1,
            IsEnabled = true,
            PublishableKey = "pk_test"
        };

        var result = await CreateComponent().InvokeAsync(
            appId: "app-1",
            amount: amount,
            currency: currency,
            pricingModelId: pricingModelId,
            isSubscription: isSubscription,
            trialDays: trialDays,
            preloadedProviders: new List<PaymentProviderDto> { stripe },
            preselectedProviderId: stripe.Id);

        var view = Assert.IsType<ViewViewComponentResult>(result);
        return Assert.IsType<PaymentViewModel>(view.ViewData!.Model);
    }

    [Fact]
    public async Task Trial_days_reach_the_view()
    {
        var model = await InvokeAsync(trialDays: 14, isSubscription: true, pricingModelId: "pm-1");

        Assert.Equal(14, model.TrialDays);
        Assert.True(model.HasTrial);
        Assert.Equal("pm-1", model.PricingModelId);
        Assert.True(model.IsSubscription);
    }

    [Fact]
    public async Task A_trial_offers_the_trial_instead_of_a_charge()
    {
        var model = await InvokeAsync(amount: 99m, trialDays: 14);
        var amount = FormatHelpers.FormatMoney(99m, "USD");

        Assert.Equal("14-day free trial", model.TrialLabel);
        Assert.Equal("Start 14-day free trial", model.PayButtonLabel);
        Assert.Equal(
            $"You won't be charged today. {amount} is due when the trial ends unless you cancel before then.",
            model.TrialChargeNote);
    }

    [Fact]
    public async Task No_trial_leaves_the_button_on_the_charge()
    {
        var model = await InvokeAsync(amount: 99m);

        Assert.False(model.HasTrial);
        Assert.Equal(string.Empty, model.TrialLabel);
        Assert.Equal($"Pay {FormatHelpers.FormatMoney(99m, "USD")}", model.PayButtonLabel);
    }

    [Fact]
    public async Task Zero_trial_days_is_no_trial()
    {
        var model = await InvokeAsync(amount: 99m, trialDays: 0);

        Assert.False(model.HasTrial);
        Assert.Equal($"Pay {FormatHelpers.FormatMoney(99m, "USD")}", model.PayButtonLabel);
    }

    /// <summary>
    /// The script swaps the button to this when the server says the offered trial is not available,
    /// so the charge label has to exist even while the trial is being offered.
    /// </summary>
    [Fact]
    public async Task The_charge_label_is_rendered_alongside_the_trial_offer()
    {
        var model = await InvokeAsync(amount: 99m, trialDays: 14);

        Assert.Equal($"Pay {FormatHelpers.FormatMoney(99m, "USD")}", model.ChargeButtonLabel);
        Assert.NotEqual(model.PayButtonLabel, model.ChargeButtonLabel);
    }

    [Fact]
    public async Task The_trial_unavailable_notice_names_what_will_be_charged()
    {
        var model = await InvokeAsync(amount: 99m, trialDays: 14);
        var amount = FormatHelpers.FormatMoney(99m, "USD");

        Assert.Equal(
            $"The free trial isn't available on your account, so {amount} will be charged today. Select Pay to continue.",
            model.TrialUnavailableNotice);
    }

    /// <summary>
    /// Money is formatted once, server-side, in the payment's own currency — payment.js only ever
    /// swaps between strings this model produced.
    /// </summary>
    [Fact]
    public async Task Copy_is_formatted_in_the_payments_currency()
    {
        var model = await InvokeAsync(amount: 49.5m, trialDays: 7, currency: "EUR");
        var amount = FormatHelpers.FormatMoney(49.5m, "EUR");

        Assert.StartsWith("€", amount);
        Assert.Equal(amount, model.AmountDisplay);
        Assert.Contains(amount, model.TrialChargeNote);
        Assert.Contains(amount, model.TrialUnavailableNotice);
        Assert.Equal($"Pay {amount}", model.ChargeButtonLabel);
    }
}
