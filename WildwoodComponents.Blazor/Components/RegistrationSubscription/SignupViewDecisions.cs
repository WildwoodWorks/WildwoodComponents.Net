using System;
using System.Collections.Generic;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>The plan a signup is carrying, resolved against the live catalog.</summary>
    internal sealed class SignupPlanView
    {
        public SignupPlanView(AppTierModel tier, AppTierPricingModel? pricing)
        {
            Tier = tier;
            Pricing = pricing;
        }

        public AppTierModel Tier { get; }

        /// <summary>The option being bought, or null for a plan that sells none ("contact us").</summary>
        public AppTierPricingModel? Pricing { get; }
    }

    /// <summary>
    /// Every decision the signup view makes, as pure functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from packages/wildwood-react-shared/src/registrationSubscription/useSignupFlow.ts and
    /// packages/wildwood-react/src/components/registrationSubscription/views/SignupView.tsx. The
    /// repo has no bUnit renderer and no hook-testing library, so a rule that lives inside markup
    /// or inside an effect is a rule that cannot be tested; the view and the driver are thin
    /// arrangements of what is here.
    /// </para>
    /// <para>No LINQ: Blazor renders this and LINQ breaks iOS/MAUI at runtime.</para>
    /// </remarks>
    internal static class SignupViewDecisions
    {
        #region Error codes and fallbacks

        /// <summary>The code a failed signup is reported under when nothing more precise is known.</summary>
        public const string SignupFailedCode = "signup_failed";

        /// <summary>The code a token the server refused is reported under.</summary>
        public const string TokenRejectedCode = "registration_token_rejected";

        /// <summary>Said when the server refused a token but sent no reason.</summary>
        public const string TokenRejectedFallback = "Invalid or expired registration token";

        /// <summary>Said when a signup threw and the exception carried no message.</summary>
        public const string SignupFailedFallback = "Signup failed. Please try again.";

        #endregion

        /// <summary>
        /// The <c>data-ww-step</c> name for a machine step. The machine's <c>Done</c> is spelled
        /// <c>success</c> in the DOM, exactly as JS spells it, because that is the hook live
        /// end-to-end suites locate the finished signup by.
        /// </summary>
        public static string StepName(SignupStep step)
        {
            if (step == SignupStep.Done) return "success";
            if (step == SignupStep.PackCheckout) return "packCheckout";

            // Every other step's name is its enum name, lower-cased first letter — the machine's
            // members were named after the TS union members precisely so this holds.
            var name = step.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Whether a plan has to be paid for before the account is created. TS
        /// <c>requiresPayment</c>.
        /// </summary>
        public static bool RequiresPayment(AppTierModel? tier, AppTierPricingModel? pricing)
        {
            if (tier is null) return false;
            return !tier.IsFreeTier && pricing is not null && pricing.Price > 0m;
        }

        /// <summary>
        /// The plan the flow starts with: the one a signup link preselected, or the app's default
        /// when the host skips the plan step. TS <c>presetPlan</c>.
        /// </summary>
        /// <remarks>
        /// An invite's plan comes from its token, not from the link or the app's default, so invite
        /// redemption resolves nothing. A plan the app does not sell is IGNORED rather than
        /// honoured: the link is stale or hand-edited.
        /// </remarks>
        public static SignupPlanView? ResolvePresetPlan(
            PublicCatalog? catalog,
            bool invite,
            SignupPlanSelection planSelection,
            string? preSelectedTierId,
            string? preSelectedPricingId)
        {
            if (catalog is null || invite) return null;

            if (planSelection == SignupPlanSelection.Skip)
            {
                // The URL never picks a plan in a skip flow: the app has one, and this is it.
                AppTierModel? chosen = null;
                foreach (var tier in catalog.Tiers)
                {
                    if (tier is not null && tier.IsDefault) { chosen = tier; break; }
                }

                if (chosen is null)
                {
                    foreach (var tier in catalog.Tiers)
                    {
                        if (tier is not null && tier.IsFreeTier) { chosen = tier; break; }
                    }
                }

                return chosen is null ? null : new SignupPlanView(chosen, CatalogHelpers.ResolvePriceOption(chosen));
            }

            if (!(preSelectedTierId is { Length: > 0 })) return null;

            var wanted = FindTier(catalog, preSelectedTierId);
            if (wanted is null) return null;

            return new SignupPlanView(wanted, CatalogHelpers.ResolvePriceOption(wanted, preSelectedPricingId));
        }

        /// <summary>
        /// The plan the machine's selection names, priced off the live catalog. TS <c>plan</c>.
        /// </summary>
        /// <remarks>
        /// Matched case-insensitively, through the same <see cref="FindTier"/> the preset plan
        /// uses: the selection can carry a link's casing for a GUID rather than the server's, and
        /// an exact match would then lose the plan the signup is actually carrying — no summary
        /// card, no price, and a payment step with nothing to charge for.
        /// </remarks>
        public static SignupPlanView? ResolvePlan(PublicCatalog? catalog, string? tierId, string? pricingId)
        {
            var tier = FindTier(catalog, tierId);
            if (tier is null) return null;

            return new SignupPlanView(tier, CatalogHelpers.ResolvePriceOption(tier, pricingId));
        }

        /// <summary>
        /// The plan the grid opens on when nothing has chosen one. TS <c>defaultTierId</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A HIGHLIGHT and nothing more: the machine never sees it, no plan is selected, and the
        /// visitor still confirms with a click. <see cref="SignupPlanDefault.Free"/> names the
        /// app's first free plan; an app that sells none has nothing to suggest, which is not a
        /// failure.
        /// </para>
        /// <para>
        /// Invite redemption suggests nothing either: an invite's plan comes from its token, so
        /// there is no grid for a default to open on.
        /// </para>
        /// </remarks>
        public static string? DefaultTierId(SignupPlanDefault planDefault, bool invite, PublicCatalog? catalog)
        {
            if (planDefault != SignupPlanDefault.Free || invite || catalog is null) return null;

            foreach (var tier in catalog.Tiers)
            {
                if (tier is not null && tier.IsFreeTier) return tier.Id;
            }

            return null;
        }

        /// <summary>
        /// Which plan the grid marks. TS
        /// <c>state.selection.tierId ?? flow.defaultTierId ?? props.preSelectedTierId</c>.
        /// </summary>
        /// <remarks>
        /// The default sits AHEAD of the link's plan on purpose: a <c>preSelectedTierId</c> still
        /// showing at this point is an id the flow already refused (stale, or hand-edited), so the
        /// grid opens on the host's default rather than on nothing at all. A plan the visitor has
        /// actually chosen beats both.
        /// </remarks>
        public static string? HighlightTierId(string? selectionTierId, string? defaultTierId, string? preSelectedTierId)
        {
            if (selectionTierId is { Length: > 0 }) return selectionTierId;
            if (defaultTierId is { Length: > 0 }) return defaultTierId;
            return preSelectedTierId;
        }

        /// <summary>A plan matched case-insensitively — the server's casing for a GUID is not the link's.</summary>
        public static AppTierModel? FindTier(PublicCatalog? catalog, string? tierId)
        {
            if (catalog is null || !(tierId is { Length: > 0 })) return null;

            foreach (var tier in catalog.Tiers)
            {
                if (tier is not null && string.Equals(tier.Id, tierId, StringComparison.OrdinalIgnoreCase)) return tier;
            }

            return null;
        }

        /// <summary>
        /// The packs a signup link asked for, kept to what the app actually sells and capped at
        /// <see cref="CatalogHelpers.MaxAddOnSelection"/>. Empty for an invite: the token decides.
        /// </summary>
        public static List<string> ResolvePresetPackIds(
            PublicCatalog? catalog,
            bool invite,
            IReadOnlyList<string>? preSelectedAddOnIds)
        {
            if (catalog is null || invite || preSelectedAddOnIds is null) return new List<string>();

            var values = new string?[preSelectedAddOnIds.Count];
            for (var i = 0; i < preSelectedAddOnIds.Count; i++) values[i] = preSelectedAddOnIds[i];

            return CatalogHelpers.ParseAddOnIdList(values, catalog);
        }

        /// <summary>
        /// Free-trial days on the plan being paid for, 0 when there is no trial or no payment.
        /// </summary>
        public static int TrialDays(SignupPlanView? plan)
        {
            if (plan is null || !RequiresPayment(plan.Tier, plan.Pricing)) return 0;
            return plan.Pricing?.TrialDays ?? 0;
        }

        /// <summary>
        /// The packs still worth offering: a pack the token already granted is not one of them.
        /// </summary>
        public static List<AppTierAddOnModel> AvailablePacks(PublicCatalog? catalog, IReadOnlyList<string>? grantedIds)
        {
            var packs = new List<AppTierAddOnModel>();
            if (catalog is null) return packs;

            var granted = new HashSet<string>(StringComparer.Ordinal);
            if (grantedIds is not null)
            {
                foreach (var id in grantedIds)
                {
                    if (id is not null) granted.Add(id);
                }
            }

            foreach (var addOn in catalog.AddOns)
            {
                if (addOn is not null && !granted.Contains(addOn.Id)) packs.Add(addOn);
            }

            return packs;
        }

        /// <summary>Drops granted packs from a selection, so none is charged for or shown as chosen.</summary>
        public static List<string> WithoutGranted(IReadOnlyList<string> selected, IReadOnlyList<string>? grantedIds)
        {
            var kept = new List<string>(selected.Count);
            var granted = new HashSet<string>(StringComparer.Ordinal);
            if (grantedIds is not null)
            {
                foreach (var id in grantedIds)
                {
                    if (id is not null) granted.Add(id);
                }
            }

            foreach (var id in selected)
            {
                if (!granted.Contains(id)) kept.Add(id);
            }

            return kept;
        }

        /// <summary>
        /// The display names the outcome needs, so a pack or a plan can be named without re-reading
        /// the catalog.
        /// </summary>
        public static SignupCatalogNames CatalogNames(PublicCatalog? catalog)
        {
            if (catalog is null) return SignupCatalogNames.Empty;

            var tiers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tier in catalog.Tiers)
            {
                if (tier is not null) tiers[tier.Id] = tier.Name;
            }

            var addOns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var addOn in catalog.AddOns)
            {
                if (addOn is not null) addOns[addOn.Id] = addOn.Name;
            }

            return new SignupCatalogNames(tiers, addOns);
        }

        /// <summary>The machine's view of a token's grant: ids only, no display names.</summary>
        public static SignupTokenGrant? ToMachineGrant(RegistrationTokenAppGrant? grant)
        {
            if (grant is null) return null;

            return new SignupTokenGrant
            {
                TierId = grant.AppTierId,
                PricingId = grant.AppTierPricingId,
                AddOnIds = grant.AddOnIds ?? new List<string>(),
                FeatureCodes = grant.FeatureCodes ?? new List<string>()
            };
        }

        /// <summary>
        /// What the register form's submit button says. NOT a label on any stack: React hard-codes
        /// both words here, and a live site's suite clicks them by name.
        /// </summary>
        public static string SubmitButtonText(bool planStepAhead)
        {
            return planStepAhead ? "Continue" : "Create Account";
        }

        /// <summary>
        /// Whether a plan is still to be chosen, which is what the form's submit button says. TS
        /// <c>planStepAhead</c>. A token's grant is not known while the form is on screen, so this
        /// is what the flow knows then.
        /// </summary>
        public static bool PlanStepAhead(SignupPlanSelection planSelection, bool invite, bool planPreset)
        {
            return planSelection == SignupPlanSelection.Choose && !invite && !planPreset;
        }

        /// <summary>What the success panel says, by what actually happened. TS <c>successMessage</c>.</summary>
        public static string SuccessMessage(
            RegistrationSubscriptionLabels labels,
            RegistrationTokenAppGrant? tokenGrant,
            bool hasPlan,
            bool subscriptionFailed,
            int trialDays)
        {
            if (tokenGrant is not null)
            {
                var tier = tokenGrant.AppTierName is { Length: > 0 } name ? name : "plan";
                return RegistrationSubscriptionLabels.Format(labels.SignupCompleteToken, "tier", tier);
            }

            if (!hasPlan) return labels.SignupCompletePlain;
            if (subscriptionFailed) return labels.SignupCompletePending;
            if (trialDays > 0) return RegistrationSubscriptionLabels.Format(labels.SignupCompleteTrial, "days", trialDays);
            return labels.SignupCompleteActive;
        }

        /// <summary>
        /// The registration form's initial values: the previous attempt's, or the invitation's
        /// email. TS <c>initialFormData</c> — SDB's convention is that an invitation's email is the
        /// username too, so the visitor types neither.
        /// </summary>
        public static Registration.TokenRegistrationComponent.RegistrationFormData? InitialFormData(
            Registration.TokenRegistrationComponent.RegistrationFormData? formData,
            string? prefillEmail,
            string? registrationToken)
        {
            if (formData is not null) return formData;
            if (!(prefillEmail is { Length: > 0 })) return null;

            return new Registration.TokenRegistrationComponent.RegistrationFormData
            {
                FirstName = string.Empty,
                LastName = string.Empty,
                Username = prefillEmail,
                Email = prefillEmail,
                Password = string.Empty,
                Token = registrationToken
            };
        }

        /// <summary>The packs still to buy, as checkout items.</summary>
        public static List<AddOnCheckoutItemInput> CheckoutItems(IReadOnlyList<string> addOnIds)
        {
            var items = new List<AddOnCheckoutItemInput>(addOnIds.Count);
            foreach (var addOnId in addOnIds) items.Add(new AddOnCheckoutItemInput { AddOnId = addOnId });
            return items;
        }
    }
}
