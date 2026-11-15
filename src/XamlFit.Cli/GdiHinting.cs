using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using XamlFit.Core.Fonts;

namespace XamlFit.Cli;

/// <summary>
/// <c>fonts export --hinted</c>（只在 Windows）：請 GDI 算每個字在 8～48 px 的 hinting 寬度，補上字型 hdmx 表沒列的像素大小。
/// WPF 的 TextFormattingMode="Display" 用的就是這種寬度（WpfOracle --display-gdi：不含字距時跟 WPF 一樣，差的只有字距）。
/// 存的是跟「線性寬度 0.5 進位」的差，跟 hdmx 的資料同一個格式；檔案裡仍然只有數字。
/// GDI 選到的字面跟要的不一樣（名稱、字重、斜體任何一個不對）就不寫 —— 寫錯的寬度比沒有更糟。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class GdiHinting
{
    public const int MinPpem = 8;
    public const int MaxPpem = 48;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceExW(string path, uint flags, IntPtr reserved);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveFontResourceExW(string path, uint flags, IntPtr reserved);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic,
        uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetCharWidth32W(IntPtr dc, uint first, uint last, [Out] int[] widths);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetTextFaceW(IntPtr dc, int count, [Out] char[] face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextMetricsW(IntPtr dc, out TEXTMETRICW metrics);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TEXTMETRICW
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading, tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang,
            tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
        public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }

    private const uint FrPrivate = 0x10;

    /// <summary>把這支字型檔裡每個字面的 GDI hinting 寬度加上去。回傳加好的字面與沒加成的字面（附原因）。</summary>
    public static (List<FontMetrics> Faces, List<string> Skipped) Add(string path, IReadOnlyList<FontMetrics> faces)
    {
        var result = new List<FontMetrics>();
        var skipped = new List<string>();
        var added = AddFontResourceExW(path, FrPrivate, IntPtr.Zero) > 0;
        var dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            foreach (var face in faces)
            {
                var hinted = Read(dc, face, out var why);
                if (hinted is null)
                {
                    skipped.Add($"{face.Family} {face.Weight}{(face.Style != FontStyleKind.Normal ? " italic" : "")}: {why}");
                    result.Add(face);
                    continue;
                }
                result.Add(face.WithHinted(hinted));
            }
        }
        finally
        {
            DeleteDC(dc);
            if (added) RemoveFontResourceExW(path, FrPrivate, IntPtr.Zero);
        }
        return (result, skipped);
    }

    private static Dictionary<int, IReadOnlyDictionary<int, int>>? Read(IntPtr dc, FontMetrics face, out string why)
    {
        why = "";
        var name = face.LegacyFamily ?? face.Family;
        var italic = face.Style != FontStyleKind.Normal;
        // GDI 回報的是系統語言的名稱（繁中 Windows：「微軟正黑體」「細明體」，Light 字重是「微軟正黑體 Light」）：
        // 英文名、各語言的別名、別名加上舊式家族名的字重尾巴都算同一支；字重與斜體另外比
        var suffix = face.LegacyFamily is { } legacy && legacy.StartsWith(face.Family, StringComparison.OrdinalIgnoreCase)
            ? legacy[face.Family.Length..] : "";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name, face.Family };
        foreach (var alias in face.Aliases)
        {
            names.Add(alias);
            names.Add(alias + suffix);
        }
        var merged = face.Hinted?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];
        var buffer = new int[0x10000];

        for (var ppem = MinPpem; ppem <= MaxPpem; ppem++)
        {
            if (merged.ContainsKey(ppem)) continue;   // hdmx 已經有：用字型自己存的
            var font = CreateFontW(-ppem, 0, 0, 0, face.Weight, italic ? 1u : 0u, 0, 0, 1 /*DEFAULT_CHARSET*/, 7 /*OUT_TT_ONLY_PRECIS*/, 0,
                3 /*NONANTIALIASED_QUALITY*/, 0, name);
            if (font == IntPtr.Zero) { why = "CreateFont failed"; return null; }
            var old = SelectObject(dc, font);
            try
            {
                // 確認 GDI 選到的就是要的字面
                var chars = new char[64];
                var n = GetTextFaceW(dc, chars.Length, chars);
                var selected = new string(chars, 0, Math.Max(0, n - 1));
                if (!names.Contains(selected)) { why = $"GDI selected \"{selected}\""; return null; }
                if (!GetTextMetricsW(dc, out var tm)) { why = "GetTextMetrics failed"; return null; }
                if ((tm.tmItalic != 0) != italic || Math.Abs(tm.tmWeight - face.Weight) > 50)
                {
                    why = $"GDI selected weight {tm.tmWeight}{(tm.tmItalic != 0 ? " italic" : "")}";
                    return null;
                }

                // 一段連續的碼位問一次（寬度一樣的區段常常一個字一段，逐段問要幾百萬次呼叫）
                var deltas = new Dictionary<int, int>();
                var i = 0;
                while (i < face.Ranges.Count && face.Ranges[i].Start <= 0xFFFF)
                {
                    var j = i;
                    while (j + 1 < face.Ranges.Count && face.Ranges[j + 1].Start == face.Ranges[j].End + 1 && face.Ranges[j + 1].Start <= 0xFFFF) j++;
                    var start = face.Ranges[i].Start;
                    var end = Math.Min(face.Ranges[j].End, 0xFFFF);
                    if (!GetCharWidth32W(dc, (uint)start, (uint)end, buffer)) { why = "GetCharWidth32 failed"; return null; }
                    for (var k = i; k <= j; k++)
                    {
                        var range = face.Ranges[k];
                        var linear = TextMeasure.RoundHalfUp((double)range.Advance * ppem / face.UnitsPerEm);
                        for (var cp = range.Start; cp <= Math.Min(range.End, 0xFFFF); cp++)
                            if (buffer[cp - start] - linear is var delta && delta != 0) deltas[cp] = delta;
                    }
                    i = j + 1;
                }
                merged[ppem] = deltas;
            }
            finally
            {
                SelectObject(dc, old);
                DeleteObject(font);
            }
        }
        return merged;
    }
}
