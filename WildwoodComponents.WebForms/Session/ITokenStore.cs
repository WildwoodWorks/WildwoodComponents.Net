namespace WildwoodComponents.WebForms.Session
{
    /// <summary>
    /// Per-request key/value storage for the Wildwood tokens. The production
    /// implementation is <see cref="HttpSessionTokenStore"/> over
    /// <c>HttpContext.Current.Session</c>; the indirection exists so
    /// <see cref="WildwoodSessionManager"/> can be unit-tested without an ASP.NET
    /// request, which cannot be constructed for a real <c>HttpSessionState</c>.
    /// </summary>
    public interface ITokenStore
    {
        /// <summary>
        /// False when no session is available for the current request — session state
        /// disabled in web.config, or a handler that opted out of it. Callers degrade
        /// rather than throw.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Reads a value, or null when absent or unavailable.</summary>
        string? Get(string key);

        /// <summary>Writes a value. A no-op when unavailable.</summary>
        void Set(string key, string value);

        /// <summary>Removes a value. A no-op when unavailable.</summary>
        void Remove(string key);
    }
}
