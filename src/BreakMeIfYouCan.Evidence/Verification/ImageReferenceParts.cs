namespace BreakMeIfYouCan.Evidence.Verification;

/// <summary>
/// Splits an OCI image reference into the registry and repository that the platform must
/// report for the running image. Tags and digests are deliberately discarded.
/// </summary>
public sealed record ImageReferenceParts(string Registry, string Repository)
{
    public static ImageReferenceParts? Parse(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var value = reference.Trim();

        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            value = value[..at];
        }

        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == value.Length - 1)
        {
            return null;
        }

        var registry = NormalizeRegistry(value[..slash]);
        var repository = value[(slash + 1)..];

        // Strip a tag only from the final path segment, so a registry port is not mistaken for one.
        var lastSlash = repository.LastIndexOf('/');
        var colon = repository.IndexOf(':', lastSlash + 1);
        if (colon >= 0)
        {
            repository = repository[..colon];
        }

        if (string.IsNullOrWhiteSpace(registry) || string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        return new ImageReferenceParts(registry, repository);
    }

    public static string NormalizeRegistry(string? registry)
    {
        var value = (registry ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
        return value switch
        {
            "registry-1.docker.io" => "docker.io",
            "index.docker.io" => "docker.io",
            _ => value,
        };
    }

    public bool Matches(string? registry, string? repository) =>
        !string.IsNullOrWhiteSpace(registry) &&
        !string.IsNullOrWhiteSpace(repository) &&
        string.Equals(NormalizeRegistry(registry), Registry, StringComparison.Ordinal) &&
        string.Equals(repository.Trim(), Repository, StringComparison.OrdinalIgnoreCase);
}
