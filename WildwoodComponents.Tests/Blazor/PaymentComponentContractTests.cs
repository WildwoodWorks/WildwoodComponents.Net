using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using WildwoodComponents.Blazor.Components.Payment;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Pins what <c>PaymentComponent</c> actually renders and raises, for the two behaviours a host's
/// money depends on: the success callback fires ONCE per payment (JS d7eae5a), and a plan with a
/// free trial offers the trial rather than a charge (JS f8b095f, 3298f70).
/// </summary>
/// <remarks>
/// There is no bUnit in this solution. <c>BuildRenderTree</c> on a bare instance reads only fields
/// and parameters, so the compiled markup can be inspected without a renderer, DI or a lifecycle —
/// the same technique <c>AIChatComponentInputBindingContractTests</c> uses.
/// </remarks>
public class PaymentComponentContractTests
{
    #region Parameters

    [Fact]
    public void TrialDays_isANullableIntParameter()
    {
        var property = typeof(PaymentComponent).GetProperty("TrialDays");

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<ParameterAttribute>());
        Assert.Equal(typeof(int?), property.PropertyType);
    }

    [Fact]
    public void OnContinue_isASeparateCallbackFromOnPaymentSuccess()
    {
        var onContinue = typeof(PaymentComponent).GetProperty("OnContinue");
        var onSuccess = typeof(PaymentComponent).GetProperty("OnPaymentSuccess");

        Assert.NotNull(onContinue);
        Assert.NotNull(onContinue!.GetCustomAttribute<ParameterAttribute>());
        Assert.Equal(typeof(EventCallback<PaymentSuccessEventArgs>), onContinue.PropertyType);
        Assert.Equal(typeof(EventCallback<PaymentSuccessEventArgs>), onSuccess!.PropertyType);
    }

    #endregion

    #region Single success

    /// <summary>
    /// The double-success bug: Continue rendered whenever a host wired <c>OnPaymentSuccess</c>, and
    /// clicking it fired that callback AGAIN — so a signup registered twice and an upgrade changed
    /// the tier twice.
    /// </summary>
    [Fact]
    public void SuccessPanel_offersNoContinue_whenOnlyOnPaymentSuccessIsWired()
    {
        var component = CompletedPayment();
        SetParameter(component, "OnPaymentSuccess", NoOpCallback());

        Assert.DoesNotContain("Continue", RenderedText(component));
    }

    [Fact]
    public void SuccessPanel_offersContinue_onlyWhenOnContinueIsWired()
    {
        var component = CompletedPayment();
        SetParameter(component, "OnContinue", NoOpCallback());

        Assert.Contains("Continue", RenderedText(component));
    }

    /// <summary>
    /// Continue invokes ONLY <c>OnContinue</c>, and hands it the whole result — the old handler
    /// re-raised <c>OnPaymentSuccess</c> with a payload that dropped the payment intent and
    /// subscription ids.
    /// </summary>
    [Fact]
    public async Task Continue_invokesOnContinueAlone_withTheFullResult()
    {
        var component = CompletedPayment();
        var successCount = 0;
        PaymentSuccessEventArgs? continued = null;

        // The receiver is a plain object, not the component: a component receiver would make the
        // callback re-enter ComponentBase and call StateHasChanged on an unattached instance.
        SetParameter(component, "OnPaymentSuccess", EventCallback.Factory.Create<PaymentSuccessEventArgs>(
            Host, _ => successCount++));
        SetParameter(component, "OnContinue", EventCallback.Factory.Create<PaymentSuccessEventArgs>(
            Host, args => continued = args));

        typeof(PaymentComponent)
            .GetMethod("HandleSuccessContinue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, Array.Empty<object>());

        // EventCallback.InvokeAsync on an unattached component still runs the delegate synchronously.
        await Task.Yield();

        Assert.Equal(0, successCount);
        Assert.NotNull(continued);
        Assert.Equal("txn_1", continued!.TransactionId);
        Assert.Equal("pi_1", continued.PaymentIntentId);
        Assert.Equal("sub_1", continued.SubscriptionId);
    }

    #endregion

    #region Trial copy

    [Fact]
    public void SuccessPanel_saysThePaymentSucceeded_whenNoCardWasSavedForATrial()
    {
        var text = RenderedText(CompletedPayment());

        Assert.Contains("Payment Successful!", text);
        Assert.DoesNotContain("free trial has started", text);
    }

    /// <summary>
    /// The live money bug the SetupIntent branch fixes: a trial used to report "Payment
    /// Successful!" with no card attached, so there was nothing to charge when it ended.
    /// </summary>
    [Fact]
    public void SuccessPanel_namesTheFirstChargeDate_whenACardWasSavedForATrial()
    {
        var component = CompletedPayment();
        var trialEnd = new DateTime(2026, 10, 2);
        SetField(component, "_trialEndsAt", trialEnd);

        var text = RenderedText(component);

        Assert.Contains("Your free trial has started!", text);
        Assert.Contains("Your card is saved.", text);
        Assert.Contains(trialEnd.ToString("MMM d, yyyy"), text);
        Assert.DoesNotContain("Payment Successful!", text);
    }

    [Fact]
    public void SubmitButton_offersTheTrial_insteadOfACharge()
    {
        var component = StripeForm();
        SetParameter(component, "TrialDays", 14);

        var text = RenderedText(component);

        Assert.Contains("Start 14-day free trial", text);
        Assert.Contains("You won't be charged today.", text);
        Assert.DoesNotContain("Pay $", text);
    }

    [Fact]
    public void SubmitButton_asksToPay_whenThereIsNoTrial()
    {
        var text = RenderedText(StripeForm());

        Assert.Contains("Pay $49.99", text);
        Assert.DoesNotContain("free trial", text);
    }

    /// <summary>
    /// A trial the account cannot have: nothing is charged on that click. The component says the
    /// amount is due today and the button becomes a plain Pay, which confirms the SAME intent.
    /// </summary>
    [Fact]
    public void TrialUnavailable_warnsAndSwitchesBackToPay_withoutCharging()
    {
        var component = StripeForm();
        SetParameter(component, "TrialDays", 14);
        SetField(component, "_trialUnavailable", true);

        var text = RenderedText(component);

        Assert.Contains("The free trial isn't available on your account", text);
        Assert.Contains("Pay $49.99", text);
        Assert.DoesNotContain("Start 14-day free trial", text);
    }

    /// <summary>
    /// That answer was about ONE plan (JS 1236503). A form reused for another plan offers its trial
    /// again, and drops the previous plan's intent so nothing confirms against it.
    /// </summary>
    [Fact]
    public void TrialUnavailable_resetsWhenTheFormIsReusedForAnotherPlan()
    {
        var component = StripeForm();
        SetParameter(component, "TrialDays", 14);
        SetParameter(component, "PricingModelId", "pm-1");
        OnParametersSet(component);

        SetField(component, "_trialUnavailable", true);
        SetField(component, "_pendingIntentKey", "prov|pm-1|49.99|sub");
        SetField(component, "_pendingIntent", new InitiatePaymentResponse { Success = true, ClientSecret = "cs" });

        SetParameter(component, "PricingModelId", "pm-2");
        OnParametersSet(component);

        Assert.False((bool)GetField(component, "_trialUnavailable")!);
        Assert.Null(GetField(component, "_pendingIntent"));
        Assert.Null(GetField(component, "_pendingIntentKey"));
    }

    [Fact]
    public void TrialUnavailable_survivesARenderForTheSamePlan()
    {
        var component = StripeForm();
        SetParameter(component, "TrialDays", 14);
        SetParameter(component, "PricingModelId", "pm-1");
        OnParametersSet(component);

        SetField(component, "_trialUnavailable", true);
        OnParametersSet(component);

        Assert.True((bool)GetField(component, "_trialUnavailable")!);
    }

    #endregion

    #region Helpers

    private static PaymentComponent CompletedPayment()
    {
        var component = new PaymentComponent();
        SetParameter(component, "Amount", 49.99m);
        SetParameter(component, "Currency", "USD");
        SetField(component, "_paymentComplete", true);
        SetField(component, "_paymentResult", new PaymentCompletionResult
        {
            Success = true,
            TransactionId = "txn_1",
            PaymentIntentId = "pi_1",
            SubscriptionId = "sub_1"
        });
        return component;
    }

    /// <summary>A form showing one Stripe provider, which is the branch with the submit button.</summary>
    private static PaymentComponent StripeForm()
    {
        var provider = new PaymentProviderDto
        {
            Id = "prov",
            Name = "Stripe",
            ProviderType = (int)PaymentProviderType.Stripe,
            IsEnabled = true
        };

        var component = new PaymentComponent();
        SetParameter(component, "Amount", 49.99m);
        SetParameter(component, "Currency", "USD");
        SetParameter(component, "ShowAmount", false);
        SetField(component, "_availableProviders", new List<PaymentProviderDto> { provider });
        SetField(component, "_selectedProvider", provider);
        return component;
    }

    /// <summary>Stands in for the host that wired the callbacks.</summary>
    private static readonly object Host = new object();

    private static EventCallback<PaymentSuccessEventArgs> NoOpCallback() =>
        EventCallback.Factory.Create<PaymentSuccessEventArgs>(Host, _ => { });

    private static void OnParametersSet(PaymentComponent component) =>
        typeof(PaymentComponent)
            .GetMethod("OnParametersSet", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, Array.Empty<object>());

    private static void SetParameter(PaymentComponent component, string name, object? value)
    {
        var property = FindProperty(typeof(PaymentComponent), name);
        property.SetValue(component, value);
    }

    private static void SetField(object component, string name, object? value) =>
        FindField(component.GetType(), name).SetValue(component, value);

    private static object? GetField(object component, string name) =>
        FindField(component.GetType(), name).GetValue(component);

    private static PropertyInfo FindProperty(Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var property = t.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property is not null) return property;
        }

        throw new Xunit.Sdk.XunitException($"No property '{name}' on {type.Name}.");
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var field = t.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }

        throw new Xunit.Sdk.XunitException($"No field '{name}' on {type.Name}.");
    }

    /// <summary>Every literal and bound string the component's markup would render, joined.</summary>
    private static string RenderedText(PaymentComponent component)
    {
        using var builder = new RenderTreeBuilder();
        typeof(PaymentComponent)
            .GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, new object[] { builder });

        var frames = builder.GetFrames();
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Text || frame.FrameType == RenderTreeFrameType.Markup)
            {
                // Joined with nothing: the Razor compiler splits "Pay @FormatAmount(...)" into two
                // text frames, and a separator between them would not match the rendered page.
                text.Append(frame.TextContent);
            }
        }

        return text.ToString();
    }

    #endregion
}
