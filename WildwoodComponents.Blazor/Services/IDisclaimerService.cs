using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    public interface IDisclaimerService
    {
        Task<PendingDisclaimersResponse> GetPendingDisclaimersAsync(string appId, string? userId = null, string? showOn = null);
        Task<DisclaimerAcceptanceResponse> AcceptDisclaimersAsync(string appId, List<DisclaimerAcceptanceResult> acceptances);
    }
}
