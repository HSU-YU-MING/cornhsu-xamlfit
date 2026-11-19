using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 0.9.0：設定檔 windowTitleBar: "windows11" 時，一般視窗的標題照 Windows 11 實量的標題列檢查
/// （prototype/TitleBar：100%～175%、四種視窗寬，每種視窗類型取最大的損失）。字型是 Segoe UI 12 的線性寬度。
/// 沒宣告：照舊 drawn-by-windows。Mono 當 Segoe UI：每個字 6 px（字級 12）。
/// </summary>
public class WindowTitleBarTests
{
    private static List<TextSite> Titles(string window, bool declared = true, params Core.Fonts.FontMetrics[] fonts)
    {
        var dir = TempProject(("Main.xaml", window));
        try
        {
            var options = new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, WindowTitleBarWindows11 = declared };
            var resolver = fonts.Length > 0 ? Fonts(fonts) : Fonts(Mono(), Mono("Segoe UI"));
            return [.. new XamlScanner(Chrome, options, resolver).Scan(dir).Sites.Where(s => s.Element == "Window.Title")];
        }
        finally { Cleanup(dir); }
    }

    private static TextSite Title(string attributes, bool declared = true) =>
        Assert.Single(Titles($"<Window {Ns} Title=\"Export settings\" {attributes}/>", declared));

    [Theory]
    [InlineData("Width=\"400\"", 400 - 194.0)]
    [InlineData("Width=\"400\" ResizeMode=\"CanMinimize\"", 400 - 194.0)]
    [InlineData("Width=\"400\" ResizeMode=\"NoResize\"", 400 - 96.0)]
    [InlineData("Width=\"400\" WindowStyle=\"ThreeDBorderWindow\"", 400 - 200.0)]
    [InlineData("Width=\"400\" WindowStyle=\"ToolWindow\"", 400 - 76.0)]
    public void The_title_gets_the_window_width_minus_the_measured_title_bar(string attributes, double expected)
    {
        var title = Title(attributes);
        Assert.Equal(WidthStatus.Known, title.Width.Status);
        Assert.Equal(expected, title.Width.Px, 3);
        Assert.Equal("Segoe UI", title.FontFamily);
        Assert.Equal(12, title.FontSize);
        Assert.Equal(TrimMode.CharacterEllipsis, title.Trim);
    }

    [Fact]
    public void Without_the_declaration_the_title_is_not_checked()
    {
        Assert.Equal(WidthStatus.DrawnByWindows, Title("Width=\"400\"", declared: false).Width.Status);
    }

    [Fact]
    public void A_combination_that_was_not_measured_is_not_guessed()
    {
        Assert.Equal(WidthStatus.DrawnByWindows, Title("Width=\"400\" WindowStyle=\"ThreeDBorderWindow\" ResizeMode=\"NoResize\"").Width.Status);
    }

    [Fact]
    public void A_window_without_a_width_has_no_declared_limit()
    {
        Assert.Equal(WidthStatus.NoDeclaredLimit, Title("").Width.Status);
        Assert.Equal(WidthStatus.NoDeclaredLimit, Title("Width=\"400\" SizeToContent=\"WidthAndHeight\"").Width.Status);
    }

    [Fact]
    public void A_title_needs_the_title_font_in_the_metrics()
    {
        var title = Assert.Single(Titles($"<Window {Ns} Title=\"Export settings\" Width=\"400\"/>", true, Mono()));
        Assert.Equal(WidthStatus.Unmeasurable, title.Width.Status);
    }

    [Fact]
    public void A_long_title_is_reported_as_trimmed()
    {
        // Width 300 − 194 = 106 px；60 個字 × 6 = 360 px
        var dir = TempProject(("Main.xaml", $"<Window {Ns} Width=\"300\" Title=\"{new string('A', 60)}\"><TextBlock Text=\"x\"/></Window>"));
        try
        {
            var fonts = Fonts(Mono(), Mono("Segoe UI"));
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, WindowTitleBarWindows11 = true }, fonts).Scan(dir);
            var budget = new BudgetAnalyzer(fonts, MonoFamily).Analyze(scan, "test");
            var result = new CheckAnalyzer(fonts, MonoFamily, marginPercent: 5).Analyze(scan, budget, [], false);
            Assert.Contains(result.Findings, f => f.Kind == FitKind.Trimmed);
        }
        finally { Cleanup(dir); }
    }

    private static Core.Fonts.Measurement Measure(string title)
    {
        // Segoe UI 沒有中文：WPF 會拿後備表（這裡的 Yu Gothic UI）補，但 Windows 畫標題時用的是它自己的字型連結
        var dir = TempProject(("Main.xaml", $"<Window {Ns} Width=\"300\" Title=\"{title}\"><TextBlock Text=\"x\"/></Window>"));
        try
        {
            var fonts = Fonts(Mono(), Mono("Segoe UI", cjk: false), Mono("Yu Gothic UI"));
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, WindowTitleBarWindows11 = true }, fonts).Scan(dir);
            var site = Assert.Single(scan.Sites, s => s.Element == "Window.Title");
            return SiteMeasure.Widest(site, site.Text, fonts.Resolve(site.FontFamily, 400, Core.Fonts.FontStyleKind.Normal, MonoFamily, site.Language));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Cjk_ideographs_in_a_title_are_one_em_each()
    {
        // 實量「另存新檔設定」畫出 70.4～72 DIP = 6 × 12：Windows 補上的中日韓字型裡，表意文字都是 1 em
        var m = Measure("匯出設定 v2");
        Assert.True(m.IsReliable);
        Assert.Equal(4 * 12 + 3 * 6, m.WidthPx, 3);   // 四個字 1 em，「 v2」三個字照 Segoe UI（Mono 6 px）
    }

    [Fact]
    public void Other_characters_the_title_font_lacks_are_not_measured()
    {
        // 假名、韓文、泰文…：Windows 的字型連結補哪支、多寬沒量過
        Assert.False(Measure("エクスポート").IsReliable);
        Assert.False(Measure("내보내기").IsReliable);
    }
}
