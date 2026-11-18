using XamlFit.Core.Layout;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 回報 2 §3-3（ZeroCel：MenuItem 150、ContextMenu 36、Popup 9 處）：快顯跟著內容變寬、不裁切，所以預設不檢查；
/// 使用者宣告 <c>popupMaxWidth</c> 之後，快顯裡的文字拿它當上界，扣掉 LayoutOracle --popups 在真的視窗裡量到的快顯內距
/// （ContextMenu 13、ToolTip 14、子選單的 Popup 6、裸 Popup 0；MenuItem 子項 85）。
/// </summary>
public class PopupWidthTests
{
    private const string ContextMenuItem = "<ContextMenu><MenuItem x:Name=\"probe\" Header=\"Save\"/></ContextMenu>";

    [Fact]
    public void Popups_are_not_checked_unless_a_maximum_is_declared()
    {
        Assert.Equal(WidthStatus.NoDeclaredLimit, In(ContextMenuItem).Status);
        Assert.Equal(WidthStatus.NoDeclaredLimit, In("<Popup><TextBlock x:Name=\"probe\"/></Popup>").Status);
        Assert.Equal(WidthStatus.NoDeclaredLimit, In("<Button Content=\"x\"><Button.ToolTip><TextBlock x:Name=\"probe\"/></Button.ToolTip></Button>").Status);
    }

    [Fact]
    public void A_context_menu_item_is_held_to_the_declared_width_minus_menu_and_item_chrome()
    {
        var w = In(ContextMenuItem, popupMaxWidth: 300);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - 13 - 85, w.Px, 3);
        Assert.Contains("popupMaxWidth", w.BoundSource);
    }

    [Fact]
    public void A_submenu_item_is_held_to_the_declared_width_minus_the_submenu_popup_chrome()
    {
        var w = In("<Menu><MenuItem Header=\"File\"><MenuItem x:Name=\"probe\" Header=\"Save\"/></MenuItem></Menu>", popupMaxWidth: 300);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - 6 - 85, w.Px, 3);
    }

    [Fact]
    public void A_menu_bar_item_is_still_unbounded()
    {
        // 選單列的項目放不下會換列，不會被裁
        var w = In("<Menu><MenuItem x:Name=\"probe\" Header=\"File\"/></Menu>", popupMaxWidth: 300);
        Assert.Equal(WidthStatus.NoDeclaredLimit, w.Status);
    }

    [Fact]
    public void A_tool_tip_element_and_an_implicit_tool_tip_get_the_same_chrome()
    {
        var explicitTip = In("<Button Content=\"x\"><Button.ToolTip><ToolTip><TextBlock x:Name=\"probe\"/></ToolTip></Button.ToolTip></Button>", popupMaxWidth: 300);
        var implicitTip = In("<Button Content=\"x\"><Button.ToolTip><TextBlock x:Name=\"probe\"/></Button.ToolTip></Button>", popupMaxWidth: 300);
        Assert.Equal(300 - 14, explicitTip.Px, 3);
        Assert.Equal(300 - 14, implicitTip.Px, 3);
    }

    [Fact]
    public void A_bare_popup_has_no_chrome_of_its_own()
    {
        var w = In("<Popup><Border Padding=\"5,0\"><TextBlock x:Name=\"probe\"/></Border></Popup>", popupMaxWidth: 300);
        Assert.Equal(290, w.Px, 3);
    }

    [Fact]
    public void A_width_inside_the_popup_still_wins_when_narrower()
    {
        var w = In("<Popup><Border Width=\"120\"><TextBlock x:Name=\"probe\"/></Border></Popup>", popupMaxWidth: 300);
        Assert.Equal(120, w.Px, 3);
    }

    [Fact]
    public void The_header_of_a_menu_item_is_in_the_item_not_in_a_submenu()
    {
        // <MenuItem.Header> 裡的文字畫在那個項目上；它所屬的 ContextMenu 才是快顯
        var w = In("<ContextMenu><MenuItem><MenuItem.Header><TextBlock x:Name=\"probe\"/></MenuItem.Header></MenuItem></ContextMenu>", popupMaxWidth: 300);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - 13 - 85, w.Px, 3);   // 跟 Header="Save" 寫在屬性上一樣：MenuItem 的內容就是 Header
    }
}
