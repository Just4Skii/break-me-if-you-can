using BreakMeIfYouCan.Evidence.Verification;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class ImageReferencePartsTests
{
    [Theory]
    [InlineData("ghcr.io/owner/repo", "ghcr.io", "owner/repo")]
    [InlineData("ghcr.io/owner/repo:sha-abc", "ghcr.io", "owner/repo")]
    [InlineData("ghcr.io/owner/repo@sha256:" + "a", "ghcr.io", "owner/repo")]
    [InlineData("registry-1.docker.io/library/ubuntu:latest", "docker.io", "library/ubuntu")]
    [InlineData("localhost:5000/team/app", "localhost:5000", "team/app")]
    public void Parse_SplitsRegistryAndRepository(string reference, string registry, string repository)
    {
        var parsed = ImageReferenceParts.Parse(reference);

        Assert.NotNull(parsed);
        Assert.Equal(registry, parsed.Registry);
        Assert.Equal(repository, parsed.Repository);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("repo-only")]
    [InlineData("ghcr.io/")]
    public void Parse_RejectsReferencesWithoutARegistryAndRepository(string? reference)
    {
        Assert.Null(ImageReferenceParts.Parse(reference));
    }

    [Fact]
    public void Matches_IsCaseInsensitiveAndNormalisesDockerRegistries()
    {
        var expected = ImageReferenceParts.Parse("ghcr.io/Just4Skii/break-me-if-you-can");

        Assert.NotNull(expected);
        Assert.True(expected.Matches("ghcr.io", "just4skii/break-me-if-you-can"));
        Assert.True(expected.Matches("GHCR.IO", "JUST4SKII/BREAK-ME-IF-YOU-CAN"));
        Assert.False(expected.Matches("ghcr.io", "someone-else/another-repo"));
        Assert.False(expected.Matches(null, "just4skii/break-me-if-you-can"));
    }

    [Fact]
    public void NormalizeRegistry_MapsDockerAliases()
    {
        Assert.Equal("docker.io", ImageReferenceParts.NormalizeRegistry("registry-1.docker.io"));
        Assert.Equal("docker.io", ImageReferenceParts.NormalizeRegistry("index.docker.io"));
        Assert.Equal("ghcr.io", ImageReferenceParts.NormalizeRegistry("GHCR.IO"));
    }
}
