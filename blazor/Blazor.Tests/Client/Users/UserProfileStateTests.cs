using Account.Client;
using Account.Features.Users.Domain;
using Account.Features.Users.Queries;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Users;

// The side pane applies only its latest load, shows a defined state for a user it cannot read, and compares its copy of
// the user with the list's loaded page
public sealed class UserProfileStateTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_WhenTheRowIsKnown_ShouldShowTheRowWhileTheUserLoads()
    {
        // Arrange
        var state = new UserProfileState();
        var row = CreateUser("usr_01KC0ANN00000000000000000A", UserRole.Member);

        // Act
        state.Open(row.Id.Value, row);

        // Assert
        state.Status.Should().Be(UserProfileStatus.Ready);
        state.User.Should().Be(row);
        state.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Open_WhenTheUserIsNotOnThePage_ShouldBeLoading()
    {
        // Arrange
        var state = new UserProfileState();

        // Act
        state.Open("usr_01KC0ANN00000000000000000A");

        // Assert
        state.Status.Should().Be(UserProfileStatus.Loading);
        state.User.Should().BeNull();
    }

    [Fact]
    public void Complete_WhenAnOlderLoadFinishesAfterANewerOne_ShouldKeepTheNewerUser()
    {
        // Arrange
        var state = new UserProfileState();
        var member = CreateUser("usr_01KC0ANN00000000000000000A", UserRole.Member);
        var admin = member with { Role = UserRole.Admin, ModifiedAt = CreatedAt.AddHours(1) };
        var olderLoad = state.Open(member.Id.Value, member);
        var newerLoad = state.Open(member.Id.Value);

        // Act
        var newerApplied = state.Complete(newerLoad, ApiCallResult<UserDetails>.Success(admin));
        var olderApplied = state.Complete(olderLoad, ApiCallResult<UserDetails>.Success(member));

        // Assert
        newerApplied.Should().BeTrue();
        olderApplied.Should().BeFalse();
        state.User!.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public void Complete_WhenThePaneWasClosedBeforeTheLoadFinished_ShouldStayClosed()
    {
        // Arrange
        var state = new UserProfileState();
        var user = CreateUser("usr_01KC0ANN00000000000000000A", UserRole.Member);
        var load = state.Open(user.Id.Value);
        state.Close();

        // Act
        var applied = state.Complete(load, ApiCallResult<UserDetails>.Success(user));

        // Assert
        applied.Should().BeFalse();
        state.Status.Should().Be(UserProfileStatus.Closed);
        state.User.Should().BeNull();
    }

    [Fact]
    public void Complete_WhenAnotherUserWasOpenedSince_ShouldIgnoreThePreviousUser()
    {
        // Arrange
        var state = new UserProfileState();
        var first = CreateUser("usr_01KC0ANN00000000000000000A", UserRole.Member);
        var second = CreateUser("usr_01KC0BOB00000000000000000A", UserRole.Admin);
        var firstLoad = state.Open(first.Id.Value);
        state.Open(second.Id.Value, second);

        // Act
        var applied = state.Complete(firstLoad, ApiCallResult<UserDetails>.Success(first));

        // Assert
        applied.Should().BeFalse();
        state.User.Should().Be(second);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    public void Complete_WhenTheUserCannotBeRead_ShouldBeNotFoundWithoutUserData(int statusCode)
    {
        // Arrange
        var state = new UserProfileState();
        var row = CreateUser("usr_01KC0ANN00000000000000000A", UserRole.Member);
        var load = state.Open(row.Id.Value, row);
        var problem = new ApiCallProblem(statusCode, "Not Found", $"User with id '{row.Id}' not found.", new Dictionary<string, string[]>(), null);

        // Act
        var applied = state.Complete(load, ApiCallResult<UserDetails>.Failed(ApiCallOutcome.Failure, problem));

        // Assert
        applied.Should().BeTrue();
        state.Status.Should().Be(UserProfileStatus.NotFound);
        state.User.Should().BeNull();
        state.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Complete_WhenTheServerFails_ShouldKeepTheApiMessage()
    {
        // Arrange
        var state = new UserProfileState();
        var load = state.Open("usr_01KC0ANN00000000000000000A");
        var problem = new ApiCallProblem(500, "Internal Server Error", "Something broke.", new Dictionary<string, string[]>(), null);

        // Act
        state.Complete(load, ApiCallResult<UserDetails>.Failed(ApiCallOutcome.Failure, problem));

        // Assert
        state.Status.Should().Be(UserProfileStatus.Failed);
        state.ErrorMessage.Should().Be("Something broke.");
    }

    [Fact]
    public void Complete_WhenTheSessionIsLost_ShouldLeaveTheStateUnchanged()
    {
        // Arrange
        var state = new UserProfileState();
        var row = CreateUser("usr_01KC0ANN00000000000000000A", UserRole.Member);
        var load = state.Open(row.Id.Value, row);
        var problem = new ApiCallProblem(401, "Unauthorized", null, new Dictionary<string, string[]>(), "SessionNotFound");

        // Act
        var applied = state.Complete(load, ApiCallResult<UserDetails>.Failed(ApiCallOutcome.Unauthorized, problem));

        // Assert
        applied.Should().BeFalse();
        state.User.Should().Be(row);
    }

    [Fact]
    public void IsNotInCurrentView_ShouldCompareTheShownUserWithTheLoadedPage()
    {
        // Arrange
        var state = new UserProfileState();
        var shown = CreateUser("usr_01KC0ANN00000000000000000A", UserRole.Member);
        var other = CreateUser("usr_01KC0BOB00000000000000000A", UserRole.Member);
        state.Open(shown.Id.Value, shown);

        // Act
        var notOnPage = state.IsNotInCurrentView([other]);
        var onPage = state.IsNotInCurrentView([other, shown]);

        // Assert
        notOnPage.Should().BeTrue();
        onPage.Should().BeFalse();
    }

    [Fact]
    public void IsDataUpdated_WhenTheRowWasModifiedAtAnotherTime_ShouldBeTrue()
    {
        // Arrange
        var state = new UserProfileState();
        var row = CreateUser("usr_01KC0ANN00000000000000000A", UserRole.Member);
        var load = state.Open(row.Id.Value, row);
        state.Complete(load, ApiCallResult<UserDetails>.Success(row with { Role = UserRole.Admin, ModifiedAt = CreatedAt.AddHours(1) }));

        // Act
        var updated = state.IsDataUpdated([row]);
        var current = state.IsDataUpdated([row with { ModifiedAt = CreatedAt.AddHours(1) }]);

        // Assert
        updated.Should().BeTrue();
        current.Should().BeFalse();
    }

    private static UserDetails CreateUser(string id, UserRole role)
    {
        return new UserDetails(new UserId(id), CreatedAt, CreatedAt, null, $"{id}@example.com", role, "Ann", "Lee", "", true, null);
    }
}
