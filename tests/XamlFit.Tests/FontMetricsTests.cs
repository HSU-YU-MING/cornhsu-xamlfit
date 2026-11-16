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
        // 只要某些家族：其他的讀完名稱就跳過（不跑形狀表）
        Assert.Empty(FontMetricsExtractor.FromBytes(SyntheticFont.Build("Other Sans"), f => f == "Test Sans"));
        Assert.Single(FontMetricsExtractor.FromBytes(SyntheticFont.Build("Other Sans"), f => f == "Other Sans"));
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
    public void A_missing_primary_family_is_a_miss_not_a_silent_substitution()
    {
        // ⚠ 第一版直接拿清單裡的下一支去量。"Segoe UI Variable, Segoe UI" 在 Windows 11 上
        //   用的是前者，寬度不一樣 —— 拿後者量出來的數字看起來很真，但是錯的。
        var r = Resolver();
        var lookup = r.Resolve("Nonexistent UI, Test Sans", 400, FontStyleKind.Normal, "x");
        Assert.Empty(lookup.Chain);
        Assert.Equal(FontMissReason.NoSuchFamily, lookup.Miss);
        Assert.Equal("Nonexistent UI", lookup.MissingFamily);
    }

    [Fact]
    public void A_family_declared_absent_is_skipped()
    {
        // 使用者說了「目標機器上沒有這支」，才可以跳過它
        var r = new FontResolver(
            [FontMetricsExtractor.FromBytes(SyntheticFont.Build("Test Sans", 400)).Single()],
            absentFamilies: ["Nonexistent UI"]);
        var lookup = r.Resolve("Nonexistent UI, Test Sans", 400, FontStyleKind.Normal, "x");
        Assert.Equal("Test Sans", lookup.Chain[0].Family);
        // 鏈裡只有 Test Sans 的字面（含同家族的其他字面，WPF 會先從那裡補字）；不存在的家族不佔位置
        Assert.All(lookup.Chain, f => Assert.Equal("Test Sans", f.Family));
        Assert.Equal(lookup.Chain.Count, lookup.DeclaredCount);
    }

    [Fact]
    public void A_missing_family_after_the_primary_only_affects_fallback()
    {
        var r = Resolver();
        var lookup = r.Resolve("Test Sans, Nonexistent UI", 400, FontStyleKind.Normal, "x");
        Assert.Equal("Test Sans", lookup.Chain[0].Family);
        // Test Sans 的三個字面（同家族的其他字面排在後面，WPF 先從那裡補字）；不存在的家族不佔位置
        Assert.Equal(3, lookup.Chain.Count);
        Assert.All(lookup.Chain, f => Assert.Equal("Test Sans", f.Family));
        Assert.Equal(3, lookup.DeclaredCount);
    }

    [Fact]
    public void A_family_outside_WPFs_fallback_table_is_never_used_as_fallback()
    {
        // ⚠ 第一版把度量檔裡其他所有字型依字母順序接在後面。WPF 只照它自己的複合字型表補字 ——
        //   表裡沒有的字型（這裡的 Fallback Sans），WPF 不會拿來補。
        var r = Resolver();
        var lookup = r.Resolve("Test Sans", 400, FontStyleKind.Normal, "x");
        Assert.All(lookup.Chain, f => Assert.Equal("Test Sans", f.Family));
        // 「中」：WPF 會找 Yu Gothic UI、Meiryo UI…，都不在這份度量裡 → 沒有後備
        Assert.Null(lookup.Fallback!(0x4E2D));
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

    [Fact]
    public void A_variable_font_becomes_one_face_per_named_instance_under_the_name_wpf_uses()
    {
        // WPF 不認得字型自己的家族名（Segoe UI Variable 會被拆成 Segoe UI + 字面名稱）；它認得的是「家族 + 那組實例共同的字」
        var faces = FontMetricsExtractor.FromBytes(SyntheticFont.BuildVariable()).OrderBy(f => f.Weight).ToList();
        Assert.Equal([300, 400, 700], faces.Select(f => f.Weight));
        Assert.All(faces, f => Assert.Equal("Test Var Text", f.Family));
        Assert.All(faces, f => Assert.True(f.Variable));
        Assert.Equal("Test Var Text Light", faces[0].LegacyFamily);   // GDI／舊式名稱
        Assert.Null(faces[1].LegacyFamily);
        Assert.Null(faces[2].LegacyFamily);                           // 粗體跟一般同一個舊式家族
        // HVAR：'A' 600 → Light 570、Bold 660；'B' 700 → 660、770
        Assert.True(faces[0].TryAdvance('A', out var a300) && a300 == 570);
        Assert.True(faces[1].TryAdvance('A', out var a400) && a400 == 600);
        Assert.True(faces[2].TryAdvance('A', out var a700) && a700 == 660);
        Assert.True(faces[2].TryAdvance('B', out var b700) && b700 == 770);
        Assert.True(faces[0].TryAdvance('B', out var b300) && b300 == 660);
        // --family 寫字型自己的家族名或實例的家族名都拿得到
        Assert.Equal(3, FontMetricsExtractor.FromBytes(SyntheticFont.BuildVariable(), f => f == "Test Var").Count);
        Assert.Equal(3, FontMetricsExtractor.FromBytes(SyntheticFont.BuildVariable(), f => f == "Test Var Text").Count);
        Assert.Empty(FontMetricsExtractor.FromBytes(SyntheticFont.BuildVariable(), f => f == "Other"));
        // 解析時只准用實例的字重
        var resolver = new FontResolver(faces);
        Assert.Single(resolver.Resolve("Test Var Text", 700, FontStyleKind.Normal, "Test Var Text").Chain);
        Assert.Empty(resolver.Resolve("Test Var Text", 600, FontStyleKind.Normal, "Test Var Text").Chain);
        Assert.Single(resolver.Resolve("Test Var Text Light", 400, FontStyleKind.Normal, "Test Var Text Light").Chain);
    }

    [Fact]
    public void A_gzipped_metrics_file_reads_the_same()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xamlfit-gz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var json = MetricsFile.FromMetrics([FontMetricsExtractor.FromBytes(SyntheticFont.Build()).Single()], "test", new DateOnly(2026, 11, 16)).ToJson();
            var gz = Path.Combine(dir, "m.fonts.json.gz");
            var plain = Path.Combine(dir, "m.fonts.json");
            MetricsFile.Save(gz, json);
            MetricsFile.Save(plain, json);
            Assert.Equal(0x1F, File.ReadAllBytes(gz)[0]);
            Assert.Equal('{', (char)File.ReadAllBytes(plain)[0]);
            Assert.Equal(json, MetricsFile.ReadText(gz));
            Assert.Equal(MetricsFile.Load(plain).ToJson(), MetricsFile.Load(gz).ToJson());
        }
        finally { Directory.Delete(dir, true); }
    }

    private static FontMetrics Plain(string family, int weight) => new()
    {
        Family = family, Weight = weight, UnitsPerEm = 1000, DefaultAdvance = 500,
        Ranges = [new AdvanceRange(0x20, 0x7E, weight == 700 ? 600 : 500)],
    };

    [Fact]
    public void A_name_that_is_not_an_installed_family_is_read_as_family_plus_face_name_like_wpf()
    {
        // WPF：「Segoe UI Variable」畫 Segoe UI、「Times New Roman Italic」是斜體、「Arial Foo Bar」照 XAML 的字重
        FontMetrics[] faces = [Plain("Segoe UI", 400), Plain("Segoe UI", 700)];
        var resolver = new FontResolver(faces, installedFamilies: ["Segoe UI", "Segoe UI Historic", "Segoe UI Variable Text"]);
        Assert.Equal(700, resolver.Resolve("Segoe UI Variable", 700, FontStyleKind.Normal, "Segoe UI").Chain[0].Weight);
        Assert.Equal(400, resolver.Resolve("Segoe UI Variable", 400, FontStyleKind.Normal, "Segoe UI").Chain[0].Weight);
        Assert.Equal(700, resolver.Resolve("Segoe UI Bold", 400, FontStyleKind.Normal, "Segoe UI").Chain[0].Weight);
        Assert.Empty(resolver.Resolve("Segoe UI Historic", 400, FontStyleKind.Normal, "Segoe UI").Chain);       // 真的另一支字型，只是沒匯出
        Assert.Empty(resolver.Resolve("Segoe UI Variable Text", 400, FontStyleKind.Normal, "Segoe UI").Chain);  // 同上
        Assert.Empty(resolver.Resolve("Segoe UI Foo Bold", 400, FontStyleKind.Normal, "Segoe UI").Chain);       // 混著：不知道 WPF 怎麼讀
        // 舊的度量檔沒記裝了哪些家族：不拆
        Assert.Empty(new FontResolver(faces).Resolve("Segoe UI Variable", 400, FontStyleKind.Normal, "Segoe UI").Chain);
    }

    [Fact]
    public void Language_system_tables_are_stored_as_differences_and_read_back_whole()
    {
        var face = Plain("Indic", 400);
        face = new FontMetrics
        {
            Family = face.Family, Weight = 400, UnitsPerEm = 1000, DefaultAdvance = 500, Ranges = face.Ranges,
            Indic = new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["deva"] = new Dictionary<string, int> { ["2325"] = 10, ["2326"] = 20, ["+2325,2359"] = -300 },
                ["deva/MAR"] = new Dictionary<string, int> { ["2325"] = 10, ["2326"] = 25, ["+2325,2359"] = -300, ["2400"] = 5 },
                ["deva/HIN"] = new Dictionary<string, int> { ["2325"] = 10 },
            },
        };
        var json = MetricsFile.FromMetrics([face], "test", new DateOnly(2026, 11, 16)).ToJson();
        var back = MetricsFile.Parse(json).ToMetrics().Single().Indic!;
        foreach (var key in face.Indic!.Keys) Assert.Equal(face.Indic[key], back[key]);
        var raw = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("fonts")[0].GetProperty("indic");
        Assert.Equal(2, raw.GetProperty("deva/MAR").GetArrayLength());   // 只有不一樣的兩項
        Assert.Equal(2, raw.GetProperty("deva/HIN").GetArrayLength());   // 兩項「沒有」
    }
}
