using System;
using System.Collections.Generic;
using System.Text;
using WildwoodComponents.WebForms.Session;

namespace WildwoodComponents.WebForms.Tests.TestHelpers
{
    /// <summary>
    /// <see cref="ITokenStore"/> over a dictionary. This is the seam that lets the session
    /// manager be tested without an ASP.NET request, which cannot be manufactured for a
    /// real HttpSessionState.
    /// </summary>
    public sealed class InMemoryTokenStore : ITokenStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Set false to simulate a request with no session state.</summary>
        public bool IsAvailable { get; set; } = true;

        /// <summary>Keys currently stored.</summary>
        public IReadOnlyCollection<string> Keys
        {
            get { return _values.Keys; }
        }

        /// <inheritdoc />
        public string? Get(string key)
        {
            if (!IsAvailable)
            {
                return null;
            }

            string value;
            return _values.TryGetValue(key, out value) ? value : null;
        }

        /// <inheritdoc />
        public void Set(string key, string value)
        {
            if (!IsAvailable)
            {
                return;
            }

            _values[key] = value;
        }

        /// <inheritdoc />
        public void Remove(string key)
        {
            if (!IsAvailable)
            {
                return;
            }

            _values.Remove(key);
        }
    }

    /// <summary>Builds JWTs for expiry-parsing tests.</summary>
    public static class TestJwt
    {
        /// <summary>A token whose <c>exp</c> claim is <paramref name="expiresUtc"/>.</summary>
        public static string WithExpiry(DateTime expiresUtc)
        {
            var unix = (long)(expiresUtc.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return Build("{\"sub\":\"test\",\"exp\":" + unix + "}");
        }

        /// <summary>A structurally valid token carrying no <c>exp</c> claim.</summary>
        public static string WithoutExpiry()
        {
            return Build("{\"sub\":\"test\"}");
        }

        private static string Build(string payloadJson)
        {
            return Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}")
                   + "." + Base64Url(payloadJson)
                   + "." + Base64Url("signature");
        }

        private static string Base64Url(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                          .TrimEnd('=')
                          .Replace('+', '-')
                          .Replace('/', '_');
        }
    }
}
