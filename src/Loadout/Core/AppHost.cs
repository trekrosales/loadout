using System.Text;
using System.Windows;
using System.Windows.Threading;
using Loadout.Contracts;
using Loadout.Settings.Model;
using Loadout.Tray;

namespace Loadout.Core;

/// <summary>
/// Connects all the parts (owned by the lead). Each part only knows its contract;
/// this class wires their events together. Everything runs on the UI thread.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly IInputService _input;
    private readonly IWheelPresenter _wheel;
    private readonly ILauncher _launcher;
    private readonly IIconProvider _icons;
    private readonly IGameModePolicy _gameMode;
    private readonly ISettingsWindow _settingsWindow;
    private readonly TrayIcon _tray;
    private string? _warmedIconsSignature;
    private bool _disposed;

    public AppHost(
        ISettingsStore settings,
        IInputService input,
        IWheelPresenter wheel,
        ILauncher launcher,
        IIconProvider icons,
        IGameModePolicy gameMode,
        ISettingsWindow settingsWindow,
        TrayIcon tray)
    {
        _settings = settings;
        _input = input;
        _wheel = wheel;
        _launcher = launcher;
        _icons = icons;
        _gameMode = gameMode;
        _settingsWindow = settingsWindow;
        _tray = tray;
    }

    /// <summary>Raised when the user chooses Exit.</summary>
    public event EventHandler? ExitRequested;

    public void Start()
    {
        _settings.Load();
        _settings.Changed += OnSettingsChanged;

        _tray.SettingsRequested += (_, _) => _settingsWindow.Show();
        _tray.PauseToggled += (_, paused) => _input.IsPaused = paused;
        _tray.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _tray.Show();

        _input.OpenRequested += OnOpenRequested;
        _input.AimChanged += (_, e) =>
        {
            Log.Trace($"Aim {e.Dx:0},{e.Dy:0}");
            _wheel.UpdateAim(e.Dx, e.Dy);
        };
        _input.GroupScrolled += (_, delta) =>
        {
            Log.Trace($"GroupScrolled {delta}");
            _wheel.ChangeGroup(delta);
        };
        _input.SlotKeyPressed += (_, index) =>
        {
            Log.Trace($"SlotKeyPressed {index}");
            _wheel.HighlightSlot(index);
        };
        _input.Released += OnReleased;
        _input.Cancelled += (_, _) =>
        {
            Log.Trace("Cancelled");
            _wheel.Close(launch: false);
        };
        _input.Configure(_settings.Current.Hotkey);
        _input.Start();

        WarmIconsIfNeeded();
        ShowWelcomeOnce();
    }

    /// <summary>Opens settings, used when a second copy of Loadout is started.</summary>
    public void ShowSettings() => _settingsWindow.Show();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _input.Dispose();
        _settings.Flush();
        _tray.Dispose();
    }

    private void OnOpenRequested(object? sender, OpenRequestedEventArgs e)
    {
        if (!_gameMode.CanOpenNow())
        {
            e.Cancel = true;
            return;
        }

        e.BlockMouse = _gameMode.ShouldBlockMouse();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _wheel.Open(e.Cursor);
        Log.Trace($"OpenRequested at {e.Cursor.X},{e.Cursor.Y} blockMouse={e.BlockMouse}, opened in "
            + $"{System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms");
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        var slot = _wheel.Close(launch: true);
        Log.Trace($"Released, slot={(slot is null ? "none" : slot.Type.ToString())}");
        if (slot is null)
        {
            return;
        }

        // Launch on a later dispatcher turn so the wheel's close is processed first
        // and nothing slow ever runs while input is being handled.
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var result = _launcher.Launch(slot);
            if (result.Outcome == LaunchOutcome.Failed)
            {
                Log.Error($"Could not open {slot.Type} slot: {result.Message}");
            }
        });
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _input.Configure(_settings.Current.Hotkey);
        WarmIconsIfNeeded();
    }

    /// <summary>Re-warms icons only when the slots or the icon size changed, not on every save.</summary>
    private void WarmIconsIfNeeded()
    {
        var current = _settings.Current;
        var signature = IconSignature(current);
        if (signature == _warmedIconsSignature)
        {
            return;
        }

        _warmedIconsSignature = signature;
        var slots = current.Groups.SelectMany(g => g.Slots).OfType<SlotSettings>().ToList();
        _icons.Warm(slots, current.Appearance.IconSize);
    }

    private static string IconSignature(LoadoutSettings settings)
    {
        var sb = new StringBuilder().Append(settings.Appearance.IconSize).Append('|');
        foreach (var slot in settings.Groups.SelectMany(g => g.Slots))
        {
            sb.Append(slot?.Type).Append(':').Append(slot?.Path).Append(':')
              .Append(slot?.Url).Append(':').Append(slot?.IconPath).Append('|');
        }

        return sb.ToString();
    }

    private void ShowWelcomeOnce()
    {
        var general = _settings.Current.General;
        if (general.HasShownWelcome)
        {
            return;
        }

        if (_tray.ShowNotification("Loadout is running", "Hold Ctrl + Alt to open your wheel."))
        {
            general.HasShownWelcome = true;
            _settings.Save();
        }
    }

    internal static void RunOnUiThread(Action action) =>
        Application.Current?.Dispatcher.BeginInvoke(action);
}
