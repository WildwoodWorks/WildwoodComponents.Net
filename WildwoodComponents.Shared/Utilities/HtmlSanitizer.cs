using System.Text.RegularExpressions;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Sanitizes HTML content by stripping dangerous tags and attributes
/// while preserving safe content for display.
/// </summary>
public static partial class HtmlSanitizer
{
    private static readonly string[] DangerousTags =
        ["script", "style", "iframe", "object", "embed", "form", "link", "meta"];

    private static readonly string[] DangerousAttributes =
        ["srcdoc", "formaction"];

    private static readonly string[] DangerousSchemes =
        ["javascript:", "data:", "vbscript:"];

    private static readonly string[] UrlAttributes =
        ["href", "src", "action"];

    /// <summary>
    /// Sanitizes HTML by removing dangerous tags, event handler attributes,
    /// and dangerous URL schemes while preserving safe content.
    /// </summary>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var result = html;

        // Remove dangerous tags and their content
        foreach (var tag in DangerousTags)
        {
            result = Regex.Replace(
                result,
                $@"<{tag}\b[^>]*>[\s\S]*?</{tag}>",
                string.Empty,
                RegexOptions.IgnoreCase);

            // Also remove self-closing variants
            result = Regex.Replace(
                result,
                $@"<{tag}\b[^>]*/?\s*>",
                string.Empty,
                RegexOptions.IgnoreCase);
        }

        // Remove event handler attributes (on*) — loop until none remain
        // since each pass only strips one per tag due to regex capture.
        // Split by quote type so a double-quoted value containing single quotes matches fully.
        string previous;
        do
        {
            previous = result;
            // Double-quoted values: onclick="..."
            result = EventHandlerDoubleQuotedRegex().Replace(result, "$1");
            // Single-quoted values: onclick='...'
            result = EventHandlerSingleQuotedRegex().Replace(result, "$1");
            // Unquoted values: onclick=alert(1)
            result = EventHandlerUnquotedRegex().Replace(result, "$1");
        } while (result != previous);

        // Remove dangerous attributes (srcdoc, formaction)
        // Split by quote type so mixed quotes in values are handled correctly
        foreach (var attr in DangerousAttributes)
        {
            // Double-quoted values
            result = Regex.Replace(
                result,
                $@"\s{attr}\s*=\s*""[^""]*""",
                string.Empty,
                RegexOptions.IgnoreCase);

            // Single-quoted values
            result = Regex.Replace(
                result,
                $@"\s{attr}\s*=\s*'[^']*'",
                string.Empty,
                RegexOptions.IgnoreCase);

            // Unquoted values
            result = Regex.Replace(
                result,
                $@"\s{attr}\s*=\s*[^\s""'>]+",
                string.Empty,
                RegexOptions.IgnoreCase);
        }

        // Remove entire attributes with dangerous URL schemes (href, src, action)
        // Matches the React behavior of removing the whole attribute, not just the scheme
        // Split into separate patterns per quote type so a double-quoted value
        // containing single quotes (or vice versa) is matched fully
        foreach (var attr in UrlAttributes)
        {
            foreach (var scheme in DangerousSchemes)
            {
                // Double-quoted attribute values
                result = Regex.Replace(
                    result,
                    $@"\s{attr}\s*=\s*""\s*{Regex.Escape(scheme)}[^""]*""",
                    string.Empty,
                    RegexOptions.IgnoreCase);

                // Single-quoted attribute values
                result = Regex.Replace(
                    result,
                    $@"\s{attr}\s*=\s*'\s*{Regex.Escape(scheme)}[^']*'",
                    string.Empty,
                    RegexOptions.IgnoreCase);

                // Unquoted attribute values
                result = Regex.Replace(
                    result,
                    $@"\s{attr}\s*=\s*{Regex.Escape(scheme)}\S*",
                    string.Empty,
                    RegexOptions.IgnoreCase);
            }
        }

        return result;
    }

    [GeneratedRegex(@"(<[^>]*?)\s+on\w+\s*=\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerDoubleQuotedRegex();

    [GeneratedRegex(@"(<[^>]*?)\s+on\w+\s*=\s*'[^']*'", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerSingleQuotedRegex();

    [GeneratedRegex(@"(<[^>]*?)\s+on\w+\s*=\s*[^\s""'>]+", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerUnquotedRegex();
}
