using Account.Integrations.OAuth.Google;
using FluentAssertions;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class GoogleOAuthCanonicalizeIssuerTests
{
    [Fact]
    public void CanonicalizeIssuer_WhenIssuerIsTheHttpsForm_ShouldReturnItUnchanged()
    {
        // Act
        var result = GoogleOAuthProvider.CanonicalizeIssuer("https://accounts.google.com");

        // Assert
        result.Should().Be("https://accounts.google.com");
    }

    [Fact]
    public void CanonicalizeIssuer_WhenIssuerIsTheBareDomain_ShouldReturnTheHttpsForm()
    {
        // Act
        var result = GoogleOAuthProvider.CanonicalizeIssuer("accounts.google.com");

        // Assert
        result.Should().Be("https://accounts.google.com");
    }
}
