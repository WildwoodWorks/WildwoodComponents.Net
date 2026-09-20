using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.RegistrationSubscription;

/// <summary>
/// Every decision the manage view makes about its own shape, as pure functions.
/// </summary>
/// <remarks>
/// <para>
/// Ported from packages/wildwood-react/src/components/registrationSubscription/views/ManageView.tsx
/// by way of the Blazor <c>ManageViewDecisions</c>, and kept identical to it: which sections a
/// viewer gets, in what order, which of them is lifted above the tabs, what each is called and
/// what its <c>data-ww-section</c> value is.
/// </para>
/// <para>
/// A rule that lives inside a <c>.cshtml</c> is a rule no test can reach — this package has no
/// view renderer in its test suite — so the view is a thin arrangement of what is here.
/// </para>
/// </remarks>
public static class RegistrationSubscriptionManageDecisions
{
    /// <summary>Said when the manage view was rendered without an app to manage.</summary>
    public const string AppIdRequired = "An appId is required.";

    /// <summary>
    /// Said when a plan change needs a card the package will not ask for. Razor collects no card
    /// for a plan change by standing decision; the sentence is the honest version of what React
    /// answers with its payment modal.
    /// </summary>
    public const string PaymentMethodRequired = "This change needs a payment method.";

    /// <summary>Asked before a FIRST subscription, which has nothing to preview.</summary>
    public const string SubscribeConfirm = "Subscribe to {tier}?";

    /// <summary>Said when the proxy answers 401 because the session is gone.</summary>
    public const string SessionExpired = "Your session has expired. Please sign in again.";

    /// <summary>The sections a viewer may see, in the order they render when the host names none.</summary>
    public static IReadOnlyList<ManageSection> AllSections { get; } = new List<ManageSection>
    {
        ManageSection.Subscription,
        ManageSection.Plans,
        ManageSection.Features,
        ManageSection.AddOns,
        ManageSection.Usage,
        ManageSection.Overrides
    };

    /// <summary>
    /// The layout a tag-helper attribute asked for. A string rather than an enum for the same
    /// reason the pricing view's <c>billing</c> is one: a Razor attribute whose property is not a
    /// string compiles as a C# expression, so an enum would force every host to write
    /// <c>layout="@ManageLayout.Stacked"</c>.
    /// </summary>
    public static ManageLayout ParseLayout(string? value)
    {
        return string.Equals(value?.Trim(), "stacked", StringComparison.OrdinalIgnoreCase)
            ? ManageLayout.Stacked
            : ManageLayout.Tabs;
    }

    /// <summary>The wire spelling of a layout, for the root's data attribute.</summary>
    public static string LayoutCode(ManageLayout layout)
    {
        return layout == ManageLayout.Stacked ? "stacked" : "tabs";
    }

    /// <summary>
    /// The sections a comma-separated attribute named, in the host's order. An unknown name is
    /// dropped rather than throwing: a typo should cost the section, not the page. Null when the
    /// host named none at all, which is not the same as naming none.
    /// </summary>
    public static List<ManageSection>? ParseSections(string? value)
    {
        if (value is null || value.Trim().Length == 0) return null;

        var sections = new List<ManageSection>();

        foreach (var part in value.Split(','))
        {
            var name = part.Trim();
            if (name.Length == 0) continue;

            var section = ParseSection(name);
            if (section is null || sections.Contains(section.Value)) continue;

            sections.Add(section.Value);
        }

        return sections;
    }

    /// <summary>One section name, in React's own spelling (<c>addOns</c> included).</summary>
    public static ManageSection? ParseSection(string? name)
    {
        var value = name?.Trim() ?? string.Empty;

        if (string.Equals(value, "subscription", StringComparison.OrdinalIgnoreCase)) return ManageSection.Subscription;
        if (string.Equals(value, "plans", StringComparison.OrdinalIgnoreCase)) return ManageSection.Plans;
        if (string.Equals(value, "features", StringComparison.OrdinalIgnoreCase)) return ManageSection.Features;
        if (string.Equals(value, "addons", StringComparison.OrdinalIgnoreCase)) return ManageSection.AddOns;
        if (string.Equals(value, "usage", StringComparison.OrdinalIgnoreCase)) return ManageSection.Usage;
        if (string.Equals(value, "overrides", StringComparison.OrdinalIgnoreCase)) return ManageSection.Overrides;

        return null;
    }

    /// <summary>
    /// The sections this viewer actually gets, in the host's order: overrides are an admin panel,
    /// and packs go with the host's <c>show-add-ons</c>. A section the host left out is GONE, not
    /// merely hidden behind a tab.
    /// </summary>
    public static List<ManageSection> VisibleSections(
        IReadOnlyList<ManageSection>? requested, bool isAdmin, bool showAddOns)
    {
        var wanted = requested is null || requested.Count == 0 ? AllSections : requested;
        var visible = new List<ManageSection>(wanted.Count);

        foreach (var section in wanted)
        {
            if (section == ManageSection.Overrides && !isAdmin) continue;
            if (section == ManageSection.AddOns && !showAddOns) continue;
            visible.Add(section);
        }

        return visible;
    }

    /// <summary>
    /// The sections rendered in the body. The status card lifted above the tab bar is not also one
    /// of the sections below it.
    /// </summary>
    public static List<ManageSection> BodySections(IReadOnlyList<ManageSection> visible, bool statusAbove)
    {
        var body = new List<ManageSection>(visible.Count);

        foreach (var section in visible)
        {
            if (statusAbove && section == ManageSection.Subscription) continue;
            body.Add(section);
        }

        return body;
    }

    /// <summary>Whether the subscription card is lifted out of the section list.</summary>
    public static bool StatusAbove(IReadOnlyList<ManageSection> visible, bool showStatusAboveTabs)
    {
        if (!showStatusAboveTabs) return false;

        foreach (var section in visible)
        {
            if (section == ManageSection.Subscription) return true;
        }

        return false;
    }

    /// <summary>The section's heading and tab text, from the shared labels.</summary>
    public static string SectionTitle(ManageSection section, RegistrationSubscriptionLabels labels)
    {
        switch (section)
        {
            case ManageSection.Subscription:
                return labels.SectionStatus;

            case ManageSection.Plans:
                return labels.SectionPlans;

            case ManageSection.Features:
                return labels.SectionFeatures;

            case ManageSection.AddOns:
                return labels.SectionPacks;

            case ManageSection.Usage:
                return labels.SectionUsage;

            default:
                return labels.SectionOverrides;
        }
    }

    /// <summary>
    /// The section's <c>data-ww-section</c> value: the enum name with a lower-cased first letter,
    /// which is the vocabulary JS's test-hook table pins (<c>addOns</c> included).
    /// </summary>
    public static string SectionName(ManageSection section)
    {
        var name = section.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>
    /// The packs the pack picker offers: everything the app sells that the account does not
    /// already hold and the plan does not already bundle.
    /// </summary>
    /// <remarks>
    /// The ownership rule is <see cref="AddOnRowRules.AvailableRows"/>, the SAME one the add-ons
    /// panel's rows are decided by — so a pack that was granted by a registration token, or bought
    /// and still running, is not offered for sale a second time, and a cancelled or expired one is
    /// back on offer. A pack the current plan bundles is excluded too: the panel shows it as
    /// "Included in plan" and there is nothing to buy.
    /// </remarks>
    public static List<AppTierAddOnModel> PickerPacks(
        IReadOnlyList<AppTierAddOnModel>? available,
        IReadOnlyList<UserAddOnSubscriptionModel>? owned,
        string? currentTierId)
    {
        var packs = new List<AppTierAddOnModel>();

        foreach (var addOn in AddOnRowRules.AvailableRows(available, owned))
        {
            if (AddOnRowRules.IsBundledInTier(addOn, currentTierId)) continue;
            packs.Add(addOn);
        }

        return packs;
    }

    /// <summary>
    /// Whether the panels read the COMPANY's subscription. The app's tracking mode decides, which
    /// is why the view asks the server for it rather than inferring one from a company id.
    /// </summary>
    /// <remarks>
    /// These two are the C# spelling of <c>resolveScope</c> in <c>regsub-planchange.js</c>, and
    /// the rule the six admin panels have always used. Keep them in step: the server renders the
    /// panels for one scope and the browser posts the change for the same one.
    /// </remarks>
    public static bool UseCompanyScope(bool isCompanyMode, string? companyId)
    {
        return isCompanyMode && companyId is { Length: > 0 };
    }

    /// <summary>Whether the panels read ONE USER's subscription, as an admin acting for them.</summary>
    public static bool UseUserScope(bool isCompanyMode, string? userId)
    {
        return !isCompanyMode && userId is { Length: > 0 };
    }
}
