using System.Xml.Linq;
using XamlFit.Core.Layout;

namespace XamlFit.Tests;

public class WidthResolverTests
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static readonly ChromeTable Chrome = ChromeTable.Builtin();
    private static readonly WidthResolver Resolver = new(Chrome);

    /// <summary>帶樣式索引的解析器，給需要驗證樣式解析的測試用。</summary>
    private static WidthResolver WithStyles(XDocument doc)
    {
        var idx = new XamlFit.Core.Xaml.StyleIndex();
        idx.Add(doc);
        return new WidthResolver(Chrome, idx);
    }

    /// <summary>解析片段，回傳第一個叫 "probe" 的元素的可用寬度。</summary>
    private static AvailableWidth Resolve(string body)
    {
        var doc = XDocument.Parse($"<Root {Ns}>{body}</Root>", LoadOptions.SetLineInfo);
        var probe = doc.Descendants().First(e => e.Attribute(XName.Get("Name",
            "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "probe");
        return Resolver.Resolve(probe);
    }

    [Fact]
    public void Embedded_chrome_table_loads()
    {
        Assert.True(Chrome.Count > 10);
        Assert.Contains("Aero2", Chrome.Theme);
    }

    [Fact]
    public void Explicit_width_on_the_element_itself()
    {
        var w = Resolve("""<TextBlock x:Name="probe" Width="200" Text="hi"/>""");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(200, w.Px);
    }

    [Fact]
    public void MaxWidth_is_a_bound_too()
    {
        var w = Resolve("""<TextBlock x:Name="probe" MaxWidth="150" Text="hi"/>""");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(150, w.Px);
    }

    [Fact]
    public void Ancestor_width_minus_insets()
    {
        // Border 寬 300，Padding 左右各 10，TextBlock Margin 左右各 5 → 300-20-10 = 270
        var w = Resolve("""
            <Border Width="300" Padding="10">
              <TextBlock x:Name="probe" Margin="5" Text="hi"/>
            </Border>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(270, w.Px);
    }

    [Fact]
    public void Thickness_with_four_values_uses_left_and_right()
    {
        var w = Resolve("""
            <Border Width="300" Padding="4,100,6,100">
              <TextBlock x:Name="probe" Text="hi"/>
            </Border>
            """);
        Assert.Equal(290, w.Px);
    }

    [Fact]
    public void Button_content_deducts_control_chrome()
    {
        // 規則 1：位置自己就是樣板化控制項。Aero2 的 Button chrome = 4
        var w = Resolve("""<Button x:Name="probe" Width="100" Content="Save"/>""");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.True(w.UsedChromeTable);
        Assert.Equal(96, w.Px);
    }

    [Fact]
    public void CheckBox_chrome_includes_the_box()
    {
        // CheckBox 的方框吃掉約 20 DIP —— 漏掉它會嚴重高估可用寬度
        var w = Resolve("""<CheckBox x:Name="probe" Width="100" Content="Enable"/>""");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.True(w.Px < 85, $"CheckBox chrome should be ~20 DIP, got available {w.Px}");
    }

    [Fact]
    public void Fixed_grid_column_is_a_bound()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="0" Text="hi"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(120, w.Px);
    }

    [Fact]
    public void Star_grid_column_needs_v2()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="0" Text="hi"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
    }

    [Fact]
    public void Omitted_column_width_defaults_to_star_not_auto()
    {
        // ⚠ <ColumnDefinition/> 沒寫 Width 時預設是 "*"，不是 Auto 也不是 0
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions><ColumnDefinition/></Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="0" Text="hi"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
    }

    [Fact]
    public void ColumnSpan_sums_the_spanned_columns()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="60"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="0" Grid.ColumnSpan="2" Text="hi"/>
            </Grid>
            """);
        Assert.Equal(160, w.Px);
    }

    // ===== M2 的兩個必要條件（驗證者實跑後發現，語料各命中 79／60 處）=====

    [Fact]
    public void SizeToContent_invalidates_the_window_width()
    {
        // 語料裡 46 個檔案同時寫了 SizeToContent 與數字 Width —— 照單全收會給出完全錯誤的預算
        var w = Resolve("""
            <Window Width="800" SizeToContent="WidthAndHeight">
              <TextBlock x:Name="probe" Text="hi"/>
            </Window>
            """);
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("SizeToContent", w.Reason);
    }

    [Fact]
    public void SizeToContent_Height_only_does_not_invalidate_width()
    {
        var w = Resolve("""
            <Window Width="800" SizeToContent="Height">
              <TextBlock x:Name="probe" Text="hi"/>
            </Window>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(800, w.Px);
    }

    [Fact]
    public void Horizontally_scrollable_content_is_not_clipped()
    {
        // 文字可以捲動 → 不會被裁 → 上界沒有意義
        var w = Resolve("""
            <Border Width="300">
              <ScrollViewer HorizontalScrollBarVisibility="Auto">
                <TextBlock x:Name="probe" Text="hi"/>
              </ScrollViewer>
            </Border>
            """);
        Assert.Equal(WidthStatus.NoDeclaredLimit, w.Status);
        Assert.Contains("scrolls horizontally", w.Reason);
    }

    [Fact]
    public void ScrollViewer_without_horizontal_scrolling_still_bounds()
    {
        // ⚠ ScrollViewer 的 HorizontalScrollBarVisibility 預設是 Disabled
        var w = Resolve("""
            <Border Width="300">
              <ScrollViewer>
                <TextBlock x:Name="probe" Text="hi"/>
              </ScrollViewer>
            </Border>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
    }

    // ===== M0 修正過的錯誤，不准回歸 =====

    [Fact]
    public void Context_menu_items_are_not_bounded_by_the_window()
    {
        // 實查：QuillNest NoteEditorWindow.xaml:838。快顯選單不在視窗的版面裡。
        var w = Resolve("""
            <Window Width="800">
              <RichTextBox>
                <RichTextBox.ContextMenu>
                  <ContextMenu>
                    <MenuItem x:Name="probe" Header="開啟連結"/>
                  </ContextMenu>
                </RichTextBox.ContextMenu>
              </RichTextBox>
            </Window>
            """);
        Assert.Equal(WidthStatus.NoDeclaredLimit, w.Status);
    }

    [Fact]
    public void Control_names_match_by_suffix()
    {
        // MahApps 的 MetroTabControl 要對得上 TabControl；精確比對會整組漏掉第三方控制項庫
        var (known, chrome) = Chrome.Lookup("MetroButton");
        Assert.True(known);
        Assert.Equal(4, chrome);
    }

    [Fact]
    public void Unknown_chrome_makes_the_site_unmeasurable_rather_than_guessing()
    {
        // 退回「只算 padding+border」會低估 chrome → 高估可用寬度 → 漏報溢出。
        // ⚠ 內建的表裡現在每個控制項都量到了，所以用一張刻意含 null 的表測這條原則。
        var table = ChromeTable.Parse("""
            { "schemaVersion": 1, "theme": "test",
              "controls": { "MysteryBox": { "chromeWidth": null, "minWidth": 0 } } }
            """);

        var doc = XDocument.Parse($"""
            <Root {Ns}>
              <MysteryBox Width="200">
                <TextBlock x:Name="probe" Text="hi"/>
              </MysteryBox>
            </Root>
            """, LoadOptions.SetLineInfo);
        var probe = doc.Descendants().First(e => e.Attribute(XName.Get("Name",
            "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "probe");

        var w = new WidthResolver(table).Resolve(probe);
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("chrome", w.Reason);
    }

    [Fact]
    public void ComboBoxItem_chrome_is_not_double_counted_inside_a_ComboBox()
    {
        // ComboBoxItem 的內距只存在於展開的下拉清單；關起來的選取框沒有它。
        // 「會不會被裁」問的是關起來那個框，所以只扣 ComboBox 的 chrome（27.8），
        // 不扣 ComboBoxItem 的（10）。兩個都扣會多扣約 6%。
        var w = Resolve("""
            <ComboBox Width="200">
              <ComboBoxItem x:Name="probe" Content="Option"/>
            </ComboBox>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(200 - 27.8, w.Px, 1);
    }

    [Fact]
    public void A_standalone_ComboBoxItem_still_gets_its_own_chrome()
    {
        // 不在 ComboBox 底下（例如在 ItemTemplate 之外的 ListBox 裡）就照常算
        var w = Resolve("""
            <StackPanel Width="200">
              <ComboBoxItem x:Name="probe" Content="Option"/>
            </StackPanel>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(190, w.Px, 1);
    }

    [Fact]
    public void Inside_a_template_the_width_comes_from_the_consumer()
    {
        var w = Resolve("""
            <DataTemplate>
              <TextBlock x:Name="probe" Text="hi"/>
            </DataTemplate>
            """);
        Assert.Equal(WidthStatus.InTemplate, w.Status);
    }

    [Fact]
    public void No_bound_anywhere_is_named_not_blank()
    {
        var w = Resolve("""
            <StackPanel>
              <TextBlock x:Name="probe" Text="hi"/>
            </StackPanel>
            """);
        Assert.Equal(WidthStatus.NoDeclaredLimit, w.Status);
        Assert.NotEmpty(w.Reason);
    }

    [Fact]
    public void Chrome_larger_than_the_bound_is_unmeasurable_not_negative()
    {
        var w = Resolve("""<CheckBox x:Name="probe" Width="10" Content="x"/>""");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
    }

    [Theory]
    [InlineData("8", 16)]
    [InlineData("4,2", 8)]
    [InlineData("1,2,3,4", 4)]
    [InlineData("{StaticResource P}", 0)]
    [InlineData(null, 0)]
    public void Thickness_parsing(string? value, double expectedLeftPlusRight)
    {
        var (l, r) = WidthResolver.ParseThicknessLeftRight(value);
        Assert.Equal(expectedLeftPlusRight, l + r);
    }
}
