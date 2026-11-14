using XamlFit.Core.Layout;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// DataGrid 的星號欄（QuillNest 的工作量模板）。LayoutOracle「star column」實測（WPF 預設範本）：
/// 欄可用的寬 = DataGrid 外框 − BorderThickness（預設 1+1）− 垂直捲軸（沒關掉就假設在）；
/// 扣掉其他欄後依權重分，MinWidth 夾住；標題再扣標題內距 10。
/// </summary>
public class DataGridStarColumnTests
{
    private static AvailableWidth Header(string columns, string grid = "HeadersVisibility=\"Column\" ScrollViewer.VerticalScrollBarVisibility=\"Disabled\"") =>
        Resolve($"<Window {Ns}><DataGrid Width=\"500\" AutoGenerateColumns=\"False\" {grid}><DataGrid.Columns>{columns}</DataGrid.Columns></DataGrid></Window>");

    private const string Star = "<DataGridTextColumn x:Name=\"probe\" Width=\"*\" MinWidth=\"80\" Header=\"x\"/>";

    [Fact]
    public void A_lone_star_column_takes_the_grid_minus_its_border() =>
        Assert.Equal(500 - 2 - 10, Header(Star).Px, 3);

    [Fact]
    public void Fixed_columns_come_off_first() =>
        Assert.Equal(500 - 2 - 175 - 10, Header(Star + "<DataGridTextColumn Width=\"100\" Header=\"A\"/><DataGridCheckBoxColumn Width=\"75\" Header=\"B\"/>").Px, 3);

    [Fact]
    public void Star_columns_share_by_weight() =>
        Assert.Equal((500 - 2 - 130) / 2.0 - 10, Header(Star + "<DataGridTextColumn Width=\"*\" Header=\"O\"/><DataGridTextColumn Width=\"130\" Header=\"C\"/>").Px, 3);

    [Fact]
    public void MinWidth_wins_when_the_rest_is_too_narrow() =>
        Assert.Equal(80 - 10, Header(Star + "<DataGridTextColumn Width=\"450\" Header=\"A\"/>").Px, 3);

    [Fact]
    public void The_scroll_bar_is_assumed_unless_turned_off()
    {
        var w = Header(Star, "HeadersVisibility=\"Column\"");
        Assert.Equal(500 - 2 - Chrome.ScrollBarWidth - 10, w.Px, 3);
        Assert.True(w.AssumedScrollBar);
    }

    [Theory]
    [InlineData("<DataGridTextColumn Width=\"Auto\" MaxWidth=\"120\" Header=\"T\"/>")]   // 依儲存格內容長：執行期才知道
    [InlineData("<DataGridTextColumn Header=\"T\"/>")]                                   // 沒寫 Width = SizeToHeader…也是依內容
    public void A_column_sized_by_its_content_leaves_the_star_column_unknown(string other) =>
        Assert.NotEqual(WidthStatus.Known, Header(Star + other).Status);

    [Fact]
    public void Row_headers_leave_it_unknown() =>
        Assert.NotEqual(WidthStatus.Known, Header(Star, "ScrollViewer.VerticalScrollBarVisibility=\"Disabled\"").Status);
}
