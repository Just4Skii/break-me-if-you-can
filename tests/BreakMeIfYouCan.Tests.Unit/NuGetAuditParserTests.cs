using BreakMeIfYouCan.Evidence.Manifest;
using BreakMeIfYouCan.Evidence.Parsing;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class NuGetAuditParserTests
{
    [Fact]
    public void Parse_CleanReportIsClean()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("audit.json", """
            { "version": 1, "projects": [ { "path": "/src/Api.csproj" } ] }
            """);

        var audit = NuGetAuditParser.Parse(file, 0);

        Assert.Equal(AuditStatus.Clean, audit.Status);
        Assert.Equal(0, audit.DirectCount);
        Assert.Equal(0, audit.TransitiveCount);
        Assert.Empty(audit.Packages);
    }

    [Fact]
    public void Parse_VulnerableReportCountsDirectAndTransitiveAdvisories()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("audit.json", """
            {
              "version": 1,
              "projects": [
                {
                  "path": "/src/Api.csproj",
                  "frameworks": [
                    {
                      "framework": "net10.0",
                      "topLevelPackages": [
                        {
                          "id": "Example.Direct",
                          "requestedVersion": "1.0.0",
                          "resolvedVersion": "1.0.0",
                          "vulnerabilities": [
                            { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-1111" }
                          ]
                        }
                      ],
                      "transitivePackages": [
                        {
                          "id": "Example.Transitive",
                          "resolvedVersion": "2.0.0",
                          "vulnerabilities": [
                            { "severity": "Moderate", "advisoryurl": "https://github.com/advisories/GHSA-2222" }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        var audit = NuGetAuditParser.Parse(file, 0);

        Assert.Equal(AuditStatus.Vulnerable, audit.Status);
        Assert.Equal(1, audit.DirectCount);
        Assert.Equal(1, audit.TransitiveCount);
        Assert.Equal(1, audit.BySeverity["high"]);
        Assert.Equal(1, audit.BySeverity["moderate"]);
        Assert.Contains(audit.Packages, p => p.Id == "Example.Direct" && !p.Transitive);
        Assert.Contains(audit.Packages, p => p.Id == "Example.Transitive" && p.Transitive);
    }

    [Fact]
    public void Parse_DeduplicatesTheSameAdvisory()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("audit.json", """
            {
              "version": 1,
              "projects": [
                {
                  "path": "/src/Api.csproj",
                  "frameworks": [
                    {
                      "framework": "net10.0",
                      "topLevelPackages": [
                        {
                          "id": "Example",
                          "resolvedVersion": "1.0.0",
                          "vulnerabilities": [
                            { "severity": "High", "advisoryurl": "https://example/1" }
                          ]
                        }
                      ],
                      "transitivePackages": [
                        {
                          "id": "Example",
                          "resolvedVersion": "1.0.0",
                          "vulnerabilities": [
                            { "severity": "High", "advisoryurl": "https://example/1" }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        var audit = NuGetAuditParser.Parse(file, 0);

        Assert.Single(audit.Packages);
    }

    [Fact]
    public void Parse_MissingReportIsMissingNotClean()
    {
        var audit = NuGetAuditParser.Parse("/does/not/exist.json", 0);

        Assert.Equal(AuditStatus.Missing, audit.Status);
        Assert.Contains("missing", audit.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_FailedAuditCommandIsFailed()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("audit.json", """{ "version": 1, "projects": [] }""");

        var audit = NuGetAuditParser.Parse(file, 1);

        Assert.Equal(AuditStatus.Failed, audit.Status);
    }

    [Fact]
    public void Parse_InvalidJsonIsFailed()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("audit.json", "{ not json");

        var audit = NuGetAuditParser.Parse(file, 0);

        Assert.Equal(AuditStatus.Failed, audit.Status);
    }

    [Fact]
    public void Parse_UnknownSeverityIsPreservedLowerCase()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("audit.json", """
            {
              "version": 1,
              "projects": [
                {
                  "path": "p",
                  "frameworks": [
                    {
                      "framework": "net10.0",
                      "topLevelPackages": [
                        { "id": "X", "resolvedVersion": "1", "vulnerabilities": [ { "severity": "Unknown" } ] }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        var audit = NuGetAuditParser.Parse(file, 0);

        Assert.Equal(AuditStatus.Vulnerable, audit.Status);
        Assert.Equal(1, audit.BySeverity["unknown"]);
    }
}
