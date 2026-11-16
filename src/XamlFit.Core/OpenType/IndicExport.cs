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
    // 定位：字距之外，下加／上加符號的定位特性裡也可能改寬度（Nirmala UI 的 blwm：兩個下加形相連時把前一個撐開 550 單位；
    // 不套的話奧里亞文三子音連寫偏窄到 27%）。這支引擎只做會改寬度的單一／成對／上下文定位，附加符號的對齊不影響寬度
    private static readonly HashSet<string> Position = ["kern", "dist", "abvm", "blwm", "mark", "mkmk"];
    private static readonly HashSet<string> NoForms = [];
    // 除錯：XAMLFIT_INDIC_TRACE="09B9,09CD,09AF,09C3;…" 印出這些序列排出來的字形（跟 WpfOracle --glyphs 比）
    private static readonly HashSet<string>? Trace = Environment.GetEnvironmentVariable("XAMLFIT_INDIC_TRACE") is { Length: > 0 } t ? [.. t.Split(';')] : null;
    private static readonly string?[] Languages = [null, "MAR", "HIN", "NEP", "TAM", "SAN", "KOK", "BEN", "ASM", "PAN", "GUJ", "ORI", "TEL", "KAN", "MAL", "MLR", "SNH", "KHM", "BRM", "TIB"];

    public static Dictionary<string, Dictionary<string, int>>? Read(SfntReader r, SortedDictionary<int, int> cmap, int[] advances)
    {
        if (!r.Has("GSUB")) return null;
        try
        {
            var probe = new LayoutEngine(r);
            var jobs = new List<(IndicClusters.Script Script, string Tag, string? Lang)>();
            foreach (var script in IndicClusters.All)
            {
                if (!cmap.ContainsKey(script.Consonants.Min())) continue;
                var tag = probe.LangSys(script.Tag2, "dflt") >= 0 ? script.Tag2 : probe.LangSys(script.Tag1, "dflt") >= 0 ? script.Tag1 : null;
                if (tag is null) continue;   // 字型沒有這個文字系統的規則：不寫，用到就量不到
                foreach (var lang in Languages)
                    if (probe.LangSys(tag, lang is null ? "dflt" : lang.PadRight(4)) >= 0) jobs.Add((script, tag, lang));
            }
            // 每個（文字系統, 語言系統）互不相干，平行算（LayoutEngine 有快取、不能共用，每個工作各開一個）。Nirmala UI 5.5 → 1.5 分鐘
            var result = new System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            Parallel.ForEach(jobs, job =>
            {
                var (script, tag, lang) = job;
                var gsub = new LayoutEngine(r);
                var gpos = r.Has("GPOS") ? new LayoutEngine(r, "GPOS") : null;
                {
                var langSys = gsub.LangSys(tag, lang is null ? "dflt" : lang.PadRight(4));
                var full = Full.Select(stage => gsub.Lookups(langSys, stage.ToHashSet())).Where(l => l.Count > 0).ToList();
                var basic = Basic.Select(stage => gsub.Lookups(langSys, stage.ToHashSet())).Where(l => l.Count > 0).ToList();
                var posLangSys = gpos?.LangSys(tag, lang is null ? "dflt" : lang.PadRight(4)) ?? -1;
                if (posLangSys < 0) posLangSys = gpos?.LangSys(tag, "dflt") ?? -1;
                var pos = posLangSys >= 0 ? gpos!.Lookups(posLangSys, Position) : null;

                // moveFront：形狀換完之後，把從這個位置來的字符搬到最前面再套字距（WPF 把前置形 —— 泰盧固文、馬拉雅拉姆文的 ra —— 畫在主子音前面）
                int Width(IReadOnlyList<int> cps, List<SortedDictionary<int, HashSet<string>>> stages, int moveFront = -1, int before = -1)
                {
                    var input = cps.Select((cp, i) => new LayoutEngine.Glyph(cmap.TryGetValue(cp, out var g) ? g : 0, i, null)).ToList();
                    var shaped = input;
                    for (var k = 0; k < stages.Count; k++)
                    {
                        // 搬動在最後一個階段（呈現用的特性）之前：搬到前面之後才套的規則會換掉字形（泰盧固文 గ్రై 的 ra 下加形換成寬的那個）
                        // 前置形可能是「virama + 子音」合成的字形（記在 virama 的位置上）：兩個位置的都搬
                        if (moveFront >= 0 && k == stages.Count - 1)
                        {
                            bool Moves(LayoutEngine.Glyph g) => g.Cluster == moveFront || g.Cluster == moveFront - 1 && cps[moveFront - 1] == script.Virama;
                            var moving = shaped.Where(Moves).ToList();
                            var rest = shaped.Where(g => !Moves(g)).ToList();
                            // before：搬到這個位置的字形前面（沒有就搬到最前面）
                            var at = before >= 0 ? rest.FindIndex(g => g.Cluster == before) : 0;
                            rest.InsertRange(Math.Max(at, 0), moving);
                            shaped = rest;
                        }
                        shaped = gsub.Shape(shaped, stages[k], NoForms);
                    }
                    if (pos is not null)
                        shaped = gpos!.Shape(shaped, pos, NoForms, script.NoDistOn.Count == 0 ? null : g => g.Cluster >= 0 && g.Cluster < cps.Count && script.NoDistOn.Contains(cps[g.Cluster]));
                    if (Trace is { } trace && trace.Contains(string.Join(",", cps.Select(cp => cp.ToString("X4")))))
                        Console.Error.WriteLine($"[trace] {tag}/{lang ?? "dflt"} {string.Join(",", cps.Select(cp => cp.ToString("X4")))} stages={stages.Count}  [{string.Join(" ", shaped.Select(g => $"{g.Id}:{(g.Id < advances.Length ? advances[g.Id] : 0) + g.Adv}"))}]");
                    return shaped.Sum(g => (g.Id < advances.Length ? advances[g.Id] : 0) + g.Adv);
                }

                if (Trace is not null)
                    foreach (var seq in Trace.Select(t => t.Split(',').Select(h => Convert.ToInt32(h, 16)).ToArray()).Where(q => q.All(cp => cp >= script.First && cp <= script.Last)))
                        Width(seq, full);
                // 「子音 + virama …」後面接的叢集帶母音符號時，把符號搬到第一個子音後面的排法（沒有符號回 null）
                int[]? SignsMoved(IReadOnlyList<int> heads, int[] next)
                {
                    var core = IndicClusters.Stack(script, next).Length;
                    var mods = 0;
                    while (mods < next.Length - core && script.Modifiers.Contains(next[^(mods + 1)])) mods++;
                    var signs = next[core..^mods];
                    if (signs.Length == 0 || signs.Contains(script.Virama)) return null;
                    return [heads[0], .. signs, .. heads.Skip(1), .. next[..core], .. next[^mods..]];
                }
                int[]? ModifiersMoved(IReadOnlyList<int> heads, int[] next)
                {
                    var mods = 0;
                    while (mods < next.Length - 1 && script.Modifiers.Contains(next[^(mods + 1)])) mods++;
                    return mods == 0 ? null : [heads[0], .. next[^mods..], .. heads.Skip(1), .. next[..^mods]];
                }
                // 這個子音接在 virama 後面（或前面）時，這些特性會不會換掉它的字形
                bool Changes(int c, params string[] features)
                {
                    var lookups = gsub.Lookups(langSys, features.ToHashSet());
                    if (lookups.Count == 0 || !cmap.ContainsKey(c)) return false;
                    foreach (var seq in new[] { new[] { c, script.Virama }, new[] { script.Virama, c } })
                    {
                        var input = seq.Select((cp, k) => new LayoutEngine.Glyph(cmap.TryGetValue(cp, out var g) ? g : 0, k, null)).ToList();
                        if (!gsub.Shape(input, lookups, NoForms).Select(g => g.Id).SequenceEqual(input.Select(g => g.Id))) return true;
                    }
                    return false;
                }
                // 照 order 排、形狀換完後把 at 那個子音的字形搬到最前面的寬度
                int Front(IReadOnlyList<int> order, int at, int beforeCp = -1)
                {
                    var i = 1;
                    while (i < order.Count && order[i] != at) i++;
                    var b = -1;
                    if (beforeCp >= 0) for (var k = 0; k < order.Count; k++) if (order[k] == beforeCp) { b = k; break; }
                    return Width(order, full, i < order.Count ? i : -1, b);
                }
                var deltas = new Dictionary<string, int>(StringComparer.Ordinal);
                var widths = new Dictionary<string, int>(StringComparer.Ordinal);
                // 只依疊字記的文字系統：鼻化／送氣符號不展開（另外記一個最大值），省掉五倍的組合
                var all = IndicClusters.Enumerate(script, cmap.ContainsKey, withModifiers: !script.StackKeyed).ToList();
                // 不是主子音的子音（下加形、後加形、前置形）：母音符號會被 WPF 移到它前面；前置形還會被移到主子音前面
                // 叢集太多的文字系統（緬甸文四萬個）不試這些排法：匯出會慢四倍，原本的量法已經對照過（緬甸文的疊字很少帶這種重排）
                var compact = all.Count > 20000;
                var nonBase = compact ? [] : script.Consonants.Where(c => Changes(c, "blwf", "pstf", "pref")).ToHashSet();
                var preBaseForm = compact ? [] : script.Consonants.Where(c => Changes(c, "pref")).ToHashSet();
                if (Trace is not null)
                    Console.Error.WriteLine($"[trace] {tag}/{lang ?? "dflt"} nonBase={string.Join(",", nonBase.Select(c => c.ToString("X4")))} preBase={string.Join(",", preBaseForm.Select(c => c.ToString("X4")))}");
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
                            // 母音符號緊接在主子音後面、下加形與後加形前面（WPF 的重排）：孟加拉文 হ্যৃ 的 ৃ 跟 হ 成合字（寬 17%）、
                            // 卡納達文 ಜ್ಞು 不成 ಜ್ಞ 合字（寬 19%）、泰盧固文 ఖ్రో 的 ra 下加形移到最前面。這支引擎不做這種重排：兩種都算、取寬的
                            var moved = nonBase.Contains(next[0]) ? SignsMoved(head, next) : null;
                            if (moved is not null) together = Math.Max(together, Width(IndicClusters.VisualOrder(script, moved), full));
                            // 鼻化符號移到第一個子音後面（奧里亞文 ଭ୍କୁଁ：WPF 把 ଁ 掛在 ଭ୍କ 合字上，後面的 କ 再畫一次，寬 1.2%）
                            if (script.ModifierMovesToFirst && ModifiersMoved(head, next) is { } modsMoved) together = Math.Max(together, Width(IndicClusters.VisualOrder(script, modsMoved), full));
                            if (preBaseForm.Contains(next[0]))
                            {
                                together = Math.Max(together, Front(IndicClusters.VisualOrder(script, [.. head, .. next]), next[0]));
                                if (moved is not null) together = Math.Max(together, Front(IndicClusters.VisualOrder(script, moved), next[0]));
                            }
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

                // 三個子音連寫（C1 + virama + C2 + virama + C3）：量的時候各叢集直接相加（不套連寫），再加「整串比相加最多寬多少」。
                // 依後兩個子音（C2, C3）記，取所有 C1 裡最寬的：只記一個總的最大值時，卡納達文、泰盧固文的下加形全部被算成全寬
                // （三個子音的字平均偏寬 40～60%）。總的最大值留給四個以上子音的連寫
                if (script.Conjuncts)
                {
                    var heads = all.Where(c => c.Length == 2 && c[1] == script.Virama).ToList();
                    // 叢集太多的文字系統：最後一個子音只試不帶符號的，每一組都記總的最大值（原本的做法；三個子音的疊字在緬甸文裡幾乎不會出現）
                    // （帶母音符號的也試，只是不帶鼻化符號、介音：高棉文 ស្ត្រី 這種帶母音的三個子音很常見）
                    var lasts = all.Where(c => script.Consonants.Contains(c[0]) && c[^1] != script.Virama
                                               && (!compact || !c.Any(cp => script.Modifiers.Contains(cp) || script.Medials.Any(m => m.Contains(cp))))).GroupBy(c => c[0]).ToList();
                    int chain = 0, specialMax = 0;
                    int Residual(int[] h1, int[] h2, int[] c3)
                    {
                        var together = Width(IndicClusters.VisualOrder(script, [.. h1, .. h2, .. c3]), full);
                        // 前置母音放在整串前面或只放在最後一個子音前面（坦米爾文 த்ன்ணெ）：兩種都算、取寬的
                        if (c3.Any(cp => script.PreBase.Contains(cp) || script.Split.ContainsKey(cp)))
                            together = Math.Max(together, Width([.. h1, .. h2, .. IndicClusters.VisualOrder(script, c3)], full));
                        // 母音符號搬到第一個或第二個子音後面（見連寫那段）
                        if (nonBase.Contains(c3[0]) && SignsMoved([.. h1, .. h2], c3) is { } moved)
                        {
                            together = Math.Max(together, Width(IndicClusters.VisualOrder(script, moved), full));
                            if (SignsMoved(h2, c3) is { } moved2) together = Math.Max(together, Width(IndicClusters.VisualOrder(script, [.. h1, .. moved2]), full));
                        }
                        // 鼻化符號移到第一個子音後面（奧里亞文，見連寫那段）
                        if (script.ModifierMovesToFirst && ModifiersMoved([.. h1, .. h2], c3) is { } modsMoved)
                            together = Math.Max(together, Width(IndicClusters.VisualOrder(script, modsMoved), full));
                        // reph 移到後面；前置形移到最前面
                        if (h1[0] == script.Ra) together = Math.Max(together, Width(IndicClusters.VisualOrder(script, [.. h2, .. c3, .. h1]), full));
                        if (preBaseForm.Contains(c3[0]))
                        {
                            together = Math.Max(together, Front(IndicClusters.VisualOrder(script, [.. h1, .. h2, .. c3]), c3[0]));
                            // 第一個子音的 virama 看得見時音節在那裡斷開：前置形只搬到第二個子音前面（馬拉雅拉姆文 ട്ച്ര）
                            together = Math.Max(together, Front(IndicClusters.VisualOrder(script, [.. h1, .. h2, .. c3]), c3[0], h2[0]));
                        }
                        if (preBaseForm.Contains(h2[0])) together = Math.Max(together, Front(IndicClusters.VisualOrder(script, [.. h1, .. h2, .. c3]), h2[0]));
                        return together - widths[IndicClusters.Key(h1)] - widths[IndicClusters.Key(h2)] - widths[IndicClusters.Key(c3)];
                    }
                    // 中間的子音有 reph、後加形、前加形（ra、ya）：這支引擎不做特性遮罩，在連寫中間也照樣套，算出來比 WPF 窄
                    // （孟加拉文 ন্য্ট 窄 16%、奧里亞文 ଲ୍ର୍ଡ଼ 窄 34%）—— 這些用總的最大值
                    bool Special(int c) => c == script.Ra || Changes(c, "rphf", "pstf", "pref", "vatu", "rkrf");
                    var special = new List<string>();
                    foreach (var h2 in heads)
                        foreach (var forms in lasts)
                        {
                            var bare = forms.MinBy(f => f.Length)!;
                            // 不帶符號：每一個 C1 都試
                            var residuals = heads.Select(h1 => (Head: h1, Change: Residual(h1, h2, bare))).ToList();
                            var widest = residuals.Max(x => x.Change);
                            // 帶 nukta、母音符號、鼻化符號：只拿最寬的兩個 C1 與 ra 試「比不帶時多寬多少」（全部試要多二十倍的時間）。
                            // 例外：後兩個子音都是下加形／後加形時主子音是 C1，母音符號掛在 C1 上跟它成合字（泰盧固文 ఫ్ఞ్యె 的 ె 跟 ఫ 合成一個字）：
                            // 多寬多少看 C1，每個 C1 都試（不帶鼻化符號的那些）
                            var sample = residuals.OrderByDescending(x => x.Change).Take(2).Concat(residuals.Where(x => x.Head[0] == script.Ra)).ToList();
                            var signOnFirst = script.BaseIsFirst || nonBase.Contains(h2[0]) && nonBase.Contains(forms.Key);
                            // 依最後一個子音帶的符號分開記（跟兩子音連寫一樣；不分的話卡納達、泰盧固、奧里亞文三子音的字平均偏寬 35～46%）
                            var bySign = new Dictionary<int, int> { [-1] = widest };
                            foreach (var form in forms)
                            {
                                if (ReferenceEquals(form, bare)) continue;
                                var hasNukta = script.Nukta >= 0 && form.Contains(script.Nukta);
                                var sign = IndicClusters.SignOf(script, form, 0);
                                var extra = 0;
                                foreach (var (h1, change) in signOnFirst && !hasNukta && !form.Any(script.Modifiers.Contains) ? residuals : sample)
                                {
                                    var residual = Residual(h1, h2, form);
                                    // 最後一個子音帶 nukta：WPF 整串都不連寫（奧里亞文 ନ୍ଫ୍ଯ଼ 三個 virama 都看得見，這支引擎照樣連成合字，窄 30%）
                                    if (hasNukta) residual = Math.Max(residual, 0);
                                    extra = Math.Max(extra, residual - change);
                                }
                                bySign[sign] = Math.Max(bySign.GetValueOrDefault(sign, widest), widest + extra);
                            }
                            chain = Math.Max(chain, bySign.Values.Max());
                            if (Special(h2[0]))
                            {
                                foreach (var (sign, v) in bySign)
                                {
                                    special.Add(sign < 0 ? IndicClusters.ChainKey3(h2[0], forms.Key) : IndicClusters.ChainKey3(h2[0], forms.Key, sign));
                                    specialMax = Math.Max(specialMax, v);
                                }
                            }
                            else
                            {
                                var general = bySign[-1];
                                if (general != 0) deltas[IndicClusters.ChainKey3(h2[0], forms.Key)] = general;
                                foreach (var (sign, v) in bySign)
                                    if (sign >= 0 && v > general) deltas[IndicClusters.ChainKey3(h2[0], forms.Key, sign)] = v;
                            }
                        }
                    if (chain > 0)
                    {
                        deltas[IndicClusters.ChainKey] = chain;
                        // 這些組合裡最寬的（不拿全部組合的最大值：會被帶 nukta 的、母音符號掛在 C1 上的撐大，孟加拉文平均偏寬 0.9% → 1.3%）
                        foreach (var key in special) deltas[key] = specialMax;
                        if (compact)
                            foreach (var h2 in heads)
                                foreach (var forms in lasts)
                                {
                                    deltas[IndicClusters.ChainKey3(h2[0], forms.Key)] = chain;
                                    foreach (var key in deltas.Keys.Where(k => k.StartsWith(IndicClusters.ChainKey3(h2[0], forms.Key) + ",", StringComparison.Ordinal)).ToList()) deltas.Remove(key);
                                }
                    }
                }

                // 叢集會影響下一個叢集的文字系統：每個叢集接「每一種開頭」的叢集時最多變寬多少（開頭 = 畫出來的前兩個字）
                if (script.Interacting && script.BoundaryTail > 0)
                {
                    // 依結尾記：每個叢集接「每一種開頭」（畫出來的第一個字）時最多變寬多少，同一個結尾取最大
                    var firsts = all.GroupBy(c => IndicClusters.VisualOrder(script, c)[0]).Select(g => g.OrderBy(c => c.Length).First()).ToList();
                    // （試過另外依「下一個叢集畫出來的第一個字」分開記：撐寬還看下一個叢集後面的符號，用最短的代表字試會偏窄 12%，撤掉）
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
            });
            // 順序固定（度量檔每次匯出要一樣）
            return result.Count > 0 ? result.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) : null;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException
                                  || e is AggregateException a && a.InnerExceptions.All(x => x is IndexOutOfRangeException or ArgumentOutOfRangeException))
        {
            return null;
        }
    }
}
