using XamlFit.Core.Analysis;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>翻譯完整度（0.5.0）：漏鍵、佔位字對不上、寫死的字。</summary>
public class TranslationCompletenessTests
{
    private static CultureStrings Table(string culture, string file, params (string Key, string Value, int Line)[] entries) =>
        new(culture, entries.ToDictionary(e => e.Key, e => e.Value), file)
        {
            Origins = entries.ToDictionary(e => e.Key, e => (file, e.Line)),
        };

    private static IReadOnlyList<FitFinding> Run(params CultureStrings[] tables) =>
        TranslationCompleteness.Analyze(tables, [], f => f);

    // ───────────────────────── 格式項目 ─────────────────────────

    [Theory]
    [InlineData("Saved {0} of {1}", new[] { 0, 1 }, false)]
    [InlineData("{0,-10} {1:N2} {2,5:0.0}", new[] { 0, 1, 2 }, false)]
    [InlineData("{{literal}} and {0}", new[] { 0 }, false)]
    [InlineData("{0 } trailing space is allowed", new[] { 0 }, false)]
    [InlineData("no items at all", new int[0], false)]
    [InlineData("{ 0} leading space", new int[0], true)]
    [InlineData("{name}", new int[0], true)]
    [InlineData("open { only", new int[0], true)]
    [InlineData("close } only", new int[0], true)]
    public void Format_items_follow_the_dotnet_composite_format(string s, int[] indices, bool malformed)
    {
        var (items, bad) = TranslationCompleteness.FormatItems(s);
        Assert.Equal(malformed, bad);
        if (!malformed) Assert.Equal(indices, items.ToArray());
    }

    [Theory]
    [InlineData("Saved {0} of {1}", "{0} von {1} gespeichert", null)]
    [InlineData("Saved {0} of {1}", "{1} von {0} gespeichert", null)]                 // 換順序沒問題
    [InlineData("Saved {0} of {1}", "{0} von {2} gespeichert", FitSeverity.Fail)]     // 來源只有兩個引數
    [InlineData("Saved {0} of {1}", "{0} von {1 gespeichert", FitSeverity.Fail)]      // 大括號不成對
    [InlineData("Saved {0} of {1}", "{0} gespeichert", FitSeverity.Warn)]             // 少了 {1}：那個值不見了
    [InlineData("{0} and {2}", "{1}", FitSeverity.Warn)]                              // {1} ≤ 最大編號：不會丟例外，但對不上
    [InlineData("Plain text", "Text mit {Klammer}", null)]                           // 來源不是格式字串：不比
    [InlineData("Use {name} here", "Hier {0}", null)]                                 // 來源本身不合法：不知道怎麼用，不比
    public void Placeholders_compare_the_translation_with_the_source(string source, string translation, FitSeverity? expected)
    {
        var result = TranslationCompleteness.Placeholders(source, translation);
        Assert.Equal(expected, result?.Severity);
    }

    // ───────────────────────── 字串表 ─────────────────────────

    [Fact]
    public void A_key_the_source_has_and_a_language_lacks_is_a_missing_translation()
    {
        var findings = Run(
            Table(ResxReader.NeutralCulture, "R/Strings.resx", ("Save", "Save", 3), ("Farewell", "Bye", 4)),
            Table("de", "R/Strings.de.resx", ("Save", "Speichern", 3)));

        var f = Assert.Single(findings);
        Assert.Equal(FitKind.MissingTranslation, f.Kind);
        Assert.Equal(FitSeverity.Warn, f.Severity);
        Assert.Equal("Farewell", f.Key);
        Assert.Equal("de", f.Culture);
        Assert.Equal("R/Strings.resx:4", f.Site);   // 指向來源語言定義它的地方：譯文那邊沒有這一行
        Assert.False(f.IsFit);
    }

    [Fact]
    public void A_language_split_across_files_is_one_table()
    {
        // 跟 CheckAnalyzer.MergeByCulture 同一個坑：Strings.de 與 Errors.de 是同一個德文
        var findings = Run(
            Table(ResxReader.NeutralCulture, "Strings.resx", ("A", "a", 1), ("B", "b", 2)),
            Table("de", "Strings.de.resx", ("A", "a-de", 1)),
            Table("de", "Errors.de.resx", ("B", "b-de", 1)));
        Assert.Empty(findings);
    }

    [Fact]
    public void A_placeholder_mismatch_points_at_the_translation()
    {
        var findings = Run(
            Table(ResxReader.NeutralCulture, "Strings.resx", ("N", "Saved {0} of {1}", 7)),
            Table("fr", "Strings.fr.resx", ("N", "{0} sur {2}", 9)));
        var f = Assert.Single(findings);
        Assert.Equal(FitKind.PlaceholderMismatch, f.Kind);
        Assert.Equal(FitSeverity.Fail, f.Severity);
        Assert.Equal("Strings.fr.resx:9", f.Site);
        Assert.Contains("FormatException", f.Warning);
    }

    [Fact]
    public void Without_a_neutral_table_english_is_the_source()
    {
        // ScreenToGif：基底是 StringResources.en.xaml，沒有中性語言的表
        var findings = Run(
            Table("en", "StringResources.en.xaml", ("K", "Open", 0), ("L", "Close", 0)),
            Table("it", "StringResources.it.xaml", ("K", "Apri", 0)));
        var f = Assert.Single(findings);
        Assert.Equal("L", f.Key);
        Assert.Equal("it", f.Culture);
        Assert.Equal("StringResources.en.xaml", f.Site);   // 字串字典沒有行號：只指得出檔案
    }

    [Fact]
    public void Nothing_is_reported_without_translations()
    {
        Assert.Empty(Run(Table(ResxReader.NeutralCulture, "Strings.resx", ("A", "a", 1))));
    }

    // ───────────────────────── 寫死的字 ─────────────────────────

    private static TextSite Site(TextSource source, string text, string? suppressed = null) => new()
    {
        File = "Main.xaml",
        Line = 5,
        Element = "TextBlock",
        Source = source,
        Text = text,
        Width = XamlFit.Core.Layout.AvailableWidth.NotKnown(XamlFit.Core.Layout.WidthStatus.NoDeclaredLimit, "x"),
        SuppressedReason = suppressed,
    };

    [Fact]
    public void Hard_coded_text_is_reported_once_per_site_only_when_there_are_translations()
    {
        var tables = new[]
        {
            Table(ResxReader.NeutralCulture, "Strings.resx", ("A", "a", 1)),
            Table("de", "Strings.de.resx", ("A", "a-de", 1)),
        };
        var sites = new[]
        {
            Site(TextSource.Literal, "Made with love"),
            Site(TextSource.Literal, "Made with love"),        // 同一個位置（重掃時會再出現一次）
            Site(TextSource.Literal, "→"),                     // 只有符號：不是翻譯對象
            Site(TextSource.Resource, "{x:Static p:S.A}"),
            Site(TextSource.Literal, "Ignored", suppressed: "decorative"),
        };

        var f = Assert.Single(TranslationCompleteness.Analyze(tables, sites, x => x));
        Assert.Equal(FitKind.HardCodedText, f.Kind);
        Assert.Equal(FitSeverity.Info, f.Severity);
        Assert.Equal("Main.xaml:5", f.Site);

        Assert.Empty(TranslationCompleteness.Analyze([tables[0]], sites, x => x));   // 沒有譯文：寫死的字就是全部
    }

    // ───────────────────────── Markdown（PR 留言）─────────────────────────

    [Theory]
    [InlineData("[click](http://evil.example)")]
    [InlineData("@someone")]
    [InlineData("www.evil.example")]
    [InlineData("a`b``c")]
    [InlineData("pipe | inside")]
    public void Strings_from_the_repository_stay_inside_code_spans(string hostile)
    {
        // 鍵名、譯文、路徑都來自 repo：貼成 PR 留言時不能變成連結、圖片或 @ 提及（Parity、XamlContrast 都出過這個安全修正）
        var finding = new FitFinding
        {
            Kind = FitKind.MissingTranslation, Severity = FitSeverity.Warn,
            Key = hostile, Culture = "de", Text = hostile, Site = hostile + ".resx:3",
            LimitPx = 0, ActualPx = 0, Warning = "no value",
        };
        var result = new CheckResult(new CheckSummary(), [finding]);
        var md = XamlFit.Core.Report.CheckMarkdown.ToMarkdown(result, [finding], 0, 0, [], []);

        var row = md.Split('\n').Single(l => l.StartsWith("| ", StringComparison.Ordinal) && l.Contains("missing-translation", StringComparison.Ordinal));
        // | 被跳脫，不會多切出一欄：六欄的表格列剛好有 7 個沒跳脫的 |
        var cells = System.Text.RegularExpressions.Regex.Split(row, @"(?<!\\)\|");
        Assert.Equal(8, cells.Length);   // 頭尾兩個空字串 + 6 欄
        // 從 repo 來的欄位（位置、語言、鍵、文字）都包在反引號裡
        foreach (var i in new[] { 1, 3, 4, 5 })
            Assert.StartsWith("`", cells[i].Trim());
    }

    [Fact]
    public void The_markdown_report_leads_with_what_was_not_checked()
    {
        var result = new CheckResult(new CheckSummary { Unreliable = 2, UnreliableReasons = [("ar", "complex script", 2)] }, []);
        var md = XamlFit.Core.Report.CheckMarkdown.ToMarkdown(result, [], 3, 0, ["Broken.xaml"], [("th", "Leelawadee UI")]);
        var verdict = md.IndexOf("NOT TRUSTWORTHY", StringComparison.Ordinal);
        Assert.True(verdict > 0);
        Assert.Contains("**Not checked:** `th`", md);
        Assert.Contains("**Parse failed:** `Broken.xaml`", md);
        Assert.Contains("2 measurement(s) not judged", md);
    }

    // ───────────────────────── 報告 ─────────────────────────

    [Fact]
    public void The_new_kinds_have_their_own_slugs_and_no_pixel_values_in_the_json()
    {
        Assert.Equal("missing-translation", FitFinding.KindSlug(FitKind.MissingTranslation));
        Assert.Equal("placeholder-mismatch", FitFinding.KindSlug(FitKind.PlaceholderMismatch));
        Assert.Equal("hard-coded-text", FitFinding.KindSlug(FitKind.HardCodedText));

        var finding = Assert.Single(Run(
            Table(ResxReader.NeutralCulture, "Strings.resx", ("A", "a", 1), ("B", "b", 2)),
            Table("de", "Strings.de.resx", ("A", "a-de", 1))));
        var result = CheckAnalyzer.WithExtra(new CheckResult(new CheckSummary(), []), [finding]);
        Assert.Equal(1, result.Summary.Counts["missing-translation"]);

        using var doc = System.Text.Json.JsonDocument.Parse(XamlFit.Core.Report.CheckReport.ToJson(result, result.Findings, "test"));
        var f = doc.RootElement.GetProperty("findings")[0];
        Assert.Equal("missing-translation", f.GetProperty("kind").GetString());
        // 沒有寬度的 finding 不寫像素欄位（報告省略 null）—— 寫 0 會被讀成「剛好放得下」
        Assert.False(f.TryGetProperty("limitPx", out _));
        Assert.False(f.TryGetProperty("actualPx", out _));
        Assert.True(f.TryGetProperty("warning", out _));   // 說明在 warning 裡
    }
}
