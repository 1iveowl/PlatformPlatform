using System.Net;
using Account.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Authentication.BackOfficeIdentity;
using Xunit;

namespace Account.Tests.BackOffice;

public sealed class BackOfficeBlazorProxyTests
{
    private const string BackOfficeHost = "back-office.test.localhost";
    private const string BlazorHostUrl = "https://blazor-host.internal.test.localhost";

    [Fact]
    public async Task TransformRequestAsync_WhenForwardingABackOfficeRequest_ShouldSendTheBlazorHostAddressAndTheBackOfficeHostAsForwardedHost()
    {
        // Arrange
        var httpContext = CreateBackOfficeRequest();
        using var proxyRequest = new HttpRequestMessage();
        var transformer = new BackOfficeBlazorProxy.BackOfficeIdentityTransformer();

        // Act
        await transformer.TransformRequestAsync(httpContext, proxyRequest, BlazorHostUrl, CancellationToken.None);

        // Assert
        proxyRequest.Headers.Host.Should().BeNull();
        proxyRequest.Headers.GetValues("X-Forwarded-Host").Should().Equal(BackOfficeHost);
        proxyRequest.Headers.GetValues("X-Forwarded-Proto").Should().Equal("https");
    }

    [Fact]
    public async Task TransformRequestAsync_WhenInboundIdentityAndForwardingHeadersArePresent_ShouldForwardOnlyTheValuesTheListenerEstablished()
    {
        // Arrange
        var httpContext = CreateBackOfficeRequest();
        httpContext.Request.Headers[BackOfficeIdentityDefaults.PrincipalNameHeader] = "forged@example.com";
        httpContext.Request.Headers[BackOfficeIdentityDefaults.PrincipalIdHeader] = "forged-id";
        httpContext.Request.Headers[BackOfficeIdentityDefaults.PrincipalPayloadHeader] = "forged-payload";
        httpContext.Request.Headers[ForwardedBackOfficeIdentity.HeaderName] = "forged-identity";
        httpContext.Request.Headers["X-Forwarded-Host"] = "forged.example.com";
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.66";
        httpContext.Request.Headers["X-Forwarded-Proto"] = "http";
        httpContext.Request.Headers["X-Forwarded-Prefix"] = "/forged";
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
        using var proxyRequest = new HttpRequestMessage();
        var transformer = new BackOfficeBlazorProxy.BackOfficeIdentityTransformer();

        // Act
        await transformer.TransformRequestAsync(httpContext, proxyRequest, BlazorHostUrl, CancellationToken.None);

        // Assert
        proxyRequest.Headers.Contains(BackOfficeIdentityDefaults.PrincipalNameHeader).Should().BeFalse();
        proxyRequest.Headers.Contains(BackOfficeIdentityDefaults.PrincipalIdHeader).Should().BeFalse();
        proxyRequest.Headers.Contains(BackOfficeIdentityDefaults.PrincipalPayloadHeader).Should().BeFalse();
        proxyRequest.Headers.GetValues(ForwardedBackOfficeIdentity.HeaderName).Should().Equal("protected-identity");
        proxyRequest.Headers.GetValues("X-Forwarded-Host").Should().Equal(BackOfficeHost);
        proxyRequest.Headers.GetValues("X-Forwarded-For").Should().Equal("198.51.100.7");
        proxyRequest.Headers.GetValues("X-Forwarded-Proto").Should().Equal("https");
        proxyRequest.Headers.Contains("X-Forwarded-Prefix").Should().BeFalse();
    }

    private static DefaultHttpContext CreateBackOfficeRequest()
    {
        return new DefaultHttpContext
        {
            Request = { Method = HttpMethods.Get, Scheme = "https", Host = new HostString(BackOfficeHost), Path = "/blazor/back-office" },
            Items = { [ForwardedBackOfficeIdentity.HeaderName] = "protected-identity" }
        };
    }
}
