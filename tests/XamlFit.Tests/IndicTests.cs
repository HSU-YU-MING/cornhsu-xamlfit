using XamlFit.Core.Fonts;

namespace XamlFit.Tests;

/// <summary>
/// 天城文、坦米爾文：一個字素叢集一次量（匯出時用字型的 GSUB／GPOS 算好），叢集相加。
/// WpfOracle --scripts（Nirmala UI）：馬拉地文真實譯文 290 句、全部叢集 5,926 個、隨機字 507 個，偏窄最多 0.02%；
/// 坦米爾文 300 句、599 個叢集、44 個隨機字，偏窄最多 0.11%（叢集之間的字距，約 0.1 px）。
/// 這裡用假字型驗證拼法。
/// </summary>
public class IndicTests
{
    private const int Ka = 0x0915, Ssa = 0x0937, Ra = 0x0930, Virama = 0x094D, AaSign = 0x093E, ISign = 0x093F, Anusvara = 0x0902;

    private static FontMetrics Face(Dictionary<string, IReadOnlyDictionary<string, int>>? indic) => new()
    {
        Family = "Deva",
        UnitsPerEm = 1000,
        DefaultAdvance = 500,
        Ranges = [new AdvanceRange(0x20, 0x7E, 500), new AdvanceRange(0x0900, 0x097F, 400)],
        Indic = indic,
    };

    private static Measurement M(FontMetrics face, string text, string? language = null) =>
        TextMeasure.Measure(text, [face], 10, language: language);

    private static string S(params int[] cps) => string.Concat(cps.Select(char.ConvertFromUtf32));

    [Fact]
    public void Clusters_match_the_syllable_shape()
    {
        var s = IndicClusters.Devanagari;
        Assert.Equal(1, IndicClusters.Match([Ka], 0, s));
        Assert.Equal(2, IndicClusters.Match([Ka, AaSign], 0, s));
        Assert.Equal(3, IndicClusters.Match([Ka, AaSign, Anusvara], 0, s));
        Assert.Equal(2, IndicClusters.Match([Ka, Virama, Ssa], 0, s));       // 子音 + virama 是一個叢集，下一個子音另起
        Assert.Equal(0, IndicClusters.Match([AaSign], 0, s));                // 單獨一個母音符號：對不上
        Assert.Equal(0, IndicClusters.Match([Ka, Virama, Anusvara], 0, s));  // virama 後面接鼻化符號：WPF 畫虛線圓圈
    }

    [Fact]
    public void A_cluster_takes_its_exported_change()
    {
        var face = Face(new() { ["deva"] = new Dictionary<string, int> { [IndicClusters.Key([Ka, ISign])] = -100 } });
        var m = M(face, S(Ka, ISign, Ka));
        Assert.True(m.IsReliable);
        Assert.Equal((400 + 400 - 100 + 400) / 100.0, m.WidthPx, 3);
    }

    [Fact]
    public void A_consonant_with_virama_before_a_consonant_takes_the_conjunct_change()
    {
        var data = new Dictionary<string, int> { [IndicClusters.ConjunctKey(Ka, Ssa)] = -300 };
        var face = Face(new() { ["deva"] = data });
        Assert.Equal((400 + 400 - 300 + 400) / 100.0, M(face, S(Ka, Virama, Ssa)).WidthPx, 3);   // क्ष：連寫
        Assert.Equal((400 + 400) / 100.0, M(face, S(Ka, Virama)).WidthPx, 3);                    // 字尾的 क्：看得見的 virama
    }

    [Fact]
    public void Punctuation_after_a_cluster_takes_the_largest_kerning_seen()
    {
        var face = Face(new() { ["deva"] = new Dictionary<string, int> { [IndicClusters.TrailingKey] = 50 } });
        Assert.Equal((400 + 50 + 500) / 100.0, M(face, S(Ka) + ":").WidthPx, 3);
        Assert.Equal((400 + 400) / 100.0, M(face, S(Ka, Ka)).WidthPx, 3);
    }

    [Fact]
    public void The_language_picks_the_language_system()
    {
        // Nirmala UI 的馬拉地文 ल／श 字形不同（MAR）
        var face = Face(new()
        {
            ["deva"] = new Dictionary<string, int>(),
            ["deva/MAR"] = new Dictionary<string, int> { [IndicClusters.Key([Ka])] = 40 },
        });
        Assert.Equal(4.0, M(face, S(Ka)).WidthPx, 3);
        Assert.Equal(4.4, M(face, S(Ka), "mr-IN").WidthPx, 3);
    }

    [Fact]
    public void Without_data_or_with_an_unknown_sequence_it_cannot_be_measured()
    {
        Assert.False(M(Face(null), S(Ka)).IsReliable);
        var face = Face(new() { ["deva"] = new Dictionary<string, int>() });
        Assert.False(M(face, S(AaSign)).IsReliable);
        Assert.True(M(face, S(Ka, 0x0964)).IsReliable);   // danda 照 advance
        Assert.False(M(face, S(0x0995)).IsReliable);      // 孟加拉文：這支假字型沒有它的資料
        Assert.Null(IndicClusters.Of(0x1820));            // 蒙古文：沒做
    }

    [Fact]
    public void Indic_data_survives_the_metrics_file()
    {
        var face = Face(new()
        {
            ["deva"] = new Dictionary<string, int> { [IndicClusters.Key([Ka, ISign])] = -100, [IndicClusters.ConjunctKey(Ka, Ssa)] = -300, [IndicClusters.TrailingKey] = 20 },
            ["deva/MAR"] = new Dictionary<string, int>(),
        });
        var json = MetricsFile.FromMetrics([face], "test", new DateOnly(2026, 11, 15)).ToJson();
        var back = MetricsFile.Parse(json).ToMetrics().Single().Indic!;
        Assert.Equal(-100, back["deva"][IndicClusters.Key([Ka, ISign])]);
        Assert.Equal(-300, back["deva"][IndicClusters.ConjunctKey(Ka, Ssa)]);
        Assert.Equal(20, back["deva"][IndicClusters.TrailingKey]);
        Assert.Empty(back["deva/MAR"]);
    }

    [Fact]
    public void In_interacting_scripts_each_cluster_boundary_takes_the_widest_change_seen()
    {
        // 古吉拉特文：鼻化符號會把下一個字推開（WPF 套字距時跳過符號）
        const int Da = 0x0AA6, Dda = 0x0AA1, Anusvara = 0x0A82;
        var face = new FontMetrics
        {
            Family = "Gujr", UnitsPerEm = 1000, DefaultAdvance = 500,
            Ranges = [new AdvanceRange(0x0A80, 0x0AFF, 400)],
            Indic = new Dictionary<string, IReadOnlyDictionary<string, int>> { ["gujr"] = new Dictionary<string, int> { [IndicClusters.BoundaryKey([Da, Anusvara])] = 80 } },
        };
        Assert.True(IndicClusters.Gujarati.Interacting);
        Assert.Equal((400 + 400 + 80 + 400) / 100.0, M(face, S(Da, Anusvara, Dda)).WidthPx, 3);
        Assert.Equal((400 + 400) / 100.0, M(face, S(Da, Anusvara)).WidthPx, 3);   // 後面沒有叢集：不加
    }

    [Fact]
    public void Tamil_two_part_vowels_are_split_and_pre_base_signs_drawn_first()
    {
        var t = IndicClusters.Tamil;
        Assert.Equal([0x0BC6, 0x0B95, 0x0BBE], IndicClusters.VisualOrder(t, [0x0B95, 0x0BCA]));   // கொ → ெ + க + ா
        Assert.Equal([0x093F, Ka], IndicClusters.VisualOrder(IndicClusters.Devanagari, [Ka, ISign]));
    }

    [Fact]
    public void Myanmar_clusters_take_medials_vowel_sequences_and_finals()
    {
        var m = IndicClusters.Myanmar;
        // ကြော် = က + ြ + ေ + ာ + ်（介音 + 一串母音）
        Assert.Equal(5, IndicClusters.Match([0x1000, 0x103C, 0x1031, 0x102C, 0x103A], 0, m));
        // ကို့ = က + ိ + ု + ့
        Assert.Equal(4, IndicClusters.Match([0x1000, 0x102D, 0x102F, 0x1037], 0, m));
        // က္က：疊字符號是 virama，下一個子音另起一個叢集
        Assert.Equal(2, IndicClusters.Match([0x1000, 0x1039, 0x1000], 0, m));
        // ေ 在最左、ြ 第二、子音第三
        Assert.Equal([0x1031, 0x103C, 0x1000, 0x102C], IndicClusters.VisualOrder(m, [0x1000, 0x103C, 0x1031, 0x102C]));
    }

    [Fact]
    public void Tibetan_stacks_take_up_to_two_subjoined_letters()
    {
        var t = IndicClusters.Tibetan;
        // སྒྲིཾ = ས + ྒ + ྲ + ི + ཾ
        Assert.Equal(5, IndicClusters.Match([0x0F66, 0x0F92, 0x0FB2, 0x0F72, 0x0F7E], 0, t));
        // 第二個疊字只能是 ya、ra、la、wa：ས + ྒ + ྒ 只認前兩個
        Assert.Equal(2, IndicClusters.Match([0x0F66, 0x0F92, 0x0F92], 0, t));
        // tsheg 不在叢集裡
        Assert.Equal(1, IndicClusters.Match([0x0F40, 0x0F0B], 0, t));
    }

    [Fact]
    public void Khmer_register_shifters_may_precede_the_vowel()
    {
        var k = IndicClusters.Khmer;
        Assert.Equal(3, IndicClusters.Match([0x1794, 0x17C9, 0x17B8], 0, k));   // ប៊ី
        Assert.Equal(2, IndicClusters.Match([0x1780, 0x17D2, 0x1781], 0, k));   // ក្ខ：coeng 是 virama
        Assert.Equal([0x17C1, 0x1780, 0x17BE], IndicClusters.VisualOrder(k, [0x1780, 0x17BE]));   // កើ → េ + ក + ើ
    }

    [Fact]
    public void Tibetan_is_measured_by_its_stack_with_the_widest_vowel_and_any_mark()
    {
        var face = new FontMetrics
        {
            Family = "Tibt",
            UnitsPerEm = 1000,
            DefaultAdvance = 500,
            Ranges = [new AdvanceRange(0x0F00, 0x0F3F, 0), new AdvanceRange(0x0F40, 0x0F6C, 600), new AdvanceRange(0x0F6D, 0x0FFF, 0)],
            Indic = new Dictionary<string, IReadOnlyDictionary<string, int>> { ["tibt"] = new Dictionary<string, int> { [IndicClusters.Key([0x0F66, 0x0F92])] = 50, [IndicClusters.ModifierKey] = 10 } },
        };
        // སྒི：疊字 ས + ྒ 記 50（各種母音裡最寬的）；ཾ 再加 10
        Assert.Equal((600 + 50) / 100.0, TextMeasure.Measure(S(0x0F66, 0x0F92, 0x0F72), [face], 10).WidthPx, 3);
        Assert.Equal((600 + 50 + 10) / 100.0, TextMeasure.Measure(S(0x0F66, 0x0F92, 0x0F72, 0x0F7E), [face], 10).WidthPx, 3);
        Assert.Equal(600 / 100.0, TextMeasure.Measure(S(0x0F40), [face], 10).WidthPx, 3);   // 沒有記：照 advance

        var back = MetricsFile.Parse(MetricsFile.FromMetrics([face], "test", new DateOnly(2026, 11, 16)).ToJson()).ToMetrics().Single().Indic!;
        Assert.Equal(10, back["tibt"][IndicClusters.ModifierKey]);
        Assert.Equal(50, back["tibt"][IndicClusters.Key([0x0F66, 0x0F92])]);
    }

    [Fact]
    public void Myanmar_is_defined_but_not_measured()
    {
        Assert.Null(IndicClusters.Of(0x1000));   // Myanmar Text 的符號會依子音挪位置，這支匯出引擎算不出來
        Assert.DoesNotContain(IndicClusters.Myanmar, IndicClusters.All);
    }

    [Fact]
    public void Every_exported_cluster_is_matched_whole()
    {
        // 匯出的叢集量的時候要整個對得上（對成兩段就會用錯鍵）；度量檔裡沒有的鍵會被當成「寬度 = advance 相加」
        foreach (var script in new[] { IndicClusters.Myanmar, IndicClusters.Khmer, IndicClusters.Devanagari })
            foreach (var cluster in IndicClusters.Enumerate(script, _ => true))
                Assert.Equal(cluster.Length, IndicClusters.Match(cluster, 0, script));
        Assert.Contains(IndicClusters.Enumerate(IndicClusters.Myanmar, _ => true), c => IndicClusters.Key(c) == IndicClusters.Key([0x1000, 0x103C, 0x1031, 0x102C, 0x103A]));
    }
}
