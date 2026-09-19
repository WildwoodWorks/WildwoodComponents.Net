using System.Collections.Generic;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>
    /// Which billing cycle a pricing surface is quoting. Ported from the JS
    /// <c>PricingBilling</c> union, whose two members are the strings <c>monthly</c> and
    /// <c>annual</c> — <see cref="PricingViewDecisions.BillingCode"/> spells them for the wire.
    /// </summary>
    public enum PricingBilling
    {
        Monthly = 0,
        Annual = 1
    }

    /// <summary>
    /// Whether, and how many, packs a visitor may pick on a pricing surface.
    /// </summary>
    public enum PricingPackSelection
    {
        /// <summary>Each pack carries its own call to action; picking one takes it straight through.</summary>
        None = 0,

        /// <summary>The visitor ticks several packs and continues once.</summary>
        Multi = 1
    }

    /// <summary>
    /// What the visitor chose on a pricing surface. The host decides where that leads — the view
    /// never navigates.
    /// </summary>
    public class PricingSelection
    {
        /// <summary>The chosen plan, when a plan was chosen. Null when only packs were picked.</summary>
        public string? TierId { get; set; }

        /// <summary>The plan's pricing option under the current billing cycle.</summary>
        public string? PricingId { get; set; }

        /// <summary>The billing cycle the toggle was on when the choice was made.</summary>
        public PricingBilling Billing { get; set; }

        /// <summary>
        /// Every pack selected at that moment, in catalog order. Empty rather than null, so a host
        /// never has to null-check a basket.
        /// </summary>
        public List<string> AddOnIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// A heading the host files packs under, matched on the add-on's catalog <c>Category</c>.
    /// </summary>
    public class AddOnGroup
    {
        /// <summary>Stable id, rendered as the group's <c>data-ww-group</c> attribute.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The heading itself.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Optional sentence under the heading.</summary>
        public string? Blurb { get; set; }

        /// <summary>Catalog categories that belong under this heading.</summary>
        public List<string> Categories { get; set; } = new List<string>();
    }

    /// <summary>
    /// What a host's own "registration is closed" markup is given. The Blazor analog of React's
    /// <c>renderClosed({ message, contactUrl })</c>.
    /// </summary>
    public class RegistrationClosedContext
    {
        public RegistrationClosedContext(string message, string? contactUrl)
        {
            Message = message;
            ContactUrl = contactUrl;
        }

        /// <summary>The sentence the component would have shown.</summary>
        public string Message { get; }

        /// <summary>Where a visitor who still wants in should go, when the host named one.</summary>
        public string? ContactUrl { get; }
    }

    /// <summary>One rendered heading and the packs filed under it.</summary>
    public class PackGroupView
    {
        public PackGroupView(string id, string? title, string? blurb, List<AppTierAddOnModel> addOns)
        {
            Id = id;
            Title = title;
            Blurb = blurb;
            AddOns = addOns;
        }

        /// <summary>
        /// The group's <c>data-ww-group</c> value: the host's group id, <c>all</c> for an ungrouped
        /// grid, or <c>more</c> for the trailing catch-all.
        /// </summary>
        public string Id { get; }

        /// <summary>The heading, or null for the single untitled group of an ungrouped grid.</summary>
        public string? Title { get; }

        /// <summary>The sentence under the heading, when the host wrote one.</summary>
        public string? Blurb { get; }

        /// <summary>The packs in this group, in catalog order.</summary>
        public List<AppTierAddOnModel> AddOns { get; }
    }
}
