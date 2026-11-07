using BreakMeIfYouCan.Evidence.Verification;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class DigestReferenceTests
{
    private static readonly string Sha = new('a', 64);

    [Theory]
    [InlineData("sha256:" + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "sha256:" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("ghcr.io/owner/repo@sha256:" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("  sha256:" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  ", "sha256:" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Extract_NormalisesDigests(string input, string expected)
    {
        Assert.Equal(expected, DigestReference.Extract(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:short")]
    [InlineData("md5:" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("ghcr.io/owner/repo:latest")]
    public void Extract_RejectsMissingOrMalformedDigests(string? input)
    {
        Assert.Null(DigestReference.Extract(input));
    }

    [Fact]
    public void Matches_ComparesCanonicalDigests()
    {
        Assert.True(DigestReference.Matches("sha256:" + Sha, "ghcr.io/owner/repo@sha256:" + Sha));
        Assert.False(DigestReference.Matches("sha256:" + Sha, "sha256:" + new string('b', 64)));
        Assert.False(DigestReference.Matches(null, "sha256:" + Sha));
    }

    [Fact]
    public void BuildOciReference_StripsAnExistingDigestSuffix()
    {
        var reference = DigestReference.BuildOciReference("ghcr.io/owner/repo@sha256:" + new string('c', 64), "sha256:" + Sha);
        Assert.Equal("oci://ghcr.io/owner/repo@sha256:" + Sha, reference);
    }

    [Fact]
    public void BuildOciReference_ReturnsNullWithoutDigest()
    {
        Assert.Null(DigestReference.BuildOciReference("ghcr.io/owner/repo", null));
    }
}
