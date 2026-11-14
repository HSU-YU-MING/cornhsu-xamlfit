namespace XamlFit.Core.Analysis;

/// <summary>
/// 一個語言的 Windows 上，沒寫 <c>FontFamily</c> 的文字用什麼字型畫。
///
/// ⚠ **WPF 的預設字型不是 Segoe UI，是「系統訊息字型」**（<c>SystemFonts.MessageFontFamily</c>），
///   它跟著 Windows 的顯示語言走。LayoutOracle 在繁體中文系統上實測是 Microsoft JhengHei UI ——
///   同一個字串 "Name:" 用它畫是 36.9 px，用 Segoe UI 是 34.3 px（寬 7%）。
///
///   所以同一個 App 在德文 Windows 與繁中 Windows 上，沒指定字型的文字寬度不一樣。
///   第一版一律假設 Segoe UI：檢查中日韓譯文時，譯文裡的拉丁字母、數字、標點都被量窄了，
///   而且靠它們撐開的 Auto 欄也被算窄、旁邊的欄被算寬 —— 兩個方向都是漏報。
///
/// 這張表是 Windows 10／11 各語言版本的訊息字型。只有繁中那一列是在這台機器上量過的；
/// 其餘來自 Windows 的字型設定，沒有逐一實測 —— 不對的話用設定檔的 <c>defaultFontByCulture</c> 蓋掉。
/// 其他語言（含所有拉丁、西里爾、希臘字母語系）都是 Segoe UI。
/// </summary>
public static class CultureFonts
{
    private static readonly (string Prefix, string Font)[] Builtin =
    [
        // 長的前綴排前面：zh-Hant 要先於 zh
        ("zh-Hant", "Microsoft JhengHei UI"),
        ("zh-TW", "Microsoft JhengHei UI"),
        ("zh-HK", "Microsoft JhengHei UI"),
        ("zh-MO", "Microsoft JhengHei UI"),
        ("zh", "Microsoft YaHei UI"),
        ("ja", "Yu Gothic UI"),
        ("ko", "Malgun Gothic"),
    ];

    /// <summary>這個語言的預設 UI 字型；跟一般情形（Segoe UI）一樣時回 null。</summary>
    /// <param name="overrides">設定檔宣告的對應（鍵是語言標籤，可以只寫前段：<c>th</c> 涵蓋 <c>th-TH</c>）。</param>
    public static string? DefaultFor(string culture, IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (overrides is not null)
        {
            // 最長的相符前綴贏：zh-Hant-TW → zh-Hant → zh
            string? best = null;
            var bestLength = -1;
            foreach (var (tag, font) in overrides)
                if (Matches(culture, tag) && tag.Length > bestLength) { best = font; bestLength = tag.Length; }
            if (best is not null) return best;
        }

        foreach (var (prefix, font) in Builtin)
            if (Matches(culture, prefix)) return font;

        return null;
    }

    /// <summary>「zh-Hant-TW」符合「zh-Hant」與「zh」，但「zhx」不符合「zh」。</summary>
    private static bool Matches(string culture, string prefix) =>
        culture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && (culture.Length == prefix.Length || culture[prefix.Length] is '-' or '_');
}
