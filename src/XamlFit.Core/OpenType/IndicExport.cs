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
    private static readonly HashSet<string> Basic = ["ccmp", "locl", "nukt", "akhn", "haln"];
    private static readonly HashSet<string> Full =
        ["ccmp", "locl", "nukt", "akhn", "rphf", "rkrf", "pref", "blwf", "abvf", "half", "pstf", "vatu", "cjct",
         "init", "pres", "abvs", "blws", "psts", "haln", "cfar", "calt", "rclt", "liga", "clig"];
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
                var full = gsub.Lookups(langSys, Full);
                var basic = gsub.Lookups(langSys, Basic);
                var posLangSys = gpos?.LangSys(tag, lang is null ? "dflt" : lang.PadRight(4)) ?? -1;
                if (posLangSys < 0) posLangSys = gpos?.LangSys(tag, "dflt") ?? -1;
                var pos = posLangSys >= 0 ? gpos!.Lookups(posLangSys, Position) : null;

                int Width(IReadOnlyList<int> cps, SortedDictionary<int, HashSet<string>> lookups)
                {
                    var input = cps.Select((cp, i) => new LayoutEngine.Glyph(cmap.TryGetValue(cp, out var g) ? g : 0, i, null)).ToList();
                    var shaped = gsub.Shape(input, lookups, NoForms);
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
                foreach (var head in all.Where(c => script.Conjuncts && c.Length == 2 && c[1] == script.Virama))
                    foreach (var group in all.Where(c => script.Consonants.Contains(c[0])).GroupBy(c => c[0]))
                    {
                        int? least = null;
                        foreach (var next in group)
                        {
                            var together = Width(IndicClusters.VisualOrder(script, [.. head, .. next]), full);
                            var change = together - widths[IndicClusters.Key(head)] - widths[IndicClusters.Key(next)];
                            if (least is null || change > least) least = change;
                        }
                        if (least is { } l && l != 0) deltas[IndicClusters.ConjunctKey(head[0], group.Key)] = l;
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

                // 叢集會影響下一個叢集的文字系統：每個叢集接「每一種開頭」的叢集時最多變寬多少（開頭 = 畫出來的前兩個字）
                if (script.Interacting)
                {
                    var starts = all.GroupBy(c => string.Join(",", IndicClusters.VisualOrder(script, c).Take(2)))
                        .Select(g => g.OrderBy(c => c.Length).First()).ToList();
                    // WPF 套字距時跳過附加符號（「દૃં」+「ડ」照「દૃ」+「ડ」撐開）；這支引擎不處理 LookupFlag，
                    // 所以叢集的每一個前綴都試，取最寬的
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
