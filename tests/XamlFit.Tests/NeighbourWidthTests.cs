using XamlFit.Core.Layout;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 鄰居（Auto 欄、同一排）的寬度量得出來的幾種寫法。每一條都對過 LayoutOracle。
/// 等寬字型：每個字 5 px。
/// </summary>
public class NeighbourWidthTests
{
    private const string AutoStar =
        "<Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition/></Grid.ColumnDefinitions>";

    private static AvailableWidth InAutoGrid(string neighbour, string probe = "<TextBlock x:Name=\"probe\" Grid.Column=\"1\" Text=\"x\"/>") =>
        Resolve($"<Window {Ns}><Grid Width=\"300\">{AutoStar}{neighbour}{probe}</Grid></Window>");

    [Fact]
    public void A_canvas_without_a_width_takes_nothing() =>
        Assert.Equal(300 - 6, InAutoGrid("<Canvas Margin=\"3,0\"><Border Width=\"200\"/></Canvas>").Px, 3);

    [Fact]
    public void A_neighbour_with_bound_visibility_is_counted_as_visible() =>
        Assert.Equal(300 - 50, InAutoGrid("<TextBlock Text=\"0123456789\" Visibility=\"{Binding Shown}\"/>").Px, 3);

    [Fact]
    public void A_single_run_in_a_hyperlink_is_measured()
    {
        var w = InAutoGrid("<TextBlock Margin=\"0,0,6,0\">\n  <Hyperlink NavigateUri=\"https://example.com\">\n    <Run Text=\"manual\"/>\n  </Hyperlink>\n</TextBlock>");
        Assert.Equal(300 - 30 - 6, w.Px, 3);
    }

    [Theory]
    [InlineData("<TextBlock><Bold><Run Text=\"abc\"/></Bold></TextBlock>")]             // Bold 改字型
    [InlineData("<TextBlock>see <Hyperlink><Run Text=\"abc\"/></Hyperlink></TextBlock>")] // 旁邊還有文字
    public void Other_inline_content_stays_unknown(string neighbour) =>
        Assert.NotEqual(WidthStatus.Known, InAutoGrid(neighbour).Status);

    [Theory]
    [InlineData("<TextBlock><Run Text=\"ab\"/><Run Text=\"cd\"/></TextBlock>", 20)]              // 緊貼著寫：沒有空格
    [InlineData("<TextBlock>\n  <Run Text=\"ab\"/>\n  <Run Text=\"cd\"/>\n</TextBlock>", 25)]   // 中間的空白是一個空格；頭尾的修掉
    [InlineData("<CheckBox><TextBlock><Run Text=\"ab\"/> <Run Text=\"cd\"/></TextBlock></CheckBox>", -1)]
    public void Runs_on_one_line_are_added_up(string neighbour, double width)
    {
        var w = InAutoGrid(neighbour);
        Assert.Equal(WidthStatus.Known, w.Status);
        if (width >= 0) Assert.Equal(300 - width, w.Px, 3);
        else Assert.True(w.Px < 300 - 25);   // 勾選框：文字 25 + 勾選框的 chrome
    }

    [Theory]
    // 樣式設 10 個字、觸發器設 4 個字 → 取 50
    [InlineData("<Setter Property=\"Text\" Value=\"0123456789\"/><Style.Triggers><DataTrigger Binding=\"{Binding Open}\" Value=\"True\"><Setter Property=\"Text\" Value=\"Hide\"/></DataTrigger></Style.Triggers>", 50)]
    // 只有觸發器設：沒觸發時是空字串，觸發時 6 個字 → 取 30
    [InlineData("<Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"Text\" Value=\"Hover!\"/></Trigger></Style.Triggers>", 30)]
    // 綁定的值：不知道
    [InlineData("<Setter Property=\"Text\" Value=\"{Binding Label}\"/>", -1)]
    public void Text_set_by_its_own_style_takes_the_widest_value(string setters, double width)
    {
        var w = InAutoGrid($"<TextBlock><TextBlock.Style><Style TargetType=\"TextBlock\">{setters}</Style></TextBlock.Style></TextBlock>");
        if (width < 0) { Assert.NotEqual(WidthStatus.Known, w.Status); return; }
        Assert.Equal(300 - width, w.Px, 3);
    }

    [Fact]
    public void A_property_element_is_not_content()
    {
        // <CheckBox.Visibility> 不是勾選框的內容：照空白的勾選框量（跟沒有那個屬性元素時一樣）
        var plain = InAutoGrid("<CheckBox/>");
        var withProperty = InAutoGrid("<CheckBox><CheckBox.Visibility><Binding Path=\"Shown\"/></CheckBox.Visibility></CheckBox>");
        Assert.Equal(WidthStatus.Known, withProperty.Status);
        Assert.Equal(plain.Px, withProperty.Px, 3);
    }

    [Fact]
    public void A_child_spanning_into_a_star_column_does_not_widen_the_auto_column()
    {
        var w = Resolve($"<Window {Ns}><Grid Width=\"300\">{AutoStar}<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition/></Grid.RowDefinitions>" +
                        "<TextBlock Text=\"Label\"/><Border Grid.Row=\"1\" Grid.ColumnSpan=\"2\" Width=\"200\"/>" +
                        "<TextBlock x:Name=\"probe\" Grid.Row=\"2\" Grid.Column=\"1\" Text=\"x\"/></Grid></Window>");
        Assert.Equal(300 - 25, w.Px, 3);
    }

    [Fact]
    public void A_child_spanning_only_auto_columns_stays_unknown()
    {
        var w = Resolve($"<Window {Ns}><Grid Width=\"300\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"Auto\"/><ColumnDefinition/></Grid.ColumnDefinitions>" +
                        "<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition/></Grid.RowDefinitions>" +
                        "<Border Grid.ColumnSpan=\"2\" Width=\"150\"/><TextBlock x:Name=\"probe\" Grid.Row=\"1\" Grid.Column=\"2\" Text=\"x\"/></Grid></Window>");
        Assert.NotEqual(WidthStatus.Known, w.Status);
    }

    [Fact]
    public void Text_spanning_an_auto_and_a_star_column_grows_until_the_grid_is_full()
    {
        // 第三欄固定 60：跨前兩欄的文字最多 300 − 60
        var w = Resolve($"<Window {Ns}><Grid Width=\"300\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition/><ColumnDefinition Width=\"60\"/></Grid.ColumnDefinitions>" +
                        "<TextBlock Text=\"Label\"/><TextBlock x:Name=\"probe\" Grid.Row=\"1\" Grid.ColumnSpan=\"2\" Text=\"x\"/></Grid></Window>");
        Assert.Equal(240, w.Px, 3);
        Assert.True(w.WrapDefeated);
    }
}
