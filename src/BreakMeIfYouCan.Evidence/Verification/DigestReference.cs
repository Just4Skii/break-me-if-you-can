namespace BreakMeIfYouCan.Evidence.Verification;

/// <summary>
/// Parsing and comparison of container image digests. Comparing strings naively is how
/// provenance bugs get shipped, so every comparison goes through this type.
/// </summary>
public static class DigestReference
{
    private const string Algorithm = "sha256:";

    private const int HexLength = 64;

    /// <summary>
    /// Extracts a canonical sha256 digest from an image reference or a bare digest.
    /// Returns null when the value is absent or malformed.
    /// </summary>
    public static string? Extract(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var atIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (atIndex >= 0)
        {
            trimmed = trimmed[(atIndex + 1)..];
        }

        if (!trimmed.StartsWith(Algorithm, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = trimmed[Algorithm.Length..].Trim();
        if (hex.Length != HexLength || !IsHex(hex))
        {
            return null;
        }

        return Algorithm + hex.ToLowerInvariant();
    }

    public static bool Matches(string? left, string? right)
    {
        var a = Extract(left);
        var b = Extract(right);
        return a is not null && b is not null && string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>Builds an oci:// reference usable with "gh attestation verify".</summary>
    public static string? BuildOciReference(string? imageReference, string? digest)
    {
        var canonical = Extract(digest);
        if (canonical is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(imageReference))
        {
            return "oci://" + canonical;
        }

        var reference = imageReference.Trim();
        var atIndex = reference.IndexOf('@', StringComparison.Ordinal);
        if (atIndex >= 0)
        {
            reference = reference[..atIndex];
        }

        return "oci://" + reference + "@" + canonical;
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
