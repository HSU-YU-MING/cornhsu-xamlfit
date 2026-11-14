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
    public bool IsReliable => MissingGlyphs == 0 && !ComplexScript;
}

public static class TextMeasure
{
    /// <summary>
    /// 量一段文字在指定字級下的寬度（DIP）。
    ///
    /// 公式：Σ advance(碼位) × 字級 ÷ unitsPerEm
    ///
    /// ⚠ 這條線性公式對應 WPF 的 <c>TextFormattingMode="Ideal"</c>（WPF 的預設）。
    ///   <c>Display</c> 模式會把字符對齊像素格，寬度略有不同。
    /// ⚠ 不含字距調整（kerning／GPOS）與連字。拉丁文 UI 標籤的誤差很小，
    ///   但那是「小」不是「零」—— 判定時要留安全邊界。
    /// </summary>
    /// <param name="declaredCount">鏈的前幾支是 XAML 明寫的；其餘是自動後備。
    /// 用到自動後備的量測會標記出來 —— Windows 的字型連結表可能選到別支字型。</param>
    public static Measurement Measure(
        string text, IReadOnlyList<FontMetrics> fallbackChain, double fontSizePx, int declaredCount = int.MaxValue)
    {
        if (string.IsNullOrEmpty(text) || fallbackChain.Count == 0)
            return new Measurement(0, 0, false);

        double totalUnits = 0;
        var missing = 0;
        var complex = false;
        var usedAuto = false;
        var primary = fallbackChain[0];

        foreach (var rune in text.AsSpan().EnumerateRunes())
        {
            var cp = rune.Value;

            if (IsComplexScript(cp)) complex = true;

            // 字型後備鏈：第一支畫得出來的就用它
            var found = false;
            for (var i = 0; i < fallbackChain.Count; i++)
            {
                var f = fallbackChain[i];
                if (!f.TryAdvance(cp, out var adv)) continue;
                // 不同字型的 unitsPerEm 可能不同，一律換算到主字型的尺度
                totalUnits += adv * (double)primary.UnitsPerEm / f.UnitsPerEm;
                if (i >= declaredCount) usedAuto = true;
                found = true;
                break;
            }

            if (!found) missing++;
        }

        var px = totalUnits * fontSizePx / primary.UnitsPerEm;
        return new Measurement(px, missing, complex, usedAuto);
    }

    /// <summary>字形會依上下文變形的文字系統 —— 把 advance 加起來完全不對。
    /// 這類一律標成不可量測，不猜（規畫書 §4.3）。</summary>
    public static bool IsComplexScript(int cp) =>
        cp is (>= 0x0590 and <= 0x05FF)   // 希伯來文
           or (>= 0x0600 and <= 0x06FF)   // 阿拉伯文
           or (>= 0x0700 and <= 0x074F)   // 敘利亞文
           or (>= 0x0900 and <= 0x0DFF)   // 天城文～僧伽羅文（印度系）
           or (>= 0x0E00 and <= 0x0E7F)   // 泰文
           or (>= 0x0F00 and <= 0x0FFF)   // 藏文
           or (>= 0x1780 and <= 0x17FF)   // 高棉文
           or (>= 0xFB50 and <= 0xFDFF)   // 阿拉伯表現形式 A
           or (>= 0xFE70 and <= 0xFEFF);  // 阿拉伯表現形式 B
}
