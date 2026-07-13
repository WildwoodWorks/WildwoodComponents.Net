using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Client for the tenant document endpoints (api/documents): upload, list, metadata, download,
/// extracted text, and delete. Razor Pages equivalent of
/// WildwoodComponents.Blazor.Services.IDocumentService. Uses the server-side session for JWT token
/// management via IWildwoodSessionManager; the token never reaches the browser. Optional app scope
/// via requestedAppId is passed per call.
/// </summary>
public interface IWildwoodDocumentService
{
    /// <summary>All documents for the current tenant/app, or an empty list on failure.</summary>
    Task<List<AppDocumentModel>> ListAsync(string? appId = null);

    /// <summary>
    /// Uploads one file as multipart form data (field name "file"). Returns the created document
    /// (status "uploaded"; text extraction runs server-side) or null on failure.
    /// </summary>
    Task<AppDocumentModel?> UploadAsync(Stream fileStream, string fileName, string? contentType = null, string? appId = null);

    /// <summary>Document metadata by id, or null when unavailable.</summary>
    Task<AppDocumentModel?> GetAsync(string documentId, string? appId = null);

    /// <summary>
    /// Extracted text. While parsing is pending/failed the server responds 409, mapped here to a
    /// result with Text=null plus the status/error so callers can poll. Null on other failures.
    /// </summary>
    Task<AppDocumentTextResult?> GetTextAsync(string documentId, string? appId = null);

    /// <summary>Original file bytes, or null when unavailable.</summary>
    Task<byte[]?> DownloadAsync(string documentId, string? appId = null);

    /// <summary>Deletes a document; true on success.</summary>
    Task<bool> DeleteAsync(string documentId, string? appId = null);
}
