using System.Net;
using System.Text.Json;
using Account.Client;
using Account.Features.Tenants.Domain;
using Account.Features.Tenants.Requests;
using FluentAssertions;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using Xunit;
using ServerCommands = Account.Features.Tenants.Commands;

namespace Account.Tests.Client;

public sealed class TenantsClientTests
{
    [Fact]
    public async Task GetTenantsAsync_WhenCalled_ShouldGetTenantsRouteAndReadResponse()
    {
        // Arrange
        var userId = UserId.NewId();
        var body = $$"""{"tenants":[{"tenantId":"42","tenantName":"Acme","userId":"{{userId}}","logoUrl":null,"isNew":true}]}""";
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetTenantsAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/account/tenants");
        result.IsSuccess.Should().BeTrue();
        var tenant = result.Value!.Tenants.Should().ContainSingle().Subject;
        tenant.TenantId.Should().Be(new TenantId(42));
        tenant.UserId.Should().Be(userId);
        tenant.IsNew.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentTenantAsync_WhenCalled_ShouldGetCurrentRouteAndReadResponse()
    {
        // Arrange
        var body = """{"id":"42","createdAt":"2026-01-02T03:04:05+00:00","modifiedAt":null,"name":"Acme","state":"Suspended","suspensionReason":"PaymentFailed","logoUrl":null}""";
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetCurrentTenantAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/account/tenants/current");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(new TenantId(42));
        result.Value.State.Should().Be(TenantState.Suspended);
        result.Value.SuspensionReason.Should().Be(SuspensionReason.PaymentFailed);
    }

    [Fact]
    public async Task UpdateCurrentTenantAsync_WhenCalled_ShouldPutServerJson()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.UpdateCurrentTenantAsync(new UpdateCurrentTenantCommand { Name = "Acme" }, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.PathAndQuery.Should().Be("/api/account/tenants/current");
        request.Body.Should().Be(JsonSerializer.Serialize(new ServerCommands.UpdateCurrentTenantCommand { Name = "Acme" }, ApiJsonSerializerOptions.Create()));
        result.IsSuccess.Should().BeTrue();
    }
}
