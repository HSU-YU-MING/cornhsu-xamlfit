using System.Text.Json;
using System.Xml.Linq;

namespace XamlFit.Core.Layout;

/// <summary>一個元素在「吃掉多少水平空間」這件事上屬於哪一類。</summary>
public enum ChromeKind
{
    /// <summary>沒有控制項範本（面板、Border、TextBlock…）：只有它自己明寫的 Padding／BorderThickness。</summary>
    NoTemplate,
    /// <summary>有範本，而且我們知道預設範本吃掉多少。</summary>
    Control,
    /// <summary>不知道。**路上有它，就不能給數字。**</summary>
    Unknown,
}

/// <summary>明寫的 Padding／BorderThickness 對預設範本的內距有什麼影響（LayoutOracle 逐個控制項實測）。</summary>
public enum InsetMode
{
    /// <summary>沒有實測過：保守地把明寫的值加在整份內距上面（會多扣，方向安全）。</summary>
    Add,
    /// <summary>一比一取代預設值：內距 = 表值 − 預設值 + 明寫的值。</summary>
    Replace,
    /// <summary>預設範本根本不用這個屬性（ContentControl 的 Padding、GroupBox 的 BorderThickness）。</summary>
    Ignore,
}

/// <param name="Chrome">預設範本吃掉的寬度，**已經含**預設的 Padding 與 BorderThickness。</param>
/// <param name="DefaultPadding">預設範本下 Padding 的生效值（左右合計）。</param>
/// <param name="DefaultBorder">同上，BorderThickness。</param>
public readonly record struct ChromeInfo(
    ChromeKind Kind, double Chrome, string Why,
    InsetMode PaddingMode = InsetMode.Add, double DefaultPadding = 0,
    InsetMode BorderMode = InsetMode.Add, double DefaultBorder = 0)
{
    /// <summary>明寫了 Padding（左右合計 <paramref name="explicitSum"/>）之後，內距要調整多少。</summary>
    public double PaddingDelta(double explicitSum) => Delta(PaddingMode, explicitSum, DefaultPadding);

    public double BorderDelta(double explicitSum) => Delta(BorderMode, explicitSum, DefaultBorder);

    private static double Delta(InsetMode mode, double explicitSum, double defaultSum) => mode switch
    {
        InsetMode.Replace => explicitSum - defaultSum,
        InsetMode.Ignore => 0,
        _ => explicitSum,
    };

    public static ChromeInfo None => new(ChromeKind.NoTemplate, 0, "");
    public static ChromeInfo NotKnown(string why) => new(ChromeKind.Unknown, 0, why);
}

/// <summary>
/// 各控制項「吃掉多少水平空間」的表（padding + border + 核取方塊 + 下拉按鈕 + …）。
///
/// 資料來源是 <c>data/control-chrome.json</c>，是用 WPF **量**出來的，不是手抄
/// （<c>prototype/ChromeGen</c> 與 <c>prototype/LayoutOracle</c>）。
///
/// ⚠ **這張表只描述 WPF 內建控制項的預設範本（Aero2）。** 全面重讀時發現第一版在三個地方
///   把它用到了不該用的對象上，每一個都是「少扣內距 → 高估可用寬度 → 漏報溢出」：
///
///   1. **第三方控制項用後綴比對套了 WPF 的數字。** <c>ui:Button</c>、<c>mah:MetroWindow</c>
///      的範本跟 WPF 的 Button／Window 沒有關係。現在只有 WPF 命名空間裡的元素才查這張表；
///      其他的一律是 <see cref="ChromeKind.Unknown"/>，除非使用者在設定檔的 <c>chrome</c> 裡宣告。
///   2. **表裡沒有的元素被當成內距 0 的透明容器。** <c>TabControl</c>（實際 6）、
///      任何自訂容器控制項都是。現在表裡沒有就是 Unknown。
///   3. **專案套了外部佈景（MaterialDesign、wpfui…）時仍然用 Aero2 的數字。**
///      那些佈景把標準控制項整個重畫，而且定義在別的組件裡，我們看不到。
///      偵測到外部佈景時，內建控制項的數字不再適用。
/// </summary>
public sealed class ChromeTable
{
    private sealed record Entry(
        double? Chrome, double MinWidth, double? Padding, double? Border, double? StylePadding,
        InsetMode PaddingMode, InsetMode BorderMode);

    private readonly Dictionary<string, Entry> _controls;
    private readonly Dictionary<string, double> _headers;
    private readonly Dictionary<string, double> _overrides;

    public string Theme { get; }
    public string GeneratedOn { get; }

    /// <summary>垂直捲軸的寬度（DIP）。
    /// ⚠ 捲軸出不出現取決於執行期的項目數，靜態不可知 ——
    /// 解析器預設「假設它在」：少算可用寬度只會誤報，多算會**漏報溢出**。</summary>
    public double ScrollBarWidth { get; }

    /// <summary>標準視窗邊框吃掉的寬度（左右合計，DIP）。<c>Window.Width</c> 是**外框**寬。</summary>
    public double WindowFrame { get; }

    /// <summary><c>WindowStyle="ThreeDBorderWindow"</c> 的邊框（比標準多一圈立體邊）。</summary>
    public double WindowFrameThreeD { get; }

    /// <summary>沒有控制項範本的 WPF 元素：面板、裝飾器、文字與行內元素。這是結構知識，不是量測值。</summary>
    private static readonly HashSet<string> NoTemplate = new(StringComparer.Ordinal)
    {
        "Grid", "StackPanel", "VirtualizingStackPanel", "DockPanel", "WrapPanel", "Canvas", "UniformGrid",
        "Border", "Viewbox", "Decorator", "AdornerDecorator",
        "ContentPresenter", "ItemsPresenter", "ScrollContentPresenter", "BulletDecorator",
        "TextBlock", "AccessText", "Run", "Span", "Bold", "Italic", "Underline", "Hyperlink", "InlineUIContainer",
    };

    private static readonly HashSet<string> WpfNamespaces = new(StringComparer.Ordinal)
    {
        "",   // 測試用的片段常常不寫 xmlns
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation",
        "http://schemas.microsoft.com/netfx/2007/xaml/presentation",
        "http://schemas.microsoft.com/netfx/2009/xaml/presentation",
    };

    private ChromeTable(
        Dictionary<string, Entry> controls, Dictionary<string, double> headers, Dictionary<string, double> overrides,
        string theme, string on, double scrollBarWidth, double windowFrame, double windowFrameThreeD)
    {
        _controls = controls;
        _headers = headers;
        _overrides = overrides;
        Theme = theme;
        GeneratedOn = on;
        ScrollBarWidth = scrollBarWidth;
        WindowFrame = windowFrame;
        WindowFrameThreeD = windowFrameThreeD;
    }

    public static ChromeTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var controls = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var headers = new Dictionary<string, double>(StringComparer.Ordinal);

        if (root.TryGetProperty("controls", out var list))
        {
            foreach (var p in list.EnumerateObject())
            {
                double? Num(string name) =>
                    p.Value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

                InsetMode Mode(string name) =>
                    p.Value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() switch { "replace" => InsetMode.Replace, "ignore" => InsetMode.Ignore, _ => InsetMode.Add }
                        : InsetMode.Add;

                controls[p.Name] = new Entry(
                    Num("chromeWidth"), Num("minWidth") ?? 0, Num("padding"), Num("border"), Num("stylePadding"),
                    Mode("paddingMode"), Mode("borderMode"));
            }
        }

        if (root.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Object)
            foreach (var p in hs.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number) headers[p.Name] = p.Value.GetDouble();

        double Number(string name, double fallback) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

        return new ChromeTable(
            controls, headers, new Dictionary<string, double>(StringComparer.Ordinal),
            root.TryGetProperty("theme", out var t) ? t.GetString() ?? "" : "",
            root.TryGetProperty("generatedOn", out var g) ? g.GetString() ?? "" : "",
            Number("scrollBarWidth", 0), Number("windowFrame", 16), Number("windowFrameThreeD", 20));
    }

    /// <summary>內建的表（隨套件走）。</summary>
    public static ChromeTable Builtin()
    {
        var asm = typeof(ChromeTable).Assembly;
        using var s = asm.GetManifestResourceStream("XamlFit.Core.control-chrome.json")
                      ?? throw new InvalidOperationException("embedded control-chrome.json is missing");
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    }

    /// <summary>
    /// 加上使用者在設定檔宣告的內距（<c>"chrome": { "Card": 16, "MetroWindow": 2 }</c>）。
    /// 鍵是元素的區域名稱（不含命名空間前綴）；標題區用 <c>"GroupBox.Header"</c> 這種寫法。
    /// 宣告的值是**全部**的內距（含那個控制項的 Padding），優先於內建表，
    /// 而且**不受外部佈景影響** —— 那是使用者自己負責的知識。
    /// </summary>
    public ChromeTable WithOverrides(IReadOnlyDictionary<string, double> overrides)
    {
        var merged = new Dictionary<string, double>(_overrides, StringComparer.Ordinal);
        foreach (var (k, v) in overrides) merged[k] = v;
        return new ChromeTable(_controls, _headers, merged, Theme, GeneratedOn,
            ScrollBarWidth, WindowFrame, WindowFrameThreeD);
    }

    public static bool IsWpfNamespace(string ns) => WpfNamespaces.Contains(ns);

    public static bool IsWpf(XElement el) => IsWpfNamespace(el.Name.NamespaceName);

    /// <summary>這個元素是不是 WPF 內建的某個型別（名稱與命名空間都要對）。</summary>
    public static bool IsWpf(XElement el, string localName) =>
        el.Name.LocalName == localName && IsWpfNamespace(el.Name.NamespaceName);

    /// <summary>元素「內容區」的內距。</summary>
    /// <param name="externalTheme">這個檔案受外部佈景影響時傳佈景名稱；否則 null。</param>
    public ChromeInfo Lookup(XName name, string? externalTheme = null)
    {
        var local = name.LocalName;

        if (_overrides.TryGetValue(local, out var declared))
            return new ChromeInfo(ChromeKind.Control, declared, "");

        if (!IsWpfNamespace(name.NamespaceName))
            return ChromeInfo.NotKnown("it is not a built-in WPF element");

        if (NoTemplate.Contains(local)) return ChromeInfo.None;

        if (!_controls.TryGetValue(local, out var entry))
            return ChromeInfo.NotKnown("there is no chrome data for it");

        if (entry.Chrome is not { } value)
            return ChromeInfo.NotKnown("its content width does not propagate");

        // 內距 0 的內容宿主（ContentControl、UserControl…）佈景通常不動它們；其他的整個被重畫
        if (externalTheme is not null && value > 0)
            return ChromeInfo.NotKnown($"the external theme {externalTheme} restyles it");

        return new ChromeInfo(
            ChromeKind.Control, value, "",
            entry.Padding is null ? InsetMode.Add : entry.PaddingMode, entry.Padding ?? 0,
            entry.Border is null ? InsetMode.Add : entry.BorderMode, entry.Border ?? 0);
    }

    /// <summary>
    /// 佈景預設樣式給這個控制項的 Padding 與 BorderThickness（左右合計）。
    /// 自訂範本裡的 <c>{TemplateBinding Padding}</c> 在專案沒設定時拿到的就是這個。不知道回 null。
    /// </summary>
    public (double Padding, double Border)? DefaultsOf(XName name) =>
        IsWpfNamespace(name.NamespaceName)
        && _controls.TryGetValue(name.LocalName, out var e) && (e.StylePadding ?? e.Padding) is { } p && e.Border is { } b
            ? (p, b)
            : null;

    /// <summary>元素「標題區」的內距（GroupBox／Expander 的 Header、欄標題）。量不到回 null。</summary>
    public double? HeaderChrome(string key, bool wpf, string? externalTheme = null)
    {
        if (_overrides.TryGetValue(key + ".Header", out var declared)) return declared;
        if (!wpf || externalTheme is not null) return null;
        return _headers.TryGetValue(key, out var v) ? v : null;
    }

    public double MinWidthOf(string elementName) =>
        _controls.TryGetValue(elementName, out var e) ? e.MinWidth : 0;

    public int Count => _controls.Count;
}
