using System.Text;
using System.Text.Json;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// The API accepts a passkey assertion only against the challenge it issued, identified by the options'
    /// <c>sessionId</c>. This remembers each issued challenge's session and finds it again from the challenge
    /// inside the assertion's signed clientDataJSON, so concurrent sign-ins through one service never pick up
    /// each other's session (which would also burn the other sign-in's challenge server-side).
    /// </summary>
    internal sealed class PasskeyChallengeSessions
    {
        private const int Capacity = 32;
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        private readonly object _gate = new();
        private readonly Dictionary<string, (string SessionId, DateTime IssuedAt)> _byChallenge = new(StringComparer.Ordinal);
        private string? _last;

        public void Remember(JsonElement options)
        {
            var sessionId = options.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
            if (string.IsNullOrEmpty(sessionId)) return;
            var challenge = options.TryGetProperty("challenge", out var ch) ? Normalize(ch.GetString()) : null;

            lock (_gate)
            {
                _last = sessionId;
                if (challenge == null) return;
                var now = DateTime.UtcNow;
                foreach (var expired in _byChallenge.Where(e => now - e.Value.IssuedAt > Lifetime).Select(e => e.Key).ToList())
                {
                    _byChallenge.Remove(expired);
                }
                while (_byChallenge.Count >= Capacity)
                {
                    _byChallenge.Remove(_byChallenge.MinBy(e => e.Value.IssuedAt).Key);
                }
                _byChallenge[challenge] = (sessionId, now);
            }
        }

        /// <summary>The session for the assertion's challenge; the most recent one when it cannot be read.</summary>
        public string? Take(string? clientDataJson)
        {
            var challenge = ChallengeOf(clientDataJson);
            lock (_gate)
            {
                if (challenge != null && _byChallenge.Remove(challenge, out var entry))
                {
                    if (_last == entry.SessionId) _last = null;
                    return entry.SessionId;
                }
                var fallback = _last;
                _last = null;
                return fallback;
            }
        }

        internal static string? ChallengeOf(string? clientDataJson)
        {
            if (string.IsNullOrEmpty(clientDataJson)) return null;
            try
            {
                var base64 = clientDataJson.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
                return doc.RootElement.TryGetProperty("challenge", out var ch) ? Normalize(ch.GetString()) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Base64url without padding, so the options' and the client data's spellings compare equal.
        private static string? Normalize(string? challenge) =>
            string.IsNullOrEmpty(challenge) ? null : challenge.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
