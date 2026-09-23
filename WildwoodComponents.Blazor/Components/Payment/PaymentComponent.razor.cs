using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Blazor.Services.Payment;

namespace WildwoodComponents.Blazor.Components.Payment;

/// <summary>
/// Multi-provider payment component. Discovers the payment providers an app offers on the
/// current platform, renders the selected provider's form and drives the payment to completion.
/// </summary>
/// <remarks>
/// Markup lives in PaymentComponent.razor. The C# is split into partial class files:
/// - PaymentComponent.razor.cs - Core: parameters, injected services, fields, lifecycle,
///   provider discovery, the non-Stripe provider flows, shared handlers and disposal
/// - PaymentComponent.Stripe.cs - Stripe: card element initialisation and teardown, payment
///   confirmation and the JS-invokable card callbacks
/// - PaymentDecisions.cs - the rules that decide whether a card is charged, saved or left alone
/// </remarks>
public partial class PaymentComponent : BaseWildwoodComponent, IAsyncDisposable
{
    [Inject] private IPaymentProviderService PaymentProviderService { get; set; } = default!;
    [Inject] private IPlatformDetectionService PlatformService { get; set; } = default!;
    [Inject] private PaymentScriptLoader ScriptLoader { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    // Required Parameters
    [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;
    [Parameter, EditorRequired] public decimal Amount { get; set; }
    
    // Optional Parameters
    [Parameter] public string Currency { get; set; } = "USD";
    [Parameter] public string? Description { get; set; }
    [Parameter] public string? CustomerId { get; set; }
    [Parameter] public string? CustomerEmail { get; set; }
    [Parameter] public string? OrderId { get; set; }
    [Parameter] public string? SubscriptionId { get; set; }
    [Parameter] public string? PricingModelId { get; set; }
    [Parameter] public bool IsSubscription { get; set; }

    /// <summary>
    /// The subscription starts with this many free-trial days. The button offers the trial instead
    /// of a charge, and with Stripe the card is saved (confirmed as a SetupIntent) rather than
    /// charged today, so the processor has something to bill when the trial ends.
    /// </summary>
    [Parameter] public int? TrialDays { get; set; }

    [Parameter] public bool ShowAmount { get; set; } = true;
    [Parameter] public bool RequireBillingAddress { get; set; } = false;
    [Parameter] public string? ReturnUrl { get; set; }
    [Parameter] public string? CancelUrl { get; set; }
    [Parameter] public Dictionary<string, string>? Metadata { get; set; }
    
    /// <summary>
    /// Optional: Pre-loaded providers to use instead of fetching from API.
    /// When provided, the component skips the API discovery call.
    /// Useful for admin scenarios where the provider is already selected.
    /// </summary>
    [Parameter] public List<PaymentProviderDto>? PreloadedProviders { get; set; }
    
    /// <summary>
    /// Optional: ID of the provider to pre-select when using PreloadedProviders.
    /// </summary>
    [Parameter] public string? PreselectedProviderId { get; set; }
    
    // Events

    /// <summary>
    /// Fired exactly once per successful payment, as soon as the payment completes.
    /// </summary>
    [Parameter] public EventCallback<PaymentSuccessEventArgs> OnPaymentSuccess { get; set; }

    /// <summary>
    /// Renders a "Continue" button on the success panel and is invoked when it is clicked. Without
    /// it the panel offers no Continue, because advancing used to re-fire
    /// <see cref="OnPaymentSuccess"/> and run the host's success handler (a signup, an upgrade) a
    /// second time — with a thinner payload that dropped the payment intent and subscription ids.
    /// </summary>
    [Parameter] public EventCallback<PaymentSuccessEventArgs> OnContinue { get; set; }

    [Parameter] public EventCallback<PaymentFailureEventArgs> OnPaymentFailure { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    // Private state
    private List<PaymentProviderDto> _availableProviders = new();
    private PaymentProviderDto? _selectedProvider;
    private PlatformFilteredProvidersDto? _platformInfo;
    private PaymentCompletionResult? _paymentResult;
    private bool _paymentComplete;
    private bool _isProcessing;
    private string? _loadingMessage;
    private DotNetObjectReference<PaymentComponent>? _dotNetRef;
    private bool _cardComplete;
    private bool _stripeInitialized;
    
    // Track which provider scripts we've loaded for cleanup
    private HashSet<PaymentProviderType> _loadedProviderScripts = new();
    private bool _scriptsLoaded;
    private bool _paypalButtonsLoaded;
    
    // Card form fields (for generic providers)
    private string _cardNumber = string.Empty;
    private string _cardExpiry = string.Empty;
    private string _cardCvv = string.Empty;
    
    // Billing address
    private string _billingFirstName = string.Empty;
    private string _billingLastName = string.Empty;
    private string _billingAddress = string.Empty;
    private string _billingCity = string.Empty;
    private string _billingState = string.Empty;
    private string _billingZip = string.Empty;
    // No country picker yet — the form collects a US address, and the value still travels with it
    // so the server and the provider get a complete address.
    private string _billingCountry = "US";

    // Inline problem with the form itself (an incomplete billing address), shown above the submit
    // button. Distinct from ErrorMessage, which replaces the whole form with the Try Again panel.
    private string? _formError;

    // The Stripe intent created for this payment, kept after a declined card so a retry confirms
    // the same intent instead of creating another subscription.
    private InitiatePaymentResponse? _pendingIntent;
    private string? _pendingIntentKey;

    // Set when the plan advertises a trial but the server started a paid subscription instead (the
    // account has already had its trial). The charge then waits for the user to agree to it.
    private bool _trialUnavailable;

    // Set when the completed payment saved a card for a free trial rather than charging it.
    private DateTime? _trialEndsAt;

    // Last plan the form was rendered for, so a form reused for another plan offers its trial again.
    private string? _lastPricingModelId;
    private int? _lastTrialDays;
    private decimal _lastAmount;
    private bool _planTracked;

    /// <summary>
    /// True while the component is offering a free trial rather than a charge.
    /// </summary>
    private bool HasTrial => (TrialDays ?? 0) > 0 && !_trialUnavailable;

    // Flag for tracking prevention detection
    private bool _trackingPreventionDetected = false;

    // Debug info for when providers aren't loaded
    private string? _noProvidersReason;
    private string? _debugInfo;
    
    // Enhanced debug info
    private List<string> _filteringDetails = new();
    private string? _apiResponseStatus;
    private int _providersFromApi = 0;
    private int _platformFlags = 0;

    protected override async Task OnComponentInitializedAsync()
    {
        await LoadAvailableProviders();
    }

    /// <summary>
    /// A "the trial isn't available" answer was about ONE plan. When the host reuses this form for
    /// another plan — a different pricing model, trial length or amount — the new plan's trial is
    /// offered again, and the previous plan's intent is dropped so nothing confirms against it.
    /// </summary>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (_planTracked
            && PaymentDecisions.PlanChanged(
                _lastPricingModelId, _lastTrialDays, _lastAmount,
                PricingModelId, TrialDays, Amount))
        {
            _trialUnavailable = false;
            _pendingIntent = null;
            _pendingIntentKey = null;
        }

        _lastPricingModelId = PricingModelId;
        _lastTrialDays = TrialDays;
        _lastAmount = Amount;
        _planTracked = true;
    }

    private bool _providersInitialized = false;
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        
        // Log initialization state on first render for debugging
        if (firstRender)
        {
            await LogToConsoleAsync($"[PaymentComponent] First render - AppId: {AppId}, Providers: {_availableProviders.Count}, Selected: {_selectedProvider?.Name ?? "none"}");
        }
        
        // Initialize providers after render - handles both first render and subsequent renders
        // This ensures DOM elements exist and scripts can be loaded
        if (!_providersInitialized && _availableProviders.Count > 0 && _selectedProvider is { } selectedProvider)
        {
            _providersInitialized = true;
            _dotNetRef = DotNetObjectReference.Create(this);

            Logger?.LogInformation("OnAfterRenderAsync: Initializing {Count} providers, selected: {Selected}",
                _availableProviders.Count, selectedProvider.Name);
            await LogToConsoleAsync($"[PaymentComponent] Initializing provider: {selectedProvider.Name}");

            // Load scripts for all available providers (multi-provider support)
            await LoadProviderScriptsAsync();

            // Small delay to ensure DOM is fully ready
            await Task.Delay(100);

            // Initialize the selected provider
            await InitializeProviderJs(selectedProvider);
        }
    }

    private async Task LoadProviderScriptsAsync()
    {
        if (_scriptsLoaded)
            return;

        foreach (var provider in _availableProviders)
        {
            var providerType = (PaymentProviderType)provider.ProviderType;
            
            // Only load scripts for providers that need client-side JS
            if (RequiresClientScript(providerType))
            {
                var loaded = await ScriptLoader.LoadAsync(providerType);
                if (loaded)
                {
                    _loadedProviderScripts.Add(providerType);
                    Logger?.LogDebug("Loaded script for provider: {Provider}", providerType);
                }
            }
        }

        _scriptsLoaded = true;
    }

    private static bool RequiresClientScript(PaymentProviderType providerType)
    {
        return providerType == PaymentProviderType.Stripe ||
               providerType == PaymentProviderType.PayPal ||
               providerType == PaymentProviderType.ApplePay ||
               providerType == PaymentProviderType.GooglePay;
    }

    private async Task LoadAvailableProviders()
    {
        // Reset debug info
        _filteringDetails.Clear();
        _apiResponseStatus = null;
        _providersFromApi = 0;
        _platformFlags = PlatformService.GetPlatformFlags();
        
        // Log to browser console via JS for easier debugging
        await LogToConsoleAsync($"[PaymentComponent] LoadAvailableProviders starting for AppId: {AppId}");
        await LogToConsoleAsync($"[PaymentComponent] Platform: {PlatformService.CurrentPlatform}, Flags: {_platformFlags} (0x{_platformFlags:X}), RequiresAppStore: {PlatformService.RequiresAppStorePayment}");
        
        // If providers are preloaded, use them directly (skip API call)
        if (PreloadedProviders != null && PreloadedProviders.Count > 0)
        {
            _availableProviders = PreloadedProviders;
            _providersFromApi = PreloadedProviders.Count;
            _apiResponseStatus = "Preloaded (skipped API call)";
            
            if (!string.IsNullOrEmpty(PreselectedProviderId))
            {
                _selectedProvider = FindProviderById(PreselectedProviderId);
            }
            
            if (_selectedProvider == null && _availableProviders.Count > 0)
            {
                _selectedProvider = _availableProviders[0];
            }
            
            Logger?.LogDebug("Using {Count} preloaded providers, selected: {SelectedId}", 
                _availableProviders.Count, _selectedProvider?.Id);
            await LogToConsoleAsync($"[PaymentComponent] Using {_availableProviders.Count} preloaded providers");
            return;
        }
        
        _loadingMessage = "Loading payment options...";
        await SetLoadingAsync(true);

        try
        {
            // Validate AppId
            if (string.IsNullOrEmpty(AppId))
            {
                _noProvidersReason = "Application ID is not configured.";
                _debugInfo = "AppId parameter is null or empty";
                _apiResponseStatus = "Not called - AppId missing";
                Logger?.LogError("LoadAvailableProviders: AppId is null or empty!");
                await LogToConsoleAsync("[PaymentComponent] ERROR: AppId is null or empty!");
                return;
            }
            
            Logger?.LogInformation("LoadAvailableProviders: Starting for AppId: '{AppId}'", AppId);
            Logger?.LogInformation("LoadAvailableProviders: Platform: {Platform}, RequiresAppStore: {RequiresAppStore}", 
                PlatformService.CurrentPlatform, PlatformService.RequiresAppStorePayment);
            
            _platformInfo = await PaymentProviderService.GetAvailableProvidersAsync(AppId);
            
            if (_platformInfo == null)
            {
                _noProvidersReason = "Unable to load payment configuration from server.";
                _debugInfo = $"API returned null response. Check server logs and network connectivity.";
                _apiResponseStatus = "Null response from API";
                Logger?.LogError("LoadAvailableProviders: API returned null for AppId: {AppId}", AppId);
                await LogToConsoleAsync($"[PaymentComponent] ERROR: API returned null for AppId: {AppId}");
                return;
            }
            
            _availableProviders = _platformInfo.AvailableProviders;
            _providersFromApi = _availableProviders.Count;
            _apiResponseStatus = $"Success - {_providersFromApi} provider(s) after filtering";
            
            Logger?.LogInformation("LoadAvailableProviders: Received {Count} providers for AppId: {AppId}", 
                _availableProviders.Count, AppId);
            await LogToConsoleAsync($"[PaymentComponent] Received {_availableProviders.Count} providers after filtering");
            await LogToConsoleAsync($"[PaymentComponent] Check server console logs for '[PaymentProviderService]' entries to see what API returned BEFORE filtering");
            
            // Log each provider for debugging
            foreach (var provider in _availableProviders)
            {
                var providerType = (PaymentProviderType)provider.ProviderType;
                var hasKey = !string.IsNullOrEmpty(provider.PublishableKey) || !string.IsNullOrEmpty(provider.ClientId);
                _filteringDetails.Add($"{provider.Name} (Type: {providerType}, Enabled: {provider.IsEnabled}, HasKey: {hasKey}, Platforms: {provider.AllowedPlatforms})");
                await LogToConsoleAsync($"[PaymentComponent]   Provider: {provider.Name}, Type: {providerType}, Enabled: {provider.IsEnabled}, AllowedPlatforms: {provider.AllowedPlatforms}, HasPublishableKey: {!string.IsNullOrEmpty(provider.PublishableKey)}, HasClientId: {!string.IsNullOrEmpty(provider.ClientId)}");
            }
            
            if (_availableProviders.Count == 0)
            {
                // Build debug info with more context - SERVER SIDE ISSUE
                var debugBuilder = new System.Text.StringBuilder();
                debugBuilder.AppendLine("SERVER-SIDE CONFIGURATION ISSUE:");
                debugBuilder.AppendLine("The WildwoodAPI returned 0 payment providers for this app.");
                debugBuilder.AppendLine("");
                debugBuilder.AppendLine("Possible causes:");
                debugBuilder.AppendLine("1. No payment providers configured for the app's company");
                debugBuilder.AppendLine("2. Providers exist but none are enabled (IsEnabled=false)");
                debugBuilder.AppendLine("3. Providers exist but none have Web platform in AllowedPlatforms");
                debugBuilder.AppendLine("");
                debugBuilder.AppendLine("To fix:");
                debugBuilder.AppendLine("1. Go to WildwoodAdmin");
                debugBuilder.AppendLine("2. Navigate to Company > Payment Providers");
                debugBuilder.AppendLine("3. Add/configure a payment provider (e.g., Stripe, PayPal)");
                debugBuilder.AppendLine("4. Ensure the provider is Enabled");
                debugBuilder.AppendLine("5. Ensure AllowedPlatforms includes 'Web' (flag 1)");
                
                _noProvidersReason = "No payment providers are configured for this application. Please configure payment providers in the admin panel.";
                _debugInfo = debugBuilder.ToString();
                
                Logger?.LogWarning("LoadAvailableProviders: API returned 0 providers - this is a SERVER-SIDE configuration issue. " +
                    "AppId: {AppId}, Platform: {Platform}. Check that payment providers are configured and enabled for the app's company.", 
                    AppId, _platformInfo.Platform);
                await LogToConsoleAsync($"[PaymentComponent] WARNING: API returned 0 providers - SERVER-SIDE CONFIGURATION ISSUE");
                await LogToConsoleAsync($"[PaymentComponent] Check that payment providers are configured in WildwoodAdmin for the company associated with AppId: {AppId}");
                return;
            }
            
            // Log each provider's details for debugging
            foreach (var provider in _availableProviders)
            {
                var providerType = (PaymentProviderType)provider.ProviderType;
                Logger?.LogInformation("  Provider: {Name} (Type: {Type}, Id: {Id})", 
                    provider.Name, providerType, provider.Id);
            }
            
            // Auto-select default or first provider
            if (_platformInfo.RequiredProviderId != null)
            {
                _selectedProvider = FindProviderById(_platformInfo.RequiredProviderId);
                Logger?.LogInformation("Required provider selected: {ProviderId}", _platformInfo.RequiredProviderId);
            }
            else if (_platformInfo.DefaultProvider != null)
            {
                _selectedProvider = _platformInfo.DefaultProvider;
                Logger?.LogInformation("Default provider selected: {ProviderId}", _selectedProvider?.Id);
            }
            else if (_availableProviders.Count > 0)
            {
                _selectedProvider = _availableProviders[0];
                Logger?.LogInformation("First provider selected: {ProviderId}", _selectedProvider?.Id);
            }
            
            if (_selectedProvider != null)
            {
                var selectedType = (PaymentProviderType)_selectedProvider.ProviderType;
                Logger?.LogInformation("SELECTED: {ProviderName} (Type: {Type})", 
                    _selectedProvider.Name, selectedType);
                await LogToConsoleAsync($"[PaymentComponent] Selected provider: {_selectedProvider.Name} (Type: {selectedType})");
            }
        }
        catch (Exception ex)
        {
            _noProvidersReason = "An error occurred while loading payment options.";
            _debugInfo = $"Exception: {ex.GetType().Name}\nMessage: {ex.Message}";
            _apiResponseStatus = $"Exception: {ex.GetType().Name}";
            Logger?.LogError(ex, "LoadAvailableProviders: Failed for AppId: {AppId}", AppId);
            await LogToConsoleAsync($"[PaymentComponent] ERROR: {ex.Message}");
            await HandleErrorAsync(ex, "Loading payment providers");
        }
        finally
        {
            await SetLoadingAsync(false);
        }
    }

    private async Task InitializeProviderJs(PaymentProviderDto provider)
    {
        try
        {
            var providerType = (PaymentProviderType)provider.ProviderType;
            Logger?.LogInformation("InitializeProviderJs: Starting for {ProviderType}", providerType);
            await LogToConsoleAsync($"[PaymentComponent] InitializeProviderJs: Starting for {providerType}");
            
            // Ensure script is loaded for this provider
            if (!ScriptLoader.IsLoaded(providerType) && RequiresClientScript(providerType))
            {
                Logger?.LogInformation("InitializeProviderJs: Loading script for {ProviderType}...", providerType);
                await LogToConsoleAsync($"[PaymentComponent] Loading script for {providerType}...");
                var loaded = await ScriptLoader.LoadAsync(providerType);
                if (loaded)
                {
                    _loadedProviderScripts.Add(providerType);
                    Logger?.LogInformation("InitializeProviderJs: Script loaded for {ProviderType}", providerType);
                    await LogToConsoleAsync($"[PaymentComponent] Script loaded for {providerType}");
                }
                else
                {
                    Logger?.LogError("InitializeProviderJs: FAILED to load script for {ProviderType}", providerType);
                    await LogToConsoleAsync($"[PaymentComponent] ERROR: FAILED to load script for {providerType}");
                    await HandleErrorAsync(new Exception($"Failed to load {providerType} payment script"), "Script loading");
                    return;
                }
            }
            else
            {
                Logger?.LogInformation("InitializeProviderJs: Script already loaded for {ProviderType}", providerType);
                await LogToConsoleAsync($"[PaymentComponent] Script already loaded for {providerType}");
            }
            
            // Initialize the provider with its configuration
            var refKey = $"payment-{ComponentId}-{providerType}";
            
            switch (providerType)
            {
                case PaymentProviderType.Stripe:
                    await InitializeStripeAsync(provider, refKey);
                    break;

                case PaymentProviderType.PayPal:
                    if (!string.IsNullOrEmpty(provider.ClientId))
                    {
                        Logger?.LogInformation("InitializeProviderJs: Calling wildwoodPayment.initPayPal with ClientId: {ClientIdPrefix}...", 
                            provider.ClientId.Length > 10 ? provider.ClientId.Substring(0, 10) : provider.ClientId);
                        await LogToConsoleAsync($"[PaymentComponent] Initializing PayPal with ClientId: {provider.ClientId.Substring(0, Math.Min(10, provider.ClientId.Length))}...");
                        Logger?.LogInformation("InitializeProviderJs: PayPal options - IsSubscription: {IsSubscription}, SupportsApplePay: {ApplePay}, SupportsGooglePay: {GooglePay}",
                            IsSubscription, provider.SupportsApplePay, provider.SupportsGooglePay);
                        _dotNetRef ??= DotNetObjectReference.Create(this);
                        
                        try
                        {
                            // Build PayPal options including subscription and funding source info
                            var paypalOptions = new Dictionary<string, object>
                            {
                                ["isSubscription"] = IsSubscription,
                                ["supportsApplePay"] = provider.SupportsApplePay,
                                ["supportsGooglePay"] = provider.SupportsGooglePay,
                                ["description"] = Description ?? string.Empty
                            };
                            
                            var result = await InvokeJSAsync<bool>("wildwoodPayment.initPayPal", 
                                provider.ClientId, "paypal-button-container", Amount, Currency, _dotNetRef, refKey, paypalOptions);
                            _paypalButtonsLoaded = result;
                            Logger?.LogInformation("InitializeProviderJs: PayPal initPayPal returned: {Result}", result);
                            await LogToConsoleAsync($"[PaymentComponent] PayPal initialization result: {result}");
                            
                            if (!result)
                            {
                                Logger?.LogWarning("InitializeProviderJs: PayPal initialization returned false");
                                await HandleErrorAsync(new Exception("PayPal buttons failed to initialize. Check browser console for errors."), "PayPal initialization");
                            }
                        }
                        catch (Exception jsEx)
                        {
                            Logger?.LogError(jsEx, "InitializeProviderJs: JavaScript error calling initPayPal");
                            await LogToConsoleAsync($"[PaymentComponent] ERROR: PayPal JavaScript error: {jsEx.Message}");
                            await HandleErrorAsync(new Exception($"PayPal JavaScript error: {jsEx.Message}"), "PayPal initialization");
                        }
                        
                        StateHasChanged();
                    }
                    else
                    {
                        Logger?.LogWarning("InitializeProviderJs: PayPal provider missing ClientId - provider ID: {ProviderId}, Name: {Name}", 
                            provider.Id, provider.Name);
                        await LogToConsoleAsync("[PaymentComponent] ERROR: PayPal provider missing ClientId");
                        _paypalButtonsLoaded = false;
                        await HandleErrorAsync(new InvalidOperationException("PayPal is not properly configured (missing ClientId). Please contact support."), "PayPal configuration");
                    }
                    break;
                    
                case PaymentProviderType.ApplePay:
                    _dotNetRef ??= DotNetObjectReference.Create(this);
                    await LogToConsoleAsync("[PaymentComponent] Initializing Apple Pay...");
                    await InvokeJSVoidAsync("wildwoodPayment.initApplePay", 
                        provider.MerchantId, Amount, Currency, Description ?? "Payment", _dotNetRef, refKey);
                    Logger?.LogInformation("InitializeProviderJs: Apple Pay initialized");
                    await LogToConsoleAsync("[PaymentComponent] Apple Pay initialized");
                    break;
                    
                case PaymentProviderType.GooglePay:
                    _dotNetRef ??= DotNetObjectReference.Create(this);
                    await LogToConsoleAsync("[PaymentComponent] Initializing Google Pay...");
                    await InvokeJSVoidAsync("wildwoodPayment.initGooglePay", 
                        provider.MerchantId, Amount, Currency, 
                        provider.IsSandboxMode ? "TEST" : "PRODUCTION",
                        provider.PublishableKey, _dotNetRef, refKey);
                    Logger?.LogInformation("InitializeProviderJs: Google Pay initialized");
                    await LogToConsoleAsync("[PaymentComponent] Google Pay initialized");
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "InitializeProviderJs: Failed to initialize payment provider JS for {ProviderType}", 
                (PaymentProviderType)provider.ProviderType);
            await LogToConsoleAsync($"[PaymentComponent] ERROR: Failed to initialize provider: {ex.Message}");
            await HandleErrorAsync(ex, $"Initializing {(PaymentProviderType)provider.ProviderType}");
        }
    }

    /// <summary>
    /// Logs a message to the browser console for debugging
    /// </summary>
    private async Task LogToConsoleAsync(string message)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("console.log", message);
        }
        catch
        {
            // Ignore logging failures - console may not be available
        }
    }

    private PaymentProviderDto? FindProviderById(string id)
    {
        foreach (var provider in _availableProviders)
        {
            if (provider.Id == id)
                return provider;
        }
        return null;
    }

    private async Task SelectProvider(PaymentProviderDto provider)
    {
        _selectedProvider = provider;
        _cardComplete = false;
        _stripeInitialized = false;
        _paypalButtonsLoaded = false;
        _formError = null;
        // Another provider means another intent: the stored one belongs to the provider that made it.
        _pendingIntent = null;
        _pendingIntentKey = null;
        ClearError();
        StateHasChanged();
        
        // Initialize JS for the new provider
        await Task.Delay(100); // Allow DOM to update
        await InitializeProviderJs(provider);
    }

    /// <summary>
    /// Called from JavaScript when PayPal payment is approved
    /// </summary>
    [JSInvokable]
    public async Task OnPayPalApproved(PayPalApprovalResult result)
    {
        try
        {
            Logger?.LogInformation("PayPal payment approved. OrderId: {OrderId}, TransactionId: {TransactionId}", 
                result.OrderId, result.TransactionId);

            _paymentResult = new PaymentCompletionResult
            {
                Success = true,
                TransactionId = result.TransactionId,
                PaymentIntentId = result.OrderId
            };
            _paymentComplete = true;
            
            await NotifyPaymentSuccess();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error handling PayPal approval");
            await HandlePaymentError("Failed to process PayPal payment", "paypal_error");
        }
    }

    /// <summary>
    /// Called from JavaScript when PayPal payment is cancelled
    /// </summary>
    [JSInvokable]
    public void OnPayPalCancelled(PayPalCancelResult result)
    {
        Logger?.LogInformation("PayPal payment cancelled. OrderId: {OrderId}", result.OrderId);
        StateHasChanged();
    }

    /// <summary>
    /// Called from JavaScript when PayPal encounters an error
    /// </summary>
    [JSInvokable]
    public async Task OnPayPalError(PayPalErrorResult result)
    {
        Logger?.LogWarning("PayPal error: {ErrorMessage}", result.ErrorMessage);
        await HandlePaymentError(result.ErrorMessage ?? "PayPal payment failed", "paypal_error");
        StateHasChanged();
    }

    // PayPal JS callback models
    public class PayPalApprovalResult
    {
        public string? OrderId { get; set; }
        public string? PayerId { get; set; }
        public string? TransactionId { get; set; }
    }

    public class PayPalCancelResult
    {
        public string? OrderId { get; set; }
    }

    public class PayPalErrorResult
    {
        public string? ErrorMessage { get; set; }
    }

    private async Task ProcessPayment()
    {
        if (_selectedProvider == null || _isProcessing)
            return;

        var isStripe = (PaymentProviderType)_selectedProvider.ProviderType == PaymentProviderType.Stripe;

        // An address the app requires has to be complete before anything is charged: an incomplete
        // one fails at the provider, after the intent already exists.
        if (!PaymentDecisions.TryBuildBillingAddress(
                RequireBillingAddress,
                _billingFirstName, _billingLastName, _billingAddress,
                _billingCity, _billingState, _billingZip, _billingCountry,
                out var billingAddress))
        {
            _formError = PaymentDecisions.IncompleteBillingAddressMessage;
            StateHasChanged();
            return;
        }

        _formError = null;
        _isProcessing = true;
        _loadingMessage = "Processing payment...";
        StateHasChanged();

        try
        {
            // Reuse the intent a declined attempt already created rather than initiating again,
            // which would leave a second subscription behind.
            var intentKey = PaymentDecisions.BuildIntentKey(
                _selectedProvider.Id, PricingModelId, Amount, IsSubscription);

            InitiatePaymentResponse response;
            if (PaymentDecisions.CanReuseIntent(_pendingIntentKey, _pendingIntent, intentKey))
            {
                response = _pendingIntent!;
            }
            else
            {
                var request = new InitiatePaymentRequest
                {
                    ProviderId = _selectedProvider.Id,
                    AppId = AppId,
                    Amount = Amount,
                    Currency = Currency,
                    Description = Description,
                    CustomerId = CustomerId,
                    CustomerEmail = CustomerEmail,
                    OrderId = OrderId,
                    SubscriptionId = SubscriptionId,
                    PricingModelId = PricingModelId,
                    IsSubscription = IsSubscription,
                    ReturnUrl = ReturnUrl,
                    CancelUrl = CancelUrl,
                    Metadata = Metadata,
                    // Only present when the app asks for an address — the property is omitted otherwise.
                    BillingAddress = billingAddress,
                    // Lets a Stripe trial come back as a SetupIntent to confirm, so the card is
                    // saved for the charge at trial end instead of nothing being collected at all.
                    SupportsSetupIntent = PaymentDecisions.ShouldRequestSetupIntent(isStripe, HasTrial)
                        ? true
                        : null
                };

                response = await PaymentProviderService.InitiatePaymentAsync(request);
                Logger?.LogInformation("ProcessPayment: Payment initiation response: {Response}", response);

                if (!response.Success)
                {
                    await HandlePaymentError(response.ErrorMessage ?? "Payment initiation failed", response.ErrorCode);
                    return;
                }

                if (isStripe && !string.IsNullOrEmpty(response.ClientSecret))
                {
                    _pendingIntent = response;
                    _pendingIntentKey = intentKey;
                }
            }

            // Offered a trial, but the server wants a charge today: never charge a card the user
            // handed over for a free trial. Say so, and let them confirm the same intent next click.
            if (PaymentDecisions.ShouldAskBeforeCharging(HasTrial, isStripe, response))
            {
                _trialUnavailable = true;
                return;
            }

            // Stripe free trial — nothing is charged now, but the card is saved (SetupIntent) so
            // Stripe can charge it when the trial ends.
            if (isStripe && PaymentDecisions.IsSetupIntentResponse(response))
            {
                await ConfirmStripeSetup(response);
                return;
            }

            // If no client-side confirmation needed ($0 amount, or payment succeeded immediately)
            if (!response.RequiresClientConfirmation || string.IsNullOrEmpty(response.ClientSecret))
            {
                await CompleteServerRecordedPayment(response);
                return;
            }

            var providerType = (PaymentProviderType)_selectedProvider.ProviderType;
            switch (providerType)
            {
                case PaymentProviderType.Stripe:
                    await ConfirmStripePayment(response);
                    break;

                default:
                    await CompleteServerRecordedPayment(response);
                    break;
            }
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Processing payment");
        }
        finally
        {
            _isProcessing = false;
            StateHasChanged();
        }
    }

    private async Task InitiateAppStorePurchase()
    {
        if (_selectedProvider == null || _isProcessing)
            return;

        _isProcessing = true;
        StateHasChanged();

        try
        {
            var request = new InitiatePaymentRequest
            {
                ProviderId = _selectedProvider.Id,
                AppId = AppId,
                Amount = Amount,
                Currency = Currency,
                PricingModelId = PricingModelId,
                IsSubscription = IsSubscription
            };

            var response = await PaymentProviderService.InitiatePaymentAsync(request);

            if (!response.Success || response.ProductIds == null || response.ProductIds.Count == 0)
            {
                await HandlePaymentError(response.ErrorMessage ?? "No products configured", response.ErrorCode);
                return;
            }

            var providerType = (PaymentProviderType)_selectedProvider.ProviderType;
            var productId = response.ProductIds[0];
            var receiptData = await InvokeJSAsync<string>("wildwoodPayment.purchaseAppStoreProduct",
                productId, providerType == PaymentProviderType.AppleAppStore);

            if (!string.IsNullOrEmpty(receiptData))
            {
                // The interop returns only the proof of purchase; the store transaction id is unknown here
                // and the server extracts it from the validated receipt.
                var validationResult = await PaymentProviderService.ValidateStorePurchaseAsync(AppId, new StorePurchase
                {
                    ProviderType = providerType,
                    ProductId = productId,
                    PurchaseToken = receiptData
                });

                if (validationResult.Success)
                {
                    _paymentResult = validationResult;
                    _paymentComplete = true;
                    await NotifyPaymentSuccess();
                }
                else
                {
                    await HandlePaymentError(validationResult.ErrorMessage ?? "Receipt validation failed", validationResult.ErrorCode);
                }
            }
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "App store purchase");
        }
        finally
        {
            _isProcessing = false;
            StateHasChanged();
        }
    }

    private async Task InitiateBnplPayment()
    {
        if (_selectedProvider == null || _isProcessing)
            return;

        _isProcessing = true;
        StateHasChanged();

        try
        {
            var request = new InitiatePaymentRequest
            {
                ProviderId = _selectedProvider.Id,
                AppId = AppId,
                Amount = Amount,
                Currency = Currency,
                CustomerEmail = CustomerEmail,
                ReturnUrl = ReturnUrl,
                CancelUrl = CancelUrl
            };

            var response = await PaymentProviderService.InitiatePaymentAsync(request);

            if (response.Success && PaymentDecisions.IsSafeRedirectUrl(response.RedirectUrl))
            {
                // The URL comes from the provider through the server, so it is navigated to as a
                // value — never interpolated into a script string, which would let the answer run
                // JavaScript in the host page.
                Navigation.NavigateTo(response.RedirectUrl!, forceLoad: true);
            }
            else if (response.Success)
            {
                await HandlePaymentError(
                    "The payment provider returned an address that cannot be opened. Please try again.",
                    response.ErrorCode);
            }
            else
            {
                await HandlePaymentError(response.ErrorMessage ?? "Failed to initialize payment", response.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "BNPL payment");
        }
        finally
        {
            _isProcessing = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Confirms a payment with the id the SERVER recorded for it. When the server named neither a
    /// payment intent nor a subscription there is nothing to confirm, and the payment fails with a
    /// message instead of asking the server to verify an empty id.
    /// </summary>
    private async Task CompleteServerRecordedPayment(InitiatePaymentResponse response)
    {
        if (PaymentDecisions.ServerRecordedId(response) is not { Length: > 0 } paymentIntentId)
        {
            await HandlePaymentError(PaymentDecisions.MissingPaymentIdMessage, response.ErrorCode);
            return;
        }

        await CompletePayment(paymentIntentId);
    }

    private async Task CompletePayment(string paymentIntentId)
    {
        var providerType = (PaymentProviderType)(_selectedProvider?.ProviderType ?? (int)PaymentProviderType.Stripe);
        var result = await PaymentProviderService.ConfirmPaymentAsync(paymentIntentId, providerType);

        if (result.Success)
        {
            _pendingIntent = null;
            _pendingIntentKey = null;
            _paymentResult = result;
            _paymentComplete = true;
            await NotifyPaymentSuccess();
        }
        else
        {
            await HandlePaymentError(result.ErrorMessage ?? "Payment confirmation failed", result.ErrorCode);
        }
    }

    /// <summary>
    /// The completed payment, as the host sees it. Built once and reused so Continue cannot hand
    /// back a thinner payload than the success callback did.
    /// </summary>
    private PaymentSuccessEventArgs BuildSuccessArgs() => new PaymentSuccessEventArgs
    {
        TransactionId = _paymentResult?.TransactionId,
        PaymentIntentId = _paymentResult?.PaymentIntentId,
        SubscriptionId = _paymentResult?.SubscriptionId,
        Amount = Amount,
        Currency = Currency,
        ProviderType = _selectedProvider?.ProviderType ?? (int)PaymentProviderType.Stripe,
        ReceiptUrl = _paymentResult?.ReceiptUrl
    };

    /// <summary>
    /// Raises <see cref="OnPaymentSuccess"/> — exactly once per payment, as soon as it completes.
    /// </summary>
    private async Task NotifyPaymentSuccess()
    {
        if (OnPaymentSuccess.HasDelegate)
        {
            await OnPaymentSuccess.InvokeAsync(BuildSuccessArgs());
        }
    }

    private async Task HandlePaymentError(string message, string? code)
    {
        await HandleErrorAsync(new Exception(message), "Payment failed");
        
        if (OnPaymentFailure.HasDelegate)
        {
            await OnPaymentFailure.InvokeAsync(new PaymentFailureEventArgs
            {
                ErrorMessage = message,
                ErrorCode = code,
                ProviderType = _selectedProvider?.ProviderType ?? (int)PaymentProviderType.Stripe,
                IsRetryable = IsRetryableError(code)
            });
        }
    }

    private void RetryPayment()
    {
        ClearError();
        _formError = null;
        _paymentComplete = false;
        _paymentResult = null;
        _providersInitialized = false;
        // _pendingIntent deliberately survives: the retry confirms the intent the declined attempt
        // already created rather than starting a second subscription.
        StateHasChanged();
    }

    /// <summary>
    /// The success panel's Continue button. It invokes ONLY <see cref="OnContinue"/> —
    /// <see cref="OnPaymentSuccess"/> already fired when the payment completed, and firing it again
    /// ran the host's success handler (a signup, an upgrade) a second time.
    /// </summary>
    private void HandleSuccessContinue()
    {
        if (OnContinue.HasDelegate && _paymentResult != null)
        {
            _ = OnContinue.InvokeAsync(BuildSuccessArgs());
        }
    }

    private void HandleCancel()
    {
        if (OnCancel.HasDelegate)
        {
            _ = OnCancel.InvokeAsync();
        }
    }

    private static bool IsRedirectProvider(int providerType)
    {
        var type = (PaymentProviderType)providerType;
        return type == PaymentProviderType.Klarna ||
               type == PaymentProviderType.Affirm ||
               type == PaymentProviderType.Afterpay;
    }

    private static bool IsRetryableError(string? code)
    {
        if (string.IsNullOrEmpty(code)) return true;
        
        return code != "card_declined" && 
               code != "insufficient_funds" && 
               code != "lost_card" && 
               code != "stolen_card";
    }

    /// <summary>
    /// The amount the customer is about to be charged. One formatter, fixed at en-US like the JS
    /// SDK's <c>formatMoney</c>: the per-currency culture switch meant the same price came out
    /// "1.234,56 €" on the server and "€1,234.56" in a browser rendering it from the same data.
    /// </summary>
    private static string FormatAmount(decimal amount, string currency)
    {
        return FormatHelpers.FormatMoney(amount, currency);
    }

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();

        await DisposeStripeAsync();

        if (_selectedProvider != null && (PaymentProviderType)_selectedProvider.ProviderType == PaymentProviderType.PayPal)
        {
            try
            {
                await InvokeJSVoidAsync("wildwoodPayment.disposePayPal");
            }
            catch
            {
                // Ignore disposal errors
            }
        }
        
        foreach (var providerType in _loadedProviderScripts)
        {
            try
            {
                await ScriptLoader.ReleaseAsync(providerType);
            }
            catch
            {
                // Ignore release errors during disposal
            }
        }
        _loadedProviderScripts.Clear();
    }
}
