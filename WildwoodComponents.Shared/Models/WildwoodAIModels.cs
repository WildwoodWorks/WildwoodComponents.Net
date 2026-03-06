namespace WildwoodComponents.Shared.Models;

/// <summary>
/// Request DTO for WildwoodAPI AI chat endpoint.
/// Maps to WildwoodAPI's AIRequestDto contract.
/// </summary>
public class WildwoodAIRequestDto
{
    public string? ConfigurationId { get; set; }
    public string? SessionId { get; set; }
    public string? Message { get; set; }
    public bool SaveToSession { get; set; }
    public Dictionary<string, string> MacroValues { get; set; } = new();

    /// <summary>
    /// Base64-encoded file content for multimodal/document analysis.
    /// When present, the request is treated as multimodal by the AI provider.
    /// </summary>
    public string? FileBase64 { get; set; }

    /// <summary>
    /// MIME type of the attached file (e.g., "image/png", "application/pdf").
    /// Required when FileBase64 is provided.
    /// </summary>
    public string? FileMediaType { get; set; }

    /// <summary>
    /// Original filename of the attached file.
    /// </summary>
    public string? FileName { get; set; }
}

/// <summary>
/// Response DTO from WildwoodAPI AI chat endpoint.
/// Maps to WildwoodAPI's AIResponseDto contract.
/// </summary>
public class WildwoodAIResponseDto
{
    public string? Id { get; set; }
    public string? SessionId { get; set; }
    public string? Response { get; set; }
    public int TokensUsed { get; set; }
    public string? Model { get; set; }
    public string? ProviderTypeCode { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Maps common file extensions to MIME types for AI provider multimodal support.
/// </summary>
public static class FileMediaTypeHelper
{
    public static string GetMediaTypeFromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }
}
