using System.Windows;
using Loadout.Contracts;
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
        _input.AimChanged += (_, e) => _wheel.UpdateAim(e.Dx, e.Dy);
        _input.GroupScrolled += (_, delta) => _wheel.ChangeGroup(delta);
        _input.SlotKeyPressed += (_, index) => _wheel.HighlightSlot(index);
        _input.Released += OnReleased;
        _input.Cancelled += (_, _) => _wheel.Close(launch: false);
        _input.Configure(_settings.Current.Hotkey);
        _input.Start();

        WarmIcons();
        ShowWelcomeOnce();
    }

    /// <summary>Opens settings, used when a second copy of Loadout is started.</summary>
    public void ShowSettings() => _settingsWindow.Show();

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _input.Dispose();
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
        _wheel.Open(e.Cursor);
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        var slot = _wheel.Close(launch: true);
        if (slot is null)
        {
            return;
        }

        var result = _launcher.Launch(slot);
        if (result.Outcome == LaunchOutcome.Failed)
        {
            Log.Error($"Could not open {slot.Type} slot: {result.Message}");
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _input.Configure(_settings.Current.Hotkey);
        WarmIcons();
    }

    private void WarmIcons()
    {
        var current = _settings.Current;
        var slots = current.Groups.SelectMany(g => g.Slots).OfType<Settings.Model.SlotSettings>();
        _icons.Warm(slots, current.Appearance.IconSize);
    }

    private void ShowWelcomeOnce()
    {
        var general = _settings.Current.General;
        if (general.HasShownWelcome)
        {
            return;
        }

        _tray.ShowNotification("Loadout is running", "Hold Ctrl + Alt to open your wheel.");
        general.HasShownWelcome = true;
        _settings.Save();
    }

    internal static void RunOnUiThread(Action action) =>
        Application.Current?.Dispatcher.BeginInvoke(action);
}
