using System.Net;
using System.Net.Http.Json;
using Account.Features.BackOffice.Dashboard.Queries;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.Users.BackOffice.Queries;
using Account.Integrations.OAuth.Mock;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Authentication.BackOfficeIdentity;
using SharedKernel.Authentication.MockEasyAuth;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class ExternalLoginAttributionTests : ExternalAuthenticationTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CompleteExternalLogin_WhenResolvedByIdentity_ShouldAttributeHistoryToResolvedUser(bool noEmail, bool sameEmailAcrossTenants)
    {
        // Arrange
        var otherUser = DatabaseSeeder.Tenant1Owner;
        Connection.Update("users", "id", otherUser.Id.ToString(), [("email", MockOAuthProvider.MockEmail)]);
        var tenantId = InsertTenant();
        var storedEmail = sameEmailAcrossTenants ? MockOAuthProvider.MockEmail : Faker.Internet.Email().ToLowerInvariant();
        var userId = InsertUser(storedEmail, tenantId);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenantId);
        var (callbackUrl, cookies) = await StartLoginFlow(preferredTenantId: tenantId);
        var loginId = GetExternalLoginIdFromUrl(callbackUrl);
        using var reportingClient = CreateReportingClient();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var callback = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: noEmail ? MockOAuthProvider.NoEmailValue : "true");

        // Assert
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().Be("/");
        Connection.ExecuteScalar<string>("SELECT user_id FROM external_logins WHERE id = @id", [new { id = loginId }]).Should().Be(userId.ToString());
        Connection.ExecuteScalar<long>("SELECT tenant_id FROM external_logins WHERE id = @id", [new { id = loginId }]).Should().Be(tenantId.Value);
        Connection.ExecuteScalar<string>("SELECT email FROM users WHERE id = @id", [new { id = userId.ToString() }]).Should().Be(storedEmail);
        Connection.ExecuteScalar<string>("SELECT email FROM external_logins WHERE id = @id", [new { id = loginId }]).Should().Be(noEmail ? null : MockOAuthProvider.MockEmail);
        var history = await reportingClient.GetFromJsonAsync<BackOfficeUserLoginHistoryResponse>($"/api/back-office/users/{userId}/login-history");
        history!.Entries.Should().ContainSingle(e => e.Kind == LoginEventKind.External && e.Outcome == LoginEventOutcome.Succeeded);
        var otherHistory = await reportingClient.GetFromJsonAsync<BackOfficeUserLoginHistoryResponse>($"/api/back-office/users/{otherUser.Id}/login-history");
        otherHistory!.Entries.Should().NotContain(e => e.Kind == LoginEventKind.External);
        var dashboard = await reportingClient.GetFromJsonAsync<BackOfficeDashboardRecentLoginsResponse>("/api/back-office/dashboard/recent-logins?Limit=50");
        var entry = dashboard!.Logins.Should().ContainSingle().Subject;
        entry.UserId.Should().Be(userId);
        entry.TenantId.Should().Be(tenantId);
        entry.Email.Should().Be(noEmail ? storedEmail : MockOAuthProvider.MockEmail);
    }

    private HttpClient CreateReportingClient()
    {
        var client = ((TestServer)WebApplicationServices.GetRequiredService<IServer>()).CreateClient();
        client.BaseAddress = new Uri("https://back-office.test.localhost");
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        client.DefaultRequestHeaders.Add(BackOfficeIdentityDefaults.PrincipalNameHeader, identity.Name);
        client.DefaultRequestHeaders.Add(BackOfficeIdentityDefaults.PrincipalIdHeader, identity.ObjectId);
        client.DefaultRequestHeaders.Add(BackOfficeIdentityDefaults.PrincipalPayloadHeader, MockEasyAuthCookie.EncodePayload(identity));
        return client;
    }
}
