namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing UI helper methods for AIFlowComponent.
/// </summary>
public partial class AIFlowComponent
{
    private static string GetStatusBadgeClass(string status)
    {
        return status switch
        {
            "pending" => "bg-secondary",
            "running" => "bg-primary",
            "completed" => "bg-success",
            "failed" => "bg-danger",
            "cancelled" => "bg-warning text-dark",
            "timed_out" => "bg-warning text-dark",
            _ => "bg-secondary"
        };
    }

    private static string GetStepStatusIcon(string status)
    {
        return status switch
        {
            "pending" => "fas fa-circle text-muted",
            "running" => "fas fa-spinner fa-spin text-primary",
            "completed" => "fas fa-check-circle text-success",
            "failed" => "fas fa-times-circle text-danger",
            "skipped" => "fas fa-minus-circle text-secondary",
            _ => "fas fa-circle text-muted"
        };
    }

    private static string GetStepRowClass(string status)
    {
        return status switch
        {
            "running" => "bg-primary bg-opacity-10",
            "completed" => "bg-success bg-opacity-10",
            "failed" => "bg-danger bg-opacity-10",
            "skipped" => "bg-light text-muted",
            _ => ""
        };
    }

    private bool TryGetFormattedOutput(string? outputJson, out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(outputJson)) return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(outputJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
            }
            return result.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private string FormatTextOutput(string text)
    {
        // Convert newlines to <br> for display, and escape HTML
        return System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br />");
    }

    /// <summary>
    /// Returns output entries excluding known special keys (response, audioBase64, format, contentType, stub).
    /// Uses a regular loop instead of LINQ to avoid iOS/AOT runtime crashes.
    /// </summary>
    private List<KeyValuePair<string, string>> GetFilteredOutputEntries(Dictionary<string, string> output)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var kvp in output)
        {
            if (kvp.Key != "response" && kvp.Key != "audioBase64" && kvp.Key != "format" && kvp.Key != "contentType" && kvp.Key != "stub")
            {
                result.Add(kvp);
            }
        }
        return result;
    }
}
