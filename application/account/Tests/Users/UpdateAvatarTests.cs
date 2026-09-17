using System.Net;
using Account.Database;
using Account.Tests.Authentication;
using FluentAssertions;
using NSubstitute;
using SharedKernel.Tests;
using SharedKernel.Tests.Persistence;
using SharedKernel.Validation;
using Xunit;

namespace Account.Tests.Users;

[Collection(AntiforgeryValidationCollection.Name)]
public sealed class UpdateAvatarTests(ImageUploadWebApplicationFactory factory)
    : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<ImageUploadWebApplicationFactory>
{
    private const string UpdateAvatarUrl = "/api/account/users/me/update-avatar";
    private const int MaximumFileSizeInBytes = 1024 * 1024;

    [Theory]
    [InlineData("png", "image/png", "image/png", "png")]
    [InlineData("jpeg", "image/jpeg", "image/jpeg", "jpeg")]
    [InlineData("gif", "image/gif", "image/gif", "gif")]
    [InlineData("webp", "image/webp", "image/webp", "webp")]
    [InlineData("png", "image/jpeg", "image/png", "png")]
    public async Task UpdateAvatar_WhenValidImage_ShouldStoreBlobWithContentTypeOfContent(string format, string declaredContentType, string storedContentType, string extension)
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm(CreateImage(format), declaredContentType, "avatar.txt");
        factory.BlobStorageClient.ClearReceivedCalls();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateAvatarUrl, form, antiforgeryCookie, requestToken));

        // Assert
        await response.ShouldBeSuccessfulPostRequest(hasLocation: false);
        await factory.BlobStorageClient.Received(1).UploadAsync("avatars", Arg.Is<string>(name => name.EndsWith($".{extension}")), storedContentType, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadOwnerAvatar().Should().Contain($".{extension}");
        TelemetryEventsCollectorSpy.CollectedEvents.Should().ContainSingle(e => e.GetType().Name == "UserAvatarUpdated");
    }

    [Fact]
    public async Task UpdateAvatar_WhenExactlyMaximumSize_ShouldSucceed()
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm(TestImages.Png(MaximumFileSizeInBytes), "image/png", "avatar.png");
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateAvatarUrl, form, antiforgeryCookie, requestToken));

        // Assert
        await response.ShouldBeSuccessfulPostRequest(hasLocation: false);
        await factory.BlobStorageClient.Received(1).UploadAsync("avatars", Arg.Any<string>(), "image/png", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4 * 1024 * 1024, true)]
    [InlineData(4 * 1024 * 1024, false)]
    public async Task UpdateAvatar_WhenOverMaximumSize_ShouldReturnBadRequestWithoutStoring(int bytesOverLimit, bool sendAntiforgeryHeader)
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm(TestImages.Png(MaximumFileSizeInBytes + bytesOverLimit), "image/png", "avatar.png");
        var avatarBefore = ReadOwnerAvatar();
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateAvatarUrl, form, antiforgeryCookie, sendAntiforgeryHeader ? requestToken : null));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadOwnerAvatar().Should().Be(avatarBefore);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("truncated-png")]
    [InlineData("truncated-jpeg")]
    [InlineData("truncated-gif")]
    [InlineData("truncated-webp")]
    [InlineData("html")]
    [InlineData("svg")]
    [InlineData("png-with-trailing-html")]
    [InlineData("jpeg-with-trailing-script")]
    [InlineData("png-over-dimension-bound")]
    public async Task UpdateAvatar_WhenContentIsNotValidImage_ShouldReturnBadRequestWithoutStoring(string content)
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AuthenticatedOwnerHttpClient);
        var form = TestImages.CreateForm(CreateInvalidContent(content), "image/png", "avatar.png");
        var avatarBefore = ReadOwnerAvatar();
        factory.BlobStorageClient.ClearReceivedCalls();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateAvatarUrl, form, antiforgeryCookie, requestToken));

        // Assert
        var expectedErrors = new[] { new ErrorDetail("fileSteam", ImageContentInspector.InvalidImageMessage) };
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, expectedErrors);
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadOwnerAvatar().Should().Be(avatarBefore);
        TelemetryEventsCollectorSpy.CollectedEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing-header")]
    [InlineData("missing-cookie-and-header")]
    [InlineData("malformed-header")]
    [InlineData("other-identity")]
    public async Task UpdateAvatar_WhenAntiforgeryTokenIsNotValidForCaller_ShouldReturnBadRequestWithoutStoring(string tokenCase)
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
        var form = TestImages.CreateForm(TestImages.Png(), "image/png", "avatar.png");
        var avatarBefore = ReadOwnerAvatar();
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AuthenticatedOwnerHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateAvatarUrl, form, cookie, requestToken));

        // Assert
        await ImageUploadWebApplicationFactory.AssertAntiforgeryRejectedAsync(response);
        await factory.BlobStorageClient.DidNotReceive().UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        ReadOwnerAvatar().Should().Be(avatarBefore);
    }

    [Fact]
    public async Task UpdateAvatar_WhenAnonymousWithValidAntiforgeryToken_ShouldReturnUnauthorized()
    {
        // Arrange
        var (antiforgeryCookie, requestToken) = await ImageUploadWebApplicationFactory.GetAntiforgeryPairAsync(AnonymousHttpClient);
        var form = TestImages.CreateForm(TestImages.Png(), "image/png", "avatar.png");
        factory.BlobStorageClient.ClearReceivedCalls();

        // Act
        var response = await AnonymousHttpClient.SendAsync(ImageUploadWebApplicationFactory.CreateUploadRequest(UpdateAvatarUrl, form, antiforgeryCookie, requestToken));

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
            "html" => "<html><body><script>alert(document.cookie)</script></body></html>"u8.ToArray(),
            "svg" => "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray(),
            "png-with-trailing-html" => TestImages.WithTrailingBytes(TestImages.Png(), "<script>alert(1)</script>"),
            "jpeg-with-trailing-script" => TestImages.WithTrailingBytes(TestImages.Jpeg(), "<script>alert(1)</script>"),
            "png-over-dimension-bound" => TestImages.Png(width: ImageContentInspector.MaximumDimension + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(content), content, "Unknown content.")
        };
    }

    private string ReadOwnerAvatar()
    {
        return Connection.ExecuteScalar<string>("SELECT avatar FROM users WHERE id = @id", [new { id = DatabaseSeeder.Tenant1Owner.Id.ToString() }]);
    }
}
