using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The server-side capture rules, ported from <c>@wildwood/core</c>'s
/// <c>__tests__/attributionService.test.ts</c> ("AttributionService capture"). WildwoodAPI re-applies
/// these rules and drops whatever does not fit, so a drift here costs precision on every WebForms
/// signup.
/// </summary>
public class AttributionRulesTests
{
    private const string Landing = "https://app.example.com/pricing?utm_source=Reddit&utm_medium=Paid&utm_campaign=spring";

    [Fact]
    public void ParseTouch_ReadsTheUtmTagsAndLowercasesSourceAndMedium()
    {
        var touch = AttributionRules.ParseTouch(Landing, referrer: null);

        Assert.NotNull(touch);
        Assert.Equal("reddit", touch!.Source);
        Assert.Equal("paid", touch.Medium);
        Assert.Equal("spring", touch.Campaign);
        Assert.Equal("app.example.com", touch.LandingHost);
        Assert.Equal("/pricing", touch.LandingPath);
    }

    [Fact]
    public void ParseTouch_CapsLongValuesAndDropsOneCarryingAControlCharacter()
    {
        var url = "https://app.example.com/?utm_source=" + new string('s', 150) + "&utm_campaign=spring%00launch";

        var touch = AttributionRules.ParseTouch(url, referrer: null);

        Assert.NotNull(touch);
        Assert.Equal(AttributionRules.SourceMediumMaxLength, touch!.Source!.Length);
        Assert.Null(touch.Campaign);
    }

    [Fact]
    public void ParseTouch_TakesTheFirstWellFormedClickIdInPriorityOrder()
    {
        var touch = AttributionRules.ParseTouch(
            "https://app.example.com/?fbclid=fb-1&gclid=gc-1",
            referrer: null);

        Assert.NotNull(touch);
        Assert.Equal("gclid", touch!.ClickIdName);
        Assert.Equal("gc-1", touch.ClickIdValue);
    }

    [Fact]
    public void ParseTouch_IgnoresClickIdsWhenTheAppTurnsThemOff()
    {
        var touch = AttributionRules.ParseTouch(
            "https://app.example.com/?gclid=gc-1",
            referrer: null,
            new AttributionCaptureOptions { CaptureClickIds = false });

        Assert.Null(touch);
    }

    [Fact]
    public void ParseTouch_TurnsAnExternalReferrerWithoutUtmTagsIntoAReferralTouch()
    {
        var touch = AttributionRules.ParseTouch("https://app.example.com/", "https://www.news.example/story");

        Assert.NotNull(touch);
        Assert.Equal("news.example", touch!.ReferrerHost);
        Assert.Equal("news.example", touch.Source);
        Assert.Equal("referral", touch.Medium);
    }

    [Fact]
    public void ParseTouch_IgnoresASelfReferral()
    {
        var touch = AttributionRules.ParseTouch("https://app.example.com/pricing", "https://www.app.example.com/");

        Assert.Null(touch);
    }

    [Fact]
    public void ParseTouch_IsNullForADirectVisitAndForANonHttpUrl()
    {
        Assert.Null(AttributionRules.ParseTouch("https://app.example.com/pricing", referrer: null));
        Assert.Null(AttributionRules.ParseTouch("file:///c:/tmp/page.html?utm_source=x", referrer: null));
        Assert.Null(AttributionRules.ParseTouch("not a url", referrer: null));
        Assert.Null(AttributionRules.ParseTouch(null, referrer: null));
    }

    [Fact]
    public void ParseTouch_CapturesOnlyTheAllowlistedExtraParameters()
    {
        var touch = AttributionRules.ParseTouch(
            "https://app.example.com/?partner=acme&secret=shh",
            referrer: null,
            new AttributionCaptureOptions { ExtraAllowedParamNames = new[] { "partner" } });

        Assert.NotNull(touch);
        Assert.NotNull(touch!.ExtraParams);
        Assert.Equal("acme", touch.ExtraParams!["partner"]);
        Assert.False(touch.ExtraParams.ContainsKey("secret"));
    }

    [Fact]
    public void ParseTouch_KeepsAnExplicitPortOnTheLandingHost()
    {
        var touch = AttributionRules.ParseTouch("https://app.example.com:8443/?utm_source=x", referrer: null);

        Assert.Equal("app.example.com:8443", touch!.LandingHost);
    }

    [Fact]
    public void ParseTouch_WritesAnIsoCaptureTimeTheServerCanRead()
    {
        var at = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

        var touch = AttributionRules.ParseTouch(Landing, referrer: null, options: null, nowUtc: at);

        Assert.Equal("2026-09-13T12:00:00.000Z", touch!.OccurredAt);
    }

    [Fact]
    public void IsTouchExpired_ComparesAgainstTheWindowAndRejectsAnUnreadableTime()
    {
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var inside = new AttributionTouchModel { OccurredAt = "2026-09-08T12:00:00.000Z" };
        var outside = new AttributionTouchModel { OccurredAt = "2026-07-08T12:00:00.000Z" };
        var unreadable = new AttributionTouchModel { OccurredAt = "not a date" };

        Assert.False(AttributionRules.IsTouchExpired(inside, 30, now));
        Assert.True(AttributionRules.IsTouchExpired(outside, 30, now));
        Assert.True(AttributionRules.IsTouchExpired(unreadable, 30, now));
        Assert.True(AttributionRules.IsTouchExpired(null, 30, now));
    }

    [Fact]
    public void ClampWindowDays_StaysInsideTheRangeTheServerAccepts()
    {
        Assert.Equal(30, AttributionRules.ClampWindowDays(0));
        Assert.Equal(1, AttributionRules.ClampWindowDays(-5, fallback: -5));
        Assert.Equal(365, AttributionRules.ClampWindowDays(4000));
        Assert.Equal(60, AttributionRules.ClampWindowDays(60));
    }

    [Fact]
    public void GeneratedVisitorKeys_AreAcceptedByTheServersPattern()
    {
        Assert.True(AttributionRules.IsValidVisitorKey(AttributionRules.GenerateVisitorKey()));
        Assert.False(AttributionRules.IsValidVisitorKey("short"));
        Assert.False(AttributionRules.IsValidVisitorKey(null));
    }
}
