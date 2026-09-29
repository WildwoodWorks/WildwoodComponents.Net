using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The funnel rules in <see cref="AttributionRules"/>, ported from <c>@wildwood/core</c>'s
/// funnelTracker / attributionRules tests and <c>@wildwood/react-shared</c>'s signupFunnel tests.
/// The browser engines carry the same lists; <see cref="AttributionFunnelSourceTests"/> pins them to these.
/// </summary>
public class AttributionFunnelRulesTests
{
    [Fact]
    public void TheEventListsMatchTheJsSdk()
    {
        Assert.Equal(
            new[]
            {
                "page_view", "engaged", "scroll_depth", "time_on_page", "cta_click", "signup_view", "signup_start",
                "signup_submit", "signup_error", "plan_selected", "checkout_start"
            },
            AttributionRules.FunnelClientEvents);
        Assert.Equal(new[] { "signup_complete", "trial_started", "purchase" }, AttributionRules.FunnelServerOnlyEvents);
        Assert.Equal(25, AttributionRules.MaxFunnelEventsPerRequest);
        Assert.Equal(30, AttributionRules.FunnelSessionTimeoutMinutes);
    }

    [Theory]
    [InlineData("page_view", true)]
    [InlineData("demo_booked", true)]
    [InlineData("a", true)]
    [InlineData("Demo_Booked", false)]
    [InlineData("demo-booked", false)]
    [InlineData("demo booked", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a234567890123456789012345678901234567890", true)]
    [InlineData("a2345678901234567890123456789012345678901", false)]
    public void EventNamesHaveTheServerShape(string? name, bool valid)
    {
        Assert.Equal(valid, AttributionRules.IsValidFunnelEventName(name));
    }

    [Fact]
    public void TheAllowlistTakesStandardAndCustomNamesAndNeverAServerOnlyOne()
    {
        var custom = new[] { "demo_booked" };

        Assert.True(AttributionRules.IsAllowedFunnelEvent("cta_click"));
        Assert.True(AttributionRules.IsAllowedFunnelEvent("demo_booked", custom));
        Assert.False(AttributionRules.IsAllowedFunnelEvent("demo_booked"));
        Assert.False(AttributionRules.IsAllowedFunnelEvent("purchase", new[] { "purchase" }));
        Assert.False(AttributionRules.IsAllowedFunnelEvent("signup_complete"));
        Assert.False(AttributionRules.IsAllowedFunnelEvent("Bad Name", new[] { "Bad Name" }));
    }

    [Fact]
    public void TheConfigGateMatchesTheJsSdk()
    {
        // funnelTracker.ts accept(): no config, attribution off or funnel tracking off accepts nothing;
        // a custom name must be configured; a signup step needs trackSignupSteps.
        var on = new AttributionConfigModel
        {
            IsEnabled = true,
            FunnelTrackingEnabled = true,
            TrackSignupSteps = false,
            CustomEventNames = new List<string> { " Demo_Booked " }
        };

        Assert.True(AttributionRules.IsFunnelEventAcceptedBy(on, "page_view"));
        Assert.True(AttributionRules.IsFunnelEventAcceptedBy(on, "plan_selected"));
        Assert.True(AttributionRules.IsFunnelEventAcceptedBy(on, "demo_booked"));
        Assert.False(AttributionRules.IsFunnelEventAcceptedBy(on, "other_custom"));
        Assert.False(AttributionRules.IsFunnelEventAcceptedBy(on, "purchase"));
        foreach (var step in AttributionRules.FunnelSignupStepEvents)
        {
            Assert.False(AttributionRules.IsFunnelEventAcceptedBy(on, step));
        }

        on.TrackSignupSteps = true;
        Assert.True(AttributionRules.IsFunnelEventAcceptedBy(on, "signup_error"));

        Assert.False(AttributionRules.IsFunnelEventAcceptedBy(null, "page_view"));
        Assert.False(AttributionRules.IsFunnelEventAcceptedBy(new AttributionConfigModel { IsEnabled = true }, "page_view"));
        Assert.False(AttributionRules.IsFunnelEventAcceptedBy(
            new AttributionConfigModel { IsEnabled = false, FunnelTrackingEnabled = true }, "page_view"));
        Assert.Equal(new[] { "signup_view", "signup_start", "signup_submit", "signup_error" }, AttributionRules.FunnelSignupStepEvents);
    }

    [Fact]
    public void CustomNamesAreNormalizedDedupedAndCapped()
    {
        var names = AttributionRules.NormalizeCustomEventNames(
            new[] { " Demo_Booked ", "demo_booked", "PAGE_VIEW", "purchase", "bad name", null, "webinar_joined" });

        Assert.Equal(new[] { "demo_booked", "webinar_joined" }, names);

        var many = Enumerable.Range(0, 80).Select(i => "custom_" + i).ToArray();
        Assert.Equal(AttributionRules.MaxCustomEventNames, AttributionRules.NormalizeCustomEventNames(many).Count);
        Assert.Empty(AttributionRules.NormalizeCustomEventNames(null));
    }

    [Theory]
    [InlineData("Mobile", "mobile")]
    [InlineData(" tablet ", "tablet")]
    [InlineData("desktop", "desktop")]
    [InlineData("phone", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DeviceClassesNormalize(string? raw, string? expected)
    {
        Assert.Equal(expected, AttributionRules.NormalizeDeviceClass(raw));
    }

    [Theory]
    [InlineData(500, false, "mobile")]
    [InlineData(767, false, "mobile")]
    [InlineData(768, false, "tablet")]
    [InlineData(1023, false, "tablet")]
    [InlineData(1100, false, "desktop")]
    [InlineData(1100, true, "tablet")]
    [InlineData(1280, true, "desktop")]
    [InlineData(0, false, null)]
    public void TheViewportBucketsMatchTheBrowserEngines(int width, bool coarse, string? expected)
    {
        Assert.Equal(expected, AttributionRules.DeviceClassFromViewport(width, coarse));
    }

    [Theory]
    [InlineData("USERNAME_EXISTS", null, "username_taken")]
    [InlineData("UsernameExists", null, "username_taken")]
    [InlineData("USER_EXISTS", null, "email_taken")]
    [InlineData("DUPLICATE_EMAIL", null, "email_taken")]
    [InlineData("PASSWORD_INVALID", null, "password_policy")]
    [InlineData("PASSWORD_TOO_SHORT", null, "password_policy")]
    [InlineData("HCAPTCHA_FAILED", null, "captcha")]
    [InlineData("TOKEN_EXPIRED", null, "invalid_token")]
    [InlineData("registration_token_rejected", null, "invalid_token")]
    [InlineData("REGISTRATION_NOT_ALLOWED", null, "registration_closed")]
    [InlineData("SELF_REGISTRATION_DISABLED", null, "registration_closed")]
    [InlineData("RATE_LIMIT_EXCEEDED", null, "rate_limited")]
    [InlineData("NETWORK_ERROR", null, "network")]
    [InlineData("USER_CREATION_FAILED", null, "server")]
    [InlineData("VALIDATION_ERROR", null, "validation")]
    [InlineData(null, 0, "network")]
    [InlineData("", 429, "rate_limited")]
    [InlineData("SOMETHING_ELSE", 503, "server")]
    [InlineData(null, 400, "validation")]
    [InlineData(null, 422, "validation")]
    [InlineData(null, 404, "unknown")]
    [InlineData("SOMETHING_ELSE", null, "unknown")]
    [InlineData(null, null, "unknown")]
    public void SignupErrorCodesMapToCategories(string? code, int? status, string expected)
    {
        var category = AttributionRules.SignupErrorCategoryFromCode(code, status);

        Assert.Equal(expected, category);
        Assert.Contains(category, AttributionRules.SignupErrorCategories);
    }

    [Theory]
    [InlineData("username_taken", "username_taken")]
    [InlineData("USERNAME EXISTS!", "username_exists")]
    [InlineData("__x__", "x")]
    [InlineData("!!!", "unknown")]
    [InlineData(null, "unknown")]
    public void SignupErrorLabelsTakeTheCategoryShape(string? label, string expected)
    {
        Assert.Equal(expected, AttributionRules.SignupErrorLabel(label));
    }

    [Fact]
    public void ALongErrorLabelIsCappedWithoutATrailingSeparator()
    {
        var label = AttributionRules.SignupErrorLabel(new string('a', 39) + " bbbb");

        Assert.Equal(new string('a', 39), label);
        Assert.True(AttributionRules.IsValidFunnelEventName(label));
    }

    [Theory]
    [InlineData("tier-1", null, "tier-1")]
    [InlineData("  tier-1  ", "Pro", "tier-1")]
    [InlineData(null, "Pro Annual!", "pro_annual")]
    [InlineData("", "", null)]
    [InlineData(null, null, null)]
    public void PlanKeysPreferTheIdThenASlug(string? id, string? name, string? expected)
    {
        Assert.Equal(expected, AttributionRules.SignupPlanKey(id, name));
    }

    [Theory]
    [InlineData("/pricing?x=1#top", "/pricing")]
    [InlineData("pricing", "/pricing")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void FunnelPathsDropTheQuery(string? raw, string? expected)
    {
        Assert.Equal(expected, AttributionRules.NormalizeFunnelPath(raw));
    }

    [Fact]
    public void FunnelLabelsAreTrimmedAndCapped()
    {
        Assert.Equal("pricing", AttributionRules.NormalizeFunnelLabel("  pricing  "));
        Assert.Equal(100, AttributionRules.NormalizeFunnelLabel(new string('x', 150))!.Length);
        Assert.Null(AttributionRules.NormalizeFunnelLabel("   "));
    }
}
