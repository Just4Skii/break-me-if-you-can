using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Health;

/// <summary>
/// Applies schema migrations at start-up, behind a PostgreSQL advisory lock so two
/// concurrent machines cannot migrate at the same time. This keeps a single-container
/// deployment reproducible without a separate migration job.
/// </summary>
public sealed class DatabaseBootstrapper(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseBootstrapper> logger) : IHostedService
{
    private const long AdvisoryLockKey = 82_347_102;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoMigrate)
        {
            logger.LogInformation("Database:AutoMigrate is disabled; skipping migrations.");
            return;
        }

        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();

                await db.Database.OpenConnectionAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})", [AdvisoryLockKey], cancellationToken);

                try
                {
                    var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                    if (pending.Count > 0)
                    {
                        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                        await db.Database.MigrateAsync(cancellationToken);
                    }
                    else
                    {
                        logger.LogInformation("Database schema is up to date.");
                    }
                }
                finally
                {
                    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", [AdvisoryLockKey], cancellationToken);
                }

                return;
            }
            catch (Exception exception) when (attempt < maxAttempts)
            {
                logger.LogWarning(exception, "Database is not ready (attempt {Attempt}/{Max}); retrying in 2 seconds.", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new InvalidOperationException("The database could not be prepared after " + maxAttempts + " attempts.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
