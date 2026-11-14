using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// Width="0" 的收起面板，展開寬度寫在同一份檔案的動畫裡（ScreenToGif 編輯器的右側面板）。
/// </summary>
public class CollapsedPanelTests
{
    private static AvailableWidth Probe(string storyboards)
    {
        var xaml = $"""
            <Window {Ns} Width="1000">
              <Window.Resources>{storyboards}</Window.Resources>
              <Grid>
                <Grid x:Name="Panel" Width="0" HorizontalAlignment="Right">
                  <TextBlock Text="AAAA"/>
                </Grid>
              </Grid>
            </Window>
            """;
        var dir = TempProject(("Main.xaml", xaml));
        try
        {
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, Fonts()).Scan(dir);
            return scan.Sites.Single().Width;
        }
        finally { Cleanup(dir); }
    }

    private static string Open(string to, string property = "(Grid.Width)") =>
        $"<Storyboard x:Key=\"S{Guid.NewGuid():N}\"><DoubleAnimation Storyboard.TargetName=\"Panel\" Storyboard.TargetProperty=\"{property}\" To=\"{to}\" Duration=\"0:0:0.5\"/></Storyboard>";

    [Fact]
    public void The_storyboard_that_opens_the_panel_gives_its_width()
    {
        var w = Probe(Open("280"));
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(280, w.Px);
        Assert.Contains("storyboard", w.BoundSource);
    }

    [Fact]
    public void Several_opening_widths_take_the_smallest_and_closing_to_zero_is_ignored()
    {
        // ScreenToGif：一般面板 280、另存新檔的延伸面板更寬、關閉時回到 0
        var w = Probe(Open("280") + Open("600") + Open("0"));
        Assert.Equal(280, w.Px);
    }

    [Fact]
    public void A_MaxWidth_animation_counts_too()
    {
        var w = Probe(Open("280") + Open("200", "MaxWidth"));
        Assert.Equal(200, w.Px);
    }

    [Theory]
    [InlineData("<Storyboard x:Key=\"S\"><DoubleAnimation Storyboard.TargetName=\"Panel\" Storyboard.TargetProperty=\"Width\" To=\"{Binding W}\"/></Storyboard>")]
    [InlineData("<Storyboard x:Key=\"S\"><DoubleAnimation Storyboard.TargetName=\"Panel\" Storyboard.TargetProperty=\"Width\" By=\"100\"/></Storyboard>")]
    [InlineData("<Storyboard x:Key=\"S\"><DoubleAnimationUsingKeyFrames Storyboard.TargetName=\"Panel\" Storyboard.TargetProperty=\"Width\"/></Storyboard>")]
    [InlineData("")]
    public void Without_a_literal_opening_width_the_panel_stays_unknown(string storyboards) =>
        Assert.NotEqual(WidthStatus.Known, Probe(storyboards).Status);
}
