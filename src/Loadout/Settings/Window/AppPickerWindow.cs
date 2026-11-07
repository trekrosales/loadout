using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Loadout.Contracts;
using Loadout.Core;
using Loadout.Settings.Model;

namespace Loadout.Settings.Window;

/// <summary>One row of the app picker. The icon is fetched only when the row is first drawn.</summary>
internal sealed class AppEntry(InstalledApp app, IIconProvider icons)
{
    private ImageSource? _icon;

    public string Name { get; } = app.Name;

    public string Aumid { get; } = app.Aumid;

    public ImageSource Icon => _icon ??= icons.GetIcon(
        new SlotSettings { Type = SlotType.App, Path = DropConverter.AppsFolderPrefix + app.Aumid }, 32);
}

/// <summary>Lists the installed apps (like the Start menu) with a search box. The list loads on a background thread.</summary>
internal sealed class AppPickerWindow : DialogShell
{
    private readonly IIconProvider _icons;
    private readonly ListBox _list = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _search = new();
    private readonly Button _choose;
    private ICollectionView? _view;
    private bool _closed;

    public AppPickerWindow(System.Windows.Window? owner, IIconProvider icons)
        : base(owner, "Choose an app", 460, 580)
    {
        _icons = icons;

        _search.Margin = new Thickness(0, 0, 0, 10);
        _search.TextChanged += (_, _) => _view?.Refresh();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _list.Items.Count > 0)
            {
                _list.Focus();
                if (_list.SelectedIndex < 0)
                {
                    _list.SelectedIndex = 0;
                }
            }
            else if (e.Key == Key.Enter)
            {
                Accept();
            }
        };

        _status.Text = "Loadingâ€¦";
        _status.Foreground = Palette.DimText;
        _status.FontSize = 13;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.Margin = new Thickness(0, 30, 0, 0);

        _list.ItemTemplate = BuildTemplate();
        _list.SelectionMode = SelectionMode.Single;
        _list.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(_list, d) is not null)
            {
                Accept();
            }
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
            }
        };
        _list.SelectionChanged += (_, _) => _choose!.IsEnabled = _list.SelectedItem is not null;

        _choose = UiKit.MakeButton("Choose", (_, _) => Accept(), "AccentButton", 90);
        _choose.IsEnabled = false;
        var cancel = UiKit.MakeButton("Cancel", (_, _) => Close(), null, 90);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_choose);

        var area = new Grid();
        area.Children.Add(_list);
        area.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(_search);
        root.Children.Add(buttons);
        root.Children.Add(area);
        SetBody(root);

        Loaded += (_, _) =>
        {
            _search.Focus();
            StartLoading();
        };
        Closed += (_, _) => _closed = true;
    }

    /// <summary>The chosen app, or null if cancelled.</summary>
    public InstalledApp? Result { get; private set; }

    private static DataTemplate BuildTemplate()
    {
        var template = new DataTemplate();
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(FrameworkElement.WidthProperty, 32.0);
        image.SetValue(FrameworkElement.HeightProperty, 32.0);
        image.SetBinding(Image.SourceProperty, new Binding(nameof(AppEntry.Icon)) { Mode = BindingMode.OneTime });
        row.AppendChild(image);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.FontSizeProperty, 14.0);
        text.SetValue(TextBlock.ForegroundProperty, Palette.Text);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(AppEntry.Name)) { Mode = BindingMode.OneTime });
        row.AppendChild(text);

        template.VisualTree = row;
        template.Seal();
        return template;
    }

    private void StartLoading()
    {
        var dispatcher = Dispatcher;
        var thread = new Thread(() =>
        {
            var apps = ShellInterop.EnumerateInstalledApps();
            try
            {
                dispatcher.BeginInvoke(() => ShowApps(apps));
            }
            catch (Exception ex)
            {
                Log.Error("Could not show the app list.", ex);
            }
        })
        {
            IsBackground = true,
            Name = "Loadout app list",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void ShowApps(List<InstalledApp> apps)
    {
        if (_closed)
        {
            return;
        }

        if (apps.Count == 0)
        {
            _status.Text = "No apps found.\nUse \"Browse for a program\" instead.";
            _status.TextAlignment = TextAlignment.Center;
            return;
        }

        _status.Visibility = Visibility.Collapsed;
        _list.ItemsSource = apps.Select(a => new AppEntry(a, _icons)).ToList();
        _view = CollectionViewSource.GetDefaultView(_list.ItemsSource);
        _view.Filter = o => o is AppEntry e
            && (string.IsNullOrWhiteSpace(_search.Text) || e.Name.Contains(_search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    private void Accept()
    {
        if (_list.SelectedItem is AppEntry entry)
        {
            Result = new InstalledApp(entry.Name, entry.Aumid);
            DialogResult = true;
        }
        else if (_list.Items.Count == 1 && _list.Items[0] is AppEntry only)
        {
            Result = new InstalledApp(only.Name, only.Aumid);
            DialogResult = true;
        }
    }
}
