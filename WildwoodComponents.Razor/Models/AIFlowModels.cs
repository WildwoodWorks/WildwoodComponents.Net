using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

public class AIFlowViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string? FlowId { get; set; }
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-ai-flow";
    public List<FlowDefinitionDto> AvailableFlows { get; set; } = new();
    public FlowDefinitionDto? SelectedFlow { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

/// <summary>
/// Razor-specific flow definition with parsed InputFields.
/// Extends the Shared FlowDefinition with UI-specific InputFields list
/// populated by ParseInputSchema in the ViewComponent.
/// </summary>
public class FlowDefinitionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsDraft { get; set; }
    public int? Version { get; set; }
    public int? MaxExecutionTimeSeconds { get; set; }
    public string? InputSchemaJson { get; set; }
    public string? IconClass { get; set; }
    public string? Color { get; set; }
    public List<FlowInputField> InputFields { get; set; } = new();
}
