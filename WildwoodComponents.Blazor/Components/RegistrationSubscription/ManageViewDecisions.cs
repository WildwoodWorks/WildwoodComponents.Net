using System;
using System.Collections.Generic;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>
    /// Every decision the manage view makes about its own layout, as pure functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/views/ManageView.tsx. The
    /// repo has no bUnit renderer, so a rule that lives inside markup is a rule that cannot be
    /// tested; the view is a thin arrangement of what is here.
    /// </para>
    /// <para>No LINQ: Blazor renders this and LINQ breaks iOS/MAUI at runtime.</para>
    /// </remarks>
    internal static class ManageViewDecisions
    {
        /// <summary>The code a cancellation the server refused is reported under.</summary>
        public const string CancelFailedCode = "subscription_cancel_failed";

        /// <summary>Said when the server refused the cancellation but sent no reason.</summary>
        public const string CancelFailedFallback = "The subscription could not be cancelled.";

        /// <summary>Said when the manage view was mounted without an app to manage.</summary>
        public const string AppIdRequired = "An appId is required.";

        /// <summary>The sections a viewer may see, in the order they render when the host names none.</summary>
        public static readonly IReadOnlyList<ManageSection> AllSections = new List<ManageSection>
        {
            ManageSection.Subscription,
            ManageSection.Plans,
            ManageSection.Features,
            ManageSection.AddOns,
            ManageSection.Usage,
            ManageSection.Overrides
        };

        /// <summary>
        /// The sections this viewer actually gets, in the host's order: overrides are an admin
        /// panel, and packs go with the host's <c>ShowAddOns</c>. A section the host left out is
        /// GONE, not merely hidden behind a tab.
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
        /// The sections rendered in the body. The status card lifted above the tab bar is not also
        /// one of the sections below it.
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

        /// <summary>
        /// The tab on screen: the one the viewer clicked while it is still on offer, else the
        /// first. Null when there is nothing to show at all.
        /// </summary>
        public static ManageSection? CurrentTab(ManageSection? active, IReadOnlyList<ManageSection> bodySections)
        {
            if (bodySections.Count == 0) return null;

            if (active is not null)
            {
                foreach (var section in bodySections)
                {
                    if (section == active.Value) return active;
                }
            }

            return bodySections[0];
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
        /// The section's <c>data-ww-section</c> value: the enum name with a lower-cased first
        /// letter, which is the vocabulary JS's test-hook table pins (<c>addOns</c> included).
        /// </summary>
        public static string SectionName(ManageSection section)
        {
            var name = section.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Whether the panels should read the COMPANY's subscription rather than one user's. A
        /// user id wins, exactly as the JS view's scope chain does.
        /// </summary>
        public static bool IsCompanyMode(string? userId, string? companyId)
        {
            return !(userId is { Length: > 0 }) && companyId is { Length: > 0 };
        }

        /// <summary>
        /// What a successful cancellation says. Word for word the JS <c>CancelResultNotice</c>;
        /// the date is the viewer's own locale, as JS's <c>toLocaleDateString</c> is.
        /// </summary>
        public static string CancelNotice(bool isScheduled, DateTime? effectiveDate)
        {
            if (!isScheduled) return "Your subscription has been cancelled.";

            var until = effectiveDate.HasValue
                ? effectiveDate.Value.ToLocalTime().ToShortDateString()
                : "the end of the billing period";

            return "Your cancellation is scheduled — access continues until " + until + ".";
        }

        /// <summary>Said when a store keeps billing after the platform's own cancellation.</summary>
        public const string StoreCancelInstructions = "Also cancel the subscription in your store settings.";

        /// <summary>The link to the store's own subscription settings.</summary>
        public const string StoreCancelLink = "Open subscription settings";
    }
}
