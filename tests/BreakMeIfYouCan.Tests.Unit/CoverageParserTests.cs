using BreakMeIfYouCan.Evidence.Parsing;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class CoverageParserTests
{
    [Fact]
    public void Parse_UsesRootTotalsWhenPresent()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("coverage.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage line-rate="0.8" branch-rate="0.5" lines-covered="80" lines-valid="100"
                      branches-covered="10" branches-valid="20" version="1.9" timestamp="0" />
            """);

        var summary = CoverageParser.Parse([file]);

        Assert.Equal(80, summary.LinePercent);
        Assert.Equal(50, summary.BranchPercent);
        Assert.Equal(80, summary.LinesCovered);
        Assert.Equal(100, summary.LinesValid);
    }

    [Fact]
    public void Parse_ComputesLinesFromTheDocumentWhenRootTotalsAreAbsent()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("coverage.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage line-rate="0" branch-rate="0">
              <packages>
                <package name="Api">
                  <classes>
                    <class name="A" filename="A.cs">
                      <lines>
                        <line number="1" hits="1" />
                        <line number="2" hits="0" />
                        <line number="3" hits="3" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """);

        var summary = CoverageParser.Parse([file]);

        Assert.Equal(3, summary.LinesValid);
        Assert.Equal(2, summary.LinesCovered);
        Assert.Equal(66.67, summary.LinePercent);
    }

    [Fact]
    public void Parse_MergesReportsRatherThanAveragingPercentages()
    {
        using var directory = new TestTempDirectory();
        var first = directory.Write("a.xml", """
            <coverage line-rate="0.9" branch-rate="0" lines-covered="90" lines-valid="100" branches-covered="0" branches-valid="0" />
            """);
        var second = directory.Write("b.xml", """
            <coverage line-rate="0.1" branch-rate="0" lines-covered="10" lines-valid="100" branches-covered="0" branches-valid="0" />
            """);

        var summary = CoverageParser.Parse([first, second]);

        Assert.Equal(100, summary.LinesCovered);
        Assert.Equal(200, summary.LinesValid);
        Assert.Equal(50, summary.LinePercent);
    }

    [Fact]
    public void Parse_ReturnsNullPercentagesWhenThereIsNoReport()
    {
        var summary = CoverageParser.Parse([]);

        Assert.Null(summary.LinePercent);
        Assert.Null(summary.BranchPercent);
        Assert.Equal(0, summary.LinesValid);
        Assert.Empty(summary.Reports);
    }

    [Fact]
    public void Parse_CountsConditionElementsAsBranches()
    {
        using var directory = new TestTempDirectory();
        var file = directory.Write("coverage.xml", """
            <coverage line-rate="0.5" branch-rate="0">
              <packages>
                <package name="Api">
                  <classes>
                    <class name="A" filename="A.cs">
                      <lines>
                        <line number="1" hits="1">
                          <conditions>
                            <condition number="1" type="jump" coverage="100" />
                            <condition number="2" type="jump" coverage="0" />
                          </conditions>
                        </line>
                        <line number="2" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """);

        var summary = CoverageParser.Parse([file]);

        Assert.Equal(2, summary.BranchesValid);
        Assert.Equal(1, summary.BranchesCovered);
        Assert.Equal(50, summary.BranchPercent);
    }
}
