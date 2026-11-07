using System.Windows;
using Loadout.Contracts;
using Loadout.Core;

namespace Loadout.Settings.Window;

/// <summary>
/// Opens the settings window on demand and releases everything when it closes (SPEC 5, 8).
/// Wire in App.xaml.cs: <c>new SettingsWindowService(settings, input, icons, new RunKeyStartupRegistration())</c>.
/// </summary>
public sealed class SettingsWindowService(
    ISettingsStore settings, IInputService input, IIconProvider icons, IStartupRegistration startup) : ISettingsWindow
{
    private SettingsWindow? _window;

    public void Show()
    {
        if (_window is { } existing)
        {
            Bring(existing);
            return;
        }

        var window = new SettingsWindow(new SettingsContext(settings, input, icons, startup));
        _window = window;
        window.Closed += (_, _) =>
        {
            _window = null;
            MemoryTrimmer.Trim();
        };
        window.Show();
        Bring(window);
    }

    /// <summary>Without a taskbar button the window must be pulled to the front by hand.</summary>
    private static void Bring(SettingsWindow window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}
