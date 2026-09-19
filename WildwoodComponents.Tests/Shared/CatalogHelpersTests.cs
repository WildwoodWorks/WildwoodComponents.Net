using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// Ported case for case from packages/wildwood-core/src/__tests__/catalog.test.ts. The JS test
/// names are kept as the C# method names so the two files can be diffed against each other.
/// (The <c>formatMoney</c> block of that file lives in <see cref="FormatHelpersTests"/>, where the
/// .NET port of the function lives.)
/// </summary>
public class CatalogHelpersTests
{
    private static AppTierPricingModel Pricing(
        string id = "price-1",
        decimal price = 79m,
        string billingFrequency = "Monthly",
        bool isDefault = false,
        int displayOrder = 0)
    {
        return new AppTierPricingModel
        {
            Id = id,
            AppTierId = "tier-1",
            PricingModelId = "pm-1",
            IsDefault = isDefault,
            DisplayOrder = displayOrder,
            PricingModelName = "Monthly",
            Price = price,
            BillingFrequency = billingFrequency
        };
    }

    private static AppTierModel Tier(
        string id = "tier-1",
        string name = "Pro",
        int displayOrder = 0,
        string status = "Active",
        bool showPrice = true,
        string? currency = null,
        List<AppTierPricingModel>? pricingOptions = null)
    {
        return new AppTierModel
        {
            Id = id,
            AppId = "app-1",
            Name = name,
            Description = "Everything",
            DisplayOrder = displayOrder,
            IsDefault = false,
            IsFreeTier = false,
            AllowUpgrades = true,
            AllowDowngrades = true,
            Status = status,
            BadgeColor = string.Empty,
            IconClass = string.Empty,
            ShowSubscribeButton = true,
            ShowContactButton = false,
            ShowPrice = showPrice,
            Currency = currency,
            PricingOptions = pricingOptions ?? new List<AppTierPricingModel> { Pricing() }
        };
    }

    private static AppTierAddOnModel AddOn(
        string id = "addon-1",
        string name = "Radar",
        int displayOrder = 0,
        string status = "Active",
        string? currency = null,
        List<AppTierAddOnPricingModel>? pricingOptions = null)
    {
        return new AppTierAddOnModel
        {
            Id = id,
            AppId = "app-1",
            Name = name,
            Description = "Pack",
            Category = string.Empty,
            Status = status,
            DisplayOrder = displayOrder,
            IconClass = string.Empty,
            BadgeColor = string.Empty,
            Currency = currency,
            PricingOptions = pricingOptions ?? new List<AppTierAddOnPricingModel>
            {
                new AppTierAddOnPricingModel
                {
                    Id = "ap-1",
                    PricingModelId = "pm-2",
                    PricingModelName = "Monthly",
                    Price = 19m,
                    BillingFrequency = "Monthly",
                    IsDefault = true
                }
            }
        };
    }

    private static List<string> Ids(List<AppTierModel> tiers)
    {
        var ids = new List<string>();
        foreach (var tier in tiers) ids.Add(tier.Id);
        return ids;
    }

    private static List<string> Ids(List<AppTierAddOnModel> addOns)
    {
        var ids = new List<string>();
        foreach (var addOn in addOns) ids.Add(addOn.Id);
        return ids;
    }

    #region buildPublicCatalog

    [Fact]
    public void BuildPublicCatalog_TakesTheCurrencyTheServerSent_InPreferenceToTheOverride()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1",
            new List<AppTierModel> { Tier(currency: "GBP") },
            new List<AppTierAddOnModel> { AddOn(currency: "EUR") },
            currencyOverride: "CAD");

        Assert.Equal("GBP", catalog.Currency);
    }

    [Fact]
    public void BuildPublicCatalog_FallsBackToTheOverrideThenToUsd_WhenNoResponseCarriesACurrency()
    {
        Assert.Equal(
            "CAD",
            CatalogHelpers.BuildPublicCatalog("app-1", new List<AppTierModel> { Tier() }, currencyOverride: "CAD").Currency);

        // An add-on can supply it just as well as a tier — it is one app-level setting.
        Assert.Equal(
            "EUR",
            CatalogHelpers.BuildPublicCatalog(
                "app-1",
                new List<AppTierModel> { Tier() },
                new List<AppTierAddOnModel> { AddOn(currency: " EUR ") }).Currency);

        Assert.Equal("USD", CatalogHelpers.BuildPublicCatalog("app-1").Currency);

        // An empty string on the wire is not an answer.
        Assert.Equal(
            "USD",
            CatalogHelpers.BuildPublicCatalog("app-1", new List<AppTierModel> { Tier(currency: "") }).Currency);
    }

    [Fact]
    public void BuildPublicCatalog_KeepsOnlyActiveItems_InDisplayOrder()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1",
            new List<AppTierModel>
            {
                Tier(id: "late", displayOrder: 3),
                Tier(id: "retired", status: "Deprecated", displayOrder: 1),
                Tier(id: "early", displayOrder: 1)
            },
            new List<AppTierAddOnModel>
            {
                AddOn(id: "b", displayOrder: 2),
                AddOn(id: "off", status: "Inactive", displayOrder: 0),
                AddOn(id: "a", displayOrder: 1)
            });

        Assert.Equal(new List<string> { "early", "late" }, Ids(catalog.Tiers));
        Assert.Equal(new List<string> { "a", "b" }, Ids(catalog.AddOns));
        Assert.Equal("app-1", catalog.AppId);
        Assert.NotEqual(default, catalog.FetchedAt);
    }

    [Fact]
    public void BuildPublicCatalog_DoesNotMutateTheResponsesItWasGiven()
    {
        var tiers = new List<AppTierModel> { Tier(id: "b", displayOrder: 2), Tier(id: "a", displayOrder: 1) };

        CatalogHelpers.BuildPublicCatalog("app-1", tiers);

        Assert.Equal(new List<string> { "b", "a" }, Ids(tiers));
    }

    #endregion

    #region resolvePriceOption

    private static AppTierPricingModel Monthly()
    {
        return Pricing(id: "m", billingFrequency: "Monthly", displayOrder: 2);
    }

    private static AppTierPricingModel Yearly()
    {
        return Pricing(id: "y", billingFrequency: "Yearly", displayOrder: 3, price: 790m);
    }

    private static AppTierPricingModel Preferred()
    {
        return Pricing(id: "d", billingFrequency: "Quarterly", isDefault: true, displayOrder: 4);
    }

    private static AppTierPricingModel First()
    {
        return Pricing(id: "f", billingFrequency: "Weekly", displayOrder: 1);
    }

    [Fact]
    public void ResolvePriceOption_PrefersTheNamedOption()
    {
        var tier = Tier(pricingOptions: new List<AppTierPricingModel> { Monthly(), Yearly() });

        Assert.Equal("y", CatalogHelpers.ResolvePriceOption(tier, pricingId: "y")?.Id);
    }

    [Fact]
    public void ResolvePriceOption_FallsBackToTheBillingFrequency_MatchingAnnualSynonyms()
    {
        var item = Tier(pricingOptions: new List<AppTierPricingModel> { Monthly(), Yearly() });

        Assert.Equal("m", CatalogHelpers.ResolvePriceOption(item, pricingId: "gone", billing: "Monthly")?.Id);
        Assert.Equal("y", CatalogHelpers.ResolvePriceOption(item, billing: "annual")?.Id);
        Assert.Equal("m", CatalogHelpers.ResolvePriceOption(item, billing: "MONTHLY")?.Id);
    }

    [Fact]
    public void ResolvePriceOption_FallsBackToTheDefaultOption_ThenToTheFirstInDisplayOrder()
    {
        Assert.Equal(
            "d",
            CatalogHelpers.ResolvePriceOption(
                Tier(pricingOptions: new List<AppTierPricingModel> { First(), Monthly(), Preferred() }),
                billing: "Daily")?.Id);

        Assert.Equal(
            "f",
            CatalogHelpers.ResolvePriceOption(
                Tier(pricingOptions: new List<AppTierPricingModel> { Monthly(), First() }),
                billing: "Daily")?.Id);
    }

    [Fact]
    public void ResolvePriceOption_ReturnsNull_WhenTheItemSellsNoPricing()
    {
        Assert.Null(CatalogHelpers.ResolvePriceOption(Tier(pricingOptions: new List<AppTierPricingModel>())));
        Assert.Null(CatalogHelpers.ResolvePriceOption((AppTierModel?)null));
        Assert.Null(CatalogHelpers.ResolvePriceOption((AppTierAddOnModel?)null));
    }

    [Fact]
    public void ResolvePriceOption_WorksOnAddOnPricing_WhichCarriesNoDisplayOrder()
    {
        Assert.Equal("ap-1", CatalogHelpers.ResolvePriceOption(AddOn())?.Id);
    }

    #endregion

    #region trialLabel

    [Fact]
    public void TrialLabel_NamesTheTrial_AndSaysNothingWhenThereIsNone()
    {
        Assert.Equal("14-day free trial", CatalogHelpers.TrialLabel(14));
        Assert.Equal("1-day free trial", CatalogHelpers.TrialLabel(1));
        Assert.Equal(string.Empty, CatalogHelpers.TrialLabel(0));
        Assert.Equal(string.Empty, CatalogHelpers.TrialLabel(null));
        Assert.Equal(string.Empty, CatalogHelpers.TrialLabel(-7));
    }

    #endregion

    #region catalogToJsonLdOffers

    [Fact]
    public void CatalogToJsonLdOffers_PublishesLivePricesOnly_AndNeverInventsOne()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1",
            new List<AppTierModel>
            {
                Tier(
                    id: "pro",
                    name: "Pro",
                    currency: "USD",
                    pricingOptions: new List<AppTierPricingModel> { Pricing(price: 79m, isDefault: true) }),

                // No pricing at all: a "contact us" tier must not appear as an offer.
                Tier(id: "enterprise", name: "Enterprise", displayOrder: 1, pricingOptions: new List<AppTierPricingModel>()),

                // The operator hides the price, so it is not published as structured data either.
                Tier(id: "secret", name: "Secret", displayOrder: 2, showPrice: false)
            },
            new List<AppTierAddOnModel> { AddOn(id: "radar", name: "Radar") });

        var offers = CatalogHelpers.CatalogToJsonLdOffers(catalog, "https://example.test/pricing");

        Assert.Equal(2, offers.Count);

        Assert.Equal("Offer", offers[0].Type);
        Assert.Equal("Pro", offers[0].Name);
        Assert.Equal("79.00", offers[0].Price);
        Assert.Equal("USD", offers[0].PriceCurrency);
        Assert.Equal("https://example.test/pricing", offers[0].Url);

        Assert.Equal("Offer", offers[1].Type);
        Assert.Equal("Radar", offers[1].Name);
        Assert.Equal("19.00", offers[1].Price);
        Assert.Equal("USD", offers[1].PriceCurrency);
        Assert.Equal("https://example.test/pricing", offers[1].Url);
    }

    [Fact]
    public void CatalogToJsonLdOffers_OmitsTheUrlWhenNoneWasGiven()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog("app-1", new List<AppTierModel> { Tier(currency: "EUR") });

        var offers = CatalogHelpers.CatalogToJsonLdOffers(catalog);

        var offer = Assert.Single(offers);
        Assert.Equal("Offer", offer.Type);
        Assert.Equal("Pro", offer.Name);
        Assert.Equal("79.00", offer.Price);
        Assert.Equal("EUR", offer.PriceCurrency);
        Assert.Null(offer.Url);
    }

    #endregion

    #region parseAddOnIdList

    [Fact]
    public void ParseAddOnIdList_TrimsSplitsOnCommasAndWhitespace_AndDeDuplicates()
    {
        Assert.Equal(new List<string> { "a", "b", "c" }, CatalogHelpers.ParseAddOnIdList(" a, b  c ,,a "));
        Assert.Equal(new List<string> { "a", "b" }, CatalogHelpers.ParseAddOnIdList(new string?[] { "a", "b,a" }));
        Assert.Empty(CatalogHelpers.ParseAddOnIdList(string.Empty));
        Assert.Empty(CatalogHelpers.ParseAddOnIdList((string?)null));
        Assert.Empty(CatalogHelpers.ParseAddOnIdList((IReadOnlyList<string?>?)null));
    }

    [Fact]
    public void ParseAddOnIdList_CapsTheSelectionAtMaxAddOnSelection()
    {
        var many = new List<string>();
        for (var i = 0; i < CatalogHelpers.MaxAddOnSelection + 10; i++) many.Add("a" + i);

        Assert.Equal(
            CatalogHelpers.MaxAddOnSelection,
            CatalogHelpers.ParseAddOnIdList(string.Join(",", many.ToArray())).Count);
    }

    [Fact]
    public void ParseAddOnIdList_KeepsOnlyIdsTheCatalogSells_WhenOneIsGiven()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1",
            addOns: new List<AppTierAddOnModel> { AddOn(id: "radar"), AddOn(id: "vault", displayOrder: 1) });

        Assert.Equal(
            new List<string> { "radar", "vault" },
            CatalogHelpers.ParseAddOnIdList("radar,smuggled,vault", catalog));

        // The cap counts what survives the filter, not what was asked for.
        Assert.Empty(CatalogHelpers.ParseAddOnIdList("nope", catalog));
    }

    #endregion

    #region selectPacks

    [Fact]
    public void SelectPacks_ReturnsThePacksInCatalogOrder_NotTheOrderTheyWereAskedFor()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1",
            addOns: new List<AppTierAddOnModel>
            {
                AddOn(id: "a", displayOrder: 1),
                AddOn(id: "b", displayOrder: 2),
                AddOn(id: "c", displayOrder: 3)
            });

        Assert.Equal(
            new List<string> { "a", "c" },
            Ids(CatalogHelpers.SelectPacks(catalog, new List<string> { "c", "a" })));
        Assert.Empty(CatalogHelpers.SelectPacks(catalog, new List<string> { "unknown" }));
        Assert.Empty(CatalogHelpers.SelectPacks(catalog, new List<string>()));
        Assert.Empty(CatalogHelpers.SelectPacks(catalog, null));
    }

    #endregion

    #region catalog selection round trip

    [Fact]
    public void CatalogSelection_EncodesAndDecodesUnderTheTierPricingAddonsKeys()
    {
        var encoded = CatalogHelpers.EncodeCatalogSelection(new CatalogSelection
        {
            TierId = "tier-1",
            PricingId = "price-1",
            AddOnIds = new List<string> { "a", "b", "a" }
        });

        var decoded = CatalogHelpers.DecodeCatalogSelection(encoded);

        Assert.Equal("tier-1", decoded.TierId);
        Assert.Equal("price-1", decoded.PricingId);
        Assert.Equal(new List<string> { "a", "b" }, decoded.AddOnIds);
    }

    /// <summary>
    /// .NET-only: JS gets its query string from the platform's URLSearchParams, while this port
    /// writes one, so the urlencoded serializer is pinned here — a comma is %2C, not a raw comma.
    /// </summary>
    [Fact]
    public void EncodeCatalogSelection_WritesTheUrlencodedFormJsWrites()
    {
        var encoded = CatalogHelpers.EncodeCatalogSelection(new CatalogSelection
        {
            TierId = "t1",
            PricingId = "p2",
            AddOnIds = new List<string> { "a", "b" }
        });

        Assert.Equal("tier=t1&pricing=p2&addons=a%2Cb", encoded);
    }

    [Fact]
    public void DecodeCatalogSelection_DecodesAFullUrlALeadingQuestionMarkRepeatedParamsAndParsedParams()
    {
        var fromUrl = CatalogHelpers.DecodeCatalogSelection("https://example.test/signup?tier=t1&addons=a&addons=b#top");
        Assert.Equal("t1", fromUrl.TierId);
        Assert.Null(fromUrl.PricingId);
        Assert.Equal(new List<string> { "a", "b" }, fromUrl.AddOnIds);

        var fromQuery = CatalogHelpers.DecodeCatalogSelection("?tier= t1 &pricing=");
        Assert.Equal("t1", fromQuery.TierId);
        Assert.Null(fromQuery.PricingId);
        Assert.Empty(fromQuery.AddOnIds);

        Assert.Equal("t1", CatalogHelpers.DecodeCatalogSelection(QueryParameters.Parse("tier=t1")).TierId);
    }

    [Fact]
    public void EncodeCatalogSelection_EncodesNothingForAnEmptySelection()
    {
        Assert.Equal(string.Empty, CatalogHelpers.EncodeCatalogSelection(new CatalogSelection()));
        Assert.Equal(
            string.Empty,
            CatalogHelpers.EncodeCatalogSelection(new CatalogSelection { TierId = "  ", AddOnIds = new List<string>() }));
    }

    [Fact]
    public void AsSearchParams_NormalisesEveryUrlShape()
    {
        Assert.Equal("1", CatalogHelpers.AsSearchParams("a=1").Get("a"));
        Assert.Equal("1", CatalogHelpers.AsSearchParams("?a=1").Get("a"));
        Assert.Equal("1", CatalogHelpers.AsSearchParams("https://example.test/x?a=1").Get("a"));
        Assert.Null(CatalogHelpers.AsSearchParams(string.Empty).Get("a"));
        Assert.Null(CatalogHelpers.AsSearchParams(null).Get("a"));
    }

    #endregion

    #region grantsAccess (subscription/statusDisplay.ts)

    [Fact]
    public void GrantsAccess_CountsActiveTrialingAndPendingCancellation()
    {
        Assert.Equal(
            new List<string> { "Active", "Trialing", "PendingCancellation" },
            new List<string>(SubscriptionAccess.AccessGrantingStatuses));

        Assert.True(SubscriptionAccess.GrantsAccess("Active"));
        Assert.True(SubscriptionAccess.GrantsAccess("Trialing"));
        Assert.True(SubscriptionAccess.GrantsAccess("PendingCancellation"));

        Assert.False(SubscriptionAccess.GrantsAccess("Cancelled"));
        Assert.False(SubscriptionAccess.GrantsAccess("Expired"));
        // Not IsActive: a past-due row is unhealthy but was never a "grants access" status in JS.
        Assert.False(SubscriptionAccess.GrantsAccess("PastDue"));
        Assert.False(SubscriptionAccess.GrantsAccess(string.Empty));
        Assert.False(SubscriptionAccess.GrantsAccess(null));
    }

    #endregion
}
