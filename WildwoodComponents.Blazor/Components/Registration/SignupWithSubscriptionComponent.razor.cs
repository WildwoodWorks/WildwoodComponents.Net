using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using static WildwoodComponents.Blazor.Components.Registration.TokenRegistrationComponent;

namespace WildwoodComponents.Blazor.Components.Registration
{
    public partial class SignupWithSubscriptionComponent : BaseWildwoodComponent
    {
        #region Injected Services

        [Inject] private IAuthenticationService AuthService { get; set; } = default!;
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;
        [Inject] private IFeatureEntitlementService EntitlementService { get; set; } = default!;
        [Inject] private IDisclaimerService DisclaimerService { get; set; } = default!;

        /// <summary>
        /// The four server calls that make the account, shared with the newer
        /// <c>RegistrationSubscriptionSignup</c> view so both create accounts by one route. It also
        /// owns the Campaign Attribution payload this wizard used to attach itself.
        /// </summary>
        [Inject] private ISignupAccountCreator AccountCreator { get; set; } = default!;

        [Inject] private new ILogger<SignupWithSubscriptionComponent> Logger { get; set; } = default!;

        #endregion

        #region Parameters

        [Parameter, EditorRequired]
        public string AppId { get; set; } = string.Empty;

        [Parameter] public string? PreSelectedTierId { get; set; }

        /// <summary>
        /// Pricing option to preselect within the pre-selected tier (e.g. the annual option
        /// chosen on a pricing page). Falls back to the tier's default, then first, option.
        /// </summary>
        [Parameter] public string? PreSelectedPricingId { get; set; }

        [Parameter] public string? RegistrationToken { get; set; }
        [Parameter] public bool RequireToken { get; set; }
        [Parameter] public bool AllowOpenRegistration { get; set; } = true;

        /// <summary>
        /// Show the optional "Have a Registration Token?" card on the registration step.
        /// Default true. Set false to hide it for public signups.
        /// </summary>
        [Parameter] public bool ShowOptionalTokenEntry { get; set; } = true;

        /// <summary>
        /// If true, skips the tier selection step and goes directly to processing after registration.
        /// </summary>
        [Parameter] public bool SkipTierSelection { get; set; }

        /// <summary>
        /// If true, requires billing address during payment.
        /// </summary>
        [Parameter] public bool RequireBillingAddress { get; set; }

        [Parameter] public EventCallback OnComplete { get; set; }
        [Parameter] public EventCallback OnCancel { get; set; }

        #endregion

        #region State

        private SignupStep _currentStep = SignupStep.Register;
        private string? _selectedTierId;
        private string? _selectedPricingId;
        private RegistrationFormData? _collectedFormData;
        private string? _processingStatus;
        private string? _processingError;

        // Track completed sub-steps for retry logic. Owned by the shared account creator, which
        // reads it to resume rather than register the same person twice.
        private readonly SignupAccountAttempt _attempt = new SignupAccountAttempt();

        /// <summary>The sign-in response, once there is one. Carries the JWT and the new user's id.</summary>
        private AuthenticationResponse? AuthResponse => _attempt.AuthResponse;

        /// <summary>The account exists but its plan could not be started.</summary>
        private bool SubscriptionFailed => _attempt.SubscriptionFailed;

        /// <summary>
        /// The plan a registration token gives this app. Registering with the token subscribes the
        /// user to it, so the wizard skips plan selection and payment, and must not self-subscribe
        /// over it — that call replaced the subscription the token had just created.
        /// </summary>
        private RegistrationTokenAppGrant? _tokenGrant;

        // Payment tracking (deferred flow: payment before user creation)
        private string? _paymentTransactionId;
        private string? _paymentExternalId;

        // One signup at a time — see ProcessSignupAsync.
        private bool _signupInFlight;

        // Registration disclaimers gated in front of the Success step (populated
        // after login once the session JWT is stored)
        private List<PendingDisclaimerModel>? _pendingDisclaimers;

        // Pre-selected tier details
        private AppTierModel? _preSelectedTier;
        private AppTierPricingModel? _preSelectedTierPricing;
        private AppTierModel? _selectedTier;
        private AppTierPricingModel? _selectedPricing;
        private bool _tierLoading;
        private bool _showFullTierSelection;

        private enum SignupStep
        {
            Register,
            SelectTier,
            Payment,
            Processing,
            Disclaimers,
            Success
        }

        private class StepDef
        {
            public SignupStep Key { get; set; }
            public string Label { get; set; } = string.Empty;
            public int Number { get; set; }
        }

        private List<StepDef> _stepConfig = new();

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            _selectedTierId = PreSelectedTierId;

            // Fetch pre-selected tier details if provided
            if (!string.IsNullOrEmpty(PreSelectedTierId))
            {
                _tierLoading = true;
                StateHasChanged();

                try
                {
                    var tiers = await AppTierService.GetPublicTiersAsync(AppId);
                    AppTierModel? tier = null;
                    if (tiers != null)
                    {
                        foreach (var candidate in tiers)
                        {
                            if (string.Equals(candidate.Id, PreSelectedTierId, StringComparison.OrdinalIgnoreCase))
                            {
                                tier = candidate;
                                break;
                            }
                        }
                    }
                    if (tier != null)
                    {
                        _preSelectedTier = tier;
                        // Honor the pricing choice carried from the pricing page (e.g. annual),
                        // falling back to the tier's default and then its first option.
                        AppTierPricingModel? pricing = null;
                        if (!string.IsNullOrEmpty(PreSelectedPricingId))
                        {
                            foreach (var option in tier.PricingOptions)
                            {
                                if (string.Equals(option.Id, PreSelectedPricingId, StringComparison.OrdinalIgnoreCase))
                                {
                                    pricing = option;
                                    break;
                                }
                            }
                        }
                        if (pricing == null)
                        {
                            foreach (var option in tier.PricingOptions)
                            {
                                if (option.IsDefault)
                                {
                                    pricing = option;
                                    break;
                                }
                            }
                        }
                        if (pricing == null && tier.PricingOptions.Count > 0)
                            pricing = tier.PricingOptions[0];
                        _preSelectedTierPricing = pricing;
                        _selectedTier = tier;
                        _selectedPricing = _preSelectedTierPricing;
                        // Without this the pre-selected flow subscribes with a null pricing id
                        // and the server re-defaults, losing the monthly/annual choice.
                        _selectedPricingId = _preSelectedTierPricing?.Id;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to load pre-selected tier details");
                }
                finally
                {
                    _tierLoading = false;
                }
            }

            BuildStepConfig();
        }

        /// <summary>
        /// Whether the pre-selected tier is active (not overridden by full tier selection).
        /// </summary>
        private bool HasPreSelectedTier => _preSelectedTier != null && !_showFullTierSelection;

        /// <summary>
        /// Whether the registration token already decided the plan for this app.
        /// </summary>
        private bool HasTokenPlan => _tokenGrant != null;

        /// <summary>
        /// Whether tier selection should be skipped (explicitly, via a pre-selected tier, or
        /// because the token carries the plan).
        /// </summary>
        private bool EffectiveSkipTierSelection => SkipTierSelection || HasPreSelectedTier || HasTokenPlan;

        /// <summary>
        /// Whether the selected tier requires payment. A token's plan is already paid for (or free
        /// by grant), so it collects nothing.
        /// </summary>
        private bool RequiresPayment =>
            !HasTokenPlan
            && _selectedTier != null && !_selectedTier.IsFreeTier
            && _selectedPricing != null && _selectedPricing.Price > 0;

        /// <summary>
        /// Free-trial days on the plan being paid for, 0 when there is no trial or no payment.
        /// </summary>
        private int SelectedTrialDays => RequiresPayment ? (_selectedPricing?.TrialDays ?? 0) : 0;

        private void BuildStepConfig()
        {
            var stepNumber = 1;
            _stepConfig = new List<StepDef>
            {
                new StepDef { Key = SignupStep.Register, Label = "Create Account", Number = stepNumber++ },
            };

            if (!EffectiveSkipTierSelection)
            {
                _stepConfig.Add(new StepDef { Key = SignupStep.SelectTier, Label = "Choose Plan", Number = stepNumber++ });
            }

            if (RequiresPayment)
            {
                _stepConfig.Add(new StepDef { Key = SignupStep.Payment, Label = "Payment", Number = stepNumber++ });
            }

            _stepConfig.Add(new StepDef { Key = SignupStep.Success, Label = "Complete", Number = stepNumber });
        }

        #endregion

        #region Event Handlers

        private async Task HandleFormDataCollected(RegistrationFormData formData)
        {
            _collectedFormData = formData;

            // A token that carries a plan for this app decides the plan itself: registering with it
            // subscribes the user, so there is nothing to choose or pay for. When the details cannot
            // be read the wizard falls through to the normal flow — the token still grants access,
            // and "unreadable" must not be reported as "invalid".
            if (!string.IsNullOrEmpty(formData.Token))
            {
                _processingError = null;
                _processingStatus = "Checking your registration token...";
                _currentStep = SignupStep.Processing;
                StateHasChanged();

                RegistrationTokenDetails? details = null;
                try
                {
                    details = await AuthService.GetRegistrationTokenDetailsAsync(formData.Token!, AppId);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not read registration token details; continuing with the normal flow");
                }

                _tokenGrant = SignupPlanDecisions.FindGrantForApp(details, AppId);
                if (_tokenGrant != null)
                {
                    BuildStepConfig();
                    await ProcessSignupAsync();
                    return;
                }

                _processingStatus = null;
                _currentStep = SignupStep.Register;
            }
            else
            {
                _tokenGrant = null;
            }

            if (EffectiveSkipTierSelection)
            {
                if (RequiresPayment)
                {
                    _currentStep = SignupStep.Payment;
                    BuildStepConfig();
                }
                else
                {
                    await ProcessSignupAsync();
                    return;
                }
            }
            else
            {
                _currentStep = SignupStep.SelectTier;
            }
            StateHasChanged();
        }

        private async Task HandleTierSelected(PricingTierSelectedEventArgs args)
        {
            _selectedTierId = args.Tier.Id;
            _selectedPricingId = args.SelectedPricing?.Id;
            _selectedTier = args.Tier;
            _selectedPricing = args.SelectedPricing;

            // Check if this tier requires payment
            var isPaid = !args.Tier.IsFreeTier && args.SelectedPricing != null && args.SelectedPricing.Price > 0;
            if (isPaid)
            {
                BuildStepConfig();
                _currentStep = SignupStep.Payment;
                StateHasChanged();
            }
            else
            {
                await ProcessSignupAsync();
            }
        }

        private async Task HandlePaymentSuccess(PaymentSuccessEventArgs args)
        {
            _paymentTransactionId = args.TransactionId ?? args.PaymentIntentId;
            _paymentExternalId = args.PaymentIntentId;

            if (_collectedFormData != null)
            {
                await ProcessSignupAsync();
            }
        }

        /// <summary>
        /// The payment component's Continue button. Payment success already starts the signup, so
        /// this only has work to do if the wizard is somehow still on the payment step — and it can
        /// never start a second signup, because <see cref="ProcessSignupAsync"/> refuses to run twice
        /// at once.
        /// </summary>
        private async Task HandlePaymentContinue(PaymentSuccessEventArgs args)
        {
            if (_currentStep != SignupStep.Payment) return;

            _paymentTransactionId ??= args.TransactionId ?? args.PaymentIntentId;
            _paymentExternalId ??= args.PaymentIntentId;

            if (_collectedFormData != null)
            {
                await ProcessSignupAsync();
            }
        }

        private void HandlePaymentFailure(PaymentFailureEventArgs args)
        {
            // Stay on payment step — PaymentComponent shows its own error
            Logger.LogWarning("Payment failed: {Message}", args.ErrorMessage);
        }

        private void HandleChangePlan()
        {
            _showFullTierSelection = true;
            _currentStep = SignupStep.SelectTier;
            BuildStepConfig();
            StateHasChanged();
        }

        private void HandleBack()
        {
            if (_currentStep == SignupStep.SelectTier)
            {
                _currentStep = SignupStep.Register;
                if (_preSelectedTier != null)
                {
                    _showFullTierSelection = false;
                    BuildStepConfig();
                }
            }
            else if (_currentStep == SignupStep.Payment)
            {
                _currentStep = EffectiveSkipTierSelection ? SignupStep.Register : SignupStep.SelectTier;
            }
            StateHasChanged();
        }

        private async Task HandleCancel()
        {
            if (OnCancel.HasDelegate)
            {
                await OnCancel.InvokeAsync();
            }
        }

        private async Task HandleComplete()
        {
            if (OnComplete.HasDelegate)
            {
                await OnComplete.InvokeAsync();
            }
        }

        private async Task HandleRetry()
        {
            _processingError = null;
            await ProcessSignupAsync();
        }

        private void HandleStartOver()
        {
            _currentStep = SignupStep.Register;
            _collectedFormData = null;
            _processingError = null;
            _processingStatus = null;
            // A fresh start must not carry the previous attempt's payment, plan or outcome into the
            // next one: a stale grant would skip plan selection, and a stale failure flag would make
            // a healthy signup read "Plan activation is pending".
            _attempt.Reset();
            _paymentTransactionId = null;
            _paymentExternalId = null;
            _tokenGrant = null;
            _pendingDisclaimers = null;
            BuildStepConfig();
            StateHasChanged();
        }

        #endregion

        #region Processing

        private async Task ProcessSignupAsync()
        {
            // One signup at a time. Payment success and the success panel's Continue can both ask
            // for one, and two concurrent runs would register the same person twice.
            if (_signupInFlight) return;
            _signupInFlight = true;

            _currentStep = SignupStep.Processing;
            _processingError = null;
            StateHasChanged();

            try
            {
                if (_collectedFormData == null)
                {
                    _processingError = "Registration data not available. Please start over.";
                    StateHasChanged();
                    return;
                }

                // Steps 1-4 — register, sign in, attach the payment, start the plan — are the
                // shared account creator's, so this wizard and the newer
                // RegistrationSubscriptionSignup view create accounts by exactly one route. The
                // attempt is handed in so a retry resumes rather than registering again.
                var result = await AccountCreator.CreateAsync(
                    new SignupAccountRequest
                    {
                        AppId = AppId,
                        FormData = _collectedFormData,
                        TierId = _selectedTierId,
                        PricingId = _selectedPricingId,
                        PaymentTransactionId = _paymentTransactionId,
                        PaymentExternalId = _paymentExternalId,
                        TokenGrant = _tokenGrant,
                        OnStatus = status =>
                        {
                            _processingStatus = status;
                            StateHasChanged();
                        }
                    },
                    _attempt);

                if (!result.Success)
                {
                    _processingError = result.ErrorMessage;
                    StateHasChanged();
                    return;
                }

                // A new account is entitled to whatever it just signed up for — the token's
                // grant, the plan it bought, or the free tier. JS calls this reason "signup", and
                // it is the signup flow's only one: it fires once, here, whether the plan came
                // from a token grant or a self-subscribe, and even when neither happened, because
                // the account itself is new.
                EntitlementService.Invalidate(AppId, EntitlementsChangedReasons.Signup);

                // Step 5: Gate the terminal Success transition on any pending registration
                // disclaimers. The account exists and the session JWT is stored, so the
                // signed-in user reviews and accepts before signup is considered complete.
                GateSuccessOnPendingDisclaimers();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during signup processing");
                // Never empty: the error view only renders when there is a message, so a blank one
                // would leave the user on the spinner.
                _processingError = "An error occurred. Please try again.";
            }
            finally
            {
                _signupInFlight = false;
                _processingStatus = null;
                StateHasChanged();
            }
        }

        /// <summary>
        /// Fetches pending registration disclaimers for the newly signed-in user. If any are
        /// pending, advances to the Disclaimers step so they can be accepted; otherwise goes
        /// straight to Success. Failures are non-fatal and fall through to Success.
        /// </summary>
        private void GateSuccessOnPendingDisclaimers()
        {
            // Gate on the authenticated login/registration response, which already carries the
            // pending disclaimers (mirrors the React source + the sibling AuthenticationComponent).
            // Reading them directly avoids a second, fallible fetch that could fail OPEN and let a
            // user with pending disclaimers through on a transient error, and avoids a showOn filter
            // that could surface a different set than the auth system flagged.
            var pending = AuthResponse?.PendingDisclaimers;
            if (AuthResponse?.RequiresDisclaimerAcceptance == true && pending != null && pending.Count > 0)
            {
                _pendingDisclaimers = pending;
                _currentStep = SignupStep.Disclaimers;
                Logger.LogInformation("Registration disclaimers pending after signup: {Count}", pending.Count);
                return;
            }

            _currentStep = SignupStep.Success;
        }

        /// <summary>
        /// Called when the embedded DisclaimerComponent reports the user accepted the pending
        /// registration disclaimers. Persists the acceptances (non-fatal on failure) and
        /// advances to Success.
        /// </summary>
        private async Task HandleDisclaimersAccepted(List<DisclaimerAcceptanceResult> acceptances)
        {
            try
            {
                if (acceptances.Count > 0)
                {
                    // accept-bulk is [Authorize]; the signup response carries the JWT for the new user.
                    DisclaimerService.SetAuthToken(AuthResponse?.JwtToken);

                    var result = await DisclaimerService.AcceptDisclaimersAsync(AppId, acceptances);
                    if (!result.Success)
                    {
                        Logger.LogWarning("Failed to record disclaimer acceptances: {Message}", result.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error recording disclaimer acceptances after signup");
            }

            _currentStep = SignupStep.Success;
            StateHasChanged();
        }

        #endregion

        #region Helpers

        private int GetStepIndex(SignupStep step)
        {
            for (int i = 0; i < _stepConfig.Count; i++)
            {
                if (_stepConfig[i].Key == step) return i;
            }
            return 0;
        }

        private bool IsStepCompleted(SignupStep step)
        {
            return GetStepIndex(_currentStep) > GetStepIndex(step);
        }

        /// <summary>
        /// Gets the step index for the step indicator, mapping Processing to the Success position.
        /// </summary>
        private int GetDisplayStepIndex()
        {
            // Processing maps to the same position as Success in the indicator
            if (_currentStep == SignupStep.Processing)
            {
                return _stepConfig.Count - 1;
            }
            return GetStepIndex(_currentStep);
        }

        /// <summary>
        /// A plan's price in the plan's own currency, falling back to the USD this wizard has always
        /// quoted (React's signup component still hard-codes 'USD' here). It used to print a dollar
        /// sign for every currency it did not recognise, and a whole number for every price.
        /// </summary>
        private static string FormatPlanPrice(AppTierModel? tier, decimal amount)
        {
            return CatalogHelpers.FormatPrice(tier, amount, "USD");
        }

        #endregion
    }
}
