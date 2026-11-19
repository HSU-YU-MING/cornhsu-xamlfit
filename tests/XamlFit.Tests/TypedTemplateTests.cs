using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 回報 5（ZeroCel 工作室版，0.11.0）：五個頁面用 Window.Resources 裡依型別、沒有 key 的 DataTemplate
/// 放進 <c>&lt;ContentControl Content="{Binding CurrentPage}"/&gt;</c>（MVVM 換頁）。工具走到 DataTemplate 就停，
/// 128 處裡 89 處擋住或沒查。同一個資源範圍裡只有一個地方畫得出它時，從那個 ContentControl 接著走
/// （LayoutOracle --wrap「typed template:」三案行數一致）。
/// Mono 字級 10：每個字 5 px。
/// </summary>
public class TypedTemplateTests
{
    private const string Views = "xmlns:v=\"clr-namespace:App.Views\" xmlns:vm=\"clr-namespace:App.ViewModels\"";

    private static string Main(string resources, string host, string extra = "") =>
        $"<Window {Ns} {Views} Width=\"1240\" WindowStyle=\"None\" AllowsTransparency=\"True\">" +
        $"<Window.Resources>{resources}</Window.Resources>" +
        "<Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"220\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions>" +
        $"<Border Grid.Column=\"0\"><ItemsControl ItemsSource=\"{{Binding Pages}}\"><ItemsControl.ItemTemplate><DataTemplate><TextBlock Text=\"{{Binding}}\"/></DataTemplate></ItemsControl.ItemTemplate></ItemsControl></Border>" +
        $"{host}{extra}</Grid></Window>";

    private const string Pages =
        "<DataTemplate DataType=\"{x:Type vm:IntakeViewModel}\"><v:IntakeView/></DataTemplate>" +
        "<DataTemplate DataType=\"{x:Type vm:QcViewModel}\"><v:QcView/></DataTemplate>";

    private const string Host = "<ContentControl x:Name=\"PageHost\" Grid.Column=\"1\" Content=\"{Binding CurrentPage}\"/>";

    private static string Page(string cls) =>
        $"<UserControl {Ns} x:Class=\"App.Views.{cls}\"><Grid Margin=\"28,22\"><TextBlock Text=\"{cls} title\"/></Grid></UserControl>";

    private static ScanResult Scan(params (string Path, string Content)[] files)
    {
        var dir = TempProject(files);
        try
        {
            return new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, Fonts()).Scan(dir);
        }
        finally { Cleanup(dir); }
    }

    private static TextSite Site(ScanResult scan, string text) => Assert.Single(scan.Sites, s => s.Text == text);

    [Fact]
    public void A_page_drawn_by_a_typed_template_gets_the_width_of_its_content_control()
    {
        var scan = Scan(("MainWindow.xaml", Main(Pages, Host)), ("IntakeView.xaml", Page("IntakeView")), ("QcView.xaml", Page("QcView")));
        foreach (var page in new[] { "IntakeView title", "QcView title" })
        {
            var site = Site(scan, page);
            Assert.Equal(WidthStatus.Known, site.Width.Status);
            Assert.Equal(1240 - 220 - 56, site.Width.Px, 3);   // 視窗沒有邊框（WindowStyle=None）、第二欄、頁面 Margin 28 × 2
        }
    }

    [Fact]
    public void Text_written_directly_in_a_typed_template_gets_the_hosts_width_too()
    {
        var scan = Scan(("MainWindow.xaml", Main("<DataTemplate DataType=\"{x:Type vm:Home}\"><TextBlock Text=\"Home title\"/></DataTemplate>", Host)));
        Assert.Equal(WidthStatus.Known, Site(scan, "Home title").Width.Status);
        Assert.Equal(1240 - 220, Site(scan, "Home title").Width.Px, 3);
    }

    [Fact]
    public void Templates_in_the_hosts_own_resources_are_drawn_by_the_host()
    {
        // Kindling 的 MainWindow：範本寫在 <ContentControl.Resources> 裡，宿主就是它自己
        var host = "<ContentControl Grid.Column=\"1\" Content=\"{Binding Current}\"><ContentControl.Resources>" + Pages + "</ContentControl.Resources></ContentControl>";
        var scan = Scan(("MainWindow.xaml", Main("", host)), ("IntakeView.xaml", Page("IntakeView")), ("QcView.xaml", Page("QcView")));
        Assert.Equal(WidthStatus.Known, Site(scan, "IntakeView title").Width.Status);
        Assert.Equal(1240 - 220 - 56, Site(scan, "IntakeView title").Width.Px, 3);
    }

    [Fact]
    public void Two_places_that_could_draw_it_stay_in_template_and_say_how_many()
    {
        var scan = Scan(("MainWindow.xaml", Main(Pages, Host, "<ContentControl Grid.Column=\"1\" Content=\"{Binding Other}\"/>")),
            ("IntakeView.xaml", Page("IntakeView")), ("QcView.xaml", Page("QcView")));
        var site = Site(scan, "IntakeView title");
        Assert.Equal(WidthStatus.InTemplate, site.Width.Status);
        Assert.Contains("2 elements in <Window> could draw", site.Width.Reason);
    }

    [Fact]
    public void A_bound_list_without_an_item_template_could_draw_it_too()
    {
        var scan = Scan(("MainWindow.xaml", Main(Pages, Host, "<ListBox Grid.Column=\"1\" ItemsSource=\"{Binding History}\"/>")),
            ("IntakeView.xaml", Page("IntakeView")), ("QcView.xaml", Page("QcView")));
        Assert.Equal(WidthStatus.InTemplate, Site(scan, "IntakeView title").Width.Status);
    }

    [Fact]
    public void A_content_control_with_its_own_template_does_not_draw_it()
    {
        // 自己有 ContentTemplate 的不會用依型別的範本；剩下一個沒有範本的 → 就是它
        var scan = Scan(("MainWindow.xaml", Main(Pages, Host, "<ContentControl Grid.Column=\"1\" Content=\"{Binding Other}\" ContentTemplate=\"{StaticResource X}\"/>")),
            ("IntakeView.xaml", Page("IntakeView")), ("QcView.xaml", Page("QcView")));
        Assert.Equal(WidthStatus.Known, Site(scan, "IntakeView title").Width.Status);
    }

    [Fact]
    public void Nothing_that_could_draw_it_stays_in_template()
    {
        var scan = Scan(("MainWindow.xaml", Main(Pages, "<ContentControl Grid.Column=\"1\" Content=\"Fixed\"/>")),
            ("IntakeView.xaml", Page("IntakeView")), ("QcView.xaml", Page("QcView")));
        var site = Site(scan, "IntakeView title");
        Assert.Equal(WidthStatus.InTemplate, site.Width.Status);
        Assert.Contains("no element in <Window>", site.Width.Reason);
    }

    [Fact]
    public void A_typed_template_in_the_application_is_not_followed()
    {
        // App 層級的：整個 App 的任何 ContentControl 都可能用到它
        var scan = Scan(
            ("App.xaml", $"<Application {Ns} {Views}><Application.Resources><DataTemplate DataType=\"{{x:Type vm:Home}}\"><TextBlock Text=\"Home title\"/></DataTemplate></Application.Resources></Application>"),
            ("MainWindow.xaml", Main("", Host)));
        Assert.Equal(WidthStatus.InTemplate, Site(scan, "Home title").Width.Status);
    }

    [Fact]
    public void A_keyed_template_is_not_a_typed_template()
    {
        var scan = Scan(("MainWindow.xaml", Main("<DataTemplate x:Key=\"K\" DataType=\"{x:Type vm:Home}\"><TextBlock Text=\"Home title\"/></DataTemplate>", Host)));
        Assert.Equal(WidthStatus.InTemplate, Site(scan, "Home title").Width.Status);
    }
}
