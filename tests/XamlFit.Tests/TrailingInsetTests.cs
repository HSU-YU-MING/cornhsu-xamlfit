using System.Xml.Linq;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 水平 StackPanel 用無限寬量子元素：太長的按鈕保持原寬、被外層裁掉，
/// 按鈕右邊的 Padding、邊框、Margin 都在裁切線外 —— 不會擋到文字。
/// LayoutOracle「trailing:」實測；Playnite 保加利亞文的按鈕列因此誤報（工具少給 5～16 px）。
/// 只在檢查模式（Preceding）原諒；預算模式要讓整個按鈕留在畫面裡。
/// </summary>
public class TrailingInsetTests
{
    private static AvailableWidth Resolve(string body, RowSharing sharing, string resources = "")
    {
        var doc = XDocument.Parse($"<Window {Ns}><StackPanel Orientation=\"Horizontal\" Width=\"300\"><StackPanel.Resources>{resources}</StackPanel.Resources><Border Width=\"100\"/>{body}</StackPanel></Window>");
        doc.AddAnnotation(new SourceFile("Page.xaml"));
        var styles = new StyleIndex { SystemScrollBarWidth = Chrome.ScrollBarWidth };
        styles.Add(doc, "Page.xaml");
        var measurer = new ElementMeasurer(Fonts(), styles, Chrome, MonoFamily, MonoSize);
        return new WidthResolver(Chrome, styles, measurer) { Sharing = sharing }.Resolve(Probe(doc));
    }

    private const string Text = "<TextBlock x:Name=\"probe\" Text=\"x\"/>";

    [Fact]
    public void A_text_block_keeps_the_room_up_to_the_clip()
    {
        const string body = "<TextBlock x:Name=\"probe\" Margin=\"5,0,40,0\" Padding=\"0,0,7,0\" Text=\"x\"/>";
        Assert.Equal(300 - 100 - 5, Resolve(body, RowSharing.Preceding).Px, 3);
        Assert.Equal(300 - 100 - 5 - 40 - 7, Resolve(body, RowSharing.WholeRow).Px, 3);
    }

    [Fact]
    public void A_project_button_template_forgives_the_right_padding_and_border()
    {
        const string style = "<Style TargetType=\"Button\"><Setter Property=\"Padding\" Value=\"10,5,30,5\"/><Setter Property=\"BorderThickness\" Value=\"1\"/>" +
                             "<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\"><Border BorderThickness=\"{TemplateBinding BorderThickness}\">" +
                             "<ContentPresenter Margin=\"{TemplateBinding Padding}\"/></Border></ControlTemplate></Setter.Value></Setter></Style>";
        var body = $"<Button Margin=\"5,0,20,0\">{Text}</Button>";
        Assert.Equal(300 - 100 - 5 - 1 - 10, Resolve(body, RowSharing.Preceding, style).Px, 3);
        Assert.Equal(300 - 100 - 25 - 42, Resolve(body, RowSharing.WholeRow, style).Px, 3);
    }

    [Fact]
    public void A_template_with_a_second_child_forgives_only_the_outer_margin()
    {
        // 內容旁邊還有一個圖示欄：內容可能不是貼在右邊，範本那一段不原諒
        const string style = "<Style TargetType=\"Button\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\">" +
                             "<Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
                             "<ContentPresenter Margin=\"4,0,30,0\"/><Border Grid.Column=\"1\" Width=\"16\"/></Grid></ControlTemplate></Setter.Value></Setter></Style>";
        var body = $"<Button Margin=\"5,0,20,0\">{Text}</Button>";
        Assert.Equal(300 - 100 - 5 - 4 - 30 - 16, Resolve(body, RowSharing.Preceding, style).Px, 3);
    }

    [Fact]
    public void A_width_on_the_button_still_bounds_its_text()
    {
        const string style = "<Style TargetType=\"Button\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\">" +
                             "<Border Padding=\"10,0,30,0\"><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style>";
        Assert.Equal(150 - 40, Resolve($"<Button Width=\"150\">{Text}</Button>", RowSharing.Preceding, style).Px, 3);
    }

    [Fact]
    public void A_dock_panel_shrinks_the_button_so_nothing_is_forgiven()
    {
        var doc = XDocument.Parse($"<Window {Ns}><DockPanel Width=\"300\" LastChildFill=\"False\"><Border Width=\"100\"/><TextBlock x:Name=\"probe\" Margin=\"5,0,40,0\" Text=\"x\"/></DockPanel></Window>");
        doc.AddAnnotation(new SourceFile("Page.xaml"));
        var styles = new StyleIndex();
        styles.Add(doc, "Page.xaml");
        var measurer = new ElementMeasurer(Fonts(), styles, Chrome, MonoFamily, MonoSize);
        var w = new WidthResolver(Chrome, styles, measurer) { Sharing = RowSharing.Preceding }.Resolve(Probe(doc));
        Assert.Equal(300 - 100 - 45, w.Px, 3);
    }

    [Fact]
    public void Right_to_left_rows_forgive_nothing()
    {
        var doc = XDocument.Parse($"<Window {Ns} FlowDirection=\"RightToLeft\"><StackPanel Orientation=\"Horizontal\" Width=\"300\"><Border Width=\"100\"/><TextBlock x:Name=\"probe\" Margin=\"5,0,40,0\" Text=\"x\"/></StackPanel></Window>");
        doc.AddAnnotation(new SourceFile("Page.xaml"));
        var styles = new StyleIndex();
        styles.Add(doc, "Page.xaml");
        var measurer = new ElementMeasurer(Fonts(), styles, Chrome, MonoFamily, MonoSize);
        var w = new WidthResolver(Chrome, styles, measurer) { Sharing = RowSharing.Preceding }.Resolve(Probe(doc));
        Assert.Equal(300 - 100 - 45, w.Px, 3);
    }
}
