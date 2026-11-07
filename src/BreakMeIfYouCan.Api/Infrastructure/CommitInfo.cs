using System.Reflection;

namespace BreakMeIfYouCan.Api.Infrastructure;

public interface IRunningBuild
{
    string CommitSha { get; }

    string InformationalVersion { get; }

    bool HasCommit { get; }
}

/// <summary>
/// The commit the running image was built from, read from assembly metadata compiled at
/// build time. A deployment environment variable is deliberately not used: an operator
/// could set it to match the evidence, which would make the proof meaningless.
/// </summary>
public sealed class CommitInfo : IRunningBuild
{
    public CommitInfo()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(CommitInfo).Assembly;

        CommitSha = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "CommitSha", StringComparison.Ordinal))
            ?.Value ?? "unknown";

        InformationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        StartedAt = DateTimeOffset.UtcNow;
    }

    public string CommitSha { get; }

    public string InformationalVersion { get; }

    public DateTimeOffset StartedAt { get; }

    public string ShortCommitSha => CommitSha.Length > 12 ? CommitSha[..12] : CommitSha;

    public bool HasCommit => CommitSha.Length == 40 && CommitSha.All(Uri.IsHexDigit);
}