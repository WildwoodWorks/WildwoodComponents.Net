using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// AI Proxy service for communicating with WildwoodAPI AI endpoints.
/// Supports multiple named configurations and general-purpose AI requests.
/// Razor Pages equivalent of WildwoodComponents AI proxy functionality.
/// </summary>
public interface IWildwoodAIProxyService
{
    /// <summary>
    /// Send an AI request using a named configuration
    /// </summary>
    Task<AIProxyResponse> SendRequestAsync(AIProxyRequest request);

    /// <summary>
    /// Send an AI request with a file attachment (for document analysis)
    /// </summary>
    Task<AIProxyResponse> SendRequestWithFileAsync(AIProxyRequest request, Stream fileStream, string fileName);

    /// <summary>
    /// Get available AI configurations for the current app
    /// </summary>
    Task<List<AIProxyConfigInfo>> GetConfigurationsAsync();
}
