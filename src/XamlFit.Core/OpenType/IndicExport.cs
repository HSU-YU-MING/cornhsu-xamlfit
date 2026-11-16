using XamlFit.Core.Fonts;

namespace XamlFit.Core.OpenType;

/// <summary>
/// 匯出時把每個印度系字素叢集（<see cref="IndicClusters"/>）跑過字型的 GSUB 與 GPOS 字距，記下它比「各字 advance 相加」寬或窄多少（只列非零）。
///
/// ⚠ 單獨一個「子音 + virama」：不套半形、reph、下加形這類要看下一個子音的特性 —— 字的結尾 WPF 就是畫出看得見的 virama；
///   字中間 WPF 畫半形（比較窄），我們用看得見的寬度，錯在寬的那邊。
/// 度量檔裡只有數字，沒有字型的表。
/// </summary>
internal static class IndicExport
{
    // 特性要分階段套（跟 Windows／HarfBuzz 的印度系排版一樣）：基本的形狀特性一個一個來，最後才一起套呈現用的特性。
    // 全部混在一起依 lookup 編號套時，Nirmala UI 的奧里亞文在下加形（blwf）之前 virama 就被別的 lookup 吃掉，連寫一個都沒成形（平均偏寬 5%）
    private static readonly string[][] Basic = [["ccmp", "locl"], ["nukt"], ["akhn"], ["haln"]];
    private static readonly string[][] Full =
        [["ccmp", "locl"], ["nukt"], ["akhn"], ["rphf"], ["rkrf"], ["pref"], ["blwf"], ["abvf"], ["half"], ["pstf"], ["vatu"], ["cjct"], ["cfar"],
         ["init", "pres", "abvs", "blws", "psts", "haln", "calt", "rclt", "liga", "clig"]];
    private static readonly HashSet<string> Position = ["kern", "dist"];
    private static readonly HashSet<string> NoForms = [];
    private static readonly string?[] Languages = [null, "MAR", "HIN", "NEP", "TAM", "SAN", "KOK", "BEN", "ASM", "PAN", "GUJ", "ORI", "TEL", "KAN", "MAL", "MLR", "SNH", "KHM", "BRM", "TIB"];

    public static Dictionary<string, Dictionary<string, int>>? Read(SfntReader r, SortedDictionary<int, int> cmap, int[] advances)
    {
        if (!r.Has("GSUB")) return null;
        try
        {
            var gsub = new LayoutEngine(r);
            var gpos = r.Has("GPOS") ? new LayoutEngine(r, "GPOS") : null;
            var result = new Dictionary<string, Dictionary<string, int>>();

            foreach (var script in IndicClusters.All)
            {
                if (!cmap.ContainsKey(script.Consonants.Min())) continue;
                var tag = gsub.LangSys(script.Tag2, "dflt") >= 0 ? script.Tag2 : gsub.LangSys(script.Tag1, "dflt") >= 0 ? script.Tag1 : null;
                if (tag is null) continue;   // 字型沒有這個文字系統的規則：不寫，用到就量不到
                foreach (var lang in Languages)
                {
                var langSys = gsub.LangSys(tag, lang is null ? "dflt" : lang.PadRight(4));
                if (langSys < 0) continue;
                var full = Full.Select(stage => gsub.Lookups(langSys, stage.ToHashSet())).Where(l => l.Count > 0).ToList();
                var basic = Basic.Select(stage => gsub.Lookups(langSys, stage.ToHashSet())).Where(l => l.Count > 0).ToList();
                var posLangSys = gpos?.LangSys(tag, lang is null ? "dflt" : lang.PadRight(4)) ?? -1;
                if (posLangSys < 0) posLangSys = gpos?.LangSys(tag, "dflt") ?? -1;
                var pos = posLangSys >= 0 ? gpos!.Lookups(posLangSys, Position) : null;

                int Width(IReadOnlyList<int> cps, List<SortedDictionary<int, HashSet<string>>> stages)
                {
                    var input = cps.Select((cp, i) => new LayoutEngine.Glyph(cmap.TryGetValue(cp, out var g) ? g : 0, i, null)).ToList();
                    var shaped = input;
                    foreach (var stage in stages) shaped = gsub.Shape(shaped, stage, NoForms);
                    if (pos is not null) shaped = gpos!.Shape(shaped, pos, NoForms);
                    return shaped.Sum(g => (g.Id < advances.Length ? advances[g.Id] : 0) + g.Adv);
                }

                var deltas = new Dictionary<string, int>(StringComparer.Ordinal);
                var widths = new Dictionary<string, int>(StringComparer.Ordinal);
                // 只依疊字記的文字系統：鼻化／送氣符號不展開（另外記一個最大值），省掉五倍的組合
                var all = IndicClusters.Enumerate(script, cmap.ContainsKey, withModifiers: !script.StackKeyed).ToList();
                foreach (var cluster in all)
                {
                    var endsInVirama = cluster[^1] == script.Virama && !script.VisibleVirama;
                    var width = Width(IndicClusters.VisualOrder(script, cluster), endsInVirama ? basic : full);
                    // 可有可無的符號（高棉文 ៉ ៊）：沒有它的寬度 + 它自己的 advance 也算，取寬的
                    if (cluster.Any(script.Droppable.Contains))
                    {
                        var without = cluster.Where(cp => !script.Droppable.Contains(cp)).ToList();
                        width = Math.Max(width, Width(IndicClusters.VisualOrder(script, without), endsInVirama ? basic : full)
                                                + cluster.Where(script.Droppable.Contains).Sum(cp => advances[cmap[cp]]));
                    }
                    var plain = cluster.Sum(cp => advances[cmap[cp]]);
                    widths[IndicClusters.Key(cluster)] = width;
                    if (script.StackKeyed)
                    {
                        // 疊字的鍵：各種母音符號裡改變量最大的（0 也算進去）
                        var key = IndicClusters.Key(IndicClusters.Stack(script, cluster));
                        var change = width - plain;
                        if (!deltas.TryGetValue(key, out var old) || change > old) deltas[key] = change;
                    }
                    else if (width != plain) deltas[IndicClusters.Key(cluster)] = width - plain;
                }
                if (script.StackKeyed)
                {
                    foreach (var key in deltas.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList()) deltas.Remove(key);
                    // 鼻化／送氣符號：接在每一個疊字後面最多讓它變寬多少
                    var widest = 0;
                    var stacks = all.Where(c => IndicClusters.Stack(script, c).Length == c.Length).ToList();
                    foreach (var modifier in script.ModifierSeqs.Where(m => m.All(cmap.ContainsKey)))
                        foreach (var stack in stacks)
                        {
                            var change = Width(IndicClusters.VisualOrder(script, [.. stack, .. modifier]), full)
                                         - widths[IndicClusters.Key(stack)] - modifier.Sum(cp => advances[cmap[cp]]);
                            if (change > widest) widest = change;
                        }
                    if (widest > 0) deltas[IndicClusters.ModifierKey] = widest;
                }

                // 子音 + virama 後面接子音（半形、合字、下加形、reph）：比兩個叢集各自量窄多少。
                // 依「前一個子音, 後一個子音」記：後一個叢集帶什麼母音符號都試，取改變**最少**的 —— 錯在寬的那邊
                // 每一組先依後一個叢集的母音符號分開記（-1 = 沒有母音符號）
                var pairs = new List<(int First, int Second, Dictionary<int, int> BySign)>();
                foreach (var head in all.Where(c => script.Conjuncts && c.Length == 2 && c[1] == script.Virama))
                    foreach (var group in all.Where(c => script.Consonants.Contains(c[0])).GroupBy(c => c[0]))
                    {
                        var bySign = new Dictionary<int, int>();
                        // 後一個叢集自己是「子音 + virama」時量的時候不套連寫（見 TextMeasure 的 nextIsHead），這裡也不算 ——
                        // 算進去會讓「沒有母音符號」那組變成 0（奧里亞文：ବ୍ 接在 ଖ୍ 後面不變窄，ଖ୍ବ 卻窄了一半）
                        // 前置母音放在整串前面（連成合字：क्षि）還是只放在後一個子音前面（virama 看得見：馬拉雅拉姆文 ച്ങൈ，ൈ 在 ങ 前面）：
                        // 沒有母音符號時連寫一點都沒變窄，就當 virama 看得見
                        int Change(int[] next, bool separate)
                        {
                            var together = Width(separate
                                ? [.. IndicClusters.VisualOrder(script, head), .. IndicClusters.VisualOrder(script, next)]
                                : IndicClusters.VisualOrder(script, [.. head, .. next]), full);
                            // reph：WPF 把它移到後一個子音後面，兩種順序都算
                            if (head[0] == script.Ra)
                                together = Math.Max(together, Width([.. IndicClusters.VisualOrder(script, next), .. head], full));
                            return together - widths[IndicClusters.Key(head)] - widths[IndicClusters.Key(next)];
                        }
                        var bare = group.FirstOrDefault(c => c.Length == 1) is { } alone ? Change(alone, script.SyllableEndsAtVirama) : (int?)null;
                        var visibleVirama = script.SyllableEndsAtVirama || bare is null or >= 0;
                        foreach (var next in group.Where(c => !(c.Length == 2 && c[1] == script.Virama)))
                        {
                            var change = Change(next, visibleVirama);
                            if (script.PreBaseMayBreakConjunct && !visibleVirama && next.Any(cp => script.PreBase.Contains(cp) || script.Split.ContainsKey(cp)))
                                change = Math.Max(change, Change(next, true));
                            // 帶母音符號的連寫不會比沒帶的窄（奧里亞文 ଥ୍ଥି：WPF 把 ି 的寬度加在連寫上，這支引擎把它併掉，窄了 9%）
                            if (bare is { } b && change < b) change = b;
                            var sign = IndicClusters.SignOf(script, next, 0);
                            // 後一個子音帶 nukta：WPF 常常不連寫（古木基文 ਟ੍ਯ਼，這支引擎照樣連成 yakash）—— 不算變窄
                            if (script.Nukta >= 0 && sign == script.Nukta && change < 0) change = 0;
                            // 帶母音符號時整串也不會比後一個叢集自己窄（奧里亞文 ଥ୍ଥ 連成比 ଥ 還窄的合字，但 ଥ୍ଥି 時 WPF 不連，寬度跟 ଥି 一樣）
                            if (sign >= 0 && change < -widths[IndicClusters.Key(head)]) change = -widths[IndicClusters.Key(head)];
                            if (!bySign.TryGetValue(sign, out var old) || change > old) bySign[sign] = change;
                        }
                        if (bySign.Count > 0) pairs.Add((head[0], group.Key, bySign));
                    }
                // 一個母音符號在超過四分之一的組裡都是「變窄最少」的那個（卡納達文 ೌ、奧里亞文 ୁ）：它每一組另外記（0 也記），
                // 不要拖累其他形式（卡納達文原本平均偏寬 8.4%：ಗ್ಘ 這種連寫被算成幾乎不變窄）
                // 「比沒有母音符號時變窄得少」的組超過四分之一的符號都另外記（奧里亞文 ୁ ୂ ୃ େ、nukta 好幾個一起都是 0，不能只挑一個）
                var separate = pairs.SelectMany(p => p.BySign.Where(kv => kv.Key >= 0 && p.BySign.TryGetValue(-1, out var bare) && kv.Value > bare).Select(kv => kv.Key))
                    .GroupBy(sign => sign).Where(g => g.Count() * 4 > pairs.Count).Select(g => g.Key).ToHashSet();
                // 帶 nukta 的子音一律另外記（古木基文 ਢ੍ਯ਼：WPF 不連寫，一般的值會窄 8%）
                if (script.Nukta >= 0) separate.Add(script.Nukta);
                foreach (var (first, second, bySign) in pairs)
                {
                    var rest = bySign.Where(kv => !separate.Contains(kv.Key)).Select(kv => kv.Value).ToList();
                    if (rest.Count > 0 && rest.Max() is var least && least != 0) deltas[IndicClusters.ConjunctKey(first, second)] = least;
                    // 只記比一般的值寬的（沒有這個鍵就用一般的值，窄的那邊用一般的只會偏寬）：檔案小一半
                    foreach (var sign in separate)
                        if (bySign.TryGetValue(sign, out var change) && (rest.Count == 0 || change > rest.Max()))
                            deltas[IndicClusters.ConjunctKey(first, second, sign)] = change;
                }
                // 叢集後面接標點或空白：WPF 把它們併進同一段，字型的字距會套上去（坦米爾文的「ல்:」寬 0.08 px）。
                // 記全部叢集、全部標點裡**變寬最多**的那個，量的時候只要後面接的不是同一個文字系統的字就加上去
                var trailing = 0;
                // 同一個文字系統裡的標點（藏文的 tsheg ་、天城文的 danda）也試；量的時候同一個文字系統裡不組字的字（標點、數字）都加這個值
                var marks = IndicClusters.TrailingChars.Select(c => (int)c)
                    .Concat(script.Plain.Where(cp => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp) is
                        System.Globalization.UnicodeCategory.OtherPunctuation or System.Globalization.UnicodeCategory.DashPunctuation
                        or System.Globalization.UnicodeCategory.OpenPunctuation or System.Globalization.UnicodeCategory.ClosePunctuation))
                    .Where(cmap.ContainsKey).Distinct().ToList();
                // 只依疊字記的文字系統組合太多：標點只接在疊字（不帶母音符號）後面試
                foreach (var cluster in script.StackKeyed ? all.Where(c => IndicClusters.Stack(script, c).Length == c.Length) : all)
                    foreach (var mark in marks)
                    {
                        var change = Width([.. IndicClusters.VisualOrder(script, cluster), mark], cluster[^1] == script.Virama && !script.VisibleVirama ? basic : full)
                                     - widths[IndicClusters.Key(cluster)] - advances[cmap[mark]];
                        if (change > trailing) trailing = change;
                    }
                if (trailing > 0) deltas[IndicClusters.TrailingKey] = trailing;

                // 三個子音連寫：量的時候各叢集直接相加（不套連寫），這裡記整串最多比相加寬多少
                if (script.Conjuncts)
                {
                    var heads = all.Where(c => c.Length == 2 && c[1] == script.Virama).ToList();
                    var lasts = all.Where(c => script.Consonants.Contains(c[0]) && (c.Length == 1 || c.Length == 2 && c[1] == script.Nukta)).ToList();
                    var chain = 0;
                    foreach (var h1 in heads)
                        foreach (var h2 in heads)
                            foreach (var c3 in lasts)
                            {
                                var change = Width(IndicClusters.VisualOrder(script, [.. h1, .. h2, .. c3]), full)
                                             - widths[IndicClusters.Key(h1)] - widths[IndicClusters.Key(h2)] - widths[IndicClusters.Key(c3)];
                                if (change > chain) chain = change;
                            }
                    if (chain > 0) deltas[IndicClusters.ChainKey] = chain;
                }

                // 叢集會影響下一個叢集的文字系統：每個叢集接「每一種開頭」的叢集時最多變寬多少（開頭 = 畫出來的前兩個字）
                if (script.Interacting && script.BoundaryTail > 0)
                {
                    // 依結尾記：每個叢集接「每一種開頭」（畫出來的第一個字）時最多變寬多少，同一個結尾取最大
                    var firsts = all.GroupBy(c => IndicClusters.VisualOrder(script, c)[0]).Select(g => g.OrderBy(c => c.Length).First()).ToList();
                    foreach (var a in all.Where(c => c[^1] != script.Virama))
                    {
                        var visualA = IndicClusters.VisualOrder(script, a);
                        var alone = Width(visualA, full);
                        var key = IndicClusters.BoundaryKey(a.TakeLast(script.BoundaryTail));
                        var widest = deltas.TryGetValue(key, out var known) ? known : 0;
                        foreach (var b in firsts)
                        {
                            var change = Width([.. visualA, .. IndicClusters.VisualOrder(script, b)], full) - alone - widths[IndicClusters.Key(b)];
                            if (change > widest) widest = change;
                        }
                        if (widest > 0) deltas[key] = widest;
                    }
                }
                else if (script.Interacting)
                {
                    var starts = all.GroupBy(c => string.Join(",", IndicClusters.VisualOrder(script, c).Take(2)))
                        .Select(g => g.OrderBy(c => c.Length).First()).ToList();
                    // WPF 套字距時跳過附加符號（「દૃં」+「ડ」照「દૃ」+「ડ」撐開）：叢集的每一個前綴都試，取最寬的
                    // （引擎現在處理 LookupFlag 了，這層保守留著：拿掉要重新對照）
                    var memo = new Dictionary<string, int>(StringComparer.Ordinal);
                    int Widest(int[] a)
                    {
                        var key = IndicClusters.Key(a);
                        if (memo.TryGetValue(key, out var known)) return known;
                        var widest = 0;
                        var visualA = IndicClusters.VisualOrder(script, a);
                        foreach (var b in starts)
                        {
                            var change = Width([.. visualA, .. IndicClusters.VisualOrder(script, b)], full)
                                         - Width(visualA, full) - widths[IndicClusters.Key(b)];
                            if (change > widest) widest = change;
                        }
                        return memo[key] = widest;
                    }
                    foreach (var a in all)
                    {
                        // 叢集的每一個前綴（字母、字母 + nukta、再加母音符號…）：被跳過的符號可能是其中任何一段
                        var widest = 0;
                        for (var n = 1; n <= a.Length; n++) widest = Math.Max(widest, Widest(a[..n]));
                        if (widest > 0) deltas[IndicClusters.BoundaryKey(a)] = widest;
                    }
                }
                result[IndicClusters.DataKey(script, lang)] = deltas;
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
