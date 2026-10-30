using WildwoodComponents.Blazor.Components.AI;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Pins which session id <c>AIChatComponent</c> sends with a chat request. The API rebuilds the
/// conversation from that session, so a null id silently starts a fresh, empty session.
/// </summary>
public class AIChatComponentSessionIdTests
{
    [Fact]
    public void UsesTheLoadedSessionId_WhenTheSessionObjectIsLoaded()
    {
        Assert.Equal("loaded", AIChatComponent.ResolveRequestSessionId("loaded", "tracked"));
    }

    [Fact]
    public void FallsBackToTheTrackedId_WhenTheSessionObjectFailedToLoad()
    {
        Assert.Equal("tracked", AIChatComponent.ResolveRequestSessionId(null, "tracked"));
        Assert.Equal("tracked", AIChatComponent.ResolveRequestSessionId(string.Empty, "tracked"));
    }

    [Fact]
    public void SendsNull_WhenThereIsNoSessionAtAll()
    {
        Assert.Null(AIChatComponent.ResolveRequestSessionId(null, string.Empty));
        Assert.Null(AIChatComponent.ResolveRequestSessionId(null, null));
    }
}
