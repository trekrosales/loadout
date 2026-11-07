using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Loadout.Settings.Model;
using Loadout.Wheel.Geometry;

namespace Loadout.Wheel;

/// <summary>
/// Draws the wheel (SPEC section 4): 8 glass slices, icons, the last-launched marker and the center circle with its text.
/// Everything is vector and frozen where possible, so redraws are cheap and nothing runs while idle.
/// </summary>
internal sealed class WheelVisual : Canvas
{
    /// <summary>Room around the ring for the highlight glow.</summary>
    public const double Padding = 6;

    private static readonly FontFamily s_font = new("Segoe UI Variable Text, Segoe UI");

    private readonly Path[] _slices = new Path[WheelMath.SlotCount];
    private readonly Path[] _markers = new Path[WheelMath.SlotCount];
    private readonly Image[] _icons = new Image[WheelMath.SlotCount];
    private readonly ScaleTransform[] _iconScales = new ScaleTransform[WheelMath.SlotCount];
    private readonly Canvas _iconLayer = new() { IsHitTestVisible = false };
    private readonly TextBlock _cancelGlyph = new();
    private readonly TextBlock _name = new();
    private readonly TextBlock _groupName = new();
    private readonly TextBlock _pager = new();

    private string? _builtSignature;
    private bool _animate = true;
    private Brush _sliceBrush = Brushes.Transparent;
    private Brush _emptyBrush = Brushes.Transparent;
    private Brush _highlightBrush = Brushes.Transparent;
    private Pen _edgePen = new();
    private Pen _highlightPen = new();
    private bool[] _filled = new bool[WheelMath.SlotCount];
    private int? _highlight;

    public WheelVisual()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public WheelMetrics Metrics { get; private set; }

    /// <summary>Width and height of the whole visual, in DIPs.</summary>
    public double Size => Metrics.Diameter + (2 * Padding);

    /// <summary>Rebuilds the shapes if the icon size or colors changed. Cheap when nothing changed.</summary>
    public void Configure(AppearanceSettings appearance)
    {
        _animate = appearance.Animations && SystemParameters.ClientAreaAnimation;
        var signature = string.Join('|', appearance.IconSize, appearance.TintColor,
            appearance.TintOpacity.ToString(CultureInfo.InvariantCulture), appearance.HighlightColor);
        if (signature == _builtSignature)
        {
            return;
        }

        _builtSignature = signature;
        Build(appearance);
    }

    /// <summary>Shows a group's icons, names and marker. <paramref name="crossFade"/> fades the icons in (group change).</summary>
    public void ShowGroup(
        GroupSettings group, int groupIndex, int groupCount, Func<SlotSettings, ImageSource> iconFor,
        bool showMarker, bool crossFade)
    {
        for (var i = 0; i < WheelMath.SlotCount; i++)
        {
            var slot = i < group.Slots.Count ? group.Slots[i] : null;
            _filled[i] = slot is not null;
            _icons[i].Source = slot is null ? null : iconFor(slot);
            _markers[i].Visibility = showMarker && group.LastLaunchedSlot == i && slot is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        _groupName.Text = group.Name;
        _pager.Text = groupCount > 1
            ? string.Create(CultureInfo.InvariantCulture, $"‹  {groupIndex + 1} / {groupCount}  ›")
            : string.Empty;

        RefreshSliceFills();
        if (crossFade && _animate)
        {
            _iconLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(100)));
        }
    }

    /// <summary>Highlights a slot (or none = Cancel) and sets the center name.</summary>
    public void SetHighlight(int? slot, string? name)
    {
        var previous = _highlight;
        _highlight = slot;

        if (previous is int p && p != slot)
        {
            AnimateIcon(p, 1.0);
        }

        if (slot is int s && s != previous)
        {
            AnimateIcon(s, 1.12);
        }

        RefreshSliceFills();

        var cancel = slot is null;
        _cancelGlyph.Visibility = cancel ? Visibility.Visible : Visibility.Collapsed;
        _name.Text = cancel ? "Cancel" : name ?? "Empty";
        _name.Opacity = cancel || (slot is int i && !_filled[i]) ? 0.7 : 1.0;
    }

    private void RefreshSliceFills()
    {
        for (var i = 0; i < WheelMath.SlotCount; i++)
        {
            var hot = _highlight == i;
            _slices[i].Fill = hot ? _highlightBrush : _filled[i] ? _sliceBrush : _emptyBrush;
            _slices[i].Stroke = hot ? _highlightPen.Brush : _edgePen.Brush;
            _slices[i].StrokeThickness = hot ? _highlightPen.Thickness : _edgePen.Thickness;
        }
    }

    private void AnimateIcon(int index, double scale)
    {
        var transform = _iconScales[index];
        if (!_animate)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transform.ScaleX = transform.ScaleY = scale;
            return;
        }

        var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(80))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void Build(AppearanceSettings appearance)
    {
        Children.Clear();
        _iconLayer.Children.Clear();
        _highlight = null;
        _filled = new bool[WheelMath.SlotCount];

        var m = WheelMath.Measure(appearance.IconSize);
        Metrics = m;
        Width = Height = Size;
        var c = Padding + m.OuterRadius;

        var tint = ParseColor(appearance.TintColor, Color.FromRgb(0x2B, 0x2D, 0x31));
        var accent = ParseColor(appearance.HighlightColor, Color.FromRgb(0x8E, 0xC9, 0xE8));
        var opacity = Math.Clamp(appearance.TintOpacity, 0.3, 0.9);

        _sliceBrush = Frozen(new SolidColorBrush(WithAlpha(tint, opacity)));
        _emptyBrush = Frozen(new SolidColorBrush(WithAlpha(tint, opacity * 0.55)));
        // The highlight glows brighter toward the outer edge. Absolute mapping makes the gradient follow
        // the whole ring rather than each slice's own bounds.
        _highlightBrush = Frozen(new RadialGradientBrush(
            new GradientStopCollection
            {
                new(WithAlpha(accent, 0.28), 0),
                new(WithAlpha(accent, 0.28), m.InnerRadius / m.OuterRadius),
                new(WithAlpha(accent, 0.65), 1.0),
            })
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(m.OuterRadius, m.OuterRadius),
            GradientOrigin = new Point(m.OuterRadius, m.OuterRadius),
            RadiusX = m.OuterRadius,
            RadiusY = m.OuterRadius,
        });
        _edgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255))), 1));
        _highlightPen = Frozen(new Pen(Frozen(new SolidColorBrush(WithAlpha(accent, 0.95))), 1.5));

        // Faint dark outline so the glass reads on bright backgrounds.
        Children.Add(Positioned(
            new Ellipse
            {
                Width = (m.OuterRadius * 2) + 2,
                Height = (m.OuterRadius * 2) + 2,
                Stroke = Frozen(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0))),
                StrokeThickness = 2,
            },
            c - m.OuterRadius - 1,
            c - m.OuterRadius - 1));

        for (var i = 0; i < WheelMath.SlotCount; i++)
        {
            var slice = new Path { Data = WheelMath.CreateSliceGeometry(i, m), Fill = _sliceBrush };
            _slices[i] = slice;
            Children.Add(Positioned(slice, Padding, Padding));

            var marker = new Path
            {
                Data = MarkerGeometry(i, m),
                Stroke = Frozen(new SolidColorBrush(accent)),
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Visibility = Visibility.Collapsed,
            };
            _markers[i] = marker;
            Children.Add(Positioned(marker, Padding, Padding));
        }

        // Glass sheen: a soft light gradient across the top half of the ring.
        var ring = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(m.OuterRadius, m.OuterRadius), m.OuterRadius, m.OuterRadius),
            new EllipseGeometry(new Point(m.OuterRadius, m.OuterRadius), m.InnerRadius, m.InnerRadius));
        ring.Freeze();
        Children.Add(Positioned(
            new Path
            {
                Data = ring,
                Fill = Frozen(new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(Color.FromArgb(40, 255, 255, 255), 0),
                        new(Color.FromArgb(10, 255, 255, 255), 0.45),
                        new(Color.FromArgb(0, 255, 255, 255), 0.6),
                    },
                    new Point(0.5, 0),
                    new Point(0.5, 1))),
            },
            Padding,
            Padding));

        // Icons.
        for (var i = 0; i < WheelMath.SlotCount; i++)
        {
            var (dx, dy) = WheelMath.SlotCenter(i, m.IconRingRadius);
            var scale = new ScaleTransform(1, 1);
            _iconScales[i] = scale;
            var icon = new Image
            {
                Width = m.IconSize,
                Height = m.IconSize,
                Stretch = Stretch.Uniform,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            _icons[i] = icon;
            _iconLayer.Children.Add(Positioned(icon, c + dx - (m.IconSize / 2), c + dy - (m.IconSize / 2)));
        }

        Children.Add(_iconLayer);

        // Center circle.
        var centerRadius = m.InnerRadius - 4;
        Children.Add(Positioned(
            new Ellipse
            {
                Width = centerRadius * 2,
                Height = centerRadius * 2,
                Fill = Frozen(new SolidColorBrush(WithAlpha(tint, Math.Min(0.95, opacity + 0.12)))),
                Stroke = _edgePen.Brush,
                StrokeThickness = 1,
            },
            c - centerRadius,
            c - centerRadius));

        var textWidth = centerRadius * 1.7;
        var nameSize = Math.Max(11, m.IconSize * 0.27);
        StyleText(_cancelGlyph, nameSize * 1.1, 0.75, FontWeights.Normal, textWidth);
        _cancelGlyph.Text = "✕";
        StyleText(_name, nameSize, 1, FontWeights.SemiBold, textWidth);
        StyleText(_groupName, nameSize * 0.8, 0.72, FontWeights.Normal, textWidth);
        StyleText(_pager, nameSize * 0.75, 0.55, FontWeights.Normal, textWidth);

        var stack = new StackPanel { Width = textWidth, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(_cancelGlyph);
        stack.Children.Add(_name);
        stack.Children.Add(_groupName);
        stack.Children.Add(_pager);
        var holder = new Grid { Width = textWidth, Height = centerRadius * 2 };
        holder.Children.Add(stack);
        Children.Add(Positioned(holder, c - (textWidth / 2), c - centerRadius));

        SetHighlight(null, null);
    }

    private static void StyleText(TextBlock text, double size, double opacity, FontWeight weight, double width)
    {
        text.FontFamily = s_font;
        text.FontSize = size;
        text.FontWeight = weight;
        text.Foreground = Brushes.White;
        text.Opacity = opacity;
        text.TextAlignment = TextAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.MaxWidth = width;
        text.HorizontalAlignment = HorizontalAlignment.Center;
    }

    /// <summary>A thin arc just inside the outer edge of a slice, like the "equipped" marker in action games.</summary>
    private static System.Windows.Media.Geometry MarkerGeometry(int index, WheelMetrics m)
    {
        var radius = m.OuterRadius - 3.5;
        var half = (WheelMath.SlotAngle / 2) - 6;
        var center = index * WheelMath.SlotAngle;
        var start = PointAt(m.OuterRadius, radius, center - half);
        var end = PointAt(m.OuterRadius, radius, center + half);
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointAt(double c, double radius, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new Point(c + (radius * Math.Sin(a)), c - (radius * Math.Cos(a)));
    }

    private static T Positioned<T>(T element, double left, double top)
        where T : UIElement
    {
        SetLeft(element, left);
        SetTop(element, top);
        return element;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    internal static Color ParseColor(string? hex, Color fallback)
    {
        if (hex is { Length: 7 } && hex[0] == '#'
            && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        return fallback;
    }
}
