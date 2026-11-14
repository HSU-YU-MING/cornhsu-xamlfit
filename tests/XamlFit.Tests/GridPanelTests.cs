using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 使用者宣告的「用屬性產生欄的 Grid 子類別」（Playnite 的 GridEx）。
/// 語意照 Playnite 的 GridEx.cs：欄數 → 全部 Auto；星號欄 → 那幾欄 *；自動排列 → 子元素依序填格、蓋掉自己的 Grid.Column。
/// 等寬測試字型：每個字 5 px。
/// </summary>
public class GridPanelTests
{
    private const string Lib = "xmlns:c=\"clr-namespace:Playnite.Controls\"";

    private static readonly Dictionary<string, GridPanel> GridEx = new()
    {
        ["GridEx"] = new GridPanel("ColumnCount", "StarColumns", "AutoLayoutColumns"),
    };

    private static ScanResult Scan(string body, IReadOnlyDictionary<string, GridPanel>? panels = null)
    {
        var dir = TempProject(("Main.xaml", $"<Window {Ns} {Lib} Width=\"400\"><Border Width=\"300\">{body}</Border></Window>"));
        try
        {
            return new XamlScanner(Chrome, new ScanOptions
            {
                DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, GridPanels = panels ?? GridEx,
            }, Fonts()).Scan(dir);
        }
        finally { Cleanup(dir); }
    }

    private static TextSite Site(ScanResult scan, string text) => scan.Sites.Single(s => s.Text == text);

    [Fact]
    public void The_star_column_gets_what_the_auto_column_leaves()
    {
        // 第 0 欄 Auto（"AAAA" 20 px），第 1 欄 *：300 − 20
        var scan = Scan("""
            <c:GridEx ColumnCount="2" StarColumns="1" AutoLayoutColumns="2">
              <TextBlock Text="AAAA"/>
              <TextBlock Text="BB"/>
            </c:GridEx>
            """);
        Assert.Equal(280, Site(scan, "BB").Width.Px);
    }

    [Fact]
    public void Automatic_placement_overrides_the_childs_own_column()
    {
        // GridEx 每次量版面都重排：寫了 Grid.Column="1" 的第一個子元素還是放在第 0 欄
        var scan = Scan("""
            <c:GridEx ColumnCount="2" StarColumns="0" AutoLayoutColumns="2">
              <TextBlock Text="BB" Grid.Column="1"/>
              <TextBlock Text="AAAA"/>
            </c:GridEx>
            """);
        Assert.Equal(280, Site(scan, "BB").Width.Px);
    }

    [Fact]
    public void A_column_span_takes_several_cells()
    {
        // 第一個子元素跨兩欄，第二、三個從下一列開始。跨過 Auto 欄的子元素讓那一欄的寬度要一起解 ——
        // 工具對手寫的同一個 Grid 也是回「跨欄的子元素影響 Auto 欄」；改寫後的結果要跟它一模一樣
        var scan = Scan("""
            <c:GridEx ColumnCount="2" StarColumns="1" AutoLayoutColumns="2">
              <TextBlock Text="CCCCCC" Grid.ColumnSpan="2"/>
              <TextBlock Text="AAAA"/>
              <TextBlock Text="BB"/>
            </c:GridEx>
            """);
        var bb = Site(scan, "BB").Width;
        Assert.Equal(WidthStatus.NeedsGrid, bb.Status);
        Assert.Contains("column 0 is Auto and a spanning child contributes", bb.Reason);
    }

    [Theory]
    [InlineData("ColumnCount=\"{Binding N}\" AutoLayoutColumns=\"2\"")]
    [InlineData("ColumnCount=\"2\" AutoLayoutColumns=\"2\" StarColumns=\"x\"")]
    public void A_property_that_cannot_be_read_leaves_the_panel_unknown(string attributes)
    {
        var scan = Scan($"<c:GridEx {attributes}><TextBlock Text=\"AAAA\"/><TextBlock Text=\"BB\"/></c:GridEx>");
        Assert.NotEqual(WidthStatus.Known, Site(scan, "BB").Width.Status);
    }

    [Fact]
    public void Without_a_declaration_the_panel_stays_unknown()
    {
        // 不內建任何第三方類別的語意：別的專案的 GridEx 可能完全不一樣
        var scan = Scan("<c:GridEx ColumnCount=\"2\" StarColumns=\"1\" AutoLayoutColumns=\"2\"><TextBlock Text=\"AAAA\"/><TextBlock Text=\"BB\"/></c:GridEx>",
            new Dictionary<string, GridPanel>());
        Assert.NotEqual(WidthStatus.Known, Site(scan, "BB").Width.Status);
    }

    [Theory]
    [InlineData("""{ "gridPanels": { "GridEx": { "columnCount": "ColumnCount", "starColumns": "StarColumns", "autoLayoutColumns": "AutoLayoutColumns" } } }""", true)]
    [InlineData("""{ "gridPanels": { "c:GridEx": { "columnCount": "ColumnCount" } } }""", false)]
    [InlineData("""{ "gridPanels": { "GridEx": { "starColumns": "StarColumns" } } }""", false)]
    [InlineData("""{ "gridPanels": { "GridEx": { "columnCount": "GridEx.ColumnCount" } } }""", false)]
    [InlineData("""{ "gridPanels": { "GridEx": { "columnCount": "ColumnCount", "rows": "RowCount" } } }""", false)]
    public void The_declaration_is_validated(string json, bool ok)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xamlfit-gp-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            var (_, problems) = FitConfig.Load(path);
            Assert.Equal(ok, problems.Count == 0);
        }
        finally { File.Delete(path); }
    }
}
