using System.Net;
using System.Net.Http.Headers;
using Account.Client;
using Account.Features.Users.Requests;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.Tests.Account;

// The host's credential adapter for the typed account API clients. A typed call is made inside a simulated host request by
// resolving the client from a request scope with the request's HttpContext on the accessor, the way a component or form
// handler resolves it. Part of HostSecurityTests so it shares the one host and stand-in account API the fixture starts.
public sealed partial class HostSecurityTests
{
    private const string RefreshAuthenticationTokensHeaderKey = "x-refresh-authentication-tokens-required";

    [Fact]
    public async Task TypedProfileChange_WhenUsersCallAtTheSameTime_ShouldRelayOnlyEachRequestsOwnCredentialsAndResponseHeaders()
    {
        // Arrange
        var users = Enumerable.Range(0, 6).Select(index => $"profile-{index}-{Guid.NewGuid():N}").ToArray();

        // Act
        var calls = await Task.WhenAll(users.Select(async user =>
                {
                    var bearerToken = fixture.CreateToken($"{user}@example.com");
                    var context = CreateRequestContext(bearerToken, $"xsrf-cookie-{user}");
                    var result = await UpdateCurrentUserAsync(context, new UpdateCurrentUserCommand("First", user, "Title"));
                    return (User: user, BearerToken: bearerToken, Context: context, Result: result);
                }
            )
        );

        // Assert
        foreach (var call in calls)
        {
            call.Result.IsSuccess.Should().BeTrue();
            var recorded = fixture.AccountApiRequests[call.User];
            recorded.Authorization.Should().Be($"Bearer {call.BearerToken}");
            recorded.Cookie.Should().Be($"__Host-xsrf-token=xsrf-cookie-{call.User}");
            recorded.AntiforgeryToken.Should().BeNull("a request without a posted form carries no form token to relay");
            recorded.ForwardedFor.Should().Be("198.51.100.7");
            recorded.ForwardedProto.Should().Be("https");
            recorded.Locale.Should().Be("da-DK");

            var responseHeaders = call.Context.Response.Headers;
            responseHeaders[RefreshAuthenticationTokensHeaderKey].ToString().Should().Be("true");
            responseHeaders["x-access-token"].ToString().Should().Be($"access-{call.User}");
            responseHeaders["x-refresh-token"].ToString().Should().Be($"refresh-{call.User}");
            responseHeaders.SetCookie.ToString().Should().Be($"stand-in={call.User}; path=/");
        }
    }

    [Fact]
    public async Task TypedProfileChange_WhenAccountApiRejectsIt_ShouldReturnValidationFailureWithoutRefreshSignalOrTokens()
    {
        // Arrange
        var user = $"profile-failure-{Guid.NewGuid():N}";
        var context = CreateRequestContext(fixture.CreateToken($"{user}@example.com"), $"xsrf-cookie-{user}");

        // Act
        var result = await UpdateCurrentUserAsync(context, new UpdateCurrentUserCommand(HostFixture.FailingFirstName, user, "Title"));

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.ValidationFailure);
        result.Problem!.Errors["firstName"].Should().Equal(HostFixture.FieldErrorMessage);
        context.Response.Headers.Should().NotContainKey(RefreshAuthenticationTokensHeaderKey);
        context.Response.Headers.Should().NotContainKey("x-access-token");
        context.Response.Headers.Should().NotContainKey("x-refresh-token");
        context.Response.Headers.Should().NotContainKey("Set-Cookie");
    }

    [Fact]
    public async Task LoginPost_WhenAccountApiRejectsEmail_ShouldRenderFieldErrorWithoutAnyCredential()
    {
        // Arrange
        var client = fixture.Client;
        var email = $"{HostFixture.FailingEmailPrefix}{Guid.NewGuid():N}@example.com";
        var bearerToken = fixture.CreateToken(email);
        var (cookie, formToken) = await fixture.GetLoginFormAsync(client, bearerToken);
        using var request = HostFixture.CreateLoginPost(email, cookie, formToken, bearerToken);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain($"data-testid=\"form-error\">{HostFixture.FieldErrorMessage}<");
        html.Should().NotContain(bearerToken).And.NotContain(cookie.Split('=', 2)[1]);
        response.Headers.Should().NotContain(header => header.Key == "x-access-token" || header.Key == RefreshAuthenticationTokensHeaderKey);
        fixture.AccountApiRequests[email].Authorization.Should().Be($"Bearer {bearerToken}");
    }

    private async Task<ApiCallResult> UpdateCurrentUserAsync(HttpContext context, UpdateCurrentUserCommand command)
    {
        await using var scope = fixture.HostServices.CreateAsyncScope();
        context.RequestServices = scope.ServiceProvider;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return await scope.ServiceProvider.GetRequiredService<UsersClient>().UpdateCurrentUserAsync(command, CancellationToken.None);
    }

    // The request as the host sees it after the forwarded headers middleware accepted the gateway's values
    private static DefaultHttpContext CreateRequestContext(string bearerToken, string antiforgeryCookie)
    {
        var context = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("198.51.100.7") },
            Request = { Scheme = "https", Host = new HostString(HostFixture.PublicHost) }
        };
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken).ToString();
        context.Request.Headers.Cookie = $"__Host-xsrf-token={antiforgeryCookie}";
        context.Request.Headers.AcceptLanguage = "da-DK";
        return context;
    }
}
