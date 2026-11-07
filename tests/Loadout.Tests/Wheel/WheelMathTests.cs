using Loadout.Wheel.Geometry;

namespace Loadout.Tests.Wheel;

public class WheelMathTests
{
    private const double Precision = 6;

    /// <summary>Direction vector for a clockwise-from-up angle, as the mouse would move it.</summary>
    private static (double Dx, double Dy) Aim(double degrees, double length)
    {
        double radians = degrees * Math.PI / 180.0;
        return (length * Math.Sin(radians), -length * Math.Cos(radians));
    }

    // ---- Measure ----

    [Fact]
    public void Measure_At48_MatchesSpec()
    {
        var m = WheelMath.Measure(48);

        Assert.Equal(48, m.IconSize, Precision);
        Assert.Equal(132, m.OuterRadius, Precision);
        Assert.Equal(60, m.InnerRadius, Precision);
        Assert.Equal(96, m.IconRingRadius, Precision);
        Assert.Equal(24, m.CancelZoneRadius, Precision);
        Assert.Equal(3, m.GapWidth, Precision);
        Assert.Equal(264, m.Diameter, Precision);
    }

    [Fact]
    public void Measure_At16_CancelZoneHasMinimumOf12()
    {
        var m = WheelMath.Measure(16);

        Assert.Equal(44, m.OuterRadius, Precision);
        Assert.Equal(20, m.InnerRadius, Precision);
        Assert.Equal(32, m.IconRingRadius, Precision);
        Assert.Equal(12, m.CancelZoneRadius, Precision);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-48.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Measure_InvalidSize_Throws(double iconSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMath.Measure(iconSize));
    }

    // ---- SlotFromAim ----

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(45.0, 1)]
    [InlineData(90.0, 2)]
    [InlineData(135.0, 3)]
    [InlineData(180.0, 4)]
    [InlineData(225.0, 5)]
    [InlineData(270.0, 6)]
    [InlineData(315.0, 7)]
    public void SlotFromAim_EightDirections_MapToSlots(double degrees, int expectedSlot)
    {
        var (dx, dy) = Aim(degrees, 100);

        Assert.Equal<int?>(expectedSlot, WheelMath.SlotFromAim(dx, dy, 24));
    }

    [Fact]
    public void SlotFromAim_UpIsStraightUpScreenVector()
    {
        Assert.Equal<int?>(0, WheelMath.SlotFromAim(0, -100, 24));
        Assert.Equal<int?>(2, WheelMath.SlotFromAim(100, 0, 24));
        Assert.Equal<int?>(4, WheelMath.SlotFromAim(0, 100, 24));
        Assert.Equal<int?>(6, WheelMath.SlotFromAim(-100, 0, 24));
    }

    [Fact]
    public void SlotFromAim_InsideCancelZone_ReturnsNull()
    {
        var (dx, dy) = Aim(90, 23.9);

        Assert.Null(WheelMath.SlotFromAim(dx, dy, 24));
        Assert.Null(WheelMath.SlotFromAim(0, 0, 24));
    }

    [Fact]
    public void SlotFromAim_ExactlyOnCancelZoneEdge_IsNotCancelled()
    {
        Assert.Equal<int?>(0, WheelMath.SlotFromAim(0, -24, 24));
    }

    [Fact]
    public void SlotFromAim_ExactlyTwentyTwoPointFiveDegrees_GivesSlot1()
    {
        var (dx, dy) = Aim(22.5, 100);

        Assert.Equal<int?>(1, WheelMath.SlotFromAim(dx, dy, 24));
    }

    [Fact]
    public void SlotFromAim_ExactlyMinusTwentyTwoPointFiveDegrees_GivesSlot0()
    {
        var (dx, dy) = Aim(337.5, 100);

        Assert.Equal<int?>(0, WheelMath.SlotFromAim(dx, dy, 24));
    }

    [Fact]
    public void SlotFromAim_JustBeforeMinusTwentyTwoPointFive_GivesSlot7()
    {
        var (dx, dy) = Aim(337.4, 100);

        Assert.Equal<int?>(7, WheelMath.SlotFromAim(dx, dy, 24));
    }

    [Fact]
    public void SlotFromAim_NaN_ReturnsNull()
    {
        Assert.Null(WheelMath.SlotFromAim(double.NaN, 10, 24));
        Assert.Null(WheelMath.SlotFromAim(10, double.NaN, 24));
    }

    [Fact]
    public void SlotFromAim_Infinite_ReturnsNull()
    {
        Assert.Null(WheelMath.SlotFromAim(double.PositiveInfinity, 10, 24));
        Assert.Null(WheelMath.SlotFromAim(10, double.NegativeInfinity, 24));
    }

    [Fact]
    public void SlotFromAim_NegativeCancelZone_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMath.SlotFromAim(0, -100, -1));
    }

    // ---- ClampAim ----

    [Fact]
    public void ClampAim_ShortVector_IsUnchanged()
    {
        Assert.Equal((3.0, 4.0), WheelMath.ClampAim(3, 4, 10));
        Assert.Equal((3.0, 4.0), WheelMath.ClampAim(3, 4, 5));
    }

    [Fact]
    public void ClampAim_LongVector_IsScaledKeepingDirection()
    {
        var (x, y) = WheelMath.ClampAim(30, 40, 10);

        Assert.Equal(6, x, Precision);
        Assert.Equal(8, y, Precision);
        Assert.Equal(10, Math.Sqrt(x * x + y * y), Precision);
    }

    [Fact]
    public void ClampAim_ZeroVector_IsUnchanged()
    {
        Assert.Equal((0.0, 0.0), WheelMath.ClampAim(0, 0, 10));
        Assert.Equal((0.0, 0.0), WheelMath.ClampAim(0, 0, 0));
    }

    [Fact]
    public void ClampAim_NegativeMaxLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMath.ClampAim(3, 4, -1));
    }

    // ---- ClampCenter ----

    [Fact]
    public void ClampCenter_InsideRectangle_IsUnchanged()
    {
        Assert.Equal((500.0, 400.0), WheelMath.ClampCenter(500, 400, 50, 0, 0, 1000, 800));
    }

    [Fact]
    public void ClampCenter_NearLeftEdge_NudgedInward()
    {
        Assert.Equal((50.0, 400.0), WheelMath.ClampCenter(10, 400, 50, 0, 0, 1000, 800));
    }

    [Fact]
    public void ClampCenter_NearRightEdge_NudgedInward()
    {
        Assert.Equal((950.0, 400.0), WheelMath.ClampCenter(990, 400, 50, 0, 0, 1000, 800));
    }

    [Fact]
    public void ClampCenter_NearTopEdge_NudgedInward()
    {
        Assert.Equal((500.0, 50.0), WheelMath.ClampCenter(500, 10, 50, 0, 0, 1000, 800));
    }

    [Fact]
    public void ClampCenter_NearBottomEdge_NudgedInward()
    {
        Assert.Equal((500.0, 750.0), WheelMath.ClampCenter(500, 790, 50, 0, 0, 1000, 800));
    }

    [Fact]
    public void ClampCenter_RectangleSmallerThanCircle_CentersOnThatAxis()
    {
        // Width 80 is less than 2 × 50, so x is centered. Height 800 is enough, so y is clamped.
        Assert.Equal((40.0, 400.0), WheelMath.ClampCenter(10, 400, 50, 0, 0, 80, 800));
    }

    [Fact]
    public void ClampCenter_RectangleSmallerThanCircleOnBothAxes_CentersBoth()
    {
        Assert.Equal((40.0, 40.0), WheelMath.ClampCenter(10, 10, 50, 0, 0, 80, 80));
    }

    [Fact]
    public void ClampCenter_NegativeRadius_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMath.ClampCenter(0, 0, -1, 0, 0, 100, 100));
    }

    // ---- SlotCenter ----

    [Theory]
    [InlineData(0, 0.0, -100.0)]
    [InlineData(2, 100.0, 0.0)]
    [InlineData(4, 0.0, 100.0)]
    [InlineData(6, -100.0, 0.0)]
    public void SlotCenter_CardinalSlots_AreOnTheRightSide(int index, double expectedX, double expectedY)
    {
        var (x, y) = WheelMath.SlotCenter(index, 100);

        Assert.Equal(expectedX, x, Precision);
        Assert.Equal(expectedY, y, Precision);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void SlotCenter_InvalidIndex_Throws(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMath.SlotCenter(index, 100));
    }

    // ---- WrapIndex ----

    [Fact]
    public void WrapIndex_Negative_WrapsToLast()
    {
        Assert.Equal(7, WheelMath.WrapIndex(-1, 8));
        Assert.Equal(7, WheelMath.WrapIndex(-9, 8));
    }

    [Fact]
    public void WrapIndex_Count_WrapsToZero()
    {
        Assert.Equal(0, WheelMath.WrapIndex(8, 8));
    }

    [Fact]
    public void WrapIndex_LargeIndex_WrapsIntoRange()
    {
        Assert.Equal(1, WheelMath.WrapIndex(17, 8));
    }

    [Fact]
    public void WrapIndex_ZeroCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMath.WrapIndex(0, 0));
    }

    // ---- CreateSliceGeometry ----

    private static readonly WheelMetrics Metrics48 = WheelMath.Measure(48);

    private static System.Windows.Point Center => new(Metrics48.OuterRadius, Metrics48.OuterRadius);

    private static System.Windows.Point IconCenter(int slot)
    {
        var (x, y) = WheelMath.SlotCenter(slot, Metrics48.IconRingRadius);
        return new System.Windows.Point(Center.X + x, Center.Y + y);
    }

    private static System.Windows.Point OnBoundary(int slot, double radius)
    {
        double a0 = slot * WheelMath.SlotAngle - WheelMath.SlotAngle / 2;
        var (dx, dy) = Aim(a0, radius);
        return new System.Windows.Point(Center.X + dx, Center.Y + dy);
    }

    [Fact]
    public void CreateSliceGeometry_IsFrozen()
    {
        var geometry = WheelMath.CreateSliceGeometry(0, Metrics48);

        Assert.True(geometry.IsFrozen);
    }

    [Fact]
    public void CreateSliceGeometry_BoundsStayWithinDiameter()
    {
        for (int i = 0; i < WheelMath.SlotCount; i++)
        {
            var bounds = WheelMath.CreateSliceGeometry(i, Metrics48).Bounds;

            Assert.True(bounds.Left >= -1e-6, $"slot {i} left {bounds.Left}");
            Assert.True(bounds.Top >= -1e-6, $"slot {i} top {bounds.Top}");
            Assert.True(bounds.Right <= Metrics48.Diameter + 1e-6, $"slot {i} right {bounds.Right}");
            Assert.True(bounds.Bottom <= Metrics48.Diameter + 1e-6, $"slot {i} bottom {bounds.Bottom}");
        }
    }

    [Fact]
    public void CreateSliceGeometry_ContainsOwnIconCenter()
    {
        for (int i = 0; i < WheelMath.SlotCount; i++)
        {
            var geometry = WheelMath.CreateSliceGeometry(i, Metrics48);

            Assert.True(geometry.FillContains(IconCenter(i)), $"slot {i} should contain its icon");
        }
    }

    [Fact]
    public void CreateSliceGeometry_DoesNotContainCanvasCenterOrNeighbourIcon()
    {
        for (int i = 0; i < WheelMath.SlotCount; i++)
        {
            var geometry = WheelMath.CreateSliceGeometry(i, Metrics48);

            Assert.False(geometry.FillContains(Center), $"slot {i} must not contain the center");
            Assert.False(geometry.FillContains(IconCenter((i + 1) % WheelMath.SlotCount)), $"slot {i} must not contain slot {(i + 1) % 8}");
            Assert.False(geometry.FillContains(IconCenter((i + 7) % WheelMath.SlotCount)), $"slot {i} must not contain slot {(i + 7) % 8}");
        }
    }

    [Fact]
    public void CreateSliceGeometry_PointOnBoundaryLine_IsInTheGap()
    {
        var geometry = WheelMath.CreateSliceGeometry(3, Metrics48);
        int slot = 3;

        // Exactly on the first boundary line, and on the second, halfway between inner and outer edge.
        Assert.False(geometry.FillContains(OnBoundary(slot, Metrics48.IconRingRadius)));
        Assert.False(geometry.FillContains(OnBoundary(slot + 1, Metrics48.IconRingRadius)));
    }

    [Fact]
    public void CreateSliceGeometry_InvalidIndex_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMath.CreateSliceGeometry(8, Metrics48));
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelMath.CreateSliceGeometry(-1, Metrics48));
    }
}
