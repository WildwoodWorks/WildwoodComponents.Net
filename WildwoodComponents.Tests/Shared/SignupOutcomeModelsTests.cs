using System.Text.Json;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The signup outcome is the shape all three stacks hand back from a finished signup, so it has
/// to round-trip field-for-field with the JS SignupOutcome.
/// </summary>
public class SignupOutcomeModelsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Outcome_RoundTripsTheFinishedSignupShape()
    {
        const string json = """
            {"userId":"u1",
             "tier":{"tierId":"t1","name":"Pro","pricingId":"p1"},
             "packs":[
               {"addOnId":"a1","name":"Granted Pack","status":"granted"},
               {"addOnId":"a2","name":"Extra Seats","status":"trialing","trialEnd":"2026-10-02T00:00:00Z"},
               {"addOnId":"a3","name":"Priority Support","status":"failed","errorMessage":"Your card was declined."}],
             "tokenGrant":{"tierId":"t1","pricingId":"p1","addOnIds":["a1"],"featureCodes":["DOCUMENTS"]},
             "planActivationPending":true}
            """;

        var outcome = JsonSerializer.Deserialize<SignupOutcome>(json, Web);

        Assert.NotNull(outcome);
        Assert.Equal("u1", outcome!.UserId);
        Assert.Equal("t1", outcome.Tier?.TierId);
        Assert.Equal("Pro", outcome.Tier?.Name);
        Assert.Equal("p1", outcome.Tier?.PricingId);
        Assert.True(outcome.PlanActivationPending);

        Assert.Equal(3, outcome.Packs.Count);
        Assert.Equal(SignupPackStatuses.Granted, outcome.Packs[0].Status);
        Assert.Null(outcome.Packs[0].TrialEnd);
        Assert.Equal(SignupPackStatuses.Trialing, outcome.Packs[1].Status);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), outcome.Packs[1].TrialEnd!.Value.ToUniversalTime());
        Assert.Equal(SignupPackStatuses.Failed, outcome.Packs[2].Status);
        Assert.Equal("Your card was declined.", outcome.Packs[2].ErrorMessage);

        Assert.Equal("t1", outcome.TokenGrant?.TierId);
        Assert.Equal(new[] { "a1" }, outcome.TokenGrant?.AddOnIds);
        Assert.Equal(new[] { "DOCUMENTS" }, outcome.TokenGrant?.FeatureCodes);

        var written = JsonSerializer.Serialize(outcome, Web);
        Assert.Contains("\"userId\":\"u1\"", written);
        Assert.Contains("\"addOnId\":\"a1\"", written);
        Assert.Contains("\"status\":\"granted\"", written);
        Assert.Contains("\"planActivationPending\":true", written);
    }

    [Fact]
    public void Outcome_NoPlanChosenLeavesTheTierNull()
    {
        var outcome = JsonSerializer.Deserialize<SignupOutcome>(
            """{"userId":"u1","tier":null,"packs":[]}""", Web);

        Assert.NotNull(outcome);
        Assert.Null(outcome!.Tier);
        Assert.Empty(outcome.Packs);
        Assert.Null(outcome.TokenGrant);
        Assert.Null(outcome.PlanActivationPending);
    }

    [Fact]
    public void Outcome_DefaultsAreAnEmptyOutcome()
    {
        var outcome = new SignupOutcome();

        Assert.Equal(string.Empty, outcome.UserId);
        Assert.Null(outcome.Tier);
        Assert.Empty(outcome.Packs);
        Assert.Null(outcome.PlanActivationPending);
    }

    [Fact]
    public void PackStatuses_CarryTheFourWireValues()
    {
        Assert.Equal("trialing", SignupPackStatuses.Trialing);
        Assert.Equal("active", SignupPackStatuses.Active);
        Assert.Equal("failed", SignupPackStatuses.Failed);
        Assert.Equal("granted", SignupPackStatuses.Granted);
    }
}
