using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// Publishes the live catalog as schema.org offers in a <c>application/ld+json</c> script.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from packages/wildwood-react/src/components/registrationSubscription/parts/CatalogJsonLd.tsx.
    /// The offers come from <see cref="CatalogHelpers.CatalogToJsonLdOffers"/>, which leaves out
    /// anything the server has not priced rather than publishing a zero.
    /// </para>
    /// <para>
    /// The serializer is the DEFAULT <see cref="JsonSerializerOptions"/> encoder, deliberately.
    /// It escapes the angle brackets and the ampersand into their JSON unicode escapes, which
    /// keeps the payload byte-identical to a JSON parser and inert to an HTML tokenizer — a pack
    /// the operator named with a closing script tag cannot close this element and run as markup.
    /// <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c> would write that name through verbatim,
    /// so it must never be used here.
    /// </para>
    /// </remarks>
    public partial class CatalogJsonLd : BaseWildwoodComponent
    {
        private const string SchemaOrgContext = "https://schema.org";

        /// <summary>The live catalog to publish.</summary>
        [Parameter] public PublicCatalog? Catalog { get; set; }

        /// <summary>Canonical URL applied to every offer.</summary>
        [Parameter] public string? Url { get; set; }

        /// <summary>The graph as it will be written, or an empty string when nothing is priced.</summary>
        private string Json
        {
            get
            {
                var offers = CatalogHelpers.CatalogToJsonLdOffers(Catalog, Url);
                if (offers.Count == 0) return string.Empty;

                return JsonSerializer.Serialize(new JsonLdGraph { Graph = offers });
            }
        }

        /// <summary>The <c>@graph</c> wrapper schema.org readers expect around a list of offers.</summary>
        private sealed class JsonLdGraph
        {
            [JsonPropertyName("@context")]
            public string Context { get; set; } = SchemaOrgContext;

            [JsonPropertyName("@graph")]
            public List<JsonLdOffer> Graph { get; set; } = new List<JsonLdOffer>();
        }
    }
}
