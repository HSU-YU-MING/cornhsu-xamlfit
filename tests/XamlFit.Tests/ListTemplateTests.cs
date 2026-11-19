using XamlFit.Core.Layout;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 0.7.0：ListBox／ListView／ItemsControl 的自訂範本不再一律放棄。內距 = 外框到範本裡 ItemsPresenter 之間的東西；
/// ListBox／ListView 的捲軸照清單自己的 ScrollViewer.* 設定算（跟預設範本同一套）。
/// 數字跟 LayoutOracle「ListBox template…」「ItemsControl template…」「ListView template…」的 WPF 實量一樣。
/// </summary>
public class ListTemplateTests
{
    private const string Template =
        "<ControlTemplate TargetType=\"ListBox\"><Border BorderThickness=\"2\" Padding=\"5\"><ScrollViewer Focusable=\"False\"><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>";

    private const string NoBars = "ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\" ScrollViewer.VerticalScrollBarVisibility=\"Disabled\"";

    private static AvailableWidth ListBox(string attributes, string template) =>
        In($"<ListBox Width=\"200\" HorizontalAlignment=\"Left\" {attributes}><ListBox.Template>{template}</ListBox.Template><ListBoxItem><TextBlock x:Name=\"probe\"/></ListBoxItem></ListBox>");

    [Fact]
    public void A_list_template_gives_its_padding_and_border()
    {
        // 200 − 邊框 2×2 − Padding 2×5 − ListBoxItem 10 = 176（WPF 176）
        var w = ListBox(NoBars, Template);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(176, w.Px, 3);
    }

    [Fact]
    public void A_template_binding_padding_takes_the_lists_padding()
    {
        var w = ListBox($"Padding=\"8\" {NoBars}",
            "<ControlTemplate TargetType=\"ListBox\"><Border Padding=\"{TemplateBinding Padding}\"><ScrollViewer Focusable=\"False\"><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>");
        Assert.Equal(174, w.Px, 3);
    }

    [Fact]
    public void The_lists_own_scroll_settings_still_apply()
    {
        // 沒關掉垂直捲軸：假設它在（WPF 沒畫出來時是 176，這裡 158.5 偏保守）；水平沒關：會捲動，沒有上界
        Assert.Equal(176 - 17.5, ListBox("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", Template).Px, 3);
        Assert.Equal(WidthStatus.NoDeclaredLimit, ListBox("", Template).Status);
    }

    [Fact]
    public void A_template_scroll_viewer_with_its_own_bars_is_not_guessed()
    {
        var w = ListBox(NoBars,
            "<ControlTemplate TargetType=\"ListBox\"><Border Padding=\"5\"><ScrollViewer HorizontalScrollBarVisibility=\"Auto\"><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
    }

    [Fact]
    public void A_template_that_lays_out_items_itself_is_not_guessed()
    {
        var w = ListBox(NoBars, "<ControlTemplate TargetType=\"ListBox\"><Border Padding=\"5\"><StackPanel IsItemsHost=\"True\"/></Border></ControlTemplate>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
    }

    [Fact]
    public void An_items_control_template_gives_its_padding()
    {
        var w = In("<ItemsControl Width=\"200\" HorizontalAlignment=\"Left\"><ItemsControl.Template><ControlTemplate TargetType=\"ItemsControl\"><Border Padding=\"10,0,20,0\"><ItemsPresenter/></Border></ControlTemplate></ItemsControl.Template><TextBlock x:Name=\"probe\"/></ItemsControl>");
        Assert.Equal(170, w.Px, 3);
    }

    [Fact]
    public void An_items_control_template_scroll_viewer_needs_literal_bars()
    {
        var literal = In("<ItemsControl Width=\"200\" HorizontalAlignment=\"Left\"><ItemsControl.Template><ControlTemplate TargetType=\"ItemsControl\"><ScrollViewer HorizontalScrollBarVisibility=\"Disabled\" VerticalScrollBarVisibility=\"Auto\"><ItemsPresenter/></ScrollViewer></ControlTemplate></ItemsControl.Template><TextBlock x:Name=\"probe\"/></ItemsControl>");
        Assert.Equal(200 - 17.5, literal.Px, 3);
        var inherited = In("<ItemsControl Width=\"200\" HorizontalAlignment=\"Left\"><ItemsControl.Template><ControlTemplate TargetType=\"ItemsControl\"><ScrollViewer><ItemsPresenter/></ScrollViewer></ControlTemplate></ItemsControl.Template><TextBlock x:Name=\"probe\"/></ItemsControl>");
        Assert.Equal(WidthStatus.Unmeasurable, inherited.Status);
    }

    [Fact]
    public void A_tree_view_template_is_still_not_guessed()
    {
        var w = In("<TreeView Width=\"200\" HorizontalAlignment=\"Left\" " + NoBars + "><TreeView.Template><ControlTemplate TargetType=\"TreeView\"><Border Padding=\"5\"><ItemsPresenter/></Border></ControlTemplate></TreeView.Template><TreeViewItem><TreeViewItem.Header><TextBlock x:Name=\"probe\"/></TreeViewItem.Header></TreeViewItem></TreeView>");
        Assert.NotEqual(WidthStatus.Known, w.Status);
    }
}
