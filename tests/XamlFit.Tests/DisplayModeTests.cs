using XamlFit.Core.Fonts;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// TextFormattingMode="Display"：字寬是字型 hdmx 表裡那個像素大小的 hinting 寬度（整數像素），字距也四捨五入到整數像素。
/// WpfOracle --display 實測（13,200 組，10 支字型、11 個字級、5 種縮放）：有 hdmx 資料的像素大小，
/// Segoe UI、Arial、Trebuchet MS 每一組都一樣；Tahoma、Microsoft JhengHei UI 只在 12.5 px 這種剛好一半的大小偏寬（安全）。
/// 沒有資料的大小量不到：hinting 能讓一個字多出好幾個像素，沒有上限可估（Yu Gothic UI 的 hinting 旗標說不會改寬度，實測照樣改）。
/// </summary>
public class DisplayModeTests
{
    private static FontMetrics Face(Dictionary<int, IReadOnlyDictionary<int, int>>? hinted, KerningTable? kerning = null) => new()
    {
        Family = "Hinted",
        UnitsPerEm = 1000,
        DefaultAdvance = 500,
        Ranges = [new AdvanceRange(0x20, 0x7E, 550)],   // 線性 0.55 em
        Hinted = hinted,
        Kerning = kerning,
    };

    private static FontLookup Lookup(FontMetrics face) => new FontResolver([face]).Resolve("Hinted", 400, FontStyleKind.Normal, "Hinted");

    [Fact]
    public void Each_character_takes_its_hinted_width_at_the_pixel_size()
    {
        // 10 px：線性 5.5 → 0.5 進位 6；'i' 在 hdmx 裡是 3（差 −3）
        var face = Face(new() { [10] = new Dictionary<int, int> { ['i'] = -3 } });
        var m = TextMeasure.MeasureDisplay("aai", Lookup(face), 10, [1.0]);
        Assert.True(m.IsReliable);
        Assert.Equal(6 + 6 + 3, m.WidthPx, 3);
    }

    [Fact]
    public void Every_display_scaling_is_measured_and_the_widest_wins()
    {
        // 10 px 在 125% 是 12.5 px（12 與 13 都量）：0.55 × 13 = 7.15 → 7，「aa」= 14 像素 = 11.2 DIP；100% 是 12 DIP
        var face = Face(new() { [10] = new Dictionary<int, int>(), [12] = new Dictionary<int, int>(), [13] = new Dictionary<int, int> { ['a'] = 1 } });
        var m = TextMeasure.MeasureDisplay("aa", Lookup(face), 10, [1.0, 1.25]);
        Assert.Equal(Math.Max(12.0, 16 / 1.25), m.WidthPx, 3);   // 125% 時每個字 8 像素
    }

    [Fact]
    public void A_pixel_size_without_hinted_widths_cannot_be_measured()
    {
        var face = Face(new() { [10] = new Dictionary<int, int>() });
        var m = TextMeasure.MeasureDisplay("aa", Lookup(face), 10, [1.0, 1.5]);
        Assert.False(m.IsReliable);
        Assert.Contains("15 px", m.Unsupported);
        Assert.False(TextMeasure.MeasureDisplay("aa", Lookup(Face(null)), 10, [1.0]).IsReliable);
    }

    [Fact]
    public void Kerning_is_rounded_to_whole_pixels()
    {
        var kern = new KerningTable { Lookups = [new KerningLookup { Subtables = [new KerningPairs { Pairs = new Dictionary<long, int> { [KerningPairs.Key('A', 'V')] = -80 } }] }] };
        var face = Face(new() { [10] = new Dictionary<int, int>() }, kern);
        // −80 / 1000 × 10 = −0.8 → −1
        Assert.Equal(6 + 6 - 1, TextMeasure.MeasureDisplay("AV", Lookup(face), 10, [1.0]).WidthPx, 3);
    }

    private static KerningTable Kern(int av) =>
        new() { Lookups = [new KerningLookup { Subtables = [new KerningPairs { Pairs = new Dictionary<long, int> { [KerningPairs.Key('A', 'V')] = av } }] }] };

    [Fact]
    public void Kerning_of_exactly_half_a_pixel_rounds_up()
    {
        // −150 / 1000 × 10 = −1.5：WPF 給 −1（Calibri 32 px 實測），偶數進位會給 −2、拼窄一個像素
        var face = Face(new() { [10] = new Dictionary<int, int>() }, Kern(-150));
        Assert.Equal(6 + 6 - 1, TextMeasure.MeasureDisplay("AV", Lookup(face), 10, [1.0]).WidthPx, 3);
    }

    [Fact]
    public void A_half_pixel_size_measures_both_pixel_sizes_and_keeps_the_wider()
    {
        // 10 px × 125% = 12.5 px：12 px 的 'a' hinting 後寬 2 像素，13 px 照線性
        var face = Face(new() { [12] = new Dictionary<int, int> { ['a'] = 2 }, [13] = new Dictionary<int, int>() });
        Assert.Equal((9 + 9) / 1.25, TextMeasure.MeasureDisplay("aa", Lookup(face), 10, [1.25]).WidthPx, 3);
        // 只有一個有資料就量不到
        Assert.False(TextMeasure.MeasureDisplay("aa", Lookup(Face(new() { [13] = new Dictionary<int, int>() })), 10, [1.25]).IsReliable);
    }

    [Fact]
    public void At_a_half_pixel_size_kerning_may_come_from_the_other_pixel_size()
    {
        // 12.5 px：13 px 的字寬（'A' 8 + 'V' 7）配 12 px 的字距（−120 × 12 / 1000 = −1.44 → −1），不是 13 px 的（−1.56 → −2）
        var face = Face(new() { [12] = new Dictionary<int, int>(), [13] = new Dictionary<int, int> { ['A'] = 1 } }, Kern(-120));
        Assert.Equal((8 + 7 - 1) / 1.25, TextMeasure.MeasureDisplay("AV", Lookup(face), 10, [1.25]).WidthPx, 3);
    }

    [Fact]
    public void Hinted_widths_survive_the_metrics_file()
    {
        var face = Face(new() { [10] = new Dictionary<int, int> { ['i'] = -3, ['j'] = -3, ['m'] = 2 }, [12] = new Dictionary<int, int>() });
        var json = MetricsFile.FromMetrics([face], "test", new DateOnly(2026, 11, 15)).ToJson();
        var back = MetricsFile.Parse(json).ToMetrics().Single();
        Assert.True(back.TryHintedAdvance('i', 10, out var i) && i == 3);
        Assert.True(back.TryHintedAdvance('m', 10, out var m) && m == 8);
        Assert.True(back.TryHintedAdvance('a', 12, out var a) && a == 7);      // 0.55 × 12 = 6.6 → 7
        Assert.False(back.TryHintedAdvance('a', 11, out _));
    }
}
