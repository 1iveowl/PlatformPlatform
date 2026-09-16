using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Account.Database;
using Account.Features.EmailAuthentication.Commands;
using FluentAssertions;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using Xunit;
using BulkDeleteUsersRequest = Account.Features.Users.Requests.BulkDeleteUsersCommand;
using ServerBulkDeleteUsersCommand = Account.Features.Users.Commands.BulkDeleteUsersCommand;
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

    [Fact]
    public void BulkDeleteUsers_WhenSerializingContractRequest_ShouldEqualServerCommandJson()
    {
        // Arrange
        UserId[] userIds = [UserId.NewId(), UserId.NewId()];

        // Act
        var requestJson = JsonSerializer.Serialize(new BulkDeleteUsersRequest(userIds), ApiJsonSerializerOptions.Create());

        // Assert
        requestJson.Should().Be(JsonSerializer.Serialize(new ServerBulkDeleteUsersCommand(userIds), ApiJsonSerializerOptions.Create()));
        requestJson.Should().Be($$"""{"userIds":["{{userIds[0]}}","{{userIds[1]}}"]}""");
    }

    [Fact]
    public async Task BulkDeleteUsers_WhenPostingContractRequestWithUnknownUser_ShouldReturnServerCommandError()
    {
        // Arrange
        var unknownUserId = UserId.NewId();
        var request = new BulkDeleteUsersRequest([unknownUserId]);

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync("/api/account/users/bulk-delete", request, ApiJsonSerializerOptions.Create());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(ApiJsonSerializerOptions.Create());
        problemDetails!.Detail.Should().Be($"Users with ids '{unknownUserId}' not found.");
    }
}
