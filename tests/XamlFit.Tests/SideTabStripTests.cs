using System.Xml.Linq;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 標籤列放在左右兩側（Playnite 的 DatabaseFieldsManagerWindow）。
/// 範本靠 TabStripPlacement 觸發器把標籤列移到 Auto 欄，寬度 = 最寬的「標題 + 標題 chrome」。
/// LayoutOracle「side tabs」實測：Playnite 的範本 7 案全部一致。
/// 等寬字型：每個字 5 px；Playnite 的 TabItem 範本標題 chrome 20（TextBlock Padding 5 + ContentPresenter Margin 5）；
/// 標籤列外框右邊 1。
/// </summary>
public class SideTabStripTests
{
    private const string TabControlStyle =
        "<Style TargetType=\"TabControl\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"TabControl\">" +
        "<Grid><Grid.ColumnDefinitions><ColumnDefinition x:Name=\"C0\"/><ColumnDefinition x:Name=\"C1\" Width=\"0\"/></Grid.ColumnDefinitions>" +
        "<Grid.RowDefinitions><RowDefinition Height=\"Auto\" x:Name=\"R0\"/><RowDefinition Height=\"*\" x:Name=\"R1\"/></Grid.RowDefinitions>" +
        "<Border x:Name=\"HeaderBorder\" BorderThickness=\"0,0,0,1\"><TabPanel x:Name=\"HeaderPanel\" IsItemsHost=\"True\">" +
        "<TabPanel.LayoutTransform><TransformGroup><ScaleTransform ScaleX=\"1\" ScaleY=\"1\"/><RotateTransform ROTATE/><TranslateTransform X=\"0\"/></TransformGroup></TabPanel.LayoutTransform>" +
        "</TabPanel></Border>" +
        "<Border x:Name=\"ContentPanel\" Grid.Row=\"1\"><ContentPresenter ContentSource=\"SelectedContent\"/></Border></Grid>" +
        "<ControlTemplate.Triggers>" +
        "TRIGGER" +
        "<Trigger Property=\"TabStripPlacement\" Value=\"Left\"><Setter Property=\"Grid.Row\" TargetName=\"ContentPanel\" Value=\"0\"/><Setter Property=\"Grid.Column\" TargetName=\"ContentPanel\" Value=\"1\"/>" +
        "<Setter Property=\"Width\" TargetName=\"C0\" Value=\"Auto\"/><Setter Property=\"Width\" TargetName=\"C1\" Value=\"*\"/><Setter Property=\"Height\" TargetName=\"R0\" Value=\"*\"/><Setter Property=\"Height\" TargetName=\"R1\" Value=\"0\"/>" +
        "<Setter Property=\"BorderThickness\" TargetName=\"HeaderBorder\" Value=\"0,0,1,0\"/></Trigger>" +
        "<Trigger Property=\"TabStripPlacement\" Value=\"Right\"><Setter Property=\"Grid.Row\" TargetName=\"ContentPanel\" Value=\"0\"/><Setter Property=\"Grid.Column\" TargetName=\"HeaderBorder\" Value=\"1\"/>" +
        "<Setter Property=\"Width\" TargetName=\"C0\" Value=\"*\"/><Setter Property=\"Width\" TargetName=\"C1\" Value=\"Auto\"/><Setter Property=\"Height\" TargetName=\"R0\" Value=\"*\"/><Setter Property=\"Height\" TargetName=\"R1\" Value=\"0\"/>" +
        "<Setter Property=\"BorderThickness\" TargetName=\"HeaderBorder\" Value=\"1,0,0,0\"/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";

    private const string TabItemStyle =
        "<Style TargetType=\"TabItem\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"TabItem\">" +
        "<Grid><Border x:Name=\"Selected\" Visibility=\"Collapsed\" Margin=\"1,0\"/>" +
        "<TextBlock TEXTBLOCK Padding=\"5\"><ContentPresenter ContentSource=\"Header\" Margin=\"5\"/></TextBlock></Grid>" +
        "ITEMEXTRA</ControlTemplate></Setter.Value></Setter></Style>";

    private static string Xaml(
        string placement, string items, string trigger = "", string rotate = "Angle=\"0\"",
        string textBlock = "", string itemExtra = "", string resources = "") =>
        $"<Window {Ns}><Grid Width=\"400\" HorizontalAlignment=\"Left\"><Grid.Resources>{resources}" +
        TabControlStyle.Replace("TRIGGER", trigger).Replace("ROTATE", rotate) +
        TabItemStyle.Replace("TEXTBLOCK", textBlock).Replace("ITEMEXTRA", itemExtra) +
        $"</Grid.Resources><TabControl TabStripPlacement=\"{placement}\">{items}</TabControl></Grid></Window>";

    private const string ProbeTab = "<TabItem Header=\"Platforms\"><Border x:Name=\"probe\"/></TabItem>";
    private const string Items = ProbeTab + "<TabItem Header=\"Age ratings\"/>";   // 55 + 20 + 1 = 76

    [Theory]
    [InlineData("Left")]
    [InlineData("Right")]
    public void The_strip_takes_the_widest_header_plus_its_chrome(string placement)
    {
        var w = Resolve(Xaml(placement, Items));
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(400 - 76, w.Px, 3);
    }

    [Fact]
    public void A_top_strip_does_not_take_width() =>
        Assert.Equal(400, Resolve(Xaml("Top", Items)).Px, 3);

    [Fact]
    public void Collapsed_tabs_do_not_widen_the_strip() =>
        Assert.Equal(400 - 66, Resolve(Xaml("Left", ProbeTab + "<TabItem Header=\"A much longer header\" Visibility=\"Collapsed\"/>")).Px, 3);

    [Fact]
    public void A_tab_MinWidth_counts() =>
        Assert.Equal(400 - 151, Resolve(Xaml("Left", ProbeTab + "<TabItem Header=\"x\" MinWidth=\"150\"/>")).Px, 3);

    [Theory]
    [InlineData("-10,0")]
    [InlineData("15,0")]
    public void A_tab_margin_does_not_change_the_strip(string margin) =>
        // TabPanel 排在側邊時用不含 Margin 的期望寬度（LayoutOracle「long tab Margin」實測）
        Assert.Equal(400 - 76, Resolve(Xaml("Left", ProbeTab + $"<TabItem Header=\"Age ratings\" Margin=\"{margin}\"/>")).Px, 3);

    [Fact]
    public void Localized_headers_are_measured_in_each_language()
    {
        var xaml = Xaml("Left", "<TabItem Header=\"{DynamicResource A}\"><Border x:Name=\"probe\"/></TabItem><TabItem Header=\"{DynamicResource B}\"/>");
        var doc = XDocument.Parse(xaml);
        doc.AddAnnotation(new SourceFile("Page.xaml"));
        var styles = new StyleIndex { SystemScrollBarWidth = Chrome.ScrollBarWidth };
        styles.Add(doc, "Page.xaml");
        var measurer = new ElementMeasurer(Fonts(), styles, Chrome, MonoFamily, MonoSize);
        var resolver = new WidthResolver(Chrome, styles, measurer);

        var english = new Dictionary<string, string> { ["A"] = "Platforms", ["B"] = "Age ratings" };
        measurer.ResourceText = k => english.GetValueOrDefault(k);
        Assert.Equal(400 - 76, resolver.Resolve(Probe(doc)).Px, 3);
        Assert.True(measurer.ResourceLookups > 0);

        // 同一個解析器換語言：標籤列的寬度不能被快取住
        var german = new Dictionary<string, string> { ["A"] = "Plattformen", ["B"] = "Altersfreigaben und Regionen" };   // 28 字 → 140
        measurer.ResourceText = k => german.GetValueOrDefault(k);
        Assert.Equal(400 - 161, resolver.Resolve(Probe(doc)).Px, 3);
    }

    [Theory]
    [InlineData("<TabItem><TabItem.Header><StackPanel/></TabItem.Header></TabItem>")]   // 用元素畫的標題
    [InlineData("<TabItem Header=\"{Binding Name}\"/>")]                               // 綁定
    [InlineData("<TabItem Header=\"x\" HeaderTemplate=\"{StaticResource T}\"/>")]       // 標題範本
    [InlineData("<ContentControl/>")]                                                   // 不是 TabItem 的項目
    public void Headers_it_cannot_read_leave_the_width_unknown(string other) =>
        Assert.NotEqual(WidthStatus.Known, Resolve(Xaml("Left", ProbeTab + other)).Status);

    [Theory]
    [InlineData("Left", "", 400 - (55 + 12 + 2) - 6)]                       // 標題 + 預設 Padding 6+6 + 標籤列邊距 2；內容區 6
    [InlineData("Right", "", 400 - (55 + 12 + 2) - 6)]
    [InlineData("Left", " Padding=\"20,2\"", 400 - (55 + 40 + 2) - 6)]     // 標籤的 Padding 取代預設
    public void The_default_template_puts_the_widest_tab_beside_the_content(string placement, string tabPadding, double expected)
    {
        var items = ProbeTab + $"<TabItem Header=\"Age ratings\"{tabPadding} BorderThickness=\"4\" Margin=\"7,0\"/>";
        var w = Resolve($"<Window {Ns}><TabControl Width=\"400\" TabStripPlacement=\"{placement}\">{items}</TabControl></Window>");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(expected, w.Px, 3);
    }

    [Fact]
    public void An_external_theme_keeps_the_default_template_unknown()
    {
        var w = Resolve($"<Window {Ns}><TabControl Width=\"400\" TabStripPlacement=\"Left\">{Items}</TabControl></Window>", theme: "MahApps");
        Assert.NotEqual(WidthStatus.Known, w.Status);
    }

    [Theory]
    // 多條件觸發器：靜態讀不出會不會成立
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"TabStripPlacement\" Value=\"Left\"/><Condition Property=\"IsEnabled\" Value=\"True\"/></MultiTrigger.Conditions><Setter Property=\"Width\" TargetName=\"C0\" Value=\"300\"/></MultiTrigger>")]
    // 設在控制項本身的值（Padding 之類）會改變 TemplateBinding 的結果
    [InlineData("<Trigger Property=\"TabStripPlacement\" Value=\"Left\"><Setter Property=\"Padding\" Value=\"20\"/></Trigger>")]
    // 目標不在範本裡
    [InlineData("<Trigger Property=\"TabStripPlacement\" Value=\"Left\"><Setter Property=\"Width\" TargetName=\"Nope\" Value=\"20\"/></Trigger>")]
    public void Triggers_it_cannot_apply_leave_the_width_unknown(string trigger) =>
        Assert.NotEqual(WidthStatus.Known, Resolve(Xaml("Left", Items, trigger: trigger)).Status);

    [Fact]
    public void A_rotated_strip_is_unknown() =>
        Assert.NotEqual(WidthStatus.Known, Resolve(Xaml("Left", Items, rotate: "Angle=\"90\"")).Status);

    [Fact]
    public void A_tab_template_with_a_fixed_width_is_unknown() =>
        Assert.NotEqual(WidthStatus.Known,
            Resolve(Xaml("Left", Items, itemExtra: "<ControlTemplate.Triggers><Trigger Property=\"IsSelected\" Value=\"True\"><Setter Property=\"MinWidth\" TargetName=\"Selected\" Value=\"200\"/></Trigger></ControlTemplate.Triggers>")).Status);

    [Fact]
    public void A_style_on_the_header_text_must_use_the_tab_font()
    {
        // Playnite：TextBlock 的樣式與 TabItem 用同一個字級資源 → 照量
        const string same = "<sys:Double xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"Size\">10</sys:Double>" +
                            "<Style x:Key=\"Text\" TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"{DynamicResource Size}\"/></Style>";
        var items = Items.Replace("<TabItem ", "<TabItem FontSize=\"{DynamicResource Size}\" ");
        Assert.Equal(400 - 76, Resolve(Xaml("Left", items, textBlock: "Style=\"{StaticResource Text}\"", resources: same)).Px, 3);

        // 範本的樣式把字放大：量 TabItem 的字型會量小 → 不知道
        const string bigger = "<Style x:Key=\"Text\" TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"20\"/></Style>";
        Assert.NotEqual(WidthStatus.Known, Resolve(Xaml("Left", Items, textBlock: "Style=\"{StaticResource Text}\"", resources: bigger)).Status);
    }
}
