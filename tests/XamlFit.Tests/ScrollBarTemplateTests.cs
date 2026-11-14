using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 專案換掉 ScrollBar 範本、ScrollViewer 範本把捲軸放在 Auto 欄（Playnite 的寫法）。
/// LayoutOracle --scrollbar 實測：只換範本時 WPF 預設樣式仍把 Width／MinWidth 設成系統捲軸寬；
/// OverridesDefaultStyle="True" 才由範本根元素決定。
/// </summary>
public class ScrollBarTemplateTests
{
    private const string Viewer =
        "<Style TargetType=\"ScrollViewer\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"ScrollViewer\">" +
        "<Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
        "<ScrollContentPresenter x:Name=\"PART_ScrollContentPresenter\" Content=\"{TemplateBinding Content}\"/>" +
        "<ScrollBar x:Name=\"PART_VerticalScrollBar\" Grid.Column=\"1\" Orientation=\"Vertical\" Visibility=\"{TemplateBinding ComputedVerticalScrollBarVisibility}\"/>" +
        "</Grid></ControlTemplate></Setter.Value></Setter></Style>";

    private static string Bar(string root, bool overrides) =>
        "<Style TargetType=\"ScrollBar\">" + (overrides ? "<Setter Property=\"OverridesDefaultStyle\" Value=\"True\"/>" : "") +
        $"<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"ScrollBar\">{root}</ControlTemplate></Setter.Value></Setter></Style>";

    private static AvailableWidth Probe(string bar)
    {
        // 樣式放在 Grid 自己的資源裡：範本被複製出來算內距時，也要找得到它（LexicalContinuation）
        var xaml = $"<Window {Ns} Width=\"700\"><Grid Width=\"300\" HorizontalAlignment=\"Left\"><Grid.Resources>{bar}{Viewer}</Grid.Resources>" +
                   "<ScrollViewer VerticalScrollBarVisibility=\"Visible\" HorizontalScrollBarVisibility=\"Disabled\"><TextBlock Text=\"Hello\"/></ScrollViewer></Grid></Window>";
        var dir = TempProject(("Main.xaml", xaml));
        try
        {
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, Fonts()).Scan(dir);
            return scan.Sites.Single().Width;
        }
        finally { Cleanup(dir); }
    }

    [Theory]
    [InlineData("<Track x:Name=\"PART_Track\" Width=\"17\"/>")]      // Playnite
    [InlineData("<Border Width=\"24\" Margin=\"2,0\"/>")]
    [InlineData("<Border><Track x:Name=\"PART_Track\"/></Border>")]
    public void A_template_only_scroll_bar_keeps_the_system_width(string root)
    {
        var w = Probe(Bar(root, overrides: false));
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - Chrome.ScrollBarWidth, w.Px, 3);
    }

    [Fact]
    public void With_OverridesDefaultStyle_the_template_root_decides()
    {
        // ⚠ 第一版：複本裡找不到 Grid 自己資源裡的捲軸樣式，當成系統寬度 —— 多算 6.5 px
        var w = Probe(Bar("<Border Width=\"24\"/>", overrides: true));
        Assert.Equal(276, w.Px, 3);
    }

    [Fact]
    public void With_OverridesDefaultStyle_and_no_fixed_root_width_the_bar_is_unknown() =>
        Assert.NotEqual(WidthStatus.Known, Probe(Bar("<Border><Track x:Name=\"PART_Track\"/></Border>", overrides: true)).Status);
}
