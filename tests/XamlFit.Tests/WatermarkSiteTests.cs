using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 0.7.0：輸入框的浮水印／提示文字是文字位置。寬度照 LayoutOracle --watermark 在真的視窗裡量的：
/// MahApps 2.4 的 TextBox 外框 − 10（Padding 不影響）、MaterialDesign 5.3 外框 − 明寫的 Padding、
/// WPF-UI 4.3 的 ui:TextBox 外框 − 21.6；字型是 TextBox 自己的。其他組合列出來但量不到。
/// </summary>
public class WatermarkSiteTests
{
    private const string MahApps =
        "<ResourceDictionary Source=\"pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml\"/>" +
        "<ResourceDictionary Source=\"pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml\"/>";

    private static string App(string merged) =>
        $"<Application {Ns} xmlns:md=\"http://materialdesigninxaml.net/winfx/xaml/themes\"><Application.Resources><ResourceDictionary>" +
        $"<ResourceDictionary.MergedDictionaries>{merged}</ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>";

    private static string Project(string package, string version) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"{package}\" Version=\"{version}\" /></ItemGroup></Project>";

    private static string Window(string body) =>
        $"<Window {Ns} xmlns:mah=\"http://metro.mahapps.com/winfx/xaml/controls\" xmlns:md=\"http://materialdesigninxaml.net/winfx/xaml/themes\" " +
        $"xmlns:hc=\"https://handyorg.github.io/handycontrol\" Width=\"400\"><StackPanel>{body}</StackPanel></Window>";

    private static TextSite Mark(params (string Path, string Content)[] files)
    {
        var dir = TempProject(files);
        try
        {
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize },
                Fonts(Mono(), Mono("Segoe UI"), Mono("Segoe UI", weight: 600), Mono("Segoe UI", weight: 700))).Scan(dir);
            return Assert.Single(scan.Sites, s => s.Element == "Watermark");
        }
        finally { Cleanup(dir); }
    }

    private static TextSite InMahApps(string box) => Mark(
        ("App.csproj", Project("MahApps.Metro", "2.4.11")), ("App.xaml", App(MahApps)), ("Main.xaml", Window(box)));

    private static TextSite InMaterialDesign(string box) => Mark(
        ("App.csproj", Project("MaterialDesignThemes", "5.3.0")),
        ("App.xaml", App("<md:BundledTheme BaseTheme=\"Light\"/><ResourceDictionary Source=\"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml\"/>")),
        ("Main.xaml", Window(box)));

    [Fact]
    public void A_mahapps_watermark_gets_the_box_width_minus_ten()
    {
        var mark = InMahApps("<TextBox Width=\"200\" HorizontalAlignment=\"Left\" mah:TextBoxHelper.Watermark=\"Type a name\"/>");
        Assert.Equal("Type a name", mark.Text);
        Assert.Equal(WidthStatus.Known, mark.Width.Status);
        Assert.Equal(190, mark.Width.Px, 3);
    }

    [Fact]
    public void Padding_does_not_move_a_mahapps_watermark()
    {
        var mark = InMahApps("<TextBox Width=\"200\" Padding=\"10,0\" HorizontalAlignment=\"Left\" mah:TextBoxHelper.Watermark=\"Type a name\"/>");
        Assert.Equal(190, mark.Width.Px, 3);
    }

    [Fact]
    public void A_material_design_hint_gets_the_box_width_minus_its_padding()
    {
        Assert.Equal(200, InMaterialDesign("<TextBox Width=\"200\" HorizontalAlignment=\"Left\" md:HintAssist.Hint=\"Type a name\"/>").Width.Px, 3);
        Assert.Equal(180, InMaterialDesign("<TextBox Width=\"200\" Padding=\"10,0,10,0\" HorizontalAlignment=\"Left\" md:HintAssist.Hint=\"Type a name\"/>").Width.Px, 3);
    }

    [Fact]
    public void A_resource_watermark_keeps_its_key()
    {
        var mark = InMahApps("<TextBox Width=\"200\" HorizontalAlignment=\"Left\" mah:TextBoxHelper.Watermark=\"{x:Static p:Strings.NameHint}\" xmlns:p=\"clr-namespace:App\"/>");
        Assert.Equal(TextSource.Resource, mark.Source);
        Assert.Equal("NameHint", mark.ResourceKey);
    }

    [Fact]
    public void A_watermark_without_its_libraries_theme_is_listed_but_not_measured()
    {
        var mark = Mark(("Main.xaml", Window("<TextBox Width=\"200\" mah:TextBoxHelper.Watermark=\"Type a name\"/>")));
        Assert.Equal(WidthStatus.Unmeasurable, mark.Width.Status);
    }

    [Fact]
    public void An_unmeasured_library_or_control_is_listed_but_not_measured()
    {
        Assert.Equal(WidthStatus.Unmeasurable, InMahApps("<TextBox Width=\"200\" hc:InfoElement.Placeholder=\"Type a name\"/>").Width.Status);
        Assert.Equal(WidthStatus.Unmeasurable, InMahApps("<ComboBox Width=\"200\" mah:TextBoxHelper.Watermark=\"Pick one\"/>").Width.Status);
    }

    [Fact]
    public void A_box_with_its_own_border_or_style_is_not_guessed()
    {
        Assert.Equal(WidthStatus.Unmeasurable, InMahApps("<TextBox Width=\"200\" BorderThickness=\"3\" mah:TextBoxHelper.Watermark=\"Type a name\"/>").Width.Status);
        Assert.Equal(WidthStatus.Unmeasurable, InMahApps("<TextBox Width=\"200\" Style=\"{StaticResource Mine}\" mah:TextBoxHelper.Watermark=\"Type a name\"/>").Width.Status);
    }

    [Fact]
    public void A_stretched_box_takes_its_width_from_the_layout()
    {
        // 沒寫 Width 的 TextBox 拉伸到視窗寬：上界照樣找得到（視窗 400 扣邊框，再扣浮水印的 10）
        var mark = InMahApps("<TextBox mah:TextBoxHelper.Watermark=\"Type a name\"/>");
        Assert.Equal(WidthStatus.Known, mark.Width.Status);
        Assert.True(mark.Width.Px < 390);
    }
}
