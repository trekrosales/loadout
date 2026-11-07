using static Loadout.Wheel.WheelNative;

namespace Loadout.Wheel;

/// <summary>A monitor's full and work areas in physical pixels, and its display scale (1.0 = 100%).</summary>
internal readonly record struct MonitorArea(RECT Bounds, RECT Work, double Scale)
{
    /// <summary>The monitor containing (or nearest to) a point in physical pixels.</summary>
    public static MonitorArea FromPoint(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        return FromHandle(monitor);
    }

    /// <summary>All monitors.</summary>
    public static List<MonitorArea> All()
    {
        var result = new List<MonitorArea>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            result.Add(FromHandle(monitor));
            return true;
        }, 0);
        return result;
    }

    private static MonitorArea FromHandle(nint monitor)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96.0
            : 1.0;
        return new MonitorArea(info.rcMonitor, info.rcWork, scale);
    }
}
