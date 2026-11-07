using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using Loadout.Core;

namespace Loadout.Settings.Window;

/// <summary>A dark, frameless dialog window with its own title bar. Never shows in the taskbar or Alt + Tab.</summary>
internal class DialogShell : System.Windows.Window
{
    private readonly ContentControl _body = new();

    public DialogShell(System.Windows.Window? owner, string title, double width, double height)
    {
        Owner = owner;
        Title = title;
        Width = width;
        if (height > 0)
        {
            Height = height;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
        }

        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Background = Palette.Bg;
        FontFamily = Palette.Font;
        Foreground = Palette.Text;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Loadout;component/Settings/Window/Theme.xaml", UriKind.Relative),
        });
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var close = new Button { Content = "✕", Width = 40, Height = 40, Padding = new Thickness(0) };
        UiKit.UseStyle(close, "GhostButton");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Palette.DimText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
        };
        var bar = new DockPanel { Height = 40, Background = Palette.Panel, LastChildFill = true };
        bar.Children.Add(close);
        bar.Children.Add(titleText);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_body);
        Content = new System.Windows.Controls.Border
        {
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            Child = root,
        };

        SourceInitialized += (_, _) => WindowStyles.HideFromTaskSwitchers(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    protected void SetBody(UIElement body) => _body.Content = body;
}

/// <summary>Confirm, message and one-line prompt dialogs in the app's look.</summary>
internal sealed class SimpleDialog : DialogShell
{
    private readonly TextBox? _input;
    private readonly TextBlock? _error;
    private readonly Func<string, string?>? _validate;

    private SimpleDialog(
        System.Windows.Window? owner, string title, string message, string okText, string? cancelText,
        bool danger, string? initialText, Func<string, string?>? validate)
        : base(owner, title, 420, 0)
    {
        _validate = validate;
        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            Foreground = Palette.Text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        if (initialText is not null)
        {
            _input = new TextBox { Text = initialText, Margin = new Thickness(0, 0, 0, 4) };
            _input.TextChanged += (_, _) => _input.Tag = null;
            _input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    TryAccept();
                    e.Handled = true;
                }
            };
            panel.Children.Add(_input);
            _error = new TextBlock
            {
                FontSize = 12,
                Foreground = Palette.Danger,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 18,
                Margin = new Thickness(0, 2, 0, 10),
            };
            panel.Children.Add(_error);
            Loaded += (_, _) =>
            {
                _input.Focus();
                _input.SelectAll();
            };
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancelText is not null)
        {
            buttons.Children.Add(UiKit.MakeButton(cancelText, (_, _) => Close(), null, 84));
            buttons.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        }

        var ok = UiKit.MakeButton(okText, (_, _) => TryAccept(), danger ? "DangerButton" : "AccentButton", 84);
        ok.IsDefault = _input is null;
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        SetBody(panel);
    }

    public string? Text => _input?.Text;

    public static bool Confirm(System.Windows.Window? owner, string title, string message, string okText, bool danger = true)
    {
        var dialog = new SimpleDialog(owner, title, message, okText, "Cancel", danger, null, null);
        return dialog.ShowDialog() == true;
    }

    public static void Info(System.Windows.Window? owner, string title, string message) =>
        new SimpleDialog(owner, title, message, "OK", null, false, null, null).ShowDialog();

    /// <summary>Returns the text, or null if cancelled. <paramref name="validate"/> returns an error message or null.</summary>
    public static string? Prompt(
        System.Windows.Window? owner, string title, string message, string initial, Func<string, string?>? validate = null)
    {
        var dialog = new SimpleDialog(owner, title, message, "OK", "Cancel", false, initial, validate);
        return dialog.ShowDialog() == true ? dialog.Text : null;
    }

    private void TryAccept()
    {
        if (_input is not null && _validate is not null)
        {
            var error = _validate(_input.Text);
            if (error is not null)
            {
                _input.Tag = "invalid";
                _error!.Text = error;
                return;
            }
        }

        DialogResult = true;
    }
}
