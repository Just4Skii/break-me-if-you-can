namespace BreakMeIfYouCan.Api.Proof;

public static class ProofEndpoints
{
    public static IEndpointRouteBuilder MapProofEndpoints(this IEndpointRouteBuilder app)
    {
        // The canonical, human-facing URL. A browser navigation receives the client;
        // an API caller receives the evidence document.
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

            return await WriteReportAsync(context, proof, cancellationToken);
        })
        .WithName("GetEngineeringProof")
        .RequireRateLimiting("proof");

        // Explicit JSON alias used by the client, so a client-side route and an API route
        // can never resolve to the same path with different expectations.
        app.MapGet("/api/proof", async (
            HttpContext context,
            ProofService proof,
            CancellationToken cancellationToken) =>
        {
            return await WriteReportAsync(context, proof, cancellationToken);
        })
        .WithName("GetEngineeringProofJson")
        .RequireRateLimiting("proof");

        return app;
    }

    private static async Task<IResult> WriteReportAsync(
        HttpContext context,
        ProofService proof,
        CancellationToken cancellationToken)
    {
        var report = await proof.GetReportAsync(cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Evidence-State"] = report.State.ToString().ToLowerInvariant();
        return Results.Ok(report);
    }
}
