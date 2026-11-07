using BreakMeIfYouCan.Api.Arena;
using BreakMeIfYouCan.Api.Competition;
using Microsoft.AspNetCore.Mvc;

namespace BreakMeIfYouCan.Api.Infrastructure;

/// <summary>Maps expected arena failures to RFC 7807 problem details without leaking internals.</summary>
public sealed class ArenaExceptionMiddleware(RequestDelegate next, ILogger<ArenaExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (CompetitionException exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            logger.LogInformation("Competition request rejected with {StatusCode}: {Message}", exception.StatusCode, exception.Message);

            context.Response.Clear();
            context.Response.StatusCode = exception.StatusCode;

            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = exception.StatusCode,
                Title = "The request could not be completed",
                Detail = exception.Message,
                Type = "https://breakmeifyoucan.dev/problems/competition",
                Instance = context.Request.Path,
            });
        }
        catch (ArenaException exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            logger.LogInformation("Arena request rejected with {StatusCode}: {Message}", exception.StatusCode, exception.Message);

            context.Response.Clear();
            context.Response.StatusCode = exception.StatusCode;

            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = exception.StatusCode,
                Title = "The request could not be completed",
                Detail = exception.Message,
                Type = "https://breakmeifyoucan.dev/problems/arena",
                Instance = context.Request.Path,
            });
        }
    }
}
