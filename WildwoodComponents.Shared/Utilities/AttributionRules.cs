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

    // ---- Funnel events (mirrors @wildwood/core types.ts, attributionRules.ts and signupFunnel.ts) ----

    /// <summary>Minutes of inactivity after which a funnel session ends and the next event starts a new one.</summary>
    public const int FunnelSessionTimeoutMinutes = 30;

    /// <summary>Most events one POST /api/attribution/events request may carry.</summary>
    public const int MaxFunnelEventsPerRequest = 25;

    /// <summary>Maximum length of a funnel event label.</summary>
    public const int FunnelLabelMaxLength = 100;

    /// <summary>Most custom event names an app config may carry.</summary>
    public const int MaxCustomEventNames = 50;

    /// <summary>Funnel events a client may send. Configured custom names are allowed on top of these.</summary>
    public static readonly IReadOnlyList<string> FunnelClientEvents = new[]
    {
        "page_view", "engaged", "scroll_depth", "time_on_page", "cta_click",
        "signup_view", "signup_start", "signup_submit", "signup_error", "plan_selected", "checkout_start"
    };

    /// <summary>Funnel events only the server records. A client request carrying one is refused, so they are dropped.</summary>
    public static readonly IReadOnlyList<string> FunnelServerOnlyEvents = new[] { "signup_complete", "trial_started", "purchase" };

    /// <summary>The device classes a funnel event or registration may report.</summary>
    public static readonly IReadOnlyList<string> DeviceClasses = new[] { "mobile", "tablet", "desktop" };

    /// <summary>The categories a <c>signup_error</c> label is drawn from.</summary>
    public static readonly IReadOnlyList<string> SignupErrorCategories = new[]
    {
        "validation", "email_taken", "username_taken", "password_policy", "captcha", "invalid_token",
        "registration_closed", "rate_limited", "network", "server", "unknown"
    };

    private static readonly Regex FunnelEventNamePattern = new Regex("^[a-z0-9_]{1,40}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ClientEventSet = new HashSet<string>(FunnelClientEvents, StringComparer.Ordinal);
    private static readonly HashSet<string> ServerOnlyEventSet = new HashSet<string>(FunnelServerOnlyEvents, StringComparer.Ordinal);

    /// <summary>Codes compared with case and separators removed ("USERNAME_EXISTS" and "UsernameExists" match).</summary>
    private static readonly Dictionary<string, string> SignupErrorCodeCategories = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["USERNAMEEXISTS"] = "username_taken",
        ["USERNAMETAKEN"] = "username_taken",
        ["USEREXISTS"] = "email_taken",
        ["EMAILEXISTS"] = "email_taken",
        ["EMAILTAKEN"] = "email_taken",
        ["DUPLICATEEMAIL"] = "email_taken",
        ["PASSWORDINVALID"] = "password_policy",
        ["PASSWORDPOLICY"] = "password_policy",
        ["INVALIDTOKEN"] = "invalid_token",
        ["REGISTRATIONTOKENREJECTED"] = "invalid_token",
        ["VALIDATIONERROR"] = "validation",
        ["VALIDATION"] = "validation",
        ["EMAILREQUIRED"] = "validation",
        ["USERNAMEREQUIRED"] = "validation",
        ["REGISTRATIONNOTALLOWED"] = "registration_closed",
        ["FORBIDDEN"] = "registration_closed",
        ["RATELIMITED"] = "rate_limited",
        ["RATELIMITEXCEEDED"] = "rate_limited",
        ["NETWORKERROR"] = "network",
        ["TIMEOUT"] = "network",
        ["SERVERERROR"] = "server",
        ["INTERNALERROR"] = "server",
        ["DATABASEERROR"] = "server",
        ["EXECUTIONSTRATEGYERROR"] = "server",
        ["USERCREATIONFAILED"] = "server",
    };

    /// <summary>Whether the value has the shape of a funnel event name (<c>^[a-z0-9_]{1,40}$</c>).</summary>
    public static bool IsValidFunnelEventName(string? name)
        => name is { Length: > 0 } && FunnelEventNamePattern.IsMatch(name);

    /// <summary>Whether the name is one of the standard client events.</summary>
    public static bool IsClientFunnelEvent(string? name) => name is not null && ClientEventSet.Contains(name);

    /// <summary>Whether the name is recorded only by the server (a client sending it is refused).</summary>
    public static bool IsServerOnlyFunnelEvent(string? name) => name is not null && ServerOnlyEventSet.Contains(name);

    /// <summary>
    /// Whether a client may send this event: well formed, not server-only, and a standard client event
    /// or one of the app's custom names.
    /// </summary>
    public static bool IsAllowedFunnelEvent(string? name, IEnumerable<string>? customEventNames = null)
    {
        if (!IsValidFunnelEventName(name) || IsServerOnlyFunnelEvent(name))
        {
            return false;
        }

        if (IsClientFunnelEvent(name))
        {
            return true;
        }

        if (customEventNames is null)
        {
            return false;
        }

        foreach (var custom in customEventNames)
        {
            if (string.Equals(custom, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The signup steps an app accepts only while its config's <c>TrackSignupSteps</c> is on.</summary>
    public static readonly IReadOnlyList<string> FunnelSignupStepEvents = new[] { "signup_view", "signup_start", "signup_submit", "signup_error" };

    /// <summary>
    /// Whether the app's config accepts this event, by @wildwood/core's funnel rule: attribution and
    /// funnel tracking both on, the name allowed (a standard client event or one of the config's custom
    /// names), and a signup step only while <c>TrackSignupSteps</c> is on. No config accepts nothing —
    /// the SDK sends nothing until the config says so.
    /// </summary>
    public static bool IsFunnelEventAcceptedBy(AttributionConfigModel? config, string? name)
    {
        if (config is null || !config.IsEnabled || !config.FunnelTrackingEnabled)
        {
            return false;
        }

        if (!IsAllowedFunnelEvent(name, NormalizeCustomEventNames(config.CustomEventNames)))
        {
            return false;
        }

        foreach (var step in FunnelSignupStepEvents)
        {
            if (string.Equals(step, name, StringComparison.Ordinal))
            {
                return config.TrackSignupSteps;
            }
        }

        return true;
    }

    /// <summary>
    /// A config's custom event names: trimmed, lowercased, well formed, not a standard or server-only
    /// name, deduplicated, and at most <see cref="MaxCustomEventNames"/>.
    /// </summary>
    public static List<string> NormalizeCustomEventNames(IEnumerable<string?>? names)
    {
        var kept = new List<string>();
        if (names is null)
        {
            return kept;
        }

        foreach (var raw in names)
        {
            if (raw is null)
            {
                continue;
            }

            var name = raw.Trim().ToLowerInvariant();
            if (IsValidFunnelEventName(name) && !IsClientFunnelEvent(name) && !IsServerOnlyFunnelEvent(name) && !kept.Contains(name))
            {
                kept.Add(name);
            }

            if (kept.Count >= MaxCustomEventNames)
            {
                break;
            }
        }

        return kept;
    }

    /// <summary>A device class as the server accepts it (<c>mobile</c>, <c>tablet</c>, <c>desktop</c>), or null.</summary>
    public static string? NormalizeDeviceClass(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null)
        {
            return null;
        }

        foreach (var known in DeviceClasses)
        {
            if (string.Equals(known, normalized, StringComparison.Ordinal))
            {
                return known;
            }
        }

        return null;
    }

    /// <summary>
    /// The browser engines' viewport bucket: under 768 CSS pixels is mobile, under 1024 a tablet (a
    /// coarse pointer stretches the tablet range to 1280), and anything wider a desktop. Null for a
    /// non-positive width.
    /// </summary>
    public static string? DeviceClassFromViewport(int width, bool coarsePointer = false)
    {
        if (width <= 0) return null;
        if (width < 768) return "mobile";
        if (width < 1024 || (coarsePointer && width < 1280)) return "tablet";
        return "desktop";
    }

    /// <summary>
    /// A funnel event label: trimmed, capped at <see cref="FunnelLabelMaxLength"/>, and null when empty
    /// or carrying control characters.
    /// </summary>
    public static string? NormalizeFunnelLabel(string? label) => NormalizeToken(label, FunnelLabelMaxLength, lowercase: false);

    /// <summary>
    /// A page or screen path for a funnel event: no query or fragment, a leading "/", capped at
    /// <see cref="PathMaxLength"/>. Null when empty.
    /// </summary>
    public static string? NormalizeFunnelPath(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var cut = raw;
        var stop = cut.IndexOfAny(new[] { '?', '#' });
        if (stop >= 0)
        {
            cut = cut.Substring(0, stop);
        }

        cut = cut.Trim();
        if (cut.Length == 0)
        {
            return null;
        }

        return Truncate(cut[0] == '/' ? cut : "/" + cut, PathMaxLength);
    }

    /// <summary>
    /// A <c>signup_error</c> label reduced to the server's category shape (<c>^[a-z0-9_]{1,40}$</c>),
    /// <c>unknown</c> when nothing is left. The engines apply the same reduction to every signup_error.
    /// </summary>
    public static string SignupErrorLabel(string? label)
    {
        var lowered = (label ?? string.Empty).ToLowerInvariant();
        var category = Regex.Replace(lowered, "[^a-z0-9_]+", "_").Trim('_');
        if (category.Length > 40)
        {
            category = category.Substring(0, 40);
        }

        category = category.TrimEnd('_');
        return category.Length > 0 ? category : "unknown";
    }

    /// <summary>
    /// The <c>signup_error</c> category for a server or client error code (WildwoodAPI's
    /// <c>errorCode</c>, or a flow code such as <c>registration_token_rejected</c>), falling back on the
    /// HTTP status (0 for a request that never reached the server). Never reads a message.
    /// </summary>
    public static string SignupErrorCategoryFromCode(string? code, int? status = null)
    {
        var key = Regex.Replace((code ?? string.Empty).ToUpperInvariant(), "[^A-Z0-9]", string.Empty);
        if (key.Length > 0)
        {
            if (SignupErrorCodeCategories.TryGetValue(key, out var known)) return known;
            if (key.IndexOf("CAPTCHA", StringComparison.Ordinal) >= 0) return "captcha";
            if (key.StartsWith("TOKEN", StringComparison.Ordinal)) return "invalid_token";
            if (key.StartsWith("PASSWORD", StringComparison.Ordinal)) return "password_policy";
            if (key.IndexOf("REGISTRATIONDISABLED", StringComparison.Ordinal) >= 0
                || key.IndexOf("NOTALLOWED", StringComparison.Ordinal) >= 0)
            {
                return "registration_closed";
            }
        }

        if (status.HasValue)
        {
            var s = status.Value;
            if (s == 0) return "network";
            if (s == 429) return "rate_limited";
            if (s >= 500) return "server";
            if (s == 400 || s == 422) return "validation";
        }

        return "unknown";
    }

    /// <summary>
    /// A stable plan key for <c>plan_selected</c> / <c>checkout_start</c>: the tier's (or pricing
    /// option's) id, else a slug of its name, capped at 100 characters. Null when there is neither.
    /// </summary>
    public static string? SignupPlanKey(string? id, string? name = null)
    {
        var trimmed = id?.Trim() ?? string.Empty;
        if (trimmed.Length > 0)
        {
            return trimmed.Length <= 100 ? trimmed : trimmed.Substring(0, 100);
        }

        var slug = Regex.Replace((name ?? string.Empty).ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        if (slug.Length > 100)
        {
            slug = slug.Substring(0, 100);
        }

        return slug.Length > 0 ? slug : null;
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
