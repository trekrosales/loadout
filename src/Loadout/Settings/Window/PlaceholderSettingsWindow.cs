// PLACEHOLDER: a small "coming soon" window. Wave 3 (feature/settings) replaces it.

using System.Windows;
using System.Windows.Controls;
using Loadout.Contracts;
using Loadout.Core;

namespace Loadout.Settings.Window;

public sealed class PlaceholderSettingsWindow : ISettingsWindow
{
    private System.Windows.Window? _window;

    public void Show()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _window = new System.Windows.Window
        {
            Title = "Loadout Settings",
            Width = 360,
            Height = 160,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock
            {
                Text = "Settings arrive in a later build.",
                Margin = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        WindowStyles.HideFromTaskSwitchers(_window);
        _window.Closed += (_, _) =>
        {
            _window = null;
            MemoryTrimmer.Trim();
        };
        _window.Show();
        _window.Activate();
    }
}
