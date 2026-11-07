using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakMeIfYouCan.Api.Competition;

public sealed record AuthResult(string Token, Player Player);

/// <summary>
/// Username/password accounts without email. A successful registration or login returns a
/// bearer token; only its SHA-256 hash is stored, so a database leak cannot replay sessions.
/// </summary>
public sealed partial class PlayerAuthService(ArenaDbContext db)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public async Task<AuthResult> RegisterAsync(string username, string password, CancellationToken cancellationToken)
    {
        var displayName = ValidateUsername(username);
        ValidatePassword(password);

        var normalized = displayName.ToLowerInvariant();
        if (await db.Players.AnyAsync(player => player.UsernameNormalized == normalized, cancellationToken))
        {
            throw new CompetitionException(StatusCodes.Status409Conflict, "That username is already taken.");
        }

        var now = DateTimeOffset.UtcNow;
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Username = displayName,
            UsernameNormalized = normalized,
            PasswordHash = PlayerPasswords.Hash(password),
            CreatedAt = now,
        };

        var (token, tokenHash) = CreateToken();
        player.Sessions.Add(new PlayerSession
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(SessionLifetime),
        });

        db.Players.Add(player);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new CompetitionException(StatusCodes.Status409Conflict, "That username is already taken.");
        }

        return new AuthResult(token, player);
    }

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var normalized = (username ?? string.Empty).Trim().ToLowerInvariant();
        var player = await db.Players.SingleOrDefaultAsync(p => p.UsernameNormalized == normalized, cancellationToken);

        if (player is null || !PlayerPasswords.Verify(password ?? string.Empty, player.PasswordHash))
        {
            throw new CompetitionException(StatusCodes.Status401Unauthorized, "Invalid username or password.");
        }

        var now = DateTimeOffset.UtcNow;
        var (token, tokenHash) = CreateToken();
        db.PlayerSessions.Add(new PlayerSession
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(SessionLifetime),
        });

        await db.SaveChangesAsync(cancellationToken);
        return new AuthResult(token, player);
    }

    public async Task<PlayerSession?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header["Bearer ".Length..].Trim();
        if (token.Length < 20)
        {
            return null;
        }

        var hash = HashToken(token);
        var now = DateTimeOffset.UtcNow;

        return await db.PlayerSessions
            .Include(session => session.Player)
            .SingleOrDefaultAsync(
                session => session.TokenHash == hash && session.RevokedAt == null && session.ExpiresAt > now,
                cancellationToken);
    }

    public async Task LogoutAsync(PlayerSession session, CancellationToken cancellationToken)
    {
        session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string ValidateUsername(string username)
    {
        var value = (username ?? string.Empty).Trim();
        if (!UsernamePattern().IsMatch(value))
        {
            throw new CompetitionException(
                StatusCodes.Status422UnprocessableEntity,
                "Username must be 3-24 characters and use only letters, numbers, underscores or hyphens.");
        }

        return value;
    }

    private static void ValidatePassword(string password)
    {
        if (password is null || password.Length is < 8 or > 128)
        {
            throw new CompetitionException(
                StatusCodes.Status422UnprocessableEntity,
                "Password must be between 8 and 128 characters.");
        }
    }

    private static (string Token, string Hash) CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, HashToken(token));
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9_-]{3,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
