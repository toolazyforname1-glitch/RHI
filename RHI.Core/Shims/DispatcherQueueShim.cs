// Headless stand-in for Microsoft.UI.Dispatching.DispatcherQueue.
// RHI.Core never has a UI thread: HasThreadAccess is always true and TryEnqueue
// runs the callback inline. This keeps service code that marshals to "the UI
// thread" working unchanged, without pulling in the Windows App SDK.
namespace Microsoft.UI.Dispatching;

public sealed class DispatcherQueue
{
    public static DispatcherQueue Instance { get; } = new();

    public bool HasThreadAccess => true;

    public bool TryEnqueue(Action callback)
    {
        callback();
        return true;
    }

    public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
    {
        callback();
        return true;
    }
}

public enum DispatcherQueuePriority
{
    Low = -10,
    Normal = 0,
    High = 10,
}
