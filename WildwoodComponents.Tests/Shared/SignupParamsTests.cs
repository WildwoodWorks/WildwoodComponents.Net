using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// Ported case for case from packages/wildwood-core/src/__tests__/signupParams.test.ts.
/// </summary>
public class SignupParamsTests
{
    private static AppTierAddOnModel AddOn(string id, int displayOrder = 0)
    {
        return new AppTierAddOnModel
        {
            Id = id,
            AppId = "app-1",
            Name = id,
            Description = string.Empty,
            Category = string.Empty,
            Status = "Active",
            DisplayOrder = displayOrder,
            IconClass = string.Empty,
            BadgeColor = string.Empty,
            PricingOptions = new List<AppTierAddOnPricingModel>()
        };
    }

    [Fact]
    public void Parse_ReadsTheSelectionAndTheIdentityHintsASignupLinkCarries()
    {
        var parameters = SignupParams.Parse(
            "?tier=tier-1&pricing=price-1&addons=radar,vault&token=TK-1&invite=inv-9&email=someone%40example.test");

        Assert.Equal("tier-1", parameters.TierId);
        Assert.Equal("price-1", parameters.PricingId);
        Assert.Equal(new List<string> { "radar", "vault" }, parameters.AddOnIds);
        Assert.Equal("TK-1", parameters.Token);
        Assert.Equal("inv-9", parameters.Invite);
        Assert.Equal("someone@example.test", parameters.Email);
    }

    [Fact]
    public void Parse_TrimsValuesAndDropsEmptyOnes()
    {
        var parameters = SignupParams.Parse("tier=%20tier-1%20&pricing=&token=%20&email=%20me%40example.test%20");

        Assert.Equal("tier-1", parameters.TierId);
        Assert.Null(parameters.PricingId);
        Assert.Empty(parameters.AddOnIds);
        Assert.Null(parameters.Token);
        Assert.Null(parameters.Invite);
        Assert.Equal("me@example.test", parameters.Email);
    }

    [Fact]
    public void Parse_ReturnsAnEmptySelectionForAQueryWithNothingInIt()
    {
        var parameters = SignupParams.Parse(string.Empty);

        Assert.Null(parameters.TierId);
        Assert.Null(parameters.PricingId);
        Assert.Empty(parameters.AddOnIds);
        Assert.Null(parameters.Token);
        Assert.Null(parameters.Invite);
        Assert.Null(parameters.Email);
    }

    [Fact]
    public void Parse_AcceptsAFullUrlAndAlreadyParsedParams()
    {
        Assert.Equal(
            new List<string> { "a", "b" },
            SignupParams.Parse("https://example.test/signup?tier=t1&addons=a%20b").AddOnIds);

        Assert.Equal("TK-2", SignupParams.Parse(QueryParameters.Parse("token=TK-2")).Token);
    }

    [Fact]
    public void Parse_DropsAddOnIdsTheAppDoesNotSell_OnceTheCatalogIsKnown()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1",
            addOns: new List<AppTierAddOnModel> { AddOn("radar"), AddOn("vault", 1) });

        Assert.Equal(
            new List<string> { "radar", "smuggled", "vault" },
            SignupParams.Parse("?addons=radar,smuggled,vault").AddOnIds);

        Assert.Equal(
            new List<string> { "radar", "vault" },
            SignupParams.Parse("?addons=radar,smuggled,vault", catalog).AddOnIds);
    }
}
