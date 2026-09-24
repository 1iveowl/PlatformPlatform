using Account.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using SharedKernel.Database;
using SharedKernel.ExecutionContext;
using Xunit;

namespace Account.Tests.Workers;

public sealed class DataMigrationRunnerPostgreSqlTests
{
    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunMigrationsAsync_WhenRoleCannotCreateInSchemaAndHistoryTableExists_ShouldSucceed(bool locksThroughDataSource)
    {
        // Arrange
        var connectionString = Environment.GetEnvironmentVariable("ACCOUNT_TEST_POSTGRES")!;
        var schema = $"runner_{Guid.NewGuid():N}";
        var role = $"runner_{Guid.NewGuid():N}";
        const string rolePassword = "data-migration-runner";
        await using var administration = new NpgsqlConnection(connectionString);
        await administration.OpenAsync();
        await using var administrationCommand = administration.CreateCommand();

        try
        {
            administrationCommand.CommandText = $"""
                                                 CREATE SCHEMA {schema};
                                                 CREATE TABLE {schema}.__data_migrations_history (
                                                     migration_id text NOT NULL,
                                                     product_version text NOT NULL,
                                                     executed_at timestamptz NOT NULL,
                                                     execution_time_ms bigint NOT NULL,
                                                     summary text NOT NULL,
                                                     CONSTRAINT pk___data_migrations_history PRIMARY KEY (migration_id)
                                                 );
                                                 CREATE ROLE {role} LOGIN PASSWORD '{rolePassword}';
                                                 GRANT USAGE ON SCHEMA {schema} TO {role};
                                                 GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {role};
                                                 """;
            await administrationCommand.ExecuteNonQueryAsync();

            var roleConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Username = role, Password = rolePassword, SearchPath = schema, Pooling = false
            }.ConnectionString;
            var options = new DbContextOptionsBuilder<AccountDbContext>().UseNpgsql(roleConnectionString).UseSnakeCaseNamingConvention().Options;
            await using var context = new AccountDbContext(options, Substitute.For<IExecutionContext>(), TimeProvider.System);
            var serviceCollection = new ServiceCollection().AddLogging().AddSingleton(context).AddSingleton(TimeProvider.System);
            await using var dataSource = NpgsqlDataSource.Create(roleConnectionString);
            if (locksThroughDataSource) serviceCollection.AddSingleton(dataSource);
            var services = serviceCollection.BuildServiceProvider();

            var dataMigrationIds = typeof(AccountDbContext).Assembly.GetTypes()
                .Where(t => typeof(IDataMigration).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
                .Select(t => ((IDataMigration)ActivatorUtilities.CreateInstance(services, t)).Id)
                .ToArray();
            dataMigrationIds.Should().NotBeEmpty();
            foreach (var dataMigrationId in dataMigrationIds)
            {
                administrationCommand.CommandText = $"INSERT INTO {schema}.__data_migrations_history VALUES ('{dataMigrationId}', 'test', now(), 0, 'applied')";
                await administrationCommand.ExecuteNonQueryAsync();
            }

            var runner = new DataMigrationRunner<AccountDbContext>(context, services, NullLogger<DataMigrationRunner<AccountDbContext>>.Instance);

            // Act
            var act = () => runner.RunMigrationsAsync(CancellationToken.None);

            // Assert
            await act.Should().NotThrowAsync();
        }
        finally
        {
            administrationCommand.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE; DROP ROLE IF EXISTS {role};";
            await administrationCommand.ExecuteNonQueryAsync();
        }
    }

    private sealed class PostgreSqlTheoryAttribute : TheoryAttribute
    {
        public PostgreSqlTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNT_TEST_POSTGRES")))
            {
                Skip = "Set ACCOUNT_TEST_POSTGRES to an isolated PostgreSQL database to run this test.";
            }
        }
    }
}
