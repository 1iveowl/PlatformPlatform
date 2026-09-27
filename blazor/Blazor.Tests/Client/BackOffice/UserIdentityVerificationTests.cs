using System.Globalization;
using Account.Client;
using Account.Features.BackOffice.Queries;
using Account.Features.ExternalAuthentication.BackOffice.Queries;
using Account.Features.ExternalAuthentication.Domain;
using Blazor.Client.BackOffice.Users;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.BackOffice;

// The decisions of the user detail's Identity tab: the state it renders from the account API's answer, with a failed read
// never shown as not verified; the revoke offered only to an identity in the admins group and only for a verification the tab
// shows; the revoke's answer mapped to its outcome, 403, 404 and a network failure included; and the texts it shows
public sealed class UserIdentityVerificationTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private static readonly BackOfficeUserIdentityVerificationResponse Verified = new(
        true, ExternalProviderType.MitId, IdentityAssuranceLevel.Substantial, new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 20, 9, 59, 0, TimeSpan.Zero)
    );

    private static readonly BackOfficeUserIdentityVerificationResponse NotVerified = new(false, null, null, null, null);

    private static readonly MeResponse Admin = new("Admin", "admin@example.com", true, ["admins"]);

    private static readonly MeResponse User = new("User", "user@example.com", false, ["users"]);

    [Fact]
    public void GetView_WhenNothingWasReadYet_ShouldBeLoading()
    {
        // Act
        var view = UserIdentityVerification.GetView(null);

        // Assert
        view.Should().Be(UserIdentityVerificationView.Loading);
    }

    [Fact]
    public void GetView_WhenTheAccountApiReportsAVerification_ShouldBeVerified()
    {
        // Act
        var view = UserIdentityVerification.GetView(ApiCallResult<BackOfficeUserIdentityVerificationResponse>.Success(Verified));

        // Assert
        view.Should().Be(UserIdentityVerificationView.Verified);
    }

    [Fact]
    public void GetView_WhenTheAccountApiReportsNoVerification_ShouldBeUnverified()
    {
        // Act
        var view = UserIdentityVerification.GetView(ApiCallResult<BackOfficeUserIdentityVerificationResponse>.Success(NotVerified));

        // Assert
        view.Should().Be(UserIdentityVerificationView.Unverified);
    }

    [Theory]
    [InlineData(ApiCallOutcome.Failure, 403)]
    [InlineData(ApiCallOutcome.Failure, 404)]
    [InlineData(ApiCallOutcome.Failure, 500)]
    [InlineData(ApiCallOutcome.TransportFailure, null)]
    [InlineData(ApiCallOutcome.InvalidResponse, 200)]
    public void GetView_WhenTheReadFailed_ShouldBeUnavailableAndNeverUnverified(ApiCallOutcome outcome, int? statusCode)
    {
        // Arrange
        var result = ApiCallResult<BackOfficeUserIdentityVerificationResponse>.Failed(outcome, new ApiCallProblem(statusCode, null, null, NoErrors, null));

        // Act
        var view = UserIdentityVerification.GetView(result);

        // Assert
        view.Should().Be(UserIdentityVerificationView.Unavailable);
    }

    [Fact]
    public void CanRevoke_WhenAnAdminSeesAVerification_ShouldOfferTheRevoke()
    {
        // Act
        var canRevoke = UserIdentityVerification.CanRevoke(Admin, UserIdentityVerificationView.Verified);

        // Assert
        canRevoke.Should().BeTrue();
    }

    [Fact]
    public void CanRevoke_WhenTheIdentityIsNotInTheAdminsGroup_ShouldNotOfferTheRevoke()
    {
        // Act
        var canRevoke = UserIdentityVerification.CanRevoke(User, UserIdentityVerificationView.Verified);

        // Assert
        canRevoke.Should().BeFalse();
    }

    [Fact]
    public void CanRevoke_WhileTheIdentityIsStillLoading_ShouldNotOfferTheRevoke()
    {
        // Act
        var canRevoke = UserIdentityVerification.CanRevoke(null, UserIdentityVerificationView.Verified);

        // Assert
        canRevoke.Should().BeFalse();
    }

    [Theory]
    [InlineData(UserIdentityVerificationView.Loading)]
    [InlineData(UserIdentityVerificationView.Unavailable)]
    [InlineData(UserIdentityVerificationView.Unverified)]
    public void CanRevoke_WhenTheTabShowsNoVerification_ShouldNotOfferTheRevokeEvenToAnAdmin(UserIdentityVerificationView view)
    {
        // Act
        var canRevoke = UserIdentityVerification.CanRevoke(Admin, view);

        // Assert
        canRevoke.Should().BeFalse();
    }

    [Fact]
    public void FromRevoke_WhenTheAccountApiRemovedTheVerification_ShouldBeRevoked()
    {
        // Act
        var outcome = UserIdentityVerification.FromRevoke(ApiCallResult.Success());

        // Assert
        outcome.Should().Be(UserIdentityVerificationRevokeOutcome.Revoked);
    }

    [Theory]
    [InlineData(ApiCallOutcome.Failure, 403, UserIdentityVerificationRevokeOutcome.Refused)]
    [InlineData(ApiCallOutcome.Failure, 404, UserIdentityVerificationRevokeOutcome.AlreadyRevoked)]
    [InlineData(ApiCallOutcome.Failure, 500, UserIdentityVerificationRevokeOutcome.Failed)]
    [InlineData(ApiCallOutcome.Failure, 400, UserIdentityVerificationRevokeOutcome.Failed)]
    [InlineData(ApiCallOutcome.TransportFailure, null, UserIdentityVerificationRevokeOutcome.Failed)]
    [InlineData(ApiCallOutcome.Unauthorized, 401, UserIdentityVerificationRevokeOutcome.Leaving)]
    public void FromRevoke_WhenTheRevokeFailed_ShouldMapTheAnswerToItsOutcome(ApiCallOutcome outcome, int? statusCode, UserIdentityVerificationRevokeOutcome expected)
    {
        // Arrange
        var result = ApiCallResult.Failed(outcome, new ApiCallProblem(statusCode, null, null, NoErrors, null));

        // Act
        var revokeOutcome = UserIdentityVerification.FromRevoke(result);

        // Assert
        revokeOutcome.Should().Be(expected);
    }

    [Fact]
    public async Task FromRevoke_WhenTheNetworkFailsUnderTheClient_ShouldBeFailed()
    {
        // Arrange
        var client = new BackOfficeClient(new HttpClient(new UnreachableNetwork()) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.RevokeUserIdentityVerificationAsync(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        UserIdentityVerification.FromRevoke(result).Should().Be(UserIdentityVerificationRevokeOutcome.Failed);
    }

    [Theory]
    [InlineData("en-US", ExternalProviderType.MitId, "Verified with MitID")]
    [InlineData("en-US", ExternalProviderType.Google, "Verified with Google")]
    [InlineData("en-US", ExternalProviderType.Entra, "Verified with Microsoft")]
    [InlineData("en-US", null, "Verified")]
    [InlineData("da-DK", ExternalProviderType.MitId, "Bekræftet med MitID")]
    public void GetProviderLabel_ShouldNameTheProviderAsTheReactBackOfficeDoes(string locale, ExternalProviderType? provider, string expected)
    {
        // Arrange
        using var culture = new CultureScope(locale);

        // Act
        var label = UserIdentityVerification.GetProviderLabel(provider);

        // Assert
        label.Should().Be(expected);
    }

    [Theory]
    [InlineData("en-US", IdentityAssuranceLevel.Substantial, "Substantial assurance")]
    [InlineData("en-US", null, "Unknown assurance")]
    [InlineData("da-DK", IdentityAssuranceLevel.High, "Højt sikringsniveau")]
    public void GetAssuranceLevelLabel_ShouldNameTheLevel(string locale, IdentityAssuranceLevel? assuranceLevel, string expected)
    {
        // Arrange
        using var culture = new CultureScope(locale);

        // Act
        var label = UserIdentityVerification.GetAssuranceLevelLabel(assuranceLevel);

        // Assert
        label.Should().Be(expected);
    }

    [Fact]
    public void FormatDate_WithAndWithoutADate_ShouldShowTheShortDateOrUnknown()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var date = UserIdentityVerification.FormatDate(Verified.VerifiedAt);
        var unknown = UserIdentityVerification.FormatDate(null);

        // Assert
        date.Should().Be(Verified.VerifiedAt!.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
        unknown.Should().Be("Unknown");
    }

    [Theory]
    [InlineData("en-US", "This clears the proof that Blazor User verified their identity. Blazor User must verify again")]
    [InlineData("da-DK", "Dette fjerner beviset for, at Blazor User har bekræftet sin identitet. Blazor User skal bekræfte igen")]
    public void GetRevokeConfirmation_ShouldNameTheUserAndSayTheUserMustVerifyAgain(string locale, string expectedStart)
    {
        // Arrange
        using var culture = new CultureScope(locale);

        // Act
        var confirmation = UserIdentityVerification.GetRevokeConfirmation("Blazor User");

        // Assert
        confirmation.Should().StartWith(expectedStart);
    }

    private sealed class UnreachableNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("The account API could not be reached.");
        }
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
