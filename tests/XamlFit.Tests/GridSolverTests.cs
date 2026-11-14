using System.Xml.Linq;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.OpenType;
using XamlFit.Core.Xaml;

namespace XamlFit.Tests;

public class GridSolverTests
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static readonly ChromeTable Chrome = ChromeTable.Builtin();

    // 合成字型：'A'=600、'B'=700、unitsPerEm=1000 → 字級 10 時 "AB" = 13 px
    private static FontResolver Fonts() =>
        new([FontMetricsExtractor.FromBytes(SyntheticFont.Build("Test Sans")).Single()]);

    private static AvailableWidth Resolve(string body, bool withMeasurer = true)
    {
        var doc = XDocument.Parse($"<Root {Ns} FontFamily=\"Test Sans\" FontSize=\"10\">{body}</Root>",
            LoadOptions.SetLineInfo);
        var styles = new StyleIndex();
        styles.Add(doc);

        IElementMeasurer? measurer = withMeasurer
            ? new ElementMeasurer(Fonts(), styles, Chrome, "Test Sans", 10)
            : null;

        var probe = doc.Descendants().First(e => e.Attribute(XName.Get("Name",
            "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "probe");

        return new WidthResolver(Chrome, styles, measurer).Resolve(probe);
    }

    [Fact]
    public void Single_star_column_gets_the_whole_grid()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="0" Text="hi"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(500, w.Px);
        Assert.Contains("solved Grid", w.BoundSource);
    }

    [Fact]
    public void Star_shares_what_the_fixed_columns_leave()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="1" Text="hi"/>
            </Grid>
            """);
        Assert.Equal(380, w.Px);
    }

    [Fact]
    public void Star_weights_are_proportional()
    {
        var w = Resolve("""
            <Grid Width="600">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="2*"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="1" Text="hi"/>
            </Grid>
            """);
        Assert.Equal(400, w.Px);
    }

    [Fact]
    public void Auto_column_is_sized_by_its_widest_child()
    {
        // 'A'=600、'B'=700 單位，unitsPerEm=1000，字級 10 → "AB" = 13 px，"A" = 6 px
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock Grid.Column="0" Text="A"/>
              <TextBlock Grid.Column="0" Text="AB"/>
              <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(487, w.Px, 3);   // 500 - 13
    }

    [Fact]
    public void Auto_column_counts_the_child_margin()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock Grid.Column="0" Text="AB" Margin="5"/>
              <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
            </Grid>
            """);
        Assert.Equal(477, w.Px, 3);   // 500 - (13 + 10)
    }

    [Fact]
    public void An_empty_auto_column_is_zero_not_unknown()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(500, w.Px);
    }

    [Fact]
    public void MinWidth_and_MaxWidth_clamp_the_column()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" MaxWidth="200"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="0" Text="A"/>
            </Grid>
            """);
        Assert.Equal(200, w.Px);
    }

    [Fact]
    public void Nested_grids_resolve()
    {
        var w = Resolve("""
            <Grid Width="600">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="200"/>
              </Grid.ColumnDefinitions>
              <Grid Grid.Column="0">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="100"/>
                  <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
              </Grid>
            </Grid>
            """);
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(300, w.Px);   // 外層星號 400 → 內層扣掉 100
    }

    [Fact]
    public void ColumnSpan_sums_the_solved_columns()
    {
        var w = Resolve("""
            <Grid Width="600">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="200"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="0" Grid.ColumnSpan="2" Text="A"/>
            </Grid>
            """);
        Assert.Equal(400, w.Px);
    }

    // ===== 算不出來就要放棄，不准猜 =====

    [Fact]
    public void An_unmeasurable_child_in_an_auto_column_abandons_the_solve()
    {
        // 自訂控制項的期望寬度靜態不可知
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <MyCustomChart Grid.Column="0"/>
              <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
        Assert.Contains("not statically knowable", w.Reason);
    }

    [Fact]
    public void A_data_bound_text_child_in_an_auto_column_abandons_the_solve()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock Grid.Column="0" Text="{Binding Name}"/>
              <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
    }

    [Fact]
    public void A_spanning_child_over_an_auto_column_abandons_the_solve()
    {
        // WPF 會把跨欄子元素超出的寬度分攤到跨到的欄，規則複雜 —— 不猜
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock Grid.Column="0" Grid.ColumnSpan="2" Text="AB"/>
              <TextBlock x:Name="probe" Grid.Column="2" Text="A"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
        Assert.Contains("spanning child", w.Reason);
    }

    [Fact]
    public void SharedSizeGroup_abandons_the_solve()
    {
        // 欄寬由整個 scope 的其他 Grid 共同決定，看不到那些
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" SharedSizeGroup="L"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock Grid.Column="0" Text="A"/>
              <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
        Assert.Contains("SharedSizeGroup", w.Reason);
    }

    [Fact]
    public void A_star_column_in_an_unbounded_grid_has_no_declared_limit_rather_than_being_unsolved()
    {
        // Grid 自己沒有寬度上界 → 它的星號欄也**真的**沒有上界。
        // 這不是「解不開」而是「沒有上界」—— NoDeclaredLimit 比 NeedsGrid 準確。
        var w = Resolve("""
            <StackPanel>
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <TextBlock x:Name="probe" Grid.Column="0" Text="A"/>
              </Grid>
            </StackPanel>
            """);
        Assert.Equal(WidthStatus.NoDeclaredLimit, w.Status);
    }

    [Fact]
    public void An_unsolvable_auto_column_under_a_bounded_grid_is_NeedsGrid()
    {
        // 這一筆才是「有上界但解不開」—— 跟上面那筆是不同的事
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <MyCustomChart Grid.Column="0"/>
              <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
    }

    [Fact]
    public void A_bound_column_width_abandons_the_solve()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="{Binding W}"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
        Assert.Contains("cannot read", w.Reason);
    }

    [Fact]
    public void Without_a_measurer_the_behaviour_is_the_old_NeedsGrid()
    {
        var w = Resolve("""
            <Grid Width="500">
              <Grid.ColumnDefinitions><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
              <TextBlock x:Name="probe" Grid.Column="0" Text="hi"/>
            </Grid>
            """, withMeasurer: false);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
    }
}
