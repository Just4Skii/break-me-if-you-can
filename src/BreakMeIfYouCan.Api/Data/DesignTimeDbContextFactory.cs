using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BreakMeIfYouCan.Api.Data;

/// <summary>
/// Lets "dotnet ef" scaffold migrations without starting the web host or needing a live
/// database. The connection string is only used for the provider's model conventions.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ArenaDbContext>
{
    public ArenaDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=breakme;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ArenaDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "arena"))
            .Options;

        return new ArenaDbContext(options);
    }
}
