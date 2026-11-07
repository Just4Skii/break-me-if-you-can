using BreakMeIfYouCan.Api.Data;
using BreakMeIfYouCan.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BreakMeIfYouCan.Api.Health;

public sealed class SelfHealthCheck(CommitInfo commit) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        _ = cancellationToken;

        var data = new Dictionary<string, object>
        {
            ["commit"] = commit.CommitSha,
            ["informationalVersion"] = commit.InformationalVersion,
            ["startedAt"] = commit.StartedAt.ToString("O"),
            ["uptimeSeconds"] = (DateTimeOffset.UtcNow - commit.StartedAt).TotalSeconds,
        };

        return Task.FromResult(HealthCheckResult.Healthy("The process is running.", data));
    }
}

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();
            var reachable = await db.Database.CanConnectAsync(cancellationToken);

            return reachable
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
        }
    }
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply migrations on start-up. Off by default in production is safer, on here for a single machine.</summary>
    public bool AutoMigrate { get; set; } = true;
}