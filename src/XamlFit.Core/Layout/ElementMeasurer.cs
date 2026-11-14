using System.Xml.Linq;
using XamlFit.Core.Fonts;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Layout;

/// <summary>
/// 算元素的期望寬度，給 <see cref="GridSolver"/> 解 Auto 欄、給兄弟分寬度的面板用。
///
/// 能算的只有：
///   1. 明寫了 <c>Width</c> 的元素（任何元素 —— Width 是外框，不必知道裡面長什麼樣）
///   2. 量得到的 WPF 文字元素（字面文字 + 讀得出來的字型 + 有度量）
///   3. Border：零或一個子元素 —— 遞迴
///   4. StackPanel —— 水平相加、垂直取最寬
///
/// 其他一律回 <c>null</c>。⚠ **不要猜，而且特別不要猜小。**
/// 兄弟元素的寬度估小了，剩給文字的空間就被估大 —— 高估可用寬度、漏報溢出。
/// 所以凡是「可能讓元素變寬但我們讀不出來」的東西（綁定的 Margin、MinWidth、
/// 被換掉的範本、第三方控制項）都要回 null，不能當成 0。
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

    /// <summary>
    /// 資源鍵 → 現在要用的那個語言的文字。兄弟元素的文字是在地化字串時靠它才量得出寬度。
    /// null 或查不到：那個兄弟的寬度不知道。
    /// </summary>
    public Func<string, string?>? ResourceText { get; set; }

    /// <summary>沒寫 xml:lang 的元素的語言（決定中日韓的字用哪支字型補）。</summary>
    public string DefaultLanguage { get; set; } = FontFallback.DefaultLanguage;

    /// <summary>成功查到在地化字串的次數。解析器用它判斷「這個寬度取決於兄弟的譯文」。</summary>
    public int ResourceLookups { get; private set; }

    /// <summary>想查但查不到的次數：有這種情形，換一個語言的字串表重掃才有意義。</summary>
    public int ResourceMisses { get; private set; }

    public double? DesiredWidth(XElement element, int depth)
    {
        if (depth > 8) return null;

        var local = element.Name.LocalName;

        // 屬性元素不參與版面：寬度 0（這是確定的答案）
        if (local.Contains('.', StringComparison.Ordinal)) return 0;

        // Visibility="Collapsed" 不佔空間。綁定的 Visibility 讀不出來 → 不知道佔不佔
        var visibility = _styles.ResolveEx(element, "Visibility", inheritable: false);
        if (visibility.IsUnreadable) return null;
        if (visibility.IsValue && visibility.Value!.Equals("Collapsed", StringComparison.OrdinalIgnoreCase)) return 0;

        // LayoutTransform 會改變期望尺寸
        if (element.Attribute("LayoutTransform") is not null
            || element.Elements().Any(e => e.Name.LocalName.EndsWith(".LayoutTransform", StringComparison.Ordinal)))
            return null;

        if (Sum(element, "Margin") is not { } margin) return null;

        // MinWidth／MaxWidth 會把期望寬度夾住。
        // ⚠ 對話框按鈕幾乎都寫 MinWidth —— 不看它就會把 80 寬的按鈕算成 20。
        if (Length(element, "MinWidth") is not { } min) return null;
        if (Length(element, "MaxWidth") is not { } max) return null;
        var lower = min.Value ?? 0;
        var upper = max.Value ?? double.PositiveInfinity;

        // ⚠ 垂直捲軸：佈景的預設樣式把 MinWidth 設成系統捲軸寬度。
        //   LayoutOracle 實測：<ScrollBar Width="10"/> 實際是 16.8 寬 —— MinWidth 贏過比較窄的 Width。
        //   專案要做細捲軸得自己把 MinWidth 蓋掉；沒蓋就是系統寬度。
        var isScrollBar = ChromeTable.IsWpf(element, "ScrollBar");
        if (isScrollBar)
        {
            var barOrientation = _styles.ResolveEx(element, "Orientation", inheritable: false);
            if (barOrientation.IsUnreadable) return null;
            if (barOrientation.IsValue && barOrientation.Value!.Equals("Horizontal", StringComparison.OrdinalIgnoreCase)) return null;
            if (min.Value is null) lower = _chrome.ScrollBarWidth;
        }

        // WPF 的規則：MinWidth 贏過 MaxWidth
        double Finish(double content) => Math.Max(lower, Math.Min(content, upper)) + margin;

        // 1. 明寫 Width 最乾脆
        if (Length(element, "Width") is not { } width) return null;
        if (width.Value is { } explicitWidth) return Finish(explicitWidth);

        if (ChromeTable.IsWpf(element))
        {
            // 沒設尺寸的矩形與橢圓不佔空間（捲軸交會處的那塊小方塊就是這樣寫的）
            if (local is "Rectangle" or "Ellipse") return Finish(0);

            // 捲軸沒寫 Width：系統捲軸寬度 —— **換了範本也一樣**。
            // ⚠ LayoutOracle --scrollbar 實測：專案的樣式只換範本時（Playnite），範本確實套上了，
            //   但 WPF 預設樣式把 Width 與 MinWidth 都設成系統捲軸寬（125% 時 16.8），範本根元素寫多寬都沒用。
            //   只有 OverridesDefaultStyle="True"（丟掉預設樣式）時才由範本決定：根元素寫死寬度就用它，否則不知道。
            if (isScrollBar)
            {
                var overrides = _styles.ResolveEx(element, "OverridesDefaultStyle", inheritable: false);
                if (overrides.IsUnreadable) return null;
                if (!(overrides.IsValue && overrides.Value!.Trim().Equals("True", StringComparison.OrdinalIgnoreCase)))
                    return Finish(_chrome.ScrollBarWidth);

                var tpl = _styles.TemplateOf(element);
                if (tpl.Kind != TemplateKind.Custom) return null;
                double widest = 0;
                foreach (var candidate in tpl.Alternatives ?? [tpl.Template])
                {
                    if (candidate is null || FixedRootWidth(candidate) is not { } root) return null;
                    widest = Math.Max(widest, root);
                }
                return Finish(widest);
            }
        }

        // 內部內距（Padding / BorderThickness / 控制項 chrome）。不知道就不算。
        if (InnerInset(element) is not { } inner) return null;

        // 表裡記的預設最小寬度（目前都是 0，留著給使用者宣告的佈景用）
        lower = Math.Max(lower, _chrome.MinWidthOf(local));

        if (!ChromeTable.IsWpf(element)) return null;   // 第三方元素沒有明寫 Width：內容怎麼排不知道

        // 2. 文字元素
        var text = TextOf(element);
        if (text.IsText)
        {
            if (text.Value is null) return null;                       // 有文字但值未知（綁定、資源、子元素）
            if (MeasureText(element, text.Value) is not { } textWidth) return null;
            return Finish(textWidth + inner);
        }

        // 3. Border：零或一個子元素
        if (local == "Border")
        {
            var children = WidthResolver.LayoutChildren(element);
            if (children.Count == 0) return Finish(inner);
            if (children.Count == 1 && DesiredWidth(children[0], depth + 1) is { } childWidth)
                return Finish(childWidth + inner);
            return null;
        }

        // 4. StackPanel：水平相加、垂直取最寬
        if (local == "StackPanel")
        {
            var orientation = _styles.ResolveEx(element, "Orientation", inheritable: false);
            if (orientation.IsUnreadable) return null;

            var horizontal = orientation.IsValue
                             && orientation.Value!.Equals("Horizontal", StringComparison.OrdinalIgnoreCase);

            double total = 0;
            foreach (var c in WidthResolver.LayoutChildren(element))
            {
                if (DesiredWidth(c, depth + 1) is not { } w) return null;
                total = horizontal ? total + w : Math.Max(total, w);
            }
            return Finish(total + inner);
        }

        return null;
    }

    /// <summary>外層 null = 讀不出來；內層 Value null = 沒設定（或 Auto）。</summary>
    private (double? Value, bool Ok)? Length(XElement el, string property)
    {
        var r = _styles.ResolveEx(el, property, inheritable: false);
        if (r.IsUnreadable) return null;
        if (!r.IsValue) return (null, true);
        if (WidthResolver.TryLength(r.Value, out var v)) return (v, true);

        var t = r.Value!.Trim();
        // Auto／NaN／Infinity／0 都是「沒有限制」；其他讀不懂的寫法（帶單位…）不猜
        return t.Equals("Auto", StringComparison.OrdinalIgnoreCase) || t.Equals("NaN", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Infinity", StringComparison.OrdinalIgnoreCase) || t == "0"
            ? (null, true)
            : null;
    }

    private double? Sum(XElement el, string property)
    {
        var r = _styles.ResolveEx(el, property, inheritable: false);
        if (r.IsUnreadable) return null;
        if (!r.IsValue) return 0;
        var (l, rr, ok) = WidthResolver.ParseThickness(r.Value);
        return ok ? l + rr : null;
    }

    /// <summary>ControlTemplate 根元素寫死的寬度（含左右邊距）。不是字面數字、或根元素有樣式時回 null。</summary>
    private static double? FixedRootWidth(XElement template)
    {
        var root = template.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.', StringComparison.Ordinal));
        if (root is null || root.Attribute("Style") is not null) return null;
        if (!WidthResolver.TryLength(root.Attribute("Width")?.Value, out var width)) return null;
        var (l, r, ok) = WidthResolver.ParseThickness(root.Attribute("Margin")?.Value);
        return ok ? width + l + r : null;
    }

    private double? MeasureText(XElement element, string text)
    {
        if (text.Length == 0) return 0;

        var font = FontSpec.Read(_styles, element, _defaultFamily, _defaultSize, DefaultLanguage);
        if (font.Unreadable is not null) return null;

        var lookup = _fonts.Resolve(font.Family, font.Weight, font.Style, _defaultFamily, font.Language);
        if (lookup.Chain.Count == 0) return null;

        double widest = 0;
        foreach (var line in TextRules.Lines(TextRules.Display(element.Name.LocalName, text)))
        {
            var m = TextMeasure.Measure(line, lookup, font.Size);
            if (!m.IsReliable) return null;
            widest = Math.Max(widest, m.WidthPx);
        }
        return widest;
    }

    /// <summary>
    /// 元素是不是文字元素，以及它的字面文字。
    /// IsText 為 true 而 Value 為 null：它顯示內容，但內容不是我們量得出來的字面文字。
    /// </summary>
    private (bool IsText, string? Value) TextOf(XElement element)
    {
        var local = element.Name.LocalName;

        string? prop = local switch
        {
            "TextBlock" or "Run" => "Text",
            "Label" or "Button" or "ToggleButton" or "RepeatButton" or "CheckBox" or "RadioButton" or "ContentControl" => "Content",
            _ => null,
        };

        if (prop is null) return (false, null);

        if (element.Attribute(prop)?.Value is { } v)
        {
            v = v.Trim();
            if (v.StartsWith("{}", StringComparison.Ordinal)) return (true, v[2..]);
            if (v.StartsWith('{'))
            {
                // 在地化字串：這個語言的文字查得到就量得出來
                var key = XamlScanner.ResourceKeyOf(v) ?? StyleIndex.ResourceKeyOf(v);
                if (key is not null && ResourceText is not null)
                {
                    if (ResourceText(key) is { } localized)
                    {
                        ResourceLookups++;
                        return (true, localized);
                    }
                    ResourceMisses++;
                }
                return (true, null);   // 綁定，或查不到的鍵：值未知
            }
            return (true, v);
        }

        if (local is "TextBlock" or "Run")
        {
            if (element.HasElements) return (true, null);   // 內含 Run／Span：行內內容，這裡不解
            return (true, TextRules.ElementText(element));
        }

        // 內容用子元素給的控制項（<Button><StackPanel>…</StackPanel></Button>）：不是單純的文字
        return element.Elements().Any() || !string.IsNullOrWhiteSpace(element.Value)
            ? (true, null)
            : (true, "");
    }

    /// <summary>Padding + BorderThickness + 控制項 chrome。讀不出來或 chrome 未知時回 null。
    /// 交給 <see cref="WidthResolver"/> —— 兩邊對「內距」的算法必須是同一套（查表、或從專案自己的範本算）。</summary>
    private double? InnerInset(XElement el) => _resolver?.ContentInset(el);

    private WidthResolver? _resolver;

    /// <summary>由 <see cref="WidthResolver"/> 的建構式呼叫。兩者互相需要：解析器要量兄弟元素，量測器要問內距。</summary>
    internal void Attach(WidthResolver resolver) => _resolver = resolver;
}
