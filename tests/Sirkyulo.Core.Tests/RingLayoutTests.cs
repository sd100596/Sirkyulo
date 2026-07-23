using Sirkyulo.Core;

namespace Sirkyulo.Core.Tests;

public class RingLayoutTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void FirstItemSitsAtTwelveOClock()
    {
        var ring = RingLayout.Compute(5, 64);

        Assert.Equal(0d, ring.Points[0].X, Tolerance);
        Assert.Equal(-ring.Radius, ring.Points[0].Y, Tolerance);
    }

    [Fact]
    public void SecondItemGoesClockwise()
    {
        // Clockwise on screen means +X and still above centre for the second of four.
        var ring = RingLayout.Compute(4, 64);

        Assert.Equal(ring.Radius, ring.Points[1].X, Tolerance);
        Assert.Equal(0d, ring.Points[1].Y, Tolerance);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(40)]
    public void EveryItemLandsOnTheCircle(int count)
    {
        var ring = RingLayout.Compute(count, 64);

        Assert.Equal(count, ring.Points.Count);
        foreach (var p in ring.Points)
            Assert.Equal(ring.Radius, Math.Sqrt(p.X * p.X + p.Y * p.Y), 1e-6);
    }

    [Fact]
    public void ItemsAreEvenlySpaced()
    {
        var ring = RingLayout.Compute(8, 64);
        var expected = 2 * Math.PI / 8;

        for (var i = 1; i < ring.Points.Count; i++)
        {
            var a = ring.Points[i - 1];
            var b = ring.Points[i];
            var angle = Math.Acos((a.X * b.X + a.Y * b.Y) / (ring.Radius * ring.Radius));
            Assert.Equal(expected, angle, 1e-6);
        }
    }

    [Fact]
    public void SmallGroupsUseTheMinimumRadius()
    {
        // Three 64px icons fit comfortably; the ring should not shrink to hug them.
        Assert.Equal(RingLayout.MinRadius, RingLayout.Compute(3, 64).Radius, Tolerance);
    }

    [Fact]
    public void RadiusGrowsWithCountSoIconsNeverCollide()
    {
        double previous = 0;
        for (var n = 1; n <= 60; n++)
        {
            var ring = RingLayout.Compute(n, 64);
            Assert.True(ring.Radius >= previous, $"radius shrank at n={n}");
            previous = ring.Radius;

            if (n < 2) continue;

            // Chord between neighbours must clear the icon's own width.
            var a = ring.Points[0];
            var b = ring.Points[1];
            var chord = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
            Assert.True(chord >= 64, $"icons overlap at n={n}: chord {chord:F1}");
        }
    }

    [Fact]
    public void ExtentLeavesRoomForTheIconAndItsHoverGrowth()
    {
        var ring = RingLayout.Compute(6, 64);
        Assert.Equal(ring.Radius + 32 + RingLayout.HoverMargin, ring.Extent, Tolerance);
    }

    [Fact]
    public void CentreStaysClearForTheHoverLabel()
    {
        // The hovered item's name is drawn in the middle, so the hole inside the ring must
        // stay wide enough to hold a realistic shortcut name.
        var ring = RingLayout.Compute(8, 64);
        var clearDiameter = (ring.Radius - 32) * 2;

        Assert.True(clearDiameter >= 128, $"centre hole only {clearDiameter:F0}px wide");
    }

    [Fact]
    public void EmptyRingIsLegal()
    {
        var ring = RingLayout.Compute(0, 64);
        Assert.Empty(ring.Points);
        Assert.Equal(RingLayout.MinRadius, ring.Radius, Tolerance);
    }

    [Fact]
    public void RejectsNonsenseInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RingLayout.Compute(-1, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => RingLayout.Compute(5, 0));
    }
}
