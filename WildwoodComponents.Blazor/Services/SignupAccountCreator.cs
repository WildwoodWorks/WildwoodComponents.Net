using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using static WildwoodComponents.Blazor.Components.Registration.TokenRegistrationComponent;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// The four server calls that turn a filled-in registration form into a signed-in account on a
    /// plan: register, log in, attach the payment, start the subscription.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the JS SDK's <c>processSignup</c>
    /// (packages/wildwood-react-shared/src/registrationSubscription/useSignupFlow.ts, <c>runSignup</c>
    /// + <c>activatePlan</c>), lifted out of
    /// <see cref="Components.Registration.SignupWithSubscriptionComponent"/> rather than copied, so
    /// the wizard and the new <c>RegistrationSubscriptionSignup</c> view create accounts by exactly
    /// one route. Behaviour is the wizard's, unchanged — including its messages, which are what a
    /// live site's end-to-end suite reads.
    /// </para>
    /// <para>
    /// Three rules are load-bearing:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>A retry resumes, it does not re-register.</b> The completed sub-steps live on the caller's
    /// <see cref="SignupAccountAttempt"/>, which is handed back in on the next attempt: a card that
    /// was already charged is not charged again and an account that was already created is not
    /// created again.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>A registration token that carries a plan already subscribed the account.</b> Subscribing
    /// over it REPLACES the subscription the token just created, so a grant suppresses the
    /// self-subscribe entirely.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Linking the payment and starting the plan are never fatal.</b> The account exists and the
    /// money is taken; a plan can be activated from the dashboard, and the outcome says so.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public interface ISignupAccountCreator
    {
        /// <summary>
        /// Registers, signs in, links the plan's payment and starts the subscription, resuming at
        /// whichever of those <paramref name="attempt"/> says is still outstanding.
        /// </summary>
        /// <param name="request">What to create, and what was already paid for.</param>
        /// <param name="attempt">
        /// The progress of this signup, read AND written: hand the same instance back on a retry.
        /// </param>
        /// <exception cref="Exception">
        /// Whatever the transport or a service threw. Refusals the server explained come back as an
        /// unsuccessful <see cref="SignupAccountResult"/> instead, so a caller can tell "the server
        /// said no" from "the call did not happen".
        /// </exception>
        Task<SignupAccountResult> CreateAsync(SignupAccountRequest request, SignupAccountAttempt attempt);
    }

    /// <summary>Codes <see cref="SignupAccountCreator"/> reports a refusal under.</summary>
    public static class SignupAccountErrorCodes
    {
        /// <summary>The server would not create the account. JS spells this one the same way.</summary>
        public const string RegistrationRefused = "registration_refused";

        /// <summary>The account was created but the sign-in that follows it did not answer a token.</summary>
        public const string LoginFailed = "login_failed";
    }

    /// <summary>What one signup attempt is asked to create.</summary>
    public class SignupAccountRequest
    {
        /// <summary>The app the account is being created for.</summary>
        public string AppId { get; set; } = string.Empty;

        /// <summary>The registration form as the visitor filled it in.</summary>
        public RegistrationFormData FormData { get; set; } = new RegistrationFormData();

        /// <summary>The plan to subscribe to, or null when the signup chose none.</summary>
        public string? TierId { get; set; }

        /// <summary>The pricing option within that plan.</summary>
        public string? PricingId { get; set; }

        /// <summary>The transaction to subscribe with — what the plan's card produced.</summary>
        public string? PaymentTransactionId { get; set; }

        /// <summary>
        /// The provider's own id for that payment, which is what the server looks a transaction up
        /// by when it is attached to the new user.
        /// </summary>
        public string? PaymentExternalId { get; set; }

        /// <summary>
        /// The plan this app's registration token carries, when it carries one. Present, nothing is
        /// self-subscribed: the token's registration already did it.
        /// </summary>
        public RegistrationTokenAppGrant? TokenGrant { get; set; }

        /// <summary>Copy for the status line. Null takes the shipped words.</summary>
        public RegistrationSubscriptionLabels? Labels { get; set; }

        /// <summary>Raised as each step starts, with the label for it.</summary>
        public Action<string>? OnStatus { get; set; }
    }

    /// <summary>
    /// How far a signup got. Kept by the caller so "Try Again" resumes rather than registering the
    /// same person twice.
    /// </summary>
    public class SignupAccountAttempt
    {
        /// <summary>The account exists.</summary>
        public bool Registered { get; set; }

        /// <summary>The session is stored and the tier service is authenticated.</summary>
        public bool LoggedIn { get; set; }

        /// <summary>The plan could not be started. Never fatal; the success copy says so.</summary>
        public bool SubscriptionFailed { get; set; }

        /// <summary>The sign-in response, which carries the JWT and any pending disclaimers.</summary>
        public AuthenticationResponse? AuthResponse { get; set; }

        /// <summary>The registration response, for a caller that reads its payment fields.</summary>
        public RegistrationSuccessResponse? RegistrationResponse { get; set; }

        /// <summary>Throws the attempt away — what "Start Over" does.</summary>
        public void Reset()
        {
            Registered = false;
            LoggedIn = false;
            SubscriptionFailed = false;
            AuthResponse = null;
            RegistrationResponse = null;
        }
    }

    /// <summary>What one signup attempt produced.</summary>
    public class SignupAccountResult
    {
        /// <summary>The account exists and is signed in.</summary>
        public bool Success { get; set; }

        /// <summary>The refusal, in the server's own words where it sent any.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>One of <see cref="SignupAccountErrorCodes"/>, when the attempt was refused.</summary>
        public string? ErrorCode { get; set; }

        /// <summary>The sign-in response, on success.</summary>
        public AuthenticationResponse? AuthResponse { get; set; }

        /// <summary>The new account's id, or an empty string when the sign-in carried none.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>The plan could not be started, so its activation is pending.</summary>
        public bool SubscriptionFailed { get; set; }

        /// <summary>
        /// The sign-in reported disclaimers the new account has to accept before it is finished.
        /// </summary>
        public bool RequiresDisclaimers { get; set; }

        /// <summary>Those disclaimers, straight off the authenticated response.</summary>
        public List<PendingDisclaimerModel>? PendingDisclaimers { get; set; }
    }

    /// <inheritdoc cref="ISignupAccountCreator"/>
    public class SignupAccountCreator : ISignupAccountCreator
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<WildwoodComponentsOptions> _options;
        private readonly IAuthenticationService _authService;
        private readonly IAppTierComponentService _appTierService;
        private readonly IPaymentProviderService _paymentProviderService;
        private readonly IWildwoodSessionManager _sessionManager;
        private readonly ILogger<SignupAccountCreator>? _logger;
        private readonly IAttributionService? _attribution;

        private HttpClient? _httpClient;

        public SignupAccountCreator(
            IHttpClientFactory httpClientFactory,
            IOptions<WildwoodComponentsOptions> options,
            IAuthenticationService authService,
            IAppTierComponentService appTierService,
            IPaymentProviderService paymentProviderService,
            IWildwoodSessionManager sessionManager,
            ILogger<SignupAccountCreator>? logger = null,
            IAttributionService? attribution = null)
        {
            _httpClientFactory = httpClientFactory;
            _options = options;
            _authService = authService;
            _appTierService = appTierService;
            _paymentProviderService = paymentProviderService;
            _sessionManager = sessionManager;
            _logger = logger;
            _attribution = attribution;
        }

        /// <inheritdoc />
        public async Task<SignupAccountResult> CreateAsync(SignupAccountRequest request, SignupAccountAttempt attempt)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (attempt is null) throw new ArgumentNullException(nameof(attempt));

            var labels = RegistrationSubscriptionLabels.Resolve(request.Labels);
            var formData = request.FormData;

            // Step 1: Register (if not already registered from a retry)
            if (!attempt.Registered)
            {
                var refusal = await RegisterAsync(request, attempt, labels, formData);
                if (refusal is not null) return refusal;
            }

            // Step 2: Login (if not already logged in from a retry)
            if (!attempt.LoggedIn)
            {
                var refusal = await LoginAsync(request, attempt, labels, formData);
                if (refusal is not null) return refusal;
            }

            // Step 3: Link the payment transaction to the newly created user. The plan's card was
            // taken before the account existed, so the transaction belongs to nobody until now.
            await LinkPaymentAsync(request, attempt);

            // Step 4: Subscribe to the selected tier — unless the registration token already put
            // the account on a plan, in which case subscribing again REPLACES it, which cancels the
            // subscription the token just created.
            await ActivatePlanAsync(request, attempt, labels);

            var authResponse = attempt.AuthResponse;
            var pending = authResponse?.PendingDisclaimers;
            var requiresDisclaimers = authResponse?.RequiresDisclaimerAcceptance == true
                && pending is not null
                && pending.Count > 0;

            return new SignupAccountResult
            {
                Success = true,
                AuthResponse = authResponse,
                UserId = authResponse?.Id ?? string.Empty,
                SubscriptionFailed = attempt.SubscriptionFailed,
                RequiresDisclaimers = requiresDisclaimers,
                PendingDisclaimers = requiresDisclaimers ? pending : null
            };
        }

        #region Steps

        /// <summary>
        /// Creates the account. Answers null when it worked, and a refusal otherwise — in the words
        /// the wizard has always used, because a live site's suite reads them.
        /// </summary>
        private async Task<SignupAccountResult?> RegisterAsync(
            SignupAccountRequest request,
            SignupAccountAttempt attempt,
            RegistrationSubscriptionLabels labels,
            RegistrationFormData formData)
        {
            request.OnStatus?.Invoke(labels.StatusCreatingAccount);

            var httpClient = GetHttpClient();

            HttpResponseMessage response;
            if (!string.IsNullOrEmpty(formData.Token))
            {
                var tokenRequest = new TokenRegistrationRequest
                {
                    Token = formData.Token,
                    FirstName = formData.FirstName ?? string.Empty,
                    LastName = formData.LastName ?? string.Empty,
                    Username = formData.Username ?? string.Empty,
                    Email = formData.Email ?? string.Empty,
                    Password = formData.Password ?? string.Empty,
                    AppId = request.AppId,
                    Platform = "Web",
                    DeviceInfo = "Browser",
                    Attribution = await GetAttributionPayloadAsync()
                };
                response = await httpClient.PostAsJsonAsync("api/userregistration/register-with-token", tokenRequest);
            }
            else
            {
                var openRequest = new OpenRegistrationRequest
                {
                    FirstName = formData.FirstName ?? string.Empty,
                    LastName = formData.LastName ?? string.Empty,
                    Username = formData.Username ?? string.Empty,
                    Email = formData.Email ?? string.Empty,
                    Password = formData.Password ?? string.Empty,
                    AppId = request.AppId,
                    Platform = "Web",
                    DeviceInfo = "Browser",
                    PricingModelId = formData.PricingModelId,
                    Attribution = await GetAttributionPayloadAsync()
                };
                response = await httpClient.PostAsJsonAsync("api/userregistration/register", openRequest);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger?.LogError("Registration failed: {StatusCode} - {Error}", response.StatusCode, errorContent);

                string message;
                try
                {
                    var errorResult = System.Text.Json.JsonSerializer.Deserialize<RegistrationSuccessResponse>(
                        errorContent,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    message = errorResult?.Message ?? $"Registration failed: {response.StatusCode}";
                }
                catch
                {
                    message = $"Registration failed: {response.StatusCode}";
                }

                return Refused(message, SignupAccountErrorCodes.RegistrationRefused);
            }

            attempt.RegistrationResponse = await response.Content.ReadFromJsonAsync<RegistrationSuccessResponse>();
            if (attempt.RegistrationResponse?.Success != true)
            {
                return Refused(
                    attempt.RegistrationResponse?.Message ?? "Registration failed. Please try again.",
                    SignupAccountErrorCodes.RegistrationRefused);
            }

            attempt.Registered = true;

            // Recorded with the account: a later signup from this browser must not reuse the touches.
            await ClearAttributionAsync();
            return null;
        }

        private async Task<SignupAccountResult?> LoginAsync(
            SignupAccountRequest request,
            SignupAccountAttempt attempt,
            RegistrationSubscriptionLabels labels,
            RegistrationFormData formData)
        {
            request.OnStatus?.Invoke(labels.StatusSigningIn);

            var loginRequest = new LoginRequest
            {
                Username = formData.Username ?? string.Empty,
                Email = formData.Email ?? string.Empty,
                Password = formData.Password,
                AppId = request.AppId,
                Platform = "Web",
                DeviceInfo = "Browser"
            };

            attempt.AuthResponse = await _authService.LoginAsync(loginRequest);

            if (attempt.AuthResponse is null || string.IsNullOrEmpty(attempt.AuthResponse.JwtToken))
            {
                return Refused(
                    "Login failed after registration. Please try logging in manually.",
                    SignupAccountErrorCodes.LoginFailed);
            }

            // Persist session so the user stays logged in after signup completes
            await _sessionManager.LoginAsync(attempt.AuthResponse);

            // Set the auth token on the tier service so subscription calls are authenticated
            _appTierService.SetAuthToken(attempt.AuthResponse.JwtToken);
            attempt.LoggedIn = true;

            // Clear password from memory now that login is complete
            formData.Password = null;
            return null;
        }

        /// <summary>
        /// Attaches the plan's payment to the account that now exists. Non-fatal: the payment
        /// succeeded and the link can be repaired later.
        /// </summary>
        private async Task LinkPaymentAsync(SignupAccountRequest request, SignupAccountAttempt attempt)
        {
            var linkId = request.PaymentExternalId ?? request.PaymentTransactionId;
            if (string.IsNullOrEmpty(linkId)) return;
            if (attempt.AuthResponse is null || string.IsNullOrEmpty(attempt.AuthResponse.Id)) return;

            try
            {
                await _paymentProviderService.LinkTransactionToUserAsync(linkId, attempt.AuthResponse.Id);
            }
            catch (Exception linkEx)
            {
                _logger?.LogWarning(linkEx, "Failed to link payment transaction to user");
            }
        }

        /// <summary>
        /// Starts the plan the signup chose. Non-fatal either way — the account exists and the plan
        /// can be activated later. A refusal must never leave the caller on "Activating your
        /// plan..." forever.
        /// </summary>
        private async Task ActivatePlanAsync(
            SignupAccountRequest request,
            SignupAccountAttempt attempt,
            RegistrationSubscriptionLabels labels)
        {
            if (!Components.Registration.SignupPlanDecisions.ShouldSelfSubscribe(request.TokenGrant, request.TierId))
            {
                return;
            }

            request.OnStatus?.Invoke(labels.StatusActivatingPlan);

            try
            {
                var result = await _appTierService.SubscribeToTierAsync(
                    request.AppId, request.TierId!, request.PricingId, request.PaymentTransactionId);
                if (!result.Success)
                {
                    _logger?.LogWarning("Tier subscription failed: {Message}", result.ErrorMessage);
                    attempt.SubscriptionFailed = true;
                }
            }
            catch (Exception subscribeEx)
            {
                _logger?.LogWarning(subscribeEx, "Tier subscription failed");
                attempt.SubscriptionFailed = true;
            }
        }

        #endregion

        #region Helpers

        private static SignupAccountResult Refused(string message, string code)
        {
            return new SignupAccountResult { Success = false, ErrorMessage = message, ErrorCode = code };
        }

        private async Task<AttributionPayloadModel?> GetAttributionPayloadAsync()
        {
            return _attribution is not null ? await _attribution.GetForRegistrationAsync() : null;
        }

        private async Task ClearAttributionAsync()
        {
            if (_attribution is not null) await _attribution.ClearAsync();
        }

        private HttpClient GetHttpClient()
        {
            if (_httpClient is null)
            {
                _httpClient = _httpClientFactory.CreateClient(
                    Extensions.ServiceCollectionExtensions.WildwoodHttpClientName);
                var baseUrl = _options.Value.BaseUrl;
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                }
            }

            return _httpClient;
        }

        #endregion
    }
}
