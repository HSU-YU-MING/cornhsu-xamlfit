using XamlFit.Core.Fonts;

namespace XamlFit.Tests;

/// <summary>
/// 字距依文字系統與語言系統、組合符號、WPF 每 100 個字一段、依語言換字形（WpfOracle --scripts 對照俄、烏、保、塞、希臘、土耳其、越南文找到的）。
/// </summary>
public class ScriptAndLanguageTests
{
    private static string S(params int[] cps) => string.Concat(cps.Select(char.ConvertFromUtf32));

    private static KerningLookup Lookup(Dictionary<long, int> pairs, params string[] systems) =>
        new() { Subtables = [new KerningPairs { Pairs = pairs }], Systems = systems.ToHashSet() };

    private static FontMetrics Face(KerningTable? kern = null, Dictionary<string, IReadOnlyDictionary<int, int>>? languageGlyphs = null) => new()
    {
        Family = "Test", UnitsPerEm = 1000, DefaultAdvance = 500,
        Ranges = [new AdvanceRange(0x20, 0x7E, 500), new AdvanceRange(0x00C0, 0x024F, 500), new AdvanceRange(0x0300, 0x036F, 0),
                  new AdvanceRange(0x0370, 0x03FF, 500), new AdvanceRange(0x0400, 0x04FF, 500), new AdvanceRange(0x0600, 0x06FF, 500)],
        Kerning = kern,
        LanguageGlyphs = languageGlyphs,
    };

    private static double W(FontMetrics face, string text, string? language = null) => TextMeasure.Measure(text, [face], 10, language: language).WidthPx;

    [Fact]
    public void Kerning_across_a_change_of_script_only_widens()
    {
        // 希臘文後面的「 A」：空白在希臘文那段、A 在拉丁文那段，WPF 不套「空白–A」的字距
        var face = Face(new KerningTable { Lookups = [Lookup(new() { [KerningPairs.Key(' ', 'A')] = -100 })] });
        Assert.Equal(9.0, W(face, " A"), 3);
        Assert.Equal(15.0, W(face, S(0x03B9, ' ', 'A')), 3);
        Assert.Equal(15.0, W(face, S(0x0438, ' ', 'A')), 3);   // 西里爾文也是
    }

    [Fact]
    public void Only_the_lookups_of_the_run_s_script_apply()
    {
        // Calibri：阿拉伯文的 lookup 裡有「" ,」的字距，拉丁文（或只有共用字的一段）不套
        var kern = new KerningTable
        {
            Systems = new HashSet<string> { "latn", "arab", "latn/TRK" },
            Lookups =
            [
                Lookup(new() { [KerningPairs.Key('T', 'a')] = -100 }, "latn"),
                Lookup(new() { [KerningPairs.Key('"', ',')] = -100 }, "arab"),
            ],
        };
        var face = Face(kern);
        Assert.Equal(10.0, W(face, "\","), 3);
        Assert.Equal(9.0, W(face, "Ta"), 3);
        // 土耳其文的語言系統沒有掛這個 lookup：不套（Segoe UI 的「Ta」「Te」）
        Assert.Equal(10.0, W(face, "Ta", "tr-TR"), 3);
        // 舊的度量檔（沒有系統資料）照舊全部套
        var old = Face(new KerningTable { Lookups = [new KerningLookup { Subtables = [new KerningPairs { Pairs = new Dictionary<long, int> { [KerningPairs.Key('"', ',')] = -100 } }] }] });
        Assert.Equal(9.0, W(old, "\","), 3);
    }

    [Fact]
    public void A_letter_the_language_draws_differently_takes_the_wider_glyph_and_does_not_kern_narrower()
    {
        // Arial 的土耳其文把 i 換成另一個字形，「Ti」的字距就沒了
        var kern = new KerningTable { Lookups = [new KerningLookup { Subtables = [new KerningPairs { Pairs = new Dictionary<long, int> { [KerningPairs.Key('T', 'i')] = -100 } }] }] };
        var face = Face(kern, new() { ["TRK"] = new Dictionary<int, int> { ['i'] = 520 } });
        Assert.Equal(9.0, W(face, "Ti", "en-US"), 3);
        Assert.Equal(10.2, W(face, "Ti", "tr-TR"), 3);
    }

    [Fact]
    public void Combining_marks_take_no_width_and_the_composed_form_is_also_measured()
    {
        // 「T + e + ̂」：WPF 組成 T + ê，字距是 T–ê 的（沒有），不是 T–e 的
        var kern = new KerningTable { Lookups = [new KerningLookup { Subtables = [new KerningPairs { Pairs = new Dictionary<long, int> { [KerningPairs.Key('T', 'e')] = -100 } }] }] };
        var face = Face(kern);
        Assert.Equal(10.0, W(face, S('T', 'e', 0x0302)), 3);
        Assert.Equal(5.0, W(face, S('x', 0x0323)), 3);
        // 字型裡沒有這個符號：WPF 畫缺字方框，量不到
        var noMarks = new FontMetrics { Family = "NoMarks", UnitsPerEm = 1000, DefaultAdvance = 500, Ranges = [new AdvanceRange(0x20, 0x7E, 500)] };
        Assert.False(TextMeasure.Measure(S('x', 0x031B), [noMarks], 10).IsReliable);
    }

    [Fact]
    public void A_letter_followed_by_a_mark_takes_the_wider_glyph_the_font_composes()
    {
        // Arial 的「ע + ִ」WPF 畫成另一個 ע（1096，原本 1084）
        var face = new FontMetrics
        {
            Family = "Hebr", UnitsPerEm = 1000, DefaultAdvance = 500,
            Ranges = [new AdvanceRange(0x0591, 0x05C7, 0), new AdvanceRange(0x05D0, 0x05EA, 500)],
            MarkedWidths = new Dictionary<int, int> { [0x05E2] = 520 },
        };
        Assert.Equal(10.0, TextMeasure.Measure(S(0x05E2, 0x05D1), [face], 10).WidthPx, 3);
        Assert.Equal(10.2, TextMeasure.Measure(S(0x05E2, 0x05B4, 0x05D1), [face], 10).WidthPx, 3);
    }

    [Fact]
    public void Arabic_letters_joined_across_two_fonts_cannot_be_measured()
    {
        // Dubai 沒有 ؿ，後備字型補：WPF 每支字型各排一段，兩邊接不起來
        var forms = new Dictionary<int, int[]> { [0x0628] = [400, 300, 200, 350], [0x063F] = [400, 300, 200, 350] };
        var shapes = new ArabicShapes { Forms = forms, Pairs = new Dictionary<long, int>(), PairLetters = new HashSet<int> { 0x0628, 0x063F } };
        FontMetrics F(string name, int cp) => new()
        {
            Family = name, UnitsPerEm = 1000, DefaultAdvance = 500, Ranges = [new AdvanceRange(cp, cp, 400)],
            Arabic = new Dictionary<string, ArabicShapes> { ["dflt"] = shapes },
        };
        var one = F("One", 0x0628);
        var two = F("Two", 0x063F);
        Assert.True(TextMeasure.Measure(S(0x0628, 0x0628), [one], 10).IsReliable);
        Assert.False(TextMeasure.Measure(S(0x0628, 0x063F), [one, two], 10).IsReliable);
    }

    [Fact]
    public void Kerning_does_not_cross_wpf_s_hundred_character_chunks()
    {
        var face = Face(new KerningTable { Lookups = [new KerningLookup { Subtables = [new KerningPairs { Pairs = new Dictionary<long, int> { [KerningPairs.Key('A', 'V')] = -100 } }] }] });
        // 第 99 個字是 A、第 100 個是 V：不套
        Assert.Equal(101 * 5.0, W(face, new string('x', 99) + "AV"), 3);
        Assert.Equal(101 * 5.0 - 1, W(face, new string('x', 98) + "AVx"), 3);
    }

    [Fact]
    public void Scripts_that_need_shaping_and_are_not_modelled_cannot_be_measured()
    {
        var face = new FontMetrics { Family = "Any", UnitsPerEm = 1000, DefaultAdvance = 500, Ranges = [new AdvanceRange(0x20, 0x1FFFF, 500)] };
        Assert.False(TextMeasure.Measure(S(0xA98F, 0xA9BC), [face], 10).IsReliable);    // 爪哇文
        Assert.False(TextMeasure.Measure(S(0x1E900, 0x1E901), [face], 10).IsReliable);  // 阿德拉姆文
        Assert.False(TextMeasure.Measure(S(0x1100, 0x1161), [face], 10).IsReliable);    // 韓文組合字母
        Assert.True(TextMeasure.Measure(S(0x12A0, 0x121B), [face], 10).IsReliable);     // 衣索比亞文：加起來（WPF 對照過，不會偏窄）
    }

    [Fact]
    public void The_systems_and_language_glyphs_survive_the_metrics_file()
    {
        var kern = new KerningTable
        {
            Systems = new HashSet<string> { "latn", "latn/TRK" },
            Lookups = [Lookup(new() { [KerningPairs.Key('T', 'a')] = -100 }, "latn")],
        };
        var face = Face(kern, new() { ["TRK"] = new Dictionary<int, int> { ['i'] = 520 } });
        var back = MetricsFile.Parse(MetricsFile.FromMetrics([face], "test", new DateOnly(2026, 11, 17)).ToJson()).ToMetrics().Single();
        Assert.Equal(kern.Systems, back.Kerning!.Systems);
        Assert.Equal(["latn"], back.Kerning.Lookups[0].Systems!);
        Assert.Equal(520, back.LanguageGlyphs!["TRK"]['i']);
        var marked = new FontMetrics { Family = "M", UnitsPerEm = 1000, DefaultAdvance = 500, Ranges = [new AdvanceRange(0x20, 0x7E, 500)], MarkedWidths = new Dictionary<int, int> { ['e'] = 560 } };
        Assert.Equal(560, MetricsFile.Parse(MetricsFile.FromMetrics([marked], "test", new DateOnly(2026, 11, 17)).ToJson()).ToMetrics().Single().MarkedWidths!['e']);
        Assert.Equal(10.0, W(back, "Ta", "tr-TR"), 3);
    }
}
