using System.Text.Json;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// Ported case for case from packages/wildwood-core/src/__tests__/tierChange.test.ts.
/// </summary>
public class TierChangeRulesTests
{
    private static AppTierModel Tier(string id, int displayOrder, bool allowUpgrades, bool allowDowngrades) => new()
    {
        Id = id,
        DisplayOrder = displayOrder,
        AllowUpgrades = allowUpgrades,
        AllowDowngrades = allowDowngrades
    };

    private static readonly AppTierModel Free = Tier("free", 1, true, false);
    private static readonly AppTierModel Pro = Tier("pro", 2, false, false);
    private static readonly AppTierModel Business = Tier("business", 3, true, true);

    [Fact]
    public void Follows_the_current_tier_as_the_server_does_upgrades_need_allowUpgrades()
    {
        Assert.True(TierChangeRules.IsTierChangeAllowed(Free, Pro));
        Assert.False(TierChangeRules.IsTierChangeAllowed(Pro, Business));
    }

    [Fact]
    public void Treats_every_non_upgrade_as_a_downgrade_needing_allowDowngrades()
    {
        Assert.False(TierChangeRules.IsTierChangeAllowed(Pro, Free));
        Assert.True(TierChangeRules.IsTierChangeAllowed(Business, Free));
        // Same display order is not an upgrade on the server either.
        Assert.False(TierChangeRules.IsTierChangeAllowed(Tier("a", 2, true, false), Tier("b", 2, true, true)));
    }

    [Fact]
    public void Allows_a_first_subscription_and_staying_put()
    {
        Assert.True(TierChangeRules.IsTierChangeAllowed(null, Pro));
        Assert.True(TierChangeRules.IsTierChangeAllowed(Pro, Tier("PRO", 2, false, false)));
    }

    /// <summary>
    /// JS: "allows a change when an older server leaves the flags out". In .NET that is the
    /// model's default, so it is proved through deserialization of a payload without them.
    /// </summary>
    [Fact]
    public void Allows_a_change_when_an_older_server_leaves_the_flags_out()
    {
        var legacy = JsonSerializer.Deserialize<AppTierModel>(
            "{\"id\":\"legacy\",\"displayOrder\":2}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.True(TierChangeRules.IsTierChangeAllowed(legacy, Business));
        Assert.True(TierChangeRules.IsTierChangeAllowed(legacy, Free));
    }

    [Fact]
    public void A_payload_that_carries_the_flags_is_read_as_sent()
    {
        var locked = JsonSerializer.Deserialize<AppTierModel>(
            "{\"id\":\"locked\",\"displayOrder\":2,\"allowUpgrades\":false,\"allowDowngrades\":false}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.False(TierChangeRules.IsTierChangeAllowed(locked, Business));
        Assert.False(TierChangeRules.IsTierChangeAllowed(locked, Free));
    }
}
