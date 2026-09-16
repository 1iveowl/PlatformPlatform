using System.Text.Json;
using Account.Features.EmailAuthentication.Domain;
using FluentAssertions;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using Xunit;

namespace SharedKernel.Tests.StronglyTypedIds;

public sealed class StronglyTypedIdJsonConverterTests
{
    private static readonly JsonSerializerOptions ApiOptions = ApiJsonSerializerOptions.Create();

    [Fact]
    public void RoundTrip_WhenUserId_ShouldWritePrefixedStringAndReadSameId()
    {
        // Arrange
        var userId = UserId.NewId();

        // Act
        var json = JsonSerializer.Serialize(userId, ApiOptions);
        var deserialized = JsonSerializer.Deserialize<UserId>(json, ApiOptions);

        // Assert
        json.Should().Be($"\"{userId.Value}\"");
        json.Should().MatchRegex("^\"usr_[A-Z0-9]{26}\"$");
        deserialized.Should().Be(userId);
    }

    [Fact]
    public void RoundTrip_WhenTenantId_ShouldWriteNumberAsStringAndReadSameId()
    {
        // Arrange
        var tenantId = TenantId.NewId();

        // Act
        var json = JsonSerializer.Serialize(tenantId, ApiOptions);
        var deserialized = JsonSerializer.Deserialize<TenantId>(json, ApiOptions);

        // Assert
        json.Should().Be($"\"{tenantId.Value}\"");
        deserialized.Should().Be(tenantId);
    }

    [Fact]
    public void RoundTrip_WhenEmailLoginId_ShouldWritePrefixedStringAndReadSameId()
    {
        // Arrange
        var emailLoginId = EmailLoginId.NewId();

        // Act
        var json = JsonSerializer.Serialize(emailLoginId, ApiOptions);
        var deserialized = JsonSerializer.Deserialize<EmailLoginId>(json, ApiOptions);

        // Assert
        json.Should().MatchRegex("^\"emlog_[A-Z0-9]{26}\"$");
        deserialized.Should().Be(emailLoginId);
    }

    [Fact]
    public void Read_WhenIdsAreNestedAndNullable_ShouldReadIdsAndNull()
    {
        // Arrange
        const string json = """{"userId":"usr_01JMVAW4T4320KJ3A7EJMCG8R0","tenantId":"42","emailLoginId":null}""";

        // Act
        var deserialized = JsonSerializer.Deserialize<IdHolder>(json, ApiOptions)!;

        // Assert
        deserialized.UserId.Should().Be(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"));
        deserialized.TenantId.Should().Be(new TenantId(42));
        deserialized.EmailLoginId.Should().BeNull();
    }

    [Fact]
    public void Read_WhenPrefixBelongsToAnotherId_ShouldThrowJsonException()
    {
        // Arrange
        var json = JsonSerializer.Serialize(UserId.NewId(), ApiOptions);

        // Act
        var act = () => JsonSerializer.Deserialize<EmailLoginId>(json, ApiOptions);

        // Assert
        act.Should().Throw<JsonException>().WithMessage("Unable to convert EmailLoginId.");
    }

    private sealed record IdHolder(UserId UserId, TenantId TenantId, EmailLoginId? EmailLoginId);
}
