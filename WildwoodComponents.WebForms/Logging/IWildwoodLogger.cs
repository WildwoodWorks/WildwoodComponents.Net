using System;

namespace WildwoodComponents.WebForms.Logging
{
    /// <summary>
    /// Minimal logging seam for the WebForms package. Classic WebForms sites have no
    /// common logging abstraction — some use log4net, some NLog, most nothing — so the
    /// package logs through this interface and defaults to
    /// <see cref="TraceWildwoodLogger"/>. A host that wants its own sink assigns
    /// <c>WildwoodWebForms.Logger</c> in <c>Application_Start</c>.
    /// </summary>
    public interface IWildwoodLogger
    {
        /// <summary>Diagnostic detail, off in most deployments.</summary>
        void Debug(string message);

        /// <summary>Normal lifecycle events worth recording.</summary>
        void Info(string message);

        /// <summary>A recoverable problem: the component degraded but kept working.</summary>
        void Warn(string message, Exception? exception = null);

        /// <summary>A failure the host should investigate.</summary>
        void Error(string message, Exception? exception = null);
    }

    /// <summary>
    /// Default <see cref="IWildwoodLogger"/>, writing to <see cref="System.Diagnostics.Trace"/>.
    /// ASP.NET routes Trace output to whatever listeners <c>&lt;system.diagnostics&gt;</c>
    /// configures, so a site gets the package's diagnostics with no extra dependency and
    /// no wiring.
    /// </summary>
    public sealed class TraceWildwoodLogger : IWildwoodLogger
    {
        private const string Prefix = "Wildwood: ";

        /// <inheritdoc />
        public void Debug(string message)
        {
            System.Diagnostics.Trace.WriteLine(Prefix + message, "Wildwood");
        }

        /// <inheritdoc />
        public void Info(string message)
        {
            System.Diagnostics.Trace.TraceInformation(Prefix + message);
        }

        /// <inheritdoc />
        public void Warn(string message, Exception? exception = null)
        {
            System.Diagnostics.Trace.TraceWarning(Prefix + Format(message, exception));
        }

        /// <inheritdoc />
        public void Error(string message, Exception? exception = null)
        {
            System.Diagnostics.Trace.TraceError(Prefix + Format(message, exception));
        }

        private static string Format(string message, Exception? exception)
        {
            return exception == null ? message : message + " -> " + exception;
        }
    }

    /// <summary>
    /// Discards everything. Used by unit tests and by hosts that want the package silent.
    /// </summary>
    public sealed class NullWildwoodLogger : IWildwoodLogger
    {
        /// <summary>A shared instance; the type holds no state.</summary>
        public static readonly NullWildwoodLogger Instance = new NullWildwoodLogger();

        /// <inheritdoc />
        public void Debug(string message) { }

        /// <inheritdoc />
        public void Info(string message) { }

        /// <inheritdoc />
        public void Warn(string message, Exception? exception = null) { }

        /// <inheritdoc />
        public void Error(string message, Exception? exception = null) { }
    }
}
