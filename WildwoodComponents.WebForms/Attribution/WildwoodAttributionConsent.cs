namespace WildwoodComponents.WebForms.Attribution
{
    /// <summary>
    /// The visitor's consent decision as the host reports it. Three states, not two — the same gate
    /// <c>@wildwood/core</c>'s <c>AttributionService</c> applies.
    /// </summary>
    /// <remarks>
    /// WebForms ships no consent component (Phase A is Authentication and Two-Factor Settings), so the
    /// host supplies the decision through <see cref="WildwoodAttribution.ConsentDecision"/>. Unanswered
    /// is the default, and it is the same default the JS SDK takes while the visitor has not decided.
    /// </remarks>
    public enum WildwoodAttributionConsent
    {
        /// <summary>
        /// The visitor has not answered, or the host has no consent experience. Touches are held for the
        /// visit and nothing is written to the visitor's device — the server-side equivalent of the JS
        /// SDK's memory-only default.
        /// </summary>
        Undecided = 0,

        /// <summary>The visitor granted the app's persistence category.</summary>
        Granted = 1,

        /// <summary>
        /// The visitor declined or withdrew. Anything already held is dropped, matching the JS rule that
        /// stored touches are removed once consent state exists and no longer grants them.
        /// </summary>
        Denied = 2
    }
}
