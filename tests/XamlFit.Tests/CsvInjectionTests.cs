using XamlFit.Core.Analysis;
using XamlFit.Core.Report;

namespace XamlFit.Tests;

public class CsvInjectionTests
{
    private static BudgetResult One(string key, string text) => new(
        new BudgetSummary(),
        [new StringBudget { Key = key, Text = text, Status = BudgetStatus.NoDeclaredLimit }]);

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+ 新增事件")]       // 實查 QuillNest：真的有以 + 開頭的 UI 字串
    [InlineData("-5 dB")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tTabbed")]
    public void Formula_like_text_is_neutralised_by_default(string text)
    {
        var csv = BudgetReport.ToCsv(One("K", text));
        var row = csv.Split('\n').First(l => l.StartsWith("K,"));
        // 中和後一定被引號包住，而且內容以單引號開頭
        Assert.Contains("\"'", row);
    }

    [Fact]
    public void Csv_raw_keeps_the_source_string_byte_exact()
    {
        // 加上去的單引號會汙染交給譯者的原文 —— API 流程要拿得到原值
        var csv = BudgetReport.ToCsv(One("K", "+ 新增事件"), neutralizeFormulas: false);
        Assert.Contains("+ 新增事件", csv);
        Assert.DoesNotContain("'+ 新增事件", csv);
    }

    [Fact]
    public void Ordinary_text_is_untouched()
    {
        var csv = BudgetReport.ToCsv(One("Save", "Save"));
        Assert.Contains("\nSave,", csv);
    }

    [Fact]
    public void Quotes_and_commas_still_escape_correctly()
    {
        var csv = BudgetReport.ToCsv(One("K", "a,b\"c"));
        Assert.Contains("\"a,b\"\"c\"", csv);
    }

    [Theory]
    [InlineData("=x", true)]
    [InlineData("+x", true)]
    [InlineData("-x", true)]
    [InlineData("@x", true)]
    [InlineData("x", false)]
    [InlineData("", false)]
    public void Formula_detection(string v, bool expected) =>
        Assert.Equal(expected, BudgetReport.IsFormulaLike(v));
}
