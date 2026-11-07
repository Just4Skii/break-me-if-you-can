namespace BreakMeIfYouCan.Api.Infrastructure;

public sealed class RuntimeOptions
{
    public const string SectionName = "Runtime";

    /// <summary>
    /// Operational kill switch. When enabled the arena endpoints return 503 while health
    /// checks and the engineering proof stay available. Toggled by a deployment secret and
    /// a restart, so it cannot be flipped by a visitor.
    /// </summary>
    public bool KillSwitch { get; set; }
}
