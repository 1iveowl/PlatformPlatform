using System.Net;
using Account.Database;
using Account.Tests.Authentication;
using FluentAssertions;
using NSubstitute;
using SharedKernel.Tests;
using SharedKernel.Tests.Persistence;
using SharedKernel.Validation;
using Xunit;

namespace Account.Tests.Tenants;

[Collection(AntiforgeryValidationCollection.Name)]
public sealed class UpdateTenantLogoTests(ImageUploadWebApplicationFactory factory)
    : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<ImageUploadWebApplicationFactory>
{
    private const string UpdateLogoUrl = "/api/account/tenants/current/update-logo";
    private const int MaximumFileSizeInBytes = 2 * 1024 * 1024;

    [Theory]
    [InlineData("png", "image/png", "image/png", "png")]
    [InlineData("jpeg", "image/jpeg", "image/jpeg", "jpg")]
    [InlineData("gif", "image/gif", "image/gif", "gif")]
    [InlineData("webp", "image/webp", "image/webp", "webp")]
    [InlineData("gif", "image/png", "image/gif", "gif")]
    public async Task UpdateTenantLogo_WhenValidImage_ShouldStoreBlobWithContentTypeOfContent(string format, string declaredContentType, string storedContentType, string extension)
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm(CreateImage(format), declaredContentType, "logo.html");
        factory.BlobStorageClient.ClearReceivedCalls();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateLogoUrl, form, antiforgeryCookie, requestToken));

        // Assert
        await response.ShouldBeSuccessfulPostRequest(hasLocation: false);
        await factory.BlobStorageClient.Received(1).UploadAsync("logos", Arg.Is<string>(name => name.EndsWith($".{extension}")), storedContentType, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadTenantLogo().Should().Contain($".{extension}");
        TelemetryEventsCollectorSpy.CollectedEvents.Should().ContainSingle(e => e.GetType().Name == "TenantLogoUpdated");
    }

    [Fact]
    public async Task UpdateTenantLogo_WhenExactlyMaximumSize_ShouldSucceed()
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm(TestImages.Png(MaximumFileSizeInBytes), "image/png", "logo.png");
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateLogoUrl, form, antiforgeryCookie, requestToken));

        // Assert
        await response.ShouldBeSuccessfulPostRequest(hasLocation: false);
        await factory.BlobStorageClient.Received(1).UploadAsync("logos", Arg.Any<string>(), "image/png", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4 * 1024 * 1024, true)]
    [InlineData(4 * 1024 * 1024, false)]
    public async Task UpdateTenantLogo_WhenOverMaximumSize_ShouldReturnBadRequestWithoutStoring(int bytesOverLimit, bool sendAntiforgeryHeader)
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm(TestImages.Png(MaximumFileSizeInBytes + bytesOverLimit), "image/png", "logo.png");
        var logoBefore = ReadTenantLogo();
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateLogoUrl, form, antiforgeryCookie, sendAntiforgeryHeader ? requestToken : null));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadTenantLogo().Should().Be(logoBefore);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("truncated-png")]
    [InlineData("truncated-jpeg")]
    [InlineData("truncated-gif")]
    [InlineData("truncated-webp")]
    [InlineData("html")]
    [InlineData("gif-with-trailing-html")]
    [InlineData("webp-with-trailing-script")]
    public async Task UpdateTenantLogo_WhenContentIsNotValidImage_ShouldReturnBadRequestWithoutStoring(string content)
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm(CreateInvalidContent(content), "image/gif", "logo.gif");
        var logoBefore = ReadTenantLogo();
        factory.BlobStorageClient.ClearReceivedCalls();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateLogoUrl, form, antiforgeryCookie, requestToken));

        // Assert
        var expectedErrors = new[] { new ErrorDetail("fileStream", ImageContentInspector.InvalidImageMessage) };
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, expectedErrors);
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadTenantLogo().Should().Be(logoBefore);
        TelemetryEventsCollectorSpy.CollectedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateTenantLogo_WhenSvgContentType_ShouldReturnBadRequest()
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm("<svg"u8.ToArray(), "image/svg+xml", "logo.svg");
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateLogoUrl, form, antiforgeryCookie, requestToken));

        // Assert
        var expectedErrors = new[]
        {
            new ErrorDetail("contentType", "Image must be of type JPEG, PNG, GIF, or WebP."),
            new ErrorDetail("fileStream", ImageContentInspector.InvalidImageMessage)
        };
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, expectedErrors);
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTenantLogo_WhenMemberUser_ShouldReturnForbiddenWithoutStoring()
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedMemberHttpClient);
        var form = TestImages.CreateForm(TestImages.Png(), "image/png", "logo.png");
        var logoBefore = ReadTenantLogo();
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AuthenticatedMemberHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateLogoUrl, form, antiforgeryCookie, requestToken));

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.Forbidden, "Only owners are allowed to update tenant logo.");
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadTenantLogo().Should().Be(logoBefore);
    }

    [Theory]
    [InlineData("missing-header")]
    [InlineData("missing-cookie-and-header")]
    [InlineData("malformed-header")]
    [InlineData("other-identity")]
    public async Task UpdateTenantLogo_WhenAntiforgeryTokenIsNotValidForCaller_ShouldReturnBadRequestWithoutStoring(string tokenCase)
    {
        // Arrange
        var (ownerCookie, _) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var (memberCookie, memberToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedMemberHttpClient);
        var (cookie, requestToken) = tokenCase switch
        {
            "missing-header" => (ownerCookie, null),
            "missing-cookie-and-header" => (null, null),
            "malformed-header" => (ownerCookie, Faker.Random.AlphaNumeric(80)),
            _ => ((string?)memberCookie, (string?)memberToken)
        };
        var form = TestImages.CreateForm(TestImages.Png(), "image/png", "logo.png");
        var logoBefore = ReadTenantLogo();
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateLogoUrl, form, cookie, requestToken));

        // Assert
        await ImageUploadWebApplicationFactory.AssertAntiforgeryRejectedAsync(response);
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadTenantLogo().Should().Be(logoBefore);
    }

    [Fact]
    public async Task UpdateTenantLogo_WhenAnonymousWithValidAntiforgeryToken_ShouldReturnUnauthorized()
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AnonymousHttpClient);
        var form = TestImages.CreateForm(TestImages.Png(), "image/png", "logo.png");
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AnonymousHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateLogoUrl, form, antiforgeryCookie, requestToken));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    private static byte[] CreateImage(string format)
    {
        return format switch
        {
            "png" => TestImages.Png(),
            "jpeg" => TestImages.Jpeg(),
            "gif" => TestImages.Gif,
            "webp" => TestImages.WebP,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown image format.")
        };
    }

    private static byte[] CreateInvalidContent(string content)
    {
        return content switch
        {
            "empty" => [],
            "truncated-png" => TestImages.Truncated(TestImages.Png()),
            "truncated-jpeg" => TestImages.Truncated(TestImages.Jpeg()),
            "truncated-gif" => TestImages.Truncated(TestImages.Gif),
            "truncated-webp" => TestImages.Truncated(TestImages.WebP),
            "html" => "<!DOCTYPE html><html><body><script>alert(document.cookie)</script></body></html>"u8.ToArray(),
            "gif-with-trailing-html" => TestImages.WithTrailingBytes(TestImages.Gif, "<html><script>alert(1)</script></html>"),
            "webp-with-trailing-script" => TestImages.WithTrailingBytes(TestImages.WebP, "<script>alert(1)</script>"),
            _ => throw new ArgumentOutOfRangeException(nameof(content), content, "Unknown content.")
        };
    }

    private string ReadTenantLogo()
    {
        return Connection.ExecuteScalar<string>("SELECT logo FROM tenants WHERE id = @id", [new { id = DatabaseSeeder.Tenant1.Id.ToString() }]);
    }
}
