using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// The Campaign Attribution claim for a sign-in provider signup (mirrors @wildwood/core claimAttribution). A provider
    /// signup has no registration request to carry the captured campaign tags, so once the user holds a JWT the tags are
    /// claimed against the account. AuthenticationComponent calls this after a popup provider sign-in; hosts that sign in
    /// through <see cref="IAuthenticationService.LoginAsync"/> with a provider token can call it themselves.
    /// </summary>
    public static class AttributionServiceExtensions
    {
        /// <summary>How long a claim may take before it is abandoned. A claim must never hold up a sign-in.</summary>
        public static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Claims the captured attribution for the signed-in user via <c>POST api/attribution/claim?appId=</c>. Sends
        /// nothing when the app id, the token or the captured payload is empty. Any 2xx clears the captured touches,
        /// whether or not the server recorded them, because that answer is final for those touches. An error status,
        /// an exception or a timeout keeps the touches and returns null. Never throws.
        /// </summary>
        /// <param name="attribution">The attribution engine holding the captured touches.</param>
        /// <param name="http">A client whose BaseAddress is the WildwoodAPI host root, ending in a slash.</param>
        /// <param name="appId">The app the user signed in to.</param>
        /// <param name="jwtToken">The signed-in user's access token.</param>
        /// <param name="cancellationToken">Cancels the claim; <see cref="ClaimTimeout"/> applies as well.</param>
        public static async Task<AttributionClaimResultModel?> ClaimAsync(
            this IAttributionService attribution,
            HttpClient http,
            string? appId,
            string? jwtToken,
            CancellationToken cancellationToken = default)
        {
            if (attribution is null || http is null || string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(jwtToken))
            {
                return null;
            }

            try
            {
                var payload = await attribution.GetForRegistrationAsync();
                if (payload is null)
                {
                    return null;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ClaimTimeout);

                // appId rides in the query string too: the server's rate-limit partition reads it and it must match the body.
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/attribution/claim?appId=" + Uri.EscapeDataString(appId))
                {
                    Content = JsonContent.Create(AttributionClaimRequestModel.From(appId, payload))
                };
                // On the request, not DefaultRequestHeaders: the client may be shared.
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

                using var response = await http.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                AttributionClaimResultModel? result = null;
                try
                {
                    result = await response.Content.ReadFromJsonAsync<AttributionClaimResultModel>(cancellationToken: timeout.Token);
                }
                catch (Exception)
                {
                    // A 2xx without a readable body is still the server's answer for these touches.
                }

                await attribution.ClearAsync();
                return result ?? new AttributionClaimResultModel();
            }
            catch (Exception)
            {
                // Attribution is best-effort: a network failure, a timeout or a disconnected circuit keeps the touches.
                return null;
            }
        }
    }
}
