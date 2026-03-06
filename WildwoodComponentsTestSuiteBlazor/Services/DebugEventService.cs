using System.Collections.Concurrent;

namespace WildwoodComponentsTestSuiteBlazor.Services;

public class DebugEventService : IDebugEventService
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<DebugEntry> _entries = new();
    private int _count;

    public event EventHandler<DebugEntry>? OnNewEntry;

    public void EmitHttpTraffic(HttpTrafficEntry entry)
    {
        AddEntry(entry);
    }

    public void EmitLifecycleEvent(LifecycleEvent evt)
    {
        AddEntry(evt);
    }

    public void EmitSignalRMessage(SignalRMessage msg)
    {
        AddEntry(msg);
    }

    public IReadOnlyList<DebugEntry> GetAllEntries()
    {
        return _entries.ToArray();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _count, 0);
    }

    private void AddEntry(DebugEntry entry)
    {
        _entries.Enqueue(entry);
        var currentCount = Interlocked.Increment(ref _count);

        // FIFO eviction
        while (currentCount > MaxEntries && _entries.TryDequeue(out _))
        {
            currentCount = Interlocked.Decrement(ref _count);
        }

        OnNewEntry?.Invoke(this, entry);
    }
}
