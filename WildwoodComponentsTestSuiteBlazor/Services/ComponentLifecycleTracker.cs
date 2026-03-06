namespace WildwoodComponentsTestSuiteBlazor.Services;

public class ComponentLifecycleTracker
{
    private readonly IDebugEventService _debugEvents;

    public ComponentLifecycleTracker(IDebugEventService debugEvents)
    {
        _debugEvents = debugEvents;
    }

    public void TrackEvent(string componentName, string eventType, string details = "")
    {
        _debugEvents.EmitLifecycleEvent(new LifecycleEvent
        {
            ComponentName = componentName,
            EventType = eventType,
            Details = details
        });
    }

    public void TrackInitialized(string componentName) =>
        TrackEvent(componentName, "Initialized");

    public void TrackParametersSet(string componentName) =>
        TrackEvent(componentName, "ParametersSet");

    public void TrackAfterRender(string componentName, bool firstRender) =>
        TrackEvent(componentName, firstRender ? "FirstRender" : "AfterRender");

    public void TrackDisposed(string componentName) =>
        TrackEvent(componentName, "Disposed");

    public void TrackError(string componentName, string error) =>
        TrackEvent(componentName, "Error", error);

    public void TrackCallback(string componentName, string callbackName, string details = "") =>
        TrackEvent(componentName, $"Callback:{callbackName}", details);
}
