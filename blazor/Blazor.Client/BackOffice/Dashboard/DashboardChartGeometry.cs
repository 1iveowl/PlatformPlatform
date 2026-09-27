using System.Globalization;

namespace Blazor.Client.BackOffice.Dashboard;

// One bar of a grouped bar chart, in the chart's view box
public sealed record DashboardChartBar(double X, double Y, double Width, double Height);

// One horizontal grid line of a trend chart with the value it marks
public sealed record DashboardChartGridLine(double Y, decimal Value);

// The geometry of the dashboard's in-house SVG charts. Everything is computed here as SVG path data and element attributes
// in a fixed view box, so the markup carries no style attribute: sizes, colours and fonts are classes in app.css. A trend
// chart's view box is its plot alone and stretches to the card's width at a fixed height, so its strokes keep their width
// with vector-effect and its labels are HTML beside it; the donut keeps its aspect. Numbers are written with the invariant
// culture, as SVG requires.
public static class DashboardChartGeometry
{
    public const double Width = 600;
    public const double Height = 200;
    public const double PlotTop = 0;
    public const double PlotBottom = Height;
    public const double PlotLeft = 0;
    public const double PlotRight = Width;
    public const double DonutSize = 224;
    public const double DonutOuterRadius = 88;
    public const double DonutInnerRadius = 56;

    private const int GridIntervals = 4;
    private const double BarGroupFill = 0.8;
    private const double DonutPaddingDegrees = 2;

    public static string ViewBox => Format($"0 0 {Width} {Height}");

    public static string DonutViewBox => Format($"0 0 {DonutSize} {DonutSize}");

    // The top of the value axis: the smallest 1, 2, 2.5 or 5 times a power of ten that the largest value fits under, so the
    // four grid intervals fall on round numbers; 1 when every value is zero or below
    public static decimal GetAxisMaximum(IEnumerable<decimal> values)
    {
        var largest = values.DefaultIfEmpty(0).Max();
        if (largest <= 0) return 1;

        var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)largest)));
        foreach (var step in new[] { 1m, 2m, 2.5m, 5m, 10m })
        {
            if (largest <= step * magnitude) return step * magnitude;
        }

        return 10 * magnitude;
    }

    public static DashboardChartGridLine[] GetGridLines(decimal axisMaximum)
    {
        return Enumerable.Range(0, GridIntervals + 1)
            .Select(index => new DashboardChartGridLine(ToY(axisMaximum * index / GridIntervals, axisMaximum), axisMaximum * index / GridIntervals))
            .ToArray();
    }

    // The line through the values, one point per day spread evenly across the plot; empty for no values
    public static string GetLinePath(decimal[] values, decimal axisMaximum)
    {
        if (values.Length == 0) return "";
        return string.Join(" ", values.Select((value, index) => Format($"{(index == 0 ? "M" : "L")}{ToX(index, values.Length)},{ToY(value, axisMaximum)}")));
    }

    // The line closed down to the axis, filled below the line
    public static string GetAreaPath(decimal[] values, decimal axisMaximum)
    {
        if (values.Length == 0) return "";
        return GetLinePath(values, axisMaximum) + Format($" L{ToX(values.Length - 1, values.Length)},{PlotBottom} L{ToX(0, values.Length)},{PlotBottom} Z");
    }

    // Two bars per day, prior on the left and current on the right, sharing the day's slot
    public static (DashboardChartBar Prior, DashboardChartBar Current)[] GetBars(DashboardTrendRow[] rows, decimal axisMaximum)
    {
        if (rows.Length == 0) return [];

        var slot = (PlotRight - PlotLeft) / rows.Length;
        var barWidth = slot * BarGroupFill / 2;
        return rows.Select((row, index) =>
                {
                    var left = PlotLeft + slot * index + slot * (1 - BarGroupFill) / 2;
                    return (ToBar(left, barWidth, row.Prior, axisMaximum), ToBar(left + barWidth, barWidth, row.Current, axisMaximum));
                }
            )
            .ToArray();
    }

    // One donut slice per value in the order given, starting at twelve o'clock and running clockwise, with a small gap
    // between slices; a single slice is a full ring, and a zero value has no slice
    public static string[] GetDonutSlices(long[] values)
    {
        var total = values.Sum();
        if (total <= 0) return values.Select(_ => "").ToArray();

        var nonZero = values.Count(value => value > 0);
        var padding = nonZero > 1 ? DonutPaddingDegrees : 0;
        var start = 0d;
        var slices = new string[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] <= 0)
            {
                slices[index] = "";
                continue;
            }

            var sweep = 360d * values[index] / total;
            slices[index] = nonZero == 1 ? GetRingPath() : GetSlicePath(start + padding / 2, start + sweep - padding / 2);
            start += sweep;
        }

        return slices;
    }

    // The rows whose days label the plot: the first, the middle and the last, or every row when there are fewer than three
    public static int[] GetDateTickIndexes(int count)
    {
        return count switch
        {
            0 => [],
            1 => [0],
            2 => [0, 1],
            _ => [0, count / 2, count - 1]
        };
    }

    // A number as an SVG attribute value
    public static string ToAttribute(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static DashboardChartBar ToBar(double left, double width, decimal value, decimal axisMaximum)
    {
        var top = ToY(value, axisMaximum);
        return new DashboardChartBar(Math.Round(left, 2), top, Math.Round(width, 2), Math.Round(PlotBottom - top, 2));
    }

    private static double ToX(int index, int count)
    {
        return count == 1 ? (PlotLeft + PlotRight) / 2 : Math.Round(PlotLeft + (PlotRight - PlotLeft) * index / (count - 1), 2);
    }

    private static double ToY(decimal value, decimal axisMaximum)
    {
        var clamped = Math.Max(0, Math.Min((double)value, (double)axisMaximum));
        return Math.Round(PlotBottom - (PlotBottom - PlotTop) * clamped / (double)axisMaximum, 2);
    }

    private static string GetSlicePath(double startDegrees, double endDegrees)
    {
        var largeArc = endDegrees - startDegrees > 180 ? 1 : 0;
        var (outerStartX, outerStartY) = ToPoint(DonutOuterRadius, startDegrees);
        var (outerEndX, outerEndY) = ToPoint(DonutOuterRadius, endDegrees);
        var (innerEndX, innerEndY) = ToPoint(DonutInnerRadius, endDegrees);
        var (innerStartX, innerStartY) = ToPoint(DonutInnerRadius, startDegrees);
        return Format(
            $"M{outerStartX},{outerStartY} A{DonutOuterRadius},{DonutOuterRadius} 0 {largeArc} 1 {outerEndX},{outerEndY} L{innerEndX},{innerEndY} A{DonutInnerRadius},{DonutInnerRadius} 0 {largeArc} 0 {innerStartX},{innerStartY} Z"
        );
    }

    // A full ring as two half arcs per radius, since one arc cannot start and end on the same point; even-odd filling
    // leaves the inside open
    private static string GetRingPath()
    {
        const double center = DonutSize / 2;
        return Format(
            $"M{center},{center - DonutOuterRadius} A{DonutOuterRadius},{DonutOuterRadius} 0 1 1 {center},{center + DonutOuterRadius} A{DonutOuterRadius},{DonutOuterRadius} 0 1 1 {center},{center - DonutOuterRadius} Z M{center},{center - DonutInnerRadius} A{DonutInnerRadius},{DonutInnerRadius} 0 1 0 {center},{center + DonutInnerRadius} A{DonutInnerRadius},{DonutInnerRadius} 0 1 0 {center},{center - DonutInnerRadius} Z"
        );
    }

    private static (double X, double Y) ToPoint(double radius, double degrees)
    {
        const double center = DonutSize / 2;
        var radians = (degrees - 90) * Math.PI / 180;
        return (Math.Round(center + radius * Math.Cos(radians), 2), Math.Round(center + radius * Math.Sin(radians), 2));
    }

    private static string Format(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }
}
