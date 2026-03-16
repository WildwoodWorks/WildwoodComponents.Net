using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public interface IWildwoodDisclaimerService
{
    Task<PendingDisclaimersResponse?> GetPendingDisclaimersAsync(string appId, string? userId = null, string? showOn = null);
    Task<ApiResult> AcceptDisclaimersAsync(string appId, List<DisclaimerAcceptanceResult> acceptances);
}
