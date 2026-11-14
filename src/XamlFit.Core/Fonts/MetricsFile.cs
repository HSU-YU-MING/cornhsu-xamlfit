using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlFit.Core.Fonts;

/// <summary>
/// <c>xamlfit.fonts.json</c> 的讀寫。
///
/// **這個檔案裡只有數字。** 沒有字形外框、沒有字型程式碼、沒有任何可以還原成字型的東西。
/// 使用者在「有字型」的機器上從自己已授權的字型匯出，commit 進自己的 repo；
/// CI 讀數字，不需要安裝任何字型。我們從未散布任何字型。
///
/// ⚠ 發版前仍要確認字型 EULA 對「萃取 advance width 並 commit」的立場（規畫書 §4.2）。
/// </summary>
public sealed class MetricsFile
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonPropertyName("generatedBy")] public string GeneratedBy { get; set; } = "xamlfit";
    [JsonPropertyName("generatedOn")] public string GeneratedOn { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } =
        "Advance widths only. Contains no glyph outlines and no font software.";
    [JsonPropertyName("fonts")] public List<FontEntry> Fonts { get; set; } = [];

    public sealed class FontEntry
    {
        [JsonPropertyName("family")] public string Family { get; set; } = "";
        [JsonPropertyName("weight")] public int Weight { get; set; } = 400;
        [JsonPropertyName("style")] public string Style { get; set; } = "Normal";
        [JsonPropertyName("stretch")] public string Stretch { get; set; } = "Normal";
        [JsonPropertyName("unitsPerEm")] public int UnitsPerEm { get; set; }
        [JsonPropertyName("ascender")] public int Ascender { get; set; }
        [JsonPropertyName("descender")] public int Descender { get; set; }
        [JsonPropertyName("lineGap")] public int LineGap { get; set; }
        [JsonPropertyName("defaultAdvance")] public int DefaultAdvance { get; set; }

        /// <summary>[起始碼位, 結束碼位, 前進寬度] 的陣列。</summary>
        [JsonPropertyName("ranges")] public List<int[]> Ranges { get; set; } = [];
    }

    // ⚠ 不用 JsonSerializer 的 WriteIndented 輸出。
    //   它會把每個 [start,end,advance] 攤成五行 —— Segoe UI 一支字型就 2.1 MB。
    //   這個檔案要進使用者的 repo，大小是設計條件，不是細節。
    //   手寫序列化：外層保持可讀的縮排，ranges 壓成一行。讀取端仍是標準 JSON。

    public static MetricsFile FromMetrics(IEnumerable<FontMetrics> metrics, string toolVersion, DateOnly date)
    {
        var f = new MetricsFile
        {
            GeneratedBy = $"xamlfit {toolVersion}",
            GeneratedOn = date.ToString("yyyy-MM-dd"),
        };

        foreach (var m in metrics.OrderBy(m => m.Family, StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(m => m.Weight).ThenBy(m => m.Style))
        {
            f.Fonts.Add(new FontEntry
            {
                Family = m.Family,
                Weight = m.Weight,
                Style = m.Style.ToString(),
                Stretch = m.Stretch,
                UnitsPerEm = m.UnitsPerEm,
                Ascender = m.Ascender,
                Descender = m.Descender,
                LineGap = m.LineGap,
                DefaultAdvance = m.DefaultAdvance,
                Ranges = [.. m.Ranges.Select(r => new[] { r.Start, r.End, r.Advance })],
            });
        }

        return f;
    }

    public string ToJson()
    {
        var sb = new System.Text.StringBuilder(1 << 20);
        sb.Append("{\n");
        sb.Append($"  \"schemaVersion\": {SchemaVersion},\n");
        sb.Append($"  \"generatedBy\": {Quote(GeneratedBy)},\n");
        sb.Append($"  \"generatedOn\": {Quote(GeneratedOn)},\n");
        sb.Append($"  \"note\": {Quote(Note)},\n");
        sb.Append("  \"fonts\": [\n");

        for (var i = 0; i < Fonts.Count; i++)
        {
            var f = Fonts[i];
            sb.Append("    {\n");
            sb.Append($"      \"family\": {Quote(f.Family)},\n");
            sb.Append($"      \"weight\": {f.Weight},\n");
            sb.Append($"      \"style\": {Quote(f.Style)},\n");
            sb.Append($"      \"stretch\": {Quote(f.Stretch)},\n");
            sb.Append($"      \"unitsPerEm\": {f.UnitsPerEm},\n");
            sb.Append($"      \"ascender\": {f.Ascender},\n");
            sb.Append($"      \"descender\": {f.Descender},\n");
            sb.Append($"      \"lineGap\": {f.LineGap},\n");
            sb.Append($"      \"defaultAdvance\": {f.DefaultAdvance},\n");
            sb.Append("      \"ranges\": [");
            for (var j = 0; j < f.Ranges.Count; j++)
            {
                var r = f.Ranges[j];
                if (j > 0) sb.Append(',');
                sb.Append('[').Append(r[0]).Append(',').Append(r[1]).Append(',').Append(r[2]).Append(']');
            }
            sb.Append("]\n");
            sb.Append(i == Fonts.Count - 1 ? "    }\n" : "    },\n");
        }

        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    private static string Quote(string s) => JsonSerializer.Serialize(s);

    public static MetricsFile Parse(string json)
    {
        var f = JsonSerializer.Deserialize<MetricsFile>(json)
                ?? throw new InvalidDataException("metrics file is empty");
        if (f.SchemaVersion > CurrentSchemaVersion)
            throw new InvalidDataException(
                $"metrics file schemaVersion {f.SchemaVersion} is newer than this tool understands ({CurrentSchemaVersion}); upgrade xamlfit");
        return f;
    }

    public IReadOnlyList<FontMetrics> ToMetrics()
    {
        var list = new List<FontMetrics>(Fonts.Count);
        foreach (var e in Fonts)
        {
            if (e.UnitsPerEm <= 0) continue;
            var ranges = new List<AdvanceRange>(e.Ranges.Count);
            foreach (var r in e.Ranges)
                if (r.Length >= 3) ranges.Add(new AdvanceRange(r[0], r[1], r[2]));
            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

            list.Add(new FontMetrics
            {
                Family = e.Family,
                Weight = e.Weight,
                Style = Enum.TryParse<FontStyleKind>(e.Style, true, out var s) ? s : FontStyleKind.Normal,
                Stretch = e.Stretch,
                UnitsPerEm = e.UnitsPerEm,
                Ascender = e.Ascender,
                Descender = e.Descender,
                LineGap = e.LineGap,
                DefaultAdvance = e.DefaultAdvance,
                Ranges = ranges,
            });
        }
        return list;
    }
}
