using System.Globalization;
using Account.Features.BackOffice.Dashboard.Queries;
using Account.Features.Subscriptions.Domain;
using Blazor.Client.BackOffice.Dashboard;
using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.BackOffice;

public sealed class DashboardChartSeriesTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    [Fact]
    public void FromTrends_WhenPriorPeriodHasFewerPoints_ShouldPairByPositionAndDrawZeroForTheMissingPriorDay()
    {
        // Arrange
        var response = new BackOfficeDashboardTrendsResponse(
            DashboardTrendMetric.NewTenants, DashboardTrendPeriod.Last7Days,
            [new BackOfficeDashboardTrendPoint(Day, 3), new BackOfficeDashboardTrendPoint(Day.AddDays(1), 5)],
            [new BackOfficeDashboardTrendPoint(Day.AddDays(-7), 2)]
        );

        // Act
        var rows = DashboardChartSeries.FromTrends(response);

        // Assert
        rows.Should().Equal(
            new DashboardTrendRow(Day, Day.AddDays(-7), 3, 2),
            new DashboardTrendRow(Day.AddDays(1), null, 5, 0)
        );
    }

    [Fact]
    public void FromMrrTrend_WhenGivenPoints_ShouldMapMonthlyRecurringRevenue()
    {
        // Arrange
        var response = new BackOfficeDashboardMrrTrendResponse(
            DashboardTrendPeriod.Last7Days, "usd",
            [new BackOfficeDashboardMrrTrendPoint(Day, 100.5m)],
            [new BackOfficeDashboardMrrTrendPoint(Day.AddDays(-7), 90m)]
        );

        // Act
        var rows = DashboardChartSeries.FromMrrTrend(response);

        // Assert
        rows.Should().Equal(new DashboardTrendRow(Day, Day.AddDays(-7), 100.5m, 90m));
    }

    [Fact]
    public void FromRevenueTrend_WhenGivenPoints_ShouldMapRevenue()
    {
        // Arrange
        var response = new BackOfficeDashboardRevenueTrendResponse(
            DashboardTrendPeriod.Last7Days, "usd",
            [new BackOfficeDashboardRevenueTrendPoint(Day, 40m)],
            [new BackOfficeDashboardRevenueTrendPoint(Day.AddDays(-7), 10m)]
        );

        // Act
        var rows = DashboardChartSeries.FromRevenueTrend(response);

        // Assert
        rows.Should().Equal(new DashboardTrendRow(Day, Day.AddDays(-7), 40m, 10m));
    }

    [Fact]
    public void FromTrends_WhenResponseIsMissing_ShouldReturnNoRows()
    {
        // Act
        var rows = DashboardChartSeries.FromTrends(null);

        // Assert
        rows.Should().BeEmpty();
    }

    [Fact]
    public void Summarise_WhenDeltaIsOverThePeriod_ShouldCompareTheLastDayWithTheFirst()
    {
        // Arrange
        DashboardTrendRow[] rows = [new(Day, Day.AddDays(-3), 200, 1), new(Day.AddDays(1), Day.AddDays(-2), 250, 2), new(Day.AddDays(2), Day.AddDays(-1), 300, 4)];

        // Act
        var summary = DashboardChartSeries.Summarise(rows, false);

        // Assert
        summary.Should().Be(new DashboardTrendSummary(750, 7, 250, 300, 100, 50m));
    }

    [Fact]
    public void Summarise_WhenDeltaIsAgainstThePriorPeriod_ShouldCompareTheLastDayWithThePriorPeriodsLastDay()
    {
        // Arrange
        DashboardTrendRow[] rows = [new(Day, Day.AddDays(-2), 10, 20), new(Day.AddDays(1), Day.AddDays(-1), 30, 40)];

        // Act
        var summary = DashboardChartSeries.Summarise(rows, true);

        // Assert
        summary.Gain.Should().Be(20);
        summary.DeltaPercent.Should().Be(-25m);
    }

    [Fact]
    public void Summarise_WhenTheBaseIsZero_ShouldHaveNoDeltaAndRoundTheAverageHalfUp()
    {
        // Arrange
        DashboardTrendRow[] rows = [new(Day, null, 0, 0), new(Day.AddDays(1), null, 3, 0)];

        // Act
        var summary = DashboardChartSeries.Summarise(rows, false);

        // Assert
        summary.AveragePerDay.Should().Be(2);
        summary.DeltaPercent.Should().BeNull();
    }

    [Fact]
    public void FromPlanDistribution_WhenGivenPlans_ShouldOrderThemPremiumStandardBasis()
    {
        // Arrange
        var response = new BackOfficeDashboardPlanDistributionResponse(
            6,
            [
                new BackOfficeDashboardPlanDistributionEntry(SubscriptionPlan.Basis, 3, 50),
                new BackOfficeDashboardPlanDistributionEntry(SubscriptionPlan.Premium, 1, 16.7),
                new BackOfficeDashboardPlanDistributionEntry(SubscriptionPlan.Standard, 2, 33.3)
            ]
        );

        // Act
        var shares = DashboardChartSeries.FromPlanDistribution(response);

        // Assert
        shares.Select(share => share.Plan).Should().Equal(SubscriptionPlan.Premium, SubscriptionPlan.Standard, SubscriptionPlan.Basis);
        shares[0].Should().Be(new DashboardPlanShare(SubscriptionPlan.Premium, 1, 16.7));
    }

    [Fact]
    public void GetAccountGrowthSubtitle_WhenGivenRows_ShouldStateTheSignupsAgainstThePriorPeriod()
    {
        // Arrange
        using var culture = new CultureScope("en-US");
        DashboardTrendRow[] rows = [new(Day, Day.AddDays(-2), 1200, 3), new(Day.AddDays(1), Day.AddDays(-1), 4, 2)];

        // Act
        var subtitle = DashboardChartSeries.GetAccountGrowthSubtitle(rows);

        // Assert
        subtitle.Should().Be("1,204 new signups · 5 prior period");
    }

    [Fact]
    public void GetUserLoginsSubtitle_WhenGivenRows_ShouldStateTheTotalAndTheRoundedAveragePerDay()
    {
        // Arrange
        using var culture = new CultureScope("da-DK");
        DashboardTrendRow[] rows = [new(Day, null, 3, 0), new(Day.AddDays(1), null, 4, 0)];

        // Act
        var subtitle = DashboardChartSeries.GetUserLoginsSubtitle(rows);

        // Assert
        subtitle.Should().Be(string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.LoginsTotalAndAverage, "7", "4"));
    }

    [Fact]
    public void GetMrrSubtitle_WhenGivenACurrency_ShouldStateTheLastDayAndTheChangeOverThePeriod()
    {
        // Arrange
        using var culture = new CultureScope("en-US");
        DashboardTrendRow[] rows = [new(Day, null, 100, 0), new(Day.AddDays(1), null, 125, 0)];

        // Act
        var subtitle = DashboardChartSeries.GetMrrSubtitle(rows, "usd");

        // Assert
        subtitle.Should().Be("USD 125.00 blended · +25.0% over period");
    }

    [Fact]
    public void GetMrrSubtitle_WhenTheFirstDayIsZero_ShouldLeaveOutTheDelta()
    {
        // Arrange
        using var culture = new CultureScope("en-US");
        DashboardTrendRow[] rows = [new(Day, null, 0, 0), new(Day.AddDays(1), null, 50, 0)];

        // Act
        var subtitle = DashboardChartSeries.GetMrrSubtitle(rows, "USD");

        // Assert
        subtitle.Should().Be("USD 50.00 blended");
    }

    [Fact]
    public void GetRevenueSubtitle_WhenGivenACurrency_ShouldStateTheGainAndTheLastDayAgainstThePriorPeriod()
    {
        // Arrange
        using var culture = new CultureScope("en-US");
        DashboardTrendRow[] rows = [new(Day, Day.AddDays(-2), 100, 80), new(Day.AddDays(1), Day.AddDays(-1), 150, 100)];

        // Act
        var subtitle = DashboardChartSeries.GetRevenueSubtitle(rows, "USD");

        // Assert
        subtitle.Should().Be("USD 50.00 this period · +50.0% vs prior period");
    }

    [Fact]
    public void GetRevenueSubtitle_WhenThePriorPeriodEndsAtZero_ShouldStateTheGainExcludingVat()
    {
        // Arrange
        using var culture = new CultureScope("en-US");
        DashboardTrendRow[] rows = [new(Day, null, 10, 0), new(Day.AddDays(1), null, 30, 0)];

        // Act
        var subtitle = DashboardChartSeries.GetRevenueSubtitle(rows, "USD");

        // Assert
        subtitle.Should().Be("USD 20.00 this period, excluding VAT");
    }

    [Fact]
    public void GetMoneySubtitles_WhenThereIsNoCurrency_ShouldHaveNoSubtitle()
    {
        // Arrange
        DashboardTrendRow[] rows = [new(Day, null, 10, 0)];

        // Act & Assert
        DashboardChartSeries.GetMrrSubtitle(rows, null).Should().BeNull();
        DashboardChartSeries.GetRevenueSubtitle(rows, " ").Should().BeNull();
    }

    [Theory]
    [InlineData("en-US", "Sep 27")]
    [InlineData("da-DK", "27. sep.")]
    public void FormatDate_WhenGivenADay_ShouldShowTheAbbreviatedMonthAndTheDayInTheCulture(string locale, string expected)
    {
        // Arrange
        using var culture = new CultureScope(locale);

        // Act
        var text = DashboardChartSeries.FormatDate(new DateOnly(2026, 9, 27));

        // Assert
        text.Should().Be(expected);
    }

    [Theory]
    [InlineData("en-US", 1234.5, "1,234.5")]
    [InlineData("da-DK", 2000, "2.000")]
    public void FormatValue_WhenGivenAValue_ShouldGroupDigitsAndKeepOnlyTheDecimalsItHas(string locale, double value, string expected)
    {
        // Arrange
        using var culture = new CultureScope(locale);

        // Act
        var text = DashboardChartSeries.FormatValue((decimal)value);

        // Assert
        text.Should().Be(expected);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string locale)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
