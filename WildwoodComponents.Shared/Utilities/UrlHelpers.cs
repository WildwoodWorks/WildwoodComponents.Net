using System;

namespace WildwoodComponents.Shared.Utilities
{
    /// <summary>
    /// URL helpers shared across the .NET component stacks (Blazor + Razor).
    /// </summary>
    public static class UrlHelpers
    {
        /// <summary>
        /// Removes a trailing <c>/api</c> or <c>/api/</c> from a base URL so callers can append
        /// per-endpoint <c>/api/...</c> paths (or hand a host root to a client engine) without
        /// producing <c>/api/api</c>. Mirrors the vanilla widgets' <c>apiBase.replace(/\/api\/?$/, '')</c>.
        /// </summary>
        public static string StripApiSuffix(string? baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl))
                return string.Empty;

            var trimmed = baseUrl.TrimEnd('/');
            if (trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 4);
            }
            return trimmed.TrimEnd('/');
        }
    }
}
