using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// <c>PlanDefault</c>: the plan the signup's grid OPENS on when nothing has chosen one.
/// </summary>
/// <remarks>
/// <para>
/// Ported from the JS suites that cover the same rule — <c>regsubSignupFlowDefault.test.tsx</c>
/// (the flow's <c>defaultTierId</c>) and the <c>planDefault</c> cases in
/// <c>RegistrationAndSubscription.signup.test.tsx</c> (what the grid does with it).
/// </para>
/// <para>
/// It is a suggestion, not a choice: the state machine never sees it, so every case here is about
/// which id the GRID is told to mark and nothing else. The companion render test in
/// <see cref="RegistrationSubscriptionSignupRenderTests"/> proves the other half — that a flow
/// carrying a default still has an empty selection and a plan step ahead of it.
/// </para>
/// </remarks>
public class SignupPlanDefaultDecisionsTests
{
    private static AppTierModel Tier(string id, string name, bool free = false)
    {
        return new AppTierModel { Id = id, Name = name, IsFreeTier = free };
    }

    /// <summary>A paid plan, a free one, and a second free one so "the first" means something.</summary>
    private static PublicCatalog Catalog(params AppTierModel[] tiers)
    {
        return new PublicCatalog { Tiers = new List<AppTierModel>(tiers) };
    }

    private static PublicCatalog StandardCatalog()
    {
        return Catalog(Tier("tier-pro", "Pro"), Tier("tier-free", "Starter", free: true));
    }

    #region The default itself

    [Fact]
    public void Free_names_the_apps_free_plan()
    {
        var id = SignupViewDecisions.DefaultTierId(SignupPlanDefault.Free, invite: false, StandardCatalog());

        Assert.Equal("tier-free", id);
    }

    /// <summary>The first one in catalog order, so two free plans do not make the answer arbitrary.</summary>
    [Fact]
    public void Free_names_the_first_free_plan_in_catalog_order()
    {
        var catalog = Catalog(
            Tier("tier-pro", "Pro"),
            Tier("tier-free", "Starter", free: true),
            Tier("tier-free-2", "Starter Plus", free: true));

        Assert.Equal("tier-free", SignupViewDecisions.DefaultTierId(SignupPlanDefault.Free, false, catalog));
    }

    [Fact]
    public void The_default_suggests_nothing()
    {
        Assert.Null(SignupViewDecisions.DefaultTierId(SignupPlanDefault.None, invite: false, StandardCatalog()));
    }

    /// <summary>Nothing to suggest is not a failure: the grid is the one it would have been anyway.</summary>
    [Fact]
    public void An_app_that_sells_no_free_plan_suggests_nothing()
    {
        var catalog = Catalog(Tier("tier-pro", "Pro"));

        Assert.Null(SignupViewDecisions.DefaultTierId(SignupPlanDefault.Free, invite: false, catalog));
    }

    /// <summary>An invite's plan comes from its token, so there is no grid for a default to open on.</summary>
    [Fact]
    public void Invite_redemption_suggests_nothing()
    {
        Assert.Null(SignupViewDecisions.DefaultTierId(SignupPlanDefault.Free, invite: true, StandardCatalog()));
    }

    [Fact]
    public void Without_a_catalog_there_is_nothing_to_name()
    {
        Assert.Null(SignupViewDecisions.DefaultTierId(SignupPlanDefault.Free, invite: false, null));
    }

    #endregion

    #region What the grid marks

    /// <summary>A plan the visitor actually chose beats both the default and the link.</summary>
    [Fact]
    public void A_chosen_plan_wins()
    {
        Assert.Equal("tier-chosen", SignupViewDecisions.HighlightTierId("tier-chosen", "tier-free", "tier-pro"));
    }

    /// <summary>
    /// The default sits AHEAD of the link's plan on purpose: a preselected id still showing here
    /// is one the flow already refused — stale, or hand-edited.
    /// </summary>
    [Fact]
    public void The_default_beats_the_links_plan()
    {
        Assert.Equal("tier-free", SignupViewDecisions.HighlightTierId(null, "tier-free", "tier-pro"));
    }

    [Fact]
    public void The_links_plan_still_applies_when_there_is_no_default()
    {
        Assert.Equal("tier-pro", SignupViewDecisions.HighlightTierId(null, null, "tier-pro"));
    }

    [Fact]
    public void With_nothing_to_go_on_the_grid_marks_nothing()
    {
        Assert.Null(SignupViewDecisions.HighlightTierId(null, null, null));
    }

    /// <summary>An empty string is not a plan, on any of the three.</summary>
    [Fact]
    public void An_empty_id_is_no_id_at_all()
    {
        Assert.Equal("tier-free", SignupViewDecisions.HighlightTierId(string.Empty, "tier-free", "tier-pro"));
        Assert.Equal("tier-pro", SignupViewDecisions.HighlightTierId(string.Empty, string.Empty, "tier-pro"));
    }

    #endregion
}
