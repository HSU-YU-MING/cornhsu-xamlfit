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

    /// <summary>
    /// 自己宣告的控制項內距（DIP，左右合計）：<c>{ "Card": 16, "MetroWindow": 2, "GroupBox.Header": 9 }</c>。
    /// 鍵是元素的區域名稱（不含命名空間前綴）。第三方控制項、或被佈景重畫的標準控制項，
    /// 工具不知道它們的範本吃掉多少寬度 —— 宣告之後才量得到。
    /// </summary>
    [JsonPropertyName("chrome")] public Dictionary<string, double>? Chrome { get; set; }

    /// <summary>
    /// 來源語言（XAML 裡寫死的文字、中性語言的 .resx）是哪個語言。
    /// 只影響「沒寫 FontFamily 的文字用什麼字型量」：WPF 的預設字型跟著 Windows 語言走（見 CultureFonts）。
    /// </summary>
    [JsonPropertyName("sourceCulture")] public string? SourceCulture { get; set; }

    /// <summary>
    /// 沒寫 xml:lang 的元素是什麼語言。WPF 的預設是 en-US（跟 Windows 的語言無關），它決定中日韓的字用哪支字型補。
    /// 在地化的 App 常在程式碼裡把它改成目前的文化（<c>FrameworkElement.LanguageProperty.OverrideMetadata</c>），
    /// 那種情形寫 <c>"culture"</c>：檢查每個語言時就用那個語言。
    /// </summary>
    [JsonPropertyName("elementLanguage")] public string? ElementLanguage { get; set; }

    /// <summary>各語言 Windows 的預設 UI 字型，蓋掉內建的對應：<c>{ "th": "Leelawadee UI" }</c>。</summary>
    [JsonPropertyName("defaultFontByCulture")] public Dictionary<string, string>? DefaultFontByCulture { get; set; }

    /// <summary>
    /// 「把 A 當成 WPF 的 B」：<c>{ "GridEx": "Grid", "MetroWindow": "Window" }</c>。
    /// 給只加了行為、沒改版面與範本的子類別用。鍵與值都是區域名稱（不含命名空間前綴）。
    /// </summary>
    [JsonPropertyName("treatAs")] public Dictionary<string, string>? TreatAs { get; set; }

    /// <summary>用屬性產生欄的 Grid 子類別：{ "GridEx": { "columnCount": "ColumnCount", "starColumns": "StarColumns", "autoLayoutColumns": "AutoLayoutColumns" } }。</summary>
    [JsonPropertyName("gridPanels")] public Dictionary<string, GridPanelConfig>? GridPanels { get; set; }

    public sealed class GridPanelConfig
    {
        [JsonPropertyName("columnCount")] public string? ColumnCount { get; set; }
        [JsonPropertyName("starColumns")] public string? StarColumns { get; set; }
        [JsonPropertyName("autoLayoutColumns")] public string? AutoLayoutColumns { get; set; }
    }

    /// <summary>
    /// 寬度要到執行期才知道的控制項（數值框、程式碼填項目的下拉選單），它**最寬**會到多寬：<c>{ "IntegerUpDown": 100 }</c>。
    /// 只在它當鄰居、佔掉文字的空間時用；明寫的 Width 優先，MinWidth／MaxWidth 照樣夾住。宣告小了就是把文字的空間算大 ——
    /// 寫實際會出現的最大值。鍵是區域名稱（不含命名空間前綴）。
    /// </summary>
    [JsonPropertyName("controlWidths")] public Dictionary<string, double>? ControlWidths { get; set; }

    /// <summary>
    /// <c>TextFormattingMode="Display"</c> 的文字要在哪些顯示縮放比例下裝得下（1 = 100%）。預設 100%～200% 五種，取最寬的。
    /// Display 模式的字寬是字型裡各像素大小的 hinting 寬度，像素大小跟著縮放變；字型沒有某個大小的資料時那個位置量不到 ——
    /// App 只支援部分縮放比例時在這裡宣告，量得到的位置會變多。
    /// </summary>
    [JsonPropertyName("displayScales")] public List<double>? DisplayScales { get; set; }

    /// <summary>
    /// 垂直捲軸實際上不會出現的 ScrollViewer（或清單）：它們的 <c>x:Name</c>。
    /// 捲軸是 Auto 時工具一律假設它在（佔掉捲軸寬）；內容永遠不會比它高的，在這裡宣告就不扣。
    /// 宣告錯了就是把文字的空間算大 —— 只寫確定的。
    /// </summary>
    [JsonPropertyName("hiddenScrollBars")] public List<string>? HiddenScrollBars { get; set; }

    /// <summary>目標機器上沒有的字型家族。出現在 FontFamily 的後備清單裡時直接跳過，用下一個。</summary>
    [JsonPropertyName("absentFonts")] public List<string>? AbsentFonts { get; set; }

    /// <summary>專案套了外部佈景也照樣用內建的 Aero2 內距表（你確認過那個佈景沒有改動控制項的內距）。</summary>
    [JsonPropertyName("assumeDefaultTheme")] public bool? AssumeDefaultTheme { get; set; }

    /// <summary>
    /// 可以縮小的視窗用它的 MinWidth（而不是 Width）當寬度上界：回答「視窗縮到最小時裝不裝得下」，
    /// 正好是翻譯之後最常爆的情況。預設關；開了報告會說寬度來自 MinWidth。沒有 MinWidth、或不能縮小的視窗照舊用 Width。
    /// </summary>
    [JsonPropertyName("assumeWindowMinWidth")] public bool? AssumeWindowMinWidth { get; set; }

    /// <summary>
    /// 快顯（ContextMenu、ToolTip、Popup、子選單）最寬可以到多寬（DIP）。快顯跟著內容變寬、不裁切，所以預設不檢查；
    /// 宣告之後，快顯裡的文字拿它當上界（扣掉量到的快顯內距），超過的就報出來。報告標明上界來自宣告。
    /// </summary>
    [JsonPropertyName("popupMaxWidth")] public double? PopupMaxWidth { get; set; }

    /// <summary>
    /// 一般視窗的標題畫在哪個標題列上。唯一的值 "windows11"：照 Windows 11 實量的標題列（100%～175%）檢查標題，
    /// 放不下就報 trimmed（Windows 用刪節號縮短）。沒宣告時標題是 drawn-by-windows、不檢查 —— Windows 10 的標題列沒量過。
    /// </summary>
    [JsonPropertyName("windowTitleBar")] public string? WindowTitleBar { get; set; }

    /// <summary>
    /// Visibility 轉換器的鍵 → 綁定的值是 true 還是 false 時看得見：<c>{ "BoolToVis": true, "InverseBoolToVis": false }</c>。
    /// 同一排裡兩個元素綁同一個屬性、轉換器相反時，算其中一個的寬度不扣另一個。WPF 內建的 BooleanToVisibilityConverter 不用寫。
    /// </summary>
    [JsonPropertyName("visibilityConverters")] public Dictionary<string, bool>? VisibilityConverters { get; set; }

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
        Check("popupMaxWidth", config.PopupMaxWidth, 1, 4000);
        if (config.WindowTitleBar is { } titleBar && titleBar != "windows11")
            problems.Add(new Problem("windowTitleBar", "must be \"windows11\" (the only title bar that has been measured)"));

        if (config.DefaultFontFamily is { Length: 0 })
            problems.Add(new Problem("defaultFontFamily", "must not be empty"));

        foreach (var (field, list) in new (string, List<string>?)[]
                 {
                     ("exclude", config.Exclude), ("absentFonts", config.AbsentFonts),
                     ("cultures", config.Cultures), ("resx", config.Resx),
                 })
        {
            if (list is null) continue;
            for (var i = 0; i < list.Count; i++)
                if (string.IsNullOrWhiteSpace(list[i]))
                    problems.Add(new Problem($"{field}[{i}]", "must not be empty"));
        }

        if (config.ElementLanguage is { Length: 0 })
            problems.Add(new Problem("elementLanguage", "must be a language tag such as \"en-US\", or \"culture\""));

        if (config.SourceCulture is { Length: 0 })
            problems.Add(new Problem("sourceCulture", "must not be empty"));

        if (config.DefaultFontByCulture is not null)
            foreach (var (culture, font) in config.DefaultFontByCulture)
                if (string.IsNullOrWhiteSpace(culture) || string.IsNullOrWhiteSpace(font))
                    problems.Add(new Problem($"defaultFontByCulture[\"{culture}\"]", "culture and font must not be empty"));

        if (config.TreatAs is not null)
            foreach (var (from, to) in config.TreatAs)
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || from.Contains(':') || to.Contains(':'))
                    problems.Add(new Problem($"treatAs[\"{from}\"]", "both names are local element names, without a namespace prefix"));

        if (config.GridPanels is not null)
            foreach (var (name, spec) in config.GridPanels)
            {
                var props = new[] { spec?.ColumnCount, spec?.StarColumns, spec?.AutoLayoutColumns };
                if (string.IsNullOrWhiteSpace(name) || name.Contains(':') || name.Contains('.'))
                    problems.Add(new Problem($"gridPanels[\"{name}\"]", "the key is an element's local name, without a namespace prefix"));
                else if (spec is null || spec.ColumnCount is null && spec.AutoLayoutColumns is null)
                    problems.Add(new Problem($"gridPanels[\"{name}\"]", "name at least columnCount or autoLayoutColumns"));
                else if (props.Any(p => p is not null && (p.Trim().Length == 0 || p.Contains(':') || p.Contains('.'))))
                    problems.Add(new Problem($"gridPanels[\"{name}\"]", "each value is the property name the class uses, such as \"ColumnCount\""));
            }

        if (config.HiddenScrollBars is not null)
            foreach (var name in config.HiddenScrollBars)
                if (string.IsNullOrWhiteSpace(name) || name.Contains(':') || name.Contains('.') || name.Contains(' '))
                    problems.Add(new Problem($"hiddenScrollBars[\"{name}\"]", "each entry is the x:Name of a ScrollViewer or a list"));

        if (config.DisplayScales is not null)
        {
            if (config.DisplayScales.Count == 0)
                problems.Add(new Problem("displayScales", "list at least one display scaling (1 = 100%)"));
            foreach (var scale in config.DisplayScales)
                if (!double.IsFinite(scale) || scale < 0.5 || scale > 5)
                    problems.Add(new Problem("displayScales", $"{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)} is not a display scaling between 0.5 and 5 (1 = 100%)"));
        }

        if (config.ControlWidths is not null)
            foreach (var (name, value) in config.ControlWidths)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Contains(':') || name.Contains('.'))
                    problems.Add(new Problem($"controlWidths[\"{name}\"]", "the key is an element's local name, without a namespace prefix"));
                if (!double.IsFinite(value) || value <= 0 || value > 4000)
                    problems.Add(new Problem($"controlWidths[\"{name}\"]", "must be a width in DIP between 0 and 4000"));
            }

        if (config.Chrome is not null)
            foreach (var (name, value) in config.Chrome)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Contains(':'))
                    problems.Add(new Problem($"chrome[\"{name}\"]", "the key is an element's local name, without a namespace prefix"));
                if (!double.IsFinite(value) || value < 0 || value > 2000)
                    problems.Add(new Problem($"chrome[\"{name}\"]", "must be a number between 0 and 2000"));
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
