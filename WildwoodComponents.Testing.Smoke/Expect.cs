namespace WildwoodComponents.Testing.Smoke;

/// <summary>A check found the helpers doing something other than what they promise.</summary>
/// <remarks>
/// Distinct from <see cref="WildwoodContractException"/>, which several checks EXPECT: that one
/// means a driver reported a page failing its contract, which is often the thing being proved. This
/// one means the run itself found a discrepancy.
/// </remarks>
internal sealed class SmokeCheckFailure : Exception
{
    internal SmokeCheckFailure(string message) : base(message)
    {
    }
}

/// <summary>The three assertions the checks need, and nothing else.</summary>
internal static class Expect
{
    internal static void Equal(string? expected, string? actual, string what)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal)) return;

        throw new SmokeCheckFailure(
            what + "\n    expected: " + Show(expected) + "\n    actual:   " + Show(actual));
    }

    internal static void Equal(int expected, int actual, string what)
    {
        if (expected == actual) return;

        throw new SmokeCheckFailure(what + "\n    expected: " + expected + "\n    actual:   " + actual);
    }

    internal static void True(bool condition, string what)
    {
        if (condition) return;

        throw new SmokeCheckFailure(what);
    }

    /// <summary>
    /// Runs <paramref name="work"/> expecting it to report a contract failure, and hands the failure
    /// back so the check can assert on its wording.
    /// </summary>
    /// <remarks>
    /// The wording is part of the contract rather than an implementation detail: a bare timeout
    /// reads as a product hang, and these messages are what tells whoever opens a red run which of
    /// the two they are looking at.
    /// </remarks>
    internal static async Task<WildwoodContractException> FailureAsync(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (WildwoodContractException failure)
        {
            return failure;
        }

        throw new SmokeCheckFailure(what + "\n    the helper was expected to fail here, and did not.");
    }

    private static string Show(string? value)
    {
        return value is null ? "(null)" : "\"" + value + "\"";
    }
}
