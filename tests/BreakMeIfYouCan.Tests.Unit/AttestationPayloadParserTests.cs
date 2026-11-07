using System.Text;
using System.Text.Json;
using BreakMeIfYouCan.Api.Proof;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class AttestationPayloadParserTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static string Encode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Parse_ReadsSlsaV1BuildProvenance()
    {
        var payload = Encode("""
            {
              "_type": "https://in-toto.io/Statement/v0.1",
              "subject": [
                { "name": "ghcr.io/owner/repo", "digest": { "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } }
              ],
              "predicateType": "https://slsa.dev/provenance/v1",
              "predicate": {
                "buildDefinition": {
                  "externalParameters": {
                    "workflow": {
                      "ref": "refs/heads/main",
                      "repository": "https://github.com/Skinny1ne/break-me-if-you-can",
                      "path": ".github/workflows/ci.yml"
                    }
                  },
                  "resolvedDependencies": [
                    { "uri": "git+https://github.com/Skinny1ne/break-me-if-you-can@refs/heads/main", "digest": { "gitCommit": "0123456789abcdef0123456789abcdef01234567" } }
                  ]
                },
                "runDetails": {
                  "metadata": {
                    "invocationId": "https://github.com/Skinny1ne/break-me-if-you-can/actions/runs/1234567890/attempts/1"
                  }
                }
              }
            }
            """);

        var parsed = AttestationPayloadParser.Parse(payload);

        Assert.NotNull(parsed);
        Assert.Equal("sha256:" + Sha, parsed.SubjectDigest);
        Assert.Equal("https://slsa.dev/provenance/v1", parsed.PredicateType);
        Assert.Equal(ProofFixtures.Repository, parsed.Repository);
        Assert.Equal(ProofFixtures.Commit, parsed.CommitSha);
        Assert.Equal(".github/workflows/ci.yml@refs/heads/main", parsed.WorkflowRef!.Replace(ProofFixtures.Repository + "/", string.Empty));
        Assert.Equal("1234567890", parsed.RunId);
    }

    [Fact]
    public void Parse_ReadsCustomQualityPredicate()
    {
        var payload = Encode("""
            {
              "subject": [ { "digest": { "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } } ],
              "predicateType": "https://breakmeifyoucan.dev/attestation/quality/v1",
              "predicate": {
                "repository": "Skinny1ne/break-me-if-you-can",
                "commitSha": "0123456789abcdef0123456789abcdef01234567",
                "runId": "42",
                "workflowRef": "Skinny1ne/break-me-if-you-can/.github/workflows/ci.yml@refs/heads/main"
              }
            }
            """);

        var parsed = AttestationPayloadParser.Parse(payload);

        Assert.NotNull(parsed);
        Assert.Equal("https://breakmeifyoucan.dev/attestation/quality/v1", parsed.PredicateType);
        Assert.Equal(ProofFixtures.Repository, parsed.Repository);
        Assert.Equal("42", parsed.RunId);
    }

    [Fact]
    public void Parse_ReadsSlsaV02InvocationEnvironment()
    {
        var payload = Encode("""
            {
              "subject": [ { "digest": { "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } } ],
              "predicateType": "https://slsa.dev/provenance/v0.2",
              "predicate": {
                "invocation": {
                  "configSource": { "uri": "git+https://github.com/Skinny1ne/break-me-if-you-can@refs/heads/main" },
                  "environment": {
                    "github_sha": "0123456789abcdef0123456789abcdef01234567",
                    "github_repository": "Skinny1ne/break-me-if-you-can",
                    "github_run_id": "777"
                  }
                }
              }
            }
            """);

        var parsed = AttestationPayloadParser.Parse(payload);

        Assert.NotNull(parsed);
        Assert.Equal(ProofFixtures.Repository, parsed.Repository);
        Assert.Equal(ProofFixtures.Commit, parsed.CommitSha);
        Assert.Equal("777", parsed.RunId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("bm90IGpzb24=")]
    public void Parse_RejectsUnusablePayloads(string? payload)
    {
        Assert.Null(AttestationPayloadParser.Parse(payload));
    }

    [Fact]
    public void Parse_ReturnsSubjectEvenWithoutAPredicate()
    {
        var payload = Encode("""
            {
              "subject": [ { "digest": { "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } } ],
              "predicateType": "https://example.test/none"
            }
            """);

        var parsed = AttestationPayloadParser.Parse(payload);

        Assert.NotNull(parsed);
        Assert.Equal("sha256:" + Sha, parsed.SubjectDigest);
        Assert.Null(parsed.CommitSha);
    }

    [Fact]
    public void Parse_HandlesDocumentShapedLikeGitHubResponsePayload()
    {
        var statement = new
        {
            _type = "https://in-toto.io/Statement/v0.1",
            subject = new[] { new { name = "img", digest = new { sha256 = Sha } } },
            predicateType = "https://slsa.dev/provenance/v1",
            predicate = new { buildDefinition = new { externalParameters = new { workflow = new { repository = "https://github.com/Skinny1ne/break-me-if-you-can", path = ".github/workflows/ci.yml", @ref = "refs/heads/main" } }, resolvedDependencies = Array.Empty<object>() } },
        };

        var payload = Encode(JsonSerializer.Serialize(statement));
        var parsed = AttestationPayloadParser.Parse(payload);

        Assert.NotNull(parsed);
        Assert.Equal("sha256:" + Sha, parsed.SubjectDigest);
        Assert.Equal(ProofFixtures.Repository, parsed.Repository);
    }
}
