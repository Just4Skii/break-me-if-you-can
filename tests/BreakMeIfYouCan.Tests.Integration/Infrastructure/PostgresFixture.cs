using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BreakMeIfYouCan.Tests.Integration.Infrastructure;

/// <summary>
/// One real PostgreSQL instance for the whole integration suite. By default it is a
/// throwaway container; set TEST_POSTGRES to reuse an existing database (for example a
/// CI service container) when Docker is not available.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("breakme")
                .WithUsername("breakme")
                .WithPassword(Guid.NewGuid().ToString("N"))
                .Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        var options = new DbContextOptionsBuilder<ArenaDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "arena"))
            .Options;

        await using var db = new ArenaDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
