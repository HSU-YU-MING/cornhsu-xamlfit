using System.Globalization;
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

    /// <summary>
    /// 使用者宣告的「這種控制項最寬多寬」（設定檔的 <c>controlWidths</c>）：寬度要到執行期才知道的控制項
    /// （數值框、程式碼填項目的下拉選單）。工具自己從不猜這個數字。
    /// </summary>
    public IReadOnlyDictionary<string, double> DeclaredWidths { get; set; } = new Dictionary<string, double>();

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

        // Visibility="Collapsed" 不佔空間。
        // 綁定的 Visibility（Playnite 編輯視窗的「儲存這一欄」勾選框）：當成看得見來量 —— 這個寬度一律是被**扣掉**的
        //（Auto 欄、同一排的兄弟、標籤列），藏起來時佔 0、看得見時佔全寬，取大的那個只會少給文字空間。
        var visibility = _styles.ResolveEx(element, "Visibility", inheritable: false);
        if (visibility.IsValue && visibility.Value!.Equals("Collapsed", StringComparison.OrdinalIgnoreCase)) return 0;

        // LayoutTransform 會改變期望尺寸（全是單位變換的不算：Playnite 的標籤列就掛了一組 1 倍、0 度的）
        if (!IdentityLayoutTransform(element)) return null;

        if (Sum(element, "Margin") is not { } margin) return null;

        // MinWidth／MaxWidth 會把期望寬度夾住。
        // ⚠ 對話框按鈕幾乎都寫 MinWidth —— 不看它就會把 80 寬的按鈕算成 20。
        if (Length(element, "MinWidth") is not { } min) return null;
        if (Length(element, "MaxWidth") is not { } max) return null;
        var lower = min.Value ?? 0;
        var upper = max.Value ?? double.PositiveInfinity;

        // 量過的佈景的樣式設的 MinWidth（MaterialDesign 的 TabItem 90）。本地或專案樣式設了就用那個
        if (min.Value is null && ChromeTable.IsWpf(element)
            && _styles.TemplateOf(element) is { Kind: TemplateKind.Theme, Theme: { } theme } && theme.TargetType == local)
            lower = theme.MinWidth;

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

        // 1b. 使用者宣告的最大寬度（MinWidth／MaxWidth 照樣夾住）
        if (DeclaredWidths.TryGetValue(local, out var declared)) return Finish(declared);

        if (ChromeTable.IsWpf(element))
        {
            // 沒設尺寸的矩形與橢圓不佔空間（捲軸交會處的那塊小方塊就是這樣寫的）
            if (local is "Rectangle" or "Ellipse") return Finish(0);

            // Canvas 不量子元素的寬度：沒寫 Width 就是 0，裡面放多寬的東西都一樣（LayoutOracle 實測；Playnite 的 GridEx 拿它當空格）
            if (local == "Canvas") return Finish(0);

            // 捲軸沒寫 Width：系統捲軸寬度 —— **換了範本也一樣**。
            // ⚠ LayoutOracle --scrollbar 實測：專案的樣式只換範本時（Playnite），範本確實套上了，
            //   但 WPF 預設樣式把 Width 與 MinWidth 都設成系統捲軸寬（125% 時 16.8），範本根元素寫多寬都沒用。
            //   只有 OverridesDefaultStyle="True"（丟掉預設樣式）時才由範本決定：根元素寫死寬度就用它，否則不知道。
            if (isScrollBar)
            {
                // 外部佈景的範圍裡：捲軸由佈景的樣式畫（MahApps 14、MaterialDesign 跟系統）。量過就用量的，沒量過不知道
                if (element.Document?.Annotation<ExternalTheme>() is { } scrollTheme)
                {
                    var barTemplate = _styles.TemplateOf(element);
                    if (barTemplate.Kind == TemplateKind.Theme && barTemplate.Theme is { TargetType: "ScrollBar", Chrome: { } barWidth } t)
                        return Finish(t.FollowsSystem ? Math.Max(barWidth, _chrome.ScrollBarWidth) : barWidth);
                    if (barTemplate.Kind != TemplateKind.Custom || scrollTheme.Profile is null) return null;
                }

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

        // 標籤列（TabControl 範本裡 IsItemsHost 的 TabPanel，標籤列在左右兩側時）：最寬的那個標籤
        if (ChromeTable.IsWpf(element, "TabPanel")
            && string.Equals(element.Attribute("IsItemsHost")?.Value.Trim(), "True", StringComparison.OrdinalIgnoreCase))
            return SideTabStrip(element, depth) is { } strip ? Finish(strip) : null;

        // 一個標籤：標題文字 + 標題區的內距（從它的範本算）
        if (ChromeTable.IsWpf(element, "TabItem"))
        {
            if (TabHeaderText(element) is not { } header) return null;
            if (_resolver?.ItemHeaderInset(element) is not { } headerInset) return null;
            if (MeasureText(element, header) is not { } headerWidth) return null;
            return Finish(headerWidth + headerInset);
        }

        // 內部內距（Padding / BorderThickness / 控制項 chrome）。不知道就不算。
        if (InnerInset(element) is not { } inner) return null;

        // 表裡記的預設最小寬度（目前都是 0，留著給使用者宣告的佈景用）
        lower = Math.Max(lower, _chrome.MinWidthOf(local));

        if (!ChromeTable.IsWpf(element)) return null;   // 第三方元素沒有明寫 Width：內容怎麼排不知道

        // 2a. TextBlock 裡是一行 Run（外面可以包 Hyperlink／Span／Underline —— 不改字型的那幾種）：
        //     每個 Run 用自己的字型量，元素之間的空白是一個 TextBlock 字型的空格
        //    （Playnite 的關於視窗：連結放在 Auto 欄；Kindling：勾選框的「動態補間（實驗性）」兩個字級）
        if (local == "TextBlock" && element.HasElements)
        {
            // 2b. 文字由自己的內嵌樣式給（Kindling 的「進階 ▾／進階 ▴」：Setter 加 DataTrigger 切換）：每一種都量，取最寬
            if (StyleTexts(element) is { } texts)
            {
                double widest = 0;
                foreach (var t in texts)
                {
                    if (MeasureText(element, t) is not { } w) return null;
                    widest = Math.Max(widest, w);
                }
                return Finish(widest + inner);
            }
            return InlineLine(element, depth) is { } line ? Finish(line + inner) : null;
        }

        // 2. 文字元素
        var text = TextOf(element);
        if (text.IsText)
        {
            // 內容是一個元素（<CheckBox><TextBlock>…</TextBlock></CheckBox>）：那個元素的寬度 + 內距
            if (text.Value is null && ContentElement(element) is { } contentElement)
                return DesiredWidth(contentElement, depth + 1) is { } contentWidth ? Finish(contentWidth + inner) : null;

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

    private static bool IsTextElement(string local) =>
        local is "TextBlock" or "Run" or "AccessText" or "Span" or "Bold" or "Italic" or "Underline" or "Hyperlink";

    private double? MeasureText(XElement element, string text)
    {
        if (text.Length == 0) return 0;

        var font = FontSpec.Read(_styles, element, _defaultFamily, _defaultSize, DefaultLanguage);
        if (font.Unreadable is not null) return null;

        var lookup = _fonts.Resolve(font.Family, font.Weight, font.Style, _defaultFamily, font.Language);
        if (lookup.Chain.Count == 0) return null;

        double widest = 0;
        var casing = IsTextElement(element.Name.LocalName) ? TextCasing.AsIs : _styles.CasingOf(element);
        foreach (var line in TextRules.Lines(TextRules.Cased(TextRules.Display(element.Name.LocalName, text), casing)))
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

        if (element.Attribute(prop)?.Value is { } v) return (true, AttributeText(v));

        if (local is "TextBlock" or "Run")
        {
            if (element.HasElements) return (true, null);   // 內含 Run／Span：行內內容，這裡不解
            return (true, TextRules.ElementText(element));
        }

        // 內容用子元素給的控制項（<Button><StackPanel>…</StackPanel></Button>）：不是單純的文字。
        // 屬性元素（<CheckBox.Visibility>、<Button.ContextMenu>）不是內容 —— <X.Content> 才是
        var content = element.Elements().Where(e =>
            !e.Name.LocalName.Contains('.', StringComparison.Ordinal) || e.Name.LocalName.EndsWith(".Content", StringComparison.Ordinal));
        var textContent = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value));
        return content.Any() || !string.IsNullOrWhiteSpace(textContent)
            ? (true, null)
            : (true, "");
    }

    /// <summary>
    /// TextBlock 的行內內容排成一行的寬度：每個 Run 用自己的字型，元素之間的空白算一個（TextBlock 字型的）空格，
    /// 頭尾的空白不算（XAML 會修掉）。Run 可以包一層只有它的 Hyperlink／Span／Underline。
    /// 字面文字、LineBreak、Bold／Italic（改字型）、InlineUIContainer、包裝裡不只一個 Run：null。
    /// </summary>
    private double? InlineLine(XElement textBlock, int depth)
    {
        double total = 0;
        var any = false;
        var pendingSpace = false;
        double? space = null;

        foreach (var node in textBlock.Nodes())
        {
            if (node is XComment or XProcessingInstruction) continue;
            if (node is XText t)
            {
                if (!string.IsNullOrWhiteSpace(t.Value)) return null;
                pendingSpace = true;
                continue;
            }
            if (node is not XElement e) continue;
            if (e.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;   // 屬性元素

            if (InlineRun(e) is not { } run) return null;
            if (DesiredWidth(run, depth + 1) is not { } w) return null;

            if (pendingSpace && any)
            {
                space ??= SpaceWidth(textBlock);
                if (space is not { } sp) return null;
                total += sp;
            }
            total += w;
            any = true;
            pendingSpace = false;
        }
        return any ? total : null;
    }

    /// <summary>元素字型的一個空格寬（跟 XamlScanner 量 Run 之間的空白同一個算法）。</summary>
    private double? SpaceWidth(XElement element)
    {
        var font = FontSpec.Read(_styles, element, _defaultFamily, _defaultSize, DefaultLanguage);
        if (font.Unreadable is not null) return null;
        var lookup = _fonts.Resolve(font.Family, font.Weight, font.Style, _defaultFamily, font.Language);
        if (lookup.Chain.Count == 0) return null;
        var m = TextMeasure.Measure(" ", lookup, font.Size);
        return m.IsReliable ? m.WidthPx : null;
    }

    /// <summary>
    /// TextBlock 沒有 Text 屬性、也沒有行內內容，文字由它自己的 <c>&lt;TextBlock.Style&gt;</c> 設定：
    /// 樣式（含觸發器）裡每一個設 Text 的值。樣式有 BasedOn、值不是字面文字、或還有別的內容：null。
    /// 樣式本身沒設 Text（只有觸發器設）時，沒觸發的狀態是空字串。
    /// </summary>
    private List<string>? StyleTexts(XElement textBlock)
    {
        if (textBlock.Attribute("Text") is not null || textBlock.Attribute("Style") is not null) return null;
        XElement? style = null;
        foreach (var node in textBlock.Nodes())
        {
            if (node is XComment or XProcessingInstruction) continue;
            if (node is XText t) { if (!string.IsNullOrWhiteSpace(t.Value)) return null; continue; }
            if (node is not XElement e) continue;
            if (e.Name.LocalName == "TextBlock.Style") { style = e.Elements().SingleOrDefault(); continue; }
            if (!e.Name.LocalName.Contains('.', StringComparison.Ordinal)) return null;   // 行內內容
        }
        if (style is null || style.Name.LocalName != "Style" || style.Attribute("BasedOn") is not null) return null;

        var texts = new List<string>();
        var baseSetsText = false;
        foreach (var setter in style.Descendants().Where(d => d.Name.LocalName == "Setter" && (string?)d.Attribute("Property") == "Text"))
        {
            if (setter.Attribute("Value")?.Value is not { } value) return null;
            if (AttributeText(value) is not { } text) return null;
            texts.Add(text);
            if (setter.Parent == style) baseSetsText = true;
        }
        if (texts.Count == 0) return null;
        if (!baseSetsText) texts.Add("");
        return texts;
    }

    /// <summary>行內元素裡的那個 Run：Run 本身，或一層只裝著一個 Run 的 Hyperlink／Span／Underline。</summary>
    private static XElement? InlineRun(XElement e)
    {
        if (!ChromeTable.IsWpf(e)) return null;
        var name = e.Name.LocalName;
        if (name == "Run") return e.HasElements ? null : e;
        if (name is not ("Hyperlink" or "Span" or "Underline")) return null;

        XElement? only = null;
        foreach (var node in e.Nodes())
        {
            if (node is XComment or XProcessingInstruction) continue;
            if (node is XText t) { if (!string.IsNullOrWhiteSpace(t.Value)) return null; continue; }
            if (node is not XElement c || c.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;
            if (only is not null) return null;
            only = c;
        }
        return only is { } inner && inner.Name.LocalName == "Run" && ChromeTable.IsWpf(inner) && !inner.HasElements ? inner : null;
    }

    /// <summary>
    /// 內容控制項（Button、CheckBox、Label…）的內容如果是**一個**元素、而且沒有內容範本：那個元素。其他情形 null。
    /// </summary>
    private XElement? ContentElement(XElement control)
    {
        if (control.Name.LocalName is not ("Button" or "ToggleButton" or "RepeatButton" or "CheckBox" or "RadioButton" or "Label" or "ContentControl"))
            return null;
        foreach (var property in (ReadOnlySpan<string>)["ContentTemplate", "ContentTemplateSelector", "ContentStringFormat"])
        {
            var r = _styles.ResolveEx(control, property, inheritable: false);
            if (r.IsUnreadable || r.IsValue) return null;
        }
        if (control.Attribute("Content") is not null) return null;

        XElement? only = null;
        foreach (var node in control.Nodes())
        {
            if (node is XComment or XProcessingInstruction) continue;
            if (node is XText t) { if (!string.IsNullOrWhiteSpace(t.Value)) return null; continue; }
            if (node is not XElement e) continue;
            var name = e.Name.LocalName;
            if (name.EndsWith(".Content", StringComparison.Ordinal))
            {
                var inside = e.Elements().ToList();
                if (inside.Count != 1 || only is not null) return null;
                only = inside[0];
                continue;
            }
            if (name.Contains('.', StringComparison.Ordinal)) continue;
            if (only is not null) return null;
            only = e;
        }
        return only;
    }

    /// <summary>屬性寫的文字：字面值、或查得到這個語言譯文的資源。綁定與查不到的鍵回 null。</summary>
    private string? AttributeText(string v)
    {
        v = v.Trim();
        if (v.StartsWith("{}", StringComparison.Ordinal)) return v[2..];
        if (!v.StartsWith('{')) return v;

        // 在地化字串：這個語言的文字查得到就量得出來
        var key = XamlScanner.ResourceKeyOf(v) ?? StyleIndex.ResourceKeyOf(v);
        if (key is not null && ResourceText is not null)
        {
            if (ResourceText(key) is { } localized)
            {
                ResourceLookups++;
                return localized;
            }
            ResourceMisses++;
        }
        return null;   // 綁定，或查不到的鍵：值未知
    }

    /// <summary>TabItem 標題的文字。用範本或子元素畫的標題、讀不出來的值回 null。</summary>
    private string? TabHeaderText(XElement item)
    {
        var template = _styles.ResolveEx(item, "HeaderTemplate", inheritable: false);
        if (template.IsUnreadable || template.IsValue) return null;
        if (item.Elements().Any(e => e.Name.LocalName.EndsWith(".Header", StringComparison.Ordinal))) return null;

        if (item.Attribute("Header")?.Value is { } local) return AttributeText(local);
        var r = _styles.ResolveEx(item, "Header", inheritable: false);
        if (r.IsUnreadable) return null;
        return r.IsValue ? AttributeText(r.Value!) : "";
    }

    /// <summary>
    /// 標籤列在左右兩側時，TabControl 範本裡的 TabPanel 有多寬：一個標籤一列，取最寬的。
    /// 只在算範本內距的複本裡才會遇到（根元素帶著回到控制項的 <see cref="StyleIndex.LexicalContinuation"/>）。
    /// 項目來自 ItemsSource、或有項目樣式／範本：不知道。
    /// </summary>
    private double? SideTabStrip(XElement panel, int depth)
    {
        if (panel.AncestorsAndSelf().Last().Annotation<StyleIndex.LexicalContinuation>()?.From is not { } control
            || !control.Name.LocalName.EndsWith("TabControl", StringComparison.Ordinal))
            return null;

        // 上下的標籤列會換行，寬度不是這樣算
        var placement = _styles.ResolveEx(control, "TabStripPlacement", inheritable: false);
        if (!placement.IsValue || !WidthResolver.IsOneOf(placement.Value, "Left", "Right")) return null;

        return SideTabItems(control, depth);
    }

    /// <summary>
    /// 側邊標籤列裡最寬的標籤（**不含它的 Margin**：TabPanel 排左右兩側時用 GetDesiredSizeWithoutMargin，
    /// 選取的標籤靠負邊距放大也不會撐寬標籤列 —— LayoutOracle「default tabs」「side tabs」實測）。
    /// 項目來自 ItemsSource、或有項目樣式／範本：不知道。
    /// </summary>
    internal double? SideTabItems(XElement control, int depth)
    {
        foreach (var property in (ReadOnlySpan<string>)["ItemsSource", "ItemContainerStyle", "ItemContainerStyleSelector", "ItemTemplate", "ItemTemplateSelector"])
        {
            var r = _styles.ResolveEx(control, property, inheritable: false);
            if (r.IsUnreadable || r.IsValue) return null;
        }

        double widest = 0;
        foreach (var child in control.Elements())
        {
            var name = child.Name.LocalName;
            if (name.Contains('.', StringComparison.Ordinal))
            {
                if (name.EndsWith(".Items", StringComparison.Ordinal)) return null;
                continue;
            }
            if (!ChromeTable.IsWpf(child, "TabItem")) return null;
            if (DesiredWidth(child, depth + 1) is not { } w) return null;
            if (Sum(child, "Margin") is not { } margin) return null;
            widest = Math.Max(widest, w - margin);
        }
        return widest;
    }

    /// <summary>沒有 LayoutTransform、或全是單位變換（1 倍、0 度；平移在版面變換裡不算數）。</summary>
    private static bool IdentityLayoutTransform(XElement element)
    {
        var property = element.Elements().Where(e => e.Name.LocalName.EndsWith(".LayoutTransform", StringComparison.Ordinal)).ToList();
        if (element.Attribute("LayoutTransform") is not null) return false;
        if (property.Count == 0) return true;
        if (property.Count != 1) return false;
        var transforms = property[0].Elements().ToList();
        return transforms.Count == 1 && IdentityTransform(transforms[0]);
    }

    private static bool IdentityTransform(XElement t)
    {
        bool Is(string attribute, double expected) =>
            t.Attribute(attribute) is not { } a
            || (double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v == expected);

        return t.Name.LocalName switch
        {
            "TransformGroup" => t.Elements().All(IdentityTransform),
            "ScaleTransform" => !t.HasElements && Is("ScaleX", 1) && Is("ScaleY", 1),
            "SkewTransform" => !t.HasElements && Is("AngleX", 0) && Is("AngleY", 0),
            "RotateTransform" => !t.HasElements && Is("Angle", 0),
            "TranslateTransform" => true,
            _ => false,
        };
    }

    /// <summary>Padding + BorderThickness + 控制項 chrome。讀不出來或 chrome 未知時回 null。
    /// 交給 <see cref="WidthResolver"/> —— 兩邊對「內距」的算法必須是同一套（查表、或從專案自己的範本算）。</summary>
    private double? InnerInset(XElement el) => _resolver?.ContentInset(el);

    private WidthResolver? _resolver;

    /// <summary>由 <see cref="WidthResolver"/> 的建構式呼叫。兩者互相需要：解析器要量兄弟元素，量測器要問內距。</summary>
    internal void Attach(WidthResolver resolver) => _resolver = resolver;
}
