using System.Globalization;
using System.Net;
using System.Text;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// The <c>URLSearchParams</c> the JS SDK's catalog and signup helpers are written against, so the
/// ports read the same query keys and serialise the same query strings.
/// </summary>
/// <remarks>
/// Parsing and serialisation follow the WHATWG <c>application/x-www-form-urlencoded</c> rules
/// rather than <see cref="Uri.EscapeDataString"/>: a space is <c>+</c>, and only ASCII
/// alphanumerics plus <c>*</c>, <c>-</c>, <c>.</c> and <c>_</c> survive unescaped. That is what
/// keeps a link a .NET host writes identical to one the JS SDK writes.
/// </remarks>
public sealed class QueryParameters
{
    private readonly List<KeyValuePair<string, string>> _pairs = new();

    /// <summary>The name/value pairs, decoded, in the order they appeared.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Pairs
    {
        get { return _pairs; }
    }

    /// <summary>
    /// Normalises anything URL-shaped into parameters: a full URL, a query string with or without
    /// its leading <c>?</c>, or an empty string. Mirrors JS <c>asSearchParams</c>.
    /// </summary>
    public static QueryParameters Parse(string? value)
    {
        var parameters = new QueryParameters();
        if (value is null || value.Length == 0) return parameters;

        var withoutFragment = value;
        var fragmentStart = withoutFragment.IndexOf('#');
        if (fragmentStart >= 0) withoutFragment = withoutFragment.Substring(0, fragmentStart);

        var queryStart = withoutFragment.IndexOf('?');
        var query = queryStart >= 0 ? withoutFragment.Substring(queryStart + 1) : withoutFragment;

        foreach (var sequence in query.Split('&'))
        {
            if (sequence.Length == 0) continue;

            var separator = sequence.IndexOf('=');
            var name = separator >= 0 ? sequence.Substring(0, separator) : sequence;
            var encodedValue = separator >= 0 ? sequence.Substring(separator + 1) : string.Empty;

            parameters._pairs.Add(new KeyValuePair<string, string>(Decode(name), Decode(encodedValue)));
        }

        return parameters;
    }

    /// <summary>The first value under <paramref name="name"/>, or null when there is none.</summary>
    public string? Get(string name)
    {
        foreach (var pair in _pairs)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal)) return pair.Value;
        }
        return null;
    }

    /// <summary>
    /// Every value under <paramref name="name"/>, so the repeated-parameter form
    /// (<c>?addons=a&amp;addons=b</c>) reads as the list it is.
    /// </summary>
    public IReadOnlyList<string> GetAll(string name)
    {
        var values = new List<string>();
        foreach (var pair in _pairs)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal)) values.Add(pair.Value);
        }
        return values;
    }

    /// <summary>
    /// Sets one value, replacing any existing ones — <c>URLSearchParams.set</c>: the first
    /// occurrence keeps its position, later duplicates are dropped.
    /// </summary>
    public void Set(string name, string value)
    {
        // WHATWG: "set the value of the FIRST such name-value pair to value and remove the
        // others" — so the walk runs forward, the first match is replaced where it stands, and
        // every later match is dropped. Walking backwards would keep the last occurrence's slot.
        var replaced = false;
        for (var i = 0; i < _pairs.Count; i++)
        {
            if (!string.Equals(_pairs[i].Key, name, StringComparison.Ordinal)) continue;
            if (!replaced)
            {
                _pairs[i] = new KeyValuePair<string, string>(name, value);
                replaced = true;
            }
            else
            {
                _pairs.RemoveAt(i);
                i--;
            }
        }

        if (!replaced) _pairs.Add(new KeyValuePair<string, string>(name, value));
    }

    /// <summary>The query string, without a leading <c>?</c>.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var pair in _pairs)
        {
            if (builder.Length > 0) builder.Append('&');
            builder.Append(Encode(pair.Key));
            builder.Append('=');
            builder.Append(Encode(pair.Value));
        }
        return builder.ToString();
    }

    private static string Decode(string value)
    {
        if (value.Length == 0) return string.Empty;
        // WebUtility.UrlDecode already applies the urlencoded rules: '+' is a space and %XX
        // sequences are UTF-8 bytes.
        return WebUtility.UrlDecode(value) ?? string.Empty;
    }

    private static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c == ' ')
            {
                builder.Append('+');
            }
            else if ((c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || c == '*' || c == '-' || c == '.' || c == '_')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%');
                builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }
        return builder.ToString();
    }
}
