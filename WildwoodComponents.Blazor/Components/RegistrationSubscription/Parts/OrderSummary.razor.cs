using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// A pack basket priced by the server: one line per pack, the total due today, and the card
    /// already on file when there is one.
    /// </summary>
    /// <remarks>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/OrderSummary.tsx.
    /// Nothing is added up here: every figure, the total included, is the server's own, because
    /// the server is the one that will charge the card.
    /// </remarks>
    public partial class OrderSummary : BaseWildwoodComponent
    {
        /// <summary>A successful quote.</summary>
        [Parameter] public AddOnCheckoutQuoteModel? Quote { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>"Visa ending in 4242", from the quote's own words.</summary>
        private string SavedCardLine
        {
            get
            {
                var card = Quote?.SavedCard;
                var values = new Dictionary<string, object?>(2)
                {
                    ["brand"] = card?.Brand ?? string.Empty,
                    ["last4"] = card?.Last4 ?? string.Empty
                };

                return RegistrationSubscriptionLabels.Format(Labels.SavedCardOnFile, values);
            }
        }
    }
}
