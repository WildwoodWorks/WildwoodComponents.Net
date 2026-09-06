using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    public interface IDisclaimerService
    {
        /// <summary>
        /// Supplies the JWT sent as a bearer token on the acceptance calls. Both
        /// <c>disclaimeracceptance/accept</c> endpoints are <c>[Authorize]</c>, so a caller that
        /// never sets this gets a 401 the moment the user accepts. Pass null to clear it.
        /// </summary>
        /// <remarks>
        /// The token is available to the component before the accept call: a login that requires
        /// disclaimer acceptance still returns the JWT alongside the pending list.
        /// </remarks>
        void SetAuthToken(string? token);

        Task<PendingDisclaimersResponse> GetPendingDisclaimersAsync(string appId, string? userId = null, string? showOn = null);
        Task<DisclaimerAcceptanceResponse> AcceptDisclaimerAsync(string appId, string companyDisclaimerId, string companyDisclaimerVersionId);
        Task<DisclaimerAcceptanceResponse> AcceptDisclaimersAsync(string appId, List<DisclaimerAcceptanceResult> acceptances);
    }
}
