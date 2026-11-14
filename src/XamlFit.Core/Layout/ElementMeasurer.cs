using System.Globalization;
using System.Xml.Linq;
using XamlFit.Core.Fonts;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Layout;

/// <summary>
/// 算元素的期望寬度，給 <see cref="GridSolver"/> 解 Auto 欄用。
///
/// 能算的只有三種：
///   1. 明寫了 <c>Width</c> 的元素
///   2. 量得到的文字元素（字面文字 + 有度量的字型）
///   3. 只包一個子元素的容器（Border／ContentControl 之類）—— 遞迴
///
/// 其他一律回 <c>null</c>。⚠ **不要猜。** Auto 欄寬猜錯，星號分配跟著錯，
/// 而且可能高估可用寬度 —— 漏報溢出是最糟的失敗。
/// </summary>
public sealed class ElementMeasurer : IElementMeasurer
{
    private readonly FontResolver _fonts;
    private readonly StyleIndex _styles;
    private readonly ChromeTable _chrome;
    private readonly string _defaultFamily;
    private readonly double _defaultSize;

    public ElementMeasurer(
        FontResolver fonts, StyleIndex styles, ChromeTable chrome,
        string defaultFamily, double defaultSize)
    {
        _fonts = fonts;
        _styles = styles;
        _chrome = chrome;
        _defaultFamily = defaultFamily;
        _defaultSize = defaultSize;
    }

    /// <summary>只包一個子元素、會把子元素寬度原封不動往外傳的容器。</summary>
    private static readonly HashSet<string> PassThrough = new(StringComparer.Ordinal)
    {
        "Border", "ContentControl", "Viewbox", "Decorator", "AdornerDecorator",
    };

    public double? DesiredWidth(XElement element, int depth)
    {
        if (depth > 8) return null;

        var local = element.Name.LocalName;

        // 不參與版面的東西：寬度 0（這是確定的答案）
        if (local is "Grid.ColumnDefinitions" or "Grid.RowDefinitions") return 0;
        if (local.Contains('.', StringComparison.Ordinal)) return 0;

        var (ml, mr) = WidthResolver.ParseThicknessLeftRight(_styles.Resolve(element, "Margin", inheritable: false));
        var margin = ml + mr;

        // 1. 明寫 Width 最乾脆
        if (WidthResolver.TryLength(_styles.Resolve(element, "Width", inheritable: false), out var explicitWidth))
            return explicitWidth + margin;

        // 內部內距（Padding / BorderThickness / 控制項 chrome）
        var inner = InnerInset(element);
        if (inner is null) return null;   // chrome 未知（ComboBox、TextBox）

        // 2. 文字元素
        if (ContentWidthOfText(element) is { } textWidth)
            return textWidth + inner.Value + margin;

        // 3. 只有一個版面子元素的容器 → 遞迴
        if (PassThrough.Contains(local) || local.EndsWith("Border", StringComparison.Ordinal))
        {
            var children = element.Elements()
                .Where(e => !e.Name.LocalName.Contains('.', StringComparison.Ordinal))
                .ToList();
            if (children.Count == 0) return inner.Value + margin;
            if (children.Count == 1 && DesiredWidth(children[0], depth + 1) is { } childWidth)
                return childWidth + inner.Value + margin;
        }

        // 4. 水平 StackPanel：子元素寬度相加
        if (local.EndsWith("StackPanel", StringComparison.Ordinal)
            && string.Equals(_styles.Resolve(element, "Orientation", inheritable: false),
                             "Horizontal", StringComparison.OrdinalIgnoreCase))
        {
            double sum = 0;
            foreach (var c in element.Elements())
            {
                if (c.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;
                if (DesiredWidth(c, depth + 1) is not { } w) return null;
                sum += w;
            }
            return sum + inner.Value + margin;
        }

        return null;
    }

    /// <summary>文字元素的**文字本身**有多寬。不是文字元素、或量不到，回 null。</summary>
    private double? ContentWidthOfText(XElement element)
    {
        var text = LiteralTextOf(element);
        if (text is null) return null;
        if (text.Length == 0) return 0;

        var family = _styles.Resolve(element, "FontFamily", inheritable: true) ?? _defaultFamily;
        var sizeText = _styles.Resolve(element, "FontSize", inheritable: true);
        var size = sizeText is not null
                   && double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0
            ? s : _defaultSize;

        var lookup = _fonts.Resolve(
            family,
            FontResolver.ParseWeight(_styles.Resolve(element, "FontWeight", inheritable: true)),
            FontResolver.ParseStyle(_styles.Resolve(element, "FontStyle", inheritable: true)),
            _defaultFamily);

        if (lookup.Chain.Count == 0) return null;

        var m = TextMeasure.Measure(text, lookup.Chain, size, lookup.DeclaredCount);
        return m.IsReliable ? m.WidthPx : null;
    }

    /// <summary>只有**字面**文字才算得出寬度。綁資料或指向資源的文字，這裡還不知道它的值。</summary>
    private static string? LiteralTextOf(XElement element)
    {
        string[] props = element.Name.LocalName switch
        {
            "TextBlock" or "Run" => ["Text"],
            "Label" or "Button" or "CheckBox" or "RadioButton" or "ToggleButton" or "RepeatButton" => ["Content"],
            "MenuItem" or "TabItem" or "GroupBox" or "Expander" => ["Header"],
            _ => [],
        };

        foreach (var p in props)
        {
            var v = element.Attribute(p)?.Value;
            if (v is null) continue;
            v = v.Trim();
            if (v.StartsWith("{}", StringComparison.Ordinal)) return v[2..];
            if (v.StartsWith('{')) return null;   // 標記延伸：值未知
            return v;
        }

        if (element.Name.LocalName is "TextBlock" or "Run"
            && !element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
            return element.Value.Trim();

        return props.Length > 0 ? null : null;
    }

    /// <summary>Padding + BorderThickness + 控制項 chrome。chrome 未知時回 null。</summary>
    private double? InnerInset(XElement el)
    {
        double sum = 0;
        foreach (var attr in (ReadOnlySpan<string>)["Padding", "BorderThickness"])
        {
            var (l, r) = WidthResolver.ParseThicknessLeftRight(_styles.Resolve(el, attr, inheritable: false));
            sum += l + r;
        }

        var (known, chrome) = _chrome.Lookup(el.Name.LocalName);
        if (known && chrome is null) return null;
        if (known && chrome!.Value > 0) sum += chrome.Value;
        return sum;
    }
}
