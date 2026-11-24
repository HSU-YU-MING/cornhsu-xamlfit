using System.Xml.Linq;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 樣式裡的附加屬性不是元素自己的屬性（XamlA11y 的語料抓到的：HandyControl 的 hc:IconElement.Width
/// 被截成 Width）。版面的預期值跟 prototype/LayoutOracle 的「attached setter:」案例在真的 WPF 上量到的一樣。
/// </summary>
public class AttachedSetterTests
{
    private const string Local = "xmlns:local=\"clr-namespace:App\"";

    private static AvailableWidth InBorder(string setters, string targetType = "TextBlock") => Resolve(
        $"<Window {Ns} {Local} WindowStyle=\"None\" AllowsTransparency=\"True\"><Window.Resources>" +
        $"<Style x:Key=\"S\" TargetType=\"{targetType}\">{setters}</Style></Window.Resources>" +
        "<Border Width=\"300\"><TextBlock x:Name=\"probe\" Style=\"{StaticResource S}\" Text=\"Save all\"/></Border></Window>");

    private static Resolved InStyle(string query, string setters, string targetType = "Button")
    {
        var doc = XDocument.Parse(
            $"<Window {Ns} {Local}><Window.Resources><Style x:Key=\"S\" TargetType=\"{targetType}\">{setters}</Style></Window.Resources>" +
            "<Button x:Name=\"probe\" Style=\"{StaticResource S}\"/></Window>");
        var index = new StyleIndex();
        index.Add(doc);
        return index.ResolveEx(Probe(doc), query, inheritable: false);
    }

    [Theory]
    [InlineData("local:IconElement.Width")]
    [InlineData("local:IconElement.MaxWidth")]
    [InlineData("local:TitleElement.Margin")]
    [InlineData("local:TitleElement.Padding")]
    public void An_attached_property_is_not_the_elements_own(string property)
    {
        var w = InBorder($"<Setter Property=\"{property}\" Value=\"12\"/>");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300, w.Px, 3);
    }

    [Fact]
    public void An_attached_property_after_the_own_one_does_not_replace_it()
    {
        // 反方向：附加屬性寫在後面，蓋掉真正的 Width → 上界變大、漏報
        var w = InBorder("<Setter Property=\"Width\" Value=\"40\"/><Setter Property=\"local:IconElement.Width\" Value=\"200\"/>");
        Assert.Equal(40, w.Px, 3);
    }

    [Theory]
    [InlineData("FrameworkElement.Width")]
    [InlineData("TextBlock.Width")]
    [InlineData("Control.Width")]      // WPF 沿基底型別找到的是同一個 FrameworkElement.WidthProperty
    [InlineData("Canvas.Width")]
    public void An_owner_that_is_an_element_type_is_only_a_spelling(string property) =>
        Assert.Equal(40, InBorder($"<Setter Property=\"{property}\" Value=\"40\"/>").Px, 3);

    [Theory]
    [InlineData("local:Card")]
    [InlineData("{x:Type local:Card}")]
    public void The_target_type_itself_as_owner_is_only_a_spelling(string targetType)
    {
        Assert.Equal("40", InStyle("Width", "<Setter Property=\"local:Card.Width\" Value=\"40\"/>", targetType).Value);
        Assert.Equal(ResolveKind.NotSet, InStyle("Width", "<Setter Property=\"local:Other.Width\" Value=\"40\"/>", targetType).Kind);
    }

    [Theory]
    [InlineData("FontSize", "TextElement.FontSize")]
    [InlineData("FontSize", "TextBlock.FontSize")]
    [InlineData("FontSize", "Control.FontSize")]
    [InlineData("TextOptions.TextFormattingMode", "TextOptions.TextFormattingMode")]
    [InlineData("Typography.Kerning", "Typography.Kerning")]
    [InlineData("WindowChrome.WindowChrome", "shell:WindowChrome.WindowChrome")]
    [InlineData("ScrollViewer.VerticalScrollBarVisibility", "ScrollViewer.VerticalScrollBarVisibility")]
    [InlineData("VerticalScrollBarVisibility", "ScrollViewer.VerticalScrollBarVisibility")]     // 同一個 DependencyProperty（AddOwner）
    [InlineData("ScrollViewer.VerticalScrollBarVisibility", "VerticalScrollBarVisibility")]
    [InlineData("VerticalScrollBarVisibility", "hc:ScrollViewer.VerticalScrollBarVisibility")]  // 同名的衍生類別
    [InlineData("BorderThickness", "Border.BorderThickness")]
    [InlineData(StyleIndex.CasingSetter, "mah:ControlsHelper.ContentCharacterCasing")]
    [InlineData(StyleIndex.CasingSetter, "ContentCharacterCasing")]
    public void Attached_properties_this_tool_reads_still_resolve(string query, string setter) =>
        Assert.Equal("v", InStyle(query, $"<Setter Property=\"{setter}\" Value=\"v\"/>").Value);

    [Theory]
    [InlineData("FontFamily", "materialDesign:HintAssist.FontFamily")]
    [InlineData("Padding", "hc:TitleElement.Padding")]
    [InlineData("Orientation", "wpf:CalendarAssist.Orientation")]
    [InlineData("TextFormattingMode", "TextOptions.TextFormattingMode")]
    public void Other_owners_do_not_leak_into_the_short_name(string query, string setter) =>
        Assert.Equal(ResolveKind.NotSet, InStyle(query, $"<Setter Property=\"{setter}\" Value=\"v\"/>").Kind);

    [Fact]
    public void An_attached_Template_does_not_replace_the_ControlTemplate()
    {
        var doc = XDocument.Parse(
            $"<Window {Ns} {Local}><Window.Resources><Style x:Key=\"S\" TargetType=\"Button\"><Setter Property=\"local:Badge.Template\" Value=\"{{StaticResource T}}\"/></Style></Window.Resources>" +
            "<Button x:Name=\"probe\" Style=\"{StaticResource S}\"/></Window>");
        var index = new StyleIndex();
        index.Add(doc);
        Assert.Equal(TemplateKind.Default, index.TemplateOf(Probe(doc)).Kind);
    }

    // ───────── {TemplateBinding 擁有者.屬性}：跟 setter 用同一套名字，不然兩邊對不上 ─────────

    /// <summary>圖示寬度走附加屬性的按鈕（HandyControl 的 ButtonIcon 的形狀）。</summary>
    private static AvailableWidth IconButton(string setters, string attributes = "", string width = "{TemplateBinding local:IconElement.Width}") => Resolve(
        $"<Window {Ns} {Local} WindowStyle=\"None\" AllowsTransparency=\"True\"><Window.Resources>" +
        $"<Style x:Key=\"S\" TargetType=\"Button\">{setters}<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\">" +
        "<Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition/></Grid.ColumnDefinitions>" +
        $"<Border Width=\"{width}\"/><ContentPresenter Grid.Column=\"1\"/></Grid></ControlTemplate></Setter.Value></Setter></Style></Window.Resources>" +
        $"<Button Style=\"{{StaticResource S}}\" Width=\"200\" {attributes}><TextBlock x:Name=\"probe\"/></Button></Window>");

    [Fact]
    public void TemplateBinding_to_an_attached_property_reads_the_attached_setter()
    {
        var w = IconButton("<Setter Property=\"local:IconElement.Width\" Value=\"30\"/>");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(170, w.Px, 3);
    }

    [Fact]
    public void TemplateBinding_to_an_attached_property_reads_the_local_attribute() =>
        Assert.Equal(160, IconButton("<Setter Property=\"local:IconElement.Width\" Value=\"30\"/>", "local:IconElement.Width=\"40\"").Px, 3);

    [Fact]
    public void An_attached_property_that_is_not_statically_readable_blocks() =>
        Assert.Equal(WidthStatus.Unmeasurable, IconButton("<Setter Property=\"local:IconElement.Width\" Value=\"{Binding Icon}\"/>").Status);

    [Fact]
    public void TemplateBinding_with_the_controls_own_type_as_owner_still_reads_it() =>
        Assert.Equal(170, IconButton("<Setter Property=\"MinWidth\" Value=\"30\"/>", width: "{TemplateBinding Button.MinWidth}").Px, 3);

    [Fact]
    public void A_template_font_bound_to_another_property_is_not_the_controls_font()
    {
        // MaterialDesign 的 TextElement.FontFamily="{TemplateBinding wpf:HintAssist.FontFamily}"：不是控制項自己的字型
        var w = Resolve(
            $"<Window {Ns} {Local} WindowStyle=\"None\" AllowsTransparency=\"True\"><Button Width=\"200\"><Button.Template><ControlTemplate TargetType=\"Button\">" +
            "<Border TextElement.FontFamily=\"{TemplateBinding local:Hint.FontFamily}\"><ContentPresenter/></Border></ControlTemplate></Button.Template>" +
            "<TextBlock x:Name=\"probe\"/></Button></Window>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
    }

    [Fact]
    public void The_reported_text_is_no_longer_bounded_by_the_icon_width()
    {
        // 回報的重現：needs 42.2 px, has 12.0 px
        var dir = Path.Combine(Path.GetTempPath(), $"xamlfit-att-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "T.xaml"),
                $"<Window {Ns} {Local} Width=\"400\" ResizeMode=\"NoResize\"><Window.Resources><Style x:Key=\"S\" TargetType=\"TextBlock\"><Setter Property=\"local:IconElement.Width\" Value=\"12\"/></Style></Window.Resources>" +
                "<Grid><TextBlock Style=\"{StaticResource S}\" Text=\"Save all\"/></Grid></Window>");
            var site = new XamlScanner(ChromeTable.Builtin()).Scan(dir).Sites.Single();
            Assert.Equal(WidthStatus.Known, site.Width.Status);
            Assert.True(site.Width.Px > 300, $"{site.Width.Px}");
        }
        finally { Directory.Delete(dir, true); }
    }
}
