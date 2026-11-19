using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 0.7.0：一般視窗的 Title 是 Windows 畫在標題列上的文字。工具沒有建模標題列，所以量不到，
/// 但要列出來（之前不知道它存在）—— 狀態是 DrawnByWindows，不進可解析率的分母（專案沒有辦法讓它量得到）。
/// </summary>
public class TitleSiteTests
{
    private static ScanResult Scan(string root)
    {
        var dir = TempProject(("Main.xaml", root));
        try { return new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, Fonts()).Scan(dir); }
        finally { Cleanup(dir); }
    }

    private static List<TextSite> Titles(string root) => [.. Scan(root).Sites.Where(s => s.Element == "Window.Title")];

    [Fact]
    public void A_standard_window_title_is_listed_as_drawn_by_windows()
    {
        var title = Assert.Single(Titles($"<Window {Ns} Title=\"Export settings\" Width=\"400\"><TextBlock Text=\"Hi\"/></Window>"));
        Assert.Equal("Export settings", title.Text);
        Assert.Equal(WidthStatus.DrawnByWindows, title.Width.Status);
    }

    [Fact]
    public void A_resource_title_keeps_its_key()
    {
        var title = Assert.Single(Titles($"<Window {Ns} xmlns:p=\"clr-namespace:App\" Title=\"{{x:Static p:Strings.ExportTitle}}\"/>"));
        Assert.Equal("ExportTitle", title.ResourceKey);
    }

    [Theory]
    [InlineData("WindowStyle=\"None\"")]
    [InlineData("xmlns:shell=\"x\" ")]
    public void A_window_that_draws_no_system_title_bar_has_no_title_site(string attributes)
    {
        var body = attributes.StartsWith("xmlns", StringComparison.Ordinal)
            ? "<WindowChrome.WindowChrome><WindowChrome CaptionHeight=\"30\"/></WindowChrome.WindowChrome>"
            : "";
        Assert.Empty(Titles($"<Window {Ns} {attributes} Title=\"Export settings\">{body}</Window>"));
    }

    [Fact]
    public void A_third_party_window_draws_its_own_title()
    {
        Assert.Empty(Titles($"<mah:MetroWindow {Ns} xmlns:mah=\"http://metro.mahapps.com/winfx/xaml/controls\" Title=\"Export settings\"/>"));
    }

    [Fact]
    public void Titles_do_not_change_the_resolution()
    {
        var dir = TempProject(("Main.xaml", $"<Window {Ns} Title=\"Export settings\" Width=\"400\" SizeToContent=\"Manual\"><StackPanel><TextBlock Width=\"100\" Text=\"Hello\"/></StackPanel></Window>"));
        try
        {
            var fonts = Fonts();
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, fonts).Scan(dir);
            var s = new BudgetAnalyzer(fonts, MonoFamily).Analyze(scan, "test").Summary;
            Assert.Equal(1, s.DrawnByWindowsSites);
            Assert.Equal(0, s.BlockedSites);
            Assert.Equal(100, s.Resolution, 3);
        }
        finally { Cleanup(dir); }
    }
}
