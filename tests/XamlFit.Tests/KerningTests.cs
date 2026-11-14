using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 字距調整（GPOS 'kern'）。WPF 預設會調：Segoe UI 100 px 的「To」比兩個字分開量窄 9.8 px。
/// WpfOracle 實測：字距多的字串第一版偏寬到 5.7%，調了之後最大 0.26%、平均 0.006%。
/// </summary>
public class KerningTests
{
    private static KerningTable Table(params KerningSubtable[][] lookups) =>
        new() { Lookups = [.. lookups.Select(l => new KerningLookup { Subtables = l })] };

    private static KerningPairs Pairs(params (char A, char B, int D)[] pairs) =>
        new() { Pairs = pairs.ToDictionary(p => KerningPairs.Key(p.A, p.B), p => p.D) };

    [Fact]
    public void The_first_subtable_that_applies_wins_and_lookups_add_up()
    {
        var classes = new KerningClasses
        {
            Coverage = [('A', 'Z')],
            First = [('T', 'T', 1)],
            Second = [('o', 'o', 1)],
            FirstCount = 2,
            SecondCount = 2,
            Cells = new Dictionary<int, int> { [1 * 2 + 1] = -80 },
        };
        var table = Table([Pairs(('T', 'o', -50)), classes], [Pairs(('T', 'o', -10))]);
        Assert.Equal(-60, table.Adjust('T', 'o'));    // 第一個 lookup 用格式 1 的 −50，第二個 lookup 再 −10
        Assert.Equal(0, table.Adjust('A', 'o'));      // 格式 2 適用但格子是 0 —— 而且擋住後面的子表
        Assert.Equal(0, table.Adjust('a', 'o'));      // 不在涵蓋範圍
    }

    private static FontMetrics Face(string family, KerningTable? kern) => Mono(family, kerning: kern);

    [Fact]
    public void Kerning_applies_between_characters_of_the_same_face_only()
    {
        var kern = Table([Pairs(('T', 'o', -100))]);   // −0.1 em
        var lookup = new FontResolver([Face(MonoFamily, kern)]).Resolve(MonoFamily, 400, FontStyleKind.Normal, MonoFamily);
        Assert.Equal(10 - 1, TextMeasure.Measure("To", lookup, 10).WidthPx, 3);   // 5 + 5 − 1
        Assert.Equal(15, TextMeasure.Measure("T o", lookup, 10).WidthPx, 3);       // 中間隔了空格：不調
    }

    [Fact]
    public void Kerning_survives_the_metrics_file()
    {
        var classes = new KerningClasses
        {
            Coverage = [('A', 'Z')],
            First = [('T', 'T', 1)],
            Second = [('a', 'z', 1)],
            FirstCount = 2,
            SecondCount = 2,
            Cells = new Dictionary<int, int> { [3] = -70 },
        };
        var face = Face("K", Table([Pairs(('A', 'V', -60), ('A', 'W', -40))], [classes]));
        var json = MetricsFile.FromMetrics([face], "test", new DateOnly(2026, 11, 15)).ToJson();
        var back = MetricsFile.Parse(json).ToMetrics().Single().Kerning!;
        Assert.Equal(-60, back.Adjust('A', 'V'));
        Assert.Equal(-40, back.Adjust('A', 'W'));
        Assert.Equal(-70, back.Adjust('T', 'e'));
        Assert.Equal(0, back.Adjust('V', 'A'));
    }

    [Theory]
    [InlineData("Typography.Kerning=\"False\"", false)]
    [InlineData("Typography.Kerning=\"{Binding K}\"", false)]
    [InlineData("Typography.Kerning=\"True\"", true)]
    [InlineData("", true)]
    public void Text_with_kerning_turned_off_is_not_measured(string attribute, bool measurable)
    {
        var dir = TempProject(("Main.xaml", $"<Window {Ns}><Border Width=\"300\"><TextBlock {attribute} Text=\"x\"/></Border></Window>"));
        try
        {
            var site = new XamlFit.Core.Xaml.XamlScanner(Chrome, new XamlFit.Core.Xaml.ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize }, Fonts())
                .Scan(dir).Sites.Single();
            Assert.Equal(measurable, site.Width.Status == WidthStatus.Known);
            if (!measurable) Assert.Contains("Kerning", site.Width.Reason);
        }
        finally { Cleanup(dir); }
    }
}
