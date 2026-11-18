using XamlFit.Core.Layout;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 全面重讀時補上的版面語意。每一條的預期值都跟 prototype/LayoutOracle 在真的 WPF 視窗上量到的一樣
/// （那支程式需要 Windows；這裡是它的結論，寫成任何平台都跑得動的斷言）。
/// </summary>
public class WindowFrameTests
{
    private static AvailableWidth Window(string attributes) =>
        Resolve($"<Window {Ns} Width=\"400\" {attributes}><TextBlock x:Name=\"probe\" Text=\"hi\"/></Window>");

    [Fact]
    public void Window_width_is_the_outer_width()
    {
        // ⚠ 第一版完全沒扣：每一個以視窗寬度為上界的位置都高估了 16 DIP
        var w = Window("");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(400 - Chrome.WindowFrame, w.Px);
    }

    [Fact]
    public void Three_d_border_window_has_a_thicker_frame() =>
        Assert.Equal(400 - Chrome.WindowFrameThreeD, Window("WindowStyle=\"ThreeDBorderWindow\"").Px);

    [Theory]
    [InlineData("WindowStyle=\"None\" ResizeMode=\"NoResize\"")]
    [InlineData("WindowStyle=\"None\" ResizeMode=\"CanMinimize\"")]
    [InlineData("WindowStyle=\"None\" AllowsTransparency=\"True\"")]
    public void Frameless_windows_use_the_full_width(string attributes) =>
        Assert.Equal(400, Window(attributes).Px);

    [Fact]
    public void A_borderless_but_resizable_window_keeps_its_resize_frame() =>
        Assert.Equal(400 - Chrome.WindowFrame, Window("WindowStyle=\"None\"").Px);

    [Fact]
    public void WindowChrome_lets_the_content_fill_the_window()
    {
        var w = Resolve($"""
            <Window {Ns} xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework" Width="400">
              <shell:WindowChrome.WindowChrome><shell:WindowChrome/></shell:WindowChrome.WindowChrome>
              <TextBlock x:Name="probe" Text="hi"/>
            </Window>
            """);
        Assert.Equal(400, w.Px);
    }

    [Fact]
    public void WindowChrome_set_by_a_style_removes_the_frame()
    {
        // 實查 Playnite：WindowChrome 寫在 StandardWindowStyle 裡
        var w = Resolve($"""
            <Window {Ns} xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework" Width="400">
              <Window.Style>
                <Style TargetType="Window">
                  <Setter Property="WindowChrome.WindowChrome"><Setter.Value><shell:WindowChrome CaptionHeight="25"/></Setter.Value></Setter>
                </Style>
              </Window.Style>
              <TextBlock x:Name="probe" Text="hi"/>
            </Window>
            """);
        Assert.Equal(400, w.Px);
    }

    [Fact]
    public void A_project_window_subclass_has_the_standard_frame()
    {
        // ⚠ 實查 ScreenToGif：<n:ExWindow x:Class=…> 繼承自 Window、沒有 WindowChrome。
        //   第一版只對 WPF 內建的 Window 扣邊框。
        var w = Resolve($"""
            <c:ExWindow {Ns} xmlns:c="clr-namespace:App.Controls" x:Class="App.Main" Width="400">
              <c:ExWindow.Template>
                <ControlTemplate TargetType="c:ExWindow"><Border><ContentPresenter/></Border></ControlTemplate>
              </c:ExWindow.Template>
              <TextBlock x:Name="probe" Text="hi"/>
            </c:ExWindow>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(400 - Chrome.WindowFrame, w.Px);
    }

    [Fact]
    public void An_unreadable_frame_setting_is_not_guessed()
    {
        var w = Window("ResizeMode=\"{Binding Mode}\"");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("frame", w.Reason);
    }

    [Fact]
    public void SizeToContent_with_only_MaxWidth_is_bounded_by_MaxWidth()
    {
        var w = Resolve($"<Window {Ns} MaxWidth=\"400\" SizeToContent=\"Width\"><TextBlock x:Name=\"probe\"/></Window>");
        Assert.Equal(400 - Chrome.WindowFrame, w.Px);
    }
}

public class BoundsAreCapsTests
{
    private const string TwoColumns =
        "<Grid.ColumnDefinitions><ColumnDefinition Width=\"100\"/><ColumnDefinition/></Grid.ColumnDefinitions>";

    [Theory]
    [InlineData("MaxWidth=\"300\"")]
    [InlineData("Width=\"300\"")]
    public void An_inner_bound_wider_than_its_container_does_not_win(string attribute)
    {
        // WPF 實測：兩種寫法在 100 寬的欄裡都只看得到 100。第一版遇到第一個上界就停，回 300。
        var w = In($"<Grid Width=\"400\">{TwoColumns}<TextBlock x:Name=\"probe\" {attribute} Text=\"hi\"/></Grid>");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(100, w.Px);
    }

    [Fact]
    public void An_inner_MaxWidth_narrower_than_its_container_wins() =>
        Assert.Equal(150, In("<Border Width=\"400\"><TextBlock x:Name=\"probe\" MaxWidth=\"150\"/></Border>").Px);

    [Fact]
    public void MaxWidth_alone_is_a_bound_when_nothing_outside_is()
    {
        var w = In("<StackPanel><TextBlock x:Name=\"probe\" MaxWidth=\"150\"/></StackPanel>");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(150, w.Px);
    }

    private const string Unsolvable =
        "<Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>";

    [Fact]
    public void An_explicit_Width_survives_an_unresolvable_container()
    {
        // 作者明確決定了那個框的大小；外層算不出來時仍然採用它
        var w = In($"<Grid Width=\"400\">{Unsolvable}<Button x:Name=\"probe\" Width=\"100\" Content=\"x\"/></Grid>", measurer: false);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(96, w.Px);
    }

    [Fact]
    public void A_MaxWidth_does_not_survive_an_unresolvable_container()
    {
        // MaxWidth 只是天花板：真正的寬度來自外層，而外層算不出來
        var w = In($"<Grid Width=\"400\">{Unsolvable}<TextBlock x:Name=\"probe\" MaxWidth=\"100\"/></Grid>", measurer: false);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
    }
}

public class UnreadableIsNotUnsetTests
{
    [Fact]
    public void A_bound_Width_blocks_instead_of_being_skipped()
    {
        // ⚠ 第一版把它當成沒寫，跳過去用外層 800 當答案
        var w = In("<Border Width=\"800\"><Border Width=\"{Binding W}\"><TextBlock x:Name=\"probe\"/></Border></Border>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("Width", w.Reason);
    }

    [Theory]
    [InlineData("Margin")]
    [InlineData("Padding")]
    public void A_bound_inset_blocks_instead_of_counting_as_zero(string property)
    {
        var w = In($"<Border Width=\"300\"><Border {property}=\"{{Binding M}}\"><TextBlock x:Name=\"probe\"/></Border></Border>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains(property, w.Reason);
    }

    [Fact]
    public void An_unreadable_inset_does_not_matter_when_there_is_no_bound_at_all()
    {
        // 擋路者上面沒有上界 → 答案仍然是「查不到上界」，不是「量不到」
        var w = In("<StackPanel Margin=\"{Binding M}\"><TextBlock x:Name=\"probe\"/></StackPanel>");
        Assert.Equal(WidthStatus.NoDeclaredLimit, w.Status);
    }

    [Fact]
    public void A_zero_width_panel_is_a_collapsed_panel_not_a_missing_bound()
    {
        var w = In("<Border Width=\"300\"><Grid Width=\"0\"><TextBlock x:Name=\"probe\"/></Grid></Border>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("collapsed", w.Reason);
    }

    [Fact]
    public void A_LayoutTransform_blocks()
    {
        var w = In("<Border Width=\"300\"><StackPanel LayoutTransform=\"{StaticResource Zoom}\"><TextBlock x:Name=\"probe\"/></StackPanel></Border>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("LayoutTransform", w.Reason);
    }
}

public class UnknownContainerTests
{
    private const string Card = "<Border Width=\"300\"><ui:Card><TextBlock x:Name=\"probe\"/></ui:Card></Border>";

    [Fact]
    public void A_third_party_container_is_not_a_transparent_box()
    {
        // ⚠ 第一版把表裡沒有的元素當成內距 0 的透明容器
        var w = In(Card);
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("ui:Card", w.Reason);
    }

    [Fact]
    public void Declaring_its_chrome_unblocks_it() =>
        Assert.Equal(284, In(Card, chrome: new Dictionary<string, double> { ["Card"] = 16 }).Px);

    [Fact]
    public void A_third_party_container_with_no_bound_above_is_just_unbounded() =>
        Assert.Equal(WidthStatus.NoDeclaredLimit, In("<ui:Card><TextBlock x:Name=\"probe\"/></ui:Card>").Status);

    [Fact]
    public void An_unmodelled_property_element_blocks()
    {
        var w = In("<Border Width=\"300\"><ui:Card><ui:Card.Footer><TextBlock x:Name=\"probe\"/></ui:Card.Footer></ui:Card></Border>",
            chrome: new Dictionary<string, double> { ["Card"] = 16 });
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("Card.Footer", w.Reason);
    }
}

public class ListTests
{
    private static AvailableWidth List(string tag, string attributes, string item) =>
        In($"<{tag} Width=\"300\" {attributes}>{item}</{tag}>");

    private const string Item = "<ListBoxItem x:Name=\"probe\" Content=\"Option\"/>";
    private const string NoHorizontal = "ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"";
    private const string NoVertical = "ScrollViewer.VerticalScrollBarVisibility=\"Disabled\"";

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    public void Lists_scroll_horizontally_by_default(string tag)
    {
        // ⚠ WPF 實測：項目比清單寬時出現水平捲軸，不是被裁。第一版把清單寬度當成硬上界。
        var w = List(tag, "", Item);
        Assert.Equal(WidthStatus.NoDeclaredLimit, w.Status);
        Assert.Contains("scrolls horizontally", w.Reason);
    }

    [Fact]
    public void Disabling_horizontal_scrolling_makes_the_width_bind()
    {
        // 300 − ListBox 4 − 假設在場的垂直捲軸 − ListBoxItem 10
        var w = List("ListBox", NoHorizontal, Item);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300 - 4 - Chrome.ScrollBarWidth - 10, w.Px, 3);
        Assert.True(w.AssumedScrollBar);
        Assert.Contains("assuming a visible vertical scroll bar", w.Reason);
    }

    [Fact]
    public void A_disabled_vertical_bar_is_not_deducted()
    {
        var w = List("ListBox", $"{NoHorizontal} {NoVertical}", Item);
        Assert.Equal(300 - 4 - 10, w.Px, 3);
        Assert.False(w.AssumedScrollBar);
    }

    [Fact]
    public void Text_inside_a_list_item_gets_the_same_width() =>
        Assert.Equal(300 - 4 - 10,
            List("ListBox", $"{NoHorizontal} {NoVertical}", "<ListBoxItem><TextBlock x:Name=\"probe\"/></ListBoxItem>").Px, 3);

    [Fact]
    public void A_plain_ItemsControl_has_no_scroll_viewer_and_no_chrome() =>
        Assert.Equal(300, List("ItemsControl", "", "<TextBlock x:Name=\"probe\"/>").Px);

    [Fact]
    public void A_custom_ItemsPanel_is_not_guessed()
    {
        var w = List("ItemsControl", "",
            "<ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation=\"Horizontal\"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>" +
            "<TextBlock x:Name=\"probe\"/>");
        Assert.Equal(WidthStatus.NeedsItems, w.Status);
    }

    [Fact]
    public void DataGrid_cells_are_sized_by_columns() =>
        Assert.Equal(WidthStatus.NeedsItems, List("DataGrid", "", "<TextBlock x:Name=\"probe\"/>").Status);

    [Fact]
    public void A_TreeView_scrolls_horizontally_by_default() =>
        Assert.Equal(WidthStatus.NoDeclaredLimit,
            List("TreeView", "", "<TreeViewItem x:Name=\"probe\" Header=\"x\"/>").Status);

    [Fact]
    public void An_unreadable_scroll_setting_blocks()
    {
        var w = List("ListBox", "ScrollViewer.HorizontalScrollBarVisibility=\"{Binding H}\"", Item);
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
    }

    [Fact]
    public void A_closed_ComboBox_shows_the_item_without_the_item_container()
    {
        // 關起來的選取框把項目的內容直接畫在 ComboBox 上，ComboBoxItem 的內距不在場
        var w = In("<ComboBox Width=\"200\"><ComboBoxItem x:Name=\"probe\" Content=\"x\"/></ComboBox>");
        Assert.Equal(200 - 27.8, w.Px, 3);
    }
}

public class SharedPanelTests
{
    private static AvailableWidth Row(string children) =>
        In($"<Border Width=\"300\"><StackPanel Orientation=\"Horizontal\">{children}</StackPanel></Border>");

    private const string Text = "<TextBlock x:Name=\"probe\" TextWrapping=\"Wrap\" Text=\"AAAA\"/>";

    [Fact]
    public void A_horizontal_stack_shares_its_width_among_siblings()
    {
        // ⚠ 第一版把水平 StackPanel 當成透明容器：每個子元素都以為自己有整個 300
        var w = Row("<Button Width=\"100\"/>" + Text);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(200, w.Px);
    }

    [Fact]
    public void Siblings_after_the_text_count_too_in_a_budget()
    {
        // WPF 不會裁文字，而是把後面的按鈕推出畫面。預算要讓整排都留在畫面裡。
        Assert.Equal(200, Row(Text + "<Button Width=\"100\"/>").Px);
    }

    private static AvailableWidth Checked(string xaml)
    {
        var doc = System.Xml.Linq.XDocument.Parse($"<Grid {Ns}>{xaml}</Grid>");
        var styles = new XamlFit.Core.Xaml.StyleIndex();
        styles.Add(doc);
        var resolver = new WidthResolver(Chrome, styles, new ElementMeasurer(Fonts(), styles, Chrome, MonoFamily, MonoSize))
        {
            Sharing = RowSharing.Preceding,
        };
        return resolver.Resolve(Probe(doc));
    }

    [Fact]
    public void A_check_blames_the_sibling_that_is_actually_pushed_out()
    {
        // ⚠ CorpusRender 實測 Playnite：WPF 依文件順序排，前面的拿到它要的寬度，後面的被擠掉。
        //   檢查時照整排算，會把前面那些（其實完整顯示）也報成溢出。
        const string row = "<Border Width=\"300\"><StackPanel Orientation=\"Horizontal\">{0}</StackPanel></Border>";
        Assert.Equal(300, Checked(string.Format(row, Text + "<Button Width=\"100\"/>")).Px);
        Assert.Equal(200, Checked(string.Format(row, "<Button Width=\"100\"/>" + Text)).Px);
    }

    [Fact]
    public void A_sibling_pushed_completely_out_has_zero_width_not_an_unknown_one()
    {
        // ⚠ 前面的兄弟已經佔滿整排時，第一版回「內距比上界還大，算不出來」——
        //   而那正是「完全看不見」的情形（CorpusRender：Playnite 有 43 筆因此漏報）。
        var w = Checked("<Border Width=\"300\"><StackPanel Orientation=\"Horizontal\"><Button Width=\"350\"/><TextBlock x:Name=\"probe\" Text=\"AA\"/></StackPanel></Border>");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(0, w.Px);
        Assert.True(w.IsMeasurable);
    }

    [Fact]
    public void A_dock_panel_in_a_check_only_counts_children_that_come_first()
    {
        const string dock = "<DockPanel Width=\"300\" LastChildFill=\"False\">{0}</DockPanel>";
        Assert.Equal(300, Checked(string.Format(dock, "<TextBlock x:Name=\"probe\"/><Button DockPanel.Dock=\"Right\" Width=\"100\"/>")).Px);
        Assert.Equal(200, Checked(string.Format(dock, "<Button DockPanel.Dock=\"Right\" Width=\"100\"/><TextBlock x:Name=\"probe\"/>")).Px);
    }

    [Fact]
    public void A_sibling_label_is_measured()
    {
        // "AAAA" = 4 × 5 px，加右邊 8 的 Margin
        Assert.Equal(300 - 20 - 8, Row("<TextBlock Text=\"AAAA\" Margin=\"0,0,8,0\"/>" + Text).Px);
    }

    [Fact]
    public void Wrapping_does_not_happen_in_a_horizontal_stack()
    {
        // WPF 實測：子元素用無限寬量，Wrap 不會換行，文字一路畫出去
        Assert.True(Row(Text).WrapDefeated);
    }

    // ⚠ 回報 1（ZeroCel CanvasView.xaml:1171）：TextBlock 自己有 Width="120"、TextWrapping="Wrap"，放在水平 StackPanel 裡，
    //   第一版照樣報「換行失效」、判成溢出 72 px。WPF 實測（Measure(∞,∞)）它折成兩行、寬 120：
    //   MeasureCore 先把可用寬度夾到元素自己的 Width／MaxWidth，父層給無限寬也一樣。LayoutOracle「own width in a horizontal stack」。
    [Theory]
    [InlineData("<TextBlock x:Name=\"probe\" Width=\"120\" TextWrapping=\"Wrap\" Text=\"AAAA\"/>")]
    [InlineData("<TextBlock x:Name=\"probe\" MaxWidth=\"120\" TextWrapping=\"Wrap\" Text=\"AAAA\"/>")]
    [InlineData("<Border Width=\"120\"><TextBlock x:Name=\"probe\" TextWrapping=\"Wrap\" Text=\"AAAA\"/></Border>")]
    public void An_own_width_keeps_wrapping_in_a_horizontal_stack(string child)
    {
        var w = Row("<Button Width=\"100\"/>" + child);
        Assert.Equal(120, w.Px);
        Assert.False(w.WrapDefeated);
        Assert.Null(w.WrapPx);   // 換行寬度就是看得見的寬度
    }

    [Fact]
    public void An_own_width_wider_than_the_row_wraps_at_its_own_width_but_is_clipped()
    {
        // Width="400" 放在 300 寬、前面已經有 100 的排裡：照 400 換行，看得見 200
        var w = Row("<Button Width=\"100\"/><TextBlock x:Name=\"probe\" Width=\"400\" TextWrapping=\"Wrap\" Text=\"AAAA\"/>");
        Assert.Equal(200, w.Px);
        Assert.False(w.WrapDefeated);
        Assert.Equal(400, w.WrapPx);
    }

    [Fact]
    public void A_width_above_the_horizontal_stack_does_not_rescue_wrapping()
    {
        // 上界在 StackPanel 之上（Border 300）：StackPanel 仍然用無限寬量它的子元素
        Assert.True(Row("<Button Width=\"100\"/>" + Text).WrapDefeated);
    }

    [Fact]
    public void A_wrap_panel_item_width_is_the_wrapping_width()
    {
        var w = In("<Border Width=\"300\"><WrapPanel ItemWidth=\"120\">" + Text + "</WrapPanel></Border>");
        Assert.Equal(120, w.Px);
        Assert.False(w.WrapDefeated);
    }

    [Fact]
    public void An_unmeasurable_sibling_blocks()
    {
        var w = Row("<Image Source=\"x.png\"/>" + Text);
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("Image", w.Reason);
    }

    [Fact]
    public void A_collapsed_sibling_takes_no_space() =>
        Assert.Equal(300, Row("<Button Width=\"100\" Visibility=\"Collapsed\"/>" + Text).Px);

    [Fact]
    public void A_vertical_stack_gives_each_child_the_full_width()
    {
        var w = In("<Border Width=\"300\"><StackPanel><Button Width=\"100\"/><TextBlock x:Name=\"probe\"/></StackPanel></Border>");
        Assert.Equal(300, w.Px);
        Assert.False(w.WrapDefeated);
    }

    private static AvailableWidth Dock(string children, string attributes = "") =>
        In($"<DockPanel Width=\"300\" {attributes}>{children}</DockPanel>");

    [Fact]
    public void A_docked_sibling_before_the_text_takes_its_width() =>
        Assert.Equal(200, Dock("<Button Width=\"100\"/><TextBlock x:Name=\"probe\"/>").Px);

    [Fact]
    public void A_top_docked_child_is_only_affected_by_what_comes_before_it()
    {
        Assert.Equal(200, Dock("<Button Width=\"100\"/><TextBlock x:Name=\"probe\" DockPanel.Dock=\"Top\"/><Border/>").Px);
        Assert.Equal(300, Dock("<TextBlock x:Name=\"probe\" DockPanel.Dock=\"Top\"/><Button Width=\"100\"/><Border/>").Px);
    }

    [Fact]
    public void The_last_child_fills_and_claims_nothing() =>
        Assert.Equal(300, Dock("<TextBlock x:Name=\"probe\"/><Button/>").Px);

    [Fact]
    public void A_right_docked_sibling_is_reserved_even_when_it_comes_later() =>
        Assert.Equal(200, Dock("<TextBlock x:Name=\"probe\"/><Button DockPanel.Dock=\"Right\" Width=\"100\"/><Border/>").Px);

    [Theory]
    [InlineData("Columns=\"3\"", 3, 200)]
    [InlineData("", 5, 200)]            // ⌈√5⌉ = 3 欄
    [InlineData("Rows=\"2\"", 5, 200)]  // ⌈5 ÷ 2⌉ = 3 欄
    [InlineData("", 4, 300)]            // ⌈√4⌉ = 2 欄
    public void UniformGrid_divides_its_width_evenly(string attributes, int children, double expected)
    {
        var rest = string.Concat(Enumerable.Repeat("<Border/>", children - 1));
        var w = In($"<UniformGrid Width=\"600\" {attributes}><TextBlock x:Name=\"probe\"/>{rest}</UniformGrid>");
        Assert.Equal(expected, w.Px);
    }

    [Fact]
    public void UniformGrid_with_an_unknown_column_count_blocks() =>
        Assert.Equal(WidthStatus.Unmeasurable,
            In("<UniformGrid Width=\"600\" Columns=\"{Binding N}\"><TextBlock x:Name=\"probe\"/></UniformGrid>").Status);

    [Theory]
    [InlineData("Canvas")]
    [InlineData("Viewbox")]
    public void Panels_that_do_not_clip_have_no_bound(string tag) =>
        Assert.Equal(WidthStatus.NoDeclaredLimit,
            In($"<Border Width=\"300\"><{tag}><TextBlock x:Name=\"probe\"/></{tag}></Border>").Status);

    [Fact]
    public void WrapPanel_ItemWidth_is_the_bound() =>
        Assert.Equal(120, In("<WrapPanel Width=\"300\" ItemWidth=\"120\"><TextBlock x:Name=\"probe\"/></WrapPanel>").Px);

    [Fact]
    public void A_BulletDecorator_gives_the_content_what_the_bullet_leaves()
    {
        var w = In("""
            <Border Width="200">
              <BulletDecorator>
                <BulletDecorator.Bullet><Border Width="18"/></BulletDecorator.Bullet>
                <TextBlock x:Name="probe"/>
              </BulletDecorator>
            </Border>
            """);
        Assert.Equal(182, w.Px);
    }
}

public class HeaderAndContentTests
{
    [Fact]
    public void Tab_content_lives_in_the_TabControl_not_in_the_TabItem()
    {
        // TabItem 自己的 Width／Padding／Margin 描述的是標籤，跟內容無關
        var w = In("<TabControl Width=\"400\"><TabItem Header=\"A\" Padding=\"30,2\" Margin=\"7,0\" Width=\"90\"><TextBlock x:Name=\"probe\"/></TabItem></TabControl>");
        Assert.Equal(394, w.Px);
    }

    [Fact]
    public void Tab_headers_size_to_their_content() =>
        Assert.Equal(WidthStatus.NoDeclaredLimit,
            In("<TabControl Width=\"400\"><TabItem x:Name=\"probe\" Header=\"A\"/></TabControl>").Status);

    [Fact]
    public void A_side_tab_strip_takes_the_widest_tab() =>
        // 預設範本：標題 5 + Padding 12 + 標籤列邊距 2；內容區 6（LayoutOracle「default tabs」實測）。更多見 SideTabStripTests
        Assert.Equal(400 - 19 - 6,
            In("<TabControl Width=\"400\" TabStripPlacement=\"Left\"><TabItem Header=\"A\"><TextBlock x:Name=\"probe\"/></TabItem></TabControl>").Px, 3);

    [Theory]
    [InlineData("GroupBox", 291, 288)]
    [InlineData("Expander", 273, 298)]
    public void The_header_has_different_chrome_from_the_content(string tag, double header, double content)
    {
        // 標題是屬性
        Assert.Equal(header, In($"<{tag} x:Name=\"probe\" Width=\"300\" Header=\"H\"/>").Px);
        // 標題是屬性元素裡的文字
        Assert.Equal(header, In($"<{tag} Width=\"300\"><{tag}.Header><TextBlock x:Name=\"probe\"/></{tag}.Header></{tag}>").Px);
        // 內容
        Assert.Equal(content, In($"<{tag} Width=\"300\" Header=\"H\"><TextBlock x:Name=\"probe\"/></{tag}>").Px);
    }

    [Theory]
    [InlineData("<Label x:Name=\"probe\" Width=\"300\" Padding=\"0\" Content=\"x\"/>", 300)]
    [InlineData("<Label x:Name=\"probe\" Width=\"300\" Content=\"x\"/>", 290)]
    [InlineData("<Button x:Name=\"probe\" Width=\"300\" Padding=\"20,0\" Content=\"x\"/>", 258)]
    [InlineData("<Button x:Name=\"probe\" Width=\"300\" BorderThickness=\"5\" Content=\"x\"/>", 288)]
    [InlineData("<ContentControl x:Name=\"probe\" Width=\"300\" Padding=\"20,0\" Content=\"x\"/>", 300)]
    [InlineData("<CheckBox x:Name=\"probe\" Width=\"300\" Padding=\"20,0\" Content=\"x\"/>", 244.025)]
    public void An_explicit_Padding_replaces_the_default_instead_of_adding_to_it(string xaml, double expected)
    {
        // ⚠ 第一版把明寫的值加在整份內距上面：<Label Padding="0"> 被算成還有 10 的內距
        Assert.Equal(expected, In(xaml).Px, 3);
    }

    [Fact]
    public void GroupBox_content_ignores_BorderThickness() =>
        Assert.Equal(288, In("<GroupBox Width=\"300\" BorderThickness=\"5\" Header=\"H\"><TextBlock x:Name=\"probe\"/></GroupBox>").Px);

    private static AvailableWidth Column(string column) =>
        In($"<DataGrid Width=\"500\"><DataGrid.Columns>{column}</DataGrid.Columns></DataGrid>");

    [Fact]
    public void A_column_header_is_bounded_by_the_column_not_the_grid() =>
        Assert.Equal(140, Column("<DataGridTextColumn x:Name=\"probe\" Header=\"Name\" Width=\"150\"/>").Px);

    [Fact]
    public void A_column_with_no_width_sizes_to_its_header() =>
        Assert.Equal(WidthStatus.NoDeclaredLimit, Column("<DataGridTextColumn x:Name=\"probe\" Header=\"Name\"/>").Status);

    [Fact]
    public void A_star_column_depends_on_the_others() =>
        Assert.Equal(WidthStatus.Unmeasurable, Column("<DataGridTextColumn x:Name=\"probe\" Header=\"Name\" Width=\"*\"/>").Status);

    [Fact]
    public void A_restyled_column_header_is_not_guessed() =>
        Assert.Equal(WidthStatus.Unmeasurable,
            Column("<DataGridTextColumn x:Name=\"probe\" Header=\"Name\" Width=\"150\" HeaderStyle=\"{StaticResource H}\"/>").Status);

    private static AvailableWidth StyledColumn(string setters) =>
        In($"<DataGrid Width=\"500\"><DataGrid.Resources><Style TargetType=\"DataGridColumnHeader\">{setters}</Style></DataGrid.Resources>" +
           "<DataGrid.Columns><DataGridTextColumn x:Name=\"probe\" Header=\"Name\" Width=\"150\"/></DataGrid.Columns></DataGrid>");

    // LayoutOracle 實測（150 寬的欄）：Padding 全 0 時範本自己用 5,0,5,0；否則照左右和。BorderThickness 不影響寬度。
    [Theory]
    [InlineData("<Setter Property=\"Padding\" Value=\"8,7\"/><Setter Property=\"BorderThickness\" Value=\"0,0,0,1\"/>", 134)]   // QuillNest 的寫法
    [InlineData("<Setter Property=\"Padding\" Value=\"4,0\"/>", 142)]
    [InlineData("<Setter Property=\"Padding\" Value=\"1,0,30,0\"/>", 119)]
    [InlineData("<Setter Property=\"Padding\" Value=\"0\"/>", 140)]
    [InlineData("<Setter Property=\"Padding\" Value=\"0,7\"/>", 150)]
    [InlineData("<Setter Property=\"BorderThickness\" Value=\"6,0\"/>", 140)]
    [InlineData("<Setter Property=\"FontWeight\" Value=\"SemiBold\"/>", 140)]
    public void A_project_header_style_that_keeps_the_template_has_known_chrome(string setters, double expected)
    {
        // ⚠ 第一版只要有隱含標題樣式就放棄：QuillNest 的 14 個欄標題全部量不到
        var w = StyledColumn(setters);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(expected, w.Px);
    }

    [Theory]
    [InlineData("<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"DataGridColumnHeader\"><ContentPresenter/></ControlTemplate></Setter.Value></Setter>")]
    [InlineData("<Setter Property=\"Margin\" Value=\"10,0\"/>")]
    [InlineData("<Setter Property=\"Padding\" Value=\"{DynamicResource P}\"/>")]
    public void A_project_header_style_that_changes_more_than_padding_is_not_guessed(string setters) =>
        Assert.Equal(WidthStatus.Unmeasurable, StyledColumn(setters).Status);

    [Fact]
    public void GridViewColumn_header_chrome_differs_from_DataGrid() =>
        Assert.Equal(146, In("<ListView Width=\"500\"><ListView.View><GridView><GridViewColumn x:Name=\"probe\" Header=\"Name\" Width=\"150\"/></GridView></ListView.View></ListView>").Px);

    [Fact]
    public void Text_in_a_ToolTip_property_is_popup_content() =>
        Assert.Equal(WidthStatus.NoDeclaredLimit,
            In("<Button Width=\"80\" Content=\"x\"><Button.ToolTip><TextBlock x:Name=\"probe\"/></Button.ToolTip></Button>").Status);

    [Fact]
    public void Items_drawn_through_an_ItemTemplate_are_not_measured_as_written()
    {
        // ⚠ CorpusRender 實測 ScreenToGif：範本 FontSize="14"，項目元素自己是 12 —— 照項目去量，21 筆誤報
        const string template = "<ComboBox.ItemTemplate><DataTemplate><TextBlock FontSize=\"14\" Text=\"{Binding Text}\"/></DataTemplate></ComboBox.ItemTemplate>";
        Assert.Equal(WidthStatus.InTemplate,
            In($"<ComboBox Width=\"200\">{template}<TextBlock x:Name=\"probe\" Text=\"A\"/></ComboBox>").Status);
        // 項目本身就是容器時，ItemTemplate 不套在它身上
        Assert.Equal(WidthStatus.Known,
            In($"<ComboBox Width=\"200\">{template}<ComboBoxItem x:Name=\"probe\" Content=\"A\"/></ComboBox>").Status);
        Assert.Equal(WidthStatus.Known,
            In("<ComboBox Width=\"200\"><TextBlock x:Name=\"probe\" Text=\"A\"/></ComboBox>").Status);
    }

    [Fact]
    public void Text_in_Resources_is_placed_wherever_it_is_used() =>
        Assert.Equal(WidthStatus.InTemplate,
            In("<Border Width=\"300\"><Border.Resources><TextBlock x:Key=\"k\" x:Name=\"probe\"/></Border.Resources></Border>").Status);
}

public class ExternalThemeTests
{
    private static string Page(string body) => $"<Grid {Ns}>{body}</Grid>";

    [Fact]
    public void An_external_theme_makes_the_builtin_chrome_untrustworthy()
    {
        // MaterialDesign 的 Button 內距是 32 不是 4，而且樣式在別的組件裡 —— 我們看不到
        var w = Resolve(Page("<Button x:Name=\"probe\" Width=\"100\" Content=\"x\"/>"), theme: "MaterialDesignThemes.Wpf");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Contains("MaterialDesignThemes.Wpf", w.Reason);
    }

    [Fact]
    public void Elements_without_a_template_are_unaffected() =>
        Assert.Equal(100, Resolve(Page("<TextBlock x:Name=\"probe\" Width=\"100\"/>"), theme: "T").Px);

    [Fact]
    public void A_declared_chrome_is_trusted_under_an_external_theme() =>
        Assert.Equal(68, Resolve(Page("<Button x:Name=\"probe\" Width=\"100\" Content=\"x\"/>"),
            theme: "T", chrome: new Dictionary<string, double> { ["Button"] = 32 }).Px);
}
