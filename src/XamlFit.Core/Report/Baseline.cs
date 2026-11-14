using System.Text.Json;
using System.Text.Json.Serialization;
using XamlFit.Core.Analysis;

namespace XamlFit.Core.Report;

/// <summary>
/// 已知債清單。第一次跑產生，之後只擋新增的。
///
/// ⚠ 鍵刻意**不含行號、不含像素值** —— 行號會隨無關的編輯漂移，
///   像素值會隨字型度量重產而變。兩者任一進了鍵，一次格式化或一次
///   `fonts export` 就讓整份 baseline 失效，然後使用者就把它關掉了。
/// </summary>
public sealed class Baseline
{
    public const int CurrentKeyVersion = 1;

    // ⚠ 欄位名用 camelCase，跟度量檔與報告一致。
    //   baseline 的格式是 1.0 的凍結面之一，趁 0.x 統一掉。
    [JsonPropertyName("keyVersion")] public int KeyVersion { get; set; } = CurrentKeyVersion;
    [JsonPropertyName("generatedOn")] public string GeneratedOn { get; set; } = "";
    [JsonPropertyName("known")] public List<string> Known { get; set; } = [];

    public static Baseline FromFindings(IEnumerable<FitFinding> findings, DateOnly date) => new()
    {
        GeneratedOn = date.ToString("yyyy-MM-dd"),
        Known = [.. findings.Select(f => f.BaselineKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
    };

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static Baseline Parse(string json)
    {
        var b = JsonSerializer.Deserialize<Baseline>(json)
                ?? throw new InvalidDataException("baseline file is empty");
        if (b.KeyVersion > CurrentKeyVersion)
            throw new InvalidDataException(
                $"baseline keyVersion {b.KeyVersion} is newer than this tool understands ({CurrentKeyVersion}); upgrade xamlfit");
        return b;
    }

    public HashSet<string> AsSet() => new(Known, StringComparer.Ordinal);
}
