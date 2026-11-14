using System.Xml.Linq;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 控制項換了範本時，內距從專案自己的範本算。
/// 每一個預期值都跟 prototype/LayoutOracle 的「template:」案例在真的 WPF 上量到的一樣。
/// </summary>
public class TemplateChromeTests
{
    private const string Resources = """
        <Window.Resources>
          <Style x:Key="Flat" TargetType="Button">
            <Setter Property="Padding" Value="14,6"/>
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                  <ContentPresenter HorizontalAlignment="Center"/>
                </Border>
              </ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style x:Key="Margined" TargetType="Button">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border BorderThickness="2"><ContentPresenter Margin="{TemplateBinding Padding}"/></Border>
              </ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style x:Key="Iconed" TargetType="Button">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border Padding="8,0">
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                    <Border Width="16" Height="16" Margin="0,0,6,0"/>
                    <ContentPresenter Grid.Column="1"/>
                  </Grid>
                </Border>
              </ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style x:Key="Check" TargetType="CheckBox">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="CheckBox">
                <StackPanel Orientation="Horizontal">
                  <Border Width="18" Height="18" BorderThickness="1"/>
                  <ContentPresenter Margin="8,0,0,0"/>
                </StackPanel>
              </ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style x:Key="Group" TargetType="GroupBox">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="GroupBox">
                <Border BorderThickness="1" Padding="10">
                  <DockPanel>
                    <ContentPresenter DockPanel.Dock="Top" ContentSource="Header" Margin="4,0,4,6"/>
                    <ContentPresenter/>
                  </DockPanel>
                </Border>
              </ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style x:Key="NoHost" TargetType="Button">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="Button"><Border Padding="8,0"><Rectangle/></Border></ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style x:Key="Fonted" TargetType="Button">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="Button"><Border><ContentPresenter TextElement.FontSize="20"/></Border></ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style x:Key="Halved" TargetType="Button">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
                  <ContentPresenter/>
                </Grid>
              </ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style TargetType="ToggleButton">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="ToggleButton"><Border Padding="12,0"><ContentPresenter/></Border></ControlTemplate>
            </Setter.Value></Setter>
          </Style>
          <Style TargetType="ScrollViewer">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="ScrollViewer">
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <Grid.RowDefinitions><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                  <ScrollContentPresenter/>
                  <ScrollBar Grid.Column="1" Width="10" MinWidth="10" Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}"/>
                  <Rectangle Grid.Column="1" Grid.Row="1"/>
                </Grid>
              </ControlTemplate>
            </Setter.Value></Setter>
          </Style>
        </Window.Resources>
        """;

    private static AvailableWidth Page(string body) =>
        Resolve($"<Window {Ns} WindowStyle=\"None\" AllowsTransparency=\"True\">{Resources}{body}</Window>");

    private static AvailableWidth Button(string attributes) =>
        Page($"<Button {attributes} Width=\"200\"><TextBlock x:Name=\"probe\"/></Button>");

    [Fact]
    public void Padding_and_border_flow_through_TemplateBinding()
    {
        // 200 − 預設邊框 2 − 樣式的 Padding 14×2
        var w = Button("Style=\"{StaticResource Flat}\"");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(170, w.Px, 3);
    }

    [Fact]
    public void A_local_value_overrides_the_style_setter()
    {
        Assert.Equal(138, Button("Style=\"{StaticResource Flat}\" Padding=\"30,0\"").Px, 3);
        Assert.Equal(164, Button("Style=\"{StaticResource Flat}\" BorderThickness=\"4\"").Px, 3);
    }

    [Fact]
    public void An_unset_Padding_takes_the_theme_default()
    {
        // 樣式沒設 Padding：{TemplateBinding Padding} 拿到的是佈景預設樣式的 1,1,1,1
        Assert.Equal(194, Button("Style=\"{StaticResource Margined}\"").Px, 3);
    }

    [Fact]
    public void Siblings_inside_the_template_are_accounted_for()
    {
        // Border 8×2 + 圖示 16 + 圖示的右邊距 6
        Assert.Equal(162, Button("Style=\"{StaticResource Iconed}\"").Px, 3);
        // 方框 18 + 內容的左邊距 8
        Assert.Equal(174, Page("<CheckBox Style=\"{StaticResource Check}\" Width=\"200\"><TextBlock x:Name=\"probe\"/></CheckBox>").Px, 3);
    }

    [Fact]
    public void Header_and_content_use_their_own_presenters()
    {
        Assert.Equal(278, Page("<GroupBox Style=\"{StaticResource Group}\" Width=\"300\" Header=\"H\"><TextBlock x:Name=\"probe\"/></GroupBox>").Px, 3);
        Assert.Equal(270, Page("<GroupBox Style=\"{StaticResource Group}\" Width=\"300\"><GroupBox.Header><TextBlock x:Name=\"probe\"/></GroupBox.Header></GroupBox>").Px, 3);
    }

    [Fact]
    public void An_implicit_style_is_found() =>
        Assert.Equal(176, Page("<ToggleButton Width=\"200\"><TextBlock x:Name=\"probe\"/></ToggleButton>").Px, 3);

    [Fact]
    public void A_template_written_on_the_element_is_found()
    {
        var w = Page("""
            <Button Width="200">
              <Button.Template>
                <ControlTemplate TargetType="Button"><Border Padding="25,0"><ContentPresenter/></Border></ControlTemplate>
              </Button.Template>
              <TextBlock x:Name="probe"/>
            </Button>
            """);
        Assert.Equal(150, w.Px, 3);
    }

    [Fact]
    public void A_template_with_no_content_host_is_not_guessed()
    {
        var w = Button("Style=\"{StaticResource NoHost}\"");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("hosts the Content", w.Reason);
    }

    [Fact]
    public void A_template_that_sets_its_own_font_is_not_trusted()
    {
        // 範本在內容上設了字級 → 我們量文字用的字型就不對了
        var w = Button("Style=\"{StaticResource Fonted}\"");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("FontSize", w.Reason);
    }

    [Fact]
    public void A_template_that_shares_the_width_proportionally_has_no_fixed_chrome()
    {
        var w = Button("Style=\"{StaticResource Halved}\"");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("fixed share", w.Reason);
    }

    [Fact]
    public void A_restyled_ScrollViewer_uses_its_own_scroll_bar_width()
    {
        // 換了捲軸樣式的 App 幾乎都會重寫 ScrollViewer 的範本（實查 ScreenToGif：296 個位置）
        var w = Page("<ScrollViewer Width=\"300\"><TextBlock x:Name=\"probe\"/></ScrollViewer>");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(290, w.Px, 3);
    }

    [Fact]
    public void A_scroll_bar_narrower_than_the_system_minimum_is_not_believed()
    {
        // ⚠ LayoutOracle 實測：<ScrollBar Width="10"/> 實際是 16.8 寬。佈景的預設樣式把 MinWidth
        //   設成系統捲軸寬度，而 MinWidth 贏過比較窄的 Width。照 Width 算會高估內容區。
        var w = Resolve($"""
            <Window {Ns} WindowStyle="None" AllowsTransparency="True">
              <ScrollViewer Width="300">
                <ScrollViewer.Template>
                  <ControlTemplate TargetType="ScrollViewer">
                    <Grid>
                      <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                      <ScrollContentPresenter/>
                      <ScrollBar Grid.Column="1" Width="10"/>
                    </Grid>
                  </ControlTemplate>
                </ScrollViewer.Template>
                <TextBlock x:Name="probe"/>
              </ScrollViewer>
            </Window>
            """);
        Assert.Equal(300 - Chrome.ScrollBarWidth, w.Px, 3);
    }

    [Fact]
    public void A_restyled_ScrollViewer_with_the_bar_disabled_keeps_the_full_width() =>
        Assert.Equal(300, Page("<ScrollViewer Width=\"300\" VerticalScrollBarVisibility=\"Disabled\"><TextBlock x:Name=\"probe\"/></ScrollViewer>").Px, 3);

    [Fact]
    public void A_style_from_outside_the_project_is_unknown_not_default()
    {
        var w = Button("Style=\"{StaticResource MaterialDesignRaisedButton}\"");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("not defined in the project", w.Reason);
    }

    // ── 同一個樣式在好幾個佈景檔各有一份 ──

    private static string Theme(string padding) => $"""
        <ResourceDictionary {Ns}>
          <Style x:Key="Themed" TargetType="Button">
            <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="Button"><Border Padding="{padding}"><ContentPresenter/></Border></ControlTemplate>
            </Setter.Value></Setter>
          </Style>
        </ResourceDictionary>
        """;

    private static AvailableWidth Themed(string light, string dark) =>
        Resolve($"<Grid {Ns}><Button Style=\"{{StaticResource Themed}}\" Width=\"200\"><TextBlock x:Name=\"probe\"/></Button></Grid>",
            dictionaries: [("Themes/Light.xaml", Theme(light)), ("Themes/Dark.xaml", Theme(dark))]);

    [Fact]
    public void Theme_variants_that_agree_on_the_chrome_are_accepted()
    {
        // 深色與淺色佈景的範本通常只差在顏色：兩份算出來一樣就採用
        var w = Themed("10,0", "10,0");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(180, w.Px, 3);
    }

    [Fact]
    public void Theme_variants_that_disagree_are_not_guessed()
    {
        var w = Themed("10,0", "20,0");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("different chrome", w.Reason);
    }

    // ── 內容宿主不一定是 ContentPresenter ──

    [Fact]
    public void A_wrapper_bound_to_Content_is_the_content_host()
    {
        // <materialDesign:Ripple Content="{TemplateBinding Content}"/> 這種包裝。
        // 包裝自己的內距不知道 → 量不到；宣告之後就接得起來。
        const string page = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:lib="http://example.com/lib">
              <Button Width="200">
                <Button.Template>
                  <ControlTemplate TargetType="Button">
                    <Border Padding="5,0"><lib:Ripple Content="{TemplateBinding Content}"/></Border>
                  </ControlTemplate>
                </Button.Template>
                <TextBlock x:Name="probe"/>
              </Button>
            </Grid>
            """;

        Assert.Equal(WidthStatus.Unmeasurable, Resolve(page).Status);
        Assert.Equal(200 - 10 - 6, Resolve(page, chrome: new Dictionary<string, double> { ["Ripple"] = 6 }).Px, 3);
    }

    // ── 專案自己的控制項型別 ──

    [Fact]
    public void A_project_control_with_no_Padding_set_defaults_to_zero()
    {
        // 實查 ScreenToGif 的 ExWindow：範本裡 Padding="{TemplateBinding Padding}"，樣式沒設 Padding。
        // 專案自己的型別沒有佈景樣式，沒設定就是相依屬性的預設值 0。
        const string page = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:c="clr-namespace:App.Controls">
              <Grid.Resources>
                <Style TargetType="c:Panel">
                  <Setter Property="Template"><Setter.Value>
                    <ControlTemplate TargetType="c:Panel">
                      <Border BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border>
                    </ControlTemplate>
                  </Setter.Value></Setter>
                </Style>
              </Grid.Resources>
              <c:Panel Width="300"><TextBlock x:Name="probe"/></c:Panel>
            </Grid>
            """;
        var w = Resolve(page);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300, w.Px, 3);
    }

    [Fact]
    public void A_subclassed_root_inherits_the_theme_style_but_not_app_level_implicit_styles()
    {
        // <c:ExWindow x:Class="…"> 的實際型別是子類別。App.xaml 裡的隱含樣式套不到它身上；
        // Themes/Generic.xaml 裡的佈景樣式（透過 DefaultStyleKey）會。
        string Dictionary(string padding) => $"""
            <ResourceDictionary {Ns} xmlns:c="clr-namespace:App.Controls">
              <Style TargetType="c:ExWindow">
                <Setter Property="Template"><Setter.Value>
                  <ControlTemplate TargetType="c:ExWindow"><Border Padding="{padding}"><ContentPresenter/></Border></ControlTemplate>
                </Setter.Value></Setter>
              </Style>
            </ResourceDictionary>
            """;

        const string window = """
            <c:ExWindow xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:c="clr-namespace:App.Controls"
                        x:Class="App.Main" Width="400">
              <TextBlock x:Name="probe"/>
            </c:ExWindow>
            """;

        var themed = Resolve(window, dictionaries: [("Themes/Generic.xaml", Dictionary("20,0"))]);
        Assert.Equal(360, themed.Px, 3);

        var appLevel = Resolve(window, dictionaries: [("Styles/Windows.xaml", Dictionary("20,0"))]);
        Assert.Equal(WidthStatus.Unmeasurable, appLevel.Status);   // 沒有套得到的樣式 → 第三方型別、內距不明
    }
}

public class StyleScopeTests
{
    private static Resolved FontSizeOf(string page, params (string File, string Xaml)[] dictionaries)
    {
        var doc = XDocument.Parse(page);
        doc.AddAnnotation(new SourceFile("Views/Page.xaml"));
        var index = new StyleIndex();
        index.Add(doc, "Views/Page.xaml");
        foreach (var (file, xaml) in dictionaries) index.Add(XDocument.Parse(xaml), file);
        return index.ResolveEx(Probe(doc), "FontSize", inheritable: true);
    }

    private static string Dictionary(string size, string key = "") => $"""
        <ResourceDictionary {Ns}>
          <Style {(key.Length > 0 ? $"x:Key=\"{key}\"" : "")} TargetType="TextBlock"><Setter Property="FontSize" Value="{size}"/></Style>
        </ResourceDictionary>
        """;

    [Fact]
    public void Another_windows_resources_are_not_visible()
    {
        // ⚠ 實跑 QuillNest：每個對話框在自己的 Window.Resources 裡各有一份 <Style TargetType="Border">。
        //   扁平的索引把 16 份全部當成候選，於是每個 Border 的 Padding 都被判成讀不出來。
        var other = XDocument.Parse($"<Window {Ns}><Window.Resources><Style TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"30\"/></Style></Window.Resources></Window>");
        var page = XDocument.Parse($"<Window {Ns}><TextBlock x:Name=\"probe\"/></Window>");

        var index = new StyleIndex();
        index.Add(other, "Other.xaml");
        index.Add(page, "Page.xaml");

        Assert.Equal(ResolveKind.NotSet, index.ResolveEx(Probe(page), "FontSize", inheritable: true).Kind);
    }

    [Fact]
    public void The_nearest_enclosing_resources_win()
    {
        var r = FontSizeOf($"""
            <Window {Ns}>
              <Window.Resources><Style TargetType="TextBlock"><Setter Property="FontSize" Value="14"/></Style></Window.Resources>
              <StackPanel>
                <StackPanel.Resources><Style TargetType="TextBlock"><Setter Property="FontSize" Value="18"/></Style></StackPanel.Resources>
                <TextBlock x:Name="probe"/>
              </StackPanel>
            </Window>
            """);
        Assert.Equal("18", r.Value);
    }

    [Fact]
    public void Local_resources_shadow_the_application_dictionaries()
    {
        var r = FontSizeOf($"""
            <Window {Ns}>
              <Window.Resources><Style TargetType="TextBlock"><Setter Property="FontSize" Value="14"/></Style></Window.Resources>
              <TextBlock x:Name="probe"/>
            </Window>
            """, ("Themes/Text.xaml", Dictionary("22")));
        Assert.Equal("14", r.Value);
    }

    [Fact]
    public void Application_dictionaries_apply_everywhere() =>
        Assert.Equal("22", FontSizeOf($"<Window {Ns}><TextBlock x:Name=\"probe\"/></Window>", ("Themes/Text.xaml", Dictionary("22"))).Value);

    [Fact]
    public void A_key_that_is_not_in_the_project_is_unreadable_not_unset()
    {
        // ⚠ 第一版把專案外的樣式當成不存在，然後用預設字型去量
        var r = FontSizeOf($"<Window {Ns}><TextBlock x:Name=\"probe\" Style=\"{{StaticResource MaterialDesignBody1}}\"/></Window>");
        Assert.True(r.IsUnreadable);
        Assert.Contains("not defined in the project", r.Why);
    }

    [Fact]
    public void A_BasedOn_chain_is_followed()
    {
        var r = FontSizeOf($"""
            <Window {Ns}>
              <Window.Resources>
                <Style x:Key="Base" TargetType="TextBlock"><Setter Property="FontSize" Value="16"/></Style>
                <Style x:Key="Title" TargetType="TextBlock" BasedOn="{"{StaticResource Base}"}"><Setter Property="FontWeight" Value="Bold"/></Style>
              </Window.Resources>
              <TextBlock x:Name="probe" Style="{"{StaticResource Title}"}"/>
            </Window>
            """);
        Assert.Equal("16", r.Value);
    }

    [Fact]
    public void A_BasedOn_outside_the_project_is_unreadable()
    {
        var r = FontSizeOf($"""
            <Window {Ns}>
              <Window.Resources>
                <Style x:Key="Title" TargetType="TextBlock" BasedOn="{"{StaticResource ExternalBase}"}"/>
              </Window.Resources>
              <TextBlock x:Name="probe" Style="{"{StaticResource Title}"}"/>
            </Window>
            """);
        Assert.True(r.IsUnreadable);
    }

    [Fact]
    public void Implicit_styles_based_on_their_own_type_do_not_loop()
    {
        // ⚠ 實跑 MaterialDesign 直接卡死：N 份 BasedOn="{StaticResource {x:Type Button}}" 的隱含樣式
        //   互相把對方當基底，只靠深度上限擋的話是 N 的 16 次方
        var dictionaries = Enumerable.Range(0, 12).Select(i => ($"Themes/T{i}.xaml", $"""
            <ResourceDictionary {Ns}>
              <Style TargetType="TextBlock" BasedOn="{"{StaticResource {x:Type TextBlock}}"}"><Setter Property="Margin" Value="{i}"/></Style>
            </ResourceDictionary>
            """)).ToArray();

        var started = DateTime.UtcNow;
        var r = FontSizeOf($"<Window {Ns}><TextBlock x:Name=\"probe\"/></Window>", dictionaries);
        Assert.True((DateTime.UtcNow - started).TotalSeconds < 5, "resolution must not blow up exponentially");
        Assert.Equal(ResolveKind.NotSet, r.Kind);
    }

    [Fact]
    public void An_app_level_style_based_on_its_type_extends_the_real_definition()
    {
        // 實查 Playnite：一個檔案定義真正的 Button 樣式，另一個檔案的隱含樣式以型別為基底、只加觸發器
        var real = $"<ResourceDictionary {Ns}><Style TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"17\"/></Style></ResourceDictionary>";
        var extension = $"<ResourceDictionary {Ns}><Style TargetType=\"TextBlock\" BasedOn=\"{{StaticResource {{x:Type TextBlock}}}}\"><Setter Property=\"Margin\" Value=\"2\"/></Style></ResourceDictionary>";

        var r = FontSizeOf($"<Window {Ns}><TextBlock x:Name=\"probe\"/></Window>", ("Themes/Text.xaml", real), ("Views/Extra.xaml", extension));
        Assert.Equal("17", r.Value);
    }

    [Fact]
    public void An_explicit_style_replaces_the_implicit_one()
    {
        var r = FontSizeOf($"""
            <Window {Ns}>
              <Window.Resources>
                <Style TargetType="TextBlock"><Setter Property="FontSize" Value="20"/></Style>
                <Style x:Key="Plain" TargetType="TextBlock"/>
              </Window.Resources>
              <TextBlock x:Name="probe" Style="{"{StaticResource Plain}"}"/>
            </Window>
            """);
        Assert.Equal(ResolveKind.NotSet, r.Kind);
    }

    [Fact]
    public void A_resource_reference_is_resolved_lexically()
    {
        var r = FontSizeOf($"""
            <Window {Ns} xmlns:sys="clr-namespace:System;assembly=mscorlib">
              <Window.Resources><sys:Double x:Key="Big">19</sys:Double></Window.Resources>
              <TextBlock x:Name="probe" FontSize="{"{StaticResource Big}"}"/>
            </Window>
            """);
        Assert.Equal("19", r.Value);
    }

    [Theory]
    [InlineData("{Binding Size}")]
    [InlineData("{StaticResource NotDefinedAnywhere}")]
    [InlineData("{DynamicResource NotDefinedAnywhere}")]
    public void Markup_that_cannot_be_read_is_unreadable(string value) =>
        Assert.True(FontSizeOf($"<Window {Ns}><TextBlock x:Name=\"probe\" FontSize=\"{value}\"/></Window>").IsUnreadable);

    [Fact]
    public void Attached_font_properties_on_a_container_are_inherited() =>
        Assert.Equal("15", FontSizeOf($"<StackPanel {Ns} TextElement.FontSize=\"15\"><TextBlock x:Name=\"probe\"/></StackPanel>").Value);

    [Fact]
    public void Dictionaries_in_the_same_application_are_preferred()
    {
        // 實查 ScreenToGif：主程式與翻譯工具各有自己的 Themes/ComboBox.xaml
        var page = XDocument.Parse($"<Window {Ns}><TextBlock x:Name=\"probe\"/></Window>");
        page.AddAnnotation(new SourceFile("App/Views/Main.xaml"));

        var index = new StyleIndex();
        index.Add(XDocument.Parse(Dictionary("13")), "App/Themes/Text.xaml");
        index.Add(XDocument.Parse(Dictionary("21")), "Tool/Themes/Text.xaml");
        index.SetProjectDirectories(["App/", "Tool/"]);

        Assert.Equal("13", index.ResolveEx(Probe(page), "FontSize", inheritable: true).Value);
    }

    [Fact]
    public void TemplateOf_distinguishes_default_custom_and_unknown()
    {
        var doc = XDocument.Parse($"""
            <Window {Ns}>
              <Window.Resources>
                <Style x:Key="Custom" TargetType="Button">
                  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><ContentPresenter/></ControlTemplate></Setter.Value></Setter>
                </Style>
                <Style x:Key="Plain" TargetType="Button"><Setter Property="Margin" Value="2"/></Style>
              </Window.Resources>
              <Button x:Name="a"/>
              <Button x:Name="b" Style="{"{StaticResource Custom}"}"/>
              <Button x:Name="c" Style="{"{StaticResource Plain}"}"/>
              <Button x:Name="d" Style="{"{StaticResource Elsewhere}"}"/>
              <Button x:Name="e" Template="{"{Binding T}"}"/>
            </Window>
            """);
        var index = new StyleIndex();
        index.Add(doc);

        TemplateKind Kind(string name) =>
            index.TemplateOf(doc.Descendants().First(x => (string?)x.Attribute(X + "Name") == name)).Kind;

        Assert.Equal(TemplateKind.Default, Kind("a"));
        Assert.Equal(TemplateKind.Custom, Kind("b"));
        Assert.Equal(TemplateKind.Default, Kind("c"));
        Assert.Equal(TemplateKind.Unknown, Kind("d"));
        Assert.Equal(TemplateKind.Unknown, Kind("e"));
    }
}
