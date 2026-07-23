namespace Sirkyulo.Core;

/// <summary>A position on the ring, relative to the ring centre. Y grows downward (screen convention).</summary>
public readonly record struct RingPoint(double X, double Y);

public sealed record RingGeometry(double Radius, double Extent, IReadOnlyList<RingPoint> Points);

/// <summary>
/// Pure geometry for the radial menu. No UI types, no side effects — this is the one piece
/// of the app that is fully unit-testable, so it carries all the layout rules.
/// </summary>
public static class RingLayout
{
    /// <summary>Smallest ring we will draw. Below this the icons crowd the centre and look accidental.</summary>
    public const double MinRadius = 96d;

    /// <summary>
    /// Arc budget per icon, as a multiple of icon size. 1.35 leaves a comfortable gap at
    /// low counts and forces the radius to grow before icons can ever touch.
    /// </summary>
    public const double IconPitch = 1.35d;

    /// <summary>
    /// Slack beyond the outermost icon edge, for the hover scale-up and its drop shadow.
    ///
    /// Note there is deliberately no per-item label allowance. Real shortcut names
    /// ("DRAGON BALL Sparking! ZERO") are far wider than a 64px icon, so labels drawn under
    /// each item overlap their neighbours at every radius small enough to still read as a
    /// ring. The hovered item's name is drawn in the empty centre instead, which never
    /// collides and can show the name in full.
    /// </summary>
    public const double HoverMargin = 24d;

    /// <summary>
    /// Places <paramref name="count"/> items evenly around a circle, first item at 12 o'clock,
    /// proceeding clockwise.
    /// </summary>
    /// <param name="iconSize">Edge length of a single item's icon, in device-independent pixels.</param>
    public static RingGeometry Compute(int count, double iconSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iconSize);

        // Grow the radius so the circumference can seat every icon at its pitch.
        var required = count * iconSize * IconPitch / (2 * Math.PI);
        var radius = Math.Max(MinRadius, required);

        var points = new RingPoint[count];
        for (var i = 0; i < count; i++)
        {
            var angle = -Math.PI / 2 + i * (2 * Math.PI / count);
            points[i] = new RingPoint(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        // How far from centre the window must reach: ring + half an icon + hover slack.
        var extent = radius + iconSize / 2 + HoverMargin;
        return new RingGeometry(radius, extent, points);
    }
}
