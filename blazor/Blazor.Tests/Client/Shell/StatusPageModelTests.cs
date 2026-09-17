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
        var view = StatusPageModel.CreateError(null, true, true, "/blazor/users", "?search=ada", Failure, "trace-1");

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
        var view = StatusPageModel.CreateError(null, false, isDevelopment, "/blazor/login", null, hasException ? Failure : null, "trace-1");

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
        var view = StatusPageModel.CreateError(errorCode, true, true, null, null, null, "trace-1");

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
        var view = StatusPageModel.CreateError(errorCode, false, false, null, null, null, "trace-1");

        // Assert
        view.Session.Should().BeNull();
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
