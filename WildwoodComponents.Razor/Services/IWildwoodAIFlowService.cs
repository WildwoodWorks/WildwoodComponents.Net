using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public interface IWildwoodAIFlowService
{
    Task<List<FlowDefinitionDto>> GetFlowDefinitionsAsync(string appId);
    Task<FlowDefinitionDto?> GetFlowDefinitionAsync(string appId, string flowId);
    Task<FlowExecution> ExecuteFlowAsync(string appId, string flowId, string? inputDataJson);
    Task<FlowExecution?> GetExecutionStatusAsync(string appId, string executionId);
    Task<bool> CancelExecutionAsync(string appId, string executionId);
}
