namespace WildwoodComponentsTestSuiteBlazor.Services;

public interface IDebugEventService
{
    event EventHandler<DebugEntry>? OnNewEntry;

    void EmitHttpTraffic(HttpTrafficEntry entry);
    void EmitLifecycleEvent(LifecycleEvent evt);
    void EmitSignalRMessage(SignalRMessage msg);

    IReadOnlyList<DebugEntry> GetAllEntries();
    void Clear();
}

public abstract class DebugEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public abstract string EntryType { get; }
    public abstract string Summary { get; }
}

public class HttpTrafficEntry : DebugEntry
{
    public override string EntryType => "HTTP";
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public string? RequestHeaders { get; set; }
    public string? RequestBody { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseHeaders { get; set; }
    public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }
    public override string Summary => $"{Method} {Url} -> {StatusCode} ({DurationMs}ms)";
}

public class LifecycleEvent : DebugEntry
{
    public override string EntryType => "Lifecycle";
    public string ComponentName { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Details { get; set; } = "";
    public override string Summary => $"[{ComponentName}] {EventType}: {Details}";
}

public class SignalRMessage : DebugEntry
{
    public override string EntryType => "SignalR";
    public string HubName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Direction { get; set; } = "Received"; // Sent or Received
    public override string Summary => $"{Direction}: {HubName}.{MethodName}";
}
