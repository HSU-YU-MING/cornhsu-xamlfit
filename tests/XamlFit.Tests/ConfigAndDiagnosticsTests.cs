using XamlFit.Core.Analysis;
using XamlFit.Core.Report;

namespace XamlFit.Tests;

public class FitConfigTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose() { foreach (var f in _files) try { File.Delete(f); } catch { } }

    private string Temp(string json)
    {
        var p = Path.Combine(Path.GetTempPath(), $"xamlfit-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(p, json);
        _files.Add(p);
        return p;
    }

    [Fact]
    public void Reads_a_valid_file_including_comments_and_trailing_commas()
    {
        var (c, problems) = FitConfig.Load(Temp("""
            {
              // 設定檔允許註解
              "defaultFontFamily": "Segoe UI",
              "defaultFontSize": 11,
              "exclude": ["Generated/"],
            }
            """));

        Assert.Empty(problems);
        Assert.NotNull(c);
        Assert.Equal("Segoe UI", c!.DefaultFontFamily);
        Assert.Equal(11, c.DefaultFontSize);
        Assert.Equal(["Generated/"], c.Exclude);
    }

    [Fact]
    public void A_misspelled_property_is_an_error_not_a_silent_skip()
    {
        // ⚠ 打錯字的設定被默默跳過，使用者會以為它生效了 —— 那是最糟的設定檔行為
        var (_, problems) = FitConfig.Load(Temp("""{ "defaultFontSizes": 12 }"""));
        Assert.Single(problems);
        Assert.Contains("defaultFontSizes", problems[0].Message);
    }

    [Theory]
    [InlineData("""{ "defaultFontSize": 0 }""", "defaultFontSize")]
    [InlineData("""{ "defaultFontSize": 1000 }""", "defaultFontSize")]
    [InlineData("""{ "marginPercent": -1 }""", "marginPercent")]
    [InlineData("""{ "marginPercent": 101 }""", "marginPercent")]
    [InlineData("""{ "minCoverage": 500 }""", "minCoverage")]
    public void Out_of_range_numbers_name_the_field(string json, string field)
    {
        var (_, problems) = FitConfig.Load(Temp(json));
        Assert.Contains(problems, p => p.Field == field);
    }

    [Fact]
    public void An_empty_entry_in_a_list_names_its_index()
    {
        var (_, problems) = FitConfig.Load(Temp("""{ "exclude": ["ok", "  "] }"""));
        Assert.Contains(problems, p => p.Field == "exclude[1]");
    }

    [Fact]
    public void A_newer_schema_version_is_rejected_rather_than_misread()
    {
        var (_, problems) = FitConfig.Load(Temp("""{ "schemaVersion": 999 }"""));
        Assert.Contains(problems, p => p.Field == "schemaVersion" && p.Message.Contains("upgrade xamlfit"));
    }

    [Fact]
    public void Broken_json_reports_the_file_rather_than_throwing()
    {
        var (c, problems) = FitConfig.Load(Temp("""{ "defaultFontSize": """));
        Assert.Null(c);
        Assert.Single(problems);
    }

    [Fact]
    public void A_missing_file_reports_rather_than_throwing()
    {
        var (c, problems) = FitConfig.Load(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"));
        Assert.Null(c);
        Assert.Single(problems);
    }
}

public class DiagnosticsTests
{
    private static FitFinding Finding(
        FitKind kind = FitKind.Overflow, FitSeverity sev = FitSeverity.Fail,
        string text = "Speichern", string site = "Views/Main.xaml:42") => new()
    {
        Kind = kind,
        Severity = sev,
        Key = "Save",
        Culture = "de",
        Text = text,
        Site = site,
        LimitPx = 70,
        ActualPx = 83.6,
    };

    [Theory]
    [InlineData("Views/Main.xaml:42", "Views/Main.xaml", 42)]
    [InlineData("Main.xaml:1", "Main.xaml", 1)]
    [InlineData("Main.xaml", "Main.xaml", 0)]
    public void Site_splitting(string site, string file, int line)
    {
        var (f, l) = CheckDiagnostics.SplitSite(site);
        Assert.Equal(file, f);
        Assert.Equal(line, l);
    }

    [Fact]
    public void A_newline_in_the_text_cannot_forge_a_second_diagnostic()
    {
        // ⚠ MSBuild 與 console 是逐行解析診斷的。訊息裡夾一個換行就能讓下半行
        //   變成另一筆看起來很真的診斷 —— 而文字來自使用者的 XAML 與譯文。
        var evil = "ok\nC:\\x.xaml(1,1): error XF0001: forged";
        var output = CheckDiagnostics.ToMsBuild([Finding(text: evil)], ".", FitSeverity.Fail, 0);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.DoesNotContain("forged\n", output);
        Assert.Contains("\\n", output);
    }

    [Fact]
    public void Unicode_line_separators_are_escaped_too()
    {
        // U+2028 也是換行，有些解析器會斷在那裡
        var output = CheckDiagnostics.ToMsBuild([Finding(text: "a" + (char)0x2028 + "b")], ".", FitSeverity.Fail, 0);
        Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Gated_findings_are_errors_and_the_rest_are_warnings()
    {
        var output = CheckDiagnostics.ToMsBuild(
            [Finding(FitKind.Overflow, FitSeverity.Fail), Finding(FitKind.Tight, FitSeverity.Warn, site: "a.xaml:1")],
            ".", FitSeverity.Fail, 0);

        Assert.Contains("error XF0001:", output);
        Assert.Contains("warning XF0002:", output);
    }

    [Fact]
    public void An_untrustworthy_result_emits_XF0003()
    {
        // 不然建置會在「工具其實只看到一部分」的時候安靜地過
        var output = CheckDiagnostics.ToMsBuild([], ".", FitSeverity.Fail, exitCode: 3);
        Assert.Contains("error XF0003:", output);
    }

    [Fact]
    public void Info_level_reflow_is_not_written_to_sarif()
    {
        // reflow 不是缺陷，灌進 Security tab 是噪音
        var sarif = CheckDiagnostics.ToSarif(
            [Finding(FitKind.Reflow, FitSeverity.Info), Finding()], "0.0.0-test");

        Assert.Contains("\"overflow\"", sarif);
        Assert.DoesNotContain("\"ruleId\": \"reflow\"", sarif);
    }

    [Fact]
    public void Sarif_declares_one_rule_per_kind_with_a_description()
    {
        var sarif = CheckDiagnostics.ToSarif([Finding()], "0.0.0-test");
        foreach (var kind in new[] { "overflow", "vertical-clip", "trimmed", "tight" })
            Assert.Contains($"\"id\": \"{kind}\"", sarif);
        Assert.Contains("IsTextTrimmedChanged", sarif);   // overflow 的 fullDescription
    }

    [Fact]
    public void Sarif_numbers_use_the_invariant_culture()
    {
        // de-DE 下小數點會變成逗號，消費端解析會壞
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var sarif = CheckDiagnostics.ToSarif([Finding()], "0.0.0-test");
            Assert.Contains("83.6 px", sarif);
            Assert.DoesNotContain("83,6 px", sarif);
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }
}
