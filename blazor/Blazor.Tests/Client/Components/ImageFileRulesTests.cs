using Account.Client;
using Account.Features.Tenants.Requests;
using Account.Features.Users.Requests;
using Blazor.Client.Components.Images;
using Blazor.Client.Profile;
using Blazor.Client.Settings;
using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Components;

public sealed class ImageFileRulesTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    public void Validate_WhenTypeIsAllowedAndSizeIsAtTheLimit_ShouldAccept(string contentType)
    {
        // Act
        var message = AvatarFileRules.Rules.Validate(UpdateAvatarCommand.MaximumFileSizeInBytes, contentType);

        // Assert
        message.Should().BeNull();
    }

    [Fact]
    public void Validate_WhenSizeIsOneByteOverTheLimit_ShouldReturnTheSizeMessage()
    {
        // Act
        var message = AvatarFileRules.Rules.Validate(UpdateAvatarCommand.MaximumFileSizeInBytes + 1, "image/png");

        // Assert
        message.Should().Be(AccountStrings.AvatarTooLarge);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("IMAGE/PNG")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_WhenTypeIsNotAllowed_ShouldReturnTheTypeMessageEvenWhenTooLarge(string? contentType)
    {
        // Act
        var message = AvatarFileRules.Rules.Validate(UpdateAvatarCommand.MaximumFileSizeInBytes * 4, contentType);

        // Assert
        message.Should().Be(CommonStrings.ImageTypeNotSupported);
    }

    [Fact]
    public void Accept_ShouldListTheFourAllowedTypes()
    {
        // Assert
        AvatarFileRules.Rules.Accept.Should().Be("image/jpeg,image/png,image/gif,image/webp");
        TenantLogoFileRules.Rules.Accept.Should().Be("image/jpeg,image/png,image/gif,image/webp");
    }

    [Fact]
    public void Rules_ShouldCarryTheLimitOfTheEndpointTheyGuard()
    {
        // Assert
        AvatarFileRules.Rules.MaximumFileSizeInBytes.Should().Be(UpdateAvatarCommand.MaximumFileSizeInBytes);
        TenantLogoFileRules.Rules.MaximumFileSizeInBytes.Should().Be(UpdateTenantLogoCommand.MaximumFileSizeInBytes);
    }

    [Fact]
    public void Validate_WhenTheFileIsBetweenTheAvatarAndTheLogoLimit_ShouldOnlyRefuseItForTheAvatar()
    {
        // Arrange
        const long size = UpdateAvatarCommand.MaximumFileSizeInBytes + 1;

        // Act and Assert
        AvatarFileRules.Rules.Validate(size, "image/png").Should().Be(AccountStrings.AvatarTooLarge);
        TenantLogoFileRules.Rules.Validate(size, "image/png").Should().BeNull();
        TenantLogoFileRules.Rules.Validate(UpdateTenantLogoCommand.MaximumFileSizeInBytes + 1, "image/png").Should().Be(AccountStrings.LogoTooLarge);
    }

    [Theory]
    [InlineData(413, "Payload Too Large", null)]
    [InlineData(400, "Bad Request", null)]
    [InlineData(400, null, null)]
    public void IsTooLargeResponse_WhenTheEndpointRejectsTheRequestWithoutABody_ShouldBeTrue(int statusCode, string? title, string? detail)
    {
        // Arrange
        var result = ApiCallResult.Failed(ApiCallOutcome.Failure, new ApiCallProblem(statusCode, title, detail, NoErrors, null));

        // Act and Assert
        ImageFileRules.IsTooLargeResponse(result).Should().BeTrue();
    }

    [Fact]
    public void IsTooLargeResponse_WhenTheResponseCarriesAMessageErrorsOrAnAntiforgeryRejection_ShouldBeFalse()
    {
        // Arrange
        var withDetail = ApiCallResult.Failed(ApiCallOutcome.Failure, new ApiCallProblem(400, "Bad Request", "Something specific.", NoErrors, null));
        var withErrors = ApiCallResult.Failed(ApiCallOutcome.ValidationFailure, new ApiCallProblem(400, "Bad Request", null, new Dictionary<string, string[]> { ["fileStream"] = ["Image must be a valid JPEG, PNG, GIF, or WebP file."] }, null));
        var antiforgery = ApiCallResult.Failed(ApiCallOutcome.Failure, new ApiCallProblem(400, "Invalid Antiforgery Token", null, NoErrors, null));
        var serverError = ApiCallResult.Failed(ApiCallOutcome.Failure, new ApiCallProblem(500, "Internal Server Error", null, NoErrors, null));

        // Act and Assert
        ImageFileRules.IsTooLargeResponse(withDetail).Should().BeFalse();
        ImageFileRules.IsTooLargeResponse(withErrors).Should().BeFalse();
        ImageFileRules.IsTooLargeResponse(antiforgery).Should().BeFalse();
        ImageFileRules.IsTooLargeResponse(serverError).Should().BeFalse();
        ImageFileRules.IsTooLargeResponse(ApiCallResult.Success()).Should().BeFalse();
    }
}
