namespace Loadout.Core;

/// <summary>
/// Makes sure only one copy of Loadout runs per user session.
/// Starting a second copy signals the first one (which opens settings) and exits.
/// Waiting uses a registered OS wait, not polling, so it costs nothing while idle.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Loadout.SingleInstance.6b1f0c2e";
    private const string SignalName = @"Local\Loadout.ShowSettings.6b1f0c2e";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle signal)
    {
        _mutex = mutex;
        _signal = signal;
    }

    /// <summary>
    /// Returns an instance if this is the first copy; otherwise signals the running copy and returns null.
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        if (createdNew)
        {
            return new SingleInstance(mutex, signal);
        }

        signal.Set();
        signal.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onSignal"/> (on a thread-pool thread) whenever another copy starts.</summary>
    public void ListenForOtherInstances(Action onSignal)
    {
        _registration?.Unregister(null);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _signal, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _signal.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread; it is released when the process exits.
        }

        _mutex.Dispose();
    }
}
