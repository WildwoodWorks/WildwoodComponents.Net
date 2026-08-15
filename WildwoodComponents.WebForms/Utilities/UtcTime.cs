using System;

namespace WildwoodComponents.WebForms.Utilities
{
    /// <summary>
    /// One place to decide what an incoming <see cref="DateTime"/> means, so the session
    /// copy of a token expiry and the Forms Authentication ticket copy can never disagree
    /// about the same value.
    /// </summary>
    internal static class UtcTime
    {
        /// <summary>
        /// Normalises a token expiry to UTC.
        /// <see cref="DateTimeKind.Unspecified"/> is read as already-UTC rather than local:
        /// every producer in this package deals in UTC, and the common sources of an
        /// unspecified-kind value — a JSON payload without an offset, or
        /// <c>new DateTime(...)</c> — carry UTC instants. Interpreting them as local would
        /// shift the expiry by the server's offset.
        /// </summary>
        public static DateTime Normalize(DateTime value)
        {
            switch (value.Kind)
            {
                case DateTimeKind.Utc:
                    return value;
                case DateTimeKind.Local:
                    return value.ToUniversalTime();
                default:
                    return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }
        }
    }
}
