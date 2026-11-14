using XamlFit.Core.Fonts;
using XamlFit.Core.OpenType;

namespace XamlFit.Tests;

public class FontMetricsTests
{
    private static FontMetrics Sans() =>
        FontMetricsExtractor.FromBytes(SyntheticFont.Build()).Single();

    [Fact]
    public void Extracts_head_and_hhea()
    {
        var m = Sans();
        Assert.Equal("Test Sans", m.Family);
        Assert.Equal(1000, m.UnitsPerEm);
        Assert.Equal(800, m.Ascender);
        Assert.Equal(-200, m.Descender);
        Assert.Equal(0, m.LineGap);
        Assert.Equal(1000, m.LineHeight);
        Assert.Equal(400, m.Weight);
        Assert.Equal(FontStyleKind.Normal, m.Style);
    }

    [Fact]
    public void Maps_codepoints_through_cmap_format4()
    {
        var m = Sans();
        Assert.True(m.TryAdvance('A', out var a));
        Assert.Equal(600, a);
        Assert.True(m.TryAdvance('B', out var b));
        Assert.Equal(700, b);
        Assert.True(m.TryAdvance('中', out var c));
        Assert.Equal(1000, c);
    }

    [Fact]
    public void Uncovered_codepoint_reports_missing_rather_than_guessing()
    {
        // ⚠ 這是本工具最重要的一條行為：查不到就要說查不到。
        //   回一個「預設寬度」假裝量到了，就會算出「裝得下」這種謊報健康的結論。
        var m = Sans();
        Assert.False(m.TryAdvance('Z', out _));
        Assert.False(m.TryAdvance('A' + 0x1000, out _));
    }

    [Fact]
    public void Does_not_merge_ranges_across_gaps()
    {
        // 'A'(0x41) 與 'B'(0x42) 連續且寬度不同 → 兩個區間；'中' 隔很遠 → 另一個區間。
        // 跨空洞合併等於宣稱字型畫得出它畫不出的字。
        var m = Sans();
        Assert.All(m.Ranges, r => Assert.True(r.Start <= r.End));
        Assert.DoesNotContain(m.Ranges, r => r.Start <= 'Z' && 'Z' <= r.End);
    }

    [Fact]
    public void Italic_flag_comes_from_fsSelection()
    {
        var m = FontMetricsExtractor.FromBytes(SyntheticFont.Build(italic: true)).Single();
        Assert.Equal(FontStyleKind.Italic, m.Style);
    }

    [Fact]
    public void Measures_linearly_in_font_units()
    {
        var m = Sans();
        // "AB" = 600 + 700 = 1300 單位，unitsPerEm=1000，字級 20 → 26px
        var r = TextMeasure.Measure("AB", [m], 20);
        Assert.Equal(26.0, r.WidthPx, 6);
        Assert.Equal(0, r.MissingGlyphs);
        Assert.True(r.IsReliable);
    }

    [Fact]
    public void Missing_glyphs_are_counted_and_make_the_result_unreliable()
    {
        var m = Sans();
        var r = TextMeasure.Measure("AZB", [m], 20);
        Assert.Equal(1, r.MissingGlyphs);
        Assert.False(r.IsReliable);
        // 缺的字不計入寬度 —— 但因為 IsReliable 是 false，呼叫端不會拿這個數字當結論
        Assert.Equal(26.0, r.WidthPx, 6);
    }

    [Fact]
    public void Fallback_chain_is_per_codepoint()
    {
        var latin = Sans();
        // 第二支字型只有 'Z'
        var other = FontMetricsExtractor.FromBytes(SyntheticFont.Build("Other Sans")).Single();
        var r = TextMeasure.Measure("A中", [latin, other], 10);
        Assert.Equal(0, r.MissingGlyphs);
        Assert.Equal((600 + 1000) / 1000.0 * 10, r.WidthPx, 6);
    }

    [Fact]
    public void Complex_scripts_are_flagged_not_guessed()
    {
        var m = Sans();
        var r = TextMeasure.Measure("الع", [m], 12);   // 阿拉伯文
        Assert.True(r.ComplexScript);
        Assert.False(r.IsReliable);
    }

    [Fact]
    public void MetricsFile_roundtrips()
    {
        var m = Sans();
        var file = MetricsFile.FromMetrics([m], "0.0.0-test", new DateOnly(2026, 11, 14));
        var json = file.ToJson();
        var back = MetricsFile.Parse(json).ToMetrics().Single();

        Assert.Equal(m.Family, back.Family);
        Assert.Equal(m.UnitsPerEm, back.UnitsPerEm);
        Assert.Equal(m.Ascender, back.Ascender);
        Assert.Equal(m.Ranges.Count, back.Ranges.Count);
        Assert.True(back.TryAdvance('中', out var adv));
        Assert.Equal(1000, adv);
    }

    [Fact]
    public void MetricsFile_rejects_a_newer_schema_instead_of_misreading_it()
    {
        var json = """{"schemaVersion":999,"fonts":[]}""";
        var ex = Assert.Throws<InvalidDataException>(() => MetricsFile.Parse(json));
        Assert.Contains("upgrade xamlfit", ex.Message);
    }

    [Fact]
    public void MetricsFile_keeps_ranges_on_one_line()
    {
        // 度量檔要進使用者的 repo。第一版用 WriteIndented，Segoe UI 一支就 2.1 MB。
        var json = MetricsFile.FromMetrics([Sans()], "0.0.0-test", new DateOnly(2026, 11, 14)).ToJson();
        var rangeLine = json.Split('\n').Single(l => l.Contains("\"ranges\""));
        Assert.Contains("[[", rangeLine);
        Assert.EndsWith("]", rangeLine.TrimEnd());
    }
}

public class FontResolverTests
{
    private static FontResolver Resolver() => new(
    [
        FontMetricsExtractor.FromBytes(SyntheticFont.Build("Test Sans", 400)).Single(),
        FontMetricsExtractor.FromBytes(SyntheticFont.Build("Test Sans", 700)).Single(),
        FontMetricsExtractor.FromBytes(SyntheticFont.Build("Test Sans", 400, italic: true)).Single(),
        FontMetricsExtractor.FromBytes(SyntheticFont.Build("Fallback Sans", 400)).Single(),
    ]);

    [Theory]
    [InlineData("Segoe UI", new[] { "Segoe UI" })]
    [InlineData("Segoe UI, Microsoft JhengHei", new[] { "Segoe UI", "Microsoft JhengHei" })]
    [InlineData("  Segoe UI ,  Arial  ", new[] { "Segoe UI", "Arial" })]
    [InlineData("./fonts/#My Icons", new[] { "My Icons" })]
    [InlineData("{DynamicResource F}", new string[0])]
    public void Splits_family_lists(string input, string[] expected) =>
        Assert.Equal(expected, FontResolver.SplitFamilies(input));

    [Theory]
    [InlineData("Bold", 700)]
    [InlineData("SemiBold", 600)]
    [InlineData("Normal", 400)]
    [InlineData("550", 550)]
    [InlineData("NotAWeight", 400)]
    [InlineData(null, 400)]
    public void Parses_weights(string? input, int expected) =>
        Assert.Equal(expected, FontResolver.ParseWeight(input));

    [Fact]
    public void Family_match_is_case_and_space_insensitive()
    {
        var r = Resolver();
        Assert.NotEmpty(r.Resolve("test   sans", 400, FontStyleKind.Normal, "x").Chain);
        Assert.NotEmpty(r.Resolve("TEST SANS", 400, FontStyleKind.Normal, "x").Chain);
    }

    [Fact]
    public void Picks_the_nearest_weight()
    {
        var r = Resolver();
        Assert.Equal(700, r.Resolve("Test Sans", 800, FontStyleKind.Normal, "x").Chain[0].Weight);
        Assert.Equal(400, r.Resolve("Test Sans", 450, FontStyleKind.Normal, "x").Chain[0].Weight);
    }

    [Fact]
    public void Missing_family_is_reported_by_name()
    {
        var r = Resolver();
        var lookup = r.Resolve("Nonexistent UI", 400, FontStyleKind.Normal, "Nonexistent UI");
        Assert.Empty(lookup.Chain);
        Assert.Equal(FontMissReason.NoSuchFamily, lookup.Miss);
        Assert.Equal("Nonexistent UI", lookup.MissingFamily);
    }

    [Fact]
    public void Fallback_list_skips_families_that_are_absent()
    {
        var r = Resolver();
        var lookup = r.Resolve("Nonexistent UI, Test Sans", 400, FontStyleKind.Normal, "x");
        Assert.Equal("Test Sans", lookup.Chain[0].Family);
        Assert.Equal(1, lookup.DeclaredCount);   // 只有一支是「明寫且存在」的
    }

    [Fact]
    public void Auto_fallback_appends_other_families_and_marks_them()
    {
        // WPF 會做系統層級的自動字型後備（Segoe UI 畫不出中文時 Windows 自己補一支）。
        // 只認明寫的清單會讓真實專案的中文字串大量變成「量不到」。
        var r = Resolver();
        var lookup = r.Resolve("Test Sans", 400, FontStyleKind.Normal, "x");
        Assert.True(lookup.Chain.Count > lookup.DeclaredCount,
            "auto fallback should append the other families in the metrics file");
        Assert.Contains(lookup.Chain, f => f.Family == "Fallback Sans");
    }

    [Fact]
    public void Auto_fallback_can_be_turned_off()
    {
        var r = new FontResolver(
        [
            FontMetricsExtractor.FromBytes(SyntheticFont.Build("Test Sans")).Single(),
            FontMetricsExtractor.FromBytes(SyntheticFont.Build("Fallback Sans")).Single(),
        ])
        { AutoFallback = false };

        var lookup = r.Resolve("Test Sans", 400, FontStyleKind.Normal, "x");
        Assert.Single(lookup.Chain);
    }

    [Fact]
    public void Measurement_flags_when_an_auto_fallback_font_was_used()
    {
        var latin = FontMetricsExtractor.FromBytes(SyntheticFont.Build("Test Sans")).Single();
        var other = FontMetricsExtractor.FromBytes(SyntheticFont.Build("Other Sans")).Single();

        // declaredCount=1 → 第二支算自動後備
        var m = TextMeasure.Measure("A", [latin, other], 10, declaredCount: 1);
        Assert.False(m.UsedAutoFallback);   // 'A' 第一支就畫得出來

        // 把第一支換成畫不出 'A' 的情況不好造，改用 declaredCount=0 驗旗標本身
        var m2 = TextMeasure.Measure("A", [latin, other], 10, declaredCount: 0);
        Assert.True(m2.UsedAutoFallback);
    }
}
