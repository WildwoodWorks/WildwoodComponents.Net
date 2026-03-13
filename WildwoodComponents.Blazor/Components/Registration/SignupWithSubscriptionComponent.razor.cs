using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Extensions;
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
        [Inject] private IWildwoodSessionManager SessionManager { get; set; } = default!;
        [Inject] private IHttpClientFactory HttpClientFactory { get; set; } = default!;
        [Inject] private IOptions<WildwoodComponentsOptions> OptionsAccessor { get; set; } = default!;
        [Inject] private new ILogger<SignupWithSubscriptionComponent> Logger { get; set; } = default!;

        #endregion

        #region Parameters

        [Parameter, EditorRequired]
        public string AppId { get; set; } = string.Empty;

        [Parameter] public string? PreSelectedTierId { get; set; }
        [Parameter] public string? RegistrationToken { get; set; }
        [Parameter] public bool RequireToken { get; set; }
        [Parameter] public bool AllowOpenRegistration { get; set; } = true;

        /// <summary>
        /// If true, skips the tier selection step and goes directly to processing after registration.
        /// </summary>
        [Parameter] public bool SkipTierSelection { get; set; }

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

        // Track completed sub-steps for retry logic
        private bool _registered;
        private bool _loggedIn;
        private bool _subscriptionFailed;
        private AuthenticationResponse? _authResponse;
        private RegistrationSuccessResponse? _registrationResponse;

        private enum SignupStep
        {
            Register,
            SelectTier,
            Processing,
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

        protected override Task OnComponentInitializedAsync()
        {
            _selectedTierId = PreSelectedTierId;
            BuildStepConfig();
            return Task.CompletedTask;
        }

        private void BuildStepConfig()
        {
            _stepConfig = new List<StepDef>
            {
                new StepDef { Key = SignupStep.Register, Label = "Create Account", Number = 1 },
            };

            if (!SkipTierSelection)
            {
                _stepConfig.Add(new StepDef { Key = SignupStep.SelectTier, Label = "Choose Plan", Number = 2 });
            }

            _stepConfig.Add(new StepDef { Key = SignupStep.Success, Label = "Complete", Number = SkipTierSelection ? 2 : 3 });
        }

        #endregion

        #region Event Handlers

        private void HandleFormDataCollected(RegistrationFormData formData)
        {
            _collectedFormData = formData;

            if (SkipTierSelection)
            {
                _ = ProcessSignupAsync();
            }
            else
            {
                _currentStep = SignupStep.SelectTier;
                StateHasChanged();
            }
        }

        private void HandleTierSelected(PricingTierSelectedEventArgs args)
        {
            _selectedTierId = args.Tier.Id;
            _selectedPricingId = args.SelectedPricing?.Id;
            _ = ProcessSignupAsync();
        }

        private void HandleBack()
        {
            if (_currentStep == SignupStep.SelectTier)
            {
                _currentStep = SignupStep.Register;
                StateHasChanged();
            }
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
            _registered = false;
            _loggedIn = false;
            _authResponse = null;
            _registrationResponse = null;
            _processingError = null;
            _processingStatus = null;
            StateHasChanged();
        }

        #endregion

        #region Processing

        private async Task ProcessSignupAsync()
        {
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

                // Step 1: Register (if not already registered from a retry)
                if (!_registered)
                {
                    _processingStatus = "Creating your account...";
                    StateHasChanged();

                    var httpClient = GetHttpClient();

                    HttpResponseMessage response;
                    if (!string.IsNullOrEmpty(_collectedFormData.Token))
                    {
                        var request = new TokenRegistrationRequest
                        {
                            Token = _collectedFormData.Token,
                            FirstName = _collectedFormData.FirstName ?? string.Empty,
                            LastName = _collectedFormData.LastName ?? string.Empty,
                            Username = _collectedFormData.Username ?? string.Empty,
                            Email = _collectedFormData.Email ?? string.Empty,
                            Password = _collectedFormData.Password ?? string.Empty,
                            AppId = AppId,
                            Platform = "Web",
                            DeviceInfo = "Browser"
                        };
                        response = await httpClient.PostAsJsonAsync("api/userregistration/register-with-token", request);
                    }
                    else
                    {
                        var request = new OpenRegistrationRequest
                        {
                            FirstName = _collectedFormData.FirstName ?? string.Empty,
                            LastName = _collectedFormData.LastName ?? string.Empty,
                            Username = _collectedFormData.Username ?? string.Empty,
                            Email = _collectedFormData.Email ?? string.Empty,
                            Password = _collectedFormData.Password ?? string.Empty,
                            AppId = AppId,
                            Platform = "Web",
                            DeviceInfo = "Browser",
                            PricingModelId = _collectedFormData.PricingModelId
                        };
                        response = await httpClient.PostAsJsonAsync("api/userregistration/register", request);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Logger.LogError("Registration failed: {StatusCode} - {Error}", response.StatusCode, errorContent);

                        try
                        {
                            var errorResult = System.Text.Json.JsonSerializer.Deserialize<RegistrationSuccessResponse>(
                                errorContent,
                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            _processingError = errorResult?.Message ?? $"Registration failed: {response.StatusCode}";
                        }
                        catch
                        {
                            _processingError = $"Registration failed: {response.StatusCode}";
                        }

                        StateHasChanged();
                        return;
                    }

                    _registrationResponse = await response.Content.ReadFromJsonAsync<RegistrationSuccessResponse>();
                    if (_registrationResponse?.Success != true)
                    {
                        _processingError = _registrationResponse?.Message ?? "Registration failed. Please try again.";
                        StateHasChanged();
                        return;
                    }

                    _registered = true;
                }

                // Step 2: Login (if not already logged in from a retry)
                if (!_loggedIn)
                {
                    _processingStatus = "Signing you in...";
                    StateHasChanged();

                    var loginRequest = new LoginRequest
                    {
                        Username = _collectedFormData.Username,
                        Email = _collectedFormData.Email,
                        Password = _collectedFormData.Password,
                        AppId = AppId,
                        Platform = "Web",
                        DeviceInfo = "Browser"
                    };

                    _authResponse = await AuthService.LoginAsync(loginRequest);

                    if (_authResponse == null || string.IsNullOrEmpty(_authResponse.JwtToken))
                    {
                        _processingError = "Login failed after registration. Please try logging in manually.";
                        StateHasChanged();
                        return;
                    }

                    // Persist session so the user stays logged in after signup completes
                    await SessionManager.LoginAsync(_authResponse);

                    // Set the auth token on the tier service so subscription calls are authenticated
                    AppTierService.SetAuthToken(_authResponse.JwtToken);
                    _loggedIn = true;

                    // Clear password from memory now that login is complete
                    if (_collectedFormData != null)
                    {
                        _collectedFormData.Password = null;
                    }
                }

                // Step 3: Subscribe to selected tier (if a tier was selected)
                if (!string.IsNullOrEmpty(_selectedTierId))
                {
                    _processingStatus = "Activating your plan...";
                    StateHasChanged();

                    var result = await AppTierService.SubscribeToTierAsync(AppId, _selectedTierId, _selectedPricingId, null);
                    if (!result.Success)
                    {
                        Logger.LogWarning("Tier subscription failed: {Message}", result.ErrorMessage);
                        _subscriptionFailed = true;
                    }
                }

                _currentStep = SignupStep.Success;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during signup processing");
                _processingError = "An error occurred. Please try again.";
            }
            finally
            {
                _processingStatus = null;
                StateHasChanged();
            }
        }

        #endregion

        #region Helpers

        private HttpClient? _httpClient;

        private HttpClient GetHttpClient()
        {
            if (_httpClient == null)
            {
                _httpClient = HttpClientFactory.CreateClient();
                var baseUrl = OptionsAccessor.Value.BaseUrl;
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                }
            }
            return _httpClient;
        }

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
        /// Gets the step index for the step indicator, mapping Processing to SelectTier's position.
        /// </summary>
        private int GetDisplayStepIndex()
        {
            // Processing step shares the position of the last action step
            if (_currentStep == SignupStep.Processing)
            {
                return SkipTierSelection ? 0 : 1;
            }
            return GetStepIndex(_currentStep);
        }

        #endregion
    }
}
