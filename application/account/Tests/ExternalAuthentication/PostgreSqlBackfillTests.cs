using System.Data.Common;
using System.Text.Json;
using Account.Database;
using Account.Database.DataMigrations;
using Account.Database.Migrations;
using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using SharedKernel.Domain;
using SharedKernel.EntityFramework;
using SharedKernel.ExecutionContext;
using Xunit;
using LegacyExternalIdentity = Account.Features.Users.Domain.ExternalIdentity;

namespace Account.Tests.ExternalAuthentication;

public sealed class PostgreSqlBackfillTests
{
    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenApiInsertsAfterSnapshot_ShouldPreserveBindingAndReconcileLateLegacyWrites(bool conflictsOnHolder)
    {
        // Arrange
        var connectionString = Environment.GetEnvironmentVariable("ACCOUNT_TEST_POSTGRES")!;
        var schema = $"backfill_{Guid.NewGuid():N}";
        await using var administration = new NpgsqlConnection(connectionString);
        await administration.OpenAsync();
        await using var schemaCommand = administration.CreateCommand();
        schemaCommand.CommandText = $"CREATE SCHEMA {schema}";
        await schemaCommand.ExecuteNonQueryAsync();
        var scopedConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        var userId = UserId.NewId();
        var otherUserId = UserId.NewId();
        var apiIdentity = ExternalIdentity.Create(new TenantId(1), conflictsOnHolder ? userId : otherUserId, ExternalProviderType.Google,
            conflictsOnHolder ? "current-key" : "legacy-key", "https://accounts.google.com", "api-subject"
        );
        var interceptor = new InsertAfterSnapshotInterceptor(async () =>
            {
                await using var apiContext = CreateContext(scopedConnectionString);
                apiContext.Set<ExternalIdentity>().Add(apiIdentity);
                await apiContext.SaveChangesAsync();
            }
        );

        try
        {
            await using var context = CreateContext(scopedConnectionString, interceptor);
            await context.Database.ExecuteSqlRawAsync("""
                                                      CREATE TABLE tenants (id bigint PRIMARY KEY);
                                                      CREATE TABLE users (tenant_id bigint NOT NULL, id text PRIMARY KEY, deleted_at timestamptz, external_identities jsonb NOT NULL DEFAULT '[]');
                                                      CREATE TABLE external_logins (id text PRIMARY KEY);
                                                      INSERT INTO tenants VALUES (1);
                                                      """
            );
            var commands = context.GetService<IMigrationsSqlGenerator>().Generate(new AddExternalIdentities().UpOperations.Concat(new AddExternalIdentityVerificationEvidence().UpOperations).ToArray());
            foreach (var command in commands)
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            Migration[] bindingMigrations = conflictsOnHolder
                ? [new AddExternalLoginAuditBinding(), new AddExternalLoginVerificationBinding(), new AddExternalLoginAuditBinding()]
                : [new AddExternalLoginVerificationBinding(), new AddExternalLoginAuditBinding(), new AddExternalLoginAuditBinding()];
            foreach (var bindingMigration in bindingMigrations)
            {
                foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(bindingMigration.UpOperations))
                {
                    await context.Database.ExecuteSqlRawAsync(command.CommandText);
                }
            }

            var legacyJson = JsonSerializer.Serialize(new[] { new LegacyExternalIdentity(ExternalProviderType.Google, "legacy-key") });
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (tenant_id, id, external_identities) VALUES (1, {userId.Value}, CAST({legacyJson} AS jsonb)), (1, {otherUserId.Value}, '[]');");
            var migration = new BackfillExternalIdentities(context, NullLogger<BackfillExternalIdentities>.Instance);

            // Act
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await migration.ExecuteAsync(CancellationToken.None);
                await transaction.CommitAsync();
            }

            // Assert
            interceptor.Inserted.Should().BeTrue();
            var identities = await context.Set<ExternalIdentity>().IgnoreQueryFilters([QueryFilterNames.Tenant]).ToArrayAsync();
            identities.Should().ContainSingle().Which.Id.Should().Be(apiIdentity.Id);
            identities[0].UserId.Should().Be(apiIdentity.UserId);
            identities[0].ProviderUserId.Should().Be(apiIdentity.ProviderUserId);
            var lateUserId = UserId.NewId();
            var lateJson = JsonSerializer.Serialize(new[] { new LegacyExternalIdentity(ExternalProviderType.Google, "late-key") });
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (tenant_id, id, external_identities) VALUES (1, {lateUserId.Value}, CAST({lateJson} AS jsonb));");
            var reconciliation = new ReconcileLegacyExternalIdentities(context, NullLogger<BackfillExternalIdentities>.Instance);
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await reconciliation.ExecuteAsync(CancellationToken.None);
                await transaction.RollbackAsync();
            }

            (await context.Set<ExternalIdentity>().IgnoreQueryFilters([QueryFilterNames.Tenant]).CountAsync()).Should().Be(1);
            await reconciliation.ExecuteAsync(CancellationToken.None);
            await reconciliation.ExecuteAsync(CancellationToken.None);
            identities = await context.Set<ExternalIdentity>().IgnoreQueryFilters([QueryFilterNames.Tenant]).ToArrayAsync();
            identities.Should().HaveCount(2);
            identities.Should().ContainSingle(e => e.UserId == lateUserId && e.ProviderUserId == "late-key");
            identities.Should().ContainSingle(e => e.Id == apiIdentity.Id && e.Subject == "api-subject");
        }
        finally
        {
            schemaCommand.CommandText = $"DROP SCHEMA {schema} CASCADE";
            await schemaCommand.ExecuteNonQueryAsync();
        }
    }

    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenLegacyVerificationFinishesAfterSchemaUpdate_ShouldReconcileAndPreserveEvidence(bool rollbackFirst)
    {
        // Arrange
        var connectionString = Environment.GetEnvironmentVariable("ACCOUNT_TEST_POSTGRES")!;
        var schema = $"mitid_rollout_{Guid.NewGuid():N}";
        await using var administration = new NpgsqlConnection(connectionString);
        await administration.OpenAsync();
        await using var schemaCommand = administration.CreateCommand();
        schemaCommand.CommandText = $"CREATE SCHEMA {schema}";
        await schemaCommand.ExecuteNonQueryAsync();
        var scopedConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;

        try
        {
            await using var context = CreateContext(scopedConnectionString);
            await context.Database.ExecuteSqlRawAsync("CREATE TABLE tenants (id bigint PRIMARY KEY); CREATE TABLE users (tenant_id bigint NOT NULL, id text PRIMARY KEY); INSERT INTO tenants VALUES (1);");
            var operations = new AddExternalIdentities().UpOperations.Concat(new AddExternalIdentityVerificationEvidence().UpOperations).ToArray();
            foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(operations))
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            var tenantId = new TenantId(1);
            var verifiedAt = new DateTimeOffset(2026, 9, 4, 14, 30, 22, TimeSpan.FromHours(2)).ToUniversalTime();
            var authenticatedAt = verifiedAt.AddMinutes(-1);
            var identities = Enumerable.Range(0, 6).Select(index => index < 2
                ? ExternalIdentity.Create(tenantId, UserId.NewId(), index == 0 ? ExternalProviderType.Google : ExternalProviderType.Entra, $"provider-{index}", "https://issuer.test.localhost", $"subject-{index}")
                : ExternalIdentity.CreateForVerification(tenantId, UserId.NewId(), ExternalProviderType.MitId, $"citizen-{index}", "https://issuer.test.localhost", $"subject-{index}", IdentityAssuranceLevel.Substantial, verifiedAt, authenticatedAt, ExternalLoginId.NewId())
            ).ToArray();
            foreach (var identity in identities)
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (tenant_id, id) VALUES (1, {identity.UserId.Value});");
            }

            context.AddRange(identities.Take(3));
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE external_identities SET capabilities = 'Verification' WHERE id = {identities[2].Id.Value};");
            foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(new GrantLoginToVerifiedExternalIdentities().UpOperations))
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            await using (var oldApi = CreateContext(scopedConnectionString))
            {
                oldApi.Add(identities[3]);
                await oldApi.SaveChangesAsync();
                await oldApi.Database.ExecuteSqlInterpolatedAsync($"UPDATE external_identities SET capabilities = 'Verification' WHERE id = {identities[3].Id.Value};");
            }

            context.AddRange(identities.Skip(4));
            await context.SaveChangesAsync();
            identities[5].RevokeVerification();
            context.Remove(identities[5]);
            await context.SaveChangesAsync();
            var before = await context.Set<ExternalIdentity>().IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking().ToArrayAsync();
            before.Single(e => e.Id == identities[3].Id).Capabilities.Should().Be(ExternalIdentityCapabilities.Verification);
            before.Single(e => e.Id == identities[2].Id).Capabilities.Should().Be(ExternalIdentityCapabilities.Login | ExternalIdentityCapabilities.Verification);
            before.Single(e => e.Id == identities[4].Id).Capabilities.Should().Be(ExternalIdentityCapabilities.Login | ExternalIdentityCapabilities.Verification);
            var reconciliation = new ReconcileVerifiedMitIdLoginCapabilities(context);

            // Act
            if (rollbackFirst)
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                await reconciliation.ExecuteAsync(CancellationToken.None);
                await transaction.RollbackAsync();
                var lateIdentity = await context.Set<ExternalIdentity>().IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking().SingleAsync(e => e.Id == identities[3].Id);
                lateIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Verification);
            }

            await reconciliation.ExecuteAsync(CancellationToken.None);
            await reconciliation.ExecuteAsync(CancellationToken.None);

            // Assert
            var after = await context.Set<ExternalIdentity>().IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking().ToArrayAsync();
            after.Should().BeEquivalentTo(before, options => options.Excluding(e => e.Capabilities));
            after.Should().HaveCount(5);
            after.Where(e => e.Provider == ExternalProviderType.MitId).Should().OnlyContain(e => e.Capabilities == (ExternalIdentityCapabilities.Login | ExternalIdentityCapabilities.Verification));
            after.Where(e => e.Provider != ExternalProviderType.MitId).Should().OnlyContain(e => e.Capabilities == ExternalIdentityCapabilities.Login);
            after.Should().NotContain(e => e.Id == identities[5].Id);
        }
        finally
        {
            schemaCommand.CommandText = $"DROP SCHEMA {schema} CASCADE";
            await schemaCommand.ExecuteNonQueryAsync();
        }
    }

    private static AccountDbContext CreateContext(string connectionString, DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AccountDbContext>().UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new AccountDbContext(options.Options, Substitute.For<IExecutionContext>(), TimeProvider.System);
    }

    private sealed class InsertAfterSnapshotInterceptor(Func<Task> insert) : DbCommandInterceptor
    {
        public bool Inserted { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Inserted && command.CommandText.Contains("FROM external_identities", StringComparison.Ordinal))
            {
                Inserted = true;
                await insert();
            }

            return result;
        }
    }

    public sealed class PostgreSqlTheoryAttribute : TheoryAttribute
    {
        public PostgreSqlTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNT_TEST_POSTGRES")))
            {
                Skip = "Set ACCOUNT_TEST_POSTGRES to an isolated PostgreSQL database to run concurrency tests.";
            }
        }
    }
}
