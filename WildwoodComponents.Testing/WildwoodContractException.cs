namespace WildwoodComponents.Testing;

/// <summary>
/// The page never honoured the DOM contract these helpers wait on.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from Playwright's own <c>PlaywrightException</c> on purpose. That one means the driver
/// could not do what it was told - a selector it cannot parse, a browser that went away. This one
/// means the driver did exactly what it was told and the component never got where it was supposed
/// to, which is a statement about the product rather than about the tooling.
/// </para>
/// <para>
/// Its message always names what was waited for AND what was on screen instead, because "timed
/// out" on its own reads as the product hanging when it usually means a step is spelled differently
/// or a hook was never rendered. That is the one rule the JS original (which throws a plain
/// <c>Error</c>, since it deliberately imports nothing at runtime) and this port share.
/// </para>
/// </remarks>
public class WildwoodContractException : Exception
{
    /// <summary>Creates the exception with a message naming what was expected and what was seen.</summary>
    public WildwoodContractException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception from an underlying failure.</summary>
    public WildwoodContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
