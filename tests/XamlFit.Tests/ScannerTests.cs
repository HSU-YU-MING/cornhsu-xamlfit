using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>掃描整個目錄才看得出來的行為：方言、外部佈景、字串字典、跨檔案的字型繼承。</summary>
public class ScannerTests
{
    private const string Sys = "xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"";

    private static ScanResult Scan(ScanOptions? options, params (string Path, string Content)[] files)
    {
        var dir = TempProject(files);
        try
        {
            return new XamlScanner(Chrome, options ?? new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, Fonts())
                .Scan(dir);
        }
        finally { Cleanup(dir); }
    }

    private static ScanResult Scan(params (string Path, string Content)[] files) => Scan(null, files);

    private static string Window(string body, string attributes = "Width=\"400\"") =>
        $"<Window {Ns} {Sys} xmlns:p=\"clr-namespace:App.Properties\" xmlns:local=\"clr-namespace:App\" {attributes}>{body}</Window>";

    private static TextSite Site(ScanResult scan, string text) => scan.Sites.Single(s => s.Text == text || s.ResourceKey == text);

    // ───────────────────────── 方言 ─────────────────────────

    [Fact]
    public void WinUI_files_are_reported_not_analysed()
    {
        // ⚠ 語料裡的 WinUI-Gallery 一直被當成 WPF 在算，還報出「可解析率 99.2%」
        var scan = Scan(
            ("Wpf/Main.xaml", Window("<TextBlock Width=\"100\" Text=\"Hello\"/>")),
            ("WinUi/Page.xaml", $"<Page {Ns} xmlns:local=\"using:Gallery\"><TextBlock Width=\"100\" Text=\"Uwp\"/></Page>"));

        Assert.Single(scan.ForeignFiles);
        Assert.Equal("WinUI/UWP", scan.ForeignFiles[0].Dialect);
        Assert.DoesNotContain(scan.Sites, s => s.Text == "Uwp");
        Assert.Contains(scan.Sites, s => s.Text == "Hello");
    }

    [Fact]
    public void A_file_with_no_marker_follows_its_neighbours()
    {
        // 只用預設命名空間的小檔案：WinUI 與 WPF 的 URI 一樣，看同一個目錄的其他檔案決定
        var scan = Scan(
            ("WinUi/Page.xaml", $"<Page {Ns} xmlns:local=\"using:Gallery\"/>"),
            ("WinUi/Small.xaml", $"<Grid {Ns}><TextBlock Width=\"100\" Text=\"Ambiguous\"/></Grid>"),
            ("Wpf/Main.xaml", Window("")),
            ("Wpf/Small.xaml", $"<Grid {Ns}><TextBlock Width=\"100\" Text=\"Plain\"/></Grid>"));

        Assert.Equal(2, scan.ForeignFiles.Count);
        Assert.DoesNotContain(scan.Sites, s => s.Text == "Ambiguous");
        Assert.Contains(scan.Sites, s => s.Text == "Plain");
    }

    [Theory]
    [InlineData("https://github.com/avaloniaui", "Avalonia")]
    [InlineData("http://schemas.microsoft.com/dotnet/2021/maui", ".NET MAUI")]
    public void Other_xaml_dialects_are_recognised(string ns, string dialect)
    {
        var scan = Scan(("View.xaml", $"<ContentPage xmlns=\"{ns}\"><Label Text=\"x\"/></ContentPage>"));
        Assert.Equal(dialect, scan.ForeignFiles.Single().Dialect);
    }

    // ───────────────────────── 外部佈景 ─────────────────────────

    private const string ButtonPage = "<Button Width=\"100\" Content=\"Save\"/>";

    private static string App(string merged) =>
        $"<Application {Ns} xmlns:md=\"http://materialdesigninxaml.net/winfx/xaml/themes\"><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>{merged}</ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>";

    [Fact]
    public void A_dictionary_from_another_assembly_is_an_external_theme()
    {
        var scan = Scan(
            ("App.xaml", App("<ResourceDictionary Source=\"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/Defaults.xaml\"/>")),
            ("Main.xaml", Window(ButtonPage)));

        Assert.Equal("MaterialDesignThemes.Wpf", scan.ExternalThemes.Single().Theme);
        var site = Site(scan, "Save");
        Assert.Equal(WidthStatus.Unmeasurable, site.Width.Status);
        Assert.Contains("external theme", site.Width.Reason);
    }

    [Fact]
    public void A_third_party_dictionary_element_is_an_external_theme()
    {
        var scan = Scan(
            ("App.xaml", App("<md:BundledTheme BaseTheme=\"Dark\"/>")),
            ("Main.xaml", Window(ButtonPage)));
        Assert.Equal("md:BundledTheme", scan.ExternalThemes.Single().Theme);
    }

    [Fact]
    public void A_component_path_that_exists_in_the_project_is_not_external()
    {
        // 判斷「是不是自己的」不靠組件名（靜態不知道），靠那條路徑在掃描範圍裡找不找得到
        var scan = Scan(
            ("App.xaml", App("<ResourceDictionary Source=\"/MyApp;component/Themes/Buttons.xaml\"/>")),
            ("Themes/Buttons.xaml", $"<ResourceDictionary {Ns}/>"),
            ("Main.xaml", Window(ButtonPage)));

        Assert.Empty(scan.ExternalThemes);
        Assert.Equal(96, Site(scan, "Save").Width.Px);
    }

    [Fact]
    public void An_external_theme_only_affects_its_own_application()
    {
        var scan = Scan(
            ("Themed/App.xaml", App("<md:BundledTheme/>")),
            ("Themed/Main.xaml", Window("<Button Width=\"100\" Content=\"Themed\"/>")),
            ("Plain/App.xaml", App("")),
            ("Plain/Main.xaml", Window("<Button Width=\"100\" Content=\"Plain\"/>")));

        Assert.Equal(WidthStatus.Unmeasurable, Site(scan, "Themed").Width.Status);
        Assert.Equal(96, Site(scan, "Plain").Width.Px);
    }

    [Fact]
    public void The_user_can_vouch_for_the_default_chrome()
    {
        var scan = Scan(
            new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, TrustDefaultChrome = true },
            ("App.xaml", App("<md:BundledTheme/>")),
            ("Main.xaml", Window(ButtonPage)));
        Assert.Equal(96, Site(scan, "Save").Width.Px);
    }

    // ───────────────────────── 字串字典 ─────────────────────────

    private static string Strings(params (string Key, string Value)[] entries) =>
        $"<ResourceDictionary {Ns} {Sys}>" +
        string.Concat(entries.Select(e => $"<sys:String x:Key=\"{e.Key}\">{e.Value}</sys:String>")) +
        "</ResourceDictionary>";

    [Fact]
    public void DynamicResource_strings_are_localizable_text()
    {
        // ⚠ 這是 WPF 最常見的在地化寫法（語料：Playnite 1,038 處、ScreenToGif 1,540 處）。
        //   第一版只認 {x:Static} 加 .resx，把這些全部當成「綁資料」略過。
        var scan = Scan(
            ("Strings/Strings.xaml", Strings(("S.Save", "Save"))),
            ("Strings/Strings.de.xaml", Strings(("S.Save", "Speichern"))),
            ("Main.xaml", Window("<Button Width=\"100\" Content=\"{DynamicResource S.Save}\"/>")));

        var site = Site(scan, "S.Save");
        Assert.Equal(TextSource.Resource, site.Source);
        Assert.Equal("Save", site.SourceText);

        var cultures = scan.Strings.Select(s => s.Culture).OrderBy(c => c).ToList();
        Assert.Equal([ResxReader.NeutralCulture, "de"], cultures);
    }

    [Fact]
    public void A_resource_that_is_not_a_string_is_not_text()
    {
        var scan = Scan(("Main.xaml", Window("<Button Width=\"100\" Content=\"{StaticResource SaveIcon}\"/>")));
        Assert.Equal(TextSource.DataBound, scan.Sites.Single().Source);
    }

    [Theory]
    [InlineData("Strings.de.xaml", "de")]
    [InlineData("de_DE.xaml", "de-DE")]
    [InlineData("pt-BR.xaml", "pt-BR")]
    [InlineData("LocSource.xaml", "(neutral)")]
    [InlineData("sr_SP.xaml", "sr-SP")]          // .NET 不認得的舊代碼：仍然是一個語言，不是來源語言
    [InlineData("Strings.sr-SP.xaml", "sr-SP")]
    [InlineData("Colors.xaml", "(neutral)")]
    [InlineData("Buttons.xaml", "(neutral)")]
    public void A_string_dictionary_takes_its_culture_from_the_file_name(string file, string culture) =>
        Assert.Equal(culture, XamlScanner.CultureOfDictionary(file));

    [Fact]
    public void English_is_the_source_when_there_is_no_neutral_dictionary()
    {
        // 實查 ScreenToGif：基底是 StringResources.en.xaml，沒有中性的那一份
        var scan = Scan(
            ("L/Strings.en.xaml", Strings(("K", "Hello"))),
            ("L/Strings.de.xaml", Strings(("K", "Hallo"))),
            ("Main.xaml", Window("<TextBlock Width=\"100\" Text=\"{DynamicResource K}\"/>")));
        Assert.Equal("Hello", Site(scan, "K").SourceText);
    }

    // ───────────────────────── 文字的認法 ─────────────────────────

    [Fact]
    public void A_converter_is_not_a_resource_key()
    {
        // ⚠ 第一版用不設錨點的比對：Converter={x:Static local:Converters.Upper} 被當成資源鍵 Upper
        var scan = Scan(("Main.xaml", Window("<TextBlock Width=\"100\" Text=\"{Binding Name, Converter={x:Static local:Converters.Upper}}\"/>")));
        Assert.Equal(TextSource.DataBound, scan.Sites.Single().Source);
    }

    [Theory]
    [InlineData("{x:Static p:Strings.Save}")]
    [InlineData("{Binding Source={x:Static p:Strings.Save}}")]
    [InlineData("{Binding Source={x:Static p:Strings.Save}, Mode=OneWay}")]
    public void Static_resource_strings_are_recognised(string markup)
    {
        var scan = Scan(("Main.xaml", Window($"<TextBlock Width=\"100\" Text=\"{markup}\"/>")));
        Assert.Equal("Save", scan.Sites.Single().ResourceKey);
    }

    [Fact]
    public void Text_written_as_element_content_is_found()
    {
        var scan = Scan(("Main.xaml", Window("""
            <StackPanel>
              <Button Width="100">Save</Button>
              <Label Width="100">Name:</Label>
              <TextBlock Width="300">
                Several words
                on two lines
              </TextBlock>
            </StackPanel>
            """)));
        Assert.Contains(scan.Sites, s => s.Text == "Save" && s.Element == "Button");
        Assert.Contains(scan.Sites, s => s.Text == "Name:");
        // 連續空白（含換行與縮排）收成一個空格
        Assert.Contains(scan.Sites, s => s.Text == "Several words on two lines");
    }

    [Fact]
    public void Third_party_text_controls_are_seen()
    {
        // 第一版用精確名稱比對，第三方控制項上的文字整批沒被看到 —— 不是「量不到」，是「不知道它存在」
        var scan = Scan(("Main.xaml", Window("<mah:MetroButton xmlns:mah=\"http://example.com/mah\" Width=\"100\" Content=\"Go\"/>")));
        var site = Site(scan, "Go");
        Assert.Equal(WidthStatus.Unmeasurable, site.Width.Status);   // 看到了，但內距不知道
    }

    [Fact]
    public void A_Run_that_shares_its_line_gets_what_the_runs_before_it_leave()
    {
        // 不換行的一行從左邊畫起、右邊被裁：後面的 Run 只剩前面的內容用剩的寬度
        var scan = Scan(("Main.xaml", Window("<TextBlock Width=\"100\"><Run Text=\"Hello\"/><Run Text=\"World\"/></TextBlock>")));
        Assert.Equal(100, Site(scan, "Hello").Width.Px);
        Assert.Equal(75, Site(scan, "World").Width.Px);
    }

    [Fact]
    public void A_lone_Run_is_measured()
    {
        var scan = Scan(("Main.xaml", Window("<TextBlock Width=\"100\"><Run Text=\"Hello\"/></TextBlock>")));
        Assert.Equal(100, scan.Sites.Single().Width.Px);
    }

    [Fact]
    public void Wrapping_is_read_from_the_text_block_not_inherited()
    {
        var scan = Scan(("Main.xaml", Window("""
            <StackPanel TextBlock.TextWrapping="Wrap">
              <TextBlock Width="100" Text="Plain"/>
              <TextBlock Width="100" TextWrapping="Wrap" Text="Wraps"/>
              <Button Width="100" Content="Button"/>
            </StackPanel>
            """)));
        Assert.Equal(WrapMode.NoWrap, Site(scan, "Plain").Wrap);
        Assert.Equal(WrapMode.Wrap, Site(scan, "Wraps").Wrap);
        Assert.Equal(WrapMode.NoWrap, Site(scan, "Button").Wrap);
    }

    // ───────────────────────── 字型 ─────────────────────────

    [Fact]
    public void A_site_without_font_metrics_is_not_counted_as_measured()
    {
        // ⚠ 第一版照樣算成量到了，然後在判定時默默跳過
        var scan = Scan(("Main.xaml", Window("<TextBlock Width=\"100\" FontFamily=\"Papyrus\" Text=\"Hello\"/>")));
        Assert.Equal(WidthStatus.Unmeasurable, scan.Sites.Single().Width.Status);
        Assert.Equal(["Papyrus"], scan.MissingFonts);
    }

    [Fact]
    public void An_icon_font_does_not_trigger_a_missing_font_warning()
    {
        // 圖示字型上的符號不是翻譯對象
        var scan = Scan(("Main.xaml", Window("<TextBlock Width=\"100\" FontFamily=\"Segoe MDL2 Assets\" Text=\"&#xE8FB;\"/>")));
        Assert.Empty(scan.MissingFonts);
    }

    [Fact]
    public void An_unreadable_font_makes_the_site_unmeasurable()
    {
        var scan = Scan(("Main.xaml", Window("<TextBlock Width=\"100\" FontFamily=\"{StaticResource UiFont}\" Text=\"Hello\"/>")));
        Assert.Equal(WidthStatus.Unmeasurable, scan.Sites.Single().Width.Status);
        Assert.Contains("FontFamily", scan.Sites.Single().Width.Reason);
    }

    private static string Control(string body) => $"<UserControl {Ns}>{body}</UserControl>";

    [Fact]
    public void A_user_control_inherits_the_font_of_the_window_that_hosts_it()
    {
        // ⚠ 自我檢驗抓到的：主視窗設了字型，各個分頁是獨立的 UserControl 檔案。
        //   第一版每個檔案各自從預設值算起。
        var scan = Scan(
            ("Main.xaml", Window("", "Width=\"400\" FontSize=\"20\"")),
            ("Page.xaml", Control("<TextBlock Width=\"100\" Text=\"Hello\"/>")));
        Assert.Equal(20, Site(scan, "Hello").FontSize);
    }

    [Fact]
    public void Windows_that_disagree_leave_the_inherited_font_unknown()
    {
        var scan = Scan(
            ("Main.xaml", Window("", "Width=\"400\" FontSize=\"20\"")),
            ("Dialog.xaml", Window("<TextBlock Width=\"100\" Text=\"InWindow\"/>")),
            ("Page.xaml", Control("<TextBlock Width=\"100\" Text=\"Hosted\"/>")));

        Assert.Equal(WidthStatus.Unmeasurable, Site(scan, "Hosted").Width.Status);
        Assert.Contains("which window hosts", Site(scan, "Hosted").Width.Reason);
        // 寫在視窗檔案裡的文字不受影響：它的字型就在同一個檔案裡
        Assert.Equal(WidthStatus.Known, Site(scan, "InWindow").Width.Status);
    }

    [Fact]
    public void A_pinned_default_font_settles_the_disagreement()
    {
        var scan = Scan(
            new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, DefaultFontPinned = true },
            ("Main.xaml", Window("", "Width=\"400\" FontSize=\"20\"")),
            ("Dialog.xaml", Window("")),
            ("Page.xaml", Control("<TextBlock Width=\"100\" Text=\"Hosted\"/>")));
        Assert.Equal(WidthStatus.Known, Site(scan, "Hosted").Width.Status);
        Assert.Equal(MonoSize, Site(scan, "Hosted").FontSize);
    }

    [Fact]
    public void A_control_that_sets_its_own_font_does_not_ask_the_window()
    {
        var scan = Scan(
            ("Main.xaml", Window("", "Width=\"400\" FontSize=\"{Binding Size}\"")),
            ("Page.xaml", Control("<TextBlock Width=\"100\" FontSize=\"14\" Text=\"Own\"/>")));
        Assert.Equal(WidthStatus.Known, Site(scan, "Own").Width.Status);
        Assert.Equal(14, Site(scan, "Own").FontSize);
    }

    // ───────────────────────── 其他 ─────────────────────────

    [Fact]
    public void TreatAs_lets_a_subclass_borrow_the_rules_of_its_base()
    {
        const string page =
            "<c:GridEx xmlns:c=\"clr-namespace:App.Controls\" Width=\"400\"><c:GridEx.ColumnDefinitions><ColumnDefinition Width=\"120\"/><ColumnDefinition/></c:GridEx.ColumnDefinitions><TextBlock Text=\"Hello\"/></c:GridEx>";

        var plain = Scan(("Main.xaml", Window(page)));
        Assert.Equal(WidthStatus.Unmeasurable, Site(plain, "Hello").Width.Status);

        var aliased = Scan(
            new ScanOptions
            {
                DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize,
                TreatAs = new Dictionary<string, string> { ["GridEx"] = "Grid" },
            },
            ("Main.xaml", Window(page)));
        Assert.Equal(120, Site(aliased, "Hello").Width.Px);
    }

    [Fact]
    public void Rescanning_with_another_cultures_strings_changes_shared_rows()
    {
        // 同一排的兄弟也是在地化字串：留給這個位置的寬度取決於兄弟在那個語言有多寬
        var dir = TempProject(
            ("S/Strings.xaml", Strings(("Ok", "OK"), ("Label", "Name"))),
            ("Main.xaml", Window("""
                <Border Width="300">
                  <StackPanel Orientation="Horizontal">
                    <TextBlock Text="{DynamicResource Label}"/>
                    <TextBlock x:Name="target" Text="{DynamicResource Ok}"/>
                  </StackPanel>
                </Border>
                """)));
        try
        {
            var scanner = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, Fonts());

            var source = scanner.Scan(dir);
            var ok = source.Sites.Single(s => s.ResourceKey == "Ok");
            Assert.Equal(300 - 4 * 5, ok.Width.Px);            // "Name" = 4 字 × 5 px
            Assert.True(ok.Width.DependsOnNeighbourText);
            Assert.True(source.NeighbourTextUses > 0);

            var german = scanner.Rescan(MonoFamily, new Dictionary<string, string> { ["Label"] = "Bezeichnung", ["Ok"] = "OK" });
            Assert.Equal(300 - 11 * 5, german.Sites.Single(s => s.ResourceKey == "Ok").Width.Px);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Files_are_visited_in_a_stable_order()
    {
        // 檔案系統的列舉順序在 Windows 與 Linux 不一樣；報告與 baseline 不該隨機器而變
        var scan = Scan(
            ("b/Two.xaml", Window("<TextBlock Width=\"100\" Text=\"Two\"/>")),
            ("a/One.xaml", Window("<TextBlock Width=\"100\" Text=\"One\"/>")),
            ("c.xaml", Window("<TextBlock Width=\"100\" Text=\"Three\"/>")));
        Assert.Equal(["a/One.xaml", "b/Two.xaml", "c.xaml"], scan.Sites.Select(s => s.File));
    }

    [Theory]
    [InlineData("<DataGrid.Resources><Style TargetType=\"DataGridColumnHeader\"><Setter Property=\"FontSize\" Value=\"16\"/><Setter Property=\"FontWeight\" Value=\"SemiBold\"/></Style></DataGrid.Resources>", 16, 600)]
    [InlineData("", 14, 400)]
    public void A_column_header_takes_its_font_from_the_header_style_or_the_grid_not_the_column(string resources, double size, int weight)
    {
        // ⚠ LayoutOracle --header-font 實測：欄身上的 FontSize／FontWeight 是給儲存格的
        var scan = Scan(("Main.xaml", Window(
            $"<DataGrid FontSize=\"14\">{resources}<DataGrid.Columns><DataGridTextColumn Header=\"Name\" Width=\"150\" FontSize=\"20\" FontWeight=\"Light\"/></DataGrid.Columns></DataGrid>")));
        var site = Site(scan, "Name");
        Assert.Equal(size, site.FontSize);
        Assert.Equal(weight, site.FontWeight);
    }

    // ───────────────────────── 行內的 Run ─────────────────────────
    // 等寬測試字型：每個字與空格都是 5 px。WPF 的規則由 LayoutOracle --inlines 實測：
    // 元素之間的換行縮排 = 一個空格（用 TextBlock 的字型）；LineBreak 兩側的空白會被修掉。

    private static ScanResult Inline(string inlines, string textBlock = "") =>
        Scan(("Main.xaml", Window($"<Border Width=\"300\"><TextBlock {textBlock}>{inlines}</TextBlock></Border>")));

    [Fact]
    public void A_Run_on_its_own_line_is_an_ordinary_line_of_text()
    {
        // ⚠ 第一版只要 Run 有兄弟（連 LineBreak 也算）就放棄：QuillNest 的條列說明 32 處全部量不到
        var scan = Inline("<Run Text=\"AAAA\"/>\n  <LineBreak/>\n  <Run Text=\"BB\"/>");
        Assert.All(scan.Sites, site => Assert.Equal(300, site.Width.Px));
    }

    [Theory]
    [InlineData("<Run Text=\"AAAA\"/>\n  <Run Text=\"BB\"/>", 275)]   // AAAA 20 + 空格 5
    [InlineData("<Run Text=\"AAAA\"/><Run Text=\"BB\"/>", 280)]         // 緊貼著寫：沒有空格
    public void On_a_line_that_does_not_wrap_a_Run_gets_what_the_content_before_it_leaves(string inlines, double expected)
    {
        var scan = Inline(inlines);
        Assert.Equal(300, Site(scan, "AAAA").Width.Px);
        Assert.Equal(expected, Site(scan, "BB").Width.Px);
    }

    [Theory]
    [InlineData("<Hyperlink><Run Text=\"BB\"/></Hyperlink>", WidthStatus.Known)]           // Playnite 的「Having issues?」
    [InlineData("<Span><Hyperlink><Run Text=\"BB\"/></Hyperlink></Span>", WidthStatus.Known)]
    [InlineData("<Bold><Run Text=\"BB\"/></Bold>", WidthStatus.Unmeasurable)]              // 字重變了，字型讀取沒套用
    [InlineData("<Hyperlink><Run Text=\"AAAA\"/><Run Text=\"BB\"/></Hyperlink>", WidthStatus.Unmeasurable)]
    public void A_Run_alone_in_a_link_is_the_link(string inlines, WidthStatus expected)
    {
        var site = Site(Inline(inlines), "BB");
        Assert.Equal(expected, site.Width.Status);
        if (expected == WidthStatus.Known) Assert.Equal(300, site.Width.Px);
    }

    [Fact]
    public void On_a_wrapping_line_without_a_height_limit_nothing_is_clipped_sideways()
    {
        var scan = Inline("<Run Text=\"AAAA\"/>\n  <Run Text=\"BB\"/>", "TextWrapping=\"Wrap\"");
        Assert.All(scan.Sites, site => Assert.Equal(300, site.Width.Px));
    }

    [Theory]
    [InlineData("<Run Text=\"AAAA\"/>\n  <Run Text=\"BB\"/>", "TextWrapping=\"Wrap\" Height=\"20\"")]   // 要整段一起排才知道幾行
    [InlineData("<Run Text=\"{Binding Name}\"/>\n  <Run Text=\"BB\"/>", "")]                              // 前面的寬度不知道
    [InlineData("<Span><Run Text=\"AAAA\"/></Span>\n  <Run Text=\"BB\"/>", "")]                        // 前面是 Span：寬度沒有加總
    public void A_Run_whose_line_cannot_be_laid_out_is_unmeasurable(string inlines, string textBlock)
    {
        var site = Site(Inline(inlines, textBlock), "BB");
        Assert.Equal(WidthStatus.Unmeasurable, site.Width.Status);
    }

    [Fact]
    public void A_pathologically_nested_document_is_a_parse_error_not_a_hang()
    {
        // ⚠ 100,000 層、1.7 MB 的檔案讓 budget 與 check 跑超過 170 秒沒結束（CI 上就是卡到逾時）
        var depth = SafeXml.MaxDepth + 10;
        var xaml = "<Window " + Ns + ">" + string.Concat(Enumerable.Repeat("<Border>", depth)) + "<TextBlock Text=\"x\"/>"
                   + string.Concat(Enumerable.Repeat("</Border>", depth)) + "</Window>";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var scan = Scan(("Deep.xaml", xaml));
        Assert.Equal(1, scan.ParseErrors);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_document_with_a_doctype_is_a_parse_error_not_an_entity_expansion()
    {
        var scan = Scan(("Bad.xaml", "<!DOCTYPE x [<!ENTITY e \"boom\">]><Window " + Ns + "><TextBlock Text=\"&e;\"/></Window>"));
        Assert.Equal(1, scan.ParseErrors);
    }
}
