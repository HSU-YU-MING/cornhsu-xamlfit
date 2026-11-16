using XamlFit.Core.Fonts;

namespace XamlFit.Tests;

/// <summary>
/// 蒙古文：每一段（一個字，或字型連成合字的一對字母）照形狀查匯出時算好的「最寬會多寬」相加。
/// WpfOracle --scripts（Mongolian Baiti）：產生的字三組各 4,000 個平均偏寬約 4%、偏窄最多 0.04%；
/// Windows 地區設定裡的月份、星期等 32 句平均偏寬 5.2%、偏窄最多 0.01%。這裡用假字型驗證拼法。
/// </summary>
public class MongolianTests
{
    private const int A = 0x1820, E = 0x1821, I = 0x1822, Na = 0x1828, Ba = 0x182A, Ga = 0x182D, Fvs1 = 0x180B, Mvs = 0x180E, Nnbsp = 0x202F;

    private static string S(params int[] cps) => string.Concat(cps.Select(char.ConvertFromUtf32));

    [Fact]
    public void Letters_take_their_joining_position()
    {
        var forms = MongolianJoining.FormsOf([Na, A, Na]);
        Assert.Equal(ArabicJoining.Initial, MongolianJoining.Position(forms[0]));
        Assert.Equal(ArabicJoining.Medial, MongolianJoining.Position(forms[1]));
        Assert.Equal(ArabicJoining.Final, MongolianJoining.Position(forms[2]));
        Assert.Equal(ArabicJoining.Isolated, MongolianJoining.Position(MongolianJoining.FormsOf([A])[0]));
        // FVS 是透明的：ᠨ᠋ᠠ 的 ᠨ 還是字首；FVS 寫進 ᠨ 的形狀裡
        var fvs = MongolianJoining.FormsOf([Na, Fvs1, A]);
        Assert.Equal(ArabicJoining.Initial, MongolianJoining.Position(fvs[0]));
        Assert.NotEqual(forms[0], fvs[0]);
        // MVS 不連寫：後面的 ᠠ 是獨立的，但跟一般獨立的 ᠠ 分開記（詞尾的形狀窄很多）
        var mvs = MongolianJoining.FormsOf([Na, Mvs, A]);
        Assert.Equal(ArabicJoining.Isolated, MongolianJoining.Position(mvs[0]));   // MVS 前面的字母不跟它連（字形另外由第 9 位分開記）
        Assert.Equal(ArabicJoining.Isolated, MongolianJoining.Position(mvs[2]));
        Assert.NotEqual(MongolianJoining.FormsOf([A])[0], mvs[2]);
        // 陽性、陰性的字分開記（ᠭ 接陰性母音時是寬的字形）
        Assert.NotEqual(MongolianJoining.FormsOf([Ga, A])[0], MongolianJoining.FormsOf([Ga, E])[0]);
    }

    [Fact]
    public void A_ligature_pair_is_one_segment()
    {
        bool Pair(int a, int b) => a == Ba && b == A;
        Assert.Equal([(0, 2), (2, 1)], MongolianJoining.Segments([Ba, A, Na], Pair));
        Assert.Equal([(0, 1), (1, 1), (2, 1)], MongolianJoining.Segments([Na, A, Ba], Pair));
        Assert.Equal([(0, 1), (1, 1)], MongolianJoining.Segments([Ba, Fvs1], Pair));   // FVS 不是字母
    }

    private static FontMetrics Face(Dictionary<string, int> table) => new()
    {
        Family = "Mong",
        UnitsPerEm = 1000,
        DefaultAdvance = 500,
        Ranges = [new AdvanceRange(0x20, 0x7E, 300), new AdvanceRange(0x1800, 0x18AF, 900), new AdvanceRange(0x202F, 0x202F, 300)],
        Indic = new Dictionary<string, IReadOnlyDictionary<string, int>> { [MongolianJoining.DataKey] = table },
    };

    [Fact]
    public void Width_is_the_sum_of_the_segments_in_the_table()
    {
        int[] word = [Ba, A, Na];
        var forms = MongolianJoining.FormsOf(word);
        var table = new Dictionary<string, int>
        {
            [MongolianJoining.PairKey(Ba, A)] = 1,
            [MongolianJoining.SegmentKey(word, 0, 2, forms)] = 700,   // ᠪᠠ 合字
            [MongolianJoining.SegmentKey(word, 2, 1, forms)] = 600,
        };
        var face = Face(table);
        var m = TextMeasure.Measure(S(word), [face], 10);
        Assert.False(m.ComplexScript);
        Assert.Equal((700 + 600) / 100.0, m.WidthPx, 3);
        // 表裡沒有這種形狀：量不到，不拿 advance 去湊
        Assert.True(TextMeasure.Measure(S(Na, I), [face], 10).ComplexScript);
    }

    [Fact]
    public void A_missing_detailed_key_falls_back_to_the_coarse_one()
    {
        int[] word = [Na, Mvs, A];
        var forms = MongolianJoining.FormsOf(word);
        var table = new Dictionary<string, int>
        {
            [MongolianJoining.SegmentKey(word, 0, 1, forms, coarse: true)] = 500,
            [MongolianJoining.SegmentKey(word, 1, 1, forms)] = 205,                 // MVS 在字中間有寬度
            [MongolianJoining.SegmentKey(word, 2, 1, forms, coarse: true)] = 450,
        };
        var m = TextMeasure.Measure(S(word), [Face(table)], 10);
        Assert.False(m.ComplexScript);
        Assert.Equal((500 + 205 + 450) / 100.0, m.WidthPx, 3);
    }

    [Fact]
    public void The_table_survives_the_metrics_file()
    {
        int[] word = [Ba, A, Nnbsp, E];
        var forms = MongolianJoining.FormsOf(word);
        var table = new Dictionary<string, int>
        {
            [MongolianJoining.PairKey(Ba, A)] = 1,
            [MongolianJoining.SegmentKey(word, 0, 2, forms)] = 700,
            [MongolianJoining.SegmentKey(word, 2, 1, forms)] = 205,
            [MongolianJoining.SegmentKey(word, 3, 1, forms, coarse: true)] = 454,
        };
        var back = MetricsFile.Parse(MetricsFile.FromMetrics([Face(table)], "test", new DateOnly(2026, 11, 16)).ToJson()).ToMetrics().Single();
        Assert.Equal(table, back.Indic![MongolianJoining.DataKey]);
        Assert.Equal((700 + 205 + 454) / 100.0, TextMeasure.Measure(S(word), [back], 10).WidthPx, 3);
    }
}
