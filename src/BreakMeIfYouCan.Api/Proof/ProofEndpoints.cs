namespace BreakMeIfYouCan.Api.Proof;

public static class ProofEndpoints
{
    public static IEndpointRouteBuilder MapProofEndpoints(this IEndpointRouteBuilder app)
    {
        // Content negotiation keeps a single canonical URL: a browser gets the client,
        // an API caller gets the evidence document.
        app.MapGet("/proof", async (
            HttpContext context,
            ProofService proof,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            var accept = context.Request.Headers.Accept.ToString();
            var wantsHtml = accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
            var wantsJson = accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);

            if (wantsHtml && !wantsJson)
            {
                var webRoot = environment.WebRootPath;
                if (!string.IsNullOrWhiteSpace(webRoot))
                {
                    var indexPath = Path.Combine(webRoot, "index.html");
                    if (File.Exists(indexPath))
                    {
                        return Results.File(indexPath, "text/html; charset=utf-8");
                    }
                }
            }

            var report = await proof.GetReportAsync(cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Evidence-State"] = report.State.ToString().ToLowerInvariant();
            return Results.Ok(report);
        })
        .WithName("GetEngineeringProof")
        .RequireRateLimiting("proof");

        return app;
    }
}
