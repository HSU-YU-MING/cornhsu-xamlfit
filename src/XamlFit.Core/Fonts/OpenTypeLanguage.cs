namespace XamlFit.Core.Fonts;

/// <summary>
/// 元素語言（xml:lang）→ OpenType 語言系統的標記（字型裡「latn/TRK」的「TRK」）。只列 Windows 字型裡拉丁、希臘、西里爾文字系統
/// 下有定義的語言（會換字形或字距的那些，見 <see cref="FontMetrics.LanguageGlyphs"/> 與 <see cref="KerningTable.Systems"/>），
/// 加上常見的；不在表裡的語言用文字系統的預設。
/// </summary>
public static class OpenTypeLanguage
{
    private static readonly Dictionary<string, string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tr"] = "TRK", ["az"] = "AZE", ["crh"] = "CRT", ["kk"] = "KAZ", ["tt"] = "TAT", ["ba"] = "BSH", ["cv"] = "CHU",
        ["ro"] = "ROM", ["mo"] = "MOL", ["lt"] = "LTH", ["lv"] = "LVI", ["pl"] = "PLK", ["sr"] = "SRB", ["bs"] = "BOS",
        ["hr"] = "HRV", ["mk"] = "MKD", ["bg"] = "BGR", ["ru"] = "RUS", ["uk"] = "UKR", ["be"] = "BEL", ["ca"] = "CAT",
        ["nl"] = "NLD", ["de"] = "DEU", ["fr"] = "FRA", ["es"] = "ESP", ["it"] = "ITA", ["pt"] = "PTG", ["sv"] = "SVE",
        ["da"] = "DAN", ["nb"] = "NOR", ["no"] = "NOR", ["nn"] = "NYN", ["fi"] = "FIN", ["is"] = "ISL", ["hu"] = "HUN",
        ["cs"] = "CSY", ["sk"] = "SKY", ["sl"] = "SLV", ["et"] = "ETI", ["el"] = "ELL", ["ga"] = "IRI", ["cy"] = "WEL",
        ["eu"] = "EUQ", ["gl"] = "GAL", ["sq"] = "SQI", ["mt"] = "MTS", ["vi"] = "VIT", ["ky"] = "KIR", ["tg"] = "TAJ",
        ["mn"] = "MNG", ["uz"] = "UZB", ["tk"] = "TKM", ["ky-KG"] = "KIR", ["sr-Latn"] = "SRB",
    };

    /// <summary>語言標記（「tr-TR」「sr-Cyrl-RS」）的語言系統；沒有回 null。</summary>
    public static string? Tag(string? language)
    {
        if (string.IsNullOrEmpty(language)) return null;
        var primary = language.Split('-', '_')[0];
        return Tags.TryGetValue(primary, out var tag) ? tag : null;
    }
}
