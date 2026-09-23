using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The shared 60-second cache in front of the public catalog — the .NET spelling of JS's
/// <c>usePublicCatalog</c>.
/// </summary>
/// <remarks>
/// Four rules matter, and each one is here because breaking it is invisible in a browser until a
/// customer hits it: N surfaces on a page make ONE pair of requests, a cached catalog is not read
/// again inside the TTL, a FAILURE is never cached (otherwise a blip pins "pricing is unavailable"
/// for a minute), and an invalidation drops every currency view of the app it names and nothing
/// else.
/// </remarks>
public class PublicCatalogServiceTests
{
    private const string TiersJson = """
        [{"id":"tier-pro","appId":"app-1","name":"Pro","status":"Active","displayOrder":1,"currency":"USD",
          "pricingOptions":[{"id":"price-pro","price":79,"billingFrequency":"Monthly","isDefault":true}]}]
        """;

    private const string AddOnsJson = """
        [{"id":"pack-docs","appId":"app-1","name":"Docs Pack","status":"Active","displayOrder":1,
          "pricingOptions":[{"id":"ao-docs","price":9,"billingFrequency":"Monthly","isDefault":true}]}]
        """;

    /// <summary>A clock the test moves, so the TTL is exercised without anybody waiting a minute.</summary>
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    /// <summary>
    /// A handler that answers nothing until the test opens the gate, so "two callers, one load"
    /// is asserted against a load that really is still in flight.
    /// </summary>
    private sealed class GatedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public void Open() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            await _gate.Task;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        }
    }

    private static (PublicCatalogService Service, FakeHttpMessageHandler Handler, TestClock Clock) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        handler.WhenOk("app-tiers/app-1/public", TiersJson);
        handler.WhenOk("app-tier-addons/app-1/public", AddOnsJson);

        var clock = new TestClock();
        var service = new PublicCatalogService(
            new AppTierComponentService(handler.CreateClient("https://api.test/"),
                NullLogger<AppTierComponentService>.Instance),
            NullLogger<PublicCatalogService>.Instance,
            clock);

        return (service, handler, clock);
    }

    [Fact]
    public async Task GetAsync_builds_the_catalog_from_the_live_public_responses()
    {
        var (service, handler, _) = CreateService();

        var catalog = await service.GetAsync("app-1");

        Assert.Equal("app-1", catalog.AppId);
        Assert.Equal("USD", catalog.Currency);
        Assert.Equal("tier-pro", Assert.Single(catalog.Tiers).Id);
        Assert.Equal("pack-docs", Assert.Single(catalog.AddOns).Id);
        Assert.Equal(2, handler.Requests.Count); // tiers + packs, once
    }

    [Fact]
    public async Task GetAsync_serves_the_cached_catalog_inside_the_ttl()
    {
        var (service, handler, clock) = CreateService();

        await service.GetAsync("app-1");
        clock.Advance(TimeSpan.FromSeconds(59));
        await service.GetAsync("app-1");

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_reads_the_catalog_again_once_the_ttl_has_passed()
    {
        var (service, handler, clock) = CreateService();

        await service.GetAsync("app-1");
        clock.Advance(TimeSpan.FromSeconds(61));
        await service.GetAsync("app-1");

        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_reads_the_catalog_again_when_a_retry_forces_it()
    {
        var (service, handler, _) = CreateService();

        await service.GetAsync("app-1");
        await service.GetAsync("app-1", null, forceRefresh: true);

        Assert.Equal(4, handler.Requests.Count);
    }

    /// <summary>
    /// The whole point of the shared cache: a page with a plan grid, a pack grid and an upgrade
    /// panel makes one pair of requests, not three.
    /// </summary>
    [Fact]
    public async Task GetAsync_shares_one_in_flight_load_between_concurrent_callers()
    {
        var handler = new GatedHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var service = new PublicCatalogService(
            new AppTierComponentService(client, NullLogger<AppTierComponentService>.Instance),
            NullLogger<PublicCatalogService>.Instance,
            new TestClock());

        var first = service.GetAsync("app-1");
        var second = service.GetAsync("app-1");
        var third = service.GetAsync("app-1");

        handler.Open();
        await Task.WhenAll(first, second, third);

        // Two requests in total (tiers, then packs) — not two per caller.
        Assert.Equal(2, handler.Requests);
        Assert.Same(await first, await second);
        Assert.Same(await second, await third);
    }

    /// <summary>
    /// A failure is NEVER cached. An unreadable catalog must not hold "pricing is unavailable" on
    /// the page for the TTL, so the next call — a Retry, or the next surface to mount — tries
    /// again.
    /// </summary>
    [Fact]
    public async Task GetAsync_does_not_cache_a_failure()
    {
        var handler = new FakeHttpMessageHandler();
        handler.When("app-tiers/app-1/public", HttpStatusCode.InternalServerError, """{"error":"boom"}""");
        var service = new PublicCatalogService(
            new AppTierComponentService(handler.CreateClient("https://api.test/"),
                NullLogger<AppTierComponentService>.Instance),
            NullLogger<PublicCatalogService>.Instance,
            new TestClock());

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetAsync("app-1"));
        var before = handler.Requests.Count;

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetAsync("app-1"));

        Assert.True(handler.Requests.Count > before, "the failed load was cached; the retry never left the process");
    }

    /// <summary>A failure gone away means the very next call succeeds, with no forced refresh.</summary>
    [Fact]
    public async Task GetAsync_succeeds_on_the_next_call_after_a_transient_failure()
    {
        var handler = new FakeHttpMessageHandler
        {
            DefaultStatus = HttpStatusCode.InternalServerError,
            DefaultJson = """{"error":"boom"}"""
        };
        var service = new PublicCatalogService(
            new AppTierComponentService(handler.CreateClient("https://api.test/"),
                NullLogger<AppTierComponentService>.Instance),
            NullLogger<PublicCatalogService>.Instance,
            new TestClock());

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetAsync("app-1"));

        handler.DefaultStatus = HttpStatusCode.OK;
        handler.WhenOk("app-tiers/app-1/public", TiersJson);
        handler.WhenOk("app-tier-addons/app-1/public", AddOnsJson);

        var catalog = await service.GetAsync("app-1");

        Assert.Single(catalog.Tiers);
    }

    [Fact]
    public async Task Invalidate_drops_the_named_apps_catalog()
    {
        var (service, handler, _) = CreateService();

        await service.GetAsync("app-1");
        service.Invalidate("app-1");
        await service.GetAsync("app-1");

        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Invalidate_with_no_app_drops_everything()
    {
        var (service, handler, _) = CreateService();

        await service.GetAsync("app-1");
        service.Invalidate();
        await service.GetAsync("app-1");

        Assert.Equal(4, handler.Requests.Count);
    }

    /// <summary>
    /// Two surfaces asking in different currencies must not read each other's answer — the
    /// override is part of the key — and invalidating the app drops both views of it.
    /// </summary>
    [Fact]
    public async Task The_currency_override_is_part_of_the_cache_key()
    {
        var (service, handler, _) = CreateService();

        await service.GetAsync("app-1");
        await service.GetAsync("app-1", "EUR");
        Assert.Equal(4, handler.Requests.Count);

        await service.GetAsync("app-1", "EUR");
        Assert.Equal(4, handler.Requests.Count);

        service.Invalidate("app-1");
        await service.GetAsync("app-1", "EUR");
        Assert.Equal(6, handler.Requests.Count);
    }

    /// <summary>
    /// The key joins the app id and the currency override with a separator no id and no ISO code
    /// can contain. Joined with nothing, ("tenant1", "GBP") and ("tenant1GBP", null) collapse onto
    /// ONE key and one tenant is served the other's prices for the whole window. The separator is
    /// a character no editor renders, so this ambiguity is written as a test: a reformat that eats
    /// it fails here rather than in production.
    /// </summary>
    [Fact]
    public async Task An_app_id_ending_in_a_currency_code_cannot_collide_with_that_currency()
    {
        const string OtherTiersJson = """
            [{"id":"tier-other","appId":"tenant1GBP","name":"Other","status":"Active","displayOrder":1,"currency":"USD",
              "pricingOptions":[{"id":"price-other","price":20,"billingFrequency":"Monthly","isDefault":true}]}]
            """;

        var handler = new FakeHttpMessageHandler();
        handler.WhenOk("app-tier-addons/tenant1GBP/public", "[]");
        handler.WhenOk("app-tiers/tenant1GBP/public", OtherTiersJson);
        handler.WhenOk("app-tier-addons/tenant1/public", "[]");
        handler.WhenOk("app-tiers/tenant1/public", TiersJson);
        var service = new PublicCatalogService(
            new AppTierComponentService(handler.CreateClient("https://api.test/"),
                NullLogger<AppTierComponentService>.Instance),
            NullLogger<PublicCatalogService>.Instance,
            new TestClock());

        var withOverride = await service.GetAsync("tenant1", "GBP");
        var otherApp = await service.GetAsync("tenant1GBP");

        // Two loads, not one answer served twice.
        Assert.Equal(4, handler.Requests.Count);
        Assert.NotSame(withOverride, otherApp);
        Assert.Equal("tenant1", withOverride.AppId);
        Assert.Equal("tenant1GBP", otherApp.AppId);
        Assert.Equal("tier-pro", Assert.Single(withOverride.Tiers).Id);
        Assert.Equal("tier-other", Assert.Single(otherApp.Tiers).Id);

        // And the second read did not overwrite the first's entry.
        Assert.Same(withOverride, await service.GetAsync("tenant1", "GBP"));
        Assert.Equal(4, handler.Requests.Count);
    }

    /// <summary>
    /// The registration the library performs (<c>AddWildwoodComponents</c>) is an
    /// interface-to-implementation scoped registration, which means the container picks the
    /// constructor itself. It has to find one without a <see cref="TimeProvider"/> registered,
    /// because nothing registers one — the optional parameter is what makes that work, and a
    /// constructor change that lost it would only fail at the first page load.
    /// </summary>
    [Fact]
    public void The_container_can_build_the_service_with_no_clock_registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IAppTierComponentService>(_ => new AppTierComponentService(
            new FakeHttpMessageHandler().CreateClient("https://api.test/"),
            NullLogger<AppTierComponentService>.Instance));
        services.AddScoped<IPublicCatalogService, PublicCatalogService>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<PublicCatalogService>(provider.GetRequiredService<IPublicCatalogService>());
    }

    [Fact]
    public async Task GetAsync_refuses_an_empty_appId()
    {
        var (service, handler, _) = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(string.Empty));

        Assert.Empty(handler.Requests);
    }
}
