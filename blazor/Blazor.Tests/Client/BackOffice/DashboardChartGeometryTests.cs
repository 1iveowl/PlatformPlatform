using System.Globalization;
using Blazor.Client.BackOffice.Dashboard;
using FluentAssertions;

namespace Blazor.Tests.Client.BackOffice;

public sealed class DashboardChartGeometryTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 5)]
    [InlineData(7, 10)]
    [InlineData(18, 20)]
    [InlineData(21, 25)]
    [InlineData(100, 100)]
    [InlineData(1234.5, 2000)]
    public void GetAxisMaximum_WhenGivenTheLargestValue_ShouldRoundUpToOneTwoTwoAndAHalfOrFive(double largest, double expected)
    {
        // Act
        var maximum = DashboardChartGeometry.GetAxisMaximum([0, (decimal)largest]);

        // Assert
        maximum.Should().Be((decimal)expected);
    }

    [Fact]
    public void GetGridLines_WhenGivenAnAxisMaximum_ShouldMarkFiveEvenlySpacedValuesFromTheAxisToTheTop()
    {
        // Act
        var lines = DashboardChartGeometry.GetGridLines(20);

        // Assert
        lines.Select(line => line.Value).Should().Equal(0, 5, 10, 15, 20);
        lines[0].Y.Should().Be(DashboardChartGeometry.PlotBottom);
        lines[^1].Y.Should().Be(DashboardChartGeometry.PlotTop);
    }

    [Fact]
    public void GetLinePath_WhenGivenThreeValues_ShouldSpreadThemAcrossThePlotWithInvariantNumbers()
    {
        // Arrange
        using var culture = new CultureScope("da-DK");

        // Act
        var path = DashboardChartGeometry.GetLinePath([0, 5, 10], 10);

        // Assert
        path.Should().Be("M0,200 L300,100 L600,0");
    }

    [Fact]
    public void GetAreaPath_WhenGivenValues_ShouldCloseTheLineDownToTheAxis()
    {
        // Act
        var path = DashboardChartGeometry.GetAreaPath([0, 10], 10);

        // Assert
        path.Should().Be("M0,200 L600,0 L600,200 L0,200 Z");
    }

    [Fact]
    public void GetLinePath_WhenThereAreNoValues_ShouldBeEmpty()
    {
        // Act
        var path = DashboardChartGeometry.GetLinePath([], 1);

        // Assert
        path.Should().BeEmpty();
    }

    [Fact]
    public void GetBars_WhenGivenTwoDays_ShouldPlaceThePriorBarLeftOfTheCurrentBarInEachSlot()
    {
        // Arrange
        DashboardTrendRow[] rows = [new(Day, Day.AddDays(-2), 10, 5), new(Day.AddDays(1), Day.AddDays(-1), 0, 10)];

        // Act
        var bars = DashboardChartGeometry.GetBars(rows, 10);

        // Assert
        bars.Should().HaveCount(2);
        bars[0].Prior.Should().Be(new DashboardChartBar(30, 100, 120, 100));
        bars[0].Current.Should().Be(new DashboardChartBar(150, 0, 120, 200));
        bars[1].Current.Height.Should().Be(0);
        bars[1].Prior.X.Should().BeGreaterThan(bars[0].Current.X);
    }

    [Fact]
    public void GetDonutSlices_WhenGivenThreeValues_ShouldDrawOneSlicePerNonZeroValueInOrder()
    {
        // Act
        var slices = DashboardChartGeometry.GetDonutSlices([1, 0, 3]);

        // Assert
        slices[0].Should().StartWith("M113.54,24.01 A88,88 0 0 1 ");
        slices[1].Should().BeEmpty();
        slices[2].Should().Contain(" 0 1 1 ").And.EndWith(" Z");
    }

    [Fact]
    public void GetDonutSlices_WhenOnlyOneValueIsAboveZero_ShouldDrawAFullRing()
    {
        // Act
        var slices = DashboardChartGeometry.GetDonutSlices([0, 4]);

        // Assert
        slices[0].Should().BeEmpty();
        slices[1].Should().Be("M112,24 A88,88 0 1 1 112,200 A88,88 0 1 1 112,24 Z M112,56 A56,56 0 1 0 112,168 A56,56 0 1 0 112,56 Z");
    }

    [Fact]
    public void GetDonutSlices_WhenEveryValueIsZero_ShouldDrawNothing()
    {
        // Act
        var slices = DashboardChartGeometry.GetDonutSlices([0, 0]);

        // Assert
        slices.Should().OnlyContain(slice => slice == "");
    }

    [Theory]
    [InlineData(0, new int[0])]
    [InlineData(1, new[] { 0 })]
    [InlineData(2, new[] { 0, 1 })]
    [InlineData(7, new[] { 0, 3, 6 })]
    [InlineData(30, new[] { 0, 15, 29 })]
    public void GetDateTickIndexes_WhenGivenDays_ShouldLabelTheFirstMiddleAndLastDay(int count, int[] expected)
    {
        // Act & Assert
        DashboardChartGeometry.GetDateTickIndexes(count).Should().Equal(expected);
    }

    [Fact]
    public void ToAttribute_WhenTheCultureUsesADecimalComma_ShouldWriteADecimalPoint()
    {
        // Arrange
        using var culture = new CultureScope("da-DK");

        // Act & Assert
        DashboardChartGeometry.ToAttribute(12.5).Should().Be("12.5");
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

        public CultureScope(string locale)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
        }
    }
}
