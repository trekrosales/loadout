using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Loadout.Core;

/// <summary>Helpers that keep Loadout's windows out of the taskbar and Alt + Tab (SPEC section 6).</summary>
public static partial class WindowStyles
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// Keeps <paramref name="window"/> out of the taskbar and Alt + Tab. Call before the window is shown.
    /// Works by adding the tool-window style once the native window exists. Use a custom title bar
    /// (WindowStyle="None") if the small tool-window caption is unwanted.
    /// </summary>
    public static void HideFromTaskSwitchers(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ShowInTaskbar = false;
        Apply(window, add: WS_EX_TOOLWINDOW, remove: WS_EX_APPWINDOW);
    }

    /// <summary>
    /// Like <see cref="HideFromTaskSwitchers"/>, and also stops the window from ever taking focus from the app
    /// in front. Used for the wheel overlay.
    /// </summary>
    public static void MakeOverlay(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        Apply(window, add: WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, remove: WS_EX_APPWINDOW);
    }

    private static void Apply(Window window, long add, long remove)
    {
        void Set(nint hwnd)
        {
            var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)((style | add) & ~remove));
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != 0)
        {
            Set(handle);
        }
        else
        {
            window.SourceInitialized += (_, _) => Set(new WindowInteropHelper(window).Handle);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
