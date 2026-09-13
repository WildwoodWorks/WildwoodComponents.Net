using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// AttributionService is a thin JS-isolation shim: it must pass the right arguments to the engine module and
/// degrade to null or a no-op whenever interop is unavailable, so attribution can never break a page or a signup.
/// </summary>
public class AttributionServiceTests
{
    private static AttributionService Create(FakeJsRuntime js, string baseUrl = "https://api.example.com/")
        => new(js, NullLogger<AttributionService>.Instance, baseUrl);

    [Fact]
    public async Task InitializeAsync_WithoutAnAppId_DoesNotLoadTheEngine()
    {
        var js = new FakeJsRuntime();

        var state = await Create(js).InitializeAsync("  ");

        Assert.Null(state);
        Assert.Empty(js.Imports);
    }

    [Fact]
    public async Task InitializeAsync_PassesTheTrimmedBaseUrlAndAppIdToTheEngine()
    {
        var js = new FakeJsRuntime();
        js.Module.Result = id => id == "initialize" ? new AttributionStateModel { VisitorKey = "visitor-key-0001" } : null;

        var state = await Create(js).InitializeAsync("app-1");

        Assert.Equal("visitor-key-0001", state?.VisitorKey);
        Assert.Equal("./_content/WildwoodComponents.Blazor/js/wildwood-attribution.js", Assert.Single(js.Imports));
        var call = Assert.Single(js.Module.Calls);
        Assert.Equal("initialize", call.Identifier);
        Assert.Equal(new object?[] { "https://api.example.com", "app-1" }, call.Args);
    }

    [Fact]
    public async Task InitializeAsync_PrefersTheBaseUrlOverride()
    {
        var js = new FakeJsRuntime();

        await Create(js).InitializeAsync("app-1", "https://other.example.com/");

        Assert.Equal("https://other.example.com", Assert.Single(js.Module.Calls).Args?[0]);
    }

    [Fact]
    public async Task GetForRegistrationAsync_WhenInteropIsUnavailable_ReturnsNull()
    {
        // Prerendering: JS interop calls throw until the page is interactive.
        var js = new FakeJsRuntime { ImportThrow = new InvalidOperationException("JavaScript interop calls cannot be issued at this time.") };

        var payload = await Create(js).GetForRegistrationAsync();

        Assert.Null(payload);
    }

    [Fact]
    public async Task ClearAsync_WhenTheEngineCallFails_DoesNotThrow()
    {
        var js = new FakeJsRuntime();
        js.Module.Throw = new JSException("engine failure");

        var exception = await Record.ExceptionAsync(() => Create(js).ClearAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetForRegistrationAsync_ReturnsTheEnginePayload()
    {
        var js = new FakeJsRuntime();
        js.Module.Result = id => id == "getForRegistration"
            ? new AttributionPayloadModel { VisitorKey = "visitor-key-0001", LastTouch = new AttributionTouchModel { Source = "reddit" } }
            : null;

        var payload = await Create(js).GetForRegistrationAsync();

        Assert.Equal("reddit", payload?.LastTouch?.Source);
        Assert.Equal("dotnet", payload?.Sdk);
    }

    private sealed class FakeModule : IJSObjectReference
    {
        public List<(string Identifier, object?[]? Args)> Calls { get; } = new();
        public Func<string, object?> Result { get; set; } = _ => null;
        public Exception? Throw { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (Throw is not null) throw Throw;
            Calls.Add((identifier, args));
            return new ValueTask<TValue>((TValue)Result(identifier)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        public FakeModule Module { get; } = new();
        public Exception? ImportThrow { get; set; }
        public List<string> Imports { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (ImportThrow is not null) throw ImportThrow;
            Imports.Add(args?[0] as string ?? string.Empty);
            return new ValueTask<TValue>((TValue)(object)Module);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }
}