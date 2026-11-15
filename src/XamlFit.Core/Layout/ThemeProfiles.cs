using System.Globalization;
using System.Text.Json;

namespace XamlFit.Core.Layout;

/// <summary>樣式對字串內容做的事（MahApps 的 Button 畫成大寫、Square 按鈕畫成小寫）。</summary>
public enum TextCasing { AsIs, Upper, Lower }

/// <summary>
/// 一個控制項（或一個有 x:Key 的樣式）在某個佈景底下的樣子：chrome、樣式設的字型、文字大小寫。
/// 數字是 <c>prototype/ThemeChrome</c> 用 WPF 量的。
/// </summary>
/// <param name="TargetType">量在哪個控制項上。有 x:Key 的樣式只套在同一種控制項上才用這些數字。</param>
/// <param name="Chrome">吃掉的寬度，**已經含**樣式的 Padding 與 BorderThickness。null = 量不到。</param>
/// <param name="TextFontSize">範本裡畫字串的那個 TextBlock 自己的字級（跟控制項不同時；MaterialDesign 的 TabItem 標題 14）。</param>
public sealed record ThemeControl(
    string TargetType, double? Chrome,
    double Padding, double Border, InsetMode PaddingMode, InsetMode BorderMode, double MinWidth,
    string? FontFamily, double? FontSize, int? FontWeight, TextCasing Casing,
    string? TextFontFamily, double? TextFontSize, int? TextFontWeight)
{
    /// <summary>ScrollBar 的寬度等於系統捲軸寬（MaterialDesign）：跟著顯示縮放變，用工具自己的上限。</summary>
    public bool FollowsSystem { get; init; }

    /// <summary>樣式開了 UseLayoutRounding（HandyControl、WPF-UI 的 CheckBox）：寬度捨入到裝置像素，可能往上多一個像素。</summary>
    public bool LayoutRounding { get; init; }
}

/// <summary>
/// 一個量過的控制項函式庫佈景（MahApps.Metro 2.4、MaterialDesignThemes 5.3…）。
/// 只有專案參照的套件版本跟量的版本 major.minor 相同時才用 —— 佈景的內距會隨版本改。
/// </summary>
public sealed class ThemeProfile
{
    public required string Id { get; init; }
    /// <summary>NuGet 套件名稱（找專案參照的版本用）。</summary>
    public required string Package { get; init; }
    public required string Version { get; init; }
    /// <summary>pack URI 或 clr-namespace 指到這些組件時，算是這個佈景。</summary>
    public IReadOnlyList<string> Assemblies { get; init; } = [];
    /// <summary>字典型別的 XML 命名空間（&lt;materialDesign:BundledTheme/&gt;）。</summary>
    public IReadOnlyList<string> Namespaces { get; init; } = [];
    /// <summary>專案一定要合併這個字典才算（MaterialDesign 的 2 與 3 差在 Defaults 檔）。null = 不用。</summary>
    public string? Requires { get; init; }
    public IReadOnlyDictionary<string, ThemeControl> Controls { get; init; } = new Dictionary<string, ThemeControl>();
    public IReadOnlyDictionary<string, ThemeControl> Styles { get; init; } = new Dictionary<string, ThemeControl>();

    /// <summary>「2.4.11」→「2.4」。</summary>
    public string MajorMinor => MajorMinorOf(Version);

    public static string MajorMinorOf(string version)
    {
        var parts = version.Trim().Split('.', '-', '+');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version.Trim();
    }

    public bool Covers(string name, string? ns) =>
        Assemblies.Contains(name, StringComparer.OrdinalIgnoreCase) || (ns is not null && Namespaces.Contains(ns, StringComparer.Ordinal));
}

public static class ThemeProfiles
{
    private static IReadOnlyList<ThemeProfile>? _builtin;

    /// <summary>內建的佈景資料（<c>data/theme-chrome.json</c>，隨套件走）。</summary>
    public static IReadOnlyList<ThemeProfile> Builtin()
    {
        if (_builtin is not null) return _builtin;
        using var s = typeof(ThemeProfiles).Assembly.GetManifestResourceStream("XamlFit.Core.theme-chrome.json")
                      ?? throw new InvalidOperationException("embedded theme-chrome.json is missing");
        using var r = new StreamReader(s);
        return _builtin = Parse(r.ReadToEnd());
    }

    public static IReadOnlyList<ThemeProfile> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ThemeProfile>();
        if (!doc.RootElement.TryGetProperty("themes", out var themes) || themes.ValueKind != JsonValueKind.Array) return list;

        foreach (var t in themes.EnumerateArray())
        {
            static string Str(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            static List<string> Strings(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                    ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
                    : [];

            list.Add(new ThemeProfile
            {
                Id = Str(t, "id"),
                Package = Str(t, "package"),
                Version = Str(t, "version"),
                Assemblies = Strings(t, "assemblies"),
                Namespaces = Strings(t, "namespaces"),
                Requires = Str(t, "requires") is { Length: > 0 } req ? req : null,
                Controls = Entries(t, "controls"),
                Styles = Entries(t, "styles"),
            });
        }
        return list;
    }

    private static Dictionary<string, ThemeControl> Entries(JsonElement theme, string name)
    {
        var map = new Dictionary<string, ThemeControl>(StringComparer.Ordinal);
        if (!theme.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return map;

        foreach (var p in obj.EnumerateObject())
        {
            var v = p.Value;
            double? Num(string n) => v.TryGetProperty(n, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : null;
            string? Text(string n) => v.TryGetProperty(n, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
            InsetMode Mode(string n) => Text(n) switch { "replace" => InsetMode.Replace, "ignore" => InsetMode.Ignore, _ => InsetMode.Add };

            map[p.Name] = new ThemeControl(
                TargetType: Text("targetType") ?? p.Name,
                Chrome: Num("chromeWidth"),
                Padding: Num("padding") ?? 0, Border: Num("border") ?? 0,
                PaddingMode: Mode("paddingMode"), BorderMode: Mode("borderMode"),
                MinWidth: Num("minWidth") ?? 0,
                FontFamily: Text("fontFamily"), FontSize: Num("fontSize"), FontWeight: (int?)Num("fontWeight"),
                Casing: Text("casing") switch { "upper" => TextCasing.Upper, "lower" => TextCasing.Lower, _ => TextCasing.AsIs },
                TextFontFamily: Text("textFontFamily"), TextFontSize: Num("textFontSize"), TextFontWeight: (int?)Num("textFontWeight"))
            {
                FollowsSystem = v.TryGetProperty("followsSystem", out var fs) && fs.ValueKind == JsonValueKind.True,
                LayoutRounding = v.TryGetProperty("layoutRounding", out var lr) && lr.ValueKind == JsonValueKind.True,
            };
        }
        return map;
    }

    /// <summary>OpenType 字重 → XAML 的 FontWeight 名稱。</summary>
    public static string WeightName(int weight) => weight switch
    {
        <= 100 => "Thin",
        <= 200 => "ExtraLight",
        <= 300 => "Light",
        <= 400 => "Normal",
        <= 500 => "Medium",
        <= 600 => "SemiBold",
        <= 700 => "Bold",
        <= 800 => "ExtraBold",
        _ => "Black",
    };

    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
