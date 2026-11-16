using XamlFit.Core.Fonts;

namespace XamlFit.Core.OpenType;

/// <summary>
/// 匯出時用 <see cref="LayoutEngine"/> 把蒙古文跑過字型的替換規則（形狀、上下文替換、連字），記下每個字在每種形狀下**最寬**佔多少：
/// 排出來的每個字形算給它來自的那個字（連字算給第一個字、拆成好幾個字形的全部算給原來那個字），
/// 同一個字、同一種形狀在所有試過的上下文裡取最大。字型會連成合字的字母對（子音 + 母音）整對一起記（見 <see cref="MongolianJoining.PairKey"/>）。
/// 量的時候照同樣的切法查表相加 —— 每一段都不比真的窄，加起來也不會窄。
///
/// 上下文：每個字母單獨與兩兩相接、基本字母（U+1820–1842）三個三個相接、每個字母接 FVS、MVS、窄不換行空白，
/// 再加 40 萬個隨機的字（陽性、陰性、只有中性母音的字，FVS、MVS、窄不換行空白都有）。
/// 蒙古文的上下文規則會看整個字（陰陽性），三個字以內看不全，所以要隨機的長字補。
/// **自我檢查**：另外 2 萬個隨機字（不同種子）整串跑、用表相加；拼窄的字拿去補表再檢查，三輪還拼窄的字母從表裡拿掉（用到就量不到）。
/// </summary>
internal static class MongolianExport
{
    private static readonly HashSet<string> FormFeatures = ["isol", "init", "medi", "fina"];
    private static readonly HashSet<string> AllFeatures = ["ccmp", "locl", "isol", "init", "medi", "fina", "rlig", "calt", "rclt", "liga", "clig"];
    private static readonly HashSet<string> PositionFeatures = ["kern", "dist"];
    private static readonly string[] FormTags = ["isol", "init", "medi", "fina"];
    private const int TrainingWords = 400000, SelfCheckWords = 20000;
    // 除錯：XAMLFIT_MONGOLIAN_WHY="6176,2;6155,0" 印出把這些鍵撐到最大的上下文
    private static readonly HashSet<string>? Why = Environment.GetEnvironmentVariable("XAMLFIT_MONGOLIAN_WHY") is { Length: > 0 } w ? [.. w.Split(';')] : null;

    public static Dictionary<string, int>? Read(SfntReader r, SortedDictionary<int, int> cmap, int[] advances)
    {
        if (!r.Has("GSUB") || !cmap.ContainsKey(0x1820)) return null;
        try
        {
            var engine = new LayoutEngine(r);
            var langSys = engine.LangSys("mong", "dflt");
            if (langSys < 0) return null;
            var lookups = engine.Lookups(langSys, AllFeatures);
            var positioner = r.Has("GPOS") ? new LayoutEngine(r, "GPOS") : null;
            var posLangSys = positioner?.LangSys("mong", "dflt") ?? -1;
            var posLookups = posLangSys >= 0 ? positioner!.Lookups(posLangSys, PositionFeatures) : null;

            // 字母照形狀標上 isol／init／medi／fina；FVS 沿用前一個字母的（跟 HarfBuzz 一樣：字型靠形狀特性把 FVS 換成零寬的字形，
            // 不標的話每個 FVS 都是 2214 單位寬）
            List<LayoutEngine.Glyph> Input(IReadOnlyList<int> cps)
            {
                var forms = MongolianJoining.FormsOf(cps);
                var input = new List<LayoutEngine.Glyph>(cps.Count);
                string? last = null;
                for (var i = 0; i < cps.Count; i++)
                {
                    var cp = cps[i];
                    string? tag = MongolianJoining.IsLetter(cp) ? FormTags[MongolianJoining.Position(forms[i])]
                        : MongolianJoining.IsVariationSelector(cp) ? last : null;
                    last = MongolianJoining.IsLetter(cp) ? tag : MongolianJoining.IsVariationSelector(cp) ? last : null;
                    input.Add(new LayoutEngine.Glyph(cmap.TryGetValue(cp, out var g) ? g : 0, i, tag));
                }
                return input;
            }

            // 每個字分到的寬度
            int[] Shaped(IReadOnlyList<int> cps)
            {
                var input = Input(cps);
                var shaped = engine.Shape(input, lookups, FormFeatures);
                if (posLookups is not null) shaped = positioner!.Shape(shaped, posLookups, FormFeatures);
                var widths = new int[cps.Count];
                foreach (var g in shaped)
                    if (g.Cluster >= 0 && g.Cluster < widths.Length) widths[g.Cluster] += (g.Id < advances.Length ? advances[g.Id] : 0) + g.Adv;
                // FVS 接在字母後面：WPF 一律畫零寬（這支引擎有時沒換掉它 2214 單位寬的字形）。單獨出現的 FVS WPF 畫虛線圓圈：不學（量不到）
                for (var i = 0; i < cps.Count; i++)
                    if (MongolianJoining.IsVariationSelector(cps[i])) widths[i] = 0;
                return widths;
            }

            var letters = cmap.Keys.Where(MongolianJoining.IsLetter).ToList();
            const int a = 0x1820, e = 0x1821, mvs = 0x180E, nnbsp = 0x202F;

            // 合字對：兩個字母相接時（單獨、字中，陽性、陰性），第二個字一個字形都沒留下
            var pairs = new HashSet<(int, int)>();
            foreach (var x in letters)
                foreach (var y in letters)
                    foreach (var (word, at) in new (int[], int)[] { ([x, y], 1), ([a, x, y, a], 2), ([e, x, y, e], 2), ([x, y, a], 1), ([a, x, y], 2) })
                    {
                        if (!engine.Shape(Input(word), lookups, FormFeatures).Any(g => g.Cluster == at)) { pairs.Add((x, y)); break; }
                    }
            bool Pair(int x, int y) => pairs.Contains((x, y));

            var widest = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (x, y) in pairs) widest[MongolianJoining.PairKey(x, y)] = 1;
            void Learn(IReadOnlyList<int> cps)
            {
                var widths = Shaped(cps);
                var forms = MongolianJoining.FormsOf(cps);
                foreach (var (start, length) in MongolianJoining.Segments(cps, Pair))
                {
                    if (MongolianJoining.IsVariationSelector(cps[start]) && (start == 0 || !MongolianJoining.IsLetter(cps[start - 1]))) continue;
                    var width = widths[start] + (length == 2 ? widths[start + 1] : 0);
                    foreach (var key in new[] { MongolianJoining.SegmentKey(cps, start, length, forms), MongolianJoining.SegmentKey(cps, start, length, forms, coarse: true) })
                        if (!widest.TryGetValue(key, out var old) || width > old)
                        {
                            widest[key] = width;
                            if (Why is { } why && why.Contains(key))
                                Console.Error.WriteLine($"[mongolian] {key} = {width} in {string.Join(" ", cps.Select(cp => cp.ToString("X4")))}");
                        }
                }
            }

            var basic = letters.Where(cp => cp is >= 0x1820 and <= 0x1842).ToList();
            var consonants = basic.Where(cp => cp >= 0x1828).ToList();
            var others = cmap.Keys.Where(cp => MongolianJoining.IsMongolian(cp) && !MongolianJoining.IsLetter(cp)).ToList();
            int[] fvs = [.. new[] { 0x180B, 0x180C, 0x180D, 0x180F }.Where(cmap.ContainsKey)];

            foreach (var cp in others) { Learn([cp]); Learn([a, cp, a]); Learn([a, cp]); Learn([cp, a]); }
            foreach (var x in letters)
            {
                Learn([x]);
                foreach (var y in letters) Learn([x, y]);
                foreach (var f in fvs) { Learn([x, f]); Learn([a, x, f]); Learn([x, f, a]); Learn([a, x, f, a]); Learn([e, x, f, e]); }
                foreach (var v in new[] { a, e }) { Learn([x, mvs, v]); Learn([a, x, mvs, v]); Learn([e, x, mvs, v]); }
                Learn([x, nnbsp, a]); Learn([a, x, nnbsp, e, x]);
            }
            // 基本字母三個三個相接，各在陽性、陰性的字裡試一次（合字對兩邊的字形看前後的字）
            foreach (var x in basic)
                foreach (var y in basic)
                    foreach (var z in basic)
                    {
                        Learn([x, y, z]);
                        Learn([a, x, y, z, a]);
                        Learn([e, x, y, z, e]);
                    }

            List<int> Word(Random rng)
            {
                // 陽性（a o u）或陰性（e ö ü）母音，加 i；子音多半從基本字母挑
                // 陽性、陰性，也有只用中性母音 ᠢ 的字（ᠵᠢᠩᠬᠢᠨᠢ：只練前兩種時偏窄 7%）
                int[] vowels = rng.Next(5) switch { 0 or 1 => [0x1820, 0x1823, 0x1824, 0x1822], 2 or 3 => [0x1821, 0x1825, 0x1826, 0x1822, 0x1827], _ => [0x1822] };
                var word = new List<int>();
                for (var n = rng.Next(2, 13); n > 0; n--)
                {
                    word.Add(rng.Next(3) == 0 ? vowels[rng.Next(vowels.Length)]
                        : consonants.Count > 0 && rng.Next(4) != 0 ? consonants[rng.Next(consonants.Count)] : letters[rng.Next(letters.Count)]);
                    if (fvs.Length > 0 && rng.Next(10) == 0) word.Add(fvs[rng.Next(fvs.Length)]);
                }
                if (rng.Next(8) == 0) { word.Add(mvs); word.Add(vowels[0] == 0x1820 ? a : e); }
                if (rng.Next(8) == 0) { word.Add(nnbsp); word.AddRange(Enumerable.Range(0, rng.Next(1, 5)).Select(_ => rng.Next(2) == 0 ? vowels[rng.Next(vowels.Length)] : consonants[rng.Next(consonants.Count)])); }
                return word;
            }
            var train = new Random(7);
            for (var n = 0; n < TrainingWords; n++) Learn(Word(train));

            var check = new Random(11);
            var words = Enumerable.Range(0, SelfCheckWords).Select(_ => Word(check)).ToList();
            var removed = new HashSet<int>();
            for (var round = 0; round < 3; round++)
            {
                var bad = 0;
                foreach (var word in words)
                {
                    if (word.Any(removed.Contains)) continue;
                    var forms = MongolianJoining.FormsOf(word);
                    var sum = 0;
                    var known = true;
                    foreach (var (start, length) in MongolianJoining.Segments(word, Pair))
                        if (widest.TryGetValue(MongolianJoining.SegmentKey(word, start, length, forms), out var w)
                            || widest.TryGetValue(MongolianJoining.SegmentKey(word, start, length, forms, coarse: true), out w)) sum += w;
                        else if (!MongolianJoining.IsVariationSelector(word[start])) { known = false; break; }
                    if (!known || sum >= Shaped(word).Sum()) continue;
                    bad++;
                    if (round == 2) foreach (var cp in word.Where(MongolianJoining.IsLetter)) removed.Add(cp);
                    else Learn(word);
                }
                if (Environment.GetEnvironmentVariable("XAMLFIT_DEBUG") is not null)
                    Console.Error.WriteLine($"[mongolian] self-check round {round}: {bad} of {words.Count} words narrower");
                if (bad == 0) break;
            }
            foreach (var key in widest.Keys.Where(k => k.Split(',').Take(2).Any(x => removed.Contains(int.Parse(x, System.Globalization.CultureInfo.InvariantCulture)))).ToList())
                widest.Remove(key);

            // 夾在蒙古文裡的標點：前後是字母、空白，各排一次取最寬的；比原本寬才記（見 MongolianJoining.CommonKey）
            foreach (var cp in cmap.Keys.Where(MongolianJoining.IsCommon))
            {
                var width = 0;
                foreach (var (word, at) in new (int[], int)[] { ([a, cp], 1), ([cp, a], 0), ([a, cp, a], 1), ([a, 0x20, cp], 2), ([cp, 0x20, a], 0), ([e, cp], 1), ([cp, e], 0) })
                    width = Math.Max(width, Shaped(word)[at]);
                if (width > advances[cmap[cp]]) widest[MongolianJoining.CommonKey(cp)] = width;
            }
            return widest.Count > 0 ? widest : null;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
