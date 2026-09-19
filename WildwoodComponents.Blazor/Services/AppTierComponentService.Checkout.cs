using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Services
{
    // Port of the pack-checkout / 3-D Secure plan-change / trial-eligibility surface in
    // packages/wildwood-core/src/features/appTierService.ts.
    //
    // Every method here reports an HTTP refusal as DATA and never throws: the server answers a
    // refusal with the same result DTO it answers a success with, and the caller has to be able to
    // word "you already own that pack" differently from "that route does not exist yet".
    public partial class AppTierComponentService
    {
        #region Structured failure plumbing

        /// <summary>
        /// One attempt at a request: the body the server sent (success or refusal alike), the
        /// status when the call reached the server, or the exception when it never did.
        /// </summary>
        private sealed class ActionResponse
        {
            public bool IsSuccess { get; init; }
            public string? Content { get; init; }
            public HttpStatusCode? Status { get; init; }
            public Exception? Exception { get; init; }
        }

        private async Task<ActionResponse> SendActionAsync(string url, HttpContent? content, bool isPost, string operation)
        {
            try
            {
                var response = isPost
                    ? await _httpClient.PostAsync(url, content)
                    : await _httpClient.GetAsync(url);

                string? body = null;
                try { body = await response.Content.ReadAsStringAsync(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read response body for {Operation}", operation); }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("{Operation} failed: HTTP {StatusCode} {Status}",
                        operation, (int)response.StatusCode, response.StatusCode);
                }

                return new ActionResponse
                {
                    IsSuccess = response.IsSuccessStatusCode,
                    Content = body,
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
        {
            HttpContent? content = body is null
                ? null
                : new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            return SendActionAsync(url, content, isPost: true, operation);
        }

        private Task<ActionResponse> GetActionAsync(string url, string operation)
            => SendActionAsync(url, null, isPost: false, operation);

        /// <summary>
        /// Turn a failed request into the structured refusal the tier/pack actions report.
        ///
        /// The server's own error code wins whenever it sent one. A 404 that carries NO code is
        /// the one case worth naming: the route itself is absent, i.e. the server predates this
        /// SDK, so it becomes <see cref="AppTierActionErrorCodes.NotSupported"/> rather than being
        /// reported as a missing subscription. Anything else without a code — a network failure, a
        /// 500, a bare 400 — is <see cref="AppTierActionErrorCodes.RequestFailed"/>. The message is
        /// never empty.
        /// </summary>
        private static AppTierActionError ToActionError(ActionResponse response, string fallbackMessage)
        {
            var body = TryParseObject(response.Content);
            var code = StringField(body, "errorCode", "code");
            var message = StringField(body, "errorMessage", "message", "error", "title");

            if (message is null && response.Exception is not null && !string.IsNullOrWhiteSpace(response.Exception.Message))
                message = response.Exception.Message;

            if (message is null && response.Status.HasValue)
                message = $"Request failed (HTTP {(int)response.Status.Value})";

            if (string.IsNullOrWhiteSpace(message))
                message = fallbackMessage;

            if (response.Status.HasValue)
            {
                return new AppTierActionError
                {
                    Code = code ?? (response.Status.Value == HttpStatusCode.NotFound
                        ? AppTierActionErrorCodes.NotSupported
                        : AppTierActionErrorCodes.RequestFailed),
                    Message = message!,
                    Status = (int)response.Status.Value
                };
            }

            return new AppTierActionError
            {
                Code = code ?? AppTierActionErrorCodes.RequestFailed,
                Message = message!
            };
        }

        private static JsonElement? TryParseObject(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            try
            {
                using var document = JsonDocument.Parse(content!);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
        {
            if (element.TryGetProperty(name, out value)) return true;
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string? StringField(JsonElement? body, params string[] keys)
        {
            if (body is null) return null;
            foreach (var key in keys)
            {
                if (TryGetPropertyIgnoreCase(body.Value, key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }

            return null;
        }

        private static T? Deserialize<T>(string? content) where T : class
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            try { return JsonSerializer.Deserialize<T>(content!, JsonOptions); }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// The refusal body read back as the result DTO, but only when the server really sent that
        /// DTO — recognised by a property of the expected kind (JS checks
        /// <c>typeof body.success === 'boolean'</c> / <c>typeof body.status === 'string'</c>).
        /// Otherwise null, and the caller keeps its own empty result.
        /// </summary>
        private static T? RefusalBody<T>(string? content, string requiredProperty, bool requireString) where T : class
        {
            var body = TryParseObject(content);
            if (body is null) return null;
            if (!TryGetPropertyIgnoreCase(body.Value, requiredProperty, out var probe)) return null;

            var matches = requireString
                ? probe.ValueKind == JsonValueKind.String
                : probe.ValueKind == JsonValueKind.True || probe.ValueKind == JsonValueKind.False;
            if (!matches) return null;

            return Deserialize<T>(content);
        }

        /// <summary>The PascalCase basket the checkout endpoints bind.</summary>
        private static List<AddOnCheckoutItemInput> ToCheckoutItems(IReadOnlyList<AddOnCheckoutItemInput>? items)
        {
            var mapped = new List<AddOnCheckoutItemInput>();
            if (items is null) return mapped;

            foreach (var item in items)
            {
                if (item is null) continue;
                mapped.Add(new AddOnCheckoutItemInput { AddOnId = item.AddOnId, PricingId = item.PricingId });
            }

            return mapped;
        }

        #endregion

        #region Trial eligibility

        /// <summary>
        /// Whether this account may still start a free trial in the app — on a tier, and per pack.
        ///
        /// Never throws: a failure answers "eligible", which is what a signup screen already shows
        /// from the catalog's trial days, and an empty <c>AddOns</c> map means "unknown" for the
        /// same reason. The checkout quote and the payment initiation re-decide authoritatively
        /// before any money moves.
        /// </summary>
        public async Task<TrialEligibilityModel> GetTrialEligibilityAsync(string appId)
        {
            var url = BuildUrl($"app-tiers/{appId}/trial-eligibility");
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
        /// Options form of the self-service tier change, for a caller that can finish a payment.
        /// With <see cref="SelfChangeTierOptions.SupportsPaymentAction"/> a change whose proration
        /// needs 3-D Secure comes back with <c>RequiresAction</c>, a client secret and a pending
        /// change id to confirm and then <see cref="CompleteTierChangeAsync"/>, instead of being
        /// refused outright.
        ///
        /// Only this overload sends <c>SupportsPaymentAction</c>: the positional
        /// <see cref="ChangeTierAsync(string, string, string?, bool, string?)"/> posts exactly what
        /// it always did, because an older server rejects an unknown property and a caller that
        /// never opted into finishing a payment must not appear to have.
        /// </summary>
        public async Task<AppTierChangeResultModel> ChangeTierAsync(string appId, SelfChangeTierOptions options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));

            var url = BuildUrl($"app-tiers/{appId}/my-subscription/change");
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
        /// been confirmed. Safe to call repeatedly: the server asks the processor whether the
        /// invoice really paid before it moves anything, and answers <c>Processing</c> while it
        /// waits. Never throws.
        /// </summary>
        public async Task<AppTierChangeResultModel> CompleteTierChangeAsync(string appId, string pendingChangeId)
        {
            var changeId = Uri.EscapeDataString(pendingChangeId ?? string.Empty);
            var url = BuildUrl($"app-tiers/{appId}/my-subscription/change/{changeId}/complete");
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
        /// Subscribe to a single pack. Returns the created subscription, or a structured refusal —
        /// a pack already owned, one bundled in the tier, and a payment the server would not accept
        /// are all different problems a UI has to word differently. Never throws.
        /// </summary>
        public async Task<AddOnSubscribeResultModel> SubscribeToAddOnDetailedAsync(
            string appId, string addOnId, string? pricingId = null, string? paymentTransactionId = null)
        {
            var url = BuildUrl($"app-tier-addons/{appId}/subscribe");
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
        /// Cancel one of the calling user's own packs. By default access continues to the end of
        /// the period already paid for (<c>IsScheduled</c>); <paramref name="immediate"/> ends it
        /// now. Never throws.
        /// </summary>
        public async Task<AddOnSubscriptionCancelResultModel> CancelAddOnDetailedAsync(string subscriptionId, bool immediate = false)
        {
            // Lowercase true/false, byte-identical to the JS `?immediate=${immediate}`.
            var immediateFlag = immediate ? "true" : "false";
            var url = BuildUrl($"app-tier-addons/subscriptions/{subscriptionId}/cancel?immediate={immediateFlag}");
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
            var url = BuildUrl($"app-tier-addons/subscriptions/{subscriptionId}/reactivate");
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
        /// Price a basket of packs. Server-authoritative: the price, billing frequency, trial
        /// length and trial eligibility of every line come from the catalog and the account, not
        /// from this request. The returned <c>CheckoutId</c> is echoed back on
        /// <see cref="CheckoutAddOnsAsync"/> and is what makes the purchase idempotent.
        /// Never throws.
        /// </summary>
        public async Task<AddOnCheckoutQuoteModel> QuoteAddOnCheckoutAsync(string appId, IReadOnlyList<AddOnCheckoutItemInput>? items)
        {
            var url = BuildUrl($"app-tier-addons/{appId}/checkout/quote");
            var response = await PostActionAsync(url, new { Items = ToCheckoutItems(items) }, $"QuoteAddOnCheckout({appId})");

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
        /// client secret, then pass the payment transaction id to
        /// <see cref="CheckoutAddOnsAsync"/>. Never throws.
        /// </summary>
        public async Task<AddOnCheckoutPaymentMethodModel> CreateCheckoutPaymentMethodAsync(string appId, string providerId)
        {
            var url = BuildUrl($"app-tier-addons/{appId}/checkout/payment-method");
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
        /// Buy the basket: one subscription per pack, charged to one card. One pack failing does
        /// not stop the others, so read <c>Results</c> per pack rather than <c>Success</c> alone —
        /// a <c>requires_action</c> line still has to be authenticated and then
        /// <see cref="CompleteAddOnCheckoutAsync"/>d. Never throws.
        /// </summary>
        public async Task<AddOnCheckoutResultModel> CheckoutAddOnsAsync(string appId, AddOnCheckoutRequestModel request)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            var url = BuildUrl($"app-tier-addons/{appId}/checkout");
            var body = new
            {
                CheckoutId = request.CheckoutId,
                ProviderId = request.ProviderId,
                PaymentTransactionId = request.PaymentTransactionId,
                UseSavedCard = request.UseSavedCard,
                Items = ToCheckoutItems(request.Items)
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
            var url = BuildUrl($"app-tier-addons/{appId}/checkout/complete");
            var response = await PostActionAsync(
                url, new { PaymentTransactionId = paymentTransactionId }, $"CompleteAddOnCheckout({appId})");

            if (response.IsSuccess)
            {
                var result = Deserialize<AddOnCheckoutItemResultModel>(response.Content);
                if (result is not null) return result;
            }

            var error = ToActionError(response, "Failed to complete the pack purchase");
            // A refusal here IS the per-item result DTO (the controller returns it with the
            // 400/404), so keep the pack it was about rather than answering about nothing.
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
        /// The app's public tiers, via the no-auth endpoint, with failures PROPAGATING — the
        /// throwing twin of <see cref="GetPublicTiersAsync"/>, which swallows a failure into an
        /// empty list. A pricing view has to tell "this app sells no plans" from "pricing is
        /// unavailable right now", and an empty list cannot express the second.
        /// </summary>
        public async Task<List<AppTierModel>> GetPublicTiersOrThrowAsync(string appId)
        {
            var url = BuildUrl($"app-tiers/{appId}/public");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetPublicTiers({appId})");
            var result = await response.Content.ReadFromJsonAsync<List<AppTierModel>>(JsonOptions);
            return result ?? new List<AppTierModel>();
        }

        /// <summary>
        /// Everything the app sells — Active tiers and packs in display order, under one resolved
        /// currency (the first the responses named, else <paramref name="currencyOverride"/>, else
        /// USD). Failures propagate, for the same reason
        /// <see cref="GetPublicTiersOrThrowAsync"/>'s do.
        /// </summary>
        public async Task<PublicCatalog> GetPublicCatalogAsync(string appId, string? currencyOverride = null)
        {
            var tiers = await GetPublicTiersOrThrowAsync(appId);
            var addOns = await GetPublicAddOnsAsync(appId);
            return CatalogHelpers.BuildPublicCatalog(appId, tiers, addOns, currencyOverride);
        }

        #endregion
    }
}
