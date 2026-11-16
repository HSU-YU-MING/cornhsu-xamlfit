namespace XamlFit.Core.Fonts;

/// <summary>
/// 印度系文字（天城、孟加拉、古木基、古吉拉特、奧里亞、坦米爾、泰盧固、卡納達、馬拉雅拉姆、僧伽羅文）與高棉文、緬甸文、藏文的字素叢集：
/// 一個母音字母，或一個子音（可加 nukta、疊在下面的子音、介音）後面接一個母音符號（或一串，見 <see cref="Script.SignSequences"/>）
/// 或一個 virama，最後最多一個鼻化／送氣／聲調符號（或一串）。
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
        /// 每個叢集記「接任何叢集時最多變寬多少」，量的時候每個叢集交界都加上去。
        /// </summary>
        public bool Interacting { get; init; }
        /// <summary>
        /// 記連寫（子音 + virama + 子音）變窄多少。關掉就是兩個叢集相加（偏寬）。泰盧固文以前關著（匯出引擎把 ళ్కి 拼窄 2.8%）；
        /// 匯出時模擬 WPF 的音節重排之後打開了（窮舉每個連寫偏窄 ≤ 0.03%，平均偏寬 8.2% → 3.7%）。
        /// </summary>
        public bool Conjuncts { get; init; } = true;
        /// <summary>
        /// 子音 + virama 不跟後面的子音組成一個音節（坦米爾文：virama 看得見）：後一個叢集的前置母音只移到它自己的子音前面，
        /// 不移到整串最前面（ன்னோ = ன் + ெ + ன + ா）。
        /// </summary>
        public bool SyllableEndsAtVirama { get; init; }
        /// <summary>
        /// 前置母音可能讓連寫不成形（馬拉雅拉姆文：ഠ്ഛ 是合字，ഠ്ഛേ 在 WPF 不連）：帶前置母音的連寫兩種放法都算、取變窄少的。
        /// 天城文不要開：ि 一定放在整串連寫前面，兩種都算只會白白偏寬（馬拉地文 1.3% → 2.3%）。
        /// </summary>
        public bool PreBaseMayBreakConjunct { get; init; }
        /// <summary>
        /// 大於 0：叢集交界的變寬量只依叢集的最後幾個字記（緬甸文 4 萬個叢集，一個一個記太多；變寬來自結尾的符號：ပျု့ + ငွီ 寬 2 px）。
        /// </summary>
        public int BoundaryTail { get; init; }
        /// <summary>
        /// 會變成 reph 的子音（ra）。「ra + virama」接子音時，WPF 把 reph 移到後一個子音後面；這支引擎照輸入的順序排：
        /// 兩種順序都算、取變窄少的（卡納達文 ರ್ಙ 差 0.5 px）。
        /// </summary>
        public int Ra { get; init; } = -1;
        /// <summary>
        /// 連寫後面的鼻化符號被 WPF 掛到第一個子音（或合字）上（奧里亞文 ଭ୍କୁଁ：ଁ 掛在 ଭ୍କ 合字上、後面的 କ 再畫一次，寬 1.2%）。
        /// 匯出時多算一種排法、取寬的；別的文字系統開了只會白白偏寬。
        /// </summary>
        public bool ModifierMovesToFirst { get; init; }
        /// <summary>只能緊接在子音後面的符號（高棉文的 robat ៌：接在下標子音或母音後面 WPF 畫虛線圓圈，寬到 3 倍）。</summary>
        public HashSet<int> BaseOnlyModifiers { get; init; } = [];
        /// <summary>一串連寫裡只能出現一次的下標子音（高棉文的 coeng ro：兩個 ្រ WPF 畫虛線圓圈）；-1 = 沒有。</summary>
        public int SingleSubjoined { get; init; } = -1;
        /// <summary>
        /// 主子音一定是第一個（高棉文：coeng 後面的子音都是下標）：三子音連寫帶母音符號時，符號改的是第一個子音的寬度
        /// （Leelawadee UI 的 វ្ច្ឌី，ី 讓 វ 寬 150 單位），匯出時每個第一子音都要試。
        /// </summary>
        public bool BaseIsFirst { get; init; }
        /// <summary>疊在下面的子音（藏文 ྐ…）：子音後面最多接 <see cref="MaxSubjoined"/> 個；第二個以後只能是 <see cref="LaterSubjoined"/> 裡的（藏文的 ya、ra、la、wa）。</summary>
        public HashSet<int> Subjoined { get; init; } = [];
        public int MaxSubjoined { get; init; }
        public HashSet<int>? LaterSubjoined { get; init; }
        /// <summary>介音的組合（緬甸文 ျ ြ ွ ှ，照 Unicode 的儲存順序）。</summary>
        public int[][] Medials { get; init; } = [];
        /// <summary>母音符號可以是一串（緬甸文 ော်、高棉文「聲調轉換符號 + 母音」）；空的 = <see cref="Signs"/> 裡每一個單獨一個。</summary>
        public int[][] SignSequences { get; init; } = [];
        /// <summary>鼻化／聲調符號可以是一串（緬甸文 ံ့）；空的 = <see cref="Modifiers"/> 裡每一個單獨一個。第一個字一定要在 <see cref="Modifiers"/> 裡。</summary>
        public int[][] ModifierSequences { get; init; } = [];
        internal int[][] SignSeqs => signSeqs ??= SignSequences.Length > 0 ? SignSequences : [.. Signs.Order().Select(x => new[] { x })];
        internal int[][] ModifierSeqs => modifierSeqs ??= ModifierSequences.Length > 0 ? ModifierSequences : [.. Modifiers.Order().Select(x => new[] { x })];
        private int[][]? signSeqs, modifierSeqs;
        /// <summary>不合法的相鄰兩個字（緬甸文 ် 後面接 ့ 或 ံ：正確的順序是 ့် ）：WPF 畫虛線圓圈，量不到。</summary>
        public HashSet<(int, int)> InvalidPairs { get; init; } = [];
        /// <summary>
        /// 可有可無、不佔寬度的符號（高棉文的聲調轉換符號 ៉ ៊）：匯出時有它跟沒它各算一次，取寬的
        /// （這支匯出引擎把「ត៉ា」算得比 WPF 窄 0.44 px；WPF 畫起來跟「តា」一樣寬）。
        /// </summary>
        public HashSet<int> Droppable { get; init; } = [];
        /// <summary>
        /// 只依「疊字」（子音 + 疊在下面的子音）記改變量，取各種母音符號裡最寬的；鼻化／送氣符號另外記一個「最多讓疊字變寬多少」
        /// （<see cref="ModifierKey"/>）。藏文：全部組合有 24 萬個，疊字只有 2,465 個；Microsoft Himalaya 上這樣量平均只多 0.4% 一個音節。
        /// </summary>
        public bool StackKeyed { get; init; }
    }

    /// <summary>度量檔裡記「子音 + virama 後面接子音」改變量的鍵前綴（＋子音碼位）。</summary>
    public const string ConjunctPrefix = "+";

    /// <summary>「前一個子音 + virama，後面接這個子音」的改變量的鍵。表是完整的：沒有這個鍵就是 0。</summary>
    public static string ConjunctKey(int first, int second) => $"{ConjunctPrefix}{first},{second}";

    /// <summary>同上，但只給後一個叢集帶這個母音符號的時候（有這個鍵就用它，不用上面那個）。</summary>
    public static string ConjunctKey(int first, int second, int sign) => $"{ConjunctPrefix}{first},{second},{sign}";

    /// <summary>
    /// 連寫改變量分組用：從 <paramref name="start"/> 開始的叢集（子音開頭）帶 nukta 就回 nukta（帶 nukta 的子音常常不連寫：奧里亞文 ଭ଼），
    /// 否則回第一個母音符號或鼻化符號；都沒有回 -1。
    /// </summary>
    public static int SignOf(Script s, IReadOnlyList<int> cps, int start)
    {
        var k = start + 1;
        if (k < cps.Count && cps[k] == s.Nukta) return s.Nukta;
        return k < cps.Count && (s.Signs.Contains(cps[k]) || s.Modifiers.Contains(cps[k])) ? cps[k] : -1;
    }

    /// <summary>度量檔裡記「叢集後面接標點或空白時最多變寬多少」的鍵（標點跟著併進同一段，字型的字距會套上去）。</summary>
    public const string TrailingKey = "+1";

    /// <summary>「這個叢集後面接同一個文字系統的叢集時最多變寬多少」的鍵（只有 <see cref="Script.Interacting"/> 的文字系統有）。</summary>
    public static string BoundaryKey(IEnumerable<int> cluster) => ">" + Key(cluster);

    /// <summary>
    /// 三個以上子音連寫（子音 + virama + 子音 + virama + 子音）時，整串比各叢集相加最多寬多少的鍵：量的時候這種連寫各叢集直接相加，
    /// 再加這個值（奧里亞文 ଲ୍ୱ୍ଢ଼ 比相加寬 1.15 px）。
    /// </summary>
    public const string ChainKey = "+3";

    /// <summary>
    /// 剛好三個子音（C1 + virama + C2 + virama + C3）時，依後兩個子音記的「整串比各叢集相加最多寬多少」（取所有 C1、C3 帶的各種符號裡最寬的）。
    /// 表是完整的：沒有這個鍵就是 0。存成 [改變量, -3, -C2, -C3]（3 不是任何文字系統的子音碼位，跟連寫的鍵分得開）。
    /// </summary>
    public static string ChainKey3(int second, int third) => $"{ChainKey},{second},{third}";

    /// <summary>同上，最後一個子音帶這個符號（nukta、母音符號、鼻化符號，見 <see cref="SignOf"/>）的時候（有這個鍵就用它，沒有用上面那個）。</summary>
    public static string ChainKey3(int second, int third, int sign) => $"{ChainKey},{second},{third},{sign}";

    /// <summary>「鼻化／送氣符號最多讓疊字變寬多少」的鍵（只有 <see cref="Script.StackKeyed"/> 的文字系統有）。</summary>
    public const string ModifierKey = "+2";   // 存成 [改變量, -2]，跟 TrailingKey 同一種寫法

    /// <summary>疊字的部分：子音、nukta、疊在下面的子音（<see cref="Script.StackKeyed"/> 的文字系統用它當鍵）。</summary>
    public static int[] Stack(Script s, IReadOnlyList<int> cluster)
    {
        var n = 0;
        while (n < cluster.Count && (n == 0 ? s.Consonants.Contains(cluster[n]) : cluster[n] == s.Nukta || s.Subjoined.Contains(cluster[n])))
            n++;
        return [.. cluster.Take(Math.Max(n, 1))];
    }

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
        InvalidPairs = [.. ((int[])[0x0929, 0x0931, 0x0934, .. Range(0x0958, 0x095F)]).Select(c => (c, 0x093C))],   // 本身已經帶 nukta 的字母再接 nukta
        Ra = 0x0930,
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
        SyllableEndsAtVirama = true,
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
        InvalidPairs = [.. ((int[])[0x09DC, 0x09DD, 0x09DF]).Select(c => (c, 0x09BC))],   // 本身已經帶 nukta 的字母再接 nukta
        Ra = 0x09B0,
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
        InvalidPairs = [.. ((int[])[0x0A33, 0x0A36, 0x0A59, 0x0A5A, 0x0A5B, 0x0A5E]).Select(c => (c, 0x0A3C))],   // 本身已經帶 nukta 的字母再接 nukta
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
        Ra = 0x0AB0,
        PreBase = [0x0ABF],
        Interacting = true,
    };

    public static readonly Script Oriya = new(
        "orya", "ory2", "orya", 0x0B00, 0x0B7F,
        Consonants: [.. Without(Range(0x0B15, 0x0B39), 0x0B29, 0x0B31, 0x0B34), 0x0B5C, 0x0B5D, 0x0B5F, 0x0B71],
        Vowels: [.. Without(Range(0x0B05, 0x0B14), 0x0B0D, 0x0B0E, 0x0B11, 0x0B12), 0x0B60, 0x0B61],
        Signs: [.. Range(0x0B3E, 0x0B44), 0x0B47, 0x0B48, 0x0B4B, 0x0B4C, 0x0B56, 0x0B57],   // ୢ ୣ（母音 L）不收：這支引擎跟 WPF 連寫不一樣
        Virama: 0x0B4D, Nukta: 0x0B3C,
        Modifiers: [.. Range(0x0B01, 0x0B03)],
        Plain: [.. Range(0x0B66, 0x0B70), .. Range(0x0B72, 0x0B77)])
    {
        InvalidPairs = [.. ((int[])[0x0B5C, 0x0B5D]).Select(c => (c, 0x0B3C))],   // 本身已經帶 nukta 的字母再接 nukta
        Ra = 0x0B30,
        ModifierMovesToFirst = true,
        PreBase = [0x0B47],
        Split = new() { [0x0B48] = [0x0B47, 0x0B56], [0x0B4B] = [0x0B47, 0x0B3E], [0x0B4C] = [0x0B47, 0x0B57] },
    };

    public static readonly Script Telugu = new(
        "telu", "tel2", "telu", 0x0C00, 0x0C7F,
        Consonants: [.. Without(Range(0x0C15, 0x0C39), 0x0C29, 0x0C34), 0x0C58, 0x0C59, 0x0C5A],   // 0C34 ఴ 已廢用：它的 virama 形往左伸進前一個叢集，留著會讓每個叢集交界都多算 9%
        Vowels: [.. Without(Range(0x0C05, 0x0C14), 0x0C0D, 0x0C11), 0x0C60, 0x0C61],
        Signs: [.. Range(0x0C3E, 0x0C44), .. Range(0x0C46, 0x0C48), .. Range(0x0C4A, 0x0C4C), 0x0C55],   // 單獨的 ౖ（ై 的後半）、母音 L 符號 ౢ ౣ：WPF 的 ra 下加形配它們會換字形（偏窄 8.7%），量不到
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
        Signs: [.. Range(0x0CBE, 0x0CC4), .. Range(0x0CC6, 0x0CC8), .. Range(0x0CCA, 0x0CCC), 0x0CD5, 0x0CD6],   // ೢ ೣ（母音 L）不收：WPF 跟它們不連寫，這支引擎會連（ಖ್ತೣ 窄 15%）
        Virama: 0x0CCD, Nukta: -1,   // nukta + virama 字型組不起來（WPF 畫得比我們寬 30%）：用到就量不到
        Modifiers: [.. Range(0x0C81, 0x0C83)],
        Plain: [.. Range(0x0CE6, 0x0CEF)])
    {
        Ra = 0x0CB0,
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
        Ra = 0x0D30,
        PreBase = [0x0D46, 0x0D47, 0x0D48],
        Split = new() { [0x0D4A] = [0x0D46, 0x0D3E], [0x0D4B] = [0x0D47, 0x0D3E], [0x0D4C] = [0x0D46, 0x0D57] },
        PreBaseMayBreakConjunct = true,
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

    /// <summary>高棉文：coeng（U+17D2）當 virama，後面的子音變成下加形；聲調轉換符號（៉ ៊）可以在母音前面。</summary>
    public static readonly Script Khmer = new(
        "khmr", "khmr", "khmr", 0x1780, 0x17FF,
        Consonants: [.. Range(0x1780, 0x17A2)],
        Vowels: [.. Range(0x17A5, 0x17B3, 0x17A8)],   // 17A3、17A4、17A8 已廢用
        Signs: [.. Range(0x17B6, 0x17C5), 0x17C9, 0x17CA],
        Virama: 0x17D2, Nukta: -1,
        Modifiers: [0x17C6, 0x17C7, 0x17C8, .. Range(0x17CB, 0x17D1), 0x17D3, 0x17DD],
        Plain: [.. Range(0x17D4, 0x17DC), .. Range(0x17E0, 0x17E9), .. Range(0x17F0, 0x17F9)])
    {
        PreBase = [0x17C1, 0x17C2, 0x17C3],
        Droppable = [0x17C9, 0x17CA],
        BaseOnlyModifiers = [0x17CC],
        SingleSubjoined = 0x179A,
        BaseIsFirst = true,
        Split = new() { [0x17BE] = [0x17C1, 0x17BE], [0x17BF] = [0x17C1, 0x17BF], [0x17C0] = [0x17C1, 0x17C0], [0x17C4] = [0x17C1, 0x17C4], [0x17C5] = [0x17C1, 0x17C5] },
        SignSequences = [.. Range(0x17B6, 0x17C5).Order().Select(v => new[] { v }), [0x17C9], [0x17CA],
            .. Range(0x17B6, 0x17C5).Order().SelectMany(v => new[] { new[] { 0x17C9, v }, new[] { 0x17CA, v } })],
    };

    /// <summary>
    /// 緬甸文：U+1039 是看不見的疊字符號（當 virama）；ေ 與介音 ြ 畫在子音前面；母音與收尾可以是一串。
    /// **定義了但沒開**（不在 <see cref="All"/>）：Myanmar Text 在下面有長尾巴的子音底下會把符號移到旁邊（WPF 畫的「ဋို့」比這支匯出引擎算的寬 29%），
    /// 「ံ」後面接「့」也會挪位置 —— 要真的緬甸文排版引擎才算得出來。
    /// </summary>
    public static readonly Script Myanmar = new(
        "mymr", "mym2", "mymr", 0x1000, 0x109F,
        Consonants: [.. Range(0x1000, 0x1021)],
        Vowels: [.. Range(0x1023, 0x102A, 0x1028)],
        Signs: [.. Range(0x102B, 0x1032), 0x1037, 0x103A],
        Virama: 0x1039, Nukta: -1,
        Modifiers: [0x1036, 0x1037, 0x1038],
        Plain: [.. Range(0x1040, 0x104F)])
    {
        PreBase = [0x1031, 0x103C],
        Medials = [[0x103B], [0x103C], [0x103D], [0x103E], [0x103B, 0x103D], [0x103B, 0x103E], [0x103C, 0x103D], [0x103C, 0x103E],
            [0x103D, 0x103E], [0x103B, 0x103D, 0x103E], [0x103C, 0x103D, 0x103E]],
        SignSequences = [[0x102B], [0x102C], [0x102D], [0x102E], [0x102F], [0x1030], [0x1031], [0x1032], [0x102D, 0x102F],
            [0x1031, 0x102C], [0x1031, 0x102B], [0x1031, 0x102C, 0x103A], [0x1031, 0x102B, 0x103A], [0x1031, 0x102C, 0x1037, 0x103A],
            [0x1031, 0x102B, 0x1037, 0x103A], [0x103A], [0x1037, 0x103A], [0x102C, 0x103A]],
        ModifierSequences = [[0x1036], [0x1037], [0x1038], [0x1036, 0x1037], [0x1036, 0x1038], [0x1037, 0x1038]],
        InvalidPairs = [(0x103A, 0x1037), (0x103A, 0x1036)],
        Droppable = [0x1036],   // ံ 跟 ့ 一起時這支引擎沒把 ့ 移開（ဆွှူံ့ 窄 0.4 px；WPF 跟 ဆွှူ့ 一樣寬）
        Interacting = true,
        BoundaryTail = 2,
    };

    /// <summary>藏文：子音下面最多疊兩個子音（第二個只有 ya、ra、la、wa），母音符號可以接在 a-chung（ཱ）後面。</summary>
    public static readonly Script Tibetan = new(
        "tibt", "tibt", "tibt", 0x0F00, 0x0FFF,
        Consonants: [.. Range(0x0F40, 0x0F6C, 0x0F48)],
        Vowels: [],
        Signs: [.. Range(0x0F71, 0x0F7D), 0x0F80, 0x0F81],
        Virama: 0x0F84, Nukta: 0x0F39,
        Modifiers: [0x0F7E, 0x0F7F, 0x0F82, 0x0F83],
        Plain: [.. Range(0x0F00, 0x0F17), .. Range(0x0F1A, 0x0F34), 0x0F36, 0x0F38, .. Range(0x0F3A, 0x0F3D), 0x0F85,
            .. Range(0x0FBE, 0x0FC5), .. Range(0x0FC7, 0x0FCC), .. Range(0x0FCE, 0x0FDA)])
    {
        Subjoined = [.. Range(0x0F90, 0x0FBC, 0x0F98)],
        MaxSubjoined = 2,
        LaterSubjoined = [0x0FAD, 0x0FB1, 0x0FB2, 0x0FB3],
        SignSequences = [[0x0F71], [0x0F72], [0x0F74], [0x0F7A], [0x0F7B], [0x0F7C], [0x0F7D], [0x0F80],
            [0x0F71, 0x0F72], [0x0F71, 0x0F74], [0x0F71, 0x0F7A], [0x0F71, 0x0F7C], [0x0F71, 0x0F80]],   // 0F73、0F75、0F81 不建議用（Unicode）
        VisibleVirama = true,
        StackKeyed = true,
    };

    /// <summary>有量的文字系統（WpfOracle --scripts 對照過）。緬甸文定義在上面但不在這裡（見 <see cref="Myanmar"/>）。</summary>
    public static readonly Script[] All = [Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada, Malayalam, Sinhala, Khmer, Myanmar, Tibetan];

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
            for (var k = 0; k < s.MaxSubjoined && j < cps.Count && s.Subjoined.Contains(cps[j])
                            && (k == 0 || s.LaterSubjoined is null || s.LaterSubjoined.Contains(cps[j])); k++)
                j++;
            j += Longest(s.Medials, cps, j);
            if (j < cps.Count && cps[j] == s.Virama) j++;
            else j += Longest(s.SignSeqs, cps, j);
        }
        else return 0;
        // virama 後面再接鼻化符號是不合法的組合：WPF 畫一個虛線圓圈佔位（寬很多），量不到
        var modifier = Longest(s.ModifierSeqs, cps, j);
        if (modifier > 0)
        {
            if (cps[j - 1] == s.Virama || s.InvalidPairs.Contains((cps[j - 1], cps[j]))) return 0;
            if (s.BaseOnlyModifiers.Contains(cps[j]) && (j != i + 1 || i > 0 && cps[i - 1] == s.Virama)) return 0;   // 下標子音（前面是 virama）也不行
            j += modifier;
        }
        if (j < cps.Count && s.InvalidPairs.Contains((cps[j - 1], cps[j]))) return 0;
        for (var k = i + 1; k < j; k++) if (s.InvalidPairs.Contains((cps[k - 1], cps[k]))) return 0;
        return j - i;
    }

    private static int Longest(int[][] sequences, IReadOnlyList<int> cps, int j)
    {
        var best = 0;
        foreach (var seq in sequences)
        {
            if (seq.Length <= best || j + seq.Length > cps.Count) continue;
            var k = 0;
            while (k < seq.Length && cps[j + k] == seq[k]) k++;
            if (k == seq.Length) best = seq.Length;
        }
        return best;
    }

    /// <summary>匯出時列舉的全部叢集（跟 <see cref="Match"/> 認得的一樣）。</summary>
    public static IEnumerable<int[]> Enumerate(Script s, Func<int, bool> drawable, bool withModifiers = true)
    {
        var modifiers = withModifiers ? s.ModifierSeqs.Where(m => m.All(drawable)).ToList() : [];
        var signs = s.SignSeqs.Where(m => m.All(drawable)).ToList();
        var medials = s.Medials.Where(m => m.All(drawable)).ToList();
        var subjoined = s.Subjoined.Order().Where(drawable).ToList();
        bool Valid(int[] c)
        {
            for (var k = 1; k < c.Length; k++) if (s.InvalidPairs.Contains((c[k - 1], c[k]))) return false;
            return true;
        }
        IEnumerable<int[]> WithModifier(int[] core)
        {
            if (!Valid(core)) yield break;
            yield return core;
            foreach (var m in modifiers)
                if (!s.InvalidPairs.Contains((core[^1], m[0])) && !(s.BaseOnlyModifiers.Contains(m[0]) && core.Length != 1)) yield return [.. core, .. m];
        }

        foreach (var v in s.Vowels.Order())
            if (drawable(v)) foreach (var c in WithModifier([v])) yield return c;

        foreach (var c in s.Consonants.Order())
        {
            if (!drawable(c)) continue;
            foreach (var head in s.Nukta >= 0 && drawable(s.Nukta) ? new[] { new[] { c }, new[] { c, s.Nukta } } : [[c]])
            {
                // 疊字（藏文）：0、1、2 個
                var stacks = new List<int[]> { head };
                if (s.MaxSubjoined >= 1)
                    foreach (var a in subjoined)
                    {
                        stacks.Add([.. head, a]);
                        if (s.MaxSubjoined >= 2)
                            foreach (var b in subjoined.Where(x => s.LaterSubjoined is null || s.LaterSubjoined.Contains(x)))
                                stacks.Add([.. head, a, b]);
                    }
                foreach (var stack in stacks)
                    foreach (var medial in medials.Prepend([]))
                    {
                        int[] core = [.. stack, .. medial];
                        foreach (var x in WithModifier(core)) yield return x;
                        foreach (var sign in signs)
                            foreach (var x in WithModifier([.. core, .. sign])) yield return x;
                    }
                if (drawable(s.Virama) && Valid(head)) yield return [.. head, s.Virama];
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
        // 畫在前面的依序移到最前面：後出現的在更左邊（緬甸文：子音 ြ ေ → ေ ြ 子音，ေ 在最左）
        for (var k = 1; k < list.Count; k++)
            if (s.PreBase.Contains(list[k]) && !(k > 0 && list.Take(k).All(s.PreBase.Contains)))
            {
                var m = list[k];
                list.RemoveAt(k);
                list.Insert(0, m);
            }
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
            "ml" => "MAL", "si" => "SNH", "km" => "KHM", "my" => "BRM", "bo" => "TIB",
            _ => null,
        };

    /// <summary>度量檔裡的鍵：文字系統，或「文字系統/語言系統」。</summary>
    public static string DataKey(Script s, string? languageSystem) => languageSystem is null ? s.Tag : $"{s.Tag}/{languageSystem}";
}
