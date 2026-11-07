using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Loadout.Contracts;
using Loadout.Settings.Model;

namespace Loadout.Settings.Window;

/// <summary>Colors used by code-built UI. Always frozen. Mirrors Theme.xaml.</summary>
internal static class Palette
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly Brush Bg = Make("#1E1F22");
    public static readonly Brush Panel = Make("#2B2D31");
    public static readonly Brush Panel2 = Make("#383A40");
    public static readonly Brush Line = Make("#3F4147");
    public static readonly Brush Accent = Make("#8EC9E8");
    public static readonly Brush AccentSoft = Make("#308EC9E8");
    public static readonly Brush Text = Make("#FFFFFF");
    public static readonly Brush DimText = Make("#B5BAC1");
    public static readonly Brush FaintText = Make("#80848E");
    public static readonly Brush Danger = Make("#F2777A");
    public static readonly Brush Good = Make("#7FD8A0");

    public static SolidColorBrush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Make(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Everything the pages share: the services, the selected group, and change notifications.</summary>
internal sealed class SettingsContext(ISettingsStore store, IInputService input, IIconProvider icons, IStartupRegistration startup)
{
    public ISettingsStore Store { get; } = store;

    public IInputService Input { get; } = input;

    public IIconProvider Icons { get; } = icons;

    public IStartupRegistration Startup { get; } = startup;

    public LoadoutSettings Current => Store.Current;

    /// <summary>The window that owns dialogs.</summary>
    public System.Windows.Window? Owner { get; set; }

    /// <summary>Index of the group being edited (and shown in the preview).</summary>
    public int SelectedGroup { get; set; }

    /// <summary>The slot under the mouse in the editor, shown highlighted in the preview.</summary>
    public int? HoverSlot { get; set; }

    /// <summary>Raised when the preview needs redrawing.</summary>
    public event Action? PreviewChanged;

    /// <summary>Raised after settings were replaced wholesale (import, reset): every page must reload.</summary>
    public event Action? Reloaded;

    public int ClampedGroup()
    {
        SelectedGroup = Math.Clamp(SelectedGroup, 0, Math.Max(0, Current.Groups.Count - 1));
        return SelectedGroup;
    }

    public GroupSettings Group => Current.Groups[ClampedGroup()];

    /// <summary>Saves (debounced by the store) and refreshes the preview.</summary>
    public void Commit()
    {
        Store.Save();
        PreviewChanged?.Invoke();
    }

    public void RefreshPreview() => PreviewChanged?.Invoke();

    public void RaiseReloaded()
    {
        Reloaded?.Invoke();
        PreviewChanged?.Invoke();
    }
}

/// <summary>Small helpers that build the common pieces of the pages.</summary>
internal static class UiKit
{
    public static void UseStyle(FrameworkElement element, string key) =>
        element.SetResourceReference(FrameworkElement.StyleProperty, key);

    public static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = 24,
        FontWeight = FontWeights.SemiBold,
        Foreground = Palette.Text,
        Margin = new Thickness(0, 0, 0, 4),
    };

    public static TextBlock Caption(string text, double bottom = 16) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = Palette.DimText,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, bottom),
    };

    public static TextBlock Label(string text, double size = 14, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Palette.Text,
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Note(string text, Brush? brush = null) => new()
    {
        Text = text,
        FontSize = 12.5,
        Foreground = brush ?? Palette.DimText,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 18,
    };

    public static Border Card(UIElement content, Thickness? margin = null) => new()
    {
        Background = Palette.Panel,
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(18, 14, 18, 14),
        Margin = margin ?? new Thickness(0, 0, 0, 12),
        Child = content,
    };

    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    /// <summary>A line of text (title and optional explanation) on the left and a control on the right.</summary>
    public static Grid Row(string title, string? description, UIElement control, double bottom = 0)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, bottom) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        texts.Children.Add(Label(title));
        if (description is not null)
        {
            var note = Note(description);
            note.Margin = new Thickness(0, 2, 0, 0);
            texts.Children.Add(note);
        }

        grid.Children.Add(texts);
        Grid.SetColumn((FrameworkElement)control, 1);
        ((FrameworkElement)control).VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }

    public static ToggleButton Switch(string name)
    {
        var toggle = new ToggleButton();
        System.Windows.Automation.AutomationProperties.SetName(toggle, name);
        UseStyle(toggle, "SwitchStyle");
        return toggle;
    }

    /// <summary>Runs <paramref name="onChange"/> whenever the switch turns on or off, however that happened (mouse, keyboard, accessibility tools).</summary>
    public static void OnToggled(ToggleButton toggle, Action<bool> onChange)
    {
        toggle.Checked += (_, _) => onChange(true);
        toggle.Unchecked += (_, _) => onChange(false);
    }

    public static Button MakeButton(string text, RoutedEventHandler onClick, string? style = null, double minWidth = 0)
    {
        var button = new Button { Content = text, MinWidth = minWidth };
        if (style is not null)
        {
            UseStyle(button, style);
        }

        button.Click += onClick;
        return button;
    }

    /// <summary>A slider with its current value shown to the right.</summary>
    public static (Grid Root, Slider Slider, TextBlock Value) SliderRow(
        string title, double min, double max, double step, Func<double, string> format)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            LargeChange = step * 5,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var value = new TextBlock
        {
            FontSize = 13,
            Foreground = Palette.DimText,
            Width = 60,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, title);
        slider.ValueChanged += (_, e) => value.Text = format(e.NewValue);

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Label(title);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return (grid, slider, value);
    }

    public static Border Cap(string text)
    {
        return new Border
        {
            Background = Palette.Panel2,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1, 1, 1, 3),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 7, 14, 7),
            MinWidth = 44,
            Margin = new Thickness(0, 0, 0, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.Text,
                TextAlignment = TextAlignment.Center,
            },
        };
    }
}
