using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Loadout.Settings.Model;
using Loadout.Wheel;

namespace Loadout.Settings.Window;

/// <summary>The live wheel preview: a wheel drawn over a sample wallpaper, dimmed like the real thing.</summary>
internal sealed class PreviewPanel : Border
{
    private static readonly Brush s_wallpaper = CreateWallpaper();

    private readonly SettingsContext _ctx;
    private readonly Viewbox _box = new();
    private WheelVisual _wheel = new();
    private string? _signature;
    private readonly Rectangle _dim = new();

    public PreviewPanel(SettingsContext ctx)
    {
        _ctx = ctx;
        CornerRadius = new CornerRadius(14);
        ClipToBounds = true;
        Background = s_wallpaper;

        var grid = new Grid();
        grid.Children.Add(_dim);
        _box.Stretch = Stretch.Uniform;
        _box.StretchDirection = StretchDirection.DownOnly;
        _box.Margin = new Thickness(10, 36, 10, 14);
        _box.Child = _wheel;
        grid.Children.Add(_box);
        grid.Children.Add(new TextBlock
        {
            Text = "LIVE PREVIEW",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Palette.Make("#B0FFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(14, 12, 0, 0),
        });
        Child = grid;
        Refresh();
    }

    public void Refresh()
    {
        var settings = _ctx.Current;
        var appearance = settings.Appearance;
        var groupIndex = _ctx.ClampedGroup();
        var group = settings.Groups[groupIndex];

        // WheelVisual.Configure can't rebuild itself a second time (its text blocks stay attached to the old layout),
        // so a new visual is used whenever size or colors change.
        var signature = string.Join('|', appearance.IconSize, appearance.TintColor, appearance.TintOpacity, appearance.HighlightColor);
        if (_signature is not null && signature != _signature)
        {
            _wheel = new WheelVisual();
            _box.Child = _wheel;
        }

        _signature = signature;
        _wheel.Configure(appearance);
        _wheel.ShowGroup(
            group,
            groupIndex,
            settings.Groups.Count,
            slot => _ctx.Icons.GetIcon(slot, appearance.IconSize),
            appearance.ShowLastLaunchedMarker,
            crossFade: false);

        var hover = _ctx.HoverSlot;
        var hoverSlot = hover is int h && h < group.Slots.Count ? group.Slots[h] : null;
        _wheel.SetHighlight(hover, hoverSlot is null ? null : SlotLabels.For(hoverSlot));

        var dim = HexColor.TryParse(appearance.DimColor, out var parsedDim) ? parsedDim : Colors.Black;
        var alpha = (byte)Math.Round(Math.Clamp(appearance.DimOpacity, 0, 1) * 255);
        _dim.Fill = Palette.Make(Color.FromArgb(alpha, dim.R, dim.G, dim.B));
    }

    private static LinearGradientBrush CreateWallpaper()
    {
        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromRgb(0x2E, 0x6F, 0x9E), 0),
                new(Color.FromRgb(0x6A, 0x4C, 0x93), 0.5),
                new(Color.FromRgb(0xC2, 0x6A, 0x7A), 1),
            },
            new Point(0, 0),
            new Point(1, 1));
        brush.Freeze();
        return brush;
    }
}
