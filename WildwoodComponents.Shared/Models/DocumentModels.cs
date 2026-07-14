namespace WildwoodComponents.Shared.Models
{
    /// <summary>
    /// Known lifecycle values for <see cref="AppDocumentModel.Status"/>. The status is carried
    /// as a plain string (mirroring the JS <c>AppDocumentStatus | string</c> open union) so an
    /// unrecognized server value never breaks deserialization; these constants document the set.
    /// </summary>
    public static class AppDocumentStatus
    {
        public const string Uploaded = "uploaded";
        public const string Parsing = "parsing";
        public const string Parsed = "parsed";
        public const string Failed = "failed";
    }

    /// <summary>
    /// A tenant document as returned by WildwoodAPI's <c>api/documents</c> surface.
    /// Mirrors @wildwood/core AppDocumentModel field-for-field (PascalCase per .NET convention).
    /// </summary>
    public class AppDocumentModel
    {
        public string Id { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }

        /// <summary>uploaded | parsing | parsed | failed (see <see cref="AppDocumentStatus"/>).</summary>
        public string Status { get; set; } = string.Empty;

        public string? ParseError { get; set; }
        public int? PageCount { get; set; }
        public int ParsedCharacters { get; set; }
        public string? CompanyClientId { get; set; }

        /// <summary>ISO 8601 timestamp string (kept as string to match the JS contract exactly).</summary>
        public string CreatedAt { get; set; } = string.Empty;

        public string? ParsedAt { get; set; }
    }

    /// <summary>
    /// Result of <c>GET /documents/{id}/text</c>. <see cref="Text"/> is null until parsing
    /// succeeds; while parsing is pending/failed the server responds 409 and the client maps it
    /// to a text-less result (status + error) so callers can poll without special-casing.
    /// </summary>
    public class AppDocumentTextResult
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Characters { get; set; }
        public string? Text { get; set; }

        /// <summary>Parse error / not-ready detail when text is unavailable.</summary>
        public string? Error { get; set; }
    }
}
