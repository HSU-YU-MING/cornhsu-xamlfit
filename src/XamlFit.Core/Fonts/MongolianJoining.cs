namespace XamlFit.Core.Fonts;

/// <summary>
/// 蒙古文（傳統蒙古文、托忒文、錫伯文、滿文，U+1800–18AF）的連寫：字母都是雙向連寫，形狀（獨立／字首／字中／字尾）看前後是不是字母。
/// FVS（U+180B–180D、180F）與附加符號是透明的；MVS（U+180E）與窄不換行空白（U+202F）不連寫 —— WPF 畫出來它們有寬度（Mongolian Baiti 205 單位），
/// 不是看不見的控制字元。
/// 寬度用匯出時算好的「每個字在每種形狀下最寬會多寬」相加（見 OpenType.MongolianExport），度量檔裡沒有字型的表。
/// </summary>
public static class MongolianJoining
{
    /// <summary>度量檔裡蒙古文資料放在 <see cref="FontMetrics.Indic"/> 的這個鍵底下（鍵：「碼位,形狀」）。</summary>
    public const string DataKey = "mong";

    public static bool IsMongolian(int cp) => cp is >= 0x1800 and <= 0x18AF;

    /// <summary>量的時候走蒙古文表的字：蒙古文區塊，加上蒙古文裡常用的窄不換行空白與 ZWJ。</summary>
    public static bool Handles(int cp) => IsMongolian(cp) || cp is 0x202F or 0x200D;

    /// <summary>雙向連寫的字母（ArabicShaping.txt 的 D）。U+1807 錫伯文音節分隔號也是 D。</summary>
    public static bool IsLetter(int cp) => cp is (>= 0x1820 and <= 0x1878) or (>= 0x1887 and <= 0x18A8) or 0x18AA or 0x1807;

    private static bool Transparent(int cp) => IsVariationSelector(cp) || cp is 0x1885 or 0x1886 or 0x18A9;

    /// <summary>FVS1–4。</summary>
    public static bool IsVariationSelector(int cp) => cp is (>= 0x180B and <= 0x180D) or 0x180F;

    private static bool JoinCausing(int cp) => cp is 0x180A or 0x200D;

    /// <summary>基本蒙古字母（U+1820–1842）以外的字母：托忒文、錫伯文、滿文、阿里嘎禮字母。</summary>
    private static bool Extended(int cp) => IsLetter(cp) && cp is not (>= 0x1820 and <= 0x1842);

    private static bool Masculine(int cp) => cp is 0x1820 or 0x1823 or 0x1824;
    private static bool Feminine(int cp) => cp is 0x1821 or 0x1825 or 0x1826 or 0x1827;

    private static bool Vowel(int cp) => cp is >= 0x1820 and <= 0x1827;

    /// <summary>
    /// 每個字查表用的「形狀」，幾件事編在一起（量的時候跟匯出時用同一個函式算，分得越細越不會被少見的字形撐大；
    /// 全部混在一起記時一般的蒙古文平均偏寬 60%）：
    /// <list type="bullet">
    /// <item>第 0–1 位：字母是 <see cref="ArabicJoining.Isolated"/>…<see cref="ArabicJoining.Final"/>；不是字母的字（FVS、MVS、標點…）
    /// 是「前面接字母 + 後面接字母 ×2」，其他位元都不用（MVS 在字尾畫獨立的 1144 單位寬，在字中間只有 205）。</item>
    /// <item>第 2 位：這個字（連續的蒙古文、窄不換行空白、ZWJ）裡有擴充字母（托忒文、錫伯文、滿文…）：
    /// Mongolian Baiti 的擴充字母接在一般字母旁邊時一般字母換成寬很多的字形（ᠠ 字首 866 → 1368）。</item>
    /// <item>第 3–4 位：字的陰陽性（有陽性母音 a o u = 1，否則有陰性母音 e ö ü ē = 2，都沒有 = 0）：ᠭ ᠬ 接陰性母音時是寬很多的字形。</item>
    /// <item>第 5–7 位：字母後面緊接的 FVS（1–4）：FVS 選的變體可能比預設的寬。</item>
    /// <item>第 8 位：前面緊接 MVS 或窄不換行空白（詞尾的形狀：MVS 後面的 ᠠ 是 454，獨立的是 1279）。</item>
    /// <item>第 9 位：後面緊接 MVS 或窄不換行空白。</item>
    /// <item>第 10 位：同一個字裡前面已經有母音（不在第一個音節：ö ü 在第一個音節以外寫短的，923 → 514）。</item>
    /// <item>第 11 位：前一個字母是母音（ᠢ 接在母音後面是雙齒的，852；接在子音後面 426）。</item>
    /// <item>第 16–21 位：鄰字（基本字母 ᠠ…ᠸ 的序號 + 1）。母音記前一個字母、子音記後一個字母：第二音節以後的 ö ü 寬度看前面的子音，
    /// ᠬ ᠭ 的字形看後面的母音。表裡沒有這個組合時退回粗的鍵（第 14 位，只看前 8 位元）。</item>
    /// </list>
    /// </summary>
    public static int[] FormsOf(IReadOnlyList<int> cps)
    {
        var forms = new int[cps.Count];
        for (var i = 0; i < cps.Count; i++)
        {
            var cp = cps[i];
            if (!IsLetter(cp))
            {
                forms[i] = (i > 0 && IsLetter(cps[i - 1]) ? 1 : 0) | (i + 1 < cps.Count && IsLetter(cps[i + 1]) ? 2 : 0);
                continue;
            }
            bool prev = Joins(cps, i, -1), next = Joins(cps, i, 1);
            var form = prev && next ? ArabicJoining.Medial : prev ? ArabicJoining.Final : next ? ArabicJoining.Initial : ArabicJoining.Isolated;
            if (i + 1 < cps.Count && IsVariationSelector(cps[i + 1])) form |= (cps[i + 1] == 0x180F ? 4 : cps[i + 1] - 0x180A) << 5;
            if (i > 0 && cps[i - 1] is 0x180E or 0x202F) form |= 1 << 8;
            if (i + 1 < cps.Count && cps[i + 1] is 0x180E or 0x202F) form |= 1 << 9;
            for (var k = i - 1; k >= 0 && Handles(cps[k]); k--)
                if (Vowel(cps[k])) { form |= 1 << 10; break; }
            var before = i - 1;
            while (before >= 0 && Transparent(cps[before])) before--;
            if (before >= 0 && Vowel(cps[before])) form |= 1 << 11;
            // 第 16–21 位：鄰字（基本字母）。母音記前一個字母、子音記後一個字母 —— 第二音節以後的 ö ü 寬度看前面的子音（514～923），
            // ᠬ ᠭ 的字形看後面的母音（616～818）。表裡沒有這種組合就用粗的鍵（不看這些位元）
            var neighbor = Vowel(cp) ? before : Next(cps, i);
            if (neighbor >= 0 && neighbor < cps.Count && cps[neighbor] is >= 0x1820 and <= 0x1842) form |= (cps[neighbor] - 0x1820 + 1) << 16;
            forms[i] = form;
        }
        for (var start = 0; start < cps.Count;)
        {
            if (!Handles(cps[start])) { start++; continue; }
            var end = start;
            while (end < cps.Count && Handles(cps[end])) end++;
            bool extended = false, masculine = false, feminine = false;
            for (var k = start; k < end; k++)
            {
                extended |= Extended(cps[k]);
                masculine |= Masculine(cps[k]);
                feminine |= Feminine(cps[k]);
            }
            var flags = (extended ? 4 : 0) | (masculine ? 1 << 3 : feminine ? 2 << 3 : 0);
            for (var k = start; k < end; k++)
                if (IsLetter(cps[k])) forms[k] |= flags;
            start = end;
        }
        return forms;
    }

    /// <summary>字母在 <see cref="FormsOf"/> 裡的連寫形狀（0–3）。</summary>
    public static int Position(int form) => form & 3;

    private static int Next(IReadOnlyList<int> cps, int i)
    {
        var k = i + 1;
        while (k < cps.Count && Transparent(cps[k])) k++;
        return k < cps.Count ? k : -1;
    }

    private static bool Joins(IReadOnlyList<int> cps, int i, int step)
    {
        var k = i + step;
        while (k >= 0 && k < cps.Count && Transparent(cps[k])) k += step;
        return k >= 0 && k < cps.Count && (IsLetter(cps[k]) || JoinCausing(cps[k]));
    }

    /// <summary>表裡一個字的鍵。</summary>
    public static string Key(int cp, int form) => $"{cp},{form}";

    /// <summary>
    /// 夾在蒙古文裡的標點、符號在表裡的鍵。WPF 把它們跟旁邊的蒙古文排成同一段、套蒙古文的排版規則：
    /// Mongolian Baiti 的 <c>rclt</c> 無條件把 60 個標點換成另一個字形（《 1026 → 1116、「 749 → 949 單位）；
    /// 單獨的標點不套。表裡只記比原本寬的。鍵是「1,碼位」（度量檔的表只存數字；其他鍵的第一個數是 2 或蒙古文的碼位）。
    /// </summary>
    public static string CommonKey(int cp) => $"1,{cp}";

    /// <summary>不屬於哪種文字、會併進旁邊蒙古文那一段的字：標點、符號、空白、數字。</summary>
    public static bool IsCommon(int cp)
    {
        if (IsMongolian(cp) || cp > 0xFFFF && !System.Text.Rune.IsValid(cp)) return false;
        var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp);
        return category is System.Globalization.UnicodeCategory.DecimalDigitNumber && cp is (>= '0' and <= '9') or (>= 0xFF10 and <= 0xFF19)
            || category is System.Globalization.UnicodeCategory.ConnectorPunctuation or System.Globalization.UnicodeCategory.DashPunctuation
                or System.Globalization.UnicodeCategory.OpenPunctuation or System.Globalization.UnicodeCategory.ClosePunctuation
                or System.Globalization.UnicodeCategory.InitialQuotePunctuation or System.Globalization.UnicodeCategory.FinalQuotePunctuation
                or System.Globalization.UnicodeCategory.OtherPunctuation or System.Globalization.UnicodeCategory.MathSymbol
                or System.Globalization.UnicodeCategory.CurrencySymbol or System.Globalization.UnicodeCategory.ModifierSymbol
                or System.Globalization.UnicodeCategory.OtherSymbol or System.Globalization.UnicodeCategory.SpaceSeparator;
    }

    /// <summary>
    /// 字型會連成一個字形的字母對（子音 + 母音的合字）在表裡的記號鍵。量的時候由左往右，遇到這種對就整對一起查表（<see cref="SegmentKey"/>）：
    /// WPF 把合字整個算給第一個字、母音分到 0，一個字一個字各取最大會把合字算給子音、母音又算一次自己的寬度（平均偏寬 40%）。
    /// </summary>
    public static string PairKey(int a, int b) => $"2,{a},{b}";

    /// <summary>由左往右切：<paramref name="pair"/> 說是合字對的兩個相鄰字母一段，其他每個字一段。</summary>
    public static List<(int Start, int Length)> Segments(IReadOnlyList<int> cps, Func<int, int, bool> pair)
    {
        var segments = new List<(int, int)>();
        for (var i = 0; i < cps.Count;)
        {
            var length = i + 1 < cps.Count && IsLetter(cps[i]) && IsLetter(cps[i + 1]) && pair(cps[i], cps[i + 1]) ? 2 : 1;
            segments.Add((i, length));
            i += length;
        }
        return segments;
    }

    /// <summary>一段的鍵：一個字是 <see cref="Key(int, int)"/>，合字對是「a,b,形狀a,形狀b」。<paramref name="coarse"/>：只看前 8 位元的形狀（見 <see cref="Coarse"/>）。</summary>
    public static string SegmentKey(IReadOnlyList<int> cps, int start, int length, int[] forms, bool coarse = false)
    {
        int F(int form) => coarse ? Coarse(form) : form;
        return length == 1 ? Key(cps[start], F(forms[start])) : $"{cps[start]},{cps[start + 1]},{F(forms[start])},{F(forms[start + 1])}";
    }

    /// <summary>
    /// 粗的形狀：拿掉第 8 位以上（前後的 MVS、在不在第一個音節、前一個是不是母音），再標上第 14 位跟細的分開。
    /// 匯出時兩種都記（粗的是更多上下文裡的最大值）；量的時候細的鍵沒遇過才用粗的 —— 一樣不會窄，只是寬一點。
    /// </summary>
    public static int Coarse(int form) => (form & 0xFF) | 1 << 14;

}
