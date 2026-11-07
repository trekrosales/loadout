using System.Windows;
using System.Windows.Controls;
using Loadout.Core;

namespace Loadout.Settings.Window;

/// <summary>The five sections of the settings window.</summary>
public enum SettingsSection
{
    Shortcuts,
    Hotkey,
    Appearance,
    Games,
    General,
}

/// <summary>The settings window shell: title bar, navigation, the current page and the live preview.</summary>
public sealed partial class SettingsWindow : System.Windows.Window
{
    private readonly SettingsContext _ctx;
    private readonly SectionPage[] _pages;
    private readonly RadioButton[] _navButtons;
    private readonly PreviewPanel _preview;
    private int _current = -1;

    internal SettingsWindow(SettingsContext ctx)
    {
        _ctx = ctx;
        InitializeComponent();
        ctx.Owner = this;

        _preview = new PreviewPanel(ctx);
        PreviewHost.Child = _preview;
        ctx.PreviewChanged += _preview.Refresh;

        _pages =
        [
            new ShortcutsPage(ctx),
            new HotkeyPage(ctx),
            new AppearancePage(ctx),
            new GamesPage(ctx),
            new GeneralPage(ctx),
        ];
        ctx.Reloaded += ReloadPages;

        var names = Enum.GetNames<SettingsSection>();
        _navButtons = new RadioButton[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var button = new RadioButton { Content = names[i], GroupName = "nav" };
            UiKit.UseStyle(button, "NavStyle");
            button.Checked += (_, _) => Select(index);
            _navButtons[i] = button;
            Nav.Children.Add(button);
        }

        CloseButton.Click += (_, _) => Close();
        SourceInitialized += (_, _) => WindowStyles.HideFromTaskSwitchers(this);
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && System.Windows.Input.Keyboard.FocusedElement is not TextBox
                && !_ctx.Input.IsWheelOpen && !((HotkeyPage)_pages[(int)SettingsSection.Hotkey]).IsRecording)
            {
                Close();
            }
        };

        SelectSection(SettingsSection.Shortcuts);
        FitToWorkArea();
    }

    /// <summary>On high-DPI or small screens the default size can be bigger than the monitor: keep the window on it.</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    /// <summary>Switches to a section (also used for screenshots).</summary>
    public void SelectSection(SettingsSection section)
    {
        _navButtons[(int)section].IsChecked = true;
        Select((int)section);
    }

    private void Select(int index)
    {
        if (index == _current)
        {
            return;
        }

        _current = index;
        if (index == (int)SettingsSection.General)
        {
            _pages[index].Reload();
        }

        PageHost.Content = _pages[index];
        var showPreview = index is (int)SettingsSection.Shortcuts or (int)SettingsSection.Appearance;
        PreviewHost.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = showPreview ? new GridLength(276) : new GridLength(0);
        _ctx.HoverSlot = null;
        _ctx.RefreshPreview();
    }

    private void ReloadPages()
    {
        foreach (var page in _pages)
        {
            page.Reload();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var page in _pages)
        {
            page.OnWindowClosing();
        }

        _ctx.PreviewChanged -= _preview.Refresh;
        _ctx.Reloaded -= ReloadPages;
        _ctx.Owner = null;
        PageHost.Content = null;
        PreviewHost.Child = null;
        base.OnClosed(e);
    }
}
