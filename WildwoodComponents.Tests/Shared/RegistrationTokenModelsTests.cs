using System.Text.Json;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The registration-token grant models must read what
/// GET api/registrationtokens/validate-detailed/{token} answers, names included.
/// </summary>
public class RegistrationTokenModelsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Details_ReadTheGrantsWithTheirDisplayNames()
    {
        const string json = """
            {"isValid":true,"appGrants":[
              {"appId":"app-1","appName":"Cairn","appTierId":"t1","appTierName":"Pro",
               "appTierPricingId":"p1","pricingName":"Monthly",
               "addOnIds":["a1","a2"],"addOnNames":["Extra Seats","Priority Support"],
               "featureCodes":["DOCUMENTS","AI_CHAT"],"featureNames":["Documents","AI Chat"]}]}
            """;

        var details = JsonSerializer.Deserialize<RegistrationTokenDetails>(json, Web);

        Assert.NotNull(details);
        Assert.True(details!.IsValid);
        Assert.Null(details.ErrorMessage);

        var grant = Assert.Single(details.AppGrants);
        Assert.Equal("app-1", grant.AppId);
        Assert.Equal("Cairn", grant.AppName);
        Assert.Equal("t1", grant.AppTierId);
        Assert.Equal("Pro", grant.AppTierName);
        Assert.Equal("p1", grant.AppTierPricingId);
        Assert.Equal("Monthly", grant.PricingName);
        Assert.Equal(new[] { "a1", "a2" }, grant.AddOnIds);
        Assert.Equal(new[] { "Extra Seats", "Priority Support" }, grant.AddOnNames);
        Assert.Equal(new[] { "DOCUMENTS", "AI_CHAT" }, grant.FeatureCodes);
        Assert.Equal(new[] { "Documents", "AI Chat" }, grant.FeatureNames);
    }

    [Fact]
    public void Details_TokenThatOnlyGrantsAppAccessHasNoGrants()
    {
        var details = JsonSerializer.Deserialize<RegistrationTokenDetails>(
            """{"isValid":true,"appGrants":[]}""", Web);

        Assert.NotNull(details);
        Assert.True(details!.IsValid);
        Assert.Empty(details.AppGrants);
    }

    [Fact]
    public void Details_InvalidTokenCarriesTheServersReason()
    {
        var details = JsonSerializer.Deserialize<RegistrationTokenDetails>(
            """{"isValid":false,"errorMessage":"That token has expired.","appGrants":[]}""", Web);

        Assert.NotNull(details);
        Assert.False(details!.IsValid);
        Assert.Equal("That token has expired.", details.ErrorMessage);
    }

    [Fact]
    public void Grant_OptionalNameFieldsAreNullWhenTheServerOmitsThem()
    {
        const string json = """
            {"appId":"app-1","appTierId":"t1","addOnIds":[],"featureCodes":[]}
            """;

        var grant = JsonSerializer.Deserialize<RegistrationTokenAppGrant>(json, Web);

        Assert.NotNull(grant);
        Assert.Null(grant!.AppName);
        Assert.Null(grant.AppTierName);
        Assert.Null(grant.AppTierPricingId);
        Assert.Null(grant.PricingName);
        Assert.Null(grant.AddOnNames);
        Assert.Null(grant.FeatureNames);
        Assert.Empty(grant.AddOnIds);
        Assert.Empty(grant.FeatureCodes);
    }

    [Fact]
    public void Details_DefaultsAreAnInvalidTokenWithNoGrants()
    {
        var details = new RegistrationTokenDetails();

        Assert.False(details.IsValid);
        Assert.Empty(details.AppGrants);
    }
}
