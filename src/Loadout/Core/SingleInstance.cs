using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Loadout.Core;

/// <summary>
/// Makes sure only one copy of Loadout runs per user session.
/// Starting a second copy signals the first one (which opens settings) and exits.
/// Waiting uses a registered OS wait, not polling, so it costs nothing while idle.
/// A copy started with LOADOUT_DATA_DIR set (tests, agents) uses its own lock, so it can run beside the real one.
/// </summary>
public sealed partial class SingleInstance : IDisposable
{
    private const string BaseName = "Loadout.6b1f0c2e";
    private const int ASFW_ANY = -1;

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
        var suffix = InstanceSuffix();
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{BaseName}.Instance{suffix}", out var createdNew);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{BaseName}.ShowSettings{suffix}");
        if (createdNew)
        {
            return new SingleInstance(mutex, signal);
        }

        // The user just started this copy, so it may hand focus to the running copy's settings window.
        AllowSetForegroundWindow(ASFW_ANY);
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

    private static string InstanceSuffix()
    {
        var overrideDir = Environment.GetEnvironmentVariable(AppPaths.DataDirOverrideVariable);
        if (string.IsNullOrWhiteSpace(overrideDir))
        {
            return string.Empty;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(overrideDir).ToUpperInvariant()));
        return "." + Convert.ToHexString(hash, 0, 8);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
