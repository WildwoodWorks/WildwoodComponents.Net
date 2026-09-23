namespace WildwoodComponents.Razor.Components.RegistrationSubscription;

/// <summary>Which surface of the registration + subscription component a page is showing.</summary>
public enum RegistrationSubscriptionView
{
    /// <summary>What the app sells, at the price the server is quoting right now.</summary>
    Pricing = 0,

    /// <summary>An account, a plan, packs and a card.</summary>
    Signup = 1,

    /// <summary>What a customer already pays for, and every way of changing it.</summary>
    Manage = 2
}

/// <summary>The shell's own decisions, as pure functions so a test can reach them.</summary>
public static class RegistrationAndSubscriptionShell
{
    /// <summary>The three names the <c>view</c> attribute takes, in React's spelling.</summary>
    public static IReadOnlyList<string> ViewNames { get; } = new List<string> { "pricing", "signup", "manage" };

    /// <summary>
    /// The view a <c>view="…"</c> attribute named, or null when it named none of them. Null is
    /// the caller's cue to report a developer mistake rather than to guess: rendering the pricing
    /// page because someone typed <c>view="Manage "</c> would hide the typo, and rendering the
    /// SIGNUP page for an unknown word would offer a second account to a signed-in customer.
    /// Matching is case-insensitive and ignores surrounding space, because those are not typos.
    /// </summary>
    public static RegistrationSubscriptionView? ParseView(string? view)
    {
        var value = view?.Trim() ?? string.Empty;

        if (string.Equals(value, "pricing", StringComparison.OrdinalIgnoreCase)) return RegistrationSubscriptionView.Pricing;
        if (string.Equals(value, "signup", StringComparison.OrdinalIgnoreCase)) return RegistrationSubscriptionView.Signup;
        if (string.Equals(value, "manage", StringComparison.OrdinalIgnoreCase)) return RegistrationSubscriptionView.Manage;

        return null;
    }

    /// <summary>The ViewComponent one view renders.</summary>
    public static string ComponentName(RegistrationSubscriptionView view)
    {
        switch (view)
        {
            case RegistrationSubscriptionView.Signup:
                return "RegistrationSubscriptionSignup";

            case RegistrationSubscriptionView.Manage:
                return "RegistrationSubscriptionManage";

            default:
                return "RegistrationSubscriptionPricing";
        }
    }

    /// <summary>
    /// What Development says about an unknown view. It names the value it was given and the three
    /// it accepts, because "invalid view" sends a developer back to the source to find out which
    /// three.
    /// </summary>
    public static string UnknownViewMessage(string? view)
    {
        var given = view is { Length: > 0 } ? view : "(empty)";
        return "<vc:registration-and-subscription> was given view=\"" + given
            + "\". It renders \"pricing\", \"signup\" or \"manage\".";
    }
}
