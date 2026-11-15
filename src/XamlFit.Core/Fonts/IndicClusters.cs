namespace XamlFit.Core.Fonts;

/// <summary>
/// 印度系文字（天城文、孟加拉文、古木基文、奧里亞文、坦米爾文、卡納達文、馬拉雅拉姆文、僧伽羅文）的字素叢集：一個母音字母，或一個子音（可加 nukta）後面接
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
        /// <summary>virama 一律看得見、跟子音組成合字（僧伽羅文）：單獨的「子音 + virama」也照全部特性跑。</summary>
        public bool VisibleVirama { get; init; }
        /// <summary>
        /// 叢集會影響下一個叢集（古吉拉特文、泰盧固文：鼻化／送氣符號、兩段式母音把下一個字推開）：
        /// 不用連寫的變窄量（匯出引擎對它們的連寫不夠準），改成每個叢集記「接任何叢集時最多變寬多少」，量的時候每個叢集交界都加上去。
        /// </summary>
        public bool Interacting { get; init; }
    }

    /// <summary>度量檔裡記「子音 + virama 後面接子音」改變量的鍵前綴（＋子音碼位）。</summary>
    public const string ConjunctPrefix = "+";

    /// <summary>「前一個子音 + virama，後面接這個子音」的改變量的鍵。表是完整的：沒有這個鍵就是 0。</summary>
    public static string ConjunctKey(int first, int second) => $"{ConjunctPrefix}{first},{second}";

    /// <summary>度量檔裡記「叢集後面接標點或空白時最多變寬多少」的鍵（標點跟著併進同一段，字型的字距會套上去）。</summary>
    public const string TrailingKey = "+1";

    /// <summary>「這個叢集後面接同一個文字系統的叢集時最多變寬多少」的鍵（只有 <see cref="Script.Interacting"/> 的文字系統有）。</summary>
    public static string BoundaryKey(IEnumerable<int> cluster) => ">" + Key(cluster);

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

    private static HashSet<int> Without(HashSet<int> set, params int[] gaps)
    {
        set.ExceptWith(gaps);
        return set;
    }

    public static readonly Script Bengali = new(
        "beng", "bng2", "beng", 0x0980, 0x09FF,
        Consonants: [.. Without(Range(0x0995, 0x09B9), 0x09A9, 0x09B1, 0x09B3, 0x09B4, 0x09B5), 0x09DC, 0x09DD, 0x09DF, 0x09F0, 0x09F1],
        Vowels: [.. Without(Range(0x0985, 0x0994), 0x098D, 0x098E, 0x0991, 0x0992), 0x09E0, 0x09E1],
        Signs: [.. Range(0x09BE, 0x09C4), 0x09C7, 0x09C8, 0x09CB, 0x09CC, 0x09D7, 0x09E2, 0x09E3],
        Virama: 0x09CD, Nukta: 0x09BC,
        Modifiers: [.. Range(0x0981, 0x0983)],
        Plain: [.. Range(0x09E6, 0x09FB)])
    {
        PreBase = [0x09BF, 0x09C7, 0x09C8],
        Split = new() { [0x09CB] = [0x09C7, 0x09BE], [0x09CC] = [0x09C7, 0x09D7] },
    };

    public static readonly Script Gurmukhi = new(
        "guru", "gur2", "guru", 0x0A00, 0x0A7F,
        Consonants: [.. Without(Range(0x0A15, 0x0A39), 0x0A29, 0x0A31, 0x0A34, 0x0A37), 0x0A59, 0x0A5A, 0x0A5B, 0x0A5C, 0x0A5E],
        Vowels: [.. Without(Range(0x0A05, 0x0A14), 0x0A0B, 0x0A0C, 0x0A0D, 0x0A0E, 0x0A11, 0x0A12), 0x0A72, 0x0A73],
        Signs: [.. Range(0x0A3E, 0x0A42), 0x0A47, 0x0A48, 0x0A4B, 0x0A4C],
        Virama: 0x0A4D, Nukta: 0x0A3C,
        Modifiers: [0x0A01, 0x0A02, 0x0A03, 0x0A70],   // 0A71 addak 在子音前面起作用：沒列，用到就量不到
        Plain: [.. Range(0x0A66, 0x0A6F), 0x0A74])
    {
        PreBase = [0x0A3F],
    };

    public static readonly Script Gujarati = new(
        "gujr", "gjr2", "gujr", 0x0A80, 0x0AFF,
        Consonants: [.. Without(Range(0x0A95, 0x0AB9), 0x0AA9, 0x0AB1, 0x0AB4), 0x0AF9],
        Vowels: [.. Without(Range(0x0A85, 0x0A94), 0x0A8E, 0x0A92), 0x0AE0, 0x0AE1],
        Signs: [.. Range(0x0ABE, 0x0AC5), .. Range(0x0AC7, 0x0AC9), 0x0ACB, 0x0ACC, 0x0AE2, 0x0AE3],
        Virama: 0x0ACD, Nukta: 0x0ABC,
        Modifiers: [.. Range(0x0A81, 0x0A83)],
        Plain: [.. Range(0x0AE6, 0x0AF1)])
    {
        PreBase = [0x0ABF],
        Interacting = true,
    };

    public static readonly Script Oriya = new(
        "orya", "ory2", "orya", 0x0B00, 0x0B7F,
        Consonants: [.. Without(Range(0x0B15, 0x0B39), 0x0B29, 0x0B31, 0x0B34), 0x0B5C, 0x0B5D, 0x0B5F, 0x0B71],
        Vowels: [.. Without(Range(0x0B05, 0x0B14), 0x0B0D, 0x0B0E, 0x0B11, 0x0B12), 0x0B60, 0x0B61],
        Signs: [.. Range(0x0B3E, 0x0B44), 0x0B47, 0x0B48, 0x0B4B, 0x0B4C, 0x0B56, 0x0B57, 0x0B62, 0x0B63],
        Virama: 0x0B4D, Nukta: 0x0B3C,
        Modifiers: [.. Range(0x0B01, 0x0B03)],
        Plain: [.. Range(0x0B66, 0x0B70), .. Range(0x0B72, 0x0B77)])
    {
        PreBase = [0x0B47],
        Split = new() { [0x0B48] = [0x0B47, 0x0B56], [0x0B4B] = [0x0B47, 0x0B3E], [0x0B4C] = [0x0B47, 0x0B57] },
    };

    public static readonly Script Telugu = new(
        "telu", "tel2", "telu", 0x0C00, 0x0C7F,
        Consonants: [.. Without(Range(0x0C15, 0x0C39), 0x0C29), 0x0C58, 0x0C59, 0x0C5A],
        Vowels: [.. Without(Range(0x0C05, 0x0C14), 0x0C0D, 0x0C11), 0x0C60, 0x0C61],
        Signs: [.. Range(0x0C3E, 0x0C44), .. Range(0x0C46, 0x0C48), .. Range(0x0C4A, 0x0C4C), 0x0C55, 0x0C56, 0x0C62, 0x0C63],
        Virama: 0x0C4D, Nukta: -1,   // 0C3C 是 Unicode 10 才加的，字型多半不會組：量不到
        Modifiers: [.. Range(0x0C00, 0x0C03)],
        Plain: [.. Range(0x0C66, 0x0C6F), .. Range(0x0C78, 0x0C7F)])
    {
        Split = new() { [0x0C48] = [0x0C46, 0x0C56] },
        Interacting = true,
    };

    public static readonly Script Kannada = new(
        "knda", "knd2", "knda", 0x0C80, 0x0CFF,
        Consonants: [.. Without(Range(0x0C95, 0x0CB9), 0x0CA9, 0x0CB4)],   // 0CDE ೞ 已廢用
        Vowels: [.. Without(Range(0x0C85, 0x0C94), 0x0C8D, 0x0C91), 0x0CE0, 0x0CE1],
        Signs: [.. Range(0x0CBE, 0x0CC4), .. Range(0x0CC6, 0x0CC8), .. Range(0x0CCA, 0x0CCC), 0x0CD5, 0x0CD6, 0x0CE2, 0x0CE3],
        Virama: 0x0CCD, Nukta: -1,   // nukta + virama 字型組不起來（WPF 畫得比我們寬 30%）：用到就量不到
        Modifiers: [.. Range(0x0C81, 0x0C83)],
        Plain: [.. Range(0x0CE6, 0x0CEF)])
    {
        Split = new()
        {
            [0x0CC0] = [0x0CBF, 0x0CD5], [0x0CC7] = [0x0CC6, 0x0CD5], [0x0CC8] = [0x0CC6, 0x0CD6],
            [0x0CCA] = [0x0CC6, 0x0CC2], [0x0CCB] = [0x0CC6, 0x0CC2, 0x0CD5],
        },
    };

    public static readonly Script Malayalam = new(
        "mlym", "mlm2", "mlym", 0x0D00, 0x0D7F,
        Consonants: [.. Range(0x0D15, 0x0D3A)],
        // 獨立的 chillu 字母當成不接母音符號的字母
        Vowels: [.. Without(Range(0x0D05, 0x0D14), 0x0D0D, 0x0D11), 0x0D60, 0x0D61, .. Range(0x0D54, 0x0D56), .. Range(0x0D7A, 0x0D7F)],
        Signs: [.. Range(0x0D3E, 0x0D44), .. Range(0x0D46, 0x0D48), .. Range(0x0D4A, 0x0D4C), 0x0D57, 0x0D62, 0x0D63],
        Virama: 0x0D4D, Nukta: -1,
        Modifiers: [.. Range(0x0D00, 0x0D03)],
        Plain: [.. Range(0x0D66, 0x0D79), 0x0D4F])
    {
        PreBase = [0x0D46, 0x0D47, 0x0D48],
        Split = new() { [0x0D4A] = [0x0D46, 0x0D3E], [0x0D4B] = [0x0D47, 0x0D3E], [0x0D4C] = [0x0D46, 0x0D57] },
    };

    public static readonly Script Sinhala = new(
        "sinh", "sinh", "sinh", 0x0D80, 0x0DFF,
        Consonants: [.. Without(Range(0x0D9A, 0x0DC6), 0x0DB2, 0x0DBC, 0x0DBE, 0x0DBF)],
        Vowels: [.. Range(0x0D85, 0x0D96)],
        Signs: [.. Range(0x0DCF, 0x0DD4), 0x0DD6, .. Range(0x0DD8, 0x0DDF), 0x0DF2, 0x0DF3],
        Virama: 0x0DCA, Nukta: -1,
        Modifiers: [.. Range(0x0D81, 0x0D83)],
        Plain: [.. Range(0x0DE6, 0x0DEF), 0x0DF4])
    {
        PreBase = [0x0DD9, 0x0DDA, 0x0DDB],
        VisibleVirama = true,
        Split = new() { [0x0DDA] = [0x0DD9, 0x0DCA], [0x0DDC] = [0x0DD9, 0x0DCF], [0x0DDD] = [0x0DD9, 0x0DCF, 0x0DCA], [0x0DDE] = [0x0DD9, 0x0DDF] },
    };

    /// <summary>
    /// 有量的文字系統。古吉拉特文、泰盧固文定義在上面但**不在這裡**：WpfOracle --scripts 對照時叢集之間的附加符號與兩段式母音
    /// 讓整個字比叢集相加寬到 3%（偏窄、危險的那邊），叢集模型處理不了 —— 用到就量不到。
    /// </summary>
    public static readonly Script[] All = [Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada, Malayalam, Sinhala];

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

    /// <summary>叢集是不是「子音 ＋ virama」，是的話回傳子音；否則 -1。</summary>
    public static int ConjunctHead(Script s, IReadOnlyList<int> cluster) =>
        // 只有「子音 + virama」兩個字（匯出時只算這種）；帶 nukta 的子音連寫起來不一樣，不套
        cluster.Count == 2 && cluster[1] == s.Virama && s.Consonants.Contains(cluster[0]) ? cluster[0] : -1;

    /// <summary>WPF 依元素的語言挑 OpenType 語言系統：馬拉地文的 ल、श 字形不一樣（Nirmala UI 的 MAR）。</summary>
    public static string? LanguageSystem(string? language) =>
        (language ?? "").Split('-', '_')[0].ToLowerInvariant() switch
        {
            "mr" => "MAR", "hi" => "HIN", "ne" => "NEP", "ta" => "TAM", "sa" => "SAN", "kok" => "KOK",
            "bn" => "BEN", "as" => "ASM", "pa" => "PAN", "gu" => "GUJ", "or" => "ORI", "te" => "TEL", "kn" => "KAN",
            "ml" => "MAL", "si" => "SNH",
            _ => null,
        };

    /// <summary>度量檔裡的鍵：文字系統，或「文字系統/語言系統」。</summary>
    public static string DataKey(Script s, string? languageSystem) => languageSystem is null ? s.Tag : $"{s.Tag}/{languageSystem}";
}
