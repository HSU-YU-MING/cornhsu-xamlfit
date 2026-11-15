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
    private static readonly HashSet<string> Basic = ["ccmp", "locl", "nukt", "akhn"];
    private static readonly HashSet<string> Full =
        ["ccmp", "locl", "nukt", "akhn", "rphf", "rkrf", "pref", "blwf", "abvf", "half", "pstf", "vatu", "cjct",
         "init", "pres", "abvs", "blws", "psts", "haln", "calt", "rclt", "liga", "clig"];
    private static readonly HashSet<string> Position = ["kern", "dist"];
    private static readonly HashSet<string> NoForms = [];

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
                foreach (var lang in new string?[] { null, "MAR", "HIN", "NEP", "TAM", "SAN", "KOK" })
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
                var all = IndicClusters.Enumerate(script, cmap.ContainsKey).ToList();
                foreach (var cluster in all)
                {
                    var endsInVirama = cluster[^1] == script.Virama;
                    var width = Width(IndicClusters.VisualOrder(script, cluster), endsInVirama ? basic : full);
                    var plain = cluster.Sum(cp => advances[cmap[cp]]);
                    widths[IndicClusters.Key(cluster)] = width;
                    if (width != plain) deltas[IndicClusters.Key(cluster)] = width - plain;
                }

                // 子音 + virama 後面接子音（半形、合字、下加形、reph）：比兩個叢集各自量窄多少。
                // 依前面那個子音記「所有接法裡改變最少的」—— 錯在寬的那邊
                foreach (var head in all.Where(c => c.Length == 2 && c[1] == script.Virama))
                {
                    int? least = null;
                    foreach (var next in all.Where(c => script.Consonants.Contains(c[0])))
                    {
                        var together = Width(IndicClusters.VisualOrder(script, [.. head, .. next]), full);
                        var change = together - widths[IndicClusters.Key(head)] - widths[IndicClusters.Key(next)];
                        if (least is null || change > least) least = change;
                    }
                    if (least is { } l && l != 0) deltas[IndicClusters.ConjunctPrefix + head[0]] = l;
                }
                // 叢集後面接標點或空白：WPF 把它們併進同一段，字型的字距會套上去（坦米爾文的「ல்:」寬 0.08 px）。
                // 記全部叢集、全部標點裡**變寬最多**的那個，量的時候只要後面接的不是同一個文字系統的字就加上去
                var trailing = 0;
                var marks = IndicClusters.TrailingChars.Where(c => cmap.ContainsKey(c)).Select(c => (int)c).ToList();
                foreach (var cluster in all)
                    foreach (var mark in marks)
                    {
                        var change = Width([.. IndicClusters.VisualOrder(script, cluster), mark], cluster[^1] == script.Virama ? basic : full)
                                     - widths[IndicClusters.Key(cluster)] - advances[cmap[mark]];
                        if (change > trailing) trailing = change;
                    }
                if (trailing > 0) deltas[IndicClusters.TrailingKey] = trailing;
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
