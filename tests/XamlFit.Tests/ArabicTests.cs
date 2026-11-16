using XamlFit.Core.Fonts;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 阿拉伯文的寬度：匯出時用字型的 GSUB／GPOS 算好的形狀寬度與相鄰字母的改變量（見 OpenType.ArabicExport）。
/// WpfOracle --scripts 實測（Segoe UI、Arial、Tahoma、Courier New、Microsoft Sans Serif、Times New Roman）：
/// 真實譯文 500 句、隨機字 1000 個、單字母 560 組，偏窄最多 0.05%。
/// 這裡用一支假字型驗證拼法；數字是隨便定的。
/// </summary>
public class ArabicTests
{
    private const int Beh = 0x0628, Meem = 0x0645, YehBarree = 0x06D2;

    private static ArabicShapes Shapes(
        Dictionary<long, int>? pairs = null, Dictionary<long, int>? triples = null, int[]? pairLetters = null) => new()
    {
        Forms = new Dictionary<int, int[]>
        {
            [Beh] = [400, 300, 300, 350],          // 獨立、字首、字中、字尾
            [Meem] = [450, 350, 350, 400],
            [YehBarree] = [600, 600, 600, 500],    // 只接右邊：字首＝獨立、字中＝字尾
        },
        Pairs = pairs ?? [],
        PairLetters = (pairLetters ?? [Beh, Meem, YehBarree]).ToHashSet(),
        Triples = triples ?? [],
        TripleLetters = new HashSet<int> { YehBarree },
    };

    private static FontMetrics Face(ArabicShapes? dflt = null, ArabicShapes? ara = null, KerningTable? kerning = null)
    {
        var arabic = new Dictionary<string, ArabicShapes>();
        if (dflt is not null) arabic["dflt"] = dflt;
        if (ara is not null) arabic["ARA "] = ara;
        return new FontMetrics
        {
            Family = "ArabicTest",
            UnitsPerEm = 1000,
            DefaultAdvance = 500,
            Kerning = kerning,
            Arabic = arabic.Count > 0 ? arabic : null,
            Ranges =
            [
                new AdvanceRange(0x20, 0x7E, 500),
                new AdvanceRange(0x060C, 0x060C, 400),    // 阿拉伯逗號
                new AdvanceRange(0x0621, 0x064A, 400),
                new AdvanceRange(0x064B, 0x0652, 300),    // 附加符號：hmtx 有寬度，WPF 量出來是 0
                new AdvanceRange(0x0660, 0x0669, 700),    // 阿拉伯—印度數字
                new AdvanceRange(0x06D2, 0x06D2, 600),
                new AdvanceRange(0x06F0, 0x06F9, 700),    // 波斯數字
            ],
        };
    }

    private static Measurement Measure(FontMetrics face, string text, string? language = null) =>
        TextMeasure.Measure(text, [face], 10, language: language);

    [Fact]
    public void Letters_take_the_width_of_their_contextual_form()
    {
        var m = Measure(Face(Shapes()), "بب");                 // 字首 300 + 字尾 350
        Assert.True(m.IsReliable);
        Assert.Equal(6.5, m.WidthPx, 3);
        Assert.Equal(4.0, Measure(Face(Shapes()), "ب").WidthPx, 3);   // 獨立
    }

    [Fact]
    public void Adjacent_letters_add_their_pair_change()
    {
        var shapes = Shapes(pairs: new() { [ArabicShapes.Key(Beh, ArabicJoining.Initial, Beh, ArabicJoining.Final)] = -50 });
        Assert.Equal(6.0, Measure(Face(shapes), "بب").WidthPx, 3);
    }

    [Fact]
    public void Letters_without_pair_data_next_to_each_other_cannot_be_measured()
    {
        // 自我檢查移掉的字母（Segoe UI 有 13 個少用字母）：單獨出現可以量，跟別的字母相鄰就量不到
        var face = Face(Shapes(pairLetters: [Beh, YehBarree]));
        Assert.True(Measure(face, "م").IsReliable);
        Assert.False(Measure(face, "بم").IsReliable);
        Assert.True(Measure(face, "ب م").IsReliable);    // 中間隔空白：不相鄰
    }

    [Fact]
    public void Yeh_barree_adds_the_change_that_depends_on_the_two_letters_before_it()
    {
        // Segoe UI 依前面兩個字把 ے 換成不同長度的字形；成對的量不到
        var key = ArabicShapes.Key3(Beh, ArabicJoining.Initial, Beh, ArabicJoining.Medial, YehBarree, ArabicJoining.Final);
        var face = Face(Shapes(triples: new() { [key] = 80 }));
        Assert.Equal(11.8, Measure(face, "ببے").WidthPx, 3);   // 300 + 300 + 500 + 80
        Assert.Equal(8.0, Measure(face, "بے").WidthPx, 3);      // 前面只有一個字：沒有三字改變量（300 + 500）
    }

    [Fact]
    public void A_mark_inside_the_yeh_barree_context_cannot_be_measured()
    {
        // 字型的規則會跳過一部分附加符號，沒模擬 —— 不猜
        Assert.False(Measure(Face(Shapes()), "بِبے").IsReliable);
        Assert.False(Measure(Face(Shapes()), "ببِے").IsReliable);
        Assert.True(Measure(Face(Shapes()), "بِب").IsReliable);
    }

    [Fact]
    public void Arabic_marks_take_no_width()
    {
        // Tahoma 的 U+0654 在 hmtx 有寬度，WPF 量出來是 0
        var m = Measure(Face(Shapes()), "بَ");
        Assert.True(m.IsReliable);
        Assert.Equal(4.0, m.WidthPx, 3);
    }

    [Fact]
    public void Non_joining_letters_and_arabic_punctuation_take_their_advance()
    {
        var m = Measure(Face(Shapes()), "ء،");    // hamza、阿拉伯逗號
        Assert.True(m.IsReliable);
        Assert.Equal(8.0, m.WidthPx, 3);
    }

    [Fact]
    public void Arabic_letters_without_shape_data_cannot_be_measured()
    {
        Assert.False(Measure(Face(), "ب").IsReliable);
    }

    [Theory]
    [InlineData("ar", 14.0)]    // 換成阿拉伯—印度數字（700）比 ASCII（500）寬，取寬的
    [InlineData("fa", 14.0)]    // 波斯數字
    [InlineData(null, 10.0)]    // 不是阿拉伯文的語言、字串裡也沒有阿拉伯字母：不換
    public void Digits_in_an_arabic_context_are_measured_both_ways(string? language, double expected)
    {
        var m = Measure(Face(Shapes()), "12", language);
        Assert.True(m.IsReliable);
        Assert.Equal(expected, m.WidthPx, 3);
    }

    [Theory]
    [InlineData("my-MM", 0x1040)]
    [InlineData("my", 0x1040)]
    [InlineData("dz-BT", 0x0F20)]
    [InlineData("ne-IN", 0x0966)]
    [InlineData("ps-AF", 0x06F0)]
    [InlineData("ur-IN", 0x06F0)]
    [InlineData("pa-Arab-PK", 0x06F0)]
    [InlineData("sd-Arab-PK", 0x06F0)]
    [InlineData("uz-Arab-AF", 0x06F0)]
    [InlineData("ks", 0x06F0)]
    [InlineData("ne-NP", null)]      // 以下 .NET 也列為原生數字（或同一個語言的別的地區），WPF 實測不換
    [InlineData("ur-PK", null)]
    [InlineData("pa-IN", null)]
    [InlineData("ks-Deva", null)]
    [InlineData("uz-Latn-UZ", null)]
    [InlineData("km-KH", null)]
    [InlineData("bn-BD", null)]
    [InlineData("fa-AF", null)]
    [InlineData("en-US", null)]
    [InlineData(null, null)]
    public void Languages_whose_digits_wpf_always_replaces(string? language, int? zero)
    {
        Assert.Equal(zero, TextMeasure.NativeZero(language));
    }

    [Fact]
    public void Digits_are_measured_both_ways_when_the_language_always_replaces_them()
    {
        // 普什圖文：沒有阿拉伯字母也換成波斯數字（700 比 ASCII 500 寬）
        Assert.Equal(14.0, Measure(Face(Shapes()), "12", "ps-AF").WidthPx, 3);
        Assert.Equal(10.0, Measure(Face(Shapes()), "12", "pa-IN").WidthPx, 3);
    }

    [Fact]
    public void Digits_next_to_arabic_letters_are_measured_both_ways_whatever_the_language()
    {
        Assert.Equal(4.0 + 5.0 + 14.0, Measure(Face(Shapes()), "ب 12").WidthPx, 3);
    }

    [Fact]
    public void The_language_picks_the_language_system()
    {
        var dflt = Shapes();
        var ara = Shapes(pairs: new() { [ArabicShapes.Key(Beh, ArabicJoining.Initial, Beh, ArabicJoining.Final)] = -100 });
        var face = Face(dflt, ara);
        Assert.Equal(5.5, Measure(face, "بب", "ar-SA").WidthPx, 3);
        Assert.Equal(6.5, Measure(face, "بب", "fa-IR").WidthPx, 3);    // 沒有 FAR：用預設
    }

    [Fact]
    public void Kerning_next_to_a_neutral_character_in_right_to_left_text_only_widens()
    {
        // Arial 的「L 空白」-76：阿拉伯文句子裡的「URL 」WPF 沒套（空白跟 L 分在不同方向的段落）
        var kern = new KerningTable
        {
            Lookups = [new KerningLookup { Subtables = [new KerningPairs { Pairs = new Dictionary<long, int>
            {
                [KerningPairs.Key('L', ' ')] = -100,
                [KerningPairs.Key('A', 'L')] = -100,
            } }] }],
        };
        var face = Face(Shapes(), kerning: kern);
        Assert.Equal(15.0 - 2.0, Measure(face, "AL ").WidthPx, 3);           // 沒有右到左文字：兩個都套
        Assert.Equal(4.0 + 15.0 - 1.0, Measure(face, "بAL ").WidthPx, 3);    // 只套字母之間的
    }

    [Fact]
    public void Shape_data_survives_the_metrics_file()
    {
        var shapes = Shapes(
            pairs: new() { [ArabicShapes.Key(Beh, 1, Meem, 3)] = -40 },
            triples: new() { [ArabicShapes.Key3(Beh, 1, Meem, 2, YehBarree, 3)] = 70 },
            pairLetters: [Beh, Meem, YehBarree, 0x0627, 0x0629]);
        var json = MetricsFile.FromMetrics([Face(shapes, Shapes())], "test", new DateOnly(2026, 11, 15)).ToJson();
        var back = MetricsFile.Parse(json).ToMetrics().Single().ArabicFor(null)!;

        Assert.Equal(shapes.Forms[Meem], back.Forms[Meem]);
        Assert.Equal(-40, back.Pairs[ArabicShapes.Key(Beh, 1, Meem, 3)]);
        Assert.Equal(70, back.Triples[ArabicShapes.Key3(Beh, 1, Meem, 2, YehBarree, 3)]);
        Assert.Equal(shapes.PairLetters.Order(), back.PairLetters.Order());
        Assert.Equal(new[] { YehBarree }, back.TripleLetters);
    }

    [Fact]
    public void Identical_language_systems_are_written_once()
    {
        // Segoe UI 的 URD 跟預設一模一樣：只寫一次，另一個寫「跟誰一樣」
        var dflt = Shapes(
            pairs: new() { [ArabicShapes.Key(Beh, 1, Meem, 3)] = -40 },
            triples: new() { [ArabicShapes.Key3(Beh, 1, Meem, 2, YehBarree, 3)] = 70 });
        var urd = Shapes(
            pairs: new() { [ArabicShapes.Key(Beh, 1, Meem, 3)] = -40 },
            triples: new() { [ArabicShapes.Key3(Beh, 1, Meem, 2, YehBarree, 3)] = 70 });
        var ara = Shapes(
            pairs: new() { [ArabicShapes.Key(Beh, 1, Meem, 3)] = -55 },
            triples: new() { [ArabicShapes.Key3(Beh, 1, Meem, 2, YehBarree, 3)] = 70 });
        var face = new FontMetrics
        {
            Family = "ArabicTest", UnitsPerEm = 1000, Ranges = [new AdvanceRange(0x0621, 0x06D2, 400)],
            Arabic = new Dictionary<string, ArabicShapes> { ["dflt"] = dflt, ["URD "] = urd, ["ARA "] = ara },
        };
        var json = MetricsFile.FromMetrics([face], "test", new DateOnly(2026, 11, 15)).ToJson();
        Assert.Contains("\"triplesFrom\"", json);
        Assert.Contains("\"pairsFrom\"", json);

        var back = MetricsFile.Parse(json).ToMetrics().Single();
        Assert.Equal(-40, back.ArabicFor("ur")!.Pairs[ArabicShapes.Key(Beh, 1, Meem, 3)]);
        Assert.Equal(-55, back.ArabicFor("ar")!.Pairs[ArabicShapes.Key(Beh, 1, Meem, 3)]);
        Assert.Equal(70, back.ArabicFor("ar")!.Triples[ArabicShapes.Key3(Beh, 1, Meem, 2, YehBarree, 3)]);
        Assert.Equal(dflt.Forms[Meem], back.ArabicFor("ur")!.Forms[Meem]);
    }
}
