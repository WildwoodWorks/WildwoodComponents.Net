using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The registration funnel reporter the Blazor signup components share, ported from
/// <c>@wildwood/react-shared</c>'s signupFunnel tests: view and start once, submit implies start,
/// errors carry a category only, plan keys prefer ids, and nothing ever throws into the form.
/// </summary>
public class SignupFunnelTests
{
    [Fact]
    public async Task ViewAndStartAreSentOncePerForm()
    {
        var attribution = new RecordingFunnelAttribution();
        var funnel = new SignupFunnel(attribution);

        await funnel.ViewAsync();
        await funnel.ViewAsync();
        await funnel.StartAsync();
        await funnel.StartAsync();

        Assert.Equal(new[] { "signup_view", "signup_start" }, attribution.Names);
    }

    [Fact]
    public async Task ASubmitWithoutAFocusStillStartsTheForm()
    {
        var attribution = new RecordingFunnelAttribution();
        var funnel = new SignupFunnel(attribution);

        await funnel.SubmitAsync();
        await funnel.SubmitAsync();

        // Start once; the engine dedups the repeated submit across the session.
        Assert.Equal(new[] { "signup_start", "signup_submit", "signup_submit" }, attribution.Names);
    }

    [Fact]
    public async Task ErrorsCarryACategoryNeverAMessage()
    {
        var attribution = new RecordingFunnelAttribution();
        var funnel = new SignupFunnel(attribution);

        await funnel.ErrorAsync("email_taken");
        await funnel.ErrorFromCodeAsync("USERNAME_EXISTS");
        await funnel.ErrorFromCodeAsync(null, 503);
        await funnel.ErrorAsync("That email is already registered to jane@example.com");

        Assert.All(attribution.Calls, call => Assert.Equal("signup_error", call.Name));
        Assert.Equal(
            new[] { "email_taken", "username_taken", "server", "that_email_is_already_registered_to_jane" },
            attribution.Calls.Select(c => c.Label));
    }

    [Fact]
    public async Task PlanEventsAreKeyedByIdThenBySlug()
    {
        var attribution = new RecordingFunnelAttribution();
        var funnel = new SignupFunnel(attribution);

        await funnel.PlanSelectedAsync("tier-pro", "Pro");
        await funnel.PlanSelectedAsync(null, "Team Plan");
        await funnel.CheckoutStartAsync("price-pro", "tier-pro");
        await funnel.CheckoutStartAsync(null, "tier-pro");

        Assert.Equal(
            new[] { "plan_selected:tier-pro", "plan_selected:team_plan", "checkout_start:price-pro", "checkout_start:tier-pro" },
            attribution.Calls.Select(c => c.Name + ":" + c.Label));
    }

    [Fact]
    public async Task WithoutAnAttributionServiceNothingHappens()
    {
        var funnel = new SignupFunnel(null);

        var exception = await Record.ExceptionAsync(async () =>
        {
            await funnel.ViewAsync();
            await funnel.SubmitAsync();
            await funnel.ErrorAsync("validation");
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task AFailingEngineNeverBreaksTheForm()
    {
        var funnel = new SignupFunnel(new RecordingFunnelAttribution { Throw = true });

        var exception = await Record.ExceptionAsync(async () =>
        {
            await funnel.ViewAsync();
            await funnel.SubmitAsync();
            await funnel.PlanSelectedAsync("tier-1");
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task AnImplementationWithoutFunnelSupportIsANoOp()
    {
        // The interface's default members keep a host's own IAttributionService compiling and working.
        IAttributionService legacy = new LegacyAttribution();

        await legacy.TrackAsync("signup_view");
        await legacy.TrackCtaAsync("hero");
        await legacy.FlushAsync();
    }

    private sealed class LegacyAttribution : IAttributionService
    {
        public Task<AttributionStateModel?> InitializeAsync(string appId, string? baseUrlOverride = null)
            => Task.FromResult<AttributionStateModel?>(null);

        public Task<AttributionTouchModel?> CaptureUrlAsync(string url, string? referrer = null)
            => Task.FromResult<AttributionTouchModel?>(null);

        public Task<AttributionPayloadModel?> GetForRegistrationAsync() => Task.FromResult<AttributionPayloadModel?>(null);

        public Task ClearAsync() => Task.CompletedTask;

        public Task<AttributionStateModel?> GetStateAsync() => Task.FromResult<AttributionStateModel?>(null);
    }
}

/// <summary>An attribution service that records the funnel events it is asked to track.</summary>
internal sealed class RecordingFunnelAttribution : IAttributionService
{
    public List<(string Name, string? Label, double? Value)> Calls { get; } = new();

    public IEnumerable<string> Names => Calls.Select(c => c.Name);

    public bool Throw { get; set; }

    public Task TrackAsync(string name, string? label = null, double? value = null)
    {
        if (Throw) throw new InvalidOperationException("engine failure");
        Calls.Add((name, label, value));
        return Task.CompletedTask;
    }

    public Task<AttributionStateModel?> InitializeAsync(string appId, string? baseUrlOverride = null)
        => Task.FromResult<AttributionStateModel?>(null);

    public Task<AttributionTouchModel?> CaptureUrlAsync(string url, string? referrer = null)
        => Task.FromResult<AttributionTouchModel?>(null);

    public Task<AttributionPayloadModel?> GetForRegistrationAsync() => Task.FromResult<AttributionPayloadModel?>(null);

    public Task ClearAsync() => Task.CompletedTask;

    public Task<AttributionStateModel?> GetStateAsync() => Task.FromResult<AttributionStateModel?>(null);
}
