using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Report;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 判定邏輯。字型是等寬的 Mono：字級 10 時每個字 5 px、行高 10 px，
/// 所以 100 px 寬的位置一行放得下 20 個字。
/// </summary>
public class CheckSemanticsTests
{
    private static string Window(string body) =>
        $"<Window {Ns} xmlns:p=\"clr-namespace:App.Properties\" WindowStyle=\"None\" AllowsTransparency=\"True\">{body}</Window>";

    private static CultureStrings Table(string culture, params (string Key, string Value)[] entries) =>
        new(culture, entries.ToDictionary(e => e.Key, e => e.Value), culture + ".resx");

    private static CheckResult Check(string body, bool pseudo, params CultureStrings[] cultures)
    {
        var dir = TempProject(("Main.xaml", Window(body)));
        try
        {
            var fonts = Fonts();
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, fonts).Scan(dir);
            CheckAnalyzer.MergeByCulture(cultures).TryGetValue(ResxReader.NeutralCulture, out var neutral);
            var budget = new BudgetAnalyzer(fonts, MonoFamily).Analyze(scan, "test", neutral);
            return new CheckAnalyzer(fonts, MonoFamily, marginPercent: 5).Analyze(scan, budget, cultures, pseudo);
        }
        finally { Cleanup(dir); }
    }

    private static CheckResult Check(string body, params CultureStrings[] cultures) => Check(body, false, cultures);

    private static string Words(int count, int length = 10) =>
        string.Join(' ', Enumerable.Repeat(new string('A', length), count));

    private const string Key = "{x:Static p:Strings.K}";

    // ───────────────────────── 來源語言 ─────────────────────────

    [Fact]
    public void Hard_coded_text_that_already_overflows_is_reported()
    {
        // 寫死的英文已經裝不下，也是真的裝不下 —— 而且它是工具自我檢查的基準
        var result = Check($"<TextBlock Width=\"100\" Text=\"{new string('A', 25)}\"/>");
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FitKind.Overflow, finding.Kind);
        Assert.Equal(CheckAnalyzer.SourceCulture, finding.Culture);
        Assert.Equal(125, finding.ActualPx);
    }

    [Fact]
    public void Hard_coded_text_is_not_looked_up_in_translations()
    {
        // ⚠ 第一版拿字面文字當鍵去查每個語言的字串表，然後把每一筆都算成「漏翻」
        var result = Check("<TextBlock Width=\"100\" Text=\"Save\"/>", Table("de", ("Other", "x")), Table("fr", ("Other", "y")));
        Assert.Equal(0, result.Summary.MissingTranslations);
        Assert.Equal(1, result.Summary.Measurements);
        Assert.Equal(0, result.Summary.TranslatedMeasurements);
    }

    [Fact]
    public void The_neutral_resource_value_is_checked_as_the_source_language()
    {
        var result = Check($"<TextBlock Width=\"100\" Text=\"{Key}\"/>",
            Table(ResxReader.NeutralCulture, ("K", new string('A', 30))),
            Table("de", ("K", "AA")));
        Assert.Equal(CheckAnalyzer.SourceCulture, Assert.Single(result.Findings).Culture);
        Assert.Equal(["de"], result.Summary.Cultures);
    }

    // ───────────────────────── 字串表 ─────────────────────────

    [Fact]
    public void Several_files_of_the_same_culture_are_one_table()
    {
        // ⚠ 第一版逐檔比對：Strings 的鍵在 Errors.de 裡「沒有譯文」，被算成漏翻
        var result = Check($"<TextBlock Width=\"100\" Text=\"{Key}\"/>",
            Table("de", ("K", "AAAA")), Table("de", ("Unrelated", "x")));
        Assert.Equal(0, result.Summary.MissingTranslations);
        Assert.Equal(1, result.Summary.TranslatedMeasurements);
    }

    [Fact]
    public void A_missing_translation_is_counted_once_per_key_and_culture()
    {
        var result = Check($"<StackPanel><TextBlock Width=\"100\" Text=\"{Key}\"/><TextBlock Width=\"200\" Text=\"{Key}\"/></StackPanel>",
            Table("de", ("Other", "x")));
        Assert.Equal(1, result.Summary.MissingTranslations);
    }

    [Fact]
    public void Run_time_placeholders_are_not_judged()
    {
        // 量 "{0}" 這三個字的寬度沒有意義：實際顯示的長度取決於執行期的值
        var result = Check($"<TextBlock Width=\"30\" Text=\"{Key}\"/>", Table("de", ("K", "{0} Dateien wurden geloescht")));
        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Summary.Placeholders);
        Assert.Equal(0, result.Summary.TranslatedMeasurements);
    }

    [Fact]
    public void Text_the_metrics_cannot_cover_is_counted_not_skipped()
    {
        // ⚠ 第一版一聲不吭地跳過：「整個語言沒檢查」看起來跟「整個語言通過」一樣
        var result = Check($"<TextBlock Width=\"100\" Text=\"{Key}\"/>", Table("de", ("K", "AA☃")));
        Assert.Equal(1, result.Summary.Unreliable);
        Assert.Equal(0x2603, result.Summary.UncoveredCodepoints.Single().Codepoint);
        Assert.Equal(("de", 0, 1), result.Summary.PerCulture.Single(p => p.Culture == "de"));
    }

    [Fact]
    public void Complex_scripts_are_reported_as_unreliable()
    {
        var result = Check($"<TextBlock Width=\"100\" Text=\"{Key}\"/>", Table("ar", ("K", "مرحبا")));
        Assert.Contains("complex script", result.Summary.UnreliableReasons.Single().Reason);
    }

    // ───────────────────────── 單行 ─────────────────────────

    [Fact]
    public void Overflow_trimmed_and_tight()
    {
        var long30 = Table("de", ("K", new string('A', 30)));
        Assert.Equal(FitKind.Overflow, Check($"<TextBlock Width=\"100\" Text=\"{Key}\"/>", long30).Findings.Single().Kind);
        Assert.Equal(FitKind.Trimmed, Check($"<TextBlock Width=\"100\" TextTrimming=\"CharacterEllipsis\" Text=\"{Key}\"/>", long30).Findings.Single().Kind);

        // 20 個字 = 100 px：剛好裝滿，餘裕 0%，低於 5% 的邊界
        var tight = Check($"<TextBlock Width=\"100\" Text=\"{Key}\"/>", Table("de", ("K", new string('A', 20)))).Findings.Single();
        Assert.Equal(FitKind.Tight, tight.Kind);
        Assert.Equal(FitSeverity.Warn, tight.Severity);
    }

    [Fact]
    public void An_access_key_underscore_does_not_count_towards_the_width()
    {
        // "_" + 20 個字：底線不畫出來 → 100 px，剛好裝得下（tight 而不是 overflow）
        var result = Check($"<Button Width=\"104\" Content=\"{Key}\"/>", Table("de", ("K", "_" + new string('A', 20))));
        Assert.Equal(FitKind.Tight, result.Findings.Single().Kind);
        Assert.Equal(100, result.Findings.Single().ActualPx);
    }

    [Fact]
    public void Multi_line_text_is_as_wide_as_its_widest_line()
    {
        // 15 字 + 換行 + 15 字：每行 75 px，裝得進 100。整串加起來是 150。
        var result = Check($"<TextBlock Width=\"100\" Text=\"{Key}\"/>",
            Table("de", ("K", new string('A', 15) + "\n" + new string('A', 15))));
        Assert.Empty(result.Findings);
    }

    // ───────────────────────── 換行 ─────────────────────────

    [Fact]
    public void Line_count_follows_word_wrap_not_total_width_divided_by_line_width()
    {
        // 四個 10 字的詞。兩個詞加一個空格是 105 px，放不進 100 → 一行一個詞，共四行。
        // ⚠ 第一版用「總寬 ÷ 行寬」：215 ÷ 100 → 三行，於是高度 30（三行）被判成放得下。
        var result = Check($"<TextBlock Width=\"100\" Height=\"30\" TextWrapping=\"Wrap\" Text=\"{Key}\"/>", Table("de", ("K", Words(4))));
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FitKind.VerticalClip, finding.Kind);
        Assert.Contains("4 line(s)", finding.Warning);
    }

    [Fact]
    public void Wrapped_text_that_fits_its_height_is_fine() =>
        Assert.DoesNotContain(
            Check($"<TextBlock Width=\"100\" Height=\"40\" TextWrapping=\"Wrap\" Text=\"{Key}\"/>", Table("de", ("K", Words(4)))).Findings,
            f => f.Kind == FitKind.VerticalClip);

    [Fact]
    public void Reflow_is_only_reported_when_the_translation_needs_more_lines_than_the_source()
    {
        var neutral = Table(ResxReader.NeutralCulture, ("K", Words(2)));
        const string site = "<TextBlock Width=\"100\" TextWrapping=\"Wrap\" Text=\"{x:Static p:Strings.K}\"/>";

        Assert.Empty(Check(site, neutral, Table("de", ("K", Words(2)))).Findings);

        var reflow = Assert.Single(Check(site, neutral, Table("de", ("K", Words(3)))).Findings);
        Assert.Equal(FitKind.Reflow, reflow.Kind);
        Assert.Equal(FitSeverity.Info, reflow.Severity);
    }

    [Fact]
    public void A_word_longer_than_the_line_overflows_under_WrapWithOverflow()
    {
        // 換行救不了一個斷不開的長詞（德文複合詞最常見）
        var text = Table("de", ("K", new string('A', 30)));
        Assert.Equal(FitKind.Overflow,
            Check($"<TextBlock Width=\"100\" TextWrapping=\"WrapWithOverflow\" Text=\"{Key}\"/>", text).Findings.Single().Kind);
        // Wrap 會在詞的中間硬斷：只是多一行
        Assert.Equal(FitKind.Reflow,
            Check($"<TextBlock Width=\"100\" TextWrapping=\"Wrap\" Text=\"{Key}\"/>", text).Findings.Single().Kind);
    }

    [Fact]
    public void Cjk_text_can_break_between_any_two_characters()
    {
        // 15 個全形字 = 150 px，放進 100 px、兩行高：第一行 10 個、第二行 5 個
        var result = Check($"<TextBlock Width=\"100\" Height=\"20\" TextWrapping=\"Wrap\" Text=\"{Key}\"/>",
            Table("zh-Hant", ("K", new string('中', 15))));
        Assert.DoesNotContain(result.Findings, f => f.Kind == FitKind.VerticalClip);
    }

    [Fact]
    public void Wrapping_that_cannot_happen_is_judged_as_overflow()
    {
        // WPF 實測：水平 StackPanel 用無限寬量子元素，Wrap 不會換行，文字一路畫出去被裁掉。
        // 把它報成無害的「換行」就是漏報。
        var result = Check($"""
            <Border Width="100">
              <StackPanel Orientation="Horizontal">
                <TextBlock TextWrapping="Wrap" Text="{Key}"/>
              </StackPanel>
            </Border>
            """, Table("de", ("K", Words(4))));
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FitKind.Overflow, finding.Kind);
        Assert.Contains("TextWrapping has no effect", finding.Warning);
    }

    // ⚠ 回報 1（ZeroCel CanvasView.xaml:1171）：TextBlock Width="120" TextWrapping="Wrap" 在水平 StackPanel 裡，
    //   第一版報 fail overflow（needs 192, has 120）。WPF 實測它照 120 折成兩行。
    [Theory]
    [InlineData("Width=\"120\"")]
    [InlineData("MaxWidth=\"120\"")]
    public void An_own_width_in_a_horizontal_stack_wraps_instead_of_overflowing(string ownWidth)
    {
        // 4 個 10 字的詞 = 215 px，照 120 換行：兩行
        var result = Check($"""
            <Border Width="300">
              <StackPanel Orientation="Horizontal">
                <Border Width="50"/>
                <TextBlock {ownWidth} TextWrapping="Wrap" Text="{Key}"/>
              </StackPanel>
            </Border>
            """, Table("de", ("K", Words(4))));
        Assert.DoesNotContain(result.Findings, f => f.Kind == FitKind.Overflow);
        var reflow = Assert.Single(result.Findings);
        Assert.Equal(FitKind.Reflow, reflow.Kind);
        Assert.Contains("2 line(s)", reflow.Warning);
    }

    [Fact]
    public void A_container_width_inside_a_horizontal_stack_also_keeps_wrapping()
    {
        var result = Check($"""
            <StackPanel Orientation="Horizontal">
              <Border Width="120"><TextBlock TextWrapping="Wrap" Text="{Key}"/></Border>
            </StackPanel>
            """, Table("de", ("K", Words(4))));
        Assert.DoesNotContain(result.Findings, f => f.Kind == FitKind.Overflow);
    }

    [Fact]
    public void An_own_width_with_a_fixed_height_still_clips_vertically()
    {
        // 兩行要 20 px 高，只給 10
        var result = Check($"""
            <StackPanel Orientation="Horizontal">
              <TextBlock Width="120" Height="10" TextWrapping="Wrap" Text="{Key}"/>
            </StackPanel>
            """, Table("de", ("K", Words(4))));
        Assert.Equal(FitKind.VerticalClip, Assert.Single(result.Findings).Kind);
    }

    [Fact]
    public void An_own_width_with_NoWrap_still_overflows()
    {
        var result = Check($"""
            <StackPanel Orientation="Horizontal">
              <TextBlock Width="120" Text="{Key}"/>
            </StackPanel>
            """, Table("de", ("K", Words(4))));
        Assert.Equal(FitKind.Overflow, Assert.Single(result.Findings).Kind);
    }

    [Fact]
    public void A_word_wider_than_the_own_width_overflows_only_with_WrapWithOverflow()
    {
        // 30 個字的詞 = 150 px > 120：Wrap 會在詞中間硬斷（WPF 實際行為），WrapWithOverflow 讓它超出去
        var wrap = Check("<StackPanel Orientation=\"Horizontal\"><TextBlock Width=\"120\" TextWrapping=\"Wrap\" Text=\"" + Key + "\"/></StackPanel>",
            Table("de", ("K", Words(1, 30))));
        Assert.DoesNotContain(wrap.Findings, f => f.Kind == FitKind.Overflow);

        var overflow = Check("<StackPanel Orientation=\"Horizontal\"><TextBlock Width=\"120\" TextWrapping=\"WrapWithOverflow\" Text=\"" + Key + "\"/></StackPanel>",
            Table("de", ("K", Words(1, 30))));
        Assert.Equal(FitKind.Overflow, Assert.Single(overflow.Findings).Kind);
    }

    [Fact]
    public void Text_wider_than_the_row_wraps_at_its_own_width_and_the_rest_is_clipped()
    {
        // Width="400" 放在 300 寬、前面已經有 100 的排裡：照 400 換行（4 個詞 215 px 一行放得下），看得見 200
        var result = Check($"""
            <Border Width="300">
              <StackPanel Orientation="Horizontal">
                <Border Width="100"/>
                <TextBlock Width="400" TextWrapping="Wrap" Text="{Key}"/>
              </StackPanel>
            </Border>
            """, Table("de", ("K", Words(4))));
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FitKind.Overflow, finding.Kind);
        Assert.Equal(215, finding.ActualPx);
        Assert.Equal(200, finding.LimitPx);
        Assert.Contains("wraps at 400.0 px", finding.Warning);
    }

    [Fact]
    public void Text_in_a_fixed_column_wraps_at_its_own_width_not_the_column()
    {
        // WPF：Grid 給 100 的約束，TextBlock 的 MeasureCore 再夾到它自己的 Width=300 → 照 300 換行，每行被裁到 100
        var result = Check($"""
            <Grid Width="300">
              <Grid.ColumnDefinitions><ColumnDefinition Width="100"/><ColumnDefinition/></Grid.ColumnDefinitions>
              <TextBlock Width="300" TextWrapping="Wrap" Text="{Key}"/>
            </Grid>
            """, Table("de", ("K", Words(4))));
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FitKind.Overflow, finding.Kind);
        Assert.Equal(100, finding.LimitPx);
    }

    // ───────────────────────── --pseudo ─────────────────────────

    [Fact]
    public void Pseudo_findings_never_fail_the_build()
    {
        // 14 個字 × 膨脹 1.8 = 26 個字 = 130 px > 100
        var result = Check($"<TextBlock Width=\"100\" Text=\"{new string('A', 14)}\"/>", pseudo: true);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FitKind.Overflow, finding.Kind);
        Assert.Equal(FitSeverity.Warn, finding.Severity);
        Assert.Equal(CheckAnalyzer.PseudoCulture, finding.Culture);
    }

    [Fact]
    public void Pseudo_reports_resource_strings_it_has_no_source_for()
    {
        var result = Check($"<TextBlock Width=\"100\" Text=\"{Key}\"/>", pseudo: true);
        Assert.Equal(1, result.Summary.NoSourceText);
        Assert.Equal(0, result.Summary.Measurements);
    }

    // ───────────────────────── 合併 ─────────────────────────

    [Fact]
    public void A_site_that_only_this_language_makes_unmeasurable_is_counted()
    {
        // 來源語言量得到，譯文把版面撐到算不出來：要算進「沒有判定」，不能默默消失
        var dir = TempProject(("Main.xaml", Window($"""
            <Border Width="300">
              <Border Margin="{Key}"><TextBlock Text="{Key}"/></Border>
            </Border>
            """)));
        try
        {
            var fonts = Fonts();
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, fonts).Scan(dir);
            var budget = new BudgetAnalyzer(fonts, MonoFamily).Analyze(scan, "test");
            var site = scan.Sites.Single();
            var measurableInSource = new HashSet<string> { site.Location };   // 假設來源語言時量得到

            var result = new CheckAnalyzer(fonts, MonoFamily, 5)
                .Analyze(scan, budget, [Table("de", ("K", "AA"))], pseudo: false, judgeSource: false, measurableInSource);
            Assert.Equal(1, result.Summary.Unreliable);
            Assert.Equal(("de", 0, 1), result.Summary.PerCulture.Single());
        }
        finally { Cleanup(dir); }
    }

    [Theory]
    [InlineData(true, true)]     // 旁邊的譯文比較寬：沒翻的字在這個語言裡被擠出去
    [InlineData(false, false)]   // 版面跟來源語言一樣：不重複判
    public void An_untranslated_string_is_judged_in_this_languages_layout(bool neighbourTranslated, bool expectFinding)
    {
        // 寬 100 的水平 StackPanel：A 在前、B 在後。來源語言 A=10、B=70 → B 有 90，裝得下。
        // 德文只翻了 A（40）→ 執行期 B 顯示英文，只剩 60 —— CorpusRender 在 ScreenToGif 實測到的情形
        var dir = TempProject(("Main.xaml", Window("""
            <StackPanel Orientation="Horizontal" Width="100">
              <TextBlock Text="{x:Static p:Strings.A}"/><TextBlock Text="{x:Static p:Strings.B}"/>
            </StackPanel>
            """)));
        try
        {
            var fonts = Fonts();
            var neutral = new Dictionary<string, string> { ["A"] = "AA", ["B"] = new string('B', 14) };
            var de = neighbourTranslated ? new Dictionary<string, string> { ["A"] = new string('A', 8) } : new Dictionary<string, string>();
            var scanner = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, Sharing = RowSharing.Preceding }, fonts);
            scanner.Scan(dir);
            var overlay = new Dictionary<string, string>(neutral);
            foreach (var (k, v) in de) overlay[k] = v;
            var localized = scanner.Rescan(MonoFamily, overlay);
            var budget = new BudgetAnalyzer(fonts, MonoFamily).Analyze(localized, "test", neutral);

            var result = new CheckAnalyzer(fonts, MonoFamily, 5).Analyze(localized, budget,
                [new CultureStrings(ResxReader.NeutralCulture, neutral, "Strings.resx"), Table("de", [.. de.Select(kv => (kv.Key, kv.Value))])],
                pseudo: false, judgeSource: false);

            var b = result.Findings.Where(f => f.Key == "B").ToList();
            if (!expectFinding) { Assert.Empty(b); return; }
            var finding = Assert.Single(b);
            Assert.Equal("de", finding.Culture);
            Assert.Equal(60, finding.LimitPx);
            Assert.Contains("no translation", finding.Warning);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Results_from_several_passes_merge_into_one()
    {
        const string site = "<TextBlock Width=\"100\" Text=\"{x:Static p:Strings.K}\"/>";
        var a = Check(site, Table("de", ("K", new string('A', 30))));
        var b = Check(site, Table("fr", ("K", new string('A', 30))));

        var merged = CheckAnalyzer.Merge([a, b]);
        Assert.Equal(["de", "fr"], merged.Summary.Cultures);
        Assert.Equal(2, merged.Findings.Count);
        Assert.Equal(2, merged.Summary.Counts["overflow"]);
        Assert.Equal(2, merged.Summary.TranslatedMeasurements);
    }
}

public class BudgetSemanticsTests
{
    private static BudgetResult Budget(string body, IReadOnlyDictionary<string, string>? source = null)
    {
        var dir = TempProject(("Main.xaml",
            $"<Window {Ns} xmlns:p=\"clr-namespace:App.Properties\" xmlns:ui=\"http://example.com/ui\" WindowStyle=\"None\" AllowsTransparency=\"True\">{body}</Window>"));
        try
        {
            var fonts = Fonts();
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, fonts).Scan(dir);
            return new BudgetAnalyzer(fonts, MonoFamily).Analyze(scan, "test", source);
        }
        finally { Cleanup(dir); }
    }

    private const string Key = "{x:Static p:Strings.K}";

    [Fact]
    public void The_binding_site_is_the_one_that_holds_the_fewest_characters()
    {
        // ⚠ 第一版取像素最小的位置。同一個字串在 200 px／字級 20 的標題（20 個字）
        //   與 150 px／字級 10 的按鈕（30 個字）上各出現一次時，先爆的是標題。
        var b = Budget($"<StackPanel><TextBlock Width=\"200\" FontSize=\"20\" Text=\"{Key}\"/><TextBlock Width=\"150\" Text=\"{Key}\"/></StackPanel>")
            .Budgets.Single();
        Assert.Equal(200, b.LimitPx);
        Assert.Equal(20, b.LimitChars);
    }

    [Fact]
    public void A_known_source_string_gives_headroom()
    {
        var b = Budget($"<TextBlock Width=\"100\" Text=\"{Key}\"/>", new Dictionary<string, string> { ["K"] = "AAAA" }).Budgets.Single();
        Assert.Equal(20, b.SourceWidthPx);
        Assert.Equal(80, b.HeadroomPx);
        Assert.Equal("AAAA", b.Text);
        Assert.True(b.ExpansionFactor > 1);
    }

    [Fact]
    public void An_unknown_source_string_has_no_expansion_factor()
    {
        var result = Budget($"<TextBlock Width=\"100\" Text=\"{Key}\"/>");
        Assert.Equal(0, result.Budgets.Single().ExpansionFactor);
        // CSV 的 expansion 欄是空的，不是一個猜出來的 2.00
        var row = BudgetReport.ToCsv(result).Split('\n').First(l => l.StartsWith("K,", StringComparison.Ordinal)).Split(',');
        Assert.Equal("", row[11]);
    }

    [Fact]
    public void Freely_wrapping_text_has_no_horizontal_budget()
    {
        // ⚠ 第一版把行寬當成預算：一段本來就排成三行的說明文字被列在「最擠的位置」第一名
        var result = Budget($"<TextBlock Width=\"100\" TextWrapping=\"Wrap\" Text=\"{new string('A', 60)}\"/>");
        var b = result.Budgets.Single();
        Assert.Equal(BudgetStatus.Wraps, b.Status);
        Assert.Null(b.LimitPx);
        Assert.Null(b.HeadroomPx);
        Assert.Contains("wraps freely", b.MeasureWarning);
        Assert.Equal(1, result.Summary.WrappingStrings);
        Assert.Contains(",wraps,", BudgetReport.ToCsv(result));
    }

    [Fact]
    public void Wrapping_text_with_a_fixed_height_is_budgeted_by_lines()
    {
        // 高 30 = 三行，每行 100 px
        var b = Budget($"<TextBlock Width=\"100\" Height=\"30\" TextWrapping=\"Wrap\" Text=\"{Key}\"/>").Budgets.Single();
        Assert.Equal(BudgetStatus.Measured, b.Status);
        Assert.Equal(300, b.LimitPx);
        Assert.Contains("3 lines", b.MeasureWarning);
    }

    [Fact]
    public void A_site_that_clips_takes_precedence_over_one_that_wraps()
    {
        var b = Budget($"<StackPanel><TextBlock Width=\"100\" TextWrapping=\"Wrap\" Text=\"{Key}\"/><TextBlock Width=\"150\" Text=\"{Key}\"/></StackPanel>")
            .Budgets.Single();
        Assert.Equal(BudgetStatus.Measured, b.Status);
        Assert.Equal(150, b.LimitPx);
    }

    [Fact]
    public void Template_sites_are_counted_separately_and_stay_out_of_the_gate()
    {
        var result = Budget("""
            <StackPanel>
              <StackPanel.Resources>
                <DataTemplate x:Key="T"><TextBlock Text="Inside"/></DataTemplate>
              </StackPanel.Resources>
              <TextBlock Width="100" Text="Outside"/>
            </StackPanel>
            """);
        Assert.Equal(1, result.Summary.InTemplateSites);
        Assert.Equal(1, result.Summary.MeasuredSites);
        Assert.Equal(0, result.Summary.BlockedSites);
        Assert.Equal(100, result.Summary.Resolution);
    }

    [Fact]
    public void Controls_with_unknown_chrome_are_named_so_they_can_be_declared()
    {
        var result = Budget("""
            <Border Width="300">
              <StackPanel>
                <ui:Card><TextBlock Text="One"/></ui:Card>
                <ui:Card><TextBlock Text="Two"/></ui:Card>
              </StackPanel>
            </Border>
            """);
        Assert.Equal(("ui:Card", 2), result.Summary.UndeclaredChrome.Single());
        Assert.Equal(2, result.Summary.BlockedSites);
    }

    [Fact]
    public void Vector_path_data_is_not_a_string_to_translate()
    {
        var result = Budget("<Label Width=\"32\" Content=\"M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z\"/>");
        Assert.Empty(result.Budgets);
        Assert.Equal(1, result.Summary.SymbolOnlySites);
    }

    [Fact]
    public void A_limit_that_depends_on_neighbouring_strings_says_so()
    {
        var dir = TempProject(
            ("Strings.xaml", $"<ResourceDictionary {Ns} xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"><sys:String x:Key=\"A\">Name</sys:String><sys:String x:Key=\"B\">OK</sys:String></ResourceDictionary>"),
            ("Main.xaml", $"<Window {Ns} WindowStyle=\"None\" AllowsTransparency=\"True\"><Border Width=\"300\"><StackPanel Orientation=\"Horizontal\"><TextBlock Text=\"{{DynamicResource A}}\"/><TextBlock Text=\"{{DynamicResource B}}\"/></StackPanel></Border></Window>"));
        try
        {
            var fonts = Fonts();
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, fonts).Scan(dir);
            var b = new BudgetAnalyzer(fonts, MonoFamily).Analyze(scan, "test").Budgets.Single(x => x.Key == "B");
            Assert.Equal(280, b.LimitPx);
            Assert.Contains("shares its row", b.MeasureWarning);
        }
        finally { Cleanup(dir); }
    }
}

public class GridSolverOracleTests
{
    private static AvailableWidth Grid(string columns, string children, double width = 500, string resources = "") =>
        In($"<Grid Width=\"{width}\">{resources}<Grid.ColumnDefinitions>{columns}</Grid.ColumnDefinitions>{children}</Grid>");

    private const string InSecond = "<TextBlock x:Name=\"probe\" Grid.Column=\"1\"/>";

    [Theory]
    // 每一列都是 LayoutOracle 在真的 WPF 上量到的
    [InlineData("<ColumnDefinition MinWidth=\"400\"/><ColumnDefinition/>", 1, 500, 100)]
    [InlineData("<ColumnDefinition MaxWidth=\"200\"/><ColumnDefinition/>", 1, 500, 300)]
    [InlineData("<ColumnDefinition/><ColumnDefinition Width=\"2*\" MaxWidth=\"150\"/><ColumnDefinition/>", 2, 600, 225)]
    [InlineData("<ColumnDefinition MinWidth=\"300\"/><ColumnDefinition/><ColumnDefinition MaxWidth=\"50\"/>", 1, 600, 250)]
    public void Star_columns_redistribute_what_min_and_max_take_or_leave(string columns, int column, double width, double expected)
    {
        // ⚠ 第一版只做「分完再夾」：MinWidth=400 的星號欄旁邊那一欄被算成 250（WPF 給 100）—— 高估
        var w = Grid(columns, $"<TextBlock x:Name=\"probe\" Grid.Column=\"{column}\"/>", width);
        Assert.Equal(expected, w.Px, 3);
    }

    [Fact]
    public void Min_greater_than_max_does_not_crash()
    {
        // Math.Clamp 在這種寫錯的 XAML 上會丟例外；WPF 的規則是 MinWidth 贏
        var w = Grid("<ColumnDefinition MinWidth=\"300\" MaxWidth=\"100\"/><ColumnDefinition/>", InSecond);
        Assert.Equal(200, w.Px, 3);
    }

    [Fact]
    public void Text_in_an_Auto_column_can_grow_until_the_grid_is_full()
    {
        // ⚠ 第一版回傳「這一欄現在多寬」：每個放在 Auto 欄的標籤餘裕都是 0，一翻譯就報溢出
        var w = Grid("<ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"100\"/>", "<TextBlock x:Name=\"probe\" Text=\"AAAA\"/>", 300);
        Assert.Equal(200, w.Px, 3);
        Assert.True(w.WrapDefeated);   // WPF 用無限寬量 Auto 欄裡的子元素
    }

    [Fact]
    public void Text_with_its_own_width_in_an_Auto_column_still_wraps()
    {
        // 回報 1 的同一條規則：Auto 欄用無限寬量，但元素自己的 Width 先把它夾住
        var w = Grid("<ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"100\"/>", "<TextBlock x:Name=\"probe\" Width=\"120\" TextWrapping=\"Wrap\" Text=\"AAAA\"/>", 300);
        Assert.Equal(120, w.Px, 3);
        Assert.False(w.WrapDefeated);
    }

    [Fact]
    public void Text_wider_than_its_fixed_column_wraps_at_its_own_width()
    {
        // WPF：欄給 100 的約束，TextBlock 的 MeasureCore 再夾到自己的 Width=300 → 照 300 換行，看得見 100
        var w = Grid("<ColumnDefinition Width=\"100\"/><ColumnDefinition/>", "<TextBlock x:Name=\"probe\" Width=\"300\" TextWrapping=\"Wrap\"/>", 300);
        Assert.Equal(100, w.Px, 3);
        Assert.Equal(300, w.WrapPx);
    }

    [Fact]
    public void A_star_sibling_keeps_its_MinWidth() =>
        Assert.Equal(220, Grid("<ColumnDefinition Width=\"Auto\"/><ColumnDefinition MinWidth=\"80\"/>", "<TextBlock x:Name=\"probe\"/>", 300).Px, 3);

    [Fact]
    public void A_column_width_given_as_a_resource_is_read()
    {
        var w = Grid("<ColumnDefinition Width=\"{StaticResource Label}\"/><ColumnDefinition/>", InSecond, 500,
            "<Grid.Resources><GridLength xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Key=\"Label\">120</GridLength></Grid.Resources>");
        Assert.Equal(380, w.Px, 3);
    }

    [Fact]
    public void The_system_scroll_bar_width_is_recognised()
    {
        // WPF 自己的 ComboBox 範本就是這樣留出下拉按鈕的位置
        var w = Grid("<ColumnDefinition/><ColumnDefinition Width=\"0\" MinWidth=\"{DynamicResource {x:Static SystemParameters.VerticalScrollBarWidthKey}}\"/>",
            "<TextBlock x:Name=\"probe\"/>", 300);
        Assert.Equal(300 - Chrome.ScrollBarWidth, w.Px, 3);
    }

    [Fact]
    public void A_star_column_squeezed_out_by_the_others_has_zero_width()
    {
        // ⚠ CorpusRender 實測 ScreenToGif：希臘文的說明文字（Auto 欄）比整個視窗寬，標題（* 欄）寬度是 0。
        //   第一版回「解不開」，那一筆在希臘文裡默默沒有檢查。
        var w = Grid("<ColumnDefinition/><ColumnDefinition Width=\"Auto\"/>",
            "<TextBlock x:Name=\"probe\"/><TextBlock Grid.Column=\"1\" Text=\"" + new string('A', 120) + "\"/>", 300);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(0, w.Px);
    }

    [Fact]
    public void A_column_width_that_cannot_be_read_blocks()
    {
        var w = Grid("<ColumnDefinition Width=\"{Binding W}\"/><ColumnDefinition/>", InSecond);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
    }

    [Fact]
    public void Children_written_inside_Grid_Children_are_counted()
    {
        var w = In("""
            <Grid Width="300">
              <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/></Grid.ColumnDefinitions>
              <Grid.Children>
                <Border Width="90"/>
                <TextBlock x:Name="probe" Grid.Column="1"/>
              </Grid.Children>
            </Grid>
            """);
        Assert.Equal(210, w.Px, 3);
    }

    [Fact]
    public void An_Auto_column_sized_by_a_button_respects_MinWidth_and_access_keys()
    {
        // 按鈕：MinWidth 75 贏過內容；底線是快速鍵標記，不佔寬度
        var w = In("""
            <Grid Width="300">
              <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/></Grid.ColumnDefinitions>
              <Button Content="_OK" MinWidth="75" Margin="0,0,8,0"/>
              <TextBlock x:Name="probe" Grid.Column="1"/>
            </Grid>
            """);
        Assert.Equal(300 - 75 - 8, w.Px, 3);
    }
}
