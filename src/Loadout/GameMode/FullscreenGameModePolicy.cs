using System.Runtime.InteropServices;
using Loadout.Contracts;

namespace Loadout.GameMode;

/// <summary>
/// Game mode (SPEC section 7): decides whether the wheel may open over the app in front.
/// <list type="bullet">
/// <item>Exclusive-fullscreen apps (Windows reports a full-screen Direct3D app): never open; nothing can draw over them.</item>
/// <item>Other fullscreen apps (borderless games, fullscreen video): open only when game mode is on.</item>
/// <item>Everything else (the desktop, normal windows): always open.</item>
/// </list>
/// Only a handful of fast Windows calls, well under 1 ms.
/// </summary>
public sealed partial class FullscreenGameModePolicy : IGameModePolicy
{
    private readonly ISettingsStore _settings;
    private bool _lastWasFullscreen;

    public FullscreenGameModePolicy(ISettingsStore settings)
    {
        _settings = settings;
    }

    public bool CanOpenNow()
    {
        var state = QueryNotificationState();
        _lastWasFullscreen = state == UserNotificationState.RunningD3DFullScreen || IsForegroundFullscreen();
        return Decide(state == UserNotificationState.RunningD3DFullScreen, _lastWasFullscreen, _settings.Current.GameMode.Enabled);
    }

    public bool ShouldBlockMouse() => !_lastWasFullscreen || _settings.Current.GameMode.BlockMouseInGame;

    /// <summary>The pure decision, kept separate for tests.</summary>
    internal static bool Decide(bool exclusiveFullscreen, bool fullscreen, bool gameModeEnabled) =>
        !exclusiveFullscreen && (!fullscreen || gameModeEnabled);

    /// <summary>
    /// True when the window in front is a borderless window covering the whole monitor the cursor is on
    /// (and isn't the desktop or taskbar). Normal maximized windows don't count, even when they cover the monitor
    /// because the taskbar is hidden or on another screen; and a fullscreen app on another monitor doesn't block
    /// the wheel on this one.
    /// </summary>
    private static bool IsForegroundFullscreen()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || IsShellWindow(hwnd) || !GetWindowRect(hwnd, out var window))
        {
            return false;
        }

        if (!LooksLikeFullscreenApp((long)GetWindowLongPtr(hwnd, GWL_STYLE), IsZoomed(hwnd)))
        {
            return false;
        }

        GetCursorPos(out var cursor);
        var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        return CoversMonitor(window, info.rcMonitor);
    }

    /// <summary>Fullscreen games and videos are borderless; a window with a title bar or resize frame, or a maximized one, isn't.</summary>
    internal static bool LooksLikeFullscreenApp(long style, bool maximized) =>
        !maximized && (style & WS_CAPTION) != WS_CAPTION && (style & WS_THICKFRAME) == 0;

    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;

    internal static bool CoversMonitor(RECT window, RECT monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    private static bool IsShellWindow(nint hwnd)
    {
        if (hwnd == GetShellWindow() || hwnd == GetDesktopWindow())
        {
            return true;
        }

        Span<char> buffer = stackalloc char[32];
        int length;
        unsafe
        {
            fixed (char* p = buffer)
            {
                length = GetClassName(hwnd, p, buffer.Length);
            }
        }

        ReadOnlySpan<char> name = buffer[..Math.Max(0, length)];
        return name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static UserNotificationState QueryNotificationState() =>
        SHQueryUserNotificationState(out var state) == 0 ? state : UserNotificationState.AcceptsNotifications;

    internal enum UserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out UserNotificationState state);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetDesktopWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint hwnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
}
