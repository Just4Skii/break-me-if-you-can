using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BreakMeIfYouCan.Tests.Integration.Infrastructure;

public sealed class ApiFactory(
    PostgresFixture database,
    IReadOnlyDictionary<string, string?>? extraConfiguration = null) : WebApplicationFactory<Program>
{
    public string ConnectionString => database.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Postgres"] = database.ConnectionString,
                ["Database:AutoMigrate"] = "false",
                ["Arena:GlobalConcurrencyLimit"] = "8",
                ["Arena:MaxRetryAttempts"] = "10",
                ["Arena:BackoffBaseMilliseconds"] = "5",
                ["Arena:BackoffMaxMilliseconds"] = "200",
                ["Arena:MaxConcurrentRuns"] = "4",
                ["Logging:LogLevel:Default"] = "Warning",
            });

            if (extraConfiguration is not null)
            {
                configuration.AddInMemoryCollection(extraConfiguration);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Keep the real pipeline: the tests exercise routing, cookies, rate limiting and
        // the actual EF Core / Npgsql stack, not a stub.
        return base.CreateHost(builder);
    }
}