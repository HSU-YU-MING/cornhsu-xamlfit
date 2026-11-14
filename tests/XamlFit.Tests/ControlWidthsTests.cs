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
