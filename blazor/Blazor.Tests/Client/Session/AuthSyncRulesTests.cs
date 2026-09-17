using Account.Features.Authentication.Queries;
using Blazor.Client.Session;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Session;

public sealed class AuthSyncRulesTests
{
    private const string UserId = "usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ";
    private const string OtherUserId = "usr_01JZ8Q4N6V3K2M7P9R5T0W1ABC";

    private static readonly AuthSyncIdentity Current = new(true, UserId, "1", "owner@example.com");

    [Fact]
    public void Decide_WhenAnotherTabLoggedOut_ShouldEndTheIdentityEvenForTheSameTenant()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.UserLoggedOutType, UserId));

        // Assert
        decision.Should().Be(new AuthSyncDecision(AuthSyncOutcome.LoggedOut));
        decision.EndsIdentity.Should().BeTrue();
    }

    [Fact]
    public void Decide_WhenAnotherTabSwitchedToAnotherTenant_ShouldEndTheIdentityWithTheTenantName()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.TenantSwitchedType, UserId, newTenantId: "2", tenantName: "Globex"));

        // Assert
        decision.Should().Be(new AuthSyncDecision(AuthSyncOutcome.TenantSwitched, "Globex"));
    }

    [Fact]
    public void Decide_WhenASwitchNamesThisRuntimesTenant_ShouldChangeNothing()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.TenantSwitchedType, UserId, newTenantId: "1"));

        // Assert
        decision.Should().Be(AuthSyncDecision.None);
    }

    [Fact]
    public void Decide_WhenASwitchNamesNoTenant_ShouldReconcileWithTheServer()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.TenantSwitchedType, UserId));

        // Assert
        decision.Outcome.Should().Be(AuthSyncOutcome.Reconcile);
    }

    [Fact]
    public void Decide_WhenADifferentUserLoggedInToTheSameTenant_ShouldEndTheIdentityAsADifferentUser()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.UserLoggedInType, OtherUserId, "1", email: "member@example.com"));

        // Assert
        decision.Should().Be(new AuthSyncDecision(AuthSyncOutcome.DifferentUser));
    }

    [Fact]
    public void Decide_WhenALoginNamesOnlyAnotherEmail_ShouldEndTheIdentityAsADifferentUser()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.UserLoggedInType, email: "member@example.com"));

        // Assert
        decision.Outcome.Should().Be(AuthSyncOutcome.DifferentUser);
    }

    [Fact]
    public void Decide_WhenTheSameUserLoggedInToAnotherTenant_ShouldEndTheIdentityAsATenantSwitch()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.UserLoggedInType, UserId, "2"));

        // Assert
        decision.Should().Be(new AuthSyncDecision(AuthSyncOutcome.TenantSwitched));
    }

    [Fact]
    public void Decide_WhenTheSameUserAnnouncesTheSameTenant_ShouldChangeNothing()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.UserLoggedInType, UserId, "1", email: "OWNER@example.com"));

        // Assert
        decision.Should().Be(AuthSyncDecision.None);
    }

    [Theory]
    [InlineData(null, "owner@example.com", null)]
    [InlineData(null, null, "1")]
    public void Decide_WhenALoginNamesTooLittle_ShouldReconcileWithTheServer(string? userId, string? email, string? tenantId)
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(AuthSyncRules.UserLoggedInType, userId, tenantId, email: email));

        // Assert
        decision.Outcome.Should().Be(AuthSyncOutcome.Reconcile);
    }

    [Theory]
    [InlineData(AuthSyncRules.UserLoggedOutType)]
    [InlineData(AuthSyncRules.TenantSwitchedType)]
    [InlineData(AuthSyncRules.UserLoggedInType)]
    public void Decide_WhenThisRuntimeHasNoAuthenticatedIdentity_ShouldChangeNothing(string type)
    {
        // Arrange
        var anonymous = new AuthSyncIdentity(false, null, null, null);

        // Act
        var decision = AuthSyncRules.Decide(anonymous, Message(type, OtherUserId, "2", "2"));

        // Assert
        decision.Should().Be(AuthSyncDecision.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SESSION_REFRESHED")]
    public void Decide_WhenTheMessageTypeIsUnknown_ShouldChangeNothing(string? type)
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Message(type, OtherUserId, "2"));

        // Assert
        decision.Should().Be(AuthSyncDecision.None);
    }

    [Fact]
    public void Decide_WhenTheServerBootstrapIsAnonymous_ShouldEndTheIdentityAsLoggedOut()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Bootstrap(null, 1));

        // Assert
        decision.Outcome.Should().Be(AuthSyncOutcome.LoggedOut);
    }

    [Fact]
    public void Decide_WhenTheServerBootstrapIsAnotherUserInTheSameTenant_ShouldEndTheIdentityAsADifferentUser()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Bootstrap(OtherUserId, 1));

        // Assert
        decision.Outcome.Should().Be(AuthSyncOutcome.DifferentUser);
    }

    [Fact]
    public void Decide_WhenTheServerBootstrapIsAnotherTenant_ShouldEndTheIdentityWithTheServersTenantName()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Bootstrap(UserId, 2));

        // Assert
        decision.Should().Be(new AuthSyncDecision(AuthSyncOutcome.TenantSwitched, "Tenant 2"));
    }

    [Fact]
    public void Decide_WhenTheServerBootstrapMatches_ShouldChangeNothing()
    {
        // Act
        var decision = AuthSyncRules.Decide(Current, Bootstrap(UserId, 1));

        // Assert
        decision.Should().Be(AuthSyncDecision.None);
    }

    [Fact]
    public void Announcements_ShouldCarryIdentifiersOnlyInTheReactEditionsShape()
    {
        // Arrange
        var user = Bootstrap(UserId, 1).User!;

        // Act
        var loggedIn = AuthSyncRules.LoggedIn(user);
        var switched = AuthSyncRules.TenantSwitched(user, new TenantId(2), "Globex");
        var loggedOut = AuthSyncRules.LoggedOut(user);

        // Assert
        loggedIn.Should().Be(new AuthSyncMessage(AuthSyncRules.UserLoggedInType, UserId, "1", null, null, null, "owner@example.com", 0));
        switched.Should().Be(new AuthSyncMessage(AuthSyncRules.TenantSwitchedType, UserId, null, "2", "1", "Globex", null, 0));
        loggedOut.Should().Be(new AuthSyncMessage(AuthSyncRules.UserLoggedOutType, UserId, null, null, null, null, null, 0));
        AuthSyncRules.Decide(Current, loggedIn).Should().Be(AuthSyncDecision.None);
    }

    private static AuthSyncMessage Message(string? type, string? userId = null, string? tenantId = null, string? newTenantId = null, string? tenantName = null, string? email = null)
    {
        return new AuthSyncMessage(type, userId, tenantId, newTenantId, null, tenantName, email, 1);
    }

    private static BootstrapResponse Bootstrap(string? userId, long tenantId)
    {
        var user = userId is null
            ? null
            : new BootstrapUser(new UserId(userId), new TenantId(tenantId), "Owner", "owner@example.com", null, null, null, null, $"Tenant {tenantId}", null, null, false, []);
        return new BootstrapResponse(user is not null, user, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), "antiforgery-token");
    }
}
