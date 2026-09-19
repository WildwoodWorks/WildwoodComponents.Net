using System;
using System.Collections.Generic;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Copy the add-ons panel lets a host replace. Anything left alone keeps the shipped words, which
/// are the same strings <c>@wildwood/react</c>'s <c>DEFAULT_ADDON_LABELS</c> ships.
/// </summary>
public class AddOnsPanelLabels
{
    /// <summary>Badge on a pack nothing bills.</summary>
    public string Included { get; set; } = "Included with your registration";

    /// <summary>Confirmation copy before cancelling a pack nothing bills.</summary>
    public string CancelIncluded { get; set; } =
        "This pack was included with your registration. Cancelling removes it from your account.";

    /// <summary>Confirmation copy before cancelling a billed pack.</summary>
    public string CancelBilled { get; set; } = "You keep access until the end of the current billing period.";

    /// <summary>Confirms the cancellation.</summary>
    public string CancelConfirm { get; set; } = "Cancel pack";

    /// <summary>Backs out of it.</summary>
    public string CancelKeep { get; set; } = "Keep pack";

    /// <summary>Takes back a scheduled cancellation.</summary>
    public string Reactivate { get; set; } = "Reactivate";

    /// <summary>Opens the host's pack picker.</summary>
    public string AddPacks { get; set; } = "Add packs";
}

/// <summary>
/// Which date line an owned pack's row shows, if any.
/// </summary>
public enum AddOnDateLine
{
    /// <summary>Nothing is promised: a granted pack has no renewal and no scheduled end.</summary>
    None = 0,

    /// <summary>"Renews: {date}" — a billed pack that keeps going.</summary>
    Renews = 1,

    /// <summary>"Cancels on: {date}" — a scheduled cancellation the server dated.</summary>
    Cancels = 2,

    /// <summary>"Cancels at the end of the billing period" — scheduled, but the server sent no date.</summary>
    CancelsAtPeriodEnd = 3
}

/// <summary>
/// The three things a row can ask the server to do, for the failure wording.
/// </summary>
public enum AddOnAction
{
    Subscribe = 0,
    Cancel = 1,
    Reactivate = 2
}

/// <summary>
/// Everything an owned pack's row decides about itself. Built by
/// <see cref="AddOnRowRules.Describe"/>; every member is already-decided data so a .razor file, a
/// .cshtml view and a JS re-render can all render the same row without re-deriving anything.
/// </summary>
public class AddOnRowDecision
{
    public AddOnRowDecision(
        bool cancelling,
        bool complimentary,
        string statusLabel,
        AddOnDateLine dateLine,
        DateTime? endDate,
        bool offersReactivate,
        bool offersCancel,
        string cancelMessage)
    {
        Cancelling = cancelling;
        Complimentary = complimentary;
        StatusLabel = statusLabel;
        DateLine = dateLine;
        EndDate = endDate;
        OffersReactivate = offersReactivate;
        OffersCancel = offersCancel;
        CancelMessage = cancelMessage;
    }

    /// <summary>The row is scheduled to cancel at the end of the period.</summary>
    public bool Cancelling { get; }

    /// <summary>Nothing bills this row — it was granted, not sold.</summary>
    public bool Complimentary { get; }

    /// <summary>The badge next to the pack name.</summary>
    public string StatusLabel { get; }

    /// <summary>Which of the two date lines to show, or none.</summary>
    public AddOnDateLine DateLine { get; }

    /// <summary>The date <see cref="DateLine"/> prints, when it prints one.</summary>
    public DateTime? EndDate { get; }

    /// <summary>A scheduled cancellation on a BILLED pack can be taken back.</summary>
    public bool OffersReactivate { get; }

    /// <summary>Cancel is offered on a row that is neither bundled nor already cancelling.</summary>
    public bool OffersCancel { get; }

    /// <summary>What the confirmation asks before the cancel is sent.</summary>
    public string CancelMessage { get; }

    /// <summary>The included badge shows on exactly the rows nothing bills.</summary>
    public bool ShowIncludedBadge
    {
        get { return Complimentary; }
    }

    /// <summary>
    /// The row's allowed actions as a space-separated list, for a <c>data-ww-addon-actions</c>
    /// attribute the Razor JS reads instead of re-deriving the rules in the browser. Empty when the
    /// row offers nothing (a bundled pack).
    /// </summary>
    public string ActionsAttribute
    {
        get
        {
            if (OffersCancel && OffersReactivate) return "cancel reactivate";
            if (OffersCancel) return "cancel";
            if (OffersReactivate) return "reactivate";
            return string.Empty;
        }
    }
}

/// <summary>
/// What a pack row says about itself, in one place, because it is the same rule in every .NET
/// stack. Ported from packages/wildwood-react/src/components/subscription/admin/AddOnsPanel.tsx and
/// its react-native twin (JS d7eae5a, 27daa30).
/// </summary>
/// <remarks>
/// <para>A pack row says how it is paid for, because that decides what cancelling it does. A row
/// with no payment behind it was GRANTED — a registration token's pack, or an admin's — so nothing
/// bills it, there is no renewal to show and there is nothing to reactivate at a provider;
/// cancelling one simply removes it. A billed row keeps access to the end of the period it is paid
/// up to, and a cancellation scheduled that way can be taken back.</para>
/// <para>Every member is pure and free of LINQ, so Blazor (iOS/MAUI) and the netstandard2.0 leg
/// both consume it.</para>
/// </remarks>
public static class AddOnRowRules
{
    /// <summary>The shipped copy, for a caller that supplies no <see cref="AddOnsPanelLabels"/>.</summary>
    public static AddOnsPanelLabels DefaultLabels
    {
        get { return new AddOnsPanelLabels(); }
    }

    /// <summary>
    /// Granted, not sold: no payment behind the row and no plan bundling it in. Exactly the web's
    /// predicate (<c>!sub.isBundled &amp;&amp; !sub.paymentTransactionId</c>), because the same row
    /// has to read the same way on every stack.
    /// </summary>
    public static bool IsComplimentary(UserAddOnSubscriptionModel? subscription)
    {
        if (subscription is null) return false;
        if (subscription.IsBundled) return false;

        // Pattern form narrows on netstandard2.0, whose string.IsNullOrEmpty is unannotated. An
        // empty transaction id is no payment, the same way JS treats "" as falsy.
        return subscription.PaymentTransactionId is not { Length: > 0 };
    }

    /// <summary>
    /// Whether the account already owns this pack. One access-granting rule for both lists: a row
    /// that is Cancelled or Expired is on offer again, and one scheduled to cancel is still owned,
    /// so it is not sold twice and shows no stale "Subscribed" badge.
    /// </summary>
    public static bool OwnsAddOn(IReadOnlyList<UserAddOnSubscriptionModel>? subscriptions, string? addOnId)
    {
        if (subscriptions is null || addOnId is not { Length: > 0 }) return false;

        foreach (var subscription in subscriptions)
        {
            if (subscription is null) continue;
            if (!string.Equals(subscription.AppTierAddOnId, addOnId, StringComparison.OrdinalIgnoreCase)) continue;
            if (SubscriptionAccess.GrantsAccess(subscription.Status)) return true;
        }

        return false;
    }

    /// <summary>
    /// The rows that still grant what they pay for — the "Active Add-Ons" list. The server returns
    /// every row it has, cancelled ones included; listing those under "Active" with a green badge
    /// and a Cancel button was the live bug.
    /// </summary>
    public static List<UserAddOnSubscriptionModel> OwnedRows(IReadOnlyList<UserAddOnSubscriptionModel>? subscriptions)
    {
        var owned = new List<UserAddOnSubscriptionModel>();
        if (subscriptions is null) return owned;

        foreach (var subscription in subscriptions)
        {
            if (subscription is not null && SubscriptionAccess.GrantsAccess(subscription.Status)) owned.Add(subscription);
        }

        return owned;
    }

    /// <summary>
    /// The packs still on offer: everything the account does not currently own.
    /// </summary>
    public static List<AppTierAddOnModel> AvailableRows(
        IReadOnlyList<AppTierAddOnModel>? addOns,
        IReadOnlyList<UserAddOnSubscriptionModel>? subscriptions)
    {
        var available = new List<AppTierAddOnModel>();
        if (addOns is null) return available;

        foreach (var addOn in addOns)
        {
            if (addOn is not null && !OwnsAddOn(subscriptions, addOn.Id)) available.Add(addOn);
        }

        return available;
    }

    /// <summary>
    /// Whether the account's current plan already bundles this pack, so it is shown as included
    /// rather than sold.
    /// </summary>
    public static bool IsBundledInTier(AppTierAddOnModel? addOn, string? currentTierId)
    {
        if (addOn is null || currentTierId is not { Length: > 0 }) return false;

        foreach (var tierId in addOn.BundledInTierIds)
        {
            if (string.Equals(tierId, currentTierId, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// The date a row's line prints. JS carries one <c>endDate</c>; the .NET model splits it into
    /// <see cref="UserAddOnSubscriptionModel.EndDate"/> (set when a cancellation is scheduled) and
    /// <see cref="UserAddOnSubscriptionModel.CurrentPeriodEnd"/> (the renewal a live row is paid
    /// up to), so both feed the one line the web shows.
    /// </summary>
    public static DateTime? RowEndDate(UserAddOnSubscriptionModel? subscription)
    {
        if (subscription is null) return null;
        return subscription.EndDate ?? subscription.CurrentPeriodEnd;
    }

    /// <summary>
    /// Everything one owned row decides about itself.
    /// </summary>
    /// <param name="subscription">The row, as the server sent it.</param>
    /// <param name="canCancel">Whether this surface offers cancelling at all.</param>
    /// <param name="canReactivate">Whether this surface offers taking a cancellation back.</param>
    /// <param name="labels">Host copy; the shipped words when null.</param>
    public static AddOnRowDecision Describe(
        UserAddOnSubscriptionModel subscription,
        bool canCancel = true,
        bool canReactivate = true,
        AddOnsPanelLabels? labels = null)
    {
        if (subscription is null) throw new ArgumentNullException(nameof(subscription));

        var copy = labels ?? DefaultLabels;
        var cancelling = string.Equals(subscription.Status, "PendingCancellation", StringComparison.Ordinal);
        var complimentary = IsComplimentary(subscription);
        var endDate = RowEndDate(subscription);

        // Nothing bills a granted pack, so there is no renewal date to promise — even when the
        // server put one on the row.
        var showEndDate = endDate.HasValue && (cancelling || !complimentary);

        AddOnDateLine dateLine;
        if (showEndDate) dateLine = cancelling ? AddOnDateLine.Cancels : AddOnDateLine.Renews;
        else dateLine = cancelling ? AddOnDateLine.CancelsAtPeriodEnd : AddOnDateLine.None;

        var statusLabel = subscription.IsBundled
            ? "Bundled"
            : cancelling ? "Cancellation Scheduled" : subscription.Status;

        return new AddOnRowDecision(
            cancelling,
            complimentary,
            statusLabel,
            dateLine,
            showEndDate ? endDate : null,
            cancelling && !subscription.IsBundled && !complimentary && canReactivate,
            !subscription.IsBundled && !cancelling && canCancel,
            complimentary ? copy.CancelIncluded : copy.CancelBilled);
    }

    /// <summary>
    /// What the panel says when an action was refused or threw. A refusal that carried words of its
    /// own says them — "you already own that pack" has to read differently from "the card was
    /// declined"; a refusal with nothing to say gets the action's wording. Never empty.
    /// </summary>
    /// <param name="action">Which call was refused.</param>
    /// <param name="name">The pack's name; "this pack" when the row has none.</param>
    /// <param name="serverMessage">
    /// The structured result's message (<c>AppTierActionError.Message</c> or
    /// <c>ErrorMessage</c>), or an exception's message. Null/blank falls through.
    /// </param>
    public static string FailureMessage(AddOnAction action, string? name, string? serverMessage)
    {
        if (serverMessage is not null)
        {
            var trimmed = serverMessage.Trim();
            if (trimmed.Length > 0) return trimmed;
        }

        var verb = action switch
        {
            AddOnAction.Subscribe => "subscribe to",
            AddOnAction.Reactivate => "reactivate",
            _ => "cancel"
        };

        var packName = name is not null && name.Trim().Length > 0 ? name.Trim() : "this pack";
        return "Could not " + verb + " " + packName + ". Please try again.";
    }

    /// <summary>
    /// The trial the processor will actually start: the pricing option that is bought, then the
    /// pack's own value as a fallback. "14-day free trial", or an empty string when nothing starts
    /// a trial — a pack with <c>TrialDays = 0</c> never renders a bare "0".
    /// </summary>
    public static string TrialLabel(AppTierAddOnModel? addOn, AppTierAddOnPricingModel? pricing)
    {
        var days = pricing?.TrialDays ?? addOn?.TrialDays;
        return CatalogHelpers.TrialLabel(days);
    }

    /// <summary>
    /// The currency a pack's prices are quoted in: the pack's own, then the catalog's, then USD
    /// (<see cref="FormatHelpers.FormatMoney"/>'s own fallback).
    /// </summary>
    public static string? ResolveCurrency(AppTierAddOnModel? addOn, string? catalogCurrency)
    {
        var own = addOn?.Currency;
        if (own is not null && own.Trim().Length > 0) return own;
        return catalogCurrency;
    }

    /// <summary>
    /// A pricing option's price in the pack's own currency. Replaces the hard-coded "$" that priced
    /// a CHF or SEK pack in dollars.
    /// </summary>
    public static string FormatPrice(
        AppTierAddOnModel? addOn,
        AppTierAddOnPricingModel? pricing,
        string? catalogCurrency)
    {
        var amount = pricing is null ? 0m : pricing.Price;
        return FormatHelpers.FormatMoney(amount, ResolveCurrency(addOn, catalogCurrency));
    }

    /// <summary>
    /// The pricing option a one-click subscribe buys: the pack's default, else the first it sells.
    /// Null when the pack sells none.
    /// </summary>
    public static AppTierAddOnPricingModel? DefaultPricing(AppTierAddOnModel? addOn)
    {
        return CatalogHelpers.ResolvePriceOption(addOn);
    }

    /// <summary>
    /// The billing cycle shown after the price, lower-cased as the web renders it ("month" when the
    /// server sent nothing).
    /// </summary>
    public static string BillingSuffix(AppTierAddOnPricingModel? pricing)
    {
        var frequency = pricing?.BillingFrequency;
        if (frequency is not { Length: > 0 }) return "month";
        return frequency.ToLowerInvariant();
    }
}
