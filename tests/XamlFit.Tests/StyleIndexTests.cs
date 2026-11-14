using System.Xml.Linq;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;

namespace XamlFit.Tests;

public class StyleIndexTests
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static (StyleIndex Index, XElement Probe) Build(string body)
    {
        var doc = XDocument.Parse($"<Root {Ns}>{body}</Root>", LoadOptions.SetLineInfo);
        var idx = new StyleIndex();
        idx.Add(doc);
        var probe = doc.Descendants().First(e => e.Attribute(XName.Get("Name",
            "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "probe");
        return (idx, probe);
    }

    [Fact]
    public void Named_style_supplies_font_size()
    {
        // 實查 QuillNest：SectionHint 設 FontSize=11，不解析就用預設 12 去量，
        // 寬度高估 9%，報出一個「已經溢出 2.9px」的假警報。
        var (idx, probe) = Build("""
            <Root.Resources>
              <Style x:Key="Hint" TargetType="TextBlock">
                <Setter Property="FontSize" Value="11"/>
              </Style>
            </Root.Resources>
            """ + """<TextBlock x:Name="probe" Style="{StaticResource Hint}" Text="hi"/>""");

        Assert.Equal("11", idx.Resolve(probe, "FontSize", inheritable: true));
    }

    [Fact]
    public void Local_attribute_beats_the_style()
    {
        var (idx, probe) = Build("""
            <Root.Resources>
              <Style x:Key="Hint" TargetType="TextBlock">
                <Setter Property="FontSize" Value="11"/>
              </Style>
            </Root.Resources>
            """ + """<TextBlock x:Name="probe" Style="{StaticResource Hint}" FontSize="20" Text="hi"/>""");

        Assert.Equal("20", idx.Resolve(probe, "FontSize", inheritable: true));
    }

    [Fact]
    public void BasedOn_chain_is_followed()
    {
        var (idx, probe) = Build("""
            <Root.Resources>
              <Style x:Key="Base" TargetType="TextBlock">
                <Setter Property="FontFamily" Value="Consolas"/>
              </Style>
              <Style x:Key="Hint" TargetType="TextBlock" BasedOn="{StaticResource Base}">
                <Setter Property="FontSize" Value="11"/>
              </Style>
            </Root.Resources>
            """ + """<TextBlock x:Name="probe" Style="{StaticResource Hint}" Text="hi"/>""");

        Assert.Equal("Consolas", idx.Resolve(probe, "FontFamily", inheritable: true));
        Assert.Equal("11", idx.Resolve(probe, "FontSize", inheritable: true));
    }

    [Fact]
    public void BasedOn_cycles_do_not_hang()
    {
        // BasedOn 寫成環不是語法錯誤，解析器要自己擋
        var (idx, probe) = Build("""
            <Root.Resources>
              <Style x:Key="A" TargetType="TextBlock" BasedOn="{StaticResource B}"/>
              <Style x:Key="B" TargetType="TextBlock" BasedOn="{StaticResource A}"/>
            </Root.Resources>
            """ + """<TextBlock x:Name="probe" Style="{StaticResource A}" Text="hi"/>""");

        Assert.Null(idx.Resolve(probe, "FontSize", inheritable: false));
    }

    [Fact]
    public void Unique_implicit_style_applies()
    {
        var (idx, probe) = Build("""
            <Root.Resources>
              <Style TargetType="TextBlock"><Setter Property="FontSize" Value="13"/></Style>
            </Root.Resources>
            """ + """<TextBlock x:Name="probe" Text="hi"/>""");

        Assert.Equal("13", idx.Resolve(probe, "FontSize", inheritable: true));
    }

    [Fact]
    public void Ambiguous_implicit_styles_are_not_guessed()
    {
        // 兩個資源字典檔各有一個同 TargetType 的隱含樣式、值不一樣：
        // 哪個生效取決於合併的順序，靜態不猜 —— 而且要說「讀不出來」，不是「沒設定」
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                          "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
        var idx = new StyleIndex();
        idx.Add(XDocument.Parse($"<ResourceDictionary {ns}><Style TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"13\"/></Style></ResourceDictionary>"), "Light.xaml");
        idx.Add(XDocument.Parse($"<ResourceDictionary {ns}><Style TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"17\"/></Style></ResourceDictionary>"), "Dark.xaml");

        var page = XDocument.Parse($"<Grid {ns}><TextBlock x:Name=\"probe\" Text=\"hi\"/></Grid>");
        var probe = page.Descendants().First(e => e.Name.LocalName == "TextBlock");

        Assert.Null(idx.Resolve(probe, "FontSize", inheritable: true));
        Assert.True(idx.ResolveEx(probe, "FontSize", inheritable: true).IsUnreadable);
    }

    [Fact]
    public void Style_supplied_margin_reaches_the_width_resolver()
    {
        var xaml = """
            <Root NS>
              <Root.Resources>
                <Style x:Key="Hint" TargetType="TextBlock">
                  <Setter Property="Margin" Value="20,0,0,0"/>
                </Style>
              </Root.Resources>
              <Border Width="300">
                <TextBlock x:Name="probe" Style="{StaticResource Hint}" Text="hi"/>
              </Border>
            </Root>
            """.Replace("NS", Ns);
        var doc = XDocument.Parse(xaml, LoadOptions.SetLineInfo);

        var idx = new StyleIndex();
        idx.Add(doc);
        var probe = doc.Descendants().First(e => e.Attribute(XName.Get("Name",
            "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "probe");

        var w = new WidthResolver(ChromeTable.Builtin(), idx).Resolve(probe);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(280, w.Px);
    }

    [Theory]
    [InlineData("{x:Type Button}", "Button")]
    [InlineData("Button", "Button")]
    [InlineData("{x:Type local:MyControl}", "MyControl")]
    [InlineData("local:MyControl", "MyControl")]
    public void TargetType_normalisation(string input, string expected) =>
        Assert.Equal(expected, StyleIndex.NormalizeType(input));

    [Theory]
    [InlineData("{StaticResource Hint}", "Hint")]
    [InlineData("{DynamicResource Hint}", "Hint")]
    [InlineData("{StaticResource {x:Type Button}}", null)]
    [InlineData("Hint", null)]
    public void Resource_key_extraction(string input, string? expected) =>
        Assert.Equal(expected, StyleIndex.ResourceKeyOf(input));
}
