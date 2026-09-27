using System.Globalization;
using Account.Features.BackOffice.Dashboard.Queries;
using Account.Features.Subscriptions.Domain;
using Blazor.Client.BackOffice.Dashboard;
using FluentAssertions;

namespace Blazor.Tests.Client.BackOffice;

public sealed class DashboardFormatTests
{
    private static readonly BackOfficeDashboardKpisResponse Kpis = new(
        DashboardTrendPeriod.Last7Days, 1234, 10, 2, 1, 5, null, 400, 37, 1234.5m, 12.34m, 98765.4m, "usd", 9
    );

    [Fact]
    public void CreateTiles_WhenSubscriptionIsEnabled_ShouldShowTheFiveTilesInTheReactOrderWithTheirLinks()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var tiles = DashboardFormat.CreateTiles(Kpis, DashboardTrendPeriod.Last7Days, true);

        // Assert
        tiles.Select(tile => (tile.TestId, tile.Label, tile.Value, tile.Subtitle, tile.Href)).Should().Equal(
            ("kpi-total-accounts", "Total accounts", "1,234", "+5 new in last 7 days", "/blazor/back-office/accounts"),
            ("kpi-blended-mrr", "Blended MRR", "USD 1,234.50", "vs prior period", "/blazor/back-office/accounts?statuses=Active&statuses=Downgrading"),
            ("kpi-total-revenue", "Total revenue", "USD 98,765.40", "All-time, excluding VAT", "/blazor/back-office/invoices"),
            ("kpi-users-active", "Users active", "37", "Last 7 days", "/blazor/back-office/users"),
            ("kpi-active-sessions", "Active sessions", "9", "Last 24 hours", null)
        );
        tiles[1].Delta.Should().Be("+12.3%");
        tiles[1].DeltaTone.Should().Be(DashboardTone.Positive);
    }

    [Fact]
    public void CreateTiles_WhenSubscriptionIsDisabled_ShouldLeaveOutTheRevenueTiles()
    {
        // Act
        var tiles = DashboardFormat.CreateTiles(Kpis, DashboardTrendPeriod.Last30Days, false);

        // Assert
        tiles.Select(tile => tile.TestId).Should().Equal("kpi-total-accounts", "kpi-users-active", "kpi-active-sessions");
    }

    [Fact]
    public void CreateTiles_WhileLoading_ShouldHaveNoValuesAndKeepThePeriodInTheSubtitle()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var tiles = DashboardFormat.CreateTiles(null, DashboardTrendPeriod.Last90Days, true);

        // Assert
        tiles.Should().OnlyContain(tile => tile.Value == null);
        tiles.Single(tile => tile.TestId == "kpi-users-active").Subtitle.Should().Be("Last 90 days");
        tiles.Single(tile => tile.TestId == "kpi-blended-mrr").Delta.Should().BeNull();
    }

    [Fact]
    public void CreateTiles_InDanish_ShouldUseTheDanishTextsAndNumberFormat()
    {
        // Arrange
        using var culture = new CultureScope("da-DK");

        // Act
        var tiles = DashboardFormat.CreateTiles(Kpis, DashboardTrendPeriod.Last7Days, true);

        // Assert
        tiles[0].Should().Be(new DashboardTile("kpi-total-accounts", "Konti i alt", "1.234", "+5 nye sidste 7 dage", "/blazor/back-office/accounts"));
        tiles[1].Value.Should().Be("1.234,50 USD");
        tiles[1].Delta.Should().Be("+12,3%");
    }

    [Theory]
    [InlineData(12.34, "+12.3%", DashboardTone.Positive)]
    [InlineData(-3, "-3.0%", DashboardTone.Negative)]
    [InlineData(0, "0.0%", DashboardTone.Neutral)]
    public void FormatDeltaPercent_ShouldShowOneDecimalWithASignUnlessZero(decimal value, string expected, DashboardTone tone)
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var text = DashboardFormat.FormatDeltaPercent(value);

        // Assert
        text.Should().Be(expected);
        DashboardFormat.GetTone(value).Should().Be(tone);
    }

    [Theory]
    [InlineData("en-US", -3, "-USD 3.00")]
    [InlineData("da-DK", -1234.5, "-1.234,50 USD")]
    public void FormatMoney_WhenNegative_ShouldPutTheSignFirst(string locale, decimal amount, string expected)
    {
        // Arrange
        using var culture = new CultureScope(locale);

        // Act
        var text = DashboardFormat.FormatMoney(amount, "usd");

        // Assert
        text.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FormatMoney_WithoutACurrency_ShouldShowMissing(string? currency)
    {
        // Act
        var text = DashboardFormat.FormatMoney(10m, currency);

        // Assert
        text.Should().Be("-");
    }

    [Theory]
    [InlineData("Ada", "Lovelace", "Ada Lovelace")]
    [InlineData(" Ada ", null, "Ada")]
    [InlineData(null, " ", "ada@example.com")]
    public void GetPersonName_ShouldPreferTheNameOverTheEmail(string? firstName, string? lastName, string expected)
    {
        // Act
        var name = DashboardFormat.GetPersonName(firstName, lastName, "ada@example.com");

        // Assert
        name.Should().Be(expected);
    }

    [Fact]
    public void Labels_ShouldCoverEveryPlanPaymentStatusAndBillingEventTypeInBothCultures()
    {
        foreach (var locale in new[] { "en-US", "da-DK" })
        {
            // Arrange
            using var culture = new CultureScope(locale);

            // Act
            var labels = Enum.GetValues<SubscriptionPlan>().Select(plan => (plan.ToString(), DashboardFormat.GetPlanLabel(plan)))
                .Concat(Enum.GetValues<PaymentTransactionStatus>().Select(status => (status.ToString(), DashboardFormat.GetPaymentStatusLabel(status))))
                .Concat(Enum.GetValues<BillingEventType>().Select(type => (type.ToString(), DashboardFormat.GetBillingEventTypeLabel(type))))
                .ToArray();

            // Assert
            labels.Should().OnlyContain(label => !string.IsNullOrWhiteSpace(label.Item2));
            labels.Where(label => label.Item1.StartsWith("Subscription", StringComparison.Ordinal) || label.Item1 == nameof(BillingEventType.NoOp))
                .Should().OnlyContain(label => label.Item1 != label.Item2);
        }
    }

    [Fact]
    public void Labels_ShouldUseTheReactWording()
    {
        // Arrange
        using var culture = new CultureScope("da-DK");

        // Act & Assert
        DashboardFormat.GetPaymentStatusLabel(PaymentTransactionStatus.Succeeded).Should().Be("Betalt");
        DashboardFormat.GetBillingEventTypeLabel(BillingEventType.SubscriptionCreated).Should().Be("Tilmeldt");
        DashboardFormat.GetBillingEventTypeLabel(BillingEventType.NoOp).Should().Be("Ingen ændring");
        DashboardFormat.GetPaymentStatusTone(PaymentTransactionStatus.Failed).Should().Be(DashboardTone.Negative);
    }

    [Fact]
    public void FormatToday_ShouldShowTheWeekdayAndTheDayWithoutTheYear()
    {
        // Arrange
        using var english = new CultureScope("en-US");

        // Act
        var text = DashboardFormat.FormatToday(new DateTime(2026, 9, 27));

        // Assert
        text.Should().Be("Sunday, September 27");
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
