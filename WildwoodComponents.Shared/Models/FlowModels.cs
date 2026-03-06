namespace WildwoodComponents.Shared.Models;

public class FlowDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDraft { get; set; }
    public string Version { get; set; } = string.Empty;
    public int MaxExecutionTimeSeconds { get; set; }
    public string? InputSchemaJson { get; set; }
    public string? OutputConfigJson { get; set; }
    public string IconClass { get; set; } = "fas fa-project-diagram";
    public string Color { get; set; } = "teal";
}

public class FlowInputField
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Placeholder { get; set; }
    public string? DefaultValue { get; set; }
}

public class FlowExecution
{
    public string Id { get; set; } = string.Empty;
    public string FlowDefinitionId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? InputDataJson { get; set; }
    public string? OutputDataJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int TotalTokensUsed { get; set; }
    public int TotalAPICallsMade { get; set; }
    public List<FlowStepExecution> StepExecutions { get; set; } = new();
}

public class FlowStepExecution
{
    public string Id { get; set; } = string.Empty;
    public string FlowStepId { get; set; } = string.Empty;
    public string? StepName { get; set; }
    public string? StepType { get; set; }
    public string Status { get; set; } = "pending";
    public string? OutputDataJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int TokensUsed { get; set; }
    public int StepOrder { get; set; }
}

public class FlowExecuteRequest
{
    public string? InputDataJson { get; set; }
}
