using System.Net;
using System.Text;
using System.Text.Json;
using Account.Client;
using Account.Features.Tenants.Domain;
using Account.Features.Tenants.Requests;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
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

    [Fact]
    public async Task UpdateLogoAsync_WhenCalled_ShouldPostOneMultipartFileNamedFileWithItsContentType()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(handler));
        var fileBytes = "logo-image-bytes"u8.ToArray();

        // Act
        var result = await client.UpdateLogoAsync(new UpdateTenantLogoCommand(new MemoryStream(fileBytes), "image/png"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/tenants/current/update-logo");
        var contentType = MediaTypeHeaderValue.Parse(request.ContentType);
        contentType.MediaType.Value.Should().Be("multipart/form-data");
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value!;
        var reader = new MultipartReader(boundary, new MemoryStream(Encoding.UTF8.GetBytes(request.Body!)));
        var section = await reader.ReadNextSectionAsync();
        section.Should().NotBeNull();
        var disposition = section.GetContentDispositionHeader()!;
        disposition.IsFileDisposition().Should().BeTrue();
        disposition.Name.Value.Should().Be("file");
        disposition.FileName.Value.Should().Be("logo");
        section.ContentType.Should().Be("image/png");
        using var sectionContent = new MemoryStream();
        await section.Body.CopyToAsync(sectionContent);
        sectionContent.ToArray().Should().Equal(fileBytes);
        (await reader.ReadNextSectionAsync()).Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateLogoAsync_WhenSentThroughTheHeaderHandlers_ShouldCarryTheAntiforgeryTokenAndLocale()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var chain = new AntiforgeryHeaderHandler(new FixedAntiforgeryTokenSource("antiforgery-token")) { InnerHandler = new LocaleHeaderHandler(() => "da-DK") { InnerHandler = handler } };
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(chain));

        // Act
        await client.UpdateLogoAsync(new UpdateTenantLogoCommand(new MemoryStream([1, 2, 3]), "image/jpeg"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Headers[AccountApiHeaders.AntiforgeryToken].Should().Equal("antiforgery-token");
        request.Headers[AccountApiHeaders.Locale].Should().Equal("da-DK");
    }

    [Fact]
    public async Task UpdateLogoAsync_WhenValidationFails_ShouldReturnTheFieldErrorsAsReturned()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.BadRequest, """{"title":"Bad Request","status":400,"errors":{"fileStream":["Image must be a valid JPEG, PNG, GIF, or WebP file."]}}""", "application/problem+json");
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.UpdateLogoAsync(new UpdateTenantLogoCommand(new MemoryStream([1]), "image/png"), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.ValidationFailure);
        result.Problem!.Errors["fileStream"].Should().Equal("Image must be a valid JPEG, PNG, GIF, or WebP file.");
    }

    [Fact]
    public async Task RemoveLogoAsync_WhenCalled_ShouldDeleteRemoveLogoRouteWithoutBody()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.RemoveLogoAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Delete);
        request.PathAndQuery.Should().Be("/api/account/tenants/current/remove-logo");
        request.Body.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void UpdateTenantLogoCommand_ShouldHaveTheServerSizeLimitAndContentTypes()
    {
        // Act and Assert
        UpdateTenantLogoCommand.MaximumFileSizeInBytes.Should().Be(ServerCommands.UpdateTenantLogoCommand.MaximumFileSizeInBytes);
        UpdateTenantLogoCommand.AllowedContentTypes.Should().Equal("image/jpeg", "image/png", "image/gif", "image/webp");
    }

    private sealed class FixedAntiforgeryTokenSource(string antiforgeryToken) : IAntiforgeryTokenSource
    {
        public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<string?>(antiforgeryToken);
        }
    }
}
