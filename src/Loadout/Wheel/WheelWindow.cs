using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Loadout.Core;
using static Loadout.Wheel.WheelNative;

namespace Loadout.Wheel;

/// <summary>
/// The small see-through window that holds the wheel. Created once and kept "parked" off-screen while unused,
/// so opening is just a move and a fade (no window creation). It never takes focus, never takes clicks,
/// and never appears in the taskbar or Alt + Tab.
/// </summary>
internal sealed class WheelWindow : Window
{
    private const int ParkedPosition = -32000;

    private readonly ScaleTransform _scale = new(1, 1);
    private nint _hwnd;
    private int _hideVersion;

    public WheelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Title = "Loadout wheel";
        Left = ParkedPosition;
        Top = ParkedPosition;

        Visual = new WheelVisual { RenderTransform = _scale, Opacity = 0 };
        Content = Visual;

        WindowStyles.MakeOverlay(this);
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            AddExStyle(_hwnd, WS_EX_TRANSPARENT); // clicks pass through
        };
    }

    public WheelVisual Visual { get; }

    /// <summary>Creates the native window off-screen so the first open is fast.</summary>
    public void Prepare()
    {
        if (_hwnd != 0)
        {
            return;
        }

        Width = Height = 1;
        Show();
    }

    /// <summary>Moves the wheel to (left, top) in physical pixels, sized <paramref name="sizePx"/>, and fades it in.</summary>
    public void ShowAt(int left, int top, int sizePx, bool animate)
    {
        Prepare();
        _hideVersion++;

        var size = Visual.Size;
        Width = Height = size;
        _scale.CenterX = _scale.CenterY = size / 2;

        // Twice: the first move may cross into a monitor with a different scale, which resizes the window.
        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, sizePx, sizePx, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, sizePx, sizePx, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        if (animate)
        {
            var duration = TimeSpan.FromMilliseconds(120);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Visual.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            var grow = new DoubleAnimation(0.92, 1, duration) { EasingFunction = ease };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        else
        {
            StopAnimations();
            Visual.Opacity = 1;
            _scale.ScaleX = _scale.ScaleY = 1;
        }
    }

    /// <summary>Fades out (if animating), then parks the window off-screen again.</summary>
    public void HideWheel(bool animate)
    {
        var version = ++_hideVersion;
        if (!animate)
        {
            StopAnimations();
            Visual.Opacity = 0;
            Park();
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(80));
        fade.Completed += (_, _) =>
        {
            if (version == _hideVersion)
            {
                Park();
            }
        };
        Visual.BeginAnimation(OpacityProperty, fade);
    }

    private void StopAnimations()
    {
        Visual.BeginAnimation(OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private void Park()
    {
        if (_hwnd != 0)
        {
            SetWindowPos(_hwnd, 0, ParkedPosition, ParkedPosition, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOOWNERZORDER | SWP_NOZORDER);
        }
    }
}
