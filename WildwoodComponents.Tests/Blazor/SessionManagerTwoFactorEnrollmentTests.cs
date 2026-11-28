using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Http;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The Blazor session manager's two-factor enrollment state, ported from @wildwood/core
/// SessionManager (isTwoFactorEnrollmentPending, completeTwoFactorEnrollment, the
/// twoFactorEnrollmentRequired / twoFactorEnrollmentCompleted events) and tokenUtils.
/// </summary>
public class SessionManagerTwoFactorEnrollmentTests
{
    private static string Jwt(string sub, bool pendingClaim, object? claimValue = null)
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var claim = pendingClaim ? $",\"{TwoFactorEnrollmentState.PendingClaim}\":{(claimValue ?? "\"true\"")}" : string.Empty;
        return Encode("{\"alg\":\"none\"}") + "." + Encode($"{{\"sub\":\"{sub}\",\"exp\":{exp}{claim}}}") + ".sig";
    }

    private static AuthenticationResponse Response(string sub, bool flag, bool claim) => new()
    {
        UserId = sub,
        Email = sub + "@example.com",
        JwtToken = Jwt(sub, claim),
        RefreshToken = "refresh-" + sub,
        RequiresTwoFactorEnrollment = flag
    };

    private sealed class Harness
    {
        public ScriptedHttpMessageHandler Handler { get; } = new();
        public FakeLocalStorageService Storage { get; } = new();
        public WildwoodApiEvents ApiEvents { get; } = new();
        public List<string> Required { get; } = new();
        public int Completed { get; private set; }
        public WildwoodSessionManager Session { get; }

        public Harness(bool autoRefresh = false)
        {
            var auth = new AuthenticationService(
                Handler.CreateClient(), Storage, NullLogger<AuthenticationService>.Instance);
            Session = new WildwoodSessionManager(
                auth, null, Storage, NullLogger<WildwoodSessionManager>.Instance,
                new WildwoodComponentsOptions { EnableAutoTokenRefresh = autoRefresh },
                ApiEvents);
            Session.TwoFactorEnrollmentRequired += (_, e) => Required.Add(e.Source);
            Session.TwoFactorEnrollmentCompleted += (_, _) => Completed++;
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_restricted_sign_in_is_pending_by_the_flag_or_the_claim(bool flag, bool claim)
    {
        var h = new Harness();

        await h.Session.LoginAsync(Response("u1", flag, claim));

        Assert.True(h.Session.IsTwoFactorEnrollmentPending);
        Assert.Equal(new[] { TwoFactorEnrollmentState.SourceAuthResponse }, h.Required);
    }

    [Fact]
    public async Task An_unrestricted_sign_in_is_not_pending_and_raises_nothing()
    {
        var h = new Harness();

        await h.Session.LoginAsync(Response("u1", false, false));

        Assert.False(h.Session.IsTwoFactorEnrollmentPending);
        Assert.Empty(h.Required);
        Assert.Equal(0, h.Completed);
    }

    /// <summary>The refresh response carries only the claim; an unrestricted token lifts it.</summary>
    [Fact]
    public async Task Completing_enrollment_forces_a_refresh_even_with_auto_refresh_off()
    {
        var h = new Harness(autoRefresh: false);
        await h.Storage.SetItemAsync(WildwoodStorageKeys.RefreshToken, "refresh-u1");
        h.Handler.On("api/auth/refresh-token",
            $"{{\"userId\":\"u1\",\"jwtToken\":\"{Jwt("u1", false)}\",\"refreshToken\":\"refresh-u1b\"}}");
        await h.Session.LoginAsync(Response("u1", true, true));

        var lifted = await h.Session.CompleteTwoFactorEnrollmentAsync();

        Assert.True(lifted);
        Assert.False(h.Session.IsTwoFactorEnrollmentPending);
        Assert.Equal(1, h.Completed);
        Assert.Equal(1, h.Handler.Count("api/auth/refresh-token"));
    }

    [Fact]
    public async Task Completing_reports_false_while_the_refreshed_token_is_still_restricted()
    {
        var h = new Harness();
        await h.Storage.SetItemAsync(WildwoodStorageKeys.RefreshToken, "refresh-u1");
        h.Handler.On("api/auth/refresh-token",
            $"{{\"userId\":\"u1\",\"jwtToken\":\"{Jwt("u1", true)}\",\"refreshToken\":\"refresh-u1b\"}}");
        await h.Session.LoginAsync(Response("u1", true, false));

        Assert.False(await h.Session.CompleteTwoFactorEnrollmentAsync());
        Assert.True(h.Session.IsTwoFactorEnrollmentPending);
        Assert.Equal(0, h.Completed);
    }

    [Fact]
    public async Task Completing_when_never_restricted_is_true_without_a_refresh()
    {
        var h = new Harness();
        await h.Session.LoginAsync(Response("u1", false, false));

        Assert.True(await h.Session.CompleteTwoFactorEnrollmentAsync());
        Assert.Equal(0, h.Handler.Count("api/auth/refresh-token"));
    }

    /// <summary>The event source is application-wide; only THIS user's refusal restricts the session.</summary>
    [Fact]
    public async Task An_api_refusal_restricts_only_the_refused_users_session()
    {
        var h = new Harness();
        await h.Session.LoginAsync(Response("u1", false, false));

        h.ApiEvents.RaiseTwoFactorEnrollmentRequired(new TwoFactorEnrollmentRequiredEventArgs { UserId = "someone-else" });
        Assert.False(h.Session.IsTwoFactorEnrollmentPending);

        // Unattributable (no bearer on the refused request): ignored on a shared event source.
        h.ApiEvents.RaiseTwoFactorEnrollmentRequired(new TwoFactorEnrollmentRequiredEventArgs { UserId = null });
        Assert.False(h.Session.IsTwoFactorEnrollmentPending);

        h.ApiEvents.RaiseTwoFactorEnrollmentRequired(new TwoFactorEnrollmentRequiredEventArgs { UserId = "U1" });
        Assert.True(h.Session.IsTwoFactorEnrollmentPending);
        Assert.Equal(new[] { TwoFactorEnrollmentState.SourceApiRefusal }, h.Required);
    }

    [Fact]
    public async Task Signing_out_clears_the_restriction()
    {
        var h = new Harness();
        await h.Session.LoginAsync(Response("u1", true, false));

        await h.Session.LogoutAsync();

        Assert.False(h.Session.IsTwoFactorEnrollmentPending);
    }

    [Fact]
    public void A_refusal_with_nobody_signed_in_changes_nothing()
    {
        var h = new Harness();

        h.ApiEvents.RaiseTwoFactorEnrollmentRequired(new TwoFactorEnrollmentRequiredEventArgs { UserId = "u1" });

        Assert.False(h.Session.IsTwoFactorEnrollmentPending);
        Assert.Empty(h.Required);
    }
}

/// <summary>Port of the tokenUtils isTwoFactorEnrollmentPending cases.</summary>
public class TwoFactorEnrollmentStateTests
{
    private static string Token(string payloadJson)
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Encode("{\"alg\":\"none\"}") + "." + Encode(payloadJson) + ".sig";
    }

    [Theory]
    [InlineData("{\"tfa_enroll_pending\":\"true\"}", true)]
    [InlineData("{\"tfa_enroll_pending\":true}", true)]
    [InlineData("{\"tfa_enroll_pending\":\"false\"}", false)]
    [InlineData("{\"sub\":\"u1\"}", false)]
    public void Reads_the_claim(string payload, bool expected)
    {
        Assert.Equal(expected, TwoFactorEnrollmentState.TokenCarriesPendingClaim(Token(payload)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.!!!.c")]
    public void A_missing_or_malformed_token_is_not_pending(string? token)
    {
        Assert.False(TwoFactorEnrollmentState.TokenCarriesPendingClaim(token));
    }

    [Fact]
    public void The_flag_alone_is_enough()
    {
        Assert.True(TwoFactorEnrollmentState.IsPending(true, null));
    }

    [Fact]
    public void The_error_code_is_the_wire_value()
    {
        Assert.Equal("TWO_FACTOR_ENROLLMENT_REQUIRED", WildwoodComponents.Shared.Models.AuthErrorCodes.TwoFactorEnrollmentRequired);
        Assert.Equal(TwoFactorEnrollmentRequiredEventArgs.ErrorCode, WildwoodComponents.Shared.Models.AuthErrorCodes.TwoFactorEnrollmentRequired);
    }
}
