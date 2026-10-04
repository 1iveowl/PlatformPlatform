using System.Net;
using System.Text;
using Account.Client;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Tenants.Domain;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Shared;
using Blazor.Client.Bootstrap;
using Blazor.Client.Forms;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.BackOffice;

// The account detail's tab model and its not-found state. The tab is the React back office's tab parameter with its values,
// Overview when absent or unknown, and for the billing tabs while the subscription setting is off. A tenant id the
// account API answers 404 for is the not-found state of the page's read (BackOfficeRead with not-found on 404); any other
// failure is the failed state.
public sealed class AccountDetailTests
{
    private const string AccountUrl = "https://back-office.dev.localhost:9001/blazor/back-office/accounts/42";

    [Theory]
    [InlineData("", AccountDetailTab.Overview)]
    [InlineData("?tab=overview", AccountDetailTab.Overview)]
    [InlineData("?tab=users", AccountDetailTab.Users)]
    [InlineData("?tab=feature-flags", AccountDetailTab.FeatureFlags)]
    [InlineData("?usersSearch=ann&tab=users&usersPageOffset=1", AccountDetailTab.Users)]
    [InlineData("?tab=invoices", AccountDetailTab.Invoices)]
    [InlineData("?tab=billing-events", AccountDetailTab.BillingEvents)]
    [InlineData("?invoicesPageOffset=2&tab=invoices", AccountDetailTab.Invoices)]
    [InlineData("?tab=Users", AccountDetailTab.Overview)]
    [InlineData("?tab=unknown", AccountDetailTab.Overview)]
    [InlineData("?tab=", AccountDetailTab.Overview)]
    [InlineData("?tab=users&tab=feature-flags", AccountDetailTab.FeatureFlags)]
    public void FromUri_ShouldReadTheTabParameterWithOverviewAsTheFallback(string query, AccountDetailTab expected)
    {
        // Act
        var tab = AccountDetailTabs.FromUri($"{AccountUrl}{query}", true);

        // Assert
        tab.Should().Be(expected);
    }

    [Theory]
    [InlineData("?tab=invoices")]
    [InlineData("?tab=billing-events")]
    public void FromUri_WhenTheSubscriptionSettingIsOff_ShouldFallBackToOverviewForTheBillingTabs(string query)
    {
        // Act
        var tab = AccountDetailTabs.FromUri($"{AccountUrl}{query}", false);

        // Assert
        tab.Should().Be(AccountDetailTab.Overview);
    }

    [Fact]
    public void Links_WhenTheSubscriptionSettingIsOff_ShouldOfferTheThreeTabsWithTheCurrentOneMarkedAndOverviewWithoutAParameter()
    {
        // Act
        var links = AccountDetailTabs.Links(new TenantId(42), AccountDetailTab.Users, false);

        // Assert
        links.Select(link => link.Href).Should().Equal(
            "/blazor/back-office/accounts/42", "/blazor/back-office/accounts/42?tab=users", "/blazor/back-office/accounts/42?tab=feature-flags"
        );
        links.Select(link => link.IsCurrent).Should().Equal(false, true, false);
        links.Select(link => link.TestId).Should().Equal("account-tab-overview", "account-tab-users", "account-tab-feature-flags");
    }

    [Fact]
    public void Links_WhenTheSubscriptionSettingIsOn_ShouldAddTheInvoicesAndBillingEventsTabsInTheReactOrder()
    {
        // Act
        var links = AccountDetailTabs.Links(new TenantId(42), AccountDetailTab.Invoices, true);

        // Assert
        links.Select(link => link.Href).Should().Equal(
            "/blazor/back-office/accounts/42", "/blazor/back-office/accounts/42?tab=users", "/blazor/back-office/accounts/42?tab=invoices",
            "/blazor/back-office/accounts/42?tab=billing-events", "/blazor/back-office/accounts/42?tab=feature-flags"
        );
        links.Select(link => link.IsCurrent).Should().Equal(false, false, true, false, false);
    }

    [Fact]
    public void Links_ShouldRoundTripThroughFromUri()
    {
        // Act
        var tabs = AccountDetailTabs.Links(new TenantId(42), AccountDetailTab.Overview, true).Select(link => AccountDetailTabs.FromUri($"https://back-office.dev.localhost:9001{link.Href}", true));

        // Assert
        tabs.Should().Equal(AccountDetailTabs.Tabs);
    }

    [Fact]
    public async Task Load_WhenTheAccountApiAnswers404_ShouldBeNotFound()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"Tenant with id '42' was not found."}""");
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var (read, result) = await LoadAccountAsync(backOfficeClient);

        // Assert
        network.Requests.Should().ContainSingle().Which.AbsolutePath.Should().Be("/api/back-office/tenants/42");
        result.Should().NotBeNull();
        read.Status.Should().Be(BackOfficeReadStatus.NotFound);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Load_WhenTheAccountApiAnswersAnotherFailure_ShouldBeFailed(HttpStatusCode statusCode)
    {
        // Arrange
        var network = new RecordingNetwork(statusCode, $$"""{"title":"Failed","status":{{(int)statusCode}}}""");
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var (read, _) = await LoadAccountAsync(backOfficeClient);

        // Assert
        read.Status.Should().Be(BackOfficeReadStatus.Failed);
    }

    [Fact]
    public async Task Load_WhenTheAccountIsReturned_ShouldBeLoadedWithTheAccountApiShape()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """
                                                              {"id":"42","name":"Acme","plan":"Standard","scheduledPlan":"Basis","scheduledPriceAmount":0,"cancelAtPeriodEnd":false,
                                                               "monthlyRecurringRevenue":29,"currency":"usd","renewalDate":"2026-10-01T00:00:00+00:00","subscribedSince":"2026-01-01T00:00:00+00:00",
                                                               "hasEverSubscribed":true,"billingName":"Acme Ltd","taxId":"DK12345678",
                                                               "billingAddress":{"line1":"Main Street 1","line2":null,"postalCode":"2100","city":"Copenhagen","state":null,"country":"DK"},
                                                               "paymentMethod":{"brand":"visa","last4":"4242","expMonth":4,"expYear":2031},"lifetimeValue":290,"state":"Active",
                                                               "suspensionReason":null,"suspendedAt":null,"logoUrl":null,"createdAt":"2025-12-24T10:00:00+00:00","modifiedAt":null,
                                                               "hasDriftDetected":true,"driftCheckedAt":null,
                                                               "driftDiscrepancies":[{"kind":"MissingEvent","description":"Missing","severity":"Critical","expectedEventType":null,"expectedValue":null,"actualValue":null,"occurredAt":null}],
                                                               "stripeCustomerUrl":null,"abInclusionPin":"NeverOn"}
                                                              """
        );
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var (read, _) = await LoadAccountAsync(backOfficeClient);

        // Assert
        read.Status.Should().Be(BackOfficeReadStatus.Loaded);
        var tenant = read.Value!;
        tenant.DriftDiscrepancies.Should().ContainSingle().Which.Severity.Should().Be(DriftSeverity.Critical);
        AccountFormat.GetStatus(tenant).Should().Be(TenantStatusFilter.Downgrading);
        AccountDetailFormat.GetAbInclusionPinLabel(tenant.AbInclusionPin).Should().Be(BackOfficeStrings.LastInRollouts);
        AccountDetailFormat.GetBillingAddressLines(tenant).Should().Equal("Main Street 1", "2100 Copenhagen");
        AccountDetailFormat.GetCountry(tenant).Should().Be("DK");
        AccountDetailFormat.GetPaymentMethod(tenant).Should().Be(new AccountPaymentMethod("Visa", "•••• 4242", "04/31"));
        AccountDetailFormat.IsFree(tenant).Should().BeFalse();
    }

    // The account detail's read as the page makes it: a 404 is the not-found state
    private static async Task<(BackOfficeRead<TenantDetailResponse> Read, ApiCallResult<TenantDetailResponse>? Result)> LoadAccountAsync(BackOfficeClient backOfficeClient)
    {
        var navigation = new TestNavigationManager();
        var presenter = new ApiFailurePresenter(new ToastService(), navigation, new AuthenticationNavigator(navigation));
        var read = new BackOfficeRead<TenantDetailResponse>(true);
        var result = await read.LoadAsync(cancellationToken => backOfficeClient.GetTenantAsync(new TenantId(42), cancellationToken), tenant => tenant, presenter);
        return (read, result);
    }

    [Fact]
    public void GetStatus_ShouldDeriveThePlannedChangeFromTheCancellationFlagBeforeTheScheduledPlan()
    {
        // Arrange
        var canceling = CreateTenant(SubscriptionPlan.Standard, SubscriptionPlan.Basis, true, true);
        var free = CreateTenant(SubscriptionPlan.Basis, null, false, false);
        var canceled = CreateTenant(SubscriptionPlan.Basis, null, false, true);

        // Act & Assert
        AccountFormat.GetStatus(canceling).Should().Be(TenantStatusFilter.Canceling);
        AccountFormat.GetMrr(canceling).Should().Be(new AccountMrr(AccountFormatMoney(29), AccountFormatMoney(0)));
        AccountFormat.GetStatus(free).Should().Be(TenantStatusFilter.Free);
        AccountDetailFormat.IsFree(free).Should().BeTrue();
        AccountFormat.GetStatus(canceled).Should().Be(TenantStatusFilter.Canceled);
        AccountFormat.GetRenewalLabel(AccountFormat.GetStatus(canceled)).Should().Be(BackOfficeStrings.Expired);
    }

    [Fact]
    public void GetPaymentMethod_WhenTheMethodIsLink_ShouldShowTheBrandOnly()
    {
        // Arrange
        var tenant = CreateTenant(SubscriptionPlan.Standard, null, false, true) with { PaymentMethod = new PaymentMethodResponse("link", "****", 0, 0) };

        // Act
        var paymentMethod = AccountDetailFormat.GetPaymentMethod(tenant);

        // Assert
        paymentMethod.Should().Be(new AccountPaymentMethod("Link", null, null));
    }

    [Theory]
    [InlineData(10, 6, 1, 3)]
    [InlineData(3, 2, 2, 0)]
    public void GetUserBreakdown_ShouldCountTheUsersNeitherActiveNorPendingAsInactive(int total, int active, int pending, int expectedInactive)
    {
        // Act
        var breakdown = AccountDetailFormat.GetUserBreakdown(new TenantUserCountsResponse(total, active, pending));

        // Assert
        breakdown.Should().Be(new AccountUserBreakdown(total, active, expectedInactive, pending));
    }

    [Theory]
    [InlineData(TenantState.Active, AbInclusionPin.AlwaysOn, false)]
    [InlineData(TenantState.Suspended, null, true)]
    public void GetStateLabel_ShouldShowOnlyForAnAccountThatIsNotActive(TenantState state, AbInclusionPin? pin, bool expectsLabel)
    {
        // Act
        var stateLabel = AccountDetailFormat.GetStateLabel(state);
        var pinLabel = AccountDetailFormat.GetAbInclusionPinLabel(pin);

        // Assert
        (stateLabel is not null).Should().Be(expectsLabel);
        pinLabel.Should().Be(pin is null ? null : BackOfficeStrings.FirstInRollouts);
    }

    private static string AccountFormatMoney(decimal amount)
    {
        return Blazor.Client.BackOffice.Dashboard.DashboardFormat.FormatMoney(amount, "usd");
    }

    private static TenantDetailResponse CreateTenant(SubscriptionPlan plan, SubscriptionPlan? scheduledPlan, bool cancelAtPeriodEnd, bool hasEverSubscribed)
    {
        return new TenantDetailResponse(new TenantId(42), "Acme", plan, scheduledPlan, null, cancelAtPeriodEnd, 29, "usd", null, hasEverSubscribed ? DateTimeOffset.UnixEpoch : null,
            hasEverSubscribed, null, null, null, null, null, TenantState.Active, null, null, null, DateTimeOffset.UnixEpoch, null, false, null, [], null, null
        );
    }

    private sealed class RecordingNetwork(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var mediaType = statusCode == HttpStatusCode.OK ? "application/json" : "application/problem+json";
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        }
    }
}
