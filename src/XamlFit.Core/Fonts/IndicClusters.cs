namespace XamlFit.Core.Fonts;

/// <summary>
/// 印度系文字（目前：天城文、坦米爾文）的字素叢集：一個母音字母，或一個子音（可加 nukta）後面接
/// 一個母音符號或一個 virama，最後最多一個鼻化／送氣符號。
///
/// 量法：每個叢集的寬度在匯出時用字型的 GSUB／GPOS 算好（OpenType.IndicExport），一段文字的寬度是各叢集相加。
/// WpfOracle --indic 實測（Nirmala UI）：馬拉地文 1,153 個字，叢集相加從來不比整個字窄 —— 連字（半形子音、合字）只會讓整個字變窄。
/// 叢集對不上這個樣式（兩個母音符號、ZWJ…）就是量不到。
/// </summary>
public static class IndicClusters
{
    public sealed record Script(string Tag, string Tag2, string Tag1, int First, int Last,
        HashSet<int> Consonants, HashSet<int> Vowels, HashSet<int> Signs, int Virama, int Nukta, HashSet<int> Modifiers, HashSet<int> Plain)
    {
        /// <summary>畫在子音前面的母音符號（天城文的 ि；坦米爾文的 ெ ே ை）。字型的字距是照畫出來的順序套的。</summary>
        public HashSet<int> PreBase { get; init; } = [];
        /// <summary>兩段式母音符號 → 前段 + 後段（坦米爾文的 ொ ோ ௌ，Unicode 正規分解）。</summary>
        public Dictionary<int, int[]> Split { get; init; } = [];
    }

    /// <summary>度量檔裡記「子音 + virama 後面接子音」改變量的鍵前綴（＋子音碼位）。</summary>
    public const string ConjunctPrefix = "+";

    /// <summary>度量檔裡記「叢集後面接標點或空白時最多變寬多少」的鍵（標點跟著併進同一段，字型的字距會套上去）。</summary>
    public const string TrailingKey = "+1";

    /// <summary>匯出時試的標點與空白。</summary>
    public const string TrailingChars = " ,.:;!?()[]'\"-/…";

    private static HashSet<int> Range(int a, int b, params int[] except)
    {
        var set = new HashSet<int>();
        for (var c = a; c <= b; c++) if (!except.Contains(c)) set.Add(c);
        return set;
    }

    public static readonly Script Devanagari = new(
        "deva", "dev2", "deva", 0x0900, 0x097F,
        Consonants: [.. Range(0x0915, 0x0939), .. Range(0x0958, 0x095F), .. Range(0x0978, 0x097F)],
        Vowels: [.. Range(0x0904, 0x0914), .. Range(0x0960, 0x0961), .. Range(0x0972, 0x0977)],
        Signs: [.. Range(0x093A, 0x093B), .. Range(0x093E, 0x094C), .. Range(0x094E, 0x094F), .. Range(0x0955, 0x0957), .. Range(0x0962, 0x0963)],
        Virama: 0x094D, Nukta: 0x093C,
        Modifiers: [.. Range(0x0900, 0x0903)],
        Plain: [.. Range(0x0964, 0x0971)])   // danda、數字、縮寫號
    {
        PreBase = [0x093F, 0x094E],
    };

    public static readonly Script Tamil = new(
        "taml", "tml2", "taml", 0x0B80, 0x0BFF,
        Consonants: [.. Range(0x0B95, 0x0BB9)],
        Vowels: [.. Range(0x0B85, 0x0B94)],
        Signs: [.. Range(0x0BBE, 0x0BCC), 0x0BD7],
        Virama: 0x0BCD, Nukta: -1,
        Modifiers: [0x0B82],
        Plain: [0x0B83, .. Range(0x0BD0, 0x0BD0), .. Range(0x0BE6, 0x0BFA)])
    {
        PreBase = [0x0BC6, 0x0BC7, 0x0BC8],
        Split = new() { [0x0BCA] = [0x0BC6, 0x0BBE], [0x0BCB] = [0x0BC7, 0x0BBE], [0x0BCC] = [0x0BC6, 0x0BD7] },
    };

    public static readonly Script[] All = [Devanagari, Tamil];

    public static Script? Of(int cp)
    {
        foreach (var s in All) if (cp >= s.First && cp <= s.Last) return s;
        return null;
    }

    /// <summary>從 <paramref name="i"/> 開始的叢集長度；對不上回 0。</summary>
    public static int Match(IReadOnlyList<int> cps, int i, Script s)
    {
        var j = i;
        var cp = cps[j];
        if (s.Vowels.Contains(cp)) j++;
        else if (s.Consonants.Contains(cp))
        {
            j++;
            if (j < cps.Count && cps[j] == s.Nukta) j++;
            if (j < cps.Count && (s.Signs.Contains(cps[j]) || cps[j] == s.Virama)) j++;
        }
        else return 0;
        // virama 後面再接鼻化符號是不合法的組合：WPF 畫一個虛線圓圈佔位（寬很多），量不到
        if (j < cps.Count && s.Modifiers.Contains(cps[j]))
        {
            if (cps[j - 1] == s.Virama) return 0;
            j++;
        }
        return j - i;
    }

    /// <summary>匯出時列舉的全部叢集（跟 <see cref="Match"/> 認得的一樣）。</summary>
    public static IEnumerable<int[]> Enumerate(Script s, Func<int, bool> drawable)
    {
        IEnumerable<int[]> WithModifier(int[] core)
        {
            yield return core;
            foreach (var m in s.Modifiers) if (drawable(m)) yield return [.. core, m];
        }

        foreach (var v in s.Vowels.Order())
            if (drawable(v)) foreach (var c in WithModifier([v])) yield return c;

        foreach (var c in s.Consonants.Order())
        {
            if (!drawable(c)) continue;
            foreach (var head in s.Nukta >= 0 && drawable(s.Nukta) ? new[] { new[] { c }, new[] { c, s.Nukta } } : [[c]])
            {
                foreach (var x in WithModifier(head)) yield return x;
                foreach (var sign in s.Signs.Order())
                    if (drawable(sign)) foreach (var x in WithModifier([.. head, sign])) yield return x;
                if (drawable(s.Virama)) yield return [.. head, s.Virama];
            }
        }
    }

    public static string Key(IEnumerable<int> cps) => string.Join(",", cps);

    /// <summary>畫出來的順序：兩段式母音拆開，畫在前面的母音符號移到叢集最前面。</summary>
    public static List<int> VisualOrder(Script s, IReadOnlyList<int> cluster)
    {
        var list = new List<int>();
        foreach (var cp in cluster)
            if (s.Split.TryGetValue(cp, out var parts)) list.AddRange(parts); else list.Add(cp);
        var pre = list.FindIndex(s.PreBase.Contains);
        if (pre > 0) { var m = list[pre]; list.RemoveAt(pre); list.Insert(0, m); }
        return list;
    }

    /// <summary>叢集是不是「子音（＋nukta）＋ virama」，是的話回傳子音；否則 -1。</summary>
    public static int ConjunctHead(Script s, IReadOnlyList<int> cluster) =>
        cluster.Count >= 2 && cluster[^1] == s.Virama && s.Consonants.Contains(cluster[0]) ? cluster[0] : -1;

    /// <summary>WPF 依元素的語言挑 OpenType 語言系統：馬拉地文的 ल、श 字形不一樣（Nirmala UI 的 MAR）。</summary>
    public static string? LanguageSystem(string? language) =>
        (language ?? "").Split('-', '_')[0].ToLowerInvariant() switch
        {
            "mr" => "MAR", "hi" => "HIN", "ne" => "NEP", "ta" => "TAM", "sa" => "SAN", "kok" => "KOK",
            _ => null,
        };

    /// <summary>度量檔裡的鍵：文字系統，或「文字系統/語言系統」。</summary>
    public static string DataKey(Script s, string? languageSystem) => languageSystem is null ? s.Tag : $"{s.Tag}/{languageSystem}";
}
