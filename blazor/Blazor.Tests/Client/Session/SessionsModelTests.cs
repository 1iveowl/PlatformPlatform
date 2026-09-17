using System.Globalization;
using Account.Features.Authentication.Domain;
using Account.Features.Authentication.Queries;
using Blazor.Client.Sessions;
using FluentAssertions;
using SharedKernel.Authentication.TokenGeneration;

namespace Blazor.Tests.Client.Session;

public sealed class SessionsModelTests
{
    private const string ChromeOnWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    private static int _sessionCount;

    [Fact]
    public void Create_WhenCurrentSessionIsNotFirst_ShouldPutItFirstAndKeepTheOthersInOrder()
    {
        // Arrange
        var first = CreateSession(false, "Acme");
        var current = CreateSession(true, "Acme");
        var last = CreateSession(false, "Other");

        // Act
        var model = SessionsModel.Create([first, current, last]);

        // Assert
        model.Current!.Session.Should().Be(current);
        model.Others.Select(card => card.Session).Should().Equal(first, last);
        model.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Create_WhenASessionIsInAnotherTenant_ShouldShowTheAccountNameOnlyThere()
    {
        // Arrange
        var current = CreateSession(true, "Acme");
        var sameTenant = CreateSession(false, "Acme");
        var otherTenant = CreateSession(false, "Other");

        // Act
        var model = SessionsModel.Create([current, sameTenant, otherTenant]);

        // Assert
        model.Current!.ShowAccountName.Should().BeFalse();
        model.Others.Select(card => card.ShowAccountName).Should().Equal(false, true);
    }

    [Fact]
    public void Create_WhenThereAreNoSessions_ShouldBeEmpty()
    {
        // Act
        var model = SessionsModel.Create([]);

        // Assert
        model.IsEmpty.Should().BeTrue();
        model.Current.Should().BeNull();
    }

    [Fact]
    public void Create_WhenUserAgentIsUnknown_ShouldUseTheUnknownLabelsFromResources()
    {
        // Arrange
        using var _ = new CultureScope("da-DK");
        var session = CreateSession(true, "Acme", "curl/8.5.0", DeviceType.Unknown, LoginMethod.OneTimePassword);

        // Act
        var card = SessionsModel.Create([session]).Current!;

        // Assert
        card.Browser.Should().Be("Ukendt");
        card.OperatingSystem.Should().Be("Ukendt");
        card.DeviceTypeLabel.Should().Be("Ukendt");
        card.LoginMethodLabel.Should().Be("Engangskode");
    }

    [Theory]
    [InlineData(LoginMethod.OneTimePassword, "One-time password")]
    [InlineData(LoginMethod.Google, "Google")]
    [InlineData(LoginMethod.Entra, "Microsoft")]
    [InlineData(LoginMethod.MitId, "MitID")]
    public void GetLoginMethodLabel_ShouldNameEveryLoginMethod(LoginMethod loginMethod, string label)
    {
        // Arrange
        using var _ = new CultureScope("en-US");

        // Act & Assert
        SessionsModel.GetLoginMethodLabel(loginMethod).Should().Be(label);
    }

    [Theory]
    [InlineData(DeviceType.Desktop, "Desktop")]
    [InlineData(DeviceType.Mobile, "Mobile")]
    [InlineData(DeviceType.Tablet, "Tablet")]
    [InlineData(DeviceType.Unknown, "Unknown")]
    public void GetDeviceTypeLabel_ShouldNameEveryDeviceType(DeviceType deviceType, string label)
    {
        // Arrange
        using var _ = new CultureScope("en-US");

        // Act & Assert
        SessionsModel.GetDeviceTypeLabel(deviceType).Should().Be(label);
    }

    private static UserSessionInfo CreateSession(bool isCurrent, string tenantName, string userAgent = ChromeOnWindows, DeviceType deviceType = DeviceType.Desktop, LoginMethod loginMethod = LoginMethod.Google)
    {
        var now = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);
        return new UserSessionInfo(new SessionId($"sess_01JZ8Q4N6V3K2M7P9R5T0W{Interlocked.Increment(ref _sessionCount):D4}"), now.AddDays(-1), loginMethod, deviceType, userAgent, "10.0.0.1", now, isCurrent, tenantName);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
