using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Loadout.Settings.Window;

/// <summary>A color chooser: about ten preset swatches plus a #RRGGBB box.</summary>
internal sealed class ColorField : StackPanel
{
    private readonly string[] _presets;
    private readonly Border[] _swatches;
    private readonly TextBox _box = new() { Width = 100, FontFamily = new FontFamily("Consolas"), MaxLength = 7 };
    private readonly Border _preview = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
    private string _value = "#000000";
    private bool _setting;

    public ColorField(string[] presets)
    {
        _presets = presets;
        _swatches = new Border[presets.Length];

        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 0; i < presets.Length; i++)
        {
            var color = presets[i];
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = Palette.Make(color),
                BorderThickness = new Thickness(1),
                BorderBrush = Palette.Line,
                Margin = new Thickness(0, 0, 8, 6),
                Cursor = Cursors.Hand,
                ToolTip = color,
            };
            swatch.MouseLeftButtonUp += (_, _) => Value = color;
            _swatches[i] = swatch;
            wrap.Children.Add(swatch);
        }

        Children.Add(wrap);

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        _preview.Margin = new Thickness(0, 0, 10, 0);
        line.Children.Add(_preview);
        line.Children.Add(_box);
        Children.Add(line);

        _box.TextChanged += (_, _) =>
        {
            if (_setting)
            {
                return;
            }

            if (HexColor.Normalize(_box.Text) is { } hex)
            {
                _box.Tag = null;
                Apply(hex, updateBox: false);
            }
            else
            {
                _box.Tag = "invalid";
            }
        };
        _box.LostFocus += (_, _) =>
        {
            _box.Tag = null;
            SetBox(_value);
        };
    }

    /// <summary>Raised when the user picks or types a valid color.</summary>
    public event Action<string>? ValueChanged;

    /// <summary>The current "#RRGGBB". Setting it raises <see cref="ValueChanged"/>; use <see cref="Load"/> to set silently.</summary>
    public string Value
    {
        get => _value;
        set => Apply(HexColor.Normalize(value) ?? _value, updateBox: true, raise: true);
    }

    public void Load(string value)
    {
        Apply(HexColor.Normalize(value) ?? "#000000", updateBox: true, raise: false);
    }

    private void Apply(string hex, bool updateBox, bool raise = true)
    {
        _value = hex;
        var color = HexColor.TryParse(hex, out var parsed) ? parsed : Colors.Black;
        _preview.Background = Palette.Make(color);
        for (var i = 0; i < _swatches.Length; i++)
        {
            _swatches[i].BorderBrush = string.Equals(_presets[i], hex, StringComparison.OrdinalIgnoreCase)
                ? Palette.Text
                : Palette.Line;
            _swatches[i].BorderThickness = new Thickness(string.Equals(_presets[i], hex, StringComparison.OrdinalIgnoreCase) ? 2 : 1);
        }

        if (updateBox)
        {
            SetBox(hex);
        }

        if (raise)
        {
            ValueChanged?.Invoke(hex);
        }
    }

    private void SetBox(string text)
    {
        _setting = true;
        _box.Text = text;
        _setting = false;
    }
}
