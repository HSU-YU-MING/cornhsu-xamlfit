using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlFit.Core.Analysis;

/// <summary>
/// <c>xamlfit.config.json</c>。
///
/// ⚠ **寫錯要 exit 2 並指出是哪一個欄位**，不能靜默忽略。
///   一個打錯字的設定被默默跳過，使用者會以為它生效了 —— 那是最糟的設定檔行為。
///   （抄 XamlContrast 的規矩：不認得的屬性名也是 exit 2。）
/// </summary>
public sealed class FitConfig
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")] public int? SchemaVersion { get; set; }
    [JsonPropertyName("defaultFontFamily")] public string? DefaultFontFamily { get; set; }
    [JsonPropertyName("defaultFontSize")] public double? DefaultFontSize { get; set; }
    [JsonPropertyName("marginPercent")] public double? MarginPercent { get; set; }
    [JsonPropertyName("minCoverage")] public double? MinCoverage { get; set; }
    [JsonPropertyName("metrics")] public string? Metrics { get; set; }

    /// <summary>相對於掃描根的路徑片段；比對不分大小寫。</summary>
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }

    /// <summary>執行期才組出來的鍵名（`Icon*`）。被蓋到的字串列入 suppressed 並可列出。</summary>
    [JsonPropertyName("usedAtRuntime")] public List<string>? UsedAtRuntime { get; set; }

    [JsonPropertyName("cultures")] public List<string>? Cultures { get; set; }
    [JsonPropertyName("resx")] public List<string>? Resx { get; set; }

    /// <summary>設定檔的問題。Field 是出問題的欄位名，給使用者直接定位。</summary>
    public sealed record Problem(string Field, string Message)
    {
        public override string ToString() => $"{Field}: {Message}";
    }

    /// <summary>
    /// 讀設定檔。回傳 (設定, 問題清單)。問題清單非空時呼叫端要 exit 2。
    /// </summary>
    public static (FitConfig? Config, IReadOnlyList<Problem> Problems) Load(string path)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { return (null, [new Problem(path, $"cannot be read: {ex.Message}")]); }

        FitConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<FitConfig>(text, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                // ⚠ 不認得的屬性名要報錯，不要默默吞掉 ——
                //   打錯字的設定被跳過，使用者會以為它生效了
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            });
        }
        catch (JsonException ex)
        {
            return (null, [new Problem(path, ex.Message)]);
        }

        if (config is null) return (null, [new Problem(path, "the file is empty")]);

        var problems = new List<Problem>();

        if (config.SchemaVersion is { } v && v > CurrentSchemaVersion)
            problems.Add(new Problem("schemaVersion",
                $"{v} is newer than this tool understands ({CurrentSchemaVersion}); upgrade xamlfit"));

        // ⚠ NaN 對任何比較都是 false，範圍檢查擋不住它；IsFinite 一併擋掉 Infinity
        Check("defaultFontSize", config.DefaultFontSize, 1, 400);
        Check("marginPercent", config.MarginPercent, 0, 100);
        Check("minCoverage", config.MinCoverage, 0, 100);

        if (config.DefaultFontFamily is { Length: 0 })
            problems.Add(new Problem("defaultFontFamily", "must not be empty"));

        foreach (var (field, list) in new (string, List<string>?)[]
                 {
                     ("exclude", config.Exclude), ("usedAtRuntime", config.UsedAtRuntime),
                     ("cultures", config.Cultures), ("resx", config.Resx),
                 })
        {
            if (list is null) continue;
            for (var i = 0; i < list.Count; i++)
                if (string.IsNullOrWhiteSpace(list[i]))
                    problems.Add(new Problem($"{field}[{i}]", "must not be empty"));
        }

        return (config, problems);

        void Check(string field, double? value, double min, double max)
        {
            if (value is not { } d) return;
            if (!double.IsFinite(d) || d < min || d > max)
                problems.Add(new Problem(field, $"must be a number between {min:0.#} and {max:0.#}"));
        }
    }

    /// <summary>掃描根旁邊的預設檔名。</summary>
    public const string DefaultFileName = "xamlfit.config.json";
}
