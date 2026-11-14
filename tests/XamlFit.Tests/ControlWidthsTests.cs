using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 設定檔的 <c>controlWidths</c>：寬度要到執行期才知道的控制項（ScreenToGif 的數值框、程式碼填項目的下拉選單）
/// 由專案宣告它最寬多寬。工具自己從不猜這個數字 —— 沒宣告就照舊是「不知道」。
/// </summary>
public class ControlWidthsTests
{
    private const string Lib = "xmlns:n=\"clr-namespace:App.Controls\"";

    private static AvailableWidth Probe(string neighbour, Dictionary<string, double>? widths = null)
    {
        var xaml = $"<Window {Ns} {Lib} Width=\"400\"><Grid Width=\"300\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition/></Grid.ColumnDefinitions>" +
                   $"{neighbour}<TextBlock Grid.Column=\"1\" Text=\"Label\"/></Grid></Window>";
        var dir = TempProject(("Main.xaml", xaml));
        try
        {
            var scan = new XamlScanner(Chrome, new ScanOptions
            {
                DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize,
                ControlWidths = widths ?? new Dictionary<string, double>(),
            }, Fonts()).Scan(dir);
            return scan.Sites.Single(s => s.Text == "Label").Width;
        }
        finally { Cleanup(dir); }
    }

    private const string NumberBox = "<n:IntegerUpDown Margin=\"10,5\" Value=\"{Binding Width}\"/>";

    [Fact]
    public void Without_a_declaration_the_width_stays_unknown() =>
        Assert.NotEqual(WidthStatus.Known, Probe(NumberBox).Status);

    [Fact]
    public void A_declared_width_counts_with_the_margin() =>
        Assert.Equal(300 - 100 - 20, Probe(NumberBox, new() { ["IntegerUpDown"] = 100 }).Px, 3);

    [Fact]
    public void MinWidth_still_wins_and_an_explicit_width_comes_first()
    {
        var widths = new Dictionary<string, double> { ["IntegerUpDown"] = 60 };
        Assert.Equal(300 - 80, Probe("<n:IntegerUpDown MinWidth=\"80\"/>", widths).Px, 3);
        Assert.Equal(300 - 40, Probe("<n:IntegerUpDown Width=\"40\"/>", widths).Px, 3);
    }

    [Theory]
    [InlineData("<ScrollViewer x:Name=\"Panel\" VerticalScrollBarVisibility=\"Auto\"><TextBlock x:Name=\"probe\" Text=\"x\"/></ScrollViewer>")]
    [InlineData("<ListBox x:Name=\"Panel\" ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"><ListBoxItem><TextBlock x:Name=\"probe\" Text=\"x\"/></ListBoxItem></ListBox>")]
    public void A_declared_hidden_scroll_bar_takes_no_width(string body)
    {
        // 設定檔的 hiddenScrollBars：這個 ScrollViewer／清單的垂直捲軸實際上不會出現（ScreenToGif 編輯器側邊面板，28 筆誤報）
        AvailableWidth Scan(IReadOnlyCollection<string> hidden)
        {
            var dir = TempProject(("Main.xaml", $"<Window {Ns} Width=\"400\"><Grid Width=\"300\">{body}</Grid></Window>"));
            try
            {
                return new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, HiddenScrollBars = hidden }, Fonts())
                    .Scan(dir).Sites.Single(s => s.Text == "x").Width;
            }
            finally { Cleanup(dir); }
        }

        var assumed = Scan([]);
        var hidden = Scan(["Panel"]);
        Assert.True(assumed.AssumedScrollBar);
        Assert.False(hidden.AssumedScrollBar);
        Assert.Equal(assumed.Px + Chrome.ScrollBarWidth, hidden.Px, 3);
        Assert.Equal(assumed.Px, Scan(["Other"]).Px, 3);   // 別的名字不影響
    }

    [Theory]
    [InlineData("""{ "hiddenScrollBars": ["ActionScrollViewer"] }""", true)]
    [InlineData("""{ "hiddenScrollBars": ["x:Panel"] }""", false)]
    [InlineData("""{ "controlWidths": { "IntegerUpDown": 100, "ComboBox": 180 } }""", true)]
    [InlineData("""{ "controlWidths": { "n:IntegerUpDown": 100 } }""", false)]
    [InlineData("""{ "controlWidths": { "IntegerUpDown": 0 } }""", false)]
    [InlineData("""{ "controlWidths": { "IntegerUpDown": 5000 } }""", false)]
    public void The_declaration_is_validated(string json, bool ok)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xamlfit-cw-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            var (_, problems) = FitConfig.Load(path);
            Assert.Equal(ok, problems.Count == 0);
        }
        finally { File.Delete(path); }
    }
}
