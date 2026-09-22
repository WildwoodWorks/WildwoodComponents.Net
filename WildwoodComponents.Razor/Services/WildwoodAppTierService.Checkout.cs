using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Pack checkout, the 3-D Secure plan change, trial eligibility and the throwing public catalog —
/// the Razor twin of WildwoodComponents.Blazor.Services.AppTierComponentService.Checkout and of
/// packages/wildwood-core/src/features/appTierService.ts.
///
/// Every method here reports an HTTP refusal as DATA and never throws (the public catalog is the
/// stated exception): the server answers a refusal with the same result DTO it answers a success
/// with, and a caller has to be able to word "you already own that pack" differently from "that
/// route does not exist yet".
///
/// Unlike the older methods in the main partial, these put the session's bearer token on the
/// REQUEST rather than on the shared client's default headers: one named "WildwoodAPI" client is
/// handed to every service in the scope, so a default header set here outlives this call.
/// <see cref="Controllers.WildwoodRegistrationSubscriptionProxyController"/> is what the browser
/// calls; these methods are the server side of it.
/// </summary>
public partial class WildwoodAppTierService
{
    #region Structured failure plumbing

    /// <summary>
    /// One attempt at a request: the body the server sent (success or refusal alike), the status
    /// when the call reached the server, or the exception when it never did.
    /// </summary>
    private sealed class ActionResponse
    {
        public bool IsSuccess { get; init; }
        public string? Content { get; init; }
        public HttpStatusCode? Status { get; init; }
        public Exception? Exception { get; init; }
    }

    /// <summary>
    /// Sends one action request with the session's token applied to THIS request. Never throws:
    /// a transport failure comes back as an <see cref="ActionResponse"/> carrying the exception.
    /// </summary>
    private async Task<ActionResponse> SendActionAsync(
        HttpMethod method, string url, object? body, bool authenticated, string operation)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            }

            if (authenticated)
            {
                // On the request, never on DefaultRequestHeaders: the named client is shared by
                // every service in the scope, so a default header would leak across callers.
                var token = _sessionManager.GetAccessToken();
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(request);

            string? content = null;
            try { content = await response.Content.ReadAsStringAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read response body for {Operation}", operation); }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Operation} failed: HTTP {StatusCode} {Status}",
                    operation, (int)response.StatusCode, response.StatusCode);
            }

            return new ActionResponse
            {
                IsSuccess = response.IsSuccessStatusCode,
                Content = content,
                Status = response.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during {Operation}", operation);
            return new ActionResponse { IsSuccess = false, Exception = ex };
        }
    }

    private Task<ActionResponse> PostActionAsync(string url, object? body, string operation)
        => SendActionAsync(HttpMethod.Post, url, body, authenticated: true, operation);

    private Task<ActionResponse> GetActionAsync(string url, string operation)
        => SendActionAsync(HttpMethod.Get, url, null, authenticated: true, operation);

    /// <summary>
    /// Turn a failed request into the structured refusal these actions report. The rule lives in
    /// <see cref="AppTierActionMapper"/>, shared with the Blazor service: the server's own error
    /// code wins, a bare 404 means the route is absent (an older server) and becomes
    /// <see cref="AppTierActionErrorCodes.NotSupported"/>, anything else becomes
    /// <see cref="AppTierActionErrorCodes.RequestFailed"/>, and the message is never empty.
    /// </summary>
    private static AppTierActionError ToActionError(ActionResponse response, string fallbackMessage)
        => AppTierActionMapper.ToActionError(
            response.Status.HasValue ? (int)response.Status.Value : null,
            response.Content,
            response.Exception?.Message,
            fallbackMessage);

    private static T? Deserialize<T>(string? content) where T : class
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try { return JsonSerializer.Deserialize<T>(content!, JsonOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The refusal body read back as the result DTO, when the server really sent that DTO.
    /// See <see cref="AppTierActionMapper.RefusalBody{T}"/>.
    /// </summary>
    private static T? RefusalBody<T>(string? content, string requiredProperty, bool requireString) where T : class
        => AppTierActionMapper.RefusalBody<T>(content, requiredProperty, requireString, JsonOptions);

    #endregion

    #region Trial eligibility

    /// <summary>
    /// Whether this account may still start a free trial in the app — on a tier, and per pack.
    ///
    /// Never throws: a failure answers "eligible", which is what a signup screen already shows
    /// from the catalog's trial days, and an empty <c>AddOns</c> map means "unknown" for the same
    /// reason. The checkout quote and the payment initiation re-decide authoritatively before any
    /// money moves.
    /// </summary>
    public async Task<TrialEligibilityModel> GetTrialEligibilityAsync(string appId)
    {
        var url = $"app-tiers/{appId}/trial-eligibility";
        var response = await GetActionAsync(url, $"GetTrialEligibility({appId})");

        if (response.IsSuccess)
        {
            var result = Deserialize<TrialEligibilityModel>(response.Content);
            if (result is not null)
            {
                result.AddOns ??= new Dictionary<string, bool>();
                return result;
            }
        }

        return new TrialEligibilityModel { TierTrialEligible = true, AddOns = new Dictionary<string, bool>() };
    }

    #endregion

    #region Tier change with payment action

    /// <summary>
    /// Options form of the self-service tier change, for a caller that can finish a payment. With
    /// <see cref="SelfChangeTierOptions.SupportsPaymentAction"/> a change whose proration needs
    /// 3-D Secure comes back with <c>RequiresAction</c>, a client secret and a pending change id to
    /// confirm and then <see cref="CompleteTierChangeAsync"/>, instead of being refused outright.
    ///
    /// Only this overload sends <c>SupportsPaymentAction</c>: the positional
    /// <see cref="ChangeTierAsync(string, string, string?, bool, string?)"/> posts exactly what it
    /// always did, because an older server rejects an unknown property and a caller that never
    /// opted into finishing a payment must not appear to have.
    /// </summary>
    public async Task<AppTierChangeResultModel> ChangeTierAsync(string appId, SelfChangeTierOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var url = $"app-tiers/{appId}/my-subscription/change";
        var body = new
        {
            NewAppTierId = options.NewTierId,
            NewAppTierPricingId = options.NewPricingId,
            Immediate = options.Immediate,
            PaymentTransactionId = options.PaymentTransactionId,
            SupportsPaymentAction = options.SupportsPaymentAction
        };

        var response = await PostActionAsync(url, body, $"ChangeTier({appId})");

        if (response.IsSuccess)
        {
            // RequiresAction / Processing arrive with Success=false and mean "not yet", not a
            // refusal — they come back to the caller as data.
            var result = Deserialize<AppTierChangeResultModel>(response.Content);
            if (result is not null) return result;
        }

        var error = ToActionError(response, "Failed to change the plan");
        return new AppTierChangeResultModel
        {
            Success = false,
            ErrorMessage = error.Message,
            ErrorCode = error.Code
        };
    }

    /// <summary>
    /// Finish a plan change that came back <c>RequiresAction</c>, once the prorated payment has
    /// been confirmed. Safe to call repeatedly: the server asks the processor whether the invoice
    /// really paid before it moves anything, and answers <c>Processing</c> while it waits.
    /// Never throws.
    /// </summary>
    public async Task<AppTierChangeResultModel> CompleteTierChangeAsync(string appId, string pendingChangeId)
    {
        var changeId = Uri.EscapeDataString(pendingChangeId ?? string.Empty);
        var url = $"app-tiers/{appId}/my-subscription/change/{changeId}/complete";
        var response = await PostActionAsync(url, null, $"CompleteTierChange({appId})");

        if (response.IsSuccess)
        {
            var result = Deserialize<AppTierChangeResultModel>(response.Content);
            if (result is not null) return result;
        }

        var error = ToActionError(response, "Failed to complete the plan change");
        return new AppTierChangeResultModel
        {
            Success = false,
            ErrorMessage = error.Message,
            ErrorCode = error.Code,
            IsScheduled = false
        };
    }

    #endregion

    #region Pack lifecycle (structured results)

    /// <summary>
    /// Subscribe to a single pack. Returns the created subscription, or a structured refusal — a
    /// pack already owned, one bundled in the tier, and a payment the server would not accept are
    /// all different problems a UI has to word differently. Never throws.
    /// </summary>
    public async Task<AddOnSubscribeResultModel> SubscribeToAddOnDetailedAsync(
        string appId, string addOnId, string? pricingId = null, string? paymentTransactionId = null)
    {
        var url = $"app-tier-addons/{appId}/subscribe";
        var body = new
        {
            AppId = appId,
            AppTierAddOnId = addOnId,
            AppTierAddOnPricingId = pricingId,
            PaymentTransactionId = paymentTransactionId
        };

        var response = await PostActionAsync(url, body, $"SubscribeToAddOn({addOnId})");

        if (response.IsSuccess)
        {
            return new AddOnSubscribeResultModel
            {
                Success = true,
                Subscription = Deserialize<UserAddOnSubscriptionModel>(response.Content)
            };
        }

        return new AddOnSubscribeResultModel
        {
            Success = false,
            Error = ToActionError(response, "Failed to subscribe to the pack")
        };
    }

    /// <summary>
    /// Cancel one of the calling user's own packs. By default access continues to the end of the
    /// period already paid for (<c>IsScheduled</c>); <paramref name="immediate"/> ends it now.
    /// Never throws.
    /// </summary>
    public async Task<AddOnSubscriptionCancelResultModel> CancelAddOnDetailedAsync(string subscriptionId, bool immediate = false)
    {
        // Lowercase true/false, byte-identical to the JS `?immediate=${immediate}`.
        var immediateFlag = immediate ? "true" : "false";
        var url = $"app-tier-addons/subscriptions/{subscriptionId}/cancel?immediate={immediateFlag}";
        var response = await PostActionAsync(url, null, $"CancelAddOnSubscription({subscriptionId})");

        if (response.IsSuccess)
        {
            var data = Deserialize<AddOnSubscriptionCancelResultModel>(response.Content);
            return new AddOnSubscriptionCancelResultModel
            {
                Success = true,
                IsScheduled = data?.IsScheduled,
                Status = data?.Status,
                EffectiveDate = data?.EffectiveDate
            };
        }

        var error = ToActionError(response, "Failed to cancel the pack");
        var result = RefusalBody<AddOnSubscriptionCancelResultModel>(response.Content, "success", requireString: false)
            ?? new AddOnSubscriptionCancelResultModel();
        result.Success = false;
        result.ErrorCode = error.Code;
        result.ErrorMessage = error.Message;
        return result;
    }

    /// <summary>
    /// Take back a scheduled cancellation: the provider stops cancelling at period end and the
    /// pack goes back to Active (or Trialing while its trial runs). Never throws.
    /// </summary>
    public async Task<AddOnSubscriptionReactivateResultModel> ReactivateAddOnAsync(string subscriptionId)
    {
        var url = $"app-tier-addons/subscriptions/{subscriptionId}/reactivate";
        var response = await PostActionAsync(url, null, $"ReactivateAddOn({subscriptionId})");

        if (response.IsSuccess)
        {
            var subscription = Deserialize<UserAddOnSubscriptionModel>(response.Content);
            return new AddOnSubscriptionReactivateResultModel
            {
                Success = true,
                Status = subscription?.Status,
                Subscription = subscription
            };
        }

        var error = ToActionError(response, "Failed to reactivate the pack");
        var result = RefusalBody<AddOnSubscriptionReactivateResultModel>(response.Content, "success", requireString: false)
            ?? new AddOnSubscriptionReactivateResultModel();
        result.Success = false;
        result.ErrorCode = error.Code;
        result.ErrorMessage = error.Message;
        return result;
    }

    #endregion

    #region Pack checkout (card once, any number of packs)

    /// <summary>
    /// Price a basket of packs. Server-authoritative: the price, billing frequency, trial length
    /// and trial eligibility of every line come from the catalog and the account, not from this
    /// request. The returned <c>CheckoutId</c> is echoed back on <see cref="CheckoutAddOnsAsync"/>
    /// and is what makes the purchase idempotent. Never throws.
    /// </summary>
    public async Task<AddOnCheckoutQuoteModel> QuoteAddOnCheckoutAsync(string appId, IReadOnlyList<AddOnCheckoutItemInput>? items)
    {
        var url = $"app-tier-addons/{appId}/checkout/quote";
        var response = await PostActionAsync(
            url, new { Items = AppTierActionMapper.ToCheckoutItems(items) }, $"QuoteAddOnCheckout({appId})");

        if (response.IsSuccess)
        {
            var quote = Deserialize<AddOnCheckoutQuoteModel>(response.Content);
            if (quote is not null) return quote;
        }

        var error = ToActionError(response, "Failed to price the packs");
        // Currency stays deliberately blank on a refusal: a refused quote priced nothing, and
        // naming a currency here would be inventing one for prices that do not exist.
        var result = RefusalBody<AddOnCheckoutQuoteModel>(response.Content, "success", requireString: false)
            ?? new AddOnCheckoutQuoteModel { Currency = string.Empty };
        result.Success = false;
        result.ErrorCode = error.Code;
        result.ErrorMessage = error.Message;
        return result;
    }

    /// <summary>
    /// Start the one-off card entry for an account with no card on file. Confirm the returned
    /// client secret, then pass the payment transaction id to <see cref="CheckoutAddOnsAsync"/>.
    /// Never throws.
    /// </summary>
    public async Task<AddOnCheckoutPaymentMethodModel> CreateCheckoutPaymentMethodAsync(string appId, string providerId)
    {
        var url = $"app-tier-addons/{appId}/checkout/payment-method";
        var response = await PostActionAsync(url, new { ProviderId = providerId }, $"CreateCheckoutPaymentMethod({appId})");

        if (response.IsSuccess)
        {
            var result = Deserialize<AddOnCheckoutPaymentMethodModel>(response.Content);
            if (result is not null) return result;
        }

        var error = ToActionError(response, "Failed to start card collection");
        var failure = RefusalBody<AddOnCheckoutPaymentMethodModel>(response.Content, "success", requireString: false)
            ?? new AddOnCheckoutPaymentMethodModel();
        failure.Success = false;
        failure.ErrorCode = error.Code;
        failure.ErrorMessage = error.Message;
        return failure;
    }

    /// <summary>
    /// Buy the basket: one subscription per pack, charged to one card. One pack failing does not
    /// stop the others, so read <c>Results</c> per pack rather than <c>Success</c> alone — a
    /// <c>requires_action</c> line still has to be authenticated and then
    /// <see cref="CompleteAddOnCheckoutAsync"/>d. Never throws.
    /// </summary>
    public async Task<AddOnCheckoutResultModel> CheckoutAddOnsAsync(string appId, AddOnCheckoutRequestModel request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var url = $"app-tier-addons/{appId}/checkout";
        var body = new
        {
            CheckoutId = request.CheckoutId,
            ProviderId = request.ProviderId,
            PaymentTransactionId = request.PaymentTransactionId,
            UseSavedCard = request.UseSavedCard,
            Items = AppTierActionMapper.ToCheckoutItems(request.Items)
        };

        var response = await PostActionAsync(url, body, $"CheckoutAddOns({appId})");

        if (response.IsSuccess)
        {
            var result = Deserialize<AddOnCheckoutResultModel>(response.Content);
            if (result is not null) return result;
        }

        var error = ToActionError(response, "Failed to buy the packs");
        var failure = RefusalBody<AddOnCheckoutResultModel>(response.Content, "success", requireString: false)
            ?? new AddOnCheckoutResultModel { CheckoutId = request.CheckoutId };
        failure.Success = false;
        failure.ErrorCode = error.Code;
        failure.ErrorMessage = error.Message;
        return failure;
    }

    /// <summary>
    /// Finish one pack whose card the customer has just authenticated: the payment is verified
    /// with the provider and, once it really paid, the pack's subscription is created.
    /// Never throws.
    /// </summary>
    public async Task<AddOnCheckoutItemResultModel> CompleteAddOnCheckoutAsync(string appId, string paymentTransactionId)
    {
        var url = $"app-tier-addons/{appId}/checkout/complete";
        var response = await PostActionAsync(
            url, new { PaymentTransactionId = paymentTransactionId }, $"CompleteAddOnCheckout({appId})");

        if (response.IsSuccess)
        {
            var result = Deserialize<AddOnCheckoutItemResultModel>(response.Content);
            if (result is not null) return result;
        }

        var error = ToActionError(response, "Failed to complete the pack purchase");
        // A refusal here IS the per-item result DTO (the controller returns it with the 400/404),
        // so keep the pack it was about rather than answering about nothing.
        var failure = RefusalBody<AddOnCheckoutItemResultModel>(response.Content, "status", requireString: true)
            ?? new AddOnCheckoutItemResultModel();
        failure.Status = AddOnCheckoutItemStatuses.Failed;
        failure.ErrorCode = error.Code;
        failure.ErrorMessage = error.Message;
        return failure;
    }

    #endregion

    #region Public catalog (throwing)

    /// <summary>
    /// The app's public tiers, via the no-auth endpoint, with failures PROPAGATING — the throwing
    /// twin of <see cref="GetPublicTiersAsync"/>, which swallows a failure into an empty list. A
    /// pricing view has to tell "this app sells no plans" from "pricing is unavailable right now",
    /// and an empty list cannot express the second.
    /// </summary>
    public async Task<List<AppTierModel>> GetPublicTiersOrThrowAsync(string appId)
    {
        using var response = await _httpClient.GetAsync($"app-tiers/{appId}/public");

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get public tiers for app {AppId}: {StatusCode}", appId, response.StatusCode);
            throw new HttpRequestException($"GetPublicTiers({appId}) failed: HTTP {(int)response.StatusCode} {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<List<AppTierModel>>(JsonOptions);
        return result ?? new List<AppTierModel>();
    }

    /// <summary>
    /// Everything the app sells — Active tiers and packs in display order, under one resolved
    /// currency (the first the responses named, else <paramref name="currencyOverride"/>, else
    /// USD). Failures propagate, for the same reason <see cref="GetPublicTiersOrThrowAsync"/>'s do.
    /// </summary>
    public async Task<PublicCatalog> GetPublicCatalogAsync(string appId, string? currencyOverride = null)
    {
        var tiers = await GetPublicTiersOrThrowAsync(appId);
        var addOns = await GetPublicAddOnsAsync(appId);
        return CatalogHelpers.BuildPublicCatalog(appId, tiers, addOns, currencyOverride);
    }

    #endregion
}
