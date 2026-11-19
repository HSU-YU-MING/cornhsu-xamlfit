using XamlFit.Core.Layout;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 0.8.0：ExpandDirection 左右。WPF 的預設範本把標題放在旁邊的一欄、不轉向：
/// 那一欄 = max(標題寬, 箭頭 19) + 2 + 左右 Padding，內容區照上方的排法再扣一次 Padding（LayoutOracle「Expander Right／Left content…」）。
/// Mono 字級 10：每個字 5 px。
/// </summary>
public class ExpanderSideTests
{
    private static AvailableWidth Content(string attributes, string header = "Header=\"Header text\"") =>
        In($"<Expander Width=\"300\" HorizontalAlignment=\"Left\" IsExpanded=\"True\" {attributes} {header}><Border x:Name=\"probe\"/></Expander>");

    [Theory]
    [InlineData("Right")]
    [InlineData("Left")]
    public void The_content_gets_what_the_header_column_leaves(string direction)
    {
        // 300 − 邊框 2 − (標題 11 字 × 5 = 55, + 2)
        var w = Content($"ExpandDirection=\"{direction}\"");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - 2 - 57, w.Px, 3);
    }

    [Fact]
    public void A_short_header_column_is_as_wide_as_the_arrow()
    {
        Assert.Equal(300 - 2 - 21, Content("ExpandDirection=\"Right\"", "Header=\"H\"").Px, 3);
    }

    [Fact]
    public void Padding_counts_on_both_the_header_and_the_content()
    {
        // 300 − 邊框 8 − 內容的 Padding 10 − (55 + 2 + 標題的 Padding 10)
        Assert.Equal(300 - 8 - 10 - 67, Content("ExpandDirection=\"Right\" BorderThickness=\"4\" Padding=\"5\"").Px, 3);
    }

    [Fact]
    public void An_element_header_counts_its_margin()
    {
        var w = Content("ExpandDirection=\"Right\"", "");
        var withElement = In("<Expander Width=\"300\" HorizontalAlignment=\"Left\" IsExpanded=\"True\" ExpandDirection=\"Right\"><Expander.Header><TextBlock Text=\"Header text\" Margin=\"3,0\"/></Expander.Header><Border x:Name=\"probe\"/></Expander>");
        Assert.Equal(300 - 2 - 63, withElement.Px, 3);
        Assert.Equal(300 - 2 - 21, w.Px, 3);   // 沒有標題：那一欄只剩箭頭
    }

    [Fact]
    public void A_templated_or_bound_header_is_not_guessed()
    {
        Assert.Equal(WidthStatus.Unmeasurable, Content("ExpandDirection=\"Right\"", "Header=\"{Binding Name}\"").Status);
        Assert.Equal(WidthStatus.Unmeasurable,
            Content("ExpandDirection=\"Right\"", "Header=\"H\" HeaderTemplate=\"{StaticResource T}\"").Status);
    }

    [Fact]
    public void A_custom_template_with_a_side_header_is_not_guessed()
    {
        var w = In("<Expander Width=\"300\" HorizontalAlignment=\"Left\" IsExpanded=\"True\" ExpandDirection=\"Right\" Header=\"H\"><Expander.Template><ControlTemplate TargetType=\"Expander\"><DockPanel><ToggleButton DockPanel.Dock=\"Left\" Content=\"{TemplateBinding Header}\"/><ContentPresenter/></DockPanel></ControlTemplate></Expander.Template><Border x:Name=\"probe\"/></Expander>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
    }
}
