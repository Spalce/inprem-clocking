using Inprem.Platform.Application.Audit;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Provider;

/// <summary>Phase 1 step 1.6: CSV cells are quoted correctly and never run as spreadsheet formulas.</summary>
public class AuditCsvTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("q\",x", "\"q\"\",x\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("=1,2", "\"'=1,2\"")]
    public void Cell(string? value, string expected) => Assert.Equal(expected, AuditLogService.Cell(value));
}
