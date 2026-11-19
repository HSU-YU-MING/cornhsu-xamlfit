using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 0.7.0：任何元素的 <c>ToolTip="…"</c> 是一個文字位置（之前工具根本不知道它們存在）。
///
/// WPF 實測（PowerShell + PresentationFramework，視窗 FontFamily="Courier New" FontSize="30"）：
/// Button 的 ToolTip、ContextMenu、裡面的 MenuItem 都是系統字型 12 —— 不從擁有者繼承字型。
/// 寬度：快顯不裁切，沒宣告 popupMaxWidth 時查不到上界；宣告了就是 popupMaxWidth − ToolTip 的內距 14（LayoutOracle --popups）。
/// </summary>
public class ToolTipSiteTests
{
    private static List<TextSite> Scan(string body, double? popupMaxWidth = null, string resources = "")
    {
        var dir = TempProject(("Main.xaml",
            $"<Window {Ns} xmlns:p=\"clr-namespace:App.Properties\" FontFamily=\"Wide\" FontSize=\"30\">{resources}<StackPanel>{body}</StackPanel></Window>"));
        try
        {
            var options = new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, PopupMaxWidth = popupMaxWidth };
            return [.. new XamlScanner(Chrome, options, Fonts(Mono(), Mono("Wide"))).Scan(dir).Sites];
        }
        finally { Cleanup(dir); }
    }

    private static TextSite Tip(List<TextSite> sites) => Assert.Single(sites, s => s.Element == "ToolTip");

    [Fact]
    public void A_tooltip_attribute_is_a_site_with_no_declared_limit_by_default()
    {
        var tip = Tip(Scan("<Button Content=\"Go\" ToolTip=\"Save the file\"/>"));
        Assert.Equal("Save the file", tip.Text);
        Assert.Equal(TextSource.Literal, tip.Source);
        Assert.Equal(WidthStatus.NoDeclaredLimit, tip.Width.Status);
    }

    [Fact]
    public void A_resource_tooltip_keeps_its_key()
    {
        var tip = Tip(Scan("<Button Content=\"Go\" ToolTip=\"{x:Static p:Strings.SaveHint}\"/>"));
        Assert.Equal(TextSource.Resource, tip.Source);
        Assert.Equal("SaveHint", tip.ResourceKey);
    }

    [Fact]
    public void A_declared_popup_maximum_bounds_the_tooltip_minus_its_chrome()
    {
        var tip = Tip(Scan("<Button Content=\"Go\" ToolTip=\"Save the file\"/>", popupMaxWidth: 300));
        Assert.Equal(WidthStatus.Known, tip.Width.Status);
        Assert.Equal(300 - 14, tip.Width.Px, 3);
    }

    [Fact]
    public void A_tooltip_does_not_inherit_the_owners_font()
    {
        // 視窗設了 Wide 30：ToolTip 是預設字型、預設大小
        var tip = Tip(Scan("<Button Content=\"Go\" ToolTip=\"Save the file\"/>", popupMaxWidth: 300));
        Assert.Equal(MonoFamily, tip.FontFamily);
        Assert.Equal(MonoSize, tip.FontSize);
    }

    [Fact]
    public void The_projects_implicit_tooltip_style_sets_its_font()
    {
        var tip = Tip(Scan("<Button Content=\"Go\" ToolTip=\"Save the file\"/>", popupMaxWidth: 300,
            resources: "<Window.Resources><Style TargetType=\"ToolTip\"><Setter Property=\"FontSize\" Value=\"14\"/></Style></Window.Resources>"));
        Assert.Equal(14, tip.FontSize);
        Assert.Equal(WidthStatus.Known, tip.Width.Status);
    }

    [Fact]
    public void A_restyled_tooltip_padding_is_not_guessed()
    {
        var tip = Tip(Scan("<Button Content=\"Go\" ToolTip=\"Save the file\"/>", popupMaxWidth: 300,
            resources: "<Window.Resources><Style TargetType=\"ToolTip\"><Setter Property=\"Padding\" Value=\"20\"/></Style></Window.Resources>"));
        Assert.Equal(WidthStatus.Unmeasurable, tip.Width.Status);
    }

    [Fact]
    public void Plain_text_in_a_tooltip_property_element_is_a_site()
    {
        var tip = Tip(Scan("<Button Content=\"Go\"><Button.ToolTip>Save the file</Button.ToolTip></Button>"));
        Assert.Equal("Save the file", tip.Text);
    }

    [Fact]
    public void Text_inside_a_tooltip_property_element_uses_the_system_font_not_the_owners()
    {
        // 之前 <Button.ToolTip><TextBlock/></Button.ToolTip> 的 TextBlock 繼承了視窗的字型
        var sites = Scan("<Button Content=\"Go\"><Button.ToolTip><TextBlock Text=\"Save the file\"/></Button.ToolTip></Button>", popupMaxWidth: 300);
        var text = Assert.Single(sites, s => s.Element == "TextBlock");
        Assert.Equal(MonoFamily, text.FontFamily);
        Assert.Equal(MonoSize, text.FontSize);
        Assert.DoesNotContain(sites, s => s.Element == "ToolTip");
    }

    [Fact]
    public void Context_menu_items_use_the_system_font_not_the_owners()
    {
        var sites = Scan("<Button Content=\"Go\"><Button.ContextMenu><ContextMenu><MenuItem Header=\"Open\"/></ContextMenu></Button.ContextMenu></Button>");
        var item = Assert.Single(sites, s => s.Element == "MenuItem");
        Assert.Equal(MonoFamily, item.FontFamily);
        Assert.Equal(MonoSize, item.FontSize);
    }

    [Fact]
    public void The_owner_keeps_its_own_font()
    {
        var button = Assert.Single(Scan("<Button Content=\"Go\" ToolTip=\"Save the file\"/>"), s => s.Element == "Button");
        Assert.Equal("Wide", button.FontFamily);
        Assert.Equal(30, button.FontSize);
    }

    [Fact]
    public void A_tooltip_element_is_not_its_own_tooltip_site()
    {
        Assert.DoesNotContain(Scan("<Button Content=\"Go\"><Button.ToolTip><ToolTip Content=\"x\"/></Button.ToolTip></Button>"), s => s.Element == "ToolTip");
    }
}
