namespace WildwoodComponents.Blazor.Components.Registration;

/// <summary>
/// What a registration token's plan means for the signup wizard, lifted out of
/// <see cref="SignupWithSubscriptionComponent"/> so the rules can be tested without a renderer.
/// </summary>
/// <remarks>
/// Ported from packages/wildwood-react/src/components/registration/SignupWithSubscriptionComponent.tsx
/// (JS f8b095f). Registering with a token that carries a plan already subscribes the user, so the
/// wizard must not subscribe again — the second call replaced the token's subscription, cancelling
/// the plan the token had just created.
/// </remarks>
internal static class SignupPlanDecisions
{
    /// <summary>
    /// The plan a token grants for THIS app, or null when it grants none. App ids are compared
    /// case-insensitively: the server's casing for a GUID is not the host's.
    /// </summary>
    /// <param name="details">
    /// The token's detailed validation, or null when it could not be read — a server that predates
    /// the route, or a transport failure. Unreadable is not "no grant": the caller falls back to
    /// the plain validity check and the normal flow, which the token still authorises.
    /// </param>
    /// <param name="appId">The app the wizard is signing the user up for.</param>
    public static RegistrationTokenAppGrant? FindGrantForApp(RegistrationTokenDetails? details, string? appId)
    {
        if (details is null) return null;
        if (appId is not { Length: > 0 }) return null;
        if (details.AppGrants is null) return null;

        foreach (var grant in details.AppGrants)
        {
            if (string.Equals(grant.AppId, appId, StringComparison.OrdinalIgnoreCase)) return grant;
        }

        return null;
    }

    /// <summary>
    /// Whether the wizard should self-subscribe after registering. A token grant means the account
    /// is already on its plan, and subscribing over it CANCELS that plan.
    /// </summary>
    public static bool ShouldSelfSubscribe(RegistrationTokenAppGrant? tokenGrant, string? selectedTierId)
    {
        if (tokenGrant is not null) return false;
        return selectedTierId is { Length: > 0 };
    }

    /// <summary>
    /// The plan's name as the server named it, falling back to its id and then to a neutral word,
    /// so the success copy never reads "created with the  from your registration token".
    /// </summary>
    public static string GrantPlanName(RegistrationTokenAppGrant? grant)
    {
        if (grant is null) return "plan";
        if (grant.AppTierName is { Length: > 0 } name) return name;
        if (grant.AppTierId is { Length: > 0 } id) return id;
        return "plan";
    }

    /// <summary>
    /// One entry per granted id, named the way the server named it — or by its id when it did not.
    /// Nothing is priced: the registrant is not paying for any of it, and a price beside a granted
    /// plan would say they were.
    /// </summary>
    public static List<string> DisplayNames(List<string>? ids, List<string>? names)
    {
        var entries = new List<string>();
        if (ids is null) return entries;

        for (var index = 0; index < ids.Count; index++)
        {
            var name = names is not null && index < names.Count && names[index] is { Length: > 0 }
                ? names[index]
                : ids[index];
            entries.Add(name);
        }

        return entries;
    }
}
