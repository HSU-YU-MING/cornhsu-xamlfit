using XamlFit.Core.Fonts;

namespace XamlFit.Core.OpenType;

/// <summary>
/// 依語言換字形（GSUB 'locl'）的字：拉丁、希臘、西里爾文字系統下每個語言系統，把每個字單獨套一次它的 locl，
/// 換成別的字形的記下來（碼位 → 換過的字形寬度；一個字換成好幾個字形就相加）。鍵是語言系統（「TRK」「ROM」「SRB」…）。
/// Arial 的土耳其文把 i 換成另一個字形（字距表裡沒有它的成對調整），羅馬尼亞文的 Ş ş Ţ ţ、立陶宛文的 í、塞爾維亞文的 б 也是。
/// 只看單一個字的替換；要看前後文的（加泰隆尼亞文 l·l）不在這裡。
/// </summary>
internal static class LanguageGlyphExport
{
    private static readonly string[] Scripts = ["latn", "cyrl", "grek"];
    private static readonly HashSet<string> Locl = ["locl"];
    private static readonly HashSet<string> NoForms = [];

    public static Dictionary<string, IReadOnlyDictionary<int, int>>? Read(SfntReader r, SortedDictionary<int, int> cmap, int[] advances)
    {
        if (!r.Has("GSUB")) return null;
        try
        {
            var engine = new LayoutEngine(r);
            var gsub = r.TableOffset("GSUB");
            var scriptList = gsub + r.U16(gsub + 4);
            var result = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
            var count = r.U16(scriptList);
            for (var s = 0; s < count; s++)
            {
                var scriptTag = r.AsciiString(scriptList + 2 + s * 6, 4);
                if (!Scripts.Contains(scriptTag)) continue;
                var script = scriptList + r.U16(scriptList + 2 + s * 6 + 4);
                var langCount = r.U16(script + 2);
                for (var l = 0; l < langCount; l++)
                {
                    var langTag = r.AsciiString(script + 4 + l * 6, 4);
                    var lookups = engine.Lookups(engine.LangSys(scriptTag, langTag), Locl);
                    if (lookups.Count == 0) continue;
                    foreach (var (cp, glyph) in cmap)
                    {
                        if (!InScript(scriptTag, cp)) continue;
                        var shaped = engine.Shape([new LayoutEngine.Glyph(glyph, 0, null)], lookups, NoForms);
                        if (shaped.Count == 1 && shaped[0].Id == glyph) continue;
                        var width = shaped.Sum(g => g.Id < advances.Length ? advances[g.Id] : 0);
                        var key = langTag.TrimEnd();
                        if (!result.TryGetValue(key, out var map)) result[key] = map = [];
                        map[cp] = map.TryGetValue(cp, out var old) ? Math.Max(old, width) : width;
                    }
                }
            }
            return result.Count > 0 ? result.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<int, int>)kv.Value, StringComparer.Ordinal) : null;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // 這個文字系統的字（加上共用的組合符號、標點：塞爾維亞文的 locl 也換 U+0306）
    private static bool InScript(string script, int cp) => script switch
    {
        "latn" => cp <= 0x02FF || cp is (>= 0x0300 and <= 0x036F) or (>= 0x1E00 and <= 0x1EFF) or (>= 0x2C60 and <= 0x2C7F) or (>= 0xA720 and <= 0xA7FF),
        "cyrl" => cp is (>= 0x0400 and <= 0x052F) or (>= 0x0300 and <= 0x036F) or (>= 0x02D8 and <= 0x02DD) or (>= 0xA640 and <= 0xA69F) or (>= 0x1C80 and <= 0x1C8F),
        "grek" => cp is (>= 0x0370 and <= 0x03FF) or (>= 0x0300 and <= 0x036F) or (>= 0x1F00 and <= 0x1FFF),
        _ => false,
    };

    private static readonly HashSet<string> Ccmp = ["ccmp"];

    /// <summary>
    /// 後面接組合符號時字會換成哪個字形（GSUB 'ccmp'）：每個字取各種符號裡最寬的那個字形，比原本寬才記。
    /// Arial 的「ע + ִ」WPF 畫成另一個 ע（1096，原本 1084 單位）；希伯來文的點、通用的組合符號（拉丁、希臘、西里爾）。
    /// </summary>
    public static Dictionary<int, int>? MarkedWidths(SfntReader r, SortedDictionary<int, int> cmap, int[] advances)
    {
        if (!r.Has("GSUB")) return null;
        try
        {
            var engine = new LayoutEngine(r);
            var result = new Dictionary<int, int>();
            foreach (var (script, letters, marks) in new (string, Func<int, bool>, Func<int, bool>)[]
            {
                ("hebr", cp => cp is >= 0x05D0 and <= 0x05EA or >= 0xFB1D and <= 0xFB4F, cp => cp is >= 0x0591 and <= 0x05C7 && TextMeasure.IsCombiningMark(cp)),
                ("latn", cp => cp <= 0x024F && char.IsLetter((char)cp), TextMeasure.IsCombiningMark),
                ("cyrl", cp => cp is >= 0x0400 and <= 0x04FF, TextMeasure.IsCombiningMark),
                ("grek", cp => cp is >= 0x0370 and <= 0x03FF, TextMeasure.IsCombiningMark),
            })
            {
                var langSys = engine.LangSys(script, "dflt");
                if (langSys < 0) continue;
                var lookups = engine.Lookups(langSys, Ccmp);
                if (lookups.Count == 0) continue;
                var markGlyphs = cmap.Where(kv => marks(kv.Key)).Select(kv => kv.Value).Distinct().ToList();
                foreach (var (cp, glyph) in cmap)
                {
                    if (!letters(cp)) continue;
                    var widest = 0;
                    foreach (var mark in markGlyphs)
                    {
                        var shaped = engine.Shape([new LayoutEngine.Glyph(glyph, 0, null), new LayoutEngine.Glyph(mark, 1, null)], lookups, NoForms);
                        // 換過的字（第一個字形）；符號 WPF 畫寬度 0
                        if (shaped.Count > 0 && shaped[0].Id != glyph && shaped[0].Id < advances.Length) widest = Math.Max(widest, advances[shaped[0].Id]);
                    }
                    if (widest > advances[glyph]) result[cp] = Math.Max(result.GetValueOrDefault(cp), widest);
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
