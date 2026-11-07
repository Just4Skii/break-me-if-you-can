using BreakMeIfYouCan.Evidence.Manifest;
using BreakMeIfYouCan.Evidence.Parsing;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class TrxParserTests
{
    private const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <TestDefinitions>
            <UnitTest name="A.Passes" storage="C:\build\bin\BreakMeIfYouCan.Tests.Unit.dll" id="t1" />
            <UnitTest name="A.Fails" storage="C:\build\bin\BreakMeIfYouCan.Tests.Unit.dll" id="t2" />
            <UnitTest name="A.Skips" storage="C:\build\bin\BreakMeIfYouCan.Tests.Unit.dll" id="t3" />
          </TestDefinitions>
          <Results>
            <UnitTestResult testId="t1" testName="A.Passes" outcome="Passed" duration="00:00:00.1200000" />
            <UnitTestResult testId="t2" testName="A.Fails" outcome="Failed" duration="00:00:00.0300000" />
            <UnitTestResult testId="t3" testName="A.Skips" outcome="NotExecuted" duration="00:00:00.0000000" />
          </Results>
          <ResultSummary outcome="Failed">
            <Counters total="3" executed="2" passed="1" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="1" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
          </ResultSummary>
        </TestRun>
        """;

    [Fact]
    public void Parse_CountsOutcomesAndDuration()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("unit.trx", Trx);

        var evidence = TrxParser.Parse([file]);

        Assert.Equal(PipelineStatus.Failure, evidence.Status);
        Assert.Equal(3, evidence.Total);
        Assert.Equal(1, evidence.Passed);
        Assert.Equal(1, evidence.Failed);
        Assert.Equal(1, evidence.Skipped);
        Assert.Equal(0.15, evidence.DurationSeconds, 3);
        Assert.Single(evidence.Assemblies);
        Assert.Equal("BreakMeIfYouCan.Tests.Unit", evidence.Assemblies[0].Assembly);
        Assert.Equal(["unit.trx"], evidence.Reports);
    }

    [Fact]
    public void Parse_MergesMultipleAssemblies()
    {
        using var directory = new TestTempDirectory();
        var unit = directory.Write("unit.trx", Trx);
        var integration = directory.Write("integration.trx", Trx.Replace(
            "BreakMeIfYouCan.Tests.Unit.dll",
            "BreakMeIfYouCan.Tests.Integration.dll",
            StringComparison.Ordinal));

        var evidence = TrxParser.Parse([unit, integration]);

        Assert.Equal(6, evidence.Total);
        Assert.Equal(2, evidence.Failed);
        Assert.Equal(2, evidence.Assemblies.Count);
        Assert.Equal(PipelineStatus.Failure, evidence.Status);
        Assert.Equal(2, evidence.Reports.Count);
    }

    [Fact]
    public void Parse_ReturnsMissingWhenThereAreNoReports()
    {
        var evidence = TrxParser.Parse([]);

        Assert.Equal(PipelineStatus.Missing, evidence.Status);
        Assert.Equal(0, evidence.Total);
        Assert.Empty(evidence.Reports);
    }

    [Fact]
    public void Parse_ReturnsMissingForAReportWithoutResults()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("empty.trx", """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results />
            </TestRun>
            """);

        var evidence = TrxParser.Parse([file]);

        Assert.Equal(PipelineStatus.Missing, evidence.Status);
        Assert.Equal(0, evidence.Total);
    }

    [Fact]
    public void Parse_IgnoresFilesThatCannotBeReadAsXml()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("broken.trx", "<not-closed");

        var evidence = TrxParser.Parse([file]);

        Assert.Equal(PipelineStatus.Missing, evidence.Status);
    }

    [Fact]
    public void Parse_MapsErrorAndTimeoutOutcomesToFailed()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("outcomes.trx", """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <TestDefinitions>
                <UnitTest name="T" storage="bin/Some.Tests.dll" id="a" />
              </TestDefinitions>
              <Results>
                <UnitTestResult testId="a" outcome="Error" duration="00:00:01" />
                <UnitTestResult testId="a" outcome="Timeout" duration="00:00:01" />
                <UnitTestResult testId="a" outcome="Passed" duration="00:00:01" />
              </Results>
            </TestRun>
            """);

        var evidence = TrxParser.Parse([file]);

        Assert.Equal(3, evidence.Total);
        Assert.Equal(2, evidence.Failed);
        Assert.Equal(1, evidence.Passed);
    }
}