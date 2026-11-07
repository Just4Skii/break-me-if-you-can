namespace BreakMeIfYouCan.Api.Competition;

public sealed class CompetitionException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
