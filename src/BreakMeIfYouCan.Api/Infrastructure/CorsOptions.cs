namespace BreakMeIfYouCan.Api.Infrastructure;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Exact origins allowed to call the API from a browser. The GitHub Pages origin is
    /// allowed by default; every other deployment must set this explicitly.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
