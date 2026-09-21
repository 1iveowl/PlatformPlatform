using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

public sealed class StatusPageModelTests
{
    private static readonly InvalidOperationException Failure = CreateThrownException();

    [Theory]
    [InlineData(true, StatusPageLayout.Shell, "/blazor/app")]
    [InlineData(false, StatusPageLayout.Public, "/blazor/")]
    public void GetLayoutAndHomeUrl_WhenAuthenticationGiven_ShouldPickTheShellOrThePublicLayout(bool isAuthenticated, StatusPageLayout layout, string homeUrl)
    {
        // Act
        var actualLayout = StatusPageModel.GetLayout(isAuthenticated);
        var actualHomeUrl = StatusPageModel.GetHomeUrl(isAuthenticated);

        // Assert
        actualLayout.Should().Be(layout);
        actualHomeUrl.Should().Be(homeUrl);
    }

    [Fact]
    public void CreateError_WhenDevelopmentWithException_ShouldRevealMessageAndStackWithoutReferenceId()
    {
        // Act
        var view = StatusPageModel.CreateError(null, null, true, true, "/blazor/users", "?search=ada", Failure, "trace-1");

        // Assert
        view.Layout.Should().Be(StatusPageLayout.Shell);
        view.Session.Should().BeNull();
        view.Message.Should().Be("Failed on purpose.");
        view.StackTrace.Should().NotBeNullOrEmpty();
        view.ReferenceId.Should().BeNull();
        view.RetryUrl.Should().Be("/blazor/users?search=ada");
        view.HomeUrl.Should().Be("/blazor/app");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CreateError_WhenNotDevelopmentOrNoException_ShouldRevealOnlyTheReferenceId(bool isDevelopment, bool hasException)
    {
        // Act
        var view = StatusPageModel.CreateError(null, null, false, isDevelopment, "/blazor/login", null, hasException ? Failure : null, "trace-1");

        // Assert
        view.Layout.Should().Be(StatusPageLayout.Public);
        view.Message.Should().BeNull();
        view.StackTrace.Should().BeNull();
        view.ReferenceId.Should().Be("trace-1");
        view.HomeUrl.Should().Be("/blazor/");
    }

    [Theory]
    [InlineData("session_revoked", SessionErrorKind.Revoked)]
    [InlineData("session_not_found", SessionErrorKind.Expired)]
    [InlineData("session_expired", SessionErrorKind.Expired)]
    [InlineData("tenant_deleted", SessionErrorKind.TenantDeleted)]
    public void CreateError_WhenSessionErrorCodeGiven_ShouldKeepThePublicLayoutEvenWithASession(string errorCode, SessionErrorKind expected)
    {
        // Act
        var view = StatusPageModel.CreateError(errorCode, null, true, true, null, null, null, "trace-1");

        // Assert
        view.Session.Should().Be(expected);
        view.Layout.Should().Be(StatusPageLayout.Public);
        view.HomeUrl.Should().Be("/blazor/");
    }

    [Theory]
    [InlineData("unknown_code")]
    [InlineData("")]
    [InlineData(null)]
    public void CreateError_WhenErrorCodeIsUnknown_ShouldShowTheFailure(string? errorCode)
    {
        // Act
        var view = StatusPageModel.CreateError(errorCode, null, false, false, null, null, null, "trace-1");

        // Assert
        view.Session.Should().BeNull();
    }

    [Theory]
    [InlineData("user_not_found", AuthenticationErrorKind.UserNotFound, "SignUp,LogIn")]
    [InlineData("account_already_exists", AuthenticationErrorKind.AccountAlreadyExists, "LogIn,SignUp")]
    [InlineData("email_not_provided", AuthenticationErrorKind.EmailNotProvided, "SignUp,LogIn")]
    [InlineData("authentication_failed", AuthenticationErrorKind.AuthenticationFailed, "LogIn")]
    [InlineData("invalid_request", AuthenticationErrorKind.InvalidRequest, "LogIn")]
    [InlineData("access_denied", AuthenticationErrorKind.AccessDenied, "LogIn")]
    [InlineData("identity_not_verified", AuthenticationErrorKind.IdentityNotVerified, "LogIn")]
    [InlineData("server_error", AuthenticationErrorKind.ServerError, "LogIn")]
    [InlineData("identity_already_linked", AuthenticationErrorKind.IdentityAlreadyLinked, "BackToProfile")]
    [InlineData("assurance_level_insufficient", AuthenticationErrorKind.AssuranceLevelInsufficient, "BackToProfile")]
    public void CreateError_WhenAuthenticationErrorCodeGivenWithoutASession_ShouldPickItsActionsAndThePublicLayout(string errorCode, AuthenticationErrorKind expected, string actions)
    {
        // Act
        var view = StatusPageModel.CreateError(errorCode, "exlog_01JZ8Q4N6V3K2M7P9R5T0W1XYZ", false, false, null, null, null, "trace-1");

        // Assert
        view.Authentication.Should().Be(expected);
        view.Session.Should().BeNull();
        view.ErrorCode.Should().Be(errorCode);
        string.Join(',', view.Actions).Should().Be(actions);
        view.Layout.Should().Be(StatusPageLayout.Public);
        view.ReferenceId.Should().Be("exlog_01JZ8Q4N6V3K2M7P9R5T0W1XYZ");
    }

    // A refusal a signed-in user can reach, above all the identity verification ones, keeps the shell the retry lives in
    [Theory]
    [InlineData("identity_already_linked")]
    [InlineData("assurance_level_insufficient")]
    [InlineData("identity_not_verified")]
    [InlineData("invalid_request")]
    [InlineData("access_denied")]
    [InlineData("server_error")]
    [InlineData("user_not_found")]
    [InlineData("account_already_exists")]
    [InlineData("email_not_provided")]
    public void CreateError_WhenAnAuthenticationRefusalReachesASignedInUser_ShouldRenderInsideTheShell(string errorCode)
    {
        // Act
        var view = StatusPageModel.CreateError(errorCode, "exlog_01JZ8Q4N6V3K2M7P9R5T0W1XYZ", true, false, null, null, null, "trace-1");

        // Assert
        view.Layout.Should().Be(StatusPageLayout.Shell);
        view.ErrorCode.Should().Be(errorCode);
        view.HomeUrl.Should().Be("/blazor/app");
    }

    // The one refusal a login attempt and an identity verification share stays public, because its action is to log in
    [Fact]
    public void CreateError_WhenAuthenticationFailedReachesASignedInUser_ShouldKeepThePublicLayout()
    {
        // Act
        var view = StatusPageModel.CreateError("authentication_failed", null, true, false, null, null, null, "trace-1");

        // Assert
        view.Layout.Should().Be(StatusPageLayout.Public);
        view.HomeUrl.Should().Be("/blazor/");
    }

    [Fact]
    public void CreateError_WhenNoCodeIsGivenForASignedInUser_ShouldRenderInsideTheShell()
    {
        // Act
        var view = StatusPageModel.CreateError(null, null, true, false, null, null, null, "trace-1");

        // Assert
        view.Layout.Should().Be(StatusPageLayout.Shell);
        view.HomeUrl.Should().Be("/blazor/app");
    }

    [Theory]
    [InlineData("identity_mismatch")]
    [InlineData("IDENTITY_ALREADY_LINKED")]
    [InlineData("ACCESS_DENIED")]
    [InlineData("<script>alert(1)</script>")]
    public void CreateError_WhenCodeIsNotAKnownExternalAuthenticationCode_ShouldShowTheGenericFailureWithoutEchoingTheCode(string errorCode)
    {
        // Act
        var view = StatusPageModel.CreateError(errorCode, "exlog_1", false, false, null, null, null, "trace-1");

        // Assert
        view.Authentication.Should().BeNull();
        view.Session.Should().BeNull();
        view.ErrorCode.Should().BeNull();
        view.Actions.Should().BeEmpty();
        view.ReferenceId.Should().Be("trace-1");
    }

    [Theory]
    [InlineData("exlog_01JZ8Q4N6V3K2M7P9R5T0W1XYZ", "exlog_01JZ8Q4N6V3K2M7P9R5T0W1XYZ")]
    [InlineData("abc-DEF_123", "abc-DEF_123")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("exlog 1", null)]
    [InlineData("<b>id</b>", null)]
    [InlineData("exlog_1\"onmouseover=\"x", null)]
    [InlineData("exælog", null)]
    [InlineData("exlog_1%0Aline", null)]
    public void GetReferenceId_WhenIdGiven_ShouldKeepOnlyTheShapeOfAnExternalLoginId(string? id, string? expected)
    {
        // Act
        var referenceId = StatusPageModel.GetReferenceId(id);

        // Assert
        referenceId.Should().Be(expected);
    }

    [Fact]
    public void GetReferenceId_WhenIdIsLongerThanAnExternalLoginId_ShouldDropIt()
    {
        // Act
        var referenceId = StatusPageModel.GetReferenceId(new string('a', 65));

        // Assert
        referenceId.Should().BeNull();
    }

    [Theory]
    [InlineData(ErrorPageAction.LogIn, "/blazor/login")]
    [InlineData(ErrorPageAction.SignUp, "/blazor/signup")]
    [InlineData(ErrorPageAction.BackToProfile, "/blazor/user/profile")]
    public void GetActionUrl_WhenActionGiven_ShouldLeadBelowThePathBase(ErrorPageAction action, string expected)
    {
        // Act
        var url = StatusPageModel.GetActionUrl(action);

        // Assert
        url.Should().Be(expected);
    }

    [Theory]
    [InlineData("/blazor/app/details", null, "/blazor/app/details")]
    [InlineData("/blazor/login", "?returnPath=%2Fblazor%2Fapp", "/blazor/login?returnPath=%2Fblazor%2Fapp")]
    [InlineData(null, null, "/home")]
    [InlineData("", "?x=1", "/home")]
    [InlineData("/other/path", null, "/home")]
    [InlineData("/blazor//evil.example", null, "/home")]
    [InlineData("/blazor/a\\b", null, "/home")]
    public void GetRetryUrl_WhenFailedPathGiven_ShouldReturnToItOnlyBelowThePathBase(string? failedPath, string? failedQuery, string expected)
    {
        // Act
        var retryUrl = StatusPageModel.GetRetryUrl(failedPath, failedQuery, "/home");

        // Assert
        retryUrl.Should().Be(expected);
    }

    private static InvalidOperationException CreateThrownException()
    {
        try
        {
            throw new InvalidOperationException("Failed on purpose.");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }
}
