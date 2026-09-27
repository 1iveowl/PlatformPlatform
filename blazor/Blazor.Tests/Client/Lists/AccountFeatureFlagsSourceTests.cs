using System.Net;
using System.Text;
using Account.Client;
using Account.Features.FeatureFlags.Domain;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;
using SharedKernel.Localization;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The account detail's feature flags: one read of GET /api/back-office/tenants/{id}/feature-flags, split into the account flags
// and the plan flags, each paged in memory for its own list. A flag is named by its resource, else the registry's label, else
// its key, and the wire value manual_override is read as a manual override.
public sealed class AccountFeatureFlagsSourceTests
{
    [Fact]
    public async Task GetGroup_ShouldSplitTheAccountApiResponseByRequiredPlan()
    {
        // Arrange
        var network = new RecordingNetwork("""
                                           {"flags":[
                                            {"flagKey":"beta-features","scope":"Tenant","description":"Early access","requiredPlan":null,"isAbTestEligible":true,"bucketStart":0,"bucketEnd":50,
                                             "rolloutPercentage":50,"isEnabled":true,"source":"manual_override","isBaseRowActive":true,"rolloutBucket":12,"inclusionThresholdPercentage":25,
                                             "defaultEnabled":false,"tenantAbInclusionPin":null},
                                            {"flagKey":"sso","scope":"Tenant","description":"Single sign-on","requiredPlan":"Premium","isAbTestEligible":false,"bucketStart":null,"bucketEnd":null,
                                             "rolloutPercentage":null,"isEnabled":false,"source":"plan","isBaseRowActive":true,"rolloutBucket":12,"inclusionThresholdPercentage":null,
                                             "defaultEnabled":false,"tenantAbInclusionPin":null}]}
                                           """
        );
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await backOfficeClient.GetTenantFeatureFlagsAsync(new TenantId(42), CancellationToken.None);

        // Assert
        network.Requests.Should().ContainSingle().Which.AbsolutePath.Should().Be("/api/back-office/tenants/42/feature-flags");
        var flags = result.Value!.Flags;
        var accountFlags = AccountFeatureFlagsSource.GetGroup(flags, AccountFeatureFlagGroup.Account);
        var planFlags = AccountFeatureFlagsSource.GetGroup(flags, AccountFeatureFlagGroup.Plan);
        accountFlags.Should().ContainSingle().Which.FlagKey.Should().Be("beta-features");
        planFlags.Should().ContainSingle().Which.FlagKey.Should().Be("sso");
        AccountFeatureFlagsSource.IsManualOverride(accountFlags[0]).Should().BeTrue();
        AccountFeatureFlagsSource.IsManualOverride(planFlags[0]).Should().BeFalse();
        AccountFeatureFlagsSource.ShowsInclusionColumn(accountFlags).Should().BeTrue();
        AccountFeatureFlagsSource.GetInclusionThreshold(planFlags[0]).Should().BeNull();
        AccountFeatureFlagsSource.GetStateLabel(planFlags[0]).Should().Be(BackOfficeStrings.Disabled);
    }

    [Fact]
    public void Page_ShouldReturnTheRequestedPageAndTheTotal()
    {
        // Arrange
        var flags = Enumerable.Range(0, 30).Select(index => CreateFlag($"flag-{index}", null)).ToArray();
        var request = new DataListRequest(new Dictionary<string, string>(), AccountFeatureFlagsSource.DefaultOrderBy, SortOrder.Ascending, 1, 25);

        // Act
        var result = AccountFeatureFlagsSource.Page(flags, request);

        // Assert
        result.Page!.TotalCount.Should().Be(30);
        result.Page.Items.Select(flag => flag.FlagKey).Should().Equal("flag-25", "flag-26", "flag-27", "flag-28", "flag-29");
    }

    [Fact]
    public void NameOf_ShouldFallBackToTheRegistryLabelAndThenToTheKey()
    {
        // Act & Assert
        AccountFeatureFlagsSource.NameOf(CreateFlag("sso", "Premium")).Should().Be("Single sign-on");
        AccountFeatureFlagsSource.NameOf(CreateFlag("not-in-the-registry", null)).Should().Be("not-in-the-registry");
        AccountFeatureFlagsSource.DescriptionOf(CreateFlag("not-in-the-registry", null) with { Description = " " }).Should().BeNull();
    }

    [Fact]
    public void ListId_ShouldBeDistinctPerAccountAndGroup()
    {
        // Act
        var ids = new[]
        {
            AccountFeatureFlagsSource.ListId(new TenantId(42), AccountFeatureFlagGroup.Account), AccountFeatureFlagsSource.ListId(new TenantId(42), AccountFeatureFlagGroup.Plan),
            AccountFeatureFlagsSource.ListId(new TenantId(43), AccountFeatureFlagGroup.Account)
        };

        // Assert
        ids.Should().OnlyHaveUniqueItems();
    }

    private static TenantFeatureFlagInfo CreateFlag(string flagKey, string? requiredPlan)
    {
        return new TenantFeatureFlagInfo(flagKey, FeatureFlagScope.Tenant, "Description", requiredPlan, false, null, null, null, true, FeatureFlagSource.Default, true, 1, null, true, null);
    }

    private sealed class RecordingNetwork(string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
