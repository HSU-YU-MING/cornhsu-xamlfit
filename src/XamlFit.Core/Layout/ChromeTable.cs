using System.Text.Json;

namespace XamlFit.Core.Layout;

/// <summary>
/// 各控制項「吃掉多少水平空間」的表（padding + border + 核取方塊 + 下拉按鈕 + …）。
///
/// 資料來源是 <c>data/control-chrome.json</c>，由 <c>prototype/ChromeGen</c> **量**出來的，
/// 不是手抄：把已知寬度的內容塞進控制項，讓 WPF 量一次，差額就是 chrome。
///
/// ⚠ <c>chromeWidth</c> 是 null 代表「量不到」（ComboBox、TextBox：內容寬度不往外傳）。
///   **絕對不要退回「只算 padding + border」** —— 那個數字漏掉範本裡的裝飾，
///   會讓 chrome 偏小、可用寬度偏大，於是**漏報溢出**。不完整的數字比沒有數字危險。
/// </summary>
public sealed class ChromeTable
{
    private readonly Dictionary<string, double?> _chrome;
    private readonly Dictionary<string, double> _minWidth;

    public string Theme { get; }
    public string GeneratedOn { get; }

    /// <summary>垂直捲軸的寬度（DIP）。
    /// ⚠ 捲軸出不出現取決於執行期的項目數，靜態不可知 ——
    /// 解析器預設「假設它在」：少算可用寬度只會誤報，多算會**漏報溢出**。</summary>
    public double ScrollBarWidth { get; }

    private ChromeTable(
        Dictionary<string, double?> chrome, Dictionary<string, double> minWidth,
        string theme, string on, double scrollBarWidth)
    {
        _chrome = chrome;
        _minWidth = minWidth;
        Theme = theme;
        GeneratedOn = on;
        ScrollBarWidth = scrollBarWidth;
    }

    public static ChromeTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var chrome = new Dictionary<string, double?>(StringComparer.Ordinal);
        var min = new Dictionary<string, double>(StringComparer.Ordinal);

        if (root.TryGetProperty("controls", out var controls))
        {
            foreach (var p in controls.EnumerateObject())
            {
                double? c = null;
                if (p.Value.TryGetProperty("chromeWidth", out var cw) && cw.ValueKind == JsonValueKind.Number)
                    c = cw.GetDouble();
                chrome[p.Name] = c;

                if (p.Value.TryGetProperty("minWidth", out var mw) && mw.ValueKind == JsonValueKind.Number)
                    min[p.Name] = mw.GetDouble();
            }
        }

        return new ChromeTable(
            chrome, min,
            root.TryGetProperty("theme", out var t) ? t.GetString() ?? "" : "",
            root.TryGetProperty("generatedOn", out var g) ? g.GetString() ?? "" : "",
            root.TryGetProperty("scrollBarWidth", out var sb) && sb.ValueKind == JsonValueKind.Number
                ? sb.GetDouble() : 0);
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

    /// <summary>這個控制項名稱有沒有 chrome 資料。
    /// 回傳 (有這個控制項, chrome 寬度或 null)。</summary>
    public (bool Known, double? Chrome) Lookup(string elementName)
    {
        // 後綴比對：MahApps 的 MetroTabControl 要對得上 TabControl。
        // 兩邊都中時取最長的（名字愈長愈具體）。抄自 XamlContrast 的實查結論。
        if (_chrome.TryGetValue(elementName, out var exact)) return (true, exact);

        string? best = null;
        foreach (var k in _chrome.Keys)
            if (elementName.Length > k.Length
                && elementName.EndsWith(k, StringComparison.Ordinal)
                && (best is null || k.Length > best.Length))
                best = k;

        return best is null ? (false, null) : (true, _chrome[best]);
    }

    public double MinWidthOf(string elementName) => _minWidth.GetValueOrDefault(elementName, 0);

    public int Count => _chrome.Count;
}
