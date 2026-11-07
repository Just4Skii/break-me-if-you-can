using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BreakMeIfYouCan.Api.Arena;
using BreakMeIfYouCan.Api.Data;
using BreakMeIfYouCan.Api.Health;
using BreakMeIfYouCan.Api.Infrastructure;
using BreakMeIfYouCan.Api.Proof;
using BreakMeIfYouCan.Evidence.Verification;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<ArenaOptions>(builder.Configuration.GetSection(ArenaOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<ProofOptions>(builder.Configuration.GetSection(ProofOptions.SectionName));

// Resolved lazily so the host configuration (including test and deployment overrides)
// is final before the provider is configured.
builder.Services.AddDbContext<ArenaDbContext>((provider, options) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Postgres");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "A PostgreSQL connection string is required. Set the ConnectionStrings__Postgres environment variable.");
    }

    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsHistoryTable("__ef_migrations_history", "arena");
        npgsql.CommandTimeout(15);
    });
});

builder.Services.AddDataProtection().SetApplicationName("break-me-if-you-can");

builder.Services.AddSingleton<CommitInfo>();
builder.Services.AddSingleton<IRunningBuild>(sp => sp.GetRequiredService<CommitInfo>());
builder.Services.AddSingleton<VisitorCookie>();
builder.Services.AddSingleton<ArenaConcurrencyGate>();
builder.Services.AddSingleton<LedgerIntegrityService>();
builder.Services.AddSingleton<TransferExecutor>();
builder.Services.AddSingleton<ArenaRunService>();
builder.Services.AddScoped<SandboxService>();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(GitHubActionsClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient(FlyRuntimeProbe.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<GitHubActionsClient>();
builder.Services.AddSingleton<ICiEvidenceProvider, CiEvidenceProvider>();
builder.Services.AddSingleton<IDeploymentIntentProvider, DeploymentIntentProvider>();
builder.Services.AddSingleton<IRuntimeImageProbe, FlyRuntimeProbe>();
builder.Services.AddSingleton<IArtifactAttestationProbe, GitHubAttestationProbe>();
builder.Services.AddSingleton<ProofVerifier>();
builder.Services.AddSingleton<ProofService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("arena", context => FixedWindow(VisitorPartition(context), permitLimit: 240));
    options.AddPolicy("arena-write", context => FixedWindow(VisitorPartition(context), permitLimit: 180));
    options.AddPolicy("arena-run", context => FixedWindow(VisitorPartition(context), permitLimit: 12));
    options.AddPolicy("proof", context => FixedWindow(IpPartition(context), permitLimit: 120));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://breakmeifyoucan.dev/problems/rate-limited",
            title = "Too many requests",
            status = 429,
            detail = "The arena admits a bounded number of requests per visitor per minute. Wait a moment and try again.",
            instance = context.HttpContext.Request.Path.Value,
        }, cancellationToken);
    };
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<SelfHealthCheck>("process", tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddHostedService<DatabaseBootstrapper>();

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
}

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Postgres")))
{
    throw new InvalidOperationException(
        "A PostgreSQL connection string is required. Set the ConnectionStrings__Postgres environment variable.");
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseMiddleware<ArenaExceptionMiddleware>();
app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapArenaEndpoints();
app.MapProofEndpoints();

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = WriteHealthReport,
});

app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthReport,
});

app.MapGet("/api/meta", (CommitInfo commit, IWebHostEnvironment environment, IOptions<ProofOptions> proof) => Results.Ok(new
{
    name = "Break Me If You Can",
    commit = commit.CommitSha,
    shortCommit = commit.ShortCommitSha,
    environment = environment.EnvironmentName,
    startedAt = commit.StartedAt,
    runtime = Environment.Version.ToString(),
    repository = proof.Value.Repository,
    repositoryUrl = proof.Value.RepositoryUrl,
    workflowFile = proof.Value.WorkflowFile,
}));

var webRoot = app.Environment.WebRootPath;
if (!string.IsNullOrWhiteSpace(webRoot) && File.Exists(Path.Combine(webRoot, "index.html")))
{
    app.MapFallbackToFile("index.html");
}
else
{
    app.MapFallback(() => Results.Text(
        "The web client is not bundled in this build. Run 'npm ci && npm run build' in web/ (see README).",
        "text/plain"));
}

app.Run();

static string VisitorPartition(HttpContext context)
{
    var cookie = context.RequestServices.GetRequiredService<VisitorCookie>();
    return cookie.Resolve(context).ToString("N");
}

static string IpPartition(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static RateLimitPartition<string> FixedWindow(string partitionKey, int permitLimit) =>
    RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    });

static Task WriteHealthReport(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString().ToLowerInvariant(),
        durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 3),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString().ToLowerInvariant(),
            durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 3),
            description = entry.Value.Description,
            data = entry.Value.Data,
        }),
    });
}

/// <summary>Exposed so integration tests can host the real application.</summary>
public partial class Program;