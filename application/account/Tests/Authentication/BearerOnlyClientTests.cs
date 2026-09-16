using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Account.Database;
using Account.Features.Users.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Authentication;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Authentication.TokenSigning;
using SharedKernel.Tests;
using Xunit;

namespace Account.Tests.Authentication;

// Antiforgery validation reads a process-wide environment variable, so these tests run in a collection that xUnit
// runs after every parallel collection, on a factory that switches the bypass off.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AntiforgeryValidationCollection
{
    public const string Name = "Antiforgery validation";
}

public sealed class AntiforgeryValidationAccountWebApplicationFactory : AccountWebApplicationFactory
{
    public AntiforgeryValidationAccountWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("BypassAntiforgeryValidation", "false");
    }

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("BypassAntiforgeryValidation", "true");
        base.Dispose(disposing);
    }
}

// A client that holds only an access token and no cookie: the bootstrap and the authenticated reads of the three
// journeys work, with antiforgery validation active. State-changing requests keep antiforgery validation whether or
// not they carry a bearer token; cookie-free writes are deferred.
[Collection(AntiforgeryValidationCollection.Name)]
public sealed class BearerOnlyClientTests(AntiforgeryValidationAccountWebApplicationFactory factory)
    : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<AntiforgeryValidationAccountWebApplicationFactory>
{
    private const string AntiforgeryHeaderName = AuthenticationTokenHttpKeys.AntiforgeryTokenHttpHeaderKey;
    private const string LogoutUrl = "/api/account/authentication/logout";

    [Theory]
    [InlineData("/api/account/bootstrap")]
    [InlineData("/api/account/users")]
    [InlineData("/api/account/users/{ownerId}")]
    [InlineData("/api/account/users/me")]
    [InlineData("/api/account/tenants/")]
    [InlineData("/api/account/tenants/current")]
    public async Task Read_WhenBearerTokenAndNoCookie_ShouldSucceed(string url)
    {
        // Arrange
        using var client = CreateBearerOnlyClient(CreateAccessToken(DatabaseSeeder.Tenant1Owner));
        using var request = new HttpRequestMessage(HttpMethod.Get, url.Replace("{ownerId}", DatabaseSeeder.Tenant1Owner.Id.ToString()));

        // Act
        var response = await client.SendAsync(request);

        // Assert
        request.Headers.Contains("Cookie").Should().BeFalse();
        response.ShouldBeSuccessfulGetRequest();
    }

    [Theory]
    [InlineData("/api/account/users")]
    [InlineData("/api/account/users/me")]
    [InlineData("/api/account/tenants/")]
    [InlineData("/api/account/tenants/current")]
    public async Task Read_WhenBearerTokenIsMissingMalformedOrExpired_ShouldReturnUnauthorized(string url)
    {
        // Arrange
        using var missing = factory.CreateClient();
        using var malformed = CreateBearerOnlyClient("not-a-jwt");
        using var expired = CreateBearerOnlyClient(CreateExpiredAccessToken(DatabaseSeeder.Tenant1Owner));

        // Act
        var responses = await Task.WhenAll(missing.GetAsync(url), malformed.GetAsync(url), expired.GetAsync(url));

        // Assert
        responses.Select(response => response.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bootstrap_WhenBearerTokenIsExpired_ShouldReturnUnauthorizedWhileNoTokenIsAnonymous()
    {
        // Arrange
        using var expired = CreateBearerOnlyClient(CreateExpiredAccessToken(DatabaseSeeder.Tenant1Owner));
        using var anonymous = factory.CreateClient();

        // Act
        var expiredResponse = await expired.GetAsync("/api/account/bootstrap");
        var anonymousResponse = await anonymous.GetAsync("/api/account/bootstrap");

        // Assert
        expiredResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousResponse.ShouldBeSuccessfulGetRequest();
        (await ReadJsonAsync(anonymousResponse)).GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Read_WhenSessionCookieAccompaniesBearerToken_ShouldAuthenticateFromBearerToken()
    {
        // Arrange
        using var client = CreateBearerOnlyClient(CreateAccessToken(DatabaseSeeder.Tenant1Member));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/bootstrap");
        request.Headers.Add("Cookie", $"{AuthenticationTokenHttpKeys.RefreshTokenCookieName}={Faker.Random.AlphaNumeric(40)}");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        (await ReadJsonAsync(response)).GetProperty("user").GetProperty("email").GetString().Should().Be(DatabaseSeeder.Tenant1Member.Email);
    }

    [Fact]
    public async Task Write_WhenBearerTokenAndNoAntiforgeryToken_ShouldReturnBadRequest()
    {
        // Arrange
        using var client = CreateBearerOnlyClient(CreateAccessToken(DatabaseSeeder.Tenant1Owner));

        // Act
        var response = await client.PostAsync(LogoutUrl, null);

        // Assert
        await AssertAntiforgeryRejectedAsync(response);
    }

    [Fact]
    public async Task Write_WhenSessionCookieAndNoAntiforgeryHeader_ShouldReturnBadRequest()
    {
        // Arrange
        using var client = CreateBearerOnlyClient(CreateAccessToken(DatabaseSeeder.Tenant1Owner));
        var (antiforgeryCookie, _) = await GetAntiforgeryPairAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, LogoutUrl);
        request.Headers.Add("Cookie", $"{AuthenticationTokenHttpKeys.RefreshTokenCookieName}={Faker.Random.AlphaNumeric(40)}; {antiforgeryCookie}");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        await AssertAntiforgeryRejectedAsync(response);
    }

    [Fact]
    public async Task StartEmailLogin_WhenAnonymousFormHasNoAntiforgeryToken_ShouldReturnBadRequest()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/account/authentication/email/login/start", new { email = Faker.Internet.Email() });

        // Assert
        await AssertAntiforgeryRejectedAsync(response);
    }

    [Fact]
    public async Task StartEmailLogin_WhenAnonymousFormUsesBootstrapAntiforgeryToken_ShouldSucceed()
    {
        // Arrange
        using var client = factory.CreateClient();
        var (antiforgeryCookie, requestToken) = await GetAntiforgeryPairAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/authentication/email/login/start");
        request.Content = JsonContent.Create(new { email = DatabaseSeeder.Tenant1Owner.Email });
        request.Headers.Add("Cookie", antiforgeryCookie);
        request.Headers.Add(AntiforgeryHeaderName, requestToken);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Write_WhenAntiforgeryTokenWasIssuedToAnotherIdentity_ShouldReturnBadRequestUntilBootstrapIsReadAgain()
    {
        // Arrange
        using var ownerClient = CreateBearerOnlyClient(CreateAccessToken(DatabaseSeeder.Tenant1Owner));
        using var memberClient = CreateBearerOnlyClient(CreateAccessToken(DatabaseSeeder.Tenant1Member));
        var (ownerCookie, ownerToken) = await GetAntiforgeryPairAsync(ownerClient);
        var (memberCookie, memberToken) = await GetAntiforgeryPairAsync(memberClient);

        // Act
        var withOwnerToken = await memberClient.SendAsync(CreateLogoutRequest(ownerCookie, ownerToken));
        var withMemberToken = await memberClient.SendAsync(CreateLogoutRequest(memberCookie, memberToken));

        // Assert
        await AssertAntiforgeryRejectedAsync(withOwnerToken);
        withMemberToken.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage CreateLogoutRequest(string antiforgeryCookie, string requestToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LogoutUrl);
        request.Headers.Add("Cookie", antiforgeryCookie);
        request.Headers.Add(AntiforgeryHeaderName, requestToken);
        return request;
    }

    private static async Task<(string Cookie, string RequestToken)> GetAntiforgeryPairAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/account/bootstrap");
        response.ShouldBeSuccessfulGetRequest();
        var cookie = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith($"{AuthenticationTokenHttpKeys.AntiforgeryTokenCookieName}=", StringComparison.Ordinal))
            .Split(';')[0];
        var requestToken = (await ReadJsonAsync(response)).GetProperty("antiforgeryToken").GetString()!;
        return (cookie, requestToken);
    }

    private static async Task AssertAntiforgeryRejectedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response)).GetProperty("title").GetString().Should().Be("Invalid Antiforgery Token");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private HttpClient CreateBearerOnlyClient(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private string CreateAccessToken(User user)
    {
        return new AccessTokenGenerator(WebApplicationServices.GetRequiredService<ITokenSigningClient>(), TimeProvider.System).Generate(ToUserInfo(user));
    }

    private string CreateExpiredAccessToken(User user)
    {
        var issuedTenMinutesAgo = new FixedTimeProvider(TimeProvider.System.GetUtcNow().AddMinutes(-10));
        return new AccessTokenGenerator(WebApplicationServices.GetRequiredService<ITokenSigningClient>(), issuedTenMinutesAgo).Generate(ToUserInfo(user));
    }

    private UserInfo ToUserInfo(User user)
    {
        return new UserInfo
        {
            IsAuthenticated = true,
            Id = user.Id,
            TenantId = user.TenantId,
            Role = user.Role.ToString(),
            SessionId = user == DatabaseSeeder.Tenant1Owner ? DatabaseSeeder.Tenant1OwnerSession.Id : DatabaseSeeder.Tenant1MemberSession.Id,
            Email = user.Email,
            Locale = user.Locale
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
