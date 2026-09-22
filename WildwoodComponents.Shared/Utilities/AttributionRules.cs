using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Server-side Campaign Attribution capture: the C# port of <c>@wildwood/core</c>'s
/// <c>attribution/attributionRules.ts</c>, and of the same rules in the two browser engines
/// (<c>WildwoodComponents.Blazor/wwwroot/js/wildwood-attribution.js</c>,
/// <c>WildwoodComponents.Razor/wwwroot/js/attribution.js</c>). Keep the four in step.
/// </summary>
/// <remarks>
/// <para>
/// This exists for the server-rendered stack: WebForms captures a landing during the request rather
/// than in the browser, and has to apply the same normalization the JS engines do, because
/// WildwoodAPI re-applies these rules and drops anything that does not fit.
/// </para>
/// <para>
/// Every value is attacker-supplied — anyone can append a query string to a public URL — so anything
/// that cannot be trusted is DROPPED rather than cleaned, and nothing here throws.
/// </para>
/// </remarks>
public static class AttributionRules
{
    /// <summary>Maximum length of <c>utm_source</c> and <c>utm_medium</c>.</summary>
    public const int SourceMediumMaxLength = 100;

    /// <summary>Maximum length of <c>utm_campaign</c>, <c>utm_term</c> and <c>utm_content</c>.</summary>
    public const int CampaignTermContentMaxLength = 200;

    /// <summary>Maximum length of a host name.</summary>
    public const int HostMaxLength = 253;

    /// <summary>Maximum length of a landing path.</summary>
    public const int PathMaxLength = 500;

    /// <summary>Maximum number of allowlisted extra parameters carried on a touch.</summary>
    public const int MaxExtraParamNames = 10;

    /// <summary>Maximum serialized length of the extra-parameter map.</summary>
    public const int ExtraParamsJsonMaxLength = 2000;

    /// <summary>Smallest attribution window the server accepts, in days.</summary>
    public const int MinWindowDays = 1;

    /// <summary>Largest attribution window the server accepts, in days.</summary>
    public const int MaxWindowDays = 365;

    /// <summary>The window used when the app config has not been read, matching the SDK engines.</summary>
    public const int DefaultWindowDays = 30;

    /// <summary>Ad-platform click-id parameters, in the priority order the SDKs read them.</summary>
    public static readonly string[] ClickIdParams =
    {
        "gclid", "gbraid", "wbraid", "fbclid", "msclkid", "ttclid", "li_fat_id", "twclid", "rdt_cid"
    };

    private static readonly Regex ParamName = new Regex("^[a-z0-9_]{1,32}$", RegexOptions.Compiled);
    private static readonly Regex ClickIdValue = new Regex("^[A-Za-z0-9._~-]{1,200}$", RegexOptions.Compiled);
    private static readonly Regex VisitorKeyPattern = new Regex("^[A-Za-z0-9_-]{8,100}$", RegexOptions.Compiled);

    /// <summary>
    /// Parses one campaign touch from a landing URL and the referring URL. Returns <c>null</c> for a
    /// direct visit: no UTM tag, no click id, no external referrer and no allowlisted extra parameter.
    /// A direct visit never overwrites what an earlier campaign captured.
    /// </summary>
    /// <param name="url">The absolute landing URL.</param>
    /// <param name="referrer">The referring URL, when the request carried one.</param>
    /// <param name="options">Capture options; defaults are used when null.</param>
    /// <param name="nowUtc">Capture time; <see cref="DateTimeOffset.UtcNow"/> when null.</param>
    public static AttributionTouchModel? ParseTouch(
        string? url,
        string? referrer,
        AttributionCaptureOptions? options = null,
        DateTimeOffset? nowUtc = null)
    {
        var landing = ParseHttpUrl(url);
        if (landing is null)
        {
            return null;
        }

        options ??= new AttributionCaptureOptions();
        var parameters = ParseQuery(landing.Query);

        var source = NormalizeToken(Lookup(parameters, "utm_source"), SourceMediumMaxLength, lowercase: true);
        var medium = NormalizeToken(Lookup(parameters, "utm_medium"), SourceMediumMaxLength, lowercase: true);
        var campaign = NormalizeToken(Lookup(parameters, "utm_campaign"), CampaignTermContentMaxLength, lowercase: false);
        var term = NormalizeToken(Lookup(parameters, "utm_term"), CampaignTermContentMaxLength, lowercase: false);
        var content = NormalizeToken(Lookup(parameters, "utm_content"), CampaignTermContentMaxLength, lowercase: false);

        string? clickIdName = null;
        string? clickIdValue = null;
        if (options.CaptureClickIds)
        {
            foreach (var name in ClickIdParams)
            {
                var raw = Lookup(parameters, name);
                var value = raw is null ? string.Empty : raw.Trim();
                if (value.Length > 0 && ClickIdValue.IsMatch(value))
                {
                    clickIdName = name;
                    clickIdValue = value;
                    break;
                }
            }
        }

        var extraParams = ReadExtraParams(parameters, options.ExtraAllowedParamNames);

        string? referrerHost = null;
        if (options.CaptureReferrer && referrer is { Length: > 0 })
        {
            var referrerUrl = ParseHttpUrl(referrer);
            var host = referrerUrl is null ? null : HostName(referrerUrl, stripWww: true, includePort: false);
            // A self-referral (in-site navigation) is not a campaign.
            if (host is not null && !string.Equals(host, HostName(landing, stripWww: true, includePort: false), StringComparison.Ordinal))
            {
                referrerHost = host;
            }
        }

        var hasUtm = source is not null || medium is not null || campaign is not null || term is not null || content is not null;
        if (!hasUtm && referrerHost is not null)
        {
            source = Truncate(referrerHost, SourceMediumMaxLength);
            medium = "referral";
        }

        if (!hasUtm && clickIdName is null && referrerHost is null && extraParams is null)
        {
            return null;
        }

        return new AttributionTouchModel
        {
            Source = source,
            Medium = medium,
            Campaign = campaign,
            Term = term,
            Content = content,
            ClickIdName = clickIdName,
            ClickIdValue = clickIdValue,
            ReferrerHost = referrerHost,
            LandingHost = HostName(landing, stripWww: false, includePort: true),
            LandingPath = NormalizePath(landing.AbsolutePath),
            ExtraParams = extraParams,
            // The same shape as JavaScript's toISOString(), which is what the server and the browser
            // engines both write.
            OccurredAt = (nowUtc ?? DateTimeOffset.UtcNow).UtcDateTime
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Trims, rejects a value carrying control or invisible-format characters, optionally lowercases,
    /// and caps the length without splitting a surrogate pair. Null for anything empty.
    /// </summary>
    public static string? NormalizeToken(string? value, int maxLength, bool lowercase)
    {
        if (value is null)
        {
            return null;
        }

        var token = value.Trim();
        if (token.Length == 0 || HasControlOrFormat(token))
        {
            return null;
        }

        if (lowercase)
        {
            token = token.ToLowerInvariant();
        }

        token = Truncate(token, maxLength).TrimEnd();
        return token.Length == 0 ? null : token;
    }

    /// <summary>Whether the value is a visitor key the server accepts (8-100 of [A-Za-z0-9_-]).</summary>
    public static bool IsValidVisitorKey(string? value)
        => value is { Length: > 0 } && VisitorKeyPattern.IsMatch(value);

    /// <summary>A random visitor key the server accepts.</summary>
    public static string GenerateVisitorKey() => Guid.NewGuid().ToString();

    /// <summary>
    /// Clamps an attribution window to the range the server accepts. A non-positive value means
    /// "unset" and takes <paramref name="fallback"/>.
    /// </summary>
    public static int ClampWindowDays(int value, int fallback = DefaultWindowDays)
    {
        var days = value <= 0 ? fallback : value;
        if (days < MinWindowDays) return MinWindowDays;
        return days > MaxWindowDays ? MaxWindowDays : days;
    }

    /// <summary>True when the touch is older than the window, or its capture time cannot be read.</summary>
    public static bool IsTouchExpired(AttributionTouchModel? touch, int windowDays, DateTimeOffset nowUtc)
    {
        if (touch is null)
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
                touch.OccurredAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var occurredAt))
        {
            return true;
        }

        return occurredAt < nowUtc - TimeSpan.FromDays(ClampWindowDays(windowDays));
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static Dictionary<string, string>? ReadExtraParams(
        IList<KeyValuePair<string, string>> parameters,
        IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        var considered = 0;
        foreach (var raw in names)
        {
            if (considered >= MaxExtraParamNames)
            {
                break;
            }

            considered++;
            var name = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (!ParamName.IsMatch(name) || kept.ContainsKey(name))
            {
                continue;
            }

            var value = NormalizeToken(Lookup(parameters, name), CampaignTermContentMaxLength, lowercase: false);
            if (value is not null)
            {
                kept[name] = value;
            }
        }

        if (kept.Count == 0)
        {
            return null;
        }

        // The same size ceiling the JS engines apply to the serialized map.
        var length = 2;
        foreach (var pair in kept)
        {
            length += pair.Key.Length + pair.Value.Length + 6;
        }

        return length <= ExtraParamsJsonMaxLength ? kept : null;
    }

    /// <summary>
    /// The query string as ordered name/value pairs. Written by hand rather than with
    /// <c>HttpUtility.ParseQueryString</c>, which netstandard2.0 does not carry.
    /// </summary>
    private static List<KeyValuePair<string, string>> ParseQuery(string? query)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        if (query is null || query.Length == 0)
        {
            return pairs;
        }

        var trimmed = query[0] == '?' ? query.Substring(1) : query;
        foreach (var part in trimmed.Split('&'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            var separator = part.IndexOf('=');
            var name = separator < 0 ? part : part.Substring(0, separator);
            var value = separator < 0 ? string.Empty : part.Substring(separator + 1);
            pairs.Add(new KeyValuePair<string, string>(Decode(name), Decode(value)));
        }

        return pairs;
    }

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace("+", " "));
        }
        catch (Exception)
        {
            // A malformed escape sequence is not worth losing the whole touch over.
            return value;
        }
    }

    private static string? Lookup(IList<KeyValuePair<string, string>> parameters, string name)
    {
        foreach (var pair in parameters)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static Uri? ParseHttpUrl(string? value)
    {
        if (value is null || value.Length == 0)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var url))
        {
            return null;
        }

        return url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps ? url : null;
    }

    private static string? HostName(Uri url, bool stripWww, bool includePort)
    {
        var host = (url.Host ?? string.Empty).ToLowerInvariant().TrimEnd('.');
        if (host.Length == 0)
        {
            return null;
        }

        if (stripWww && host.StartsWith("www.", StringComparison.Ordinal) && host.Length > 4)
        {
            host = host.Substring(4);
        }

        if (includePort && !url.IsDefaultPort)
        {
            host = host + ":" + url.Port.ToString(CultureInfo.InvariantCulture);
        }

        return host.Length <= HostMaxLength ? host : null;
    }

    private static string NormalizePath(string? pathname)
    {
        var path = pathname is { Length: > 0 } ? pathname : "/";
        if (path[0] != '/')
        {
            path = "/" + path;
        }

        return Truncate(path, PathMaxLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
        {
            cut -= 1;
        }

        return value.Substring(0, cut);
    }

    private static bool HasControlOrFormat(string value)
    {
        foreach (var c in value)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.Control || category == UnicodeCategory.Format)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// What a capture is allowed to read, mirroring <c>TouchCaptureOptions</c> in
/// <c>@wildwood/core</c>. The defaults match the engines' behaviour before an app config is loaded.
/// </summary>
public class AttributionCaptureOptions
{
    /// <summary>Whether ad-platform click ids are captured.</summary>
    public bool CaptureClickIds { get; set; } = true;

    /// <summary>Whether an external referrer becomes a referral touch.</summary>
    public bool CaptureReferrer { get; set; } = true;

    /// <summary>Extra query parameters the app allowlists, at most ten.</summary>
    public IReadOnlyList<string>? ExtraAllowedParamNames { get; set; }
}
