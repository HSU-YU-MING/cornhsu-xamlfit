using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 回報 4（ZeroCel，0.10.1）：按鈕與勾選框改用共用的內容樣板
/// <c>&lt;DataTemplate&gt;&lt;TextBlock Text="{Binding}" TextWrapping="Wrap"/&gt;&lt;/DataTemplate&gt;</c>，
/// 翻譯變長時折成兩行，工具卻照一行量 → 23 筆誤報。LayoutOracle --wrap 的「template:」案例 7 案行數都對得上。
/// 認得的只有「整個範本就是一個 TextBlock、Text 是 {Binding}」；其他範本量不到（以前照「沒有範本」量）。
/// </summary>
public class ContentTemplateTests
{
    private const string Templates =
        "<DataTemplate x:Key=\"WrapCenterText\"><TextBlock Text=\"{Binding}\" TextWrapping=\"Wrap\" TextAlignment=\"Center\"/></DataTemplate>" +
        "<DataTemplate x:Key=\"WrapLeftText\"><TextBlock Text=\"{Binding}\" TextWrapping=\"Wrap\"/></DataTemplate>" +
        "<DataTemplate x:Key=\"Plain\"><TextBlock Text=\"{Binding}\"/></DataTemplate>" +
        "<DataTemplate x:Key=\"WithIcon\"><StackPanel Orientation=\"Horizontal\"><Image Width=\"16\"/><TextBlock Text=\"{Binding}\"/></StackPanel></DataTemplate>" +
        "<DataTemplate x:Key=\"Bigger\"><TextBlock Text=\"{Binding}\" FontSize=\"20\" TextWrapping=\"Wrap\"/></DataTemplate>" +
        "<DataTemplate x:Key=\"OtherText\"><TextBlock Text=\"{Binding Title}\" TextWrapping=\"Wrap\"/></DataTemplate>";

    private static string Styles(string extra = "") => $"<ResourceDictionary {Ns}>{Templates}{extra}</ResourceDictionary>";

    private static string Window(string body, string resources = "") =>
        $"<Window {Ns} Width=\"400\" WindowStyle=\"None\" AllowsTransparency=\"True\"><Window.Resources>{resources}</Window.Resources>" +
        $"<Grid Width=\"300\" HorizontalAlignment=\"Left\">{body}</Grid></Window>";

    private static ScanResult Scan(params (string Path, string Content)[] files)
    {
        var dir = TempProject(files);
        try
        {
            return new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, Fonts()).Scan(dir);
        }
        finally { Cleanup(dir); }
    }

    private static TextSite Site(string body, string resources = "", string styles = "") =>
        Assert.Single(Scan(("Styles/Controls.xaml", Styles(styles)), ("Main.xaml", Window(body, resources))).Sites, s => s.Text == "Save the file");

    [Fact]
    public void A_shared_wrapping_template_makes_the_content_wrap()
    {
        var site = Site("<Button ContentTemplate=\"{StaticResource WrapCenterText}\" Content=\"Save the file\"/>");
        Assert.Equal(WrapMode.Wrap, site.Wrap);
        Assert.Equal(WidthStatus.Known, site.Width.Status);
    }

    [Fact]
    public void A_template_from_the_windows_own_resources_counts_too()
    {
        var site = Site("<CheckBox ContentTemplate=\"{StaticResource Mine}\" Content=\"Save the file\"/>",
            resources: "<DataTemplate x:Key=\"Mine\"><TextBlock Text=\"{Binding}\" TextWrapping=\"WrapWithOverflow\"/></DataTemplate>");
        Assert.Equal(WrapMode.WrapWithOverflow, site.Wrap);
    }

    [Fact]
    public void An_inline_template_counts_too()
    {
        var site = Site("<Button Content=\"Save the file\"><Button.ContentTemplate><DataTemplate><TextBlock Text=\"{Binding}\" TextWrapping=\"Wrap\"/></DataTemplate></Button.ContentTemplate></Button>");
        Assert.Equal(WrapMode.Wrap, site.Wrap);
        Assert.Equal(WidthStatus.Known, site.Width.Status);
    }

    [Fact]
    public void A_template_set_by_a_style_counts_too()
    {
        var site = Site("<CheckBox Style=\"{StaticResource WrapBox}\" Content=\"Save the file\"/>",
            styles: "<Style x:Key=\"WrapBox\" TargetType=\"CheckBox\"><Setter Property=\"ContentTemplate\" Value=\"{StaticResource WrapLeftText}\"/></Style>");
        Assert.Equal(WrapMode.Wrap, site.Wrap);
    }

    [Fact]
    public void A_plain_template_does_not_wrap()
    {
        var site = Site("<Button ContentTemplate=\"{StaticResource Plain}\" Content=\"Save the file\"/>");
        Assert.Equal(WrapMode.NoWrap, site.Wrap);
        Assert.Equal(WidthStatus.Known, site.Width.Status);
    }

    [Theory]
    [InlineData("WithIcon", "not a single TextBlock")]
    [InlineData("Bigger", "sets FontSize")]
    [InlineData("OtherText", "Text=\"{Binding Title}\"")]
    [InlineData("Missing", "cannot read")]
    public void Any_other_template_is_unmeasurable(string key, string why)
    {
        var site = Site($"<Button ContentTemplate=\"{{StaticResource {key}}}\" Content=\"Save the file\"/>");
        Assert.Equal(WidthStatus.Unmeasurable, site.Width.Status);
        Assert.Contains(why, site.Width.Reason);
    }

    [Fact]
    public void A_template_selector_is_unmeasurable()
    {
        var site = Site("<Button ContentTemplateSelector=\"{StaticResource Pick}\" Content=\"Save the file\"/>");
        Assert.Equal(WidthStatus.Unmeasurable, site.Width.Status);
        Assert.Contains("ContentTemplateSelector", site.Width.Reason);
    }

    [Fact]
    public void A_template_defined_twice_is_unmeasurable()
    {
        var scan = Scan(("Styles/Controls.xaml", Styles()), ("Styles/Other.xaml", Styles()),
            ("Main.xaml", Window("<Button ContentTemplate=\"{StaticResource WrapCenterText}\" Content=\"Save the file\"/>")));
        var site = Assert.Single(scan.Sites, s => s.Text == "Save the file");
        Assert.Equal(WidthStatus.Unmeasurable, site.Width.Status);
        Assert.Contains("2 places", site.Width.Reason);
    }

    [Fact]
    public void An_implicit_textblock_style_that_changes_the_font_makes_it_unmeasurable()
    {
        var site = Site("<Button ContentTemplate=\"{StaticResource WrapCenterText}\" Content=\"Save the file\"/>",
            styles: "<Style TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"16\"/></Style>");
        Assert.Equal(WidthStatus.Unmeasurable, site.Width.Status);
        Assert.Contains("gets FontSize from a Style", site.Width.Reason);
    }

    [Fact]
    public void An_unknown_template_with_no_upper_bound_is_still_no_declared_limit()
    {
        // 擋路者不立刻放棄：上面根本沒有上界，答案仍是「查不到上界」，不灌進可解析率的分母
        var scan = Scan(("Styles/Controls.xaml", Styles()),
            ("Main.xaml", $"<Window {Ns} SizeToContent=\"Width\"><StackPanel><Button ContentTemplate=\"{{StaticResource WithIcon}}\" Content=\"Save the file\"/></StackPanel></Window>"));
        var site = Assert.Single(scan.Sites, s => s.Text == "Save the file");
        Assert.Equal(WidthStatus.NoDeclaredLimit, site.Width.Status);
    }

    private static CheckResult Check(string button)
    {
        var dir = TempProject(("Styles/Controls.xaml", Styles()),
            ("Main.xaml", $"<Window {Ns} WindowStyle=\"None\" AllowsTransparency=\"True\"><StackPanel Width=\"100\" HorizontalAlignment=\"Left\">{button}</StackPanel></Window>"));
        try
        {
            var fonts = Fonts();
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, fonts).Scan(dir);
            var budget = new BudgetAnalyzer(fonts, MonoFamily).Analyze(scan, "test");
            return new CheckAnalyzer(fonts, MonoFamily, marginPercent: 5).Analyze(scan, budget, [], false);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Check_no_longer_reports_a_wrapping_template_as_an_overflow()
    {
        var text = string.Join(' ', Enumerable.Repeat("Save", 10));   // 49 字 × 5 = 245 px，放在 100 寬裡
        Assert.Contains(Check($"<Button Content=\"{text}\"/>").Findings, f => f.Kind == FitKind.Overflow);
        Assert.DoesNotContain(Check($"<Button ContentTemplate=\"{{StaticResource WrapCenterText}}\" Content=\"{text}\"/>").Findings, f => f.Kind == FitKind.Overflow);
    }
}
