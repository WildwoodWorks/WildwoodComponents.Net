using System.Collections.Generic;
using System.Text.Json;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// The pure part of the tier/pack action surface: turning a failed HTTP attempt into the
/// structured refusal those actions report instead of throwing, and reading a refusal body back
/// as the result DTO the server answered with.
///
/// Port of <c>toAppTierActionError</c> / <c>failedResult</c> in
/// packages/wildwood-core/src/features/appTierService.ts. It lives in Shared because Blazor and
/// Razor need byte-identical wording and codes — two copies of this rule would drift the moment
/// one of them gained a branch. Nothing here touches HTTP: callers hand it the status, the body
/// and (for a request that never reached the server) the transport message.
/// </summary>
public static class AppTierActionMapper
{
    /// <summary>
    /// The refusal a tier/pack action reports for a failed attempt.
    ///
    /// The server's own error code wins whenever it sent one. A 404 that carries NO code is the
    /// one case worth naming: the route itself is absent, i.e. the server predates this SDK, so it
    /// becomes <see cref="AppTierActionErrorCodes.NotSupported"/> rather than being reported as a
    /// missing subscription. Anything else without a code — a network failure, a 500, a bare 400 —
    /// is <see cref="AppTierActionErrorCodes.RequestFailed"/>. The message is never empty.
    /// </summary>
    /// <param name="status">HTTP status when the request reached the server, else null.</param>
    /// <param name="body">The response body, success or refusal alike.</param>
    /// <param name="transportMessage">The exception message when the request never got an answer.</param>
    /// <param name="fallbackMessage">Wording of last resort, never shown as an empty string.</param>
    public static AppTierActionError ToActionError(
        int? status, string? body, string? transportMessage, string fallbackMessage)
    {
        var parsed = TryParseObject(body);
        var code = StringField(parsed, "errorCode", "code");
        var message = StringField(parsed, "errorMessage", "message", "error", "title");

        if (message is null && !string.IsNullOrWhiteSpace(transportMessage))
            message = transportMessage;

        if (message is null && status.HasValue)
            message = $"Request failed (HTTP {status.Value})";

        if (string.IsNullOrWhiteSpace(message))
            message = fallbackMessage;

        if (status.HasValue)
        {
            return new AppTierActionError
            {
                Code = code ?? (status.Value == 404
                    ? AppTierActionErrorCodes.NotSupported
                    : AppTierActionErrorCodes.RequestFailed),
                Message = message!,
                Status = status.Value
            };
        }

        return new AppTierActionError
        {
            Code = code ?? AppTierActionErrorCodes.RequestFailed,
            Message = message!
        };
    }

    /// <summary>
    /// The refusal body read back as the result DTO, but only when the server really sent that
    /// DTO — recognised by a property of the expected kind (JS checks
    /// <c>typeof body.success === 'boolean'</c> / <c>typeof body.status === 'string'</c>).
    /// Otherwise null, and the caller keeps its own empty result. The checkout endpoints answer a
    /// refusal with the SAME DTO they answer a success with, so this is what keeps the server's
    /// fields (the checkout id, the pack a failure was about) instead of discarding them.
    /// </summary>
    public static T? RefusalBody<T>(
        string? content, string requiredProperty, bool requireString, JsonSerializerOptions? options = null)
        where T : class
    {
        var body = TryParseObject(content);
        if (body is null) return null;
        if (!TryGetPropertyIgnoreCase(body.Value, requiredProperty, out var probe)) return null;

        var matches = requireString
            ? probe.ValueKind == JsonValueKind.String
            : probe.ValueKind == JsonValueKind.True || probe.ValueKind == JsonValueKind.False;
        if (!matches) return null;

        try { return JsonSerializer.Deserialize<T>(content!, options); }
        catch (JsonException) { return null; }
    }

    /// <summary>The PascalCase basket the checkout endpoints bind, with nulls dropped.</summary>
    public static List<AddOnCheckoutItemInput> ToCheckoutItems(IReadOnlyList<AddOnCheckoutItemInput>? items)
    {
        var mapped = new List<AddOnCheckoutItemInput>();
        if (items is null) return mapped;

        foreach (var item in items)
        {
            if (item is null) continue;
            mapped.Add(new AddOnCheckoutItemInput { AddOnId = item.AddOnId, PricingId = item.PricingId });
        }

        return mapped;
    }

    /// <summary>The body as a JSON object, or null when it is absent, unparseable or not an object.</summary>
    public static JsonElement? TryParseObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            using var document = JsonDocument.Parse(content!);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The first non-blank string among <paramref name="keys"/>, matched case-insensitively
    /// because a server may answer camelCase or PascalCase.
    /// </summary>
    public static string? StringField(JsonElement? body, params string[] keys)
    {
        if (body is null) return null;
        foreach (var key in keys)
        {
            if (TryGetPropertyIgnoreCase(body.Value, key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
