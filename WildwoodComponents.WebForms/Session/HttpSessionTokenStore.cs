using System.Web;
using System.Web.SessionState;

namespace WildwoodComponents.WebForms.Session
{
    /// <summary>
    /// <see cref="ITokenStore"/> over classic ASP.NET session state. Resolves
    /// <c>HttpContext.Current</c> on every access rather than capturing it, so a single
    /// instance is safe to hand to a service that outlives one request.
    /// </summary>
    /// <remarks>
    /// Only strings are stored, which keeps the tokens serializable and therefore usable
    /// under out-of-process session modes (StateServer, SQLServer) as well as InProc.
    /// </remarks>
    public sealed class HttpSessionTokenStore : ITokenStore
    {
        /// <inheritdoc />
        public bool IsAvailable
        {
            get { return CurrentSession != null; }
        }

        /// <inheritdoc />
        public string? Get(string key)
        {
            var session = CurrentSession;
            if (session == null)
            {
                return null;
            }

            return session[key] as string;
        }

        /// <inheritdoc />
        public void Set(string key, string value)
        {
            var session = CurrentSession;
            if (session == null)
            {
                return;
            }

            session[key] = value;
        }

        /// <inheritdoc />
        public void Remove(string key)
        {
            var session = CurrentSession;
            if (session == null)
            {
                return;
            }

            session.Remove(key);
        }

        /// <summary>
        /// The current request's session, or null when there is no request or the request
        /// has no session state. Touching <c>HttpContext.Current.Session</c> throws in a
        /// handler without <c>IRequiresSessionState</c>, hence the guarded access.
        /// </summary>
        private static HttpSessionState? CurrentSession
        {
            get
            {
                var context = HttpContext.Current;
                if (context == null)
                {
                    return null;
                }

                try
                {
                    return context.Session;
                }
                catch (HttpException)
                {
                    // "Session state is not available in this context."
                    return null;
                }
            }
        }
    }
}
