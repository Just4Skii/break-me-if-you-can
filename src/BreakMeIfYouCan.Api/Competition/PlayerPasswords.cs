using System.Security.Cryptography;

namespace BreakMeIfYouCan.Api.Competition;

/// <summary>PBKDF2-SHA256 password hashing with a per-password salt and constant-time verification.</summary>
public static class PlayerPasswords
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return "pbkdf2-sha256$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(key);
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2-sha256", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations is < 10_000 or > 2_000_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
