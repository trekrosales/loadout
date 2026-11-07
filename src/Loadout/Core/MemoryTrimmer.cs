using System.Runtime;
using System.Runtime.InteropServices;

namespace Loadout.Core;

/// <summary>
/// Hands unused memory back to Windows after the wheel or settings window closes (SPEC section 8).
/// </summary>
public static partial class MemoryTrimmer
{
    /// <summary>Runs a full compacting garbage collection, then trims the working set. Takes a few milliseconds.</summary>
    public static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }
        catch (Exception ex)
        {
            Log.Error("Memory trim failed.", ex);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(nint process, nint minimumWorkingSetSize, nint maximumWorkingSetSize);
}
