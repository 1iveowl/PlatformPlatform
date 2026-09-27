using System.Globalization;
using Account.Features.BackOffice.Dashboard.Queries;
using Account.Features.Subscriptions.Domain;

namespace Blazor.Client.BackOffice.Dashboard;

// One day of a trend card: the day of the current period, the day at the same position in the prior period, and the two
// values. The prior day and value are missing when the prior period has fewer points, where the React cards draw zero.
public sealed record DashboardTrendRow(DateOnly Date, DateOnly? PriorDate, decimal Current, decimal Prior);

// The figures a trend card's subtitle states, as the React dashboard computes them. DeltaPercent is null where React shows
// no delta: when the base it divides by is zero.
public sealed record DashboardTrendSummary(
    decimal Total,
    decimal PriorTotal,
    decimal AveragePerDay,
    decimal Latest,
    decimal Gain,
    decimal? DeltaPercent
);

// One plan of the plan distribution card, in the React card's order (Premium, Standard, Basis)
public sealed record DashboardPlanShare(SubscriptionPlan Plan, long Count, double Percentage);

// The series mapping of the dashboard's five chart cards, from the account API's responses to the rows a card draws and
// lists as its text alternative. It mirrors the React cards: the current and prior periods are paired by position, and
// the summaries use the same arithmetic.
public static class DashboardChartSeries
{
    private static readonly SubscriptionPlan[] PlanOrder = [SubscriptionPlan.Premium, SubscriptionPlan.Standard, SubscriptionPlan.Basis];

    public static DashboardTrendRow[] FromTrends(BackOfficeDashboardTrendsResponse? response)
    {
        if (response is null) return [];
        return Pair(response.Points, response.PriorPoints, point => point.Date, point => point.Value);
    }

    public static DashboardTrendRow[] FromMrrTrend(BackOfficeDashboardMrrTrendResponse? response)
    {
        if (response is null) return [];
        return Pair(response.Points, response.PriorPoints, point => point.Date, point => point.MonthlyRecurringRevenue);
    }

    public static DashboardTrendRow[] FromRevenueTrend(BackOfficeDashboardRevenueTrendResponse? response)
    {
        if (response is null) return [];
        return Pair(response.Points, response.PriorPoints, point => point.Date, point => point.Revenue);
    }

    // Total and prior total are sums (account growth); the average per day is the total over the number of days rounded to
    // a whole number (user logins); latest, gain and the delta serve the cumulative money trends. The MRR card's delta is
    // the change from the first to the last day of the period; the revenue card's is the last day against the prior
    // period's last day, and its gain is the last day less the first.
    public static DashboardTrendSummary Summarise(DashboardTrendRow[] rows, bool isDeltaAgainstPriorPeriod)
    {
        var total = rows.Sum(row => row.Current);
        var priorTotal = rows.Sum(row => row.Prior);
        var average = rows.Length == 0 ? 0 : Math.Round(total / rows.Length, MidpointRounding.AwayFromZero);
        var latest = rows.Length == 0 ? 0 : rows[^1].Current;
        var first = rows.Length == 0 ? 0 : rows[0].Current;
        var priorLatest = rows.LastOrDefault(row => row.PriorDate is not null)?.Prior ?? 0;
        var deltaBase = isDeltaAgainstPriorPeriod ? priorLatest : first;
        decimal? delta = deltaBase == 0 ? null : (latest - deltaBase) / deltaBase * 100;
        return new DashboardTrendSummary(total, priorTotal, average, latest, latest - first, delta);
    }

    public static DashboardPlanShare[] FromPlanDistribution(BackOfficeDashboardPlanDistributionResponse? response)
    {
        if (response is null) return [];
        return response.Distribution
            .OrderBy(entry => Array.IndexOf(PlanOrder, entry.Plan))
            .Select(entry => new DashboardPlanShare(entry.Plan, entry.Count, entry.Percentage))
            .ToArray();
    }

    // The account growth card's subtitle: new signups in the period against the prior period's
    public static string GetAccountGrowthSubtitle(DashboardTrendRow[] rows)
    {
        var summary = Summarise(rows, false);
        return string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.NewSignupsAgainstPriorPeriod, FormatCount(summary.Total), FormatCount(summary.PriorTotal));
    }

    // The user logins card's subtitle: logins in the period and the whole-number average per day
    public static string GetUserLoginsSubtitle(DashboardTrendRow[] rows)
    {
        var summary = Summarise(rows, false);
        return string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.LoginsTotalAndAverage, FormatCount(summary.Total), FormatCount(summary.AveragePerDay));
    }

    // The MRR card's subtitle: the last day's MRR and its change from the first day; the delta is left out when the first
    // day is zero, and there is no subtitle without a currency
    public static string? GetMrrSubtitle(DashboardTrendRow[] rows, string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return null;

        var summary = Summarise(rows, false);
        var blended = DashboardFormat.FormatMoney(summary.Latest, currency);
        return summary.DeltaPercent is { } delta
            ? string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.MrrBlendedOverPeriod, blended, DashboardFormat.FormatDeltaPercent(delta))
            : string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.MrrBlended, blended);
    }

    // The revenue card's subtitle: the revenue gained over the period and the last day against the prior period's last day;
    // the delta is left out when the prior period ends at zero, and there is no subtitle without a currency
    public static string? GetRevenueSubtitle(DashboardTrendRow[] rows, string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return null;

        var summary = Summarise(rows, true);
        var gain = DashboardFormat.FormatMoney(summary.Gain, currency);
        return summary.DeltaPercent is { } delta
            ? string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.RevenueThisPeriodAgainstPriorPeriod, gain, DashboardFormat.FormatDeltaPercent(delta))
            : string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.RevenueThisPeriodExcludingVat, gain);
    }

    // A day as the React cards label one: the abbreviated month and the day in the current culture ("Sep 27", "27. sep.")
    public static string FormatDate(DateOnly date)
    {
        var culture = CultureInfo.CurrentCulture;
        return date.ToString(culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM"), culture);
    }

    // A value on the axis or in a data table without a currency: whole numbers with group separators, and up to two
    // decimals where the value has them
    public static string FormatValue(decimal value)
    {
        return value.ToString("#,##0.##", CultureInfo.CurrentCulture);
    }

    private static string FormatCount(decimal value)
    {
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }

    private static DashboardTrendRow[] Pair<TPoint>(TPoint[] points, TPoint[] priorPoints, Func<TPoint, DateOnly> date, Func<TPoint, decimal> value)
    {
        return points.Select((point, index) => index < priorPoints.Length
                ? new DashboardTrendRow(date(point), date(priorPoints[index]), value(point), value(priorPoints[index]))
                : new DashboardTrendRow(date(point), null, value(point), 0)
            )
            .ToArray();
    }
}
