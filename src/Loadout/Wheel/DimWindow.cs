using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Loadout.Core;
using static Loadout.Wheel.WheelNative;

namespace Loadout.Wheel;

/// <summary>
/// Darkens one monitor behind the wheel (SPEC 4.4). A plain solid window whose see-through level is set by Windows
/// itself (one alpha value for the whole window), which is far cheaper than drawing a full-screen transparent image.
/// Click-through, never focused, never in the taskbar or Alt + Tab. Parked off-screen while unused.
/// </summary>
internal sealed class DimWindow : Window
{
    private const int ParkedPosition = -32000;

    private readonly SolidColorBrush _brush = new(Colors.Black);
    private nint _hwnd;
    private double _from;
    private double _to;
    private TimeSpan _start;
    private TimeSpan _duration;
    private bool _fading;
    private bool _parkWhenDone;

    public DimWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Title = "Loadout dim";
        Background = _brush;
        Left = ParkedPosition;
        Top = ParkedPosition;
        Width = Height = 1;

        WindowStyles.MakeOverlay(this);
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            AddExStyle(_hwnd, WS_EX_LAYERED | WS_EX_TRANSPARENT);
            SetLayeredWindowAttributes(_hwnd, 0, 0, LWA_ALPHA);
        };
    }

    public double CurrentAlpha { get; private set; }

    public void Prepare()
    {
        if (_hwnd == 0)
        {
            Show();
        }
    }

    /// <summary>Covers the monitor (physical pixels) and fades to <paramref name="opacity"/>.</summary>
    public void Cover(MonitorArea monitor, Color color, double opacity, bool animate)
    {
        Prepare();
        _brush.Color = color;
        var b = monitor.Bounds;
        SetAlpha(animate ? 0 : opacity);
        SetWindowPos(_hwnd, HWND_TOPMOST, b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetWindowPos(_hwnd, HWND_TOPMOST, b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
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

    private void Park()
    {
        if (_hwnd != 0)
        {
            SetWindowPos(_hwnd, 0, ParkedPosition, ParkedPosition, 1, 1, SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOZORDER);
        }
    }
}
