using System.Globalization;
using System.Text;

namespace XamlFit.Core.Fonts;

public enum FontStyleKind { Normal, Italic, Oblique }

/// <summary>一段連續碼位共用同一個前進寬度。CJK 字型絕大多數字符是全形同寬，
/// 用區間存可以把二萬多筆壓成幾十筆 —— 度量檔要進 repo，大小是設計條件。</summary>
public readonly record struct AdvanceRange(int Start, int End, int Advance);

/// <summary>
/// 一支字型（family + weight + style + stretch）的度量。
///
/// **只有數字，沒有字形。** 這是刻意的：使用者從自己已授權的字型匯出這份數字放進
/// 自己的 repo，CI 不需要安裝任何字型，而我們從未散布任何字型檔。
/// </summary>
public sealed class FontMetrics
{
    public required string Family { get; init; }
    public int Weight { get; init; } = 400;
    public FontStyleKind Style { get; init; } = FontStyleKind.Normal;
    public string Stretch { get; init; } = "Normal";

    /// <summary>舊式家族名（name ID 1），跟 <see cref="Family"/> 不同時才有值。
    /// WPF 兩種寫法都認：<c>FontFamily="Segoe UI" FontWeight="SemiBold"</c> 與 <c>FontFamily="Segoe UI Semibold"</c>。</summary>
    public string? LegacyFamily { get; init; }

    /// <summary>家族名的其他語言版本（「微軟正黑體」）。WPF 用哪個名字都找得到這支字型。</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>可變字型（有 fvar 表）。檔案裡只有預設軸位置的前進寬度 ——
    /// 其他字重的寬度在 HVAR／gvar 裡，沒讀，所以只能量這一個字重。</summary>
    public bool Variable { get; init; }

    /// <summary>字型設計單位。一般是 1000（CFF）或 2048（TrueType）。</summary>
    public required int UnitsPerEm { get; init; }

    public int Ascender { get; init; }
    public int Descender { get; init; }
    public int LineGap { get; init; }

    /// <summary>cmap 查不到的碼位用的寬度（.notdef 的寬度）。
    /// ⚠ 查不到代表這支字型**畫不出這個字**，實際會走字型後備 ——
    /// 所以 TryAdvance 會回 false，由呼叫端決定後備，不要默默用這個值。</summary>
    public int DefaultAdvance { get; init; }

    /// <summary>依 Start 排序，不重疊。</summary>
    public required IReadOnlyList<AdvanceRange> Ranges { get; init; }

    /// <summary>GPOS 'kern' 的成對調整；字型沒有、或度量檔是舊版匯出的時候是 null（量出來偏寬，保守）。</summary>
    public KerningTable? Kerning { get; init; }

    /// <summary>阿拉伯字母的寬度，依 OpenType 語言系統（"dflt"、"ARA "、"FAR "、"URD "）；沒有的話用這支字型畫的阿拉伯文量不到。</summary>
    public IReadOnlyDictionary<string, ArabicShapes>? Arabic { get; init; }

    /// <summary>
    /// 字型 hdmx 表裡的 hinting 後寬度（TextFormattingMode="Display" 用的整數像素寬），依像素大小（ppem）。
    /// 存的是跟「線性寬度四捨五入」的差，只列非零。沒有 hdmx、或 ppem 不在表裡 → Display 模式量不到。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>>? Hinted { get; init; }

    /// <summary>
    /// 印度系文字（"deva"、"taml"）每個字素叢集比「各字 advance 相加」寬或窄多少（字型單位，只列非零；鍵是 <see cref="IndicClusters.Key"/>）。
    /// 有這個文字系統的鍵才量得到。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? Indic { get; init; }

    /// <summary>Display 模式下這個字在 ppem 像素大小時的寬度（整數像素）。這個 ppem 沒有 hinting 資料回 false。</summary>
    public bool TryHintedAdvance(int codepoint, int ppem, out int px)
    {
        px = 0;
        if (Hinted is null || !Hinted.TryGetValue(ppem, out var deltas) || !TryAdvance(codepoint, out var advance)) return false;
        px = TextMeasure.RoundHalfUp((double)advance * ppem / UnitsPerEm) + (deltas.TryGetValue(codepoint, out var d) ? d : 0);
        return true;
    }

    public ArabicShapes? ArabicFor(string? language) =>
        Arabic is null ? null
        : Arabic.TryGetValue(ArabicShapes.LanguageSystem(language), out var s) ? s
        : Arabic.TryGetValue("dflt", out var d) ? d : null;

    /// <summary>這支字型畫不畫得出這個碼位；畫得出就給前進寬度（字型單位）。</summary>
    public bool TryAdvance(int codepoint, out int advance)
    {
        var lo = 0;
        var hi = Ranges.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var r = Ranges[mid];
            if (codepoint < r.Start) hi = mid - 1;
            else if (codepoint > r.End) lo = mid + 1;
            else { advance = r.Advance; return true; }
        }
        advance = DefaultAdvance;
        return false;
    }

    /// <summary>建議行高（字型單位）。垂直裁切判定要用。</summary>
    public int LineHeight => Ascender - Descender + LineGap;

    public string Id => $"{Family}|{Weight}|{Style}|{Stretch}";

    public override string ToString() => $"{Family} {Weight} {Style} ({Ranges.Count} ranges)";
}

/// <summary>量測結果。⚠ 一定要帶「有沒有量不到的字」出去 ——
/// 把量不到的字當成寬度 0，就會報出「裝得下」這種謊報健康的結論。</summary>
public readonly record struct Measurement(
    double WidthPx, int MissingGlyphs, bool ComplexScript, bool UsedAutoFallback = false)
{
    public bool IsReliable => MissingGlyphs == 0 && !ComplexScript && Unsupported is null;

    /// <summary>量不到的其他原因（Display 模式沒有那個像素大小的 hinting 寬度…）。</summary>
    public string? Unsupported { get; init; }

    /// <summary>報告用：量不到的原因。</summary>
    public string UnreliableReason => Unsupported
        ?? (ComplexScript ? "complex script (advance widths do not sum correctly)" : "glyphs not covered by the font metrics");

    /// <summary>第一個量不到的碼位（沒有的話是 -1）。報告要能說出「缺的是哪個字」，
    /// 使用者才知道該去匯出哪支字型。</summary>
    public int FirstMissing { get; init; } = -1;
}

public static class TextMeasure
{
    /// <summary>GDI 的四捨五入（0.5 進位）。</summary>
    public static int RoundHalfUp(double v) => (int)Math.Floor(v + 0.5);

    /// <summary>
    /// <paramref name="displayScales"/> 不是 null 時是 TextFormattingMode="Display"：照 <see cref="MeasureDisplay"/>；否則照一般的量法。
    /// </summary>
    public static Measurement Measure(string text, FontLookup lookup, double fontSizePx, IReadOnlyList<double>? displayScales) =>
        displayScales is null ? Measure(text, lookup, fontSizePx) : MeasureDisplay(text, lookup, fontSizePx, displayScales);

    /// <summary>
    /// TextFormattingMode="Display"：每個字對齊整數像素，寬度用字型 hdmx 表裡那個像素大小的 hinting 寬度，
    /// 字距也四捨五入到整數像素（WpfOracle --display 實測：Segoe UI、Arial、Trebuchet MS 每一組都一樣）。
    /// 像素大小 = 字級 × 顯示縮放（0.5 進位），所以每個縮放比例各量一次、取最寬的；
    /// 任何一個縮放比例、任何一個字沒有 hinting 寬度就是量不到 —— hinting 可以讓字寬多出好幾個像素，沒有上限可以估。
    /// </summary>
    public static Measurement MeasureDisplay(string text, FontLookup lookup, double fontSizePx, IReadOnlyList<double> scales)
    {
        if (string.IsNullOrEmpty(text) || lookup.Chain.Count == 0) return new Measurement(0, 0, false);
        double widest = 0;
        var missing = 0;
        var usedFallback = false;
        var firstMissing = -1;

        foreach (var scale in scales)
        {
            var ppem = RoundHalfUp(fontSizePx * scale);
            var total = 0;
            FontMetrics? prevFace = null;
            var prevCp = -1;
            missing = 0;

            foreach (var rune in text.EnumerateRunes())
            {
                var cp = rune.Value;
                if (IsDefaultIgnorable(cp)) { prevFace = null; continue; }
                if (IsComplexScript(cp) || ArabicJoining.IsArabic(cp))
                    return new Measurement(0, 0, true) { Unsupported = "TextFormattingMode=\"Display\" with a script that needs shaping" };

                FontMetrics? face = null;
                foreach (var f in lookup.Chain)
                    if (f.TryAdvance(cp, out _)) { face = f; break; }
                if (face is null && lookup.Fallback?.Invoke(cp) is { } fb && fb.Face.TryAdvance(cp, out _))
                {
                    if (Math.Abs(fb.Scale - 1) > 1e-9)
                        return new Measurement(0, 0, false) { Unsupported = "TextFormattingMode=\"Display\" with a scaled fallback font" };
                    face = fb.Face;
                    usedFallback = true;
                }
                if (face is null)
                {
                    missing++;
                    if (firstMissing < 0) firstMissing = cp;
                    prevFace = null;
                    continue;
                }

                if (!face.TryHintedAdvance(cp, ppem, out var px))
                    return new Measurement(0, 0, false)
                    {
                        Unsupported = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"TextFormattingMode=\"Display\": {face.Family} has no hinted widths for {ppem} px ({fontSizePx:0.##} px at {scale * 100:0}% display scaling)"),
                    };
                total += px;
                if (ReferenceEquals(face, prevFace) && face.Kerning is { } kern)
                    total += (int)Math.Round((double)kern.Adjust(prevCp, cp) * ppem / face.UnitsPerEm, MidpointRounding.ToEven);
                prevFace = face;
                prevCp = cp;
            }
            widest = Math.Max(widest, total / scale);
        }
        return new Measurement(widest, missing, false, usedFallback) { FirstMissing = firstMissing };
    }
    /// <summary>
    /// 量一段文字在指定字級下的寬度（DIP）。
    ///
    /// 公式：Σ advance(碼位) × 字級 ÷ unitsPerEm
    ///
    /// ⚠ 這條線性公式對應 WPF 的 <c>TextFormattingMode="Ideal"</c>（WPF 的預設）。
    ///   <c>Display</c> 模式會把字符對齊像素格，寬度略有不同。
    /// 字距（GPOS 'kern'）與阿拉伯文的形狀、必用連字有算（見 Core）；其他連字沒算。
    /// </summary>
    /// <param name="declaredCount">鏈的前幾支是 XAML 明寫的；其餘是自動後備。
    /// 用到自動後備的量測會標記出來 —— Windows 的字型連結表可能選到別支字型。</param>
    /// <summary>用一次字型解析的結果去量：明寫的字型鏈畫不出來的字，照 WPF 的規則補。</summary>
    public static Measurement Measure(string text, FontLookup lookup, double fontSizePx)
    {
        if (string.IsNullOrEmpty(text) || lookup.Chain.Count == 0) return new Measurement(0, 0, false);
        return WithDigits(text, t => Core(t, lookup.Chain, lookup.Fallback, fontSizePx, int.MaxValue, lookup.Language), lookup.Language);
    }

    public static Measurement Measure(
        string text, IReadOnlyList<FontMetrics> fallbackChain, double fontSizePx, int declaredCount = int.MaxValue,
        string? language = null)
    {
        if (string.IsNullOrEmpty(text) || fallbackChain.Count == 0)
            return new Measurement(0, 0, false);
        return WithDigits(text, t => Core(t, fallbackChain, null, fontSizePx, declaredCount, language), language);
    }

    /// <summary>
    /// 阿拉伯文裡的 0–9：WPF 會換成阿拉伯—印度數字（WpfOracle --scripts 實測：元素語言是 ar 時連純數字的字串也換；
    /// 前面最近的字母是阿拉伯文的才換，「8x8」只換第一個）。兩種數字寬度不同 —— Segoe UI 的比較窄、Tahoma 的比較寬。
    /// 不去猜哪幾個會換：全換、不換各量一次，取寬的。
    /// </summary>
    private static Measurement WithDigits(string text, Func<string, Measurement> measure, string? language)
    {
        var plain = measure(text);
        if (!text.Any(c => c is >= '0' and <= '9')) return plain;
        if (ArabicShapes.LanguageSystem(language) == "dflt" && !text.EnumerateRunes().Any(r => ArabicJoining.IsArabic(r.Value))) return plain;
        var zero = ArabicShapes.LanguageSystem(language) is "FAR " or "URD " ? 0x06F0 : 0x0660;
        var swapped = string.Concat(text.Select(c => c is >= '0' and <= '9' ? char.ConvertFromUtf32(zero + c - '0') : c.ToString()));
        var other = measure(swapped);
        if (!other.IsReliable) return plain with { ComplexScript = true };
        return other.WidthPx > plain.WidthPx ? other : plain;
    }

    /// <summary>
    /// 逐字加 advance。另外三件事：
    /// - **字距**：同一支字型畫的相鄰兩個字，照 GPOS 'kern' 調（WPF 預設開著）。
    /// - **阿拉伯文**：照 Unicode 的連寫規則選獨立／字首／字中／字尾形狀，用那個形狀的寬度，再加相鄰兩個字母的改變量
    ///   （連字、上下文替換；WpfOracle --scripts 實測）。字型沒有阿拉伯字形資料時，那段量不到。
    /// - 其他需要重排字形的文字系統（印度系…）：標成量不到。
    /// </summary>
    private static Measurement Core(
        string text, IReadOnlyList<FontMetrics> chain, Func<int, (FontMetrics Face, double Scale)?>? fallback,
        double fontSizePx, int declaredCount, string? language)
    {
        var cps = new List<int>(text.Length);
        foreach (var rune in text.AsSpan().EnumerateRunes()) cps.Add(rune.Value);
        var forms = cps.Exists(ArabicJoining.IsArabic) ? ArabicJoining.FormsOf(cps) : null;
        // 有右到左文字時，空白、標點這類中性字可能跟旁邊的字分在不同方向的段落裡，WPF 不跨段套字距
        // （Arial 的「L 空白」-76：阿拉伯文句子裡的「URL 」量出來沒套）。這種位置只套會變寬的字距。
        var bidi = cps.Exists(IsRightToLeft);

        double totalUnits = 0;
        var missing = 0;
        var complex = false;
        var usedFallback = false;
        var firstMissing = -1;
        var primary = chain[0];

        FontMetrics? prevFace = null;
        var prevCp = -1;
        double prevScale = 1;
        ArabicShapes? prevShapes = null;   // 前一個阿拉伯字母（中間只隔附加符號）
        var prevArabic = -1;
        var prevForm = -1;
        var afterConjunctHead = false;     // 前一個印度系叢集是「子音 + virama」
        var prev2Arabic = -1;              // 再前一個字母（同一串裡），三字改變量用
        var prev2Form = -1;
        var markBeforePrev = false;        // prev2 與前一個字母之間夾過附加符號
        var markSincePrev = false;         // 前一個字母之後夾過附加符號

        for (var i = 0; i < cps.Count; i++)
        {
            var cp = cps[i];
            if (IndicClusters.Of(cp) is null) afterConjunctHead = false;
            if (IsDefaultIgnorable(cp)) { prevFace = null; prevShapes = null; continue; }   // 先判斷：U+FEFF 落在阿拉伯表現形式的範圍裡
            if (prevShapes is not null && ArabicJoining.TypeOf(cp) != ArabicJoining.Type.Transparent && !(forms is not null && forms[i] >= 0))
                prevShapes = null;

            // 這個字由哪支字型畫
            FontMetrics? face = null;
            double scale = 1;
            var advance = 0;
            for (var k = 0; k < chain.Count; k++)
            {
                if (!chain[k].TryAdvance(cp, out advance)) continue;
                face = chain[k];
                scale = (double)primary.UnitsPerEm / face.UnitsPerEm;
                if (k >= declaredCount) usedFallback = true;
                break;
            }
            if (face is null && fallback?.Invoke(cp) is { } fb && fb.Face.TryAdvance(cp, out advance))
            {
                face = fb.Face;
                scale = fb.Scale * (double)primary.UnitsPerEm / fb.Face.UnitsPerEm;
                usedFallback = true;
            }
            if (face is null)
            {
                if (IsComplexScript(cp) && !IsArabicPlain(cp)) complex = true;   // 有字型也要組字，先講這個
                missing++;
                if (firstMissing < 0) firstMissing = cp;
                prevFace = null;
                continue;
            }

            // 阿拉伯文的附加符號（母音記號、hamza…）：WPF 給它寬度 0，不管字型的 hmtx 寫多少
            // （Tahoma 的 U+0654 在 hmtx 有寬度，WPF 量出來是 0；WpfOracle --scripts 實測）
            if (ArabicJoining.IsArabic(cp) && CharUnicodeInfo.GetUnicodeCategory(cp) == UnicodeCategory.NonSpacingMark)
            {
                prevFace = null;
                markSincePrev = true;
                continue;
            }

            // 阿拉伯字母：用它在上下文裡的形狀；跟前一個字母之間的連字、上下文替換用成對的改變量
            if (forms is not null && forms[i] >= 0)
            {
                prevFace = null;   // 字距是對基本字形的，形狀換過之後不套
                if (face.ArabicFor(language) is not { } shapes || !shapes.Forms.TryGetValue(cp, out var formAdvances))
                {
                    complex = true;
                    totalUnits += advance * scale;
                    prevShapes = null;
                    continue;
                }
                totalUnits += formAdvances[forms[i]] * scale;
                var chained = ReferenceEquals(shapes, prevShapes);
                if (chained)
                {
                    if (!shapes.PairLetters.Contains(prevArabic) || !shapes.PairLetters.Contains(cp)) complex = true;   // 這一對沒驗證過
                    else if (shapes.Pairs.TryGetValue(ArabicShapes.Key(prevArabic, prevForm, cp, forms[i]), out var pairDelta))
                        totalUnits += pairDelta * scale;

                    if (shapes.TripleLetters.Contains(cp) && prev2Arabic >= 0)
                    {
                        if (markBeforePrev || markSincePrev || !shapes.PairLetters.Contains(prev2Arabic)) complex = true;
                        else if (shapes.Triples.TryGetValue(ArabicShapes.Key3(prev2Arabic, prev2Form, prevArabic, prevForm, cp, forms[i]), out var tripleDelta))
                            totalUnits += tripleDelta * scale;
                    }
                }
                if (chained) { prev2Arabic = prevArabic; prev2Form = prevForm; }
                else { prev2Arabic = -1; prev2Form = -1; }
                markBeforePrev = chained && markSincePrev;
                markSincePrev = false;
                prevShapes = shapes;
                prevArabic = cp;
                prevForm = forms[i];
                continue;
            }

            // 印度系文字：一個字素叢集一次量（匯出時算好的寬度），叢集之間相加
            if (IndicClusters.Of(cp) is { } indic)
            {
                if (indic.Plain.Contains(cp)) { totalUnits += advance * scale; prevFace = null; continue; }
                var length = IndicClusters.Match(cps, i, indic);
                IReadOnlyDictionary<string, int>? clusters = null;
                if (face.Indic is { } indicData
                    && !(IndicClusters.LanguageSystem(language) is { } indicLang && indicData.TryGetValue(IndicClusters.DataKey(indic, indicLang), out clusters)))
                    indicData.TryGetValue(indic.Tag, out clusters);
                if (length == 0 || clusters is null)
                {
                    complex = true;
                    totalUnits += advance * scale;
                    prevFace = null;
                    continue;
                }
                var units = 0;
                for (var k = i; k < i + length; k++)
                {
                    if (!face.TryAdvance(cps[k], out var a)) { units = -1; break; }   // 叢集裡的字要同一支字型畫
                    units += a;
                }
                if (units < 0) { complex = true; prevFace = null; continue; }
                var cluster = cps.Skip(i).Take(length).ToList();
                totalUnits += (units + (clusters.TryGetValue(IndicClusters.Key(cluster), out var d) ? d : 0)) * scale;
                // 子音 + virama 後面直接接子音：連寫（半形…）變窄的量，取所有接法裡最少的那個
                var head = IndicClusters.ConjunctHead(indic, cluster);
                // 只套在一串連寫的**第一個** virama：三個子音（क्र्श）時中間那個子音不能同時當下加形又當 reph，兩組加起來會窄掉 30%
                var chained = afterConjunctHead;
                afterConjunctHead = head >= 0;
                // 後面那個叢集自己又是「子音 + virama」（三個子音以上的連寫）也不套：後加形、下加形會再改一次（孟加拉文的 ya-phala）
                var nextIsHead = i + length + 1 < cps.Count && cps[i + length + 1] == indic.Virama
                                 && (i + length + 2 >= cps.Count || !indic.Modifiers.Contains(cps[i + length + 2]));
                if (head >= 0 && !chained && !nextIsHead && i + length < cps.Count && indic.Consonants.Contains(cps[i + length]) && face.TryAdvance(cps[i + length], out _)
                    && clusters.TryGetValue(IndicClusters.ConjunctKey(head, cps[i + length]), out var conjunct))
                    totalUnits += conjunct * scale;
                // 後面接的不是同一個文字系统的字（標點、空白…）：字型可能把它們之間撐開，加上匯出時量到的最大值
                if (i + length < cps.Count && IndicClusters.Of(cps[i + length]) != indic
                    && clusters.TryGetValue(IndicClusters.TrailingKey, out var trailing))
                    totalUnits += trailing * scale;
                i += length - 1;
                prevFace = null;
                continue;
            }

            if (IsComplexScript(cp) && !IsArabicPlain(cp)) complex = true;

            totalUnits += advance * scale;
            if (ReferenceEquals(face, prevFace) && face.Kerning is { } kern && scale == prevScale)
            {
                var adjust = kern.Adjust(prevCp, cp);
                if (bidi && (!Rune.IsLetterOrDigit(new Rune(prevCp)) || !Rune.IsLetterOrDigit(new Rune(cp)))) adjust = Math.Max(adjust, 0);
                totalUnits += adjust * scale;
            }
            prevFace = face;
            prevCp = cp;
            prevScale = scale;
        }

        var px = totalUnits * fontSizePx / primary.UnitsPerEm;
        return new Measurement(px, missing, complex, usedFallback) { FirstMissing = firstMissing };
    }

    /// <summary>
    /// 看不見、不佔寬度的字：變體選擇符（☀️ 後面的 U+FE0F）、零寬空白／連接符、BOM、方向控制字元。
    /// LayoutOracle 實測：WPF 給它們的寬度都是 0。第一版把它們當成「度量檔裡沒有的字」，
    /// 於是任何帶表情符號變體的字串都變成量不到。
    /// </summary>
    public static bool IsDefaultIgnorable(int cp) =>
        cp is 0x00AD or 0x034F or 0x061C or 0xFEFF
           or (>= 0x180B and <= 0x180F)
           or (>= 0x200B and <= 0x200F)
           or (>= 0x202A and <= 0x202E)
           or (>= 0x2060 and <= 0x206F)
           or (>= 0xFE00 and <= 0xFE0F)
           or (>= 0xE0000 and <= 0xE0FFF);

    private static bool IsRightToLeft(int cp) =>
        cp is (>= 0x0590 and <= 0x08FF) or (>= 0xFB1D and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF);

    /// <summary>阿拉伯文區塊裡不變形的字：阿拉伯—印度數字、波斯數字、阿拉伯逗號／分號／問號／句號、百分號與千分位符號，
    /// 以及不跟左右連寫的字母。
    /// 照 advance 量（WpfOracle --scripts 實測）。</summary>
    private static bool IsArabicPlain(int cp) =>
        cp is (>= 0x0660 and <= 0x066D) or (>= 0x06F0 and <= 0x06F9) or 0x060C or 0x061B or 0x061F or 0x06D4
        || (ArabicJoining.IsArabic(cp) && ArabicJoining.TypeOf(cp) == ArabicJoining.Type.NonJoining
            && CharUnicodeInfo.GetUnicodeCategory(cp) == UnicodeCategory.OtherLetter);   // 不連寫的字母（hamza ء）

    /// <summary>字形會依上下文變形的文字系統 —— 把 advance 加起來完全不對。
    /// 這類一律標成不可量測，不猜（規畫書 §4.3）。
    ///
    /// 希伯來文（U+0590–05FF、表現形式 FB1D–FB4F）與泰文（U+0E00–0E7F）**不在這裡**：WpfOracle --scripts 拿真實譯文實測，
    /// 把 advance 加起來（含字距）跟 WPF 只差 0.02% 以內（Segoe UI／Arial／Tahoma；Leelawadee UI／Tahoma）。
    /// 它們不換字形，附加符號的 advance 本來就是 0。</summary>
    public static bool IsComplexScript(int cp) =>
        cp is (>= 0x0600 and <= 0x08FF)   // 阿拉伯文（含補充與擴充）、敘利亞文、它拿文、N'Ko
           or (>= 0x0900 and <= 0x0DFF)   // 天城文～僧伽羅文（印度系）
           or (>= 0x0E80 and <= 0x0EFF)   // 寮文（沒實測）
           or (>= 0x0F00 and <= 0x0FFF)   // 藏文
           or (>= 0x1000 and <= 0x109F)   // 緬甸文
           or (>= 0x1780 and <= 0x17FF)   // 高棉文
           or (>= 0x1800 and <= 0x18AF)   // 蒙古文
           or (>= 0xFB50 and <= 0xFDFF)   // 阿拉伯表現形式 A
           or (>= 0xFE70 and <= 0xFEFF);  // 阿拉伯表現形式 B
}
