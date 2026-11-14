using XamlFit.Core.Fonts;

namespace XamlFit.Core.OpenType;

/// <summary>
/// 匯出時用 <see cref="LayoutEngine"/> 把阿拉伯字母跑過字型的替換規則與 GPOS 的字距，算出兩組**數字**：
/// 1. 每個字母在獨立／字首／字中／字尾四種形狀的寬度（前後接延長線 U+0640 來決定形狀）。
///    寬度一律用「整串的總寬 − 延長線的寬」算，不看字形落在哪個字的名下 —— Segoe UI 的 ۓ 在字尾時會換掉
///    **前一個**字的字形，照字形歸屬去分，寬度會算到別的字頭上（WpfOracle --scripts 實測少了 6 px）。
/// 2. 相鄰兩個字母的寬度改變量（連字、上下文替換、字距），依它們的形狀分開記；
///    同一組在不同上下文算出不同值時取大的（寧可量寬）。
/// 每個語言系統（預設、ARA、FAR、URD）各算一份：WPF 依元素的語言挑語言系統。
/// 度量檔裡只有這些數字，沒有字型的表。
///
/// **自我檢查**：算完之後拿 20000 個隨機字（固定種子）同時用引擎整串跑、用上面這些數字拼。
/// 拼出來比整串跑的窄，表示字型有跨三個字以上的上下文規則（Segoe UI 的點避讓字距、Calibri 6.27 的大量 calt）。
/// 這時把出問題的字裡最少用、出現最多次的字母移出「驗證過的字母」（<see cref="ArabicShapes.PairLetters"/>），再檢查一次，
/// 直到沒有拼窄的字。移掉的字母跟別的字母相鄰時量不到。剩下不到一半的字母，整支字型的阿拉伯文資料不寫，一律量不到。
/// </summary>
internal static class ArabicExport
{
    private static readonly HashSet<string> FormFeatures = ["isol", "init", "medi", "fina"];
    private static readonly HashSet<string> AllFeatures = ["ccmp", "locl", "rlig", "calt", "rclt", "liga", "clig", "mset", "isol", "init", "medi", "fina"];
    private static readonly string[] Languages = ["dflt", "ARA ", "FAR ", "URD "];
    private static readonly string[] FormTags = ["isol", "init", "medi", "fina"];
    private static readonly HashSet<string> PositionFeatures = ["kern", "dist"];

    /// <summary>算成對改變量的字母：阿拉伯字母區塊的基本區（U+0620–06FF）裡會連寫的字母，涵蓋阿拉伯文、波斯文、
    /// 烏爾都文、普什圖文、庫德文、信德文…。補充區（0750–077F）與擴充區（08A0–08FF）不算 —— 用到就量不到。</summary>
    private static bool PairLetter(int cp) => cp is >= 0x0620 and <= 0x06FF;

    /// <summary>常用字母：阿拉伯文基本字母，加波斯文、烏爾都文的常用字母。自我檢查移字母時最後才動它們。</summary>
    private static bool Core(int cp) =>
        cp is (>= 0x0621 and <= 0x063A) or (>= 0x0641 and <= 0x064A) or 0x0671 or 0x0679 or 0x067E or 0x0686 or 0x0688
            or 0x0691 or 0x0698 or 0x06A9 or 0x06AF or 0x06BA or 0x06BE or 0x06C0 or 0x06C1 or 0x06CC or 0x06D2;

    /// <summary>算三字改變量的結尾字母：ے、ۓ（yeh barree）。</summary>
    private static readonly int[] TripleEnds = [0x06D2, 0x06D3];

    private const int SelfCheckWords = 20000;

    public static Dictionary<string, ArabicShapes>? Read(SfntReader r, SortedDictionary<int, int> cmap, int[] advances)
    {
        if (!r.Has("GSUB")) return null;
        try
        {
            var engine = new LayoutEngine(r);
            var positioner = r.Has("GPOS") ? new LayoutEngine(r, "GPOS") : null;
            if (!cmap.ContainsKey(0x0640)) return null;   // 沒有延長線就沒辦法單獨量出每種形狀
            const int tatweel = 0x0640;

            var letters = cmap.Keys.Where(cp => ArabicJoining.IsArabic(cp)
                && ArabicJoining.TypeOf(cp) is ArabicJoining.Type.Dual or ArabicJoining.Type.Right).ToList();
            if (letters.Count == 0) return null;

            var result = new Dictionary<string, ArabicShapes>();
            foreach (var lang in Languages)
            {
                var langSys = engine.LangSys("arab", lang);
                if (langSys < 0) continue;
                var lookups = engine.Lookups(langSys, AllFeatures);
                var posLangSys = positioner?.LangSys("arab", lang) ?? -1;
                if (positioner is not null && posLangSys < 0 && lang != "dflt") posLangSys = positioner.LangSys("arab", "dflt");
                var posLookups = posLangSys >= 0 ? positioner!.Lookups(posLangSys, PositionFeatures) : null;

                int Total(IReadOnlyList<int> cps)
                {
                    var forms = ArabicJoining.FormsOf(cps);
                    var input = cps.Select((cp, i) => new LayoutEngine.Glyph(
                        cmap.TryGetValue(cp, out var g) ? g : 0, i, forms[i] >= 0 ? FormTags[forms[i]] : null)).ToList();
                    var shaped = engine.Shape(input, lookups, FormFeatures);
                    if (posLookups is not null) shaped = positioner!.Shape(shaped, posLookups, FormFeatures);
                    var total = 0;
                    foreach (var glyph in shaped)
                        if (glyph.Id < advances.Length) total += advances[glyph.Id] + glyph.Adv;
                    return total;
                }

                var tw = Total([tatweel]);

                var forms = new Dictionary<int, int[]>();
                foreach (var cp in letters)
                {
                    var dual = ArabicJoining.TypeOf(cp) == ArabicJoining.Type.Dual;
                    var w = new int[4];
                    w[ArabicJoining.Isolated] = Total([cp]);
                    w[ArabicJoining.Final] = Total([tatweel, cp]) - tw;
                    w[ArabicJoining.Initial] = dual ? Total([cp, tatweel]) - tw : w[ArabicJoining.Isolated];
                    w[ArabicJoining.Medial] = dual ? Total([tatweel, cp, tatweel]) - 2 * tw : w[ArabicJoining.Final];
                    forms[cp] = w;
                }

                var pairs = new Dictionary<long, int>();
                var common = letters.Where(PairLetter).ToList();
                foreach (var a in common)
                    foreach (var b in common)
                        foreach (var (left, right) in new[] { (false, false), (true, false), (false, true), (true, true) })
                        {
                            var cps = new List<int>();
                            if (left) cps.Add(tatweel);
                            var ia = cps.Count;
                            cps.Add(a);
                            cps.Add(b);
                            if (right) cps.Add(tatweel);
                            var fm = ArabicJoining.FormsOf(cps);
                            var delta = Total(cps) - (left ? tw : 0) - (right ? tw : 0) - forms[a][fm[ia]] - forms[b][fm[ia + 1]];
                            var key = ArabicShapes.Key(a, fm[ia], b, fm[ia + 1]);
                            if (!pairs.TryGetValue(key, out var old) || delta > old) pairs[key] = delta;
                        }

                int Pair(int a, int fa, int b, int fb) => pairs.TryGetValue(ArabicShapes.Key(a, fa, b, fb), out var d) ? d : 0;
                var triples = new Dictionary<long, int>();
                var ends = TripleEnds.Where(c => forms.ContainsKey(c)).ToList();
                foreach (var z in ends)
                    foreach (var x in common)
                        foreach (var y in common)
                        {
                            foreach (var left in new[] { false, true })
                            {
                                var cps = new List<int>();
                                if (left) cps.Add(tatweel);
                                var ix = cps.Count;
                                cps.Add(x);
                                cps.Add(y);
                                cps.Add(z);
                                var fm = ArabicJoining.FormsOf(cps);
                                int fx = fm[ix], fy = fm[ix + 1], fz = fm[ix + 2];
                                var delta = Total(cps) - (left ? tw : 0) - forms[x][fx] - forms[y][fy] - forms[z][fz]
                                    - Pair(x, fx, y, fy) - Pair(y, fy, z, fz);
                                var key = ArabicShapes.Key3(x, fx, y, fy, z, fz);
                                if (!triples.TryGetValue(key, out var old) || delta > old) triples[key] = delta;
                            }
                        }

                var shapes = new ArabicShapes
                {
                    Forms = forms,
                    Pairs = pairs.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => kv.Value),
                    PairLetters = common.ToHashSet(),
                    Triples = triples.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => kv.Value),
                    TripleLetters = ends.ToHashSet(),
                };
                var verified = Prune(shapes, common, Total, lang);
                if (verified is null) return null;
                result[lang] = new ArabicShapes
                {
                    Forms = shapes.Forms,
                    Pairs = shapes.Pairs,
                    PairLetters = verified,
                    Triples = shapes.Triples,
                    TripleLetters = shapes.TripleLetters,
                };
            }
            return result.Count > 0 ? result : null;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>反覆自我檢查、移掉出問題的字母，回傳驗證過的字母；剩不到一半回 null。</summary>
    private static HashSet<int>? Prune(ArabicShapes shapes, List<int> letters, Func<IReadOnlyList<int>, int> total, string lang)
    {
        var verified = letters.ToHashSet();
        while (true)
        {
            var failing = SelfCheck(shapes, [.. verified.Order()], total, lang);
            if (failing.Count == 0) return verified;
            // 先移少用的字母：出問題的字裡有少用字母就移它（出現最多次的），全是常用字母才動常用的
            var culprit = failing.SelectMany(w => w.Distinct()).GroupBy(cp => cp)
                .OrderBy(g => Core(g.Key)).ThenByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            verified.Remove(culprit);
            if (Environment.GetEnvironmentVariable("XAMLFIT_ARABIC_SELFCHECK") is not null)
                Console.Error.WriteLine($"    removed U+{culprit:X4} {char.ConvertFromUtf32(culprit)}");
            if (verified.Count * 2 < letters.Count) return null;
        }
    }

    /// <summary>
    /// 用存下來的數字拼出隨機字的寬度（跟 TextMeasure 一樣的拼法），跟引擎整串跑的比。拼出來比較寬可以（保守），比較窄就不行。
    /// 回傳拼窄了的字。
    /// </summary>
    private static List<List<int>> SelfCheck(ArabicShapes shapes, List<int> letters, Func<IReadOnlyList<int>, int> total, string lang)
    {
        var failing = new List<List<int>>();
        if (letters.Count == 0) return failing;
        var rng = new Random(20261115);
        var failures = 0;
        var worst = 0;
        var worstWord = "";
        for (var n = 0; n < SelfCheckWords; n++)
        {
            var word = Enumerable.Range(0, rng.Next(2, 8)).Select(_ => letters[rng.Next(letters.Count)]).ToList();
            var fm = ArabicJoining.FormsOf(word);
            var model = 0;
            for (var i = 0; i < word.Count; i++)
            {
                model += shapes.Forms[word[i]][fm[i]];
                if (i >= 1 && shapes.Pairs.TryGetValue(ArabicShapes.Key(word[i - 1], fm[i - 1], word[i], fm[i]), out var p)) model += p;
                if (i >= 2 && shapes.TripleLetters.Contains(word[i])
                    && shapes.Triples.TryGetValue(ArabicShapes.Key3(word[i - 2], fm[i - 2], word[i - 1], fm[i - 1], word[i], fm[i]), out var t))
                    model += t;
            }
            var engine = total(word);
            if (model < engine)
            {
                failures++;
                failing.Add(word);
                if (engine - model > worst) { worst = engine - model; worstWord = string.Concat(word.Select(char.ConvertFromUtf32)); }
            }
        }
        if (Environment.GetEnvironmentVariable("XAMLFIT_ARABIC_SELFCHECK") is not null)
            Console.Error.WriteLine($"  arabic self-check {lang.Trim()}: {failures}/{SelfCheckWords} narrower, worst {worst} units «{worstWord}»");
        return failing;
    }
}
