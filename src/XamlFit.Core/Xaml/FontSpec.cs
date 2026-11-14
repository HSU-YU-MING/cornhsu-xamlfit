using System.Globalization;
using System.Xml.Linq;
using XamlFit.Core.Fonts;

namespace XamlFit.Core.Xaml;

/// <summary>
/// 一個元素生效的字型。<see cref="Unreadable"/> 不是 null 時，其他欄位**不可信**。
///
/// ⚠ 語料實測：`FontFamily` 用資源引用寫的有 292 處，字面只有 147 處。
///   讀不出來就默默換成預設字型去量，等於三分之二的字型家族都量錯 ——
///   所以「讀不出來」要一路帶到位置上，讓它變成 unmeasurable。
/// </summary>
public readonly record struct FontSpec(
    string Family, double Size, int Weight, FontStyleKind Style, string? Unreadable)
{
    /// <summary>元素的 Language（xml:lang，會繼承）。沒寫是 WPF 的預設 en-US —— 跟 Windows 的語言無關。</summary>
    public string Language { get; init; } = FontFallback.DefaultLanguage;

    /// <summary>字型屬性會繼承：本地 → 樣式 → 祖先，都沒有才用預設值。</summary>
    public static FontSpec Read(StyleIndex styles, XElement el, string defaultFamily, double defaultSize,
        string defaultLanguage = FontFallback.DefaultLanguage)
    {
        string? unreadable = null;

        string Get(string property, string fallback)
        {
            var r = styles.ResolveEx(el, property, inheritable: true);
            if (r.IsUnreadable) { unreadable ??= $"{property} is not statically readable ({r.Why})"; return fallback; }
            return r.IsValue ? r.Value! : fallback;
        }

        var family = Get("FontFamily", defaultFamily);
        var sizeText = Get("FontSize", "");
        var weightText = Get("FontWeight", "");
        var styleText = Get("FontStyle", "");
        var stretch = Get("FontStretch", "");
        var formatting = Get("TextOptions.TextFormattingMode", "");

        var size = defaultSize;
        if (sizeText.Length > 0)
        {
            if (TryFontSize(sizeText, out var s)) size = s;
            else unreadable ??= $"FontSize=\"{sizeText}\" is not a size this tool can read";
        }

        // 度量檔裡每個家族只當成正常寬度的字面；壓縮／加寬的字面是另一組前進寬度
        if (stretch.Length > 0 && !WidthLike(stretch, "Normal", "Medium", "5"))
            unreadable ??= $"FontStretch=\"{stretch}\" selects a face this tool does not measure";

        // ⚠ LayoutOracle 實測：Display 模式把每個字的寬度對齊到整數像素，
        //   跟線性加總（Ideal 模式，WPF 的預設）差到 −37% ~ +45%，從字型度量算不出來。
        if (WidthLike(formatting, "Display"))
            unreadable ??= "TextFormattingMode=\"Display\" snaps glyph widths to whole pixels, which font metrics cannot predict";

        if (weightText.Length > 0 && !FontResolver.TryParseWeight(weightText, out _))
            unreadable ??= $"FontWeight=\"{weightText}\" is not a weight this tool can read";

        // xml:lang 與 Language 是同一個屬性，會繼承。App 在程式碼裡改預設值（常見於在地化的 App）時這裡看不到 ——
        // 設定檔的 elementLanguage 處理那種情形。
        var language = defaultLanguage;
        for (var cur = el; cur is not null; cur = cur.Parent)
        {
            var raw = cur.Attribute(System.Xml.Linq.XNamespace.Xml + "lang")?.Value ?? cur.Attribute("Language")?.Value;
            if (raw is null) continue;
            if (raw.TrimStart().StartsWith('{')) unreadable ??= "Language is not statically readable";
            else language = raw.Trim();
            break;
        }

        return new FontSpec(
            family, size, FontResolver.ParseWeight(weightText), FontResolver.ParseStyle(styleText), unreadable)
        {
            Language = language,
        };
    }

    private static bool WidthLike(string v, params string[] options)
    {
        foreach (var o in options)
            if (v.Trim().Equals(o, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>WPF 的 FontSize 可以帶單位：12、12px、9pt、0.5in、1cm。內部一律換成 DIP（px）。</summary>
    internal static bool TryFontSize(string v, out double px)
    {
        px = 0;
        var t = v.Trim();
        var factor = 1.0;

        foreach (var (unit, f) in new (string, double)[] { ("px", 1.0), ("pt", 96.0 / 72.0), ("in", 96.0), ("cm", 96.0 / 2.54) })
        {
            if (!t.EndsWith(unit, StringComparison.OrdinalIgnoreCase)) continue;
            t = t[..^unit.Length].Trim();
            factor = f;
            break;
        }

        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n <= 0)
            return false;

        px = n * factor;
        return true;
    }
}

/// <summary>文字在畫面上實際長什麼樣 —— 量寬度之前要先套的規則。</summary>
public static class TextRules
{
    /// <summary>
    /// 內容字串裡的底線是**快速鍵標記**、不會畫出來的元素（LayoutOracle 實測：
    /// <c>Content="_Save"</c> 的 Button 跟 <c>Content="Save"</c> 一樣寬）。
    /// TextBlock、ListBoxItem、ComboBoxItem、StatusBarItem、ContentControl 則會把底線畫出來。
    /// </summary>
    private static readonly string[] AccessKeyElements =
        ["Button", "Label", "CheckBox", "RadioButton", "GroupBox", "TabItem", "MenuItem", "Expander"];

    public static bool UsesAccessKey(string elementLocalName)
    {
        foreach (var s in AccessKeyElements)
            if (elementLocalName.EndsWith(s, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// 畫面上實際顯示的文字。快速鍵元素：第一個單獨的底線不顯示，連續兩個底線顯示成一個。
    /// </summary>
    public static string Display(string elementLocalName, string text)
    {
        if (!UsesAccessKey(elementLocalName) || !text.Contains('_', StringComparison.Ordinal)) return text;

        var sb = new System.Text.StringBuilder(text.Length);
        var markerUsed = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '_') { sb.Append(text[i]); continue; }

            if (i + 1 < text.Length && text[i + 1] == '_') { sb.Append('_'); i++; continue; }
            if (!markerUsed) { markerUsed = true; continue; }
            sb.Append('_');
        }
        return sb.ToString();
    }

    /// <summary>
    /// XAML 會把元素內容裡的連續空白（含換行與縮排）收成一個空格，頭尾去掉。
    /// 第一版只做 Trim，於是排版成多行的 TextBlock 內容被連同縮排一起量，報出假的溢出。
    /// </summary>
    public static string CollapseWhitespace(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 元素的文字內容，照 XAML 的空白規則處理：預設把連續空白收成一個空格；
    /// 元素自己或**任何一層祖先**宣告了 <c>xml:space="preserve"</c> 時原樣保留。
    ///
    /// ⚠ 實查 ScreenToGif：字串字典在根元素上寫 <c>xml:space="preserve"</c>，字串裡用
    ///   <c>&amp;#x0d;</c> 換行。第一版只看元素自己的屬性，把換行吃掉、兩行當成一長行量 ——
    ///   連帶把旁邊的欄算窄，報出一個假的溢出。
    /// </summary>
    public static string ElementText(System.Xml.Linq.XElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            var space = e.Attribute(System.Xml.Linq.XNamespace.Xml + "space")?.Value;
            if (space == "preserve") return element.Value;
            if (space == "default") break;
        }
        return CollapseWhitespace(element.Value);
    }

    /// <summary>
    /// 看起來是向量路徑資料（<c>M8.59,16.58L13.17,12…</c>）而不是文字。
    /// 圖示常常這樣塞在 Content 或字串資源裡；它有字母，但不是給人讀的，也不是翻譯對象。
    /// </summary>
    public static bool IsPathData(string text)
    {
        var t = text.Trim();
        if (t.Length < 8 || t[0] is not ('M' or 'm' or 'F')) return false;

        var digits = 0;
        foreach (var c in t)
        {
            if (char.IsDigit(c)) { digits++; continue; }
            if (c is ' ' or ',' or '.' or '-' or '+' or 'e' or 'E') continue;
            if ("MmLlHhVvCcSsQqTtAaZzF".Contains(c, StringComparison.Ordinal)) continue;
            return false;
        }
        // 數字要佔大宗（空白不算）：路徑資料是「指令字母 + 一堆座標」
        return digits * 2 >= t.Count(c => c != ' ');
    }

    /// <summary>依明寫的換行切開。多行文字的寬度是**最寬的那一行**，不是整串加起來。</summary>
    public static string[] Lines(string text) =>
        text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal)
            ? text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            : [text];

    /// <summary>
    /// 字串裡有沒有執行期才填值的格式佔位（<c>{0}</c>、<c>{0:N2}</c>、<c>{name}</c>、<c>%s</c>、<c>%1$d</c>）。
    /// 有的話實際顯示的長度靜態不可知 —— 量佔位符本身的寬度沒有意義。
    /// </summary>
    public static bool HasPlaceholder(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }   // {{ 是跳脫
                var close = text.IndexOf('}', i + 1);
                if (close > i + 1 && close - i <= 40 && IsPlaceholderBody(text.AsSpan(i + 1, close - i - 1)))
                    return true;
            }
            else if (c == '%' && i + 1 < text.Length)
            {
                var j = i + 1;
                while (j < text.Length && (char.IsDigit(text[j]) || text[j] == '$')) j++;
                if (j < text.Length && text[j] is 's' or 'd' or 'f' or 'u' or 'i' or 'x' or '@')
                {
                    // "50% discount" 的 %  後面接空白再接字母不算；緊接著才算
                    if (j > i + 1 || !(j + 1 < text.Length && char.IsLetter(text[j + 1]))) return true;
                }
            }
        }
        return false;
    }

    private static bool IsPlaceholderBody(ReadOnlySpan<char> body)
    {
        if (body.Length == 0) return false;
        // {0} {12:N2} {0,-10} {name} {user.name}
        var head = body;
        var cut = body.IndexOfAny(':', ',');
        if (cut >= 0) head = body[..cut];
        if (head.Length == 0) return false;
        foreach (var ch in head)
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or '.')) return false;
        return true;
    }
}

/// <summary>
/// 安全地載入 XML：不處理 DTD、不解析外部實體、不收巢狀太深的檔案。
/// 工具跑在 CI 上、讀的是任意 repo 的檔案 —— 包括陌生人送來的 pull request。
/// </summary>
public static class SafeXml
{
    /// <summary>
    /// 超過這個深度的檔案當成解析錯誤。真實的 XAML 不會超過一百層。
    /// ⚠ 沒有這個上限時，一個巢狀 100,000 層、1.7 MB 的檔案讓 budget 與 check 都跑超過 170 秒沒結束 ——
    ///   CI 上就是一個 PR 讓整條管線卡到逾時。深度在串流時就量，還沒建出整棵樹之前。
    /// </summary>
    public const int MaxDepth = 512;

    public static System.Xml.Linq.XDocument Load(string path, bool lineInfo = false)
    {
        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        using (var probe = System.Xml.XmlReader.Create(path, settings))
            while (probe.Read())
                if (probe.Depth > MaxDepth)
                    throw new System.Xml.XmlException($"elements are nested more than {MaxDepth} deep");

        using var reader = System.Xml.XmlReader.Create(path, settings);
        return System.Xml.Linq.XDocument.Load(
            reader, lineInfo ? System.Xml.Linq.LoadOptions.SetLineInfo : System.Xml.Linq.LoadOptions.None);
    }
}
