namespace BreakMeIfYouCan.Api.Competition;

/// <summary>
/// A competition account. Usernames are public; passwords are PBKDF2 hashes and there is no
/// email address, so the service stores the minimum needed to rank a run.
/// </summary>
public sealed class Player
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string UsernameNormalized { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<PlayerSession> Sessions { get; set; } = [];

    public ICollection<ChallengeRun> Runs { get; set; } = [];
}

/// <summary>A revocable bearer session. Only the SHA-256 hash of the token is stored.</summary>
public sealed class PlayerSession
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }

    public Player? Player { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// A server-executed challenge run. The metrics are written by the API after it has run the
/// transfers; the client never supplies a score.
/// </summary>
public sealed class ChallengeRun
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }

    public Player? Player { get; set; }

    public Guid SandboxId { get; set; }

    public string ChallengeKey { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;

    public int Requests { get; set; }

    public int Concurrency { get; set; }

    public long AmountCents { get; set; }

    public string Pattern { get; set; } = string.Empty;

    public string Idempotency { get; set; } = string.Empty;

    public int Accepted { get; set; }

    public int Rejected { get; set; }

    public int Replayed { get; set; }

    public int TotalRetries { get; set; }

    public int Conflicts { get; set; }

    public double DurationMs { get; set; }

    public long Score { get; set; }

    public bool Verified { get; set; }

    public string IntegrityJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
