// CONTRACT (owned by the lead): signatures and documented behavior.
// IMPLEMENTATION: Wave 1, branch feature/wheel-math. Pure functions only, no state, no I/O.
// Units are WPF device-independent pixels (DIPs) unless stated otherwise. Positive Y is down.
// "Aim" values (SlotFromAim, ClampAim, CancelZoneRadius) are in physical pixels of mouse movement, a separate
// unit from drawing; CancelZoneRadius is simply a number derived from the icon size.
// Invalid arguments: throw ArgumentOutOfRangeException for a slot index outside 0-7, count < 1, or an icon size
// that is not a finite number > 0; a negative maxLength or radius also throws. SlotFromAim returns null (Cancel)
// if dx or dy is NaN or infinite.
// Note: this namespace is called "Geometry", so the WPF type is written as System.Windows.Media.Geometry.

namespace Loadout.Wheel.Geometry;

/// <summary>Sizes of the wheel for a given icon size (SPEC section 4.2).</summary>
/// <param name="IconSize">Icon edge length.</param>
/// <param name="OuterRadius">Outer edge of the ring of slices. Equals 2.75 × IconSize (diameter about 5.5 × IconSize).</param>
/// <param name="InnerRadius">Inner edge of the ring, which is also the edge of the center Cancel circle. Equals 1.25 × IconSize.</param>
/// <param name="IconRingRadius">Distance from the center to each icon's center. Midway between InnerRadius and OuterRadius.</param>
/// <param name="CancelZoneRadius">
/// How far (in physical pixels of mouse movement) the aim must travel from where the wheel opened before a slot is highlighted.
/// Equals max(12, 0.5 × IconSize).
/// </param>
/// <param name="GapWidth">Width of the straight gap between neighbouring slices. 3 DIPs.</param>
public readonly record struct WheelMetrics(
    double IconSize,
    double OuterRadius,
    double InnerRadius,
    double IconRingRadius,
    double CancelZoneRadius,
    double GapWidth)
{
    public double Diameter => OuterRadius * 2;
}

public static class WheelMath
{
    public const int SlotCount = 8;

    /// <summary>Degrees covered by each slot (360 / 8 = 45).</summary>
    public const double SlotAngle = 360.0 / SlotCount;

    /// <summary>Computes all wheel sizes from the icon size, exactly as documented on <see cref="WheelMetrics"/>.</summary>
    public static WheelMetrics Measure(double iconSize)
    {
        ValidateIconSize(iconSize, nameof(iconSize));

        double outer = 2.75 * iconSize;
        double inner = 1.25 * iconSize;
        return new WheelMetrics(
            iconSize,
            outer,
            inner,
            (outer + inner) / 2,
            Math.Max(12.0, 0.5 * iconSize),
            3.0);
    }

    /// <summary>
    /// Maps total mouse movement since the wheel opened to a slot.
    /// Returns null (Cancel) while the movement length is less than <paramref name="cancelZoneRadius"/>.
    /// Otherwise returns 0-7: slot 0 is straight up (dx = 0, dy &lt; 0) and covers -22.5° to +22.5° around up;
    /// slots continue clockwise (slot 2 = right, slot 4 = down, slot 6 = left).
    /// A direction exactly on a boundary belongs to the slot clockwise of it (e.g. exactly 22.5° gives slot 1).
    /// </summary>
    public static int? SlotFromAim(double dx, double dy, double cancelZoneRadius)
    {
        if (!(cancelZoneRadius >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(cancelZoneRadius), cancelZoneRadius, "Cancel zone radius must not be negative.");
        }

        if (!double.IsFinite(dx) || !double.IsFinite(dy))
        {
            return null;
        }

        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0 || length < cancelZoneRadius)
        {
            return null;
        }

        // Clockwise angle from straight up, in degrees, normalized to [0, 360).
        double angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (angle < 0)
        {
            angle += 360.0;
        }

        if (angle >= 360.0)
        {
            angle -= 360.0;
        }

        // Rounding removes floating-point noise so that an exact boundary (22.5, 337.5, ...)
        // falls into the slot clockwise of it.
        double position = Math.Round((angle + SlotAngle / 2) / SlotAngle, 9);
        return (int)Math.Floor(position) % SlotCount;
    }

    /// <summary>
    /// Shortens the vector (dx, dy) to at most <paramref name="maxLength"/>, keeping its direction.
    /// Vectors already short enough are returned unchanged. Used so that moving the mouse far and then
    /// turning doesn't need a long movement back.
    /// </summary>
    public static (double X, double Y) ClampAim(double dx, double dy, double maxLength)
    {
        if (!(maxLength >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Maximum length must not be negative.");
        }

        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0 || length <= maxLength)
        {
            return (dx, dy);
        }

        double scale = maxLength / length;
        return (dx * scale, dy * scale);
    }

    /// <summary>
    /// Moves a desired wheel center so a circle of <paramref name="radius"/> fits fully inside the rectangle
    /// (left, top, right, bottom), e.g. a monitor's work area. If the rectangle is smaller than the circle on an axis,
    /// centers the circle on that axis. Any consistent unit.
    /// </summary>
    public static (double X, double Y) ClampCenter(
        double x, double y, double radius,
        double left, double top, double right, double bottom)
    {
        if (!(radius >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must not be negative.");
        }

        return (ClampAxis(x, radius, left, right), ClampAxis(y, radius, top, bottom));
    }

    private static double ClampAxis(double value, double radius, double min, double max)
    {
        if (max - min < 2 * radius)
        {
            return (min + max) / 2;
        }

        return Math.Clamp(value, min + radius, max - radius);
    }

    /// <summary>
    /// The center point of slot <paramref name="index"/> at distance <paramref name="radius"/> from the wheel center,
    /// relative to the wheel center. Slot 0 is straight up: (0, -radius). Clockwise.
    /// </summary>
    public static (double X, double Y) SlotCenter(int index, double radius)
    {
        ValidateIndex(index, nameof(index));

        double radians = index * SlotAngle * Math.PI / 180.0;
        return (radius * Math.Sin(radians), -radius * Math.Cos(radians));
    }

    /// <summary>
    /// The outline of slice <paramref name="index"/>: the part of the ring between InnerRadius and OuterRadius covering
    /// that slot's 45°, with half of GapWidth trimmed off each straight side so neighbouring slices have an even gap.
    /// Coordinates are for a canvas of size Diameter × Diameter whose center is (OuterRadius, OuterRadius).
    /// The returned geometry must be frozen.
    /// </summary>
    public static System.Windows.Media.Geometry CreateSliceGeometry(int index, WheelMetrics metrics)
    {
        ValidateIndex(index, nameof(index));
        ValidateMetrics(metrics);

        double center = metrics.OuterRadius; // canvas center C = (Outer, Outer)
        double halfGap = metrics.GapWidth / 2;
        double a0 = index * SlotAngle - SlotAngle / 2; // counter-clockwise boundary of the slot
        double a1 = index * SlotAngle + SlotAngle / 2; // clockwise boundary of the slot

        // The a0 side is shifted clockwise (+halfGap) and the a1 side counter-clockwise (-halfGap),
        // so both sides move into the slice and leave the gap between neighbours.
        var figure = new System.Windows.Media.PathFigure
        {
            StartPoint = BoundaryPoint(center, a0, metrics.OuterRadius, +halfGap),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new System.Windows.Media.ArcSegment(
            BoundaryPoint(center, a1, metrics.OuterRadius, -halfGap),
            new System.Windows.Size(metrics.OuterRadius, metrics.OuterRadius),
            0,
            false,
            System.Windows.Media.SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new System.Windows.Media.LineSegment(
            BoundaryPoint(center, a1, metrics.InnerRadius, -halfGap),
            true));
        figure.Segments.Add(new System.Windows.Media.ArcSegment(
            BoundaryPoint(center, a0, metrics.InnerRadius, +halfGap),
            new System.Windows.Size(metrics.InnerRadius, metrics.InnerRadius),
            0,
            false,
            System.Windows.Media.SweepDirection.Counterclockwise,
            true));

        var geometry = new System.Windows.Media.PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// A point on a circle of <paramref name="radius"/> where a radial line at <paramref name="angleDegrees"/>,
    /// shifted sideways by <paramref name="signedGap"/>, meets it. Relative to the canvas center.
    /// </summary>
    private static System.Windows.Point BoundaryPoint(double center, double angleDegrees, double radius, double signedGap)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        double sin = Math.Sin(radians);
        double cos = Math.Cos(radians);

        // Radial unit vector d = (sin, -cos); sideways unit vector n = (cos, sin), clockwise.
        double t = Math.Sqrt(radius * radius - signedGap * signedGap);
        return new System.Windows.Point(
            center + t * sin + signedGap * cos,
            center - t * cos + signedGap * sin);
    }

    private static void ValidateIconSize(double iconSize, string paramName)
    {
        if (!double.IsFinite(iconSize) || iconSize <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, iconSize, "Icon size must be a finite number greater than 0.");
        }
    }

    private static void ValidateIndex(int index, string paramName)
    {
        if ((uint)index >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(paramName, index, "Slot index must be from 0 to 7.");
        }
    }

    private static void ValidateMetrics(WheelMetrics metrics)
    {
        bool valid = double.IsFinite(metrics.OuterRadius)
            && double.IsFinite(metrics.InnerRadius)
            && double.IsFinite(metrics.GapWidth)
            && metrics.InnerRadius > 0
            && metrics.OuterRadius > metrics.InnerRadius
            && metrics.GapWidth >= 0
            && metrics.GapWidth / 2 < metrics.InnerRadius;
        if (!valid)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), metrics, "Metrics need 0 < InnerRadius < OuterRadius and a gap smaller than the inner radius.");
        }
    }

    /// <summary>
    /// Wraps a group index into 0..count-1 (e.g. -1 becomes count-1, count becomes 0).
    /// <paramref name="count"/> must be at least 1.
    /// </summary>
    public static int WrapIndex(int index, int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1.");
        }

        // Same result as ((index % count) + count) % count, without overflow for very large counts.
        int remainder = index % count;
        return remainder < 0 ? remainder + count : remainder;
    }
}
