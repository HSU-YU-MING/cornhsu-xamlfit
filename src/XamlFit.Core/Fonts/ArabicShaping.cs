using System.Globalization;

namespace XamlFit.Core.Fonts;

/// <summary>
/// 一支字型（一個語言系統）的阿拉伯字母寬度：每個字母在獨立／字首／字中／字尾四種形狀的 advance，
/// 加上相鄰兩個字母因為連字、上下文替換而改變的寬度（依兩者的形狀）。
/// 匯出時用字型的 GSUB 跑出來的數字（見 OpenType.ArabicExport），沒有字形、沒有字型的表。
/// </summary>
public sealed class ArabicShapes
{
    /// <summary>碼位 → [獨立, 字首, 字中, 字尾] 的 advance（字型單位）。</summary>
    public required IReadOnlyDictionary<int, int[]> Forms { get; init; }

    /// <summary>(前一個字母, 形狀, 這個字母, 形狀) → 寬度改變量。見 <see cref="Key"/>。只列非零的。</summary>
    public required IReadOnlyDictionary<long, int> Pairs { get; init; }

    /// <summary>匯出時算過成對改變量的字母。兩個相鄰字母有一個不在裡面，那一對的寬度沒驗證過 —— 量不到。</summary>
    public IReadOnlySet<int> PairLetters { get; init; } = new HashSet<int>();

    /// <summary>
    /// 三個相鄰字母的額外改變量（扣掉兩組成對改變量之後還剩的），只算 <see cref="TripleLetters"/> 結尾的。
    /// 長尾巴的 ے／ۓ（yeh barree）會依前面**兩個**字換成不同長度的字形（Segoe UI 的 calt），成對的量不出來。見 <see cref="Key3"/>。
    /// </summary>
    public IReadOnlyDictionary<long, int> Triples { get; init; } = new Dictionary<long, int>();

    /// <summary>算過三字改變量的結尾字母。它前面兩個字母之間夾了附加符號時量不到（字型的規則會跳過一部分符號，沒模擬）。</summary>
    public IReadOnlySet<int> TripleLetters { get; init; } = new HashSet<int>();

    /// <summary>碼位都在 BMP（成對、三字的字母都在 U+0620–06FF）。</summary>
    public static long Key3(int a, int formA, int b, int formB, int c, int formC) =>
        ((long)(a & 0xFFFF) << 38) | ((long)formA << 36) | ((long)(b & 0xFFFF) << 20) | ((long)formB << 18) | ((long)(c & 0xFFFF) << 2) | (uint)formC;

    public static long Key(int a, int formA, int b, int formB) =>
        ((long)a << 32) | ((long)formA << 30) | ((long)b << 2) | (uint)formB;

    /// <summary>WPF 依元素的語言挑 OpenType 的語言系統：阿拉伯文 ARA、波斯文 FAR、烏爾都文 URD，其他用預設。</summary>
    public static string LanguageSystem(string? language)
    {
        var tag = (language ?? "").Split('-', '_')[0].ToLowerInvariant();
        return tag switch { "ar" => "ARA ", "fa" => "FAR ", "ur" => "URD ", _ => "dflt" };
    }
}

/// <summary>阿拉伯字母的連寫規則（Unicode ArabicShaping.txt）與形狀選擇。</summary>
public static class ArabicJoining
{
    public const int Isolated = 0, Initial = 1, Medial = 2, Final = 3;

    public enum Type { NonJoining, Transparent, Right, Dual, JoinCausing }

    /// <summary>要照連寫規則選字形的字（阿拉伯文、補充、擴充 A）。表現形式區（FB50–FEFF）是已經選好的字形，不在這裡。</summary>
    public static bool IsArabic(int cp) => cp is (>= 0x0600 and <= 0x06FF) or (>= 0x0750 and <= 0x077F) or (>= 0x08A0 and <= 0x08FF);

    // 只能跟右邊（前一個字）連的字母（ArabicShaping.txt 的 R）
    private static readonly HashSet<int> RightJoining =
    [
        0x0622, 0x0623, 0x0624, 0x0625, 0x0627, 0x0629, 0x062F, 0x0630, 0x0631, 0x0632, 0x0648,
        0x0671, 0x0672, 0x0673, 0x0675, 0x0676, 0x0677,
        .. Enumerable.Range(0x0688, 0x0699 - 0x0688 + 1),
        0x06C0, .. Enumerable.Range(0x06C3, 0x06CB - 0x06C3 + 1), 0x06CD, 0x06CF, 0x06D2, 0x06D3, 0x06D5, 0x06EE, 0x06EF,
        0x0759, 0x075A, 0x075B, 0x076B, 0x076C, 0x0771, 0x0773, 0x0774, 0x0778, 0x0779,
        0x08AA, 0x08AB, 0x08AC, 0x08AE, 0x08B1, 0x08B2, 0x08B9,
    ];

    // 字母但不連寫（U）
    private static readonly HashSet<int> NonJoiningLetters = [0x0621, 0x0674, 0x06FD, 0x06FE];

    public static Type TypeOf(int cp)
    {
        if (cp == 0x200D || cp == 0x0640) return Type.JoinCausing;   // ZWJ、延長線
        if (cp == 0x200C) return Type.NonJoining;                    // ZWNJ
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) return Type.Transparent;
        if (cat == UnicodeCategory.Format) return Type.Transparent;
        if (!IsArabic(cp) || cat != UnicodeCategory.OtherLetter) return Type.NonJoining;
        if (RightJoining.Contains(cp)) return Type.Right;
        if (NonJoiningLetters.Contains(cp)) return Type.NonJoining;
        return Type.Dual;
    }

    /// <summary>每個字該用哪種形狀（非阿拉伯字母回 -1）。</summary>
    public static int[] FormsOf(IReadOnlyList<int> cps)
    {
        var types = cps.Select(TypeOf).ToArray();
        var forms = new int[cps.Count];
        for (var i = 0; i < cps.Count; i++)
        {
            forms[i] = -1;
            if (!IsArabic(cps[i]) || types[i] is not (Type.Dual or Type.Right or Type.JoinCausing)) continue;

            var prev = Neighbour(types, i, -1);
            var next = Neighbour(types, i, +1);
            var joinsPrev = prev >= 0 && types[prev] is Type.Dual or Type.JoinCausing;
            var joinsNext = types[i] is Type.Dual or Type.JoinCausing && next >= 0 && types[next] is Type.Dual or Type.Right or Type.JoinCausing;
            forms[i] = (joinsPrev, joinsNext) switch
            {
                (true, true) => Medial,
                (true, false) => Final,
                (false, true) => Initial,
                _ => Isolated,
            };
        }
        return forms;
    }

    private static int Neighbour(Type[] types, int i, int step)
    {
        for (var j = i + step; j >= 0 && j < types.Length; j += step)
            if (types[j] != Type.Transparent) return j;
        return -1;
    }
}
