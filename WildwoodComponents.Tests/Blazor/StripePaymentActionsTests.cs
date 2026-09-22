using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Who cleans up the Stripe instance a pack's 3-D Secure creates.
/// </summary>
/// <remarks>
/// <para>
/// The script keeps its instances in a module-level map for the life of the page, so anything that
/// creates one under a key owns clearing it away. <c>StripePaymentActions</c> holds the key, so it
/// is the owner: a saved-card confirmation creates <c>Stripe(publishableKey)</c> under that key
/// with no element to unmount and nothing else that would ever remove it.
/// </para>
/// <para>
/// A signup can produce a new key per attempt — every "Start Over" mounts a fresh
/// <c>PackCheckout</c> with a fresh <c>ComponentId</c> — so a missing teardown is a leak that
/// grows, not a one-off.
/// </para>
/// </remarks>
public class StripePaymentActionsTests
{
    private const string DisposeStripe = "wildwoodPayment.disposeStripe";
    private const string ConfirmPayment = "wildwoodPayment.confirmStripeCardPayment";

    #region Reflection helpers

    /// <summary>A private field on the component itself.</summary>
    private static void SetField(object target, string name, object? value)
    {
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
    }

    /// <summary>A protected property, which may be declared anywhere up the hierarchy.</summary>
    private static void SetProperty(object target, string name, object? value)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (property is null) continue;

            property.SetValue(target, value);
            return;
        }

        throw new InvalidOperationException($"No property '{name}' on {target.GetType().Name}.");
    }

    #endregion

    #region The adapter

    [Fact]
    public async Task DisposeAsync_DropsTheInstanceUnderItsOwnKey()
    {
        var js = new RecordingJsRuntime();
        var actions = new StripePaymentActions(js, "ww-pack-checkout-abc123");

        await actions.DisposeAsync();

        var call = Assert.Single(js.CallsTo(DisposeStripe));
        Assert.Equal(new object?[] { "ww-pack-checkout-abc123" }, call.Args);
    }

    /// <summary>
    /// Disposal runs from the component's own teardown AND from the driver's, and Blazor is free
    /// to call either twice on the way out of a circuit.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_DisposesOnce_HoweverManyTimesItIsCalled()
    {
        var js = new RecordingJsRuntime();
        var actions = new StripePaymentActions(js, "ww-pack-checkout-abc123");

        await actions.DisposeAsync();
        await actions.DisposeAsync();
        await actions.DisposeAsync();

        Assert.Single(js.CallsTo(DisposeStripe));
    }

    /// <summary>
    /// The usual way a page ends is the browser leaving, which takes the circuit with it: the
    /// interop call disposal makes then throws, and a throw on the way out is noise, not news.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_OnADisconnectedCircuit_DoesNotThrow()
    {
        var js = new RecordingJsRuntime { Throw = new JSDisconnectedException("circuit gone") };
        var actions = new StripePaymentActions(js, "ww-pack-checkout-abc123");

        var exception = await Record.ExceptionAsync(async () => await actions.DisposeAsync());

        Assert.Null(exception);
        Assert.Single(js.CallsTo(DisposeStripe));
    }

    [Fact]
    public async Task DisposeAsync_WhenTheJsRuntimeHasGoneFirst_DoesNotThrow()
    {
        var js = new RecordingJsRuntime { Throw = new ObjectDisposedException("JSRuntime") };
        var actions = new StripePaymentActions(js, "ww-pack-checkout-abc123");

        var exception = await Record.ExceptionAsync(async () => await actions.DisposeAsync());

        Assert.Null(exception);
    }

    /// <summary>
    /// A challenge that arrives after disposal has no instance left to confirm against, so it
    /// must not re-create one behind the teardown's back.
    /// </summary>
    [Fact]
    public async Task ConfirmPaymentAsync_AfterDisposal_NeverReachesTheScript()
    {
        var js = new RecordingJsRuntime();
        var actions = new StripePaymentActions(js, "ww-pack-checkout-abc123");

        await actions.DisposeAsync();
        var outcome = await actions.ConfirmPaymentAsync("pi_radar", "pk_test_1");

        Assert.False(outcome.Succeeded);
        Assert.Empty(js.CallsTo(ConfirmPayment));
    }

    #endregion

    #region The owners

    private static PackCheckoutDriver BuildDriver(IPaymentActionAdapter actions)
    {
        var appTier = new AppTierComponentService(
            new ScriptedHttpMessageHandler().CreateClient(),
            NullLogger<AppTierComponentService>.Instance);

        return new PackCheckoutDriver(
            appTier,
            new FakePaymentProviderService(),
            actions,
            new PackCheckoutSettings { AppId = "app-1" });
    }

    [Fact]
    public async Task DisposingTheDriver_DisposesTheAdapterItWasGiven()
    {
        var js = new RecordingJsRuntime();
        var driver = BuildDriver(new StripePaymentActions(js, "ww-pack-checkout-abc123"));

        await driver.DisposeAsync();

        var call = Assert.Single(js.CallsTo(DisposeStripe));
        Assert.Equal(new object?[] { "ww-pack-checkout-abc123" }, call.Args);
    }

    /// <summary>
    /// A quote that answers after the checkout has left the page must not re-render it: the
    /// delegates the view wired up are dropped with the driver.
    /// </summary>
    [Fact]
    public async Task DisposingTheDriver_StopsItReportingToTheViewItHasLeft()
    {
        var js = new RecordingJsRuntime();
        var driver = BuildDriver(new StripePaymentActions(js, "ww-pack-checkout-abc123"));
        var renders = 0;
        driver.StateChanged = () => renders++;

        await driver.DisposeAsync();
        await driver.StartAsync();
        await driver.RetryAsync();

        Assert.Equal(0, renders);
        Assert.Equal(PackCheckoutStep.Idle, driver.Step);
    }

    /// <summary>
    /// The component's own teardown: its driver's key is the key the script was told to drop.
    /// </summary>
    [Fact]
    public async Task DisposingTheComponent_DropsTheInstanceItsChallengesRunOn()
    {
        var js = new RecordingJsRuntime();
        var component = new PackCheckout();
        SetField(component, "_driver", BuildDriver(new StripePaymentActions(js, component.PaymentInstanceKey)));

        await ((IAsyncDisposable)component).DisposeAsync();

        var call = Assert.Single(js.CallsTo(DisposeStripe));
        Assert.Equal(new object?[] { component.PaymentInstanceKey }, call.Args);
        Assert.StartsWith("ww-pack-checkout-", component.PaymentInstanceKey);
    }

    /// <summary>
    /// The base-class hazard: Blazor calls <c>DisposeAsync</c> INSTEAD of <c>Dispose</c> on a
    /// component that implements <see cref="IAsyncDisposable"/>, so a component that stops calling
    /// the base's synchronous clean-up stays subscribed to the theme service — which then holds it
    /// for the rest of the circuit. Both components that dispose a Stripe key are pinned here.
    /// </summary>
    [Theory]
    [InlineData(typeof(PackCheckout))]
    [InlineData(typeof(CardSetupForm))]
    public async Task DisposingAsynchronously_StillRunsTheBaseClassCleanUp(Type componentType)
    {
        var theme = new RecordingThemeService();
        var component = (IAsyncDisposable)Activator.CreateInstance(componentType)!;
        SetProperty(component, "ThemeService", theme);
        SetProperty(component, "JSRuntime", new RecordingJsRuntime());

        await component.DisposeAsync();

        Assert.Equal(1, theme.Unsubscribes);
    }

    /// <summary>Counts what the base class does to it, which is unsubscribe exactly once.</summary>
    private sealed class RecordingThemeService : IComponentThemeService
    {
        private EventHandler<ComponentTheme>? _handlers;

        public int Unsubscribes { get; private set; }

        public event EventHandler<ComponentTheme>? ThemeChanged
        {
            add { _handlers += value; }
            remove
            {
                Unsubscribes++;
                _handlers -= value;
            }
        }

        public Task<ComponentTheme> GetCurrentThemeAsync() => Task.FromResult(new ComponentTheme());

        public Task SetThemeAsync(ComponentTheme theme) => Task.CompletedTask;

        public Task ResetThemeAsync() => Task.CompletedTask;
    }

    #endregion
}
