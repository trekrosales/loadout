using System.Windows.Media;
using Loadout.Contracts;
using Loadout.Core;
using Loadout.Settings.Model;
using Loadout.Wheel.Geometry;

namespace Loadout.Wheel;

/// <summary>
/// The real <see cref="IWheelPresenter"/> (SPEC sections 2 to 4): positions the wheel at the cursor,
/// darkens the screen, turns mouse movement into a highlighted slot, switches groups, and remembers
/// the last group and last launched slot.
/// </summary>
public sealed class WheelPresenter : IWheelPresenter
{
    private readonly ISettingsStore _settings;
    private readonly IIconProvider _icons;
    private readonly List<DimWindow> _dims = [];
    private WheelWindow? _window;

    private int _groupIndex;
    private int? _highlight;
    private double _aimX;
    private double _aimY;
    private double _lastTotalX;
    private double _lastTotalY;
    private bool _animate;
    private bool _prewarmScheduled;

    public WheelPresenter(ISettingsStore settings, IIconProvider icons)
    {
        _settings = settings;
        _icons = icons;
        _settings.Changed += (_, _) => SchedulePrewarm();
    }

    public bool IsOpen { get; private set; }

    /// <summary>Creates the windows ahead of time (off-screen) and pre-draws the wheel so the first open is instant.</summary>
    public void Prepare()
    {
        EnsureWindows();
        SchedulePrewarm();
    }

    public void Open(ScreenPoint cursor)
    {
        if (IsOpen)
        {
            Close(launch: false);
        }

        EnsureWindows();
        var settings = _settings.Current;
        var appearance = settings.Appearance;
        _animate = appearance.Animations && System.Windows.SystemParameters.ClientAreaAnimation;

        // Marked open first, so if anything below fails, Close can still take down whatever was shown.
        IsOpen = true;
        try
        {
            var visual = _window!.Visual;
            visual.Configure(appearance);

            _groupIndex = WheelMath.WrapIndex(settings.LastGroupIndex, settings.Groups.Count);
            _highlight = null;
            _aimX = _aimY = _lastTotalX = _lastTotalY = 0;
            ShowCurrentGroup(crossFade: false);
            visual.SetHighlight(null, null);

            // Place the wheel centered on the cursor, nudged to stay inside the monitor's work area.
            var monitor = MonitorArea.FromPoint(cursor.X, cursor.Y);
            var sizePx = (int)Math.Ceiling(visual.Size * monitor.Scale);
            var half = sizePx / 2.0;
            var w = monitor.Work;
            var (cx, cy) = WheelMath.ClampCenter(cursor.X, cursor.Y, half, w.Left, w.Top, w.Right, w.Bottom);

            if (appearance.DimOpacity > 0.001)
            {
                var color = WheelVisual.ParseColor(appearance.DimColor, Colors.Black);
                var monitors = appearance.DimAllMonitors ? MonitorArea.All() : [monitor];
                while (_dims.Count < monitors.Count)
                {
                    _dims.Add(new DimWindow());
                }

                for (var i = 0; i < monitors.Count; i++)
                {
                    _dims[i].Cover(monitors[i], color, appearance.DimOpacity, _animate);
                }
            }

            // Shown after the dim windows so it sits on top of them.
            _window.ShowAt((int)Math.Round(cx - half), (int)Math.Round(cy - half), sizePx, _animate);
        }
        catch (Exception ex)
        {
            Log.Error("Opening the wheel failed.", ex);
            _animate = false;
            Close(launch: false);
        }
    }

    private void EnsureWindows()
    {
        if (_window is null)
        {
            _window = new WheelWindow();

            // Re-prepare only once the wheel has fully faded out, so the center text never changes mid-fade.
            _window.Parked += (_, _) => SchedulePrewarm();
        }

        _window.Prepare();
        if (_dims.Count == 0)
        {
            _dims.Add(new DimWindow());
        }
    }

    /// <summary>
    /// When the app is idle, draws the wheel off-screen with the group it will open on: icons loaded, text laid out,
    /// code warmed up. Opening is then just a move and a fade. Runs at startup and after settings change.
    /// </summary>
    private void SchedulePrewarm()
    {
        if (_prewarmScheduled)
        {
            return;
        }

        _prewarmScheduled = true;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () =>
            {
                _prewarmScheduled = false;
                Prewarm();
            });
    }

    private void Prewarm()
    {
        if (IsOpen || _window is null)
        {
            return;
        }

        try
        {
            var settings = _settings.Current;
            _window.Visual.Configure(settings.Appearance);
            _window.MatchSize();
            _groupIndex = WheelMath.WrapIndex(settings.LastGroupIndex, settings.Groups.Count);
            ShowCurrentGroup(crossFade: false);
            _window.Visual.SetHighlight(0, "Loadout");
            _window.Visual.SetHighlight(null, null);
            _window.Visual.UpdateLayout();
            SlotLabels.Warm(settings.Groups.SelectMany(g => g.Slots).OfType<SlotSettings>());
        }
        catch (Exception ex)
        {
            Log.Error("Preparing the wheel failed.", ex);
        }
    }

    public void UpdateAim(double dx, double dy)
    {
        if (!IsOpen)
        {
            return;
        }

        var metrics = _window!.Visual.Metrics;
        var (x, y) = WheelMath.ClampAim(
            _aimX + (dx - _lastTotalX),
            _aimY + (dy - _lastTotalY),
            metrics.CancelZoneRadius * 3);
        _aimX = x;
        _aimY = y;
        _lastTotalX = dx;
        _lastTotalY = dy;

        SetHighlight(WheelMath.SlotFromAim(_aimX, _aimY, metrics.CancelZoneRadius));
    }

    public void ChangeGroup(int delta)
    {
        if (!IsOpen || delta == 0)
        {
            return;
        }

        var settings = _settings.Current;
        if (settings.Groups.Count < 2)
        {
            return;
        }

        _groupIndex = WheelMath.WrapIndex(_groupIndex + delta, settings.Groups.Count);
        settings.LastGroupIndex = _groupIndex;
        _settings.Save();
        ShowCurrentGroup(crossFade: true);

        var highlight = _highlight;
        _highlight = null;
        SetHighlight(highlight);
    }

    public void HighlightSlot(int index)
    {
        if (!IsOpen || index is < 0 or >= WheelMath.SlotCount)
        {
            return;
        }

        // Point the aim at that slot so a small mouse movement afterwards keeps it.
        var metrics = _window!.Visual.Metrics;
        var (x, y) = WheelMath.SlotCenter(index, metrics.CancelZoneRadius * 2);
        _aimX = x;
        _aimY = y;
        SetHighlight(index);
    }

    public SlotSettings? Close(bool launch)
    {
        if (!IsOpen)
        {
            return null;
        }

        IsOpen = false;
        SlotSettings? chosen = null;
        var settings = _settings.Current;
        if (launch && _highlight is int index && _groupIndex < settings.Groups.Count)
        {
            var group = settings.Groups[_groupIndex];
            chosen = index < group.Slots.Count ? group.Slots[index] : null;
            if (chosen is not null)
            {
                group.LastLaunchedSlot = index;
                _settings.Save();
            }
        }

        _window?.HideWheel(_animate);
        foreach (var dim in _dims)
        {
            dim.Uncover(_animate);
        }

        _highlight = null;
        return chosen;
    }

    private void ShowCurrentGroup(bool crossFade)
    {
        var settings = _settings.Current;
        var group = settings.Groups[_groupIndex];
        var iconSize = settings.Appearance.IconSize;
        _window!.Visual.ShowGroup(
            group,
            _groupIndex,
            settings.Groups.Count,
            slot => SafeIcon(slot, iconSize),
            settings.Appearance.ShowLastLaunchedMarker,
            crossFade);
    }

    private ImageSource SafeIcon(SlotSettings slot, int iconSize)
    {
        try
        {
            return _icons.GetIcon(slot, iconSize);
        }
        catch (Exception ex)
        {
            Log.Error("Icon lookup failed.", ex);
            return new DrawingImage();
        }
    }

    private void SetHighlight(int? slot)
    {
        if (slot == _highlight)
        {
            return;
        }

        _highlight = slot;
        string? name = null;
        if (slot is int i)
        {
            var group = _settings.Current.Groups[_groupIndex];
            var filled = i < group.Slots.Count ? group.Slots[i] : null;
            name = filled is null ? "Empty" : SlotLabels.For(filled);
        }

        _window!.Visual.SetHighlight(slot, name);
    }
}
