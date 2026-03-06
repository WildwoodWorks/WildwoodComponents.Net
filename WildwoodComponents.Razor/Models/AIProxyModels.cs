using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

// ──────────────────────────────────────────────
// Client-facing request/response DTOs
// ──────────────────────────────────────────────

/// <summary>
/// AI proxy request DTO. Uses "request/response" terminology, not "chat".
/// </summary>
public class AIProxyRequest
{
    /// <summary>
    /// Name of the AI configuration to use (e.g., "form-builder", "document-analyzer")
    /// </summary>
    public string? ConfigurationName { get; set; }

    /// <summary>
    /// The user's prompt/request text
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Optional context data to include with the request (e.g., current template structure)
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Optional configuration ID (alternative to ConfigurationName for direct reference)
    /// </summary>
    public string? ConfigurationId { get; set; }

    /// <summary>
    /// Optional session ID for multi-turn interactions
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Whether to save the request/response to the session
    /// </summary>
    public bool SaveToSession { get; set; }
}

/// <summary>
/// AI proxy response DTO
/// </summary>
public class AIProxyResponse
{
    public bool Succeeded { get; set; }
    public string? Content { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SessionId { get; set; }
    public string? Model { get; set; }
    public AIProxyUsage? Usage { get; set; }

    public static AIProxyResponse Error(string message) => new()
    {
        Succeeded = false,
        ErrorMessage = message
    };

    internal static AIProxyResponse FromWildwoodResponse(WildwoodAIResponseDto ww) => new()
    {
        Succeeded = true,
        Content = ww.Response,
        SessionId = ww.SessionId,
        Model = ww.Model,
        Usage = new AIProxyUsage { TotalTokens = ww.TokensUsed }
    };
}

/// <summary>
/// Token usage information from an AI request
/// </summary>
public class AIProxyUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

/// <summary>
/// Information about an available AI configuration
/// </summary>
public class AIProxyConfigInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string? ProviderTypeCode { get; set; }
    public string? Model { get; set; }
}

// WildwoodAPI-specific DTOs now live in WildwoodComponents.Shared.Models
// (WildwoodAIRequestDto, WildwoodAIResponseDto)
