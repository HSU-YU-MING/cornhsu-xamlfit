using System.Xml.Linq;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;

namespace XamlFit.Tests;

public class ItemsControlTests
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static readonly ChromeTable Chrome = ChromeTable.Builtin();

    private static AvailableWidth Resolve(string body)
    {
        var doc = XDocument.Parse($"<Root {Ns}>{body}</Root>", LoadOptions.SetLineInfo);
        var styles = new StyleIndex();
        styles.Add(doc);
        var probe = doc.Descendants().First(e => e.Attribute(XName.Get("Name",
            "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "probe");
        return new WidthResolver(Chrome, styles).Resolve(probe);
    }

    [Fact]
    public void The_scroll_bar_width_is_in_the_table()
    {
        Assert.True(Chrome.ScrollBarWidth > 10, "a vertical scroll bar should be about 17 DIP");
    }

    [Fact]
    public void An_item_in_a_fixed_width_ListBox_resolves()
    {
        // 300 − ListBox chrome 4 − 捲軸 16.8 − ListBoxItem chrome 10 = 269.2
        var w = Resolve("""
            <ListBox Width="300">
              <ListBoxItem x:Name="probe" Content="Option"/>
            </ListBox>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - 4 - Chrome.ScrollBarWidth - 10, w.Px, 1);
    }

    [Fact]
    public void The_scroll_bar_assumption_is_stated_in_the_reason()
    {
        // 捲軸出不出現是執行期才知道的；假設它在是保守的方向，但必須講出來
        var w = Resolve("""
            <ListBox Width="300"><ListBoxItem x:Name="probe" Content="x"/></ListBox>
            """);
        Assert.Contains("assuming a visible vertical scroll bar", w.Reason);
    }

    [Fact]
    public void An_explicitly_disabled_scroll_bar_is_not_deducted()
    {
        var w = Resolve("""
            <ListBox Width="300" ScrollViewer.VerticalScrollBarVisibility="Disabled">
              <ListBoxItem x:Name="probe" Content="x"/>
            </ListBox>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - 4 - 10, w.Px, 1);
        Assert.DoesNotContain("scroll bar", w.Reason);
    }

    [Fact]
    public void A_DataGrid_stays_blocked_because_cells_are_sized_by_columns()
    {
        // 儲存格寬度來自 DataGridColumn 的尺寸系統，跟 DataGrid 的寬度不是同一回事
        var w = Resolve("""
            <DataGrid Width="400">
              <TextBlock x:Name="probe" Text="x"/>
            </DataGrid>
            """);
        Assert.Equal(WidthStatus.NeedsItems, w.Status);
        Assert.Contains("separate sizing system", w.Reason);
    }

    [Fact]
    public void A_TreeView_stays_blocked_because_of_indentation()
    {
        // 項目依巢狀深度往右縮排，而深度對綁資料的項目是執行期才知道的
        var w = Resolve("""
            <TreeView Width="400">
              <TreeViewItem x:Name="probe" Header="x"/>
            </TreeView>
            """);
        Assert.Equal(WidthStatus.NeedsItems, w.Status);
    }

    [Fact]
    public void A_text_block_inside_a_list_item_also_resolves()
    {
        var w = Resolve("""
            <ListBox Width="300">
              <ListBoxItem>
                <TextBlock x:Name="probe" Text="Option"/>
              </ListBoxItem>
            </ListBox>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - 4 - Chrome.ScrollBarWidth - 10, w.Px, 1);
    }
}
