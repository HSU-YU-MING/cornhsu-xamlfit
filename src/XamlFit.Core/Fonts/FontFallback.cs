using System.Globalization;
using System.Text.Json;

namespace XamlFit.Core.Fonts;

/// <summary>
/// WPF 替「指定的字型畫不出來的字」挑字型的規則。
///
/// 資料直接取自 WPF 自己的 <c>GlobalUserInterface.CompositeFont</c>（PresentationCore 的資源，
/// dotnet/wpf，MIT 授權），由 <c>data/font-fallback.json</c> 隨工具附上。
///
/// ⚠ 第一版的「自動後備」是把度量檔裡**其他所有字型依字母順序**接在後面 —— 跟 WPF 完全無關。
///   CorpusRender 實測 Playnite 的日文：WPF 用 Yu Gothic UI 的比例寬度假名，「クラッシュレポート」是 90.22 px；
///   工具挑到全形等寬的字型，量成 126 px（寬了 40%）。
///
/// 規則：依檔案順序，第一個「範圍包含這個字、而且語言對得上（沒寫語言的對得上任何語言）」的對應，
/// 在它的目標字型裡找第一支畫得出這個字的。
///
/// ⚠ 語言是**元素的** Language（xml:lang），WPF 預設是 en-US —— 跟 Windows 是什麼語言無關。
///   所以在日文 Windows 上，沒設 Language 的 App 顯示中文也是用 Yu Gothic UI 補字。
/// </summary>
public static class FontFallback
{
    private sealed record Map((int Start, int End)[] Ranges, string? Language, string[] Targets, double Scale);

    private static readonly Lazy<IReadOnlyList<Map>> Maps = new(Load);

    private static IReadOnlyList<Map> Load()
    {
        var asm = typeof(FontFallback).Assembly;
        using var s = asm.GetManifestResourceStream("XamlFit.Core.font-fallback.json")
                      ?? throw new InvalidOperationException("embedded font-fallback.json is missing");
        using var doc = JsonDocument.Parse(s);

        var list = new List<Map>();
        foreach (var m in doc.RootElement.GetProperty("maps").EnumerateArray())
        {
            var ranges = m.GetProperty("u").EnumerateArray()
                .Select(r => (r[0].GetInt32(), r[1].GetInt32())).ToArray();
            var targets = m.GetProperty("t").EnumerateArray().Select(t => t.GetString()!).ToArray();
            var lang = m.TryGetProperty("l", out var l) ? l.GetString() : null;
            var scale = m.TryGetProperty("s", out var sc) ? sc.GetDouble() : 1.0;
            list.Add(new Map(ranges, lang, targets, scale));
        }
        return list;
    }

    /// <summary>表裡出現過的所有補字字型（fonts export 用來提醒 --family 漏掉了哪些）。</summary>
    public static IReadOnlyList<string> Families =>
        Maps.Value.SelectMany(m => m.Targets).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>這個字在這個語言下，WPF 會依序嘗試哪些字型（以及寬度要乘的倍率）。</summary>
    public static (IReadOnlyList<string> Targets, double Scale) For(int codepoint, string language)
    {
        foreach (var map in Maps.Value)
        {
            if (!Covers(map, codepoint)) continue;
            if (map.Language is not null && !LanguageMatches(map.Language, language)) continue;
            return (map.Targets, map.Scale);
        }
        return ([], 1.0);
    }

    private static bool Covers(Map map, int cp)
    {
        foreach (var (a, b) in map.Ranges)
            if (cp >= a && cp <= b) return true;
        return false;
    }

    /// <summary>
    /// 對應的語言是不是元素語言本身或它的上層（ja 對得上 ja-JP；zh-Hant 對得上 zh-TW）。
    ///
    /// ⚠ **不查 .NET 的文化資料庫**，用固定的規則。Linux 的 CI 映像檔常常開著
    ///   globalization-invariant 模式，那裡 <c>CultureInfo.GetCultureInfo("zh-TW")</c> 會丟例外 ——
    ///   第一版因此在 Linux 上認不出 zh-TW 是繁體中文，中文字拿日文字型去補，同一份專案在 CI 與本機量出不同的數字。
    /// </summary>
    internal static bool LanguageMatches(string mapLanguage, string elementLanguage)
    {
        foreach (var tag in Ancestors(elementLanguage))
            if (string.Equals(tag, mapLanguage, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>語言標籤本身與它的上層：zh-TW → zh-Hant → zh；ja-JP → ja；de-CH-1996 → de-CH → de。</summary>
    internal static IEnumerable<string> Ancestors(string tag)
    {
        var t = tag.Trim().Replace('_', '-');
        if (t.Length == 0) yield break;
        yield return t;

        var parts = t.Split('-');
        if (parts[0].Equals("zh", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
        {
            // 中文的上層是文字（Hant／Hans），不是單純去掉地區
            var script = parts.Skip(1).FirstOrDefault(p => p.Length == 4);
            var region = parts.Skip(1).FirstOrDefault(p => p.Length == 2)?.ToUpperInvariant();
            script ??= region is "TW" or "HK" or "MO" ? "Hant" : region is "CN" or "SG" ? "Hans" : null;
            if (region is "HK" or "MO") yield return "zh-HK";
            if (script is not null) yield return "zh-" + script;
            yield return "zh";
            yield break;
        }

        for (var i = parts.Length - 1; i >= 1; i--)
            yield return string.Join('-', parts.Take(i));
    }

    /// <summary>WPF 元素的預設 Language。跟 Windows 的顯示語言無關。</summary>
    public const string DefaultLanguage = "en-US";
}
