using System.Net;
using System.Net.Http.Json;
using Account.Database;
using Account.Features.EmailAuthentication.Commands;
using FluentAssertions;
using SharedKernel.ApiResults;
using Xunit;
using StartEmailLoginRequest = Account.Features.EmailAuthentication.Requests.StartEmailLoginCommand;

namespace Account.Tests.Contracts;

// A client posts the contract request records and reads the contract responses with the shared API JSON options
public sealed class ContractsWireFormatTests(AccountWebApplicationFactory factory) : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<AccountWebApplicationFactory>
{
    [Fact]
    public async Task StartEmailLogin_WhenPostingContractRequest_ShouldReturnContractResponseWithEmailLoginId()
    {
        // Arrange
        var request = new StartEmailLoginRequest(DatabaseSeeder.Tenant1Owner.Email);

        // Act
        var response = await AnonymousHttpClient.PostAsJsonAsync("/api/account/authentication/email/login/start", request, ApiJsonSerializerOptions.Create());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var startEmailLoginResponse = await response.Content.ReadFromJsonAsync<StartEmailLoginResponse>(ApiJsonSerializerOptions.Create());
        startEmailLoginResponse!.EmailLoginId.Value.Should().StartWith("emlog_");
        startEmailLoginResponse.ValidForSeconds.Should().BePositive();
    }

    [Fact]
    public async Task StartEmailLogin_WhenContractRequestIsInvalid_ShouldReturnProblemDetailsResponseWithErrors()
    {
        // Arrange
        var request = new StartEmailLoginRequest("invalid-email");

        // Act
        var response = await AnonymousHttpClient.PostAsJsonAsync("/api/account/authentication/email/login/start", request, ApiJsonSerializerOptions.Create());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(ApiJsonSerializerOptions.Create());
        problemDetails!.Status.Should().Be((int)HttpStatusCode.BadRequest);
        problemDetails.Title.Should().Be("Bad Request");
        problemDetails.Errors.Should().ContainKey("email").WhoseValue.Should().Equal("Email must be in a valid format and no longer than 100 characters.");
    }
}
