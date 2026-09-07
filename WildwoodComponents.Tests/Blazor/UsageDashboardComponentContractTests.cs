using System.Reflection;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Usage;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Pins the UsageDashboardComponent parameter that carries the React component's
/// <c>subscription</c> override contract. The markup lives in a .razor file with no bUnit
/// renderer, so the test checks what a consuming app depends on: the name, the exact type,
/// that Blazor will bind it, and that it is nullable — a non-nullable declaration would make
/// "no override supplied" impossible to express.
/// </summary>
public class UsageDashboardComponentContractTests
{
    [Fact]
    public void SubscriptionOverride_is_a_nullable_subscription_parameter()
    {
        var property = typeof(UsageDashboardComponent).GetProperty("SubscriptionOverride");

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<ParameterAttribute>());
        Assert.Equal(typeof(UserTierSubscriptionModel), property.PropertyType);

        var nullability = new NullabilityInfoContext().Create(property);
        Assert.Equal(NullabilityState.Nullable, nullability.ReadState);
    }
}
