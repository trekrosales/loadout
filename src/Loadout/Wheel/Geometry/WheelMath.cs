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
    public static WheelMetrics Measure(double iconSize) => throw new NotImplementedException();

    /// <summary>
    /// Maps total mouse movement since the wheel opened to a slot.
    /// Returns null (Cancel) while the movement length is less than <paramref name="cancelZoneRadius"/>.
    /// Otherwise returns 0-7: slot 0 is straight up (dx = 0, dy &lt; 0) and covers -22.5° to +22.5° around up;
    /// slots continue clockwise (slot 2 = right, slot 4 = down, slot 6 = left).
    /// A direction exactly on a boundary belongs to the slot clockwise of it (e.g. exactly 22.5° gives slot 1).
    /// </summary>
    public static int? SlotFromAim(double dx, double dy, double cancelZoneRadius) => throw new NotImplementedException();

    /// <summary>
    /// Shortens the vector (dx, dy) to at most <paramref name="maxLength"/>, keeping its direction.
    /// Vectors already short enough are returned unchanged. Used so that moving the mouse far and then
    /// turning doesn't need a long movement back.
    /// </summary>
    public static (double X, double Y) ClampAim(double dx, double dy, double maxLength) => throw new NotImplementedException();

    /// <summary>
    /// Moves a desired wheel center so a circle of <paramref name="radius"/> fits fully inside the rectangle
    /// (left, top, right, bottom), e.g. a monitor's work area. If the rectangle is smaller than the circle on an axis,
    /// centers the circle on that axis. Any consistent unit.
    /// </summary>
    public static (double X, double Y) ClampCenter(
        double x, double y, double radius,
        double left, double top, double right, double bottom) => throw new NotImplementedException();

    /// <summary>
    /// The center point of slot <paramref name="index"/> at distance <paramref name="radius"/> from the wheel center,
    /// relative to the wheel center. Slot 0 is straight up: (0, -radius). Clockwise.
    /// </summary>
    public static (double X, double Y) SlotCenter(int index, double radius) => throw new NotImplementedException();

    /// <summary>
    /// The outline of slice <paramref name="index"/>: the part of the ring between InnerRadius and OuterRadius covering
    /// that slot's 45°, with half of GapWidth trimmed off each straight side so neighbouring slices have an even gap.
    /// Coordinates are for a canvas of size Diameter × Diameter whose center is (OuterRadius, OuterRadius).
    /// The returned geometry must be frozen.
    /// </summary>
    public static System.Windows.Media.Geometry CreateSliceGeometry(int index, WheelMetrics metrics) => throw new NotImplementedException();

    /// <summary>
    /// Wraps a group index into 0..count-1 (e.g. -1 becomes count-1, count becomes 0).
    /// <paramref name="count"/> must be at least 1.
    /// </summary>
    public static int WrapIndex(int index, int count) => throw new NotImplementedException();
}
