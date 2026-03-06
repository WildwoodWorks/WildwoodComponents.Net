using System.Diagnostics;
using System.Text;

namespace WildwoodComponentsTestSuiteBlazor.Services;

public class DebugHttpInterceptor : DelegatingHandler
{
    private readonly IDebugEventService _debugEvents;

    public DebugHttpInterceptor(IDebugEventService debugEvents)
    {
        _debugEvents = debugEvents;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var entry = new HttpTrafficEntry
        {
            Method = request.Method.Method,
            Url = request.RequestUri?.ToString() ?? "",
            RequestHeaders = FormatHeaders(request.Headers),
            RequestBody = await ReadContentAsync(request.Content)
        };

        var sw = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            entry.DurationMs = sw.ElapsedMilliseconds;
            entry.StatusCode = 0;
            entry.ResponseBody = $"Exception: {ex.Message}";
            _debugEvents.EmitHttpTraffic(entry);
            throw;
        }

        sw.Stop();
        entry.StatusCode = (int)response.StatusCode;
        entry.DurationMs = sw.ElapsedMilliseconds;
        entry.ResponseHeaders = FormatHeaders(response.Headers);

        // Read response body without consuming it
        if (response.Content != null)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            entry.ResponseBody = responseBody.Length > 4096
                ? responseBody[..4096] + "... (truncated)"
                : responseBody;
        }

        _debugEvents.EmitHttpTraffic(entry);
        return response;
    }

    private static string FormatHeaders(System.Net.Http.Headers.HttpHeaders headers)
    {
        var sb = new StringBuilder();
        foreach (var header in headers)
        {
            // Mask authorization headers
            var value = header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                ? "Bearer ***"
                : string.Join(", ", header.Value);
            sb.AppendLine($"{header.Key}: {value}");
        }
        return sb.ToString();
    }

    private static async Task<string?> ReadContentAsync(HttpContent? content)
    {
        if (content == null) return null;
        var body = await content.ReadAsStringAsync();
        return body.Length > 4096 ? body[..4096] + "... (truncated)" : body;
    }
}
