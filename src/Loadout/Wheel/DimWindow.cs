using System.Runtime.InteropServices;
using System.Windows.Media;
using Loadout.Core;
using static Loadout.Wheel.WheelNative;

namespace Loadout.Wheel;

/// <summary>
/// Darkens one monitor behind the wheel (SPEC 4.4).
/// <para>
/// This is a plain Windows window (no WPF at all) painted with one solid color, whose see-through level is
/// applied by Windows itself (one alpha value for the whole window, <c>SetLayeredWindowAttributes</c>).
/// That's far cheaper than a full-screen transparent WPF window and costs nothing while parked.
/// WPF's own window wrapper can't be used here because it refuses the layered style it needs.
/// Click-through, never focused, never in the taskbar or Alt + Tab. Parked off-screen while unused.
/// </para>
/// </summary>
internal sealed partial class DimWindow : IDisposable
{
    private const string ClassName = "Loadout.Dim";
    private const int ParkedPosition = -32000;
    private const uint WS_POPUP = 0x80000000;
    private const long WS_EX_TOPMOST = 0x00000008;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_PAINT = 0x000F;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int WM_CLOSE = 0x0010;

    // Kept in a static so the delegate handed to Windows is never garbage-collected.
    private static readonly WndProcDelegate s_wndProc = StaticWndProc;
    private static readonly Dictionary<nint, DimWindow> s_windows = [];
    private static bool s_classRegistered;

    private readonly nint _hwnd;
    private nint _brush;
    private uint _colorRef = uint.MaxValue;
    private double _from;
    private double _to;
    private TimeSpan _start;
    private TimeSpan _duration;
    private bool _fading;
    private bool _parkWhenDone;
    private bool _disposed;

    public DimWindow()
    {
        RegisterClassOnce();
        _hwnd = CreateWindowEx(
            (uint)(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST),
            ClassName,
            "Loadout dim",
            WS_POPUP,
            ParkedPosition,
            ParkedPosition,
            1,
            1,
            0,
            0,
            GetModuleHandle(null),
            0);
        if (_hwnd == 0)
        {
            Log.Error($"Could not create the dim window (error {Marshal.GetLastPInvokeError()}).");
            return;
        }

        s_windows[_hwnd] = this;
        SetColor(Colors.Black);
        SetLayeredWindowAttributes(_hwnd, 0, 0, LWA_ALPHA);
    }

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    public double CurrentAlpha { get; private set; }

    /// <summary>Covers the monitor (physical pixels) and fades to <paramref name="opacity"/>.</summary>
    public void Cover(MonitorArea monitor, Color color, double opacity, bool animate)
    {
        if (_hwnd == 0)
        {
            return;
        }

        StopFade(); // a fade-out still running must not undo this
        SetColor(color);
        var b = monitor.Bounds;
        SetAlpha(animate ? 0 : opacity);
        SetWindowPos(_hwnd, HWND_TOPMOST, b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(_hwnd, 0, true);
        UpdateWindow(_hwnd); // paint the new color before it becomes visible
        if (animate)
        {
            FadeTo(opacity, 120, park: false);
        }
    }

    public void Uncover(bool animate)
    {
        if (_hwnd == 0)
        {
            return;
        }

        if (animate && CurrentAlpha > 0)
        {
            FadeTo(0, 80, park: true);
        }
        else
        {
            StopFade();
            SetAlpha(0);
            Park();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopFade();
        if (_hwnd != 0)
        {
            s_windows.Remove(_hwnd);
            DestroyWindow(_hwnd);
        }

        if (_brush != 0)
        {
            DeleteObject(_brush);
            _brush = 0;
        }
    }

    private static void RegisterClassOnce()
    {
        if (s_classRegistered)
        {
            return;
        }

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
        };
        s_classRegistered = RegisterClassEx(ref wc) != 0;
        if (!s_classRegistered)
        {
            Log.Error($"Could not register the dim window class (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    private static nint StaticWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (s_windows.TryGetValue(hwnd, out var window))
        {
            switch (msg)
            {
                case WM_ERASEBKGND:
                    GetClientRect(hwnd, out var rect);
                    FillRect(wParam, ref rect, window._brush);
                    return 1;
                case WM_PAINT:
                    var hdc = BeginPaint(hwnd, out var paint);
                    GetClientRect(hwnd, out var client);
                    FillRect(hdc, ref client, window._brush);
                    EndPaint(hwnd, ref paint);
                    return 0;
                case WM_NCHITTEST:
                    return HTTRANSPARENT;
                case WM_CLOSE:
                    return 0; // only Loadout itself may destroy this window (Dispose)
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void SetColor(Color color)
    {
        var colorRef = (uint)(color.R | (color.G << 8) | (color.B << 16));
        if (colorRef == _colorRef)
        {
            return;
        }

        if (_brush != 0)
        {
            DeleteObject(_brush);
        }

        _brush = CreateSolidBrush(colorRef);
        _colorRef = colorRef;
    }

    private void FadeTo(double target, int milliseconds, bool park)
    {
        _from = CurrentAlpha;
        _to = target;
        _duration = TimeSpan.FromMilliseconds(milliseconds);
        _start = TimeSpan.MinValue;
        _parkWhenDone = park;
        if (!_fading)
        {
            _fading = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>Runs once per frame only while fading.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_start == TimeSpan.MinValue)
        {
            _start = now;
        }

        var t = _duration <= TimeSpan.Zero ? 1 : Math.Clamp((now - _start) / _duration, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        SetAlpha(_from + ((_to - _from) * eased));
        if (t >= 1)
        {
            StopFade();
            if (_parkWhenDone)
            {
                Park();
            }
        }
    }

    private void StopFade()
    {
        if (_fading)
        {
            _fading = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void SetAlpha(double alpha)
    {
        CurrentAlpha = Math.Clamp(alpha, 0, 1);
        if (_hwnd != 0)
        {
            SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Round(CurrentAlpha * 255), LWA_ALPHA);
        }
    }

    private void Park() =>
        SetWindowPos(_hwnd, 0, ParkedPosition, ParkedPosition, 1, 1, SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOZORDER);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

#pragma warning disable SYSLIB1054 // These use structs with strings or arrays, which need the classic marshaller.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hwnd, out PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint hwnd, ref PAINTSTRUCT paint);
#pragma warning restore SYSLIB1054

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowEx(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? name);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint hdc, ref RECT rect, nint brush);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint hwnd);
}
