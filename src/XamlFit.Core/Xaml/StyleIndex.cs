using System.Xml.Linq;

namespace XamlFit.Core.Xaml;

/// <summary>
/// 具名樣式與隱含樣式的索引，只為了解析**字型屬性**。
///
/// 為什麼非做不可：實查 QuillNest 時，`Style="{StaticResource SectionHint}"` 裡設了
/// `FontSize="11"`，工具用預設的 12 去量，寬度高估 9%，報出一個
/// 「已經溢出 2.9px」的**假警報**。不解析樣式，所有數字都不可信 ——
/// 而真實專案的字型大小幾乎都寫在樣式裡。
///
/// 範圍界定（刻意比 XamlContrast 的 StyleIndex 小很多，因為只要四個屬性）：
///   - 具名樣式 + BasedOn 鏈
///   - 隱含樣式（沒有 x:Key 的 &lt;Style TargetType="X"&gt;）—— ⚠ 只在**全專案只有一個**
///     符合的隱含樣式時才套用；有多個就不猜，標記為不確定
///   - 不解觸發器（Trigger）：那是狀態相關的，不是穩定值
///   - 不解模板裡的 Setter
/// </summary>
public sealed class StyleIndex
{
    private sealed record StyleRecord(
        string? Key, string? TargetType, string? BasedOn, Dictionary<string, string> Setters);

    private readonly Dictionary<string, StyleRecord> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StyleRecord>> _implicit = new(StringComparer.Ordinal);

    /// <summary>字型相關的屬性 —— 只收這些，其他一律忽略。</summary>
    private static readonly string[] Interesting =
        ["FontFamily", "FontSize", "FontWeight", "FontStyle", "TextWrapping", "TextTrimming",
         "Padding", "Margin", "BorderThickness", "Width", "MaxWidth", "Height"];

    private static readonly XName XKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    public int NamedCount => _byKey.Count;
    public int ImplicitCount => _implicit.Count;

    /// <summary>有多個候選的隱含樣式：這些 TargetType 的字型屬性不猜。</summary>
    public IReadOnlyCollection<string> AmbiguousImplicitTargets =>
        [.. _implicit.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key)];

    public void Add(XDocument doc)
    {
        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
        {
            var key = style.Attribute(XKey)?.Value;
            var target = NormalizeType(style.Attribute("TargetType")?.Value);
            var basedOn = ResourceKeyOf(style.Attribute("BasedOn")?.Value);

            var setters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var setter in style.Elements().Where(e => e.Name.LocalName == "Setter"))
            {
                var prop = setter.Attribute("Property")?.Value;
                var value = setter.Attribute("Value")?.Value;
                if (prop is null || value is null) continue;
                // 「Control.FontSize」這種附加寫法取最後一段
                var dot = prop.LastIndexOf('.');
                if (dot >= 0) prop = prop[(dot + 1)..];
                if (Array.IndexOf(Interesting, prop) < 0) continue;
                setters[prop] = value;
            }

            var record = new StyleRecord(key, target, basedOn, setters);

            if (key is not null) _byKey[key] = record;
            else if (target is not null)
            {
                if (!_implicit.TryGetValue(target, out var list)) _implicit[target] = list = [];
                list.Add(record);
            }
        }
    }

    /// <summary>
    /// 元素身上這個屬性的生效值，依 WPF 的優先序：
    /// 本地屬性 &gt; 具名樣式（含 BasedOn 鏈）&gt; 隱含樣式 &gt; 繼承自祖先。
    /// </summary>
    /// <param name="inheritable">這個屬性會不會從祖先繼承（字型屬性會，Padding／Margin 不會）。</param>
    public string? Resolve(XElement element, string property, bool inheritable)
    {
        for (var cur = element; cur is not null; cur = inheritable ? cur.Parent : null)
        {
            // 1. 本地屬性最優先
            var local = cur.Attribute(property)?.Value;
            if (!string.IsNullOrWhiteSpace(local) && !local.TrimStart().StartsWith('{'))
                return local.Trim();

            // 2. 具名樣式
            var styleKey = ResourceKeyOf(cur.Attribute("Style")?.Value);
            if (styleKey is not null && FromChain(styleKey, property) is { } fromStyle)
                return fromStyle;

            // 3. 隱含樣式（只在唯一時才用）
            var target = cur.Name.LocalName;
            if (_implicit.TryGetValue(target, out var list) && list.Count == 1
                && FromRecord(list[0], property, 0) is { } fromImplicit)
                return fromImplicit;
        }

        return null;
    }

    private string? FromChain(string key, string property) =>
        _byKey.TryGetValue(key, out var r) ? FromRecord(r, property, 0) : null;

    /// <summary>沿 BasedOn 往上找。⚠ 深度上限擋住迴圈 —— BasedOn 寫成環不是語法錯誤。</summary>
    private string? FromRecord(StyleRecord r, string property, int depth)
    {
        if (depth > 16) return null;
        if (r.Setters.TryGetValue(property, out var v) && !v.TrimStart().StartsWith('{'))
            return v.Trim();
        if (r.BasedOn is not null && _byKey.TryGetValue(r.BasedOn, out var parent))
            return FromRecord(parent, property, depth + 1);
        return null;
    }

    /// <summary>{StaticResource K} / {DynamicResource K} 的 K。其他寫法回 null。</summary>
    internal static string? ResourceKeyOf(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup)) return null;
        var t = markup.Trim();
        if (!t.StartsWith('{') || !t.EndsWith('}')) return null;
        var inner = t[1..^1].Trim();
        foreach (var prefix in (ReadOnlySpan<string>)["StaticResource", "DynamicResource", "ThemeResource"])
        {
            if (!inner.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = inner[prefix.Length..].Trim();
            if (rest.Length == 0 || rest.StartsWith('{')) return null;   // {StaticResource {x:Type …}} 之類
            var space = rest.IndexOfAny([' ', ',']);
            return space < 0 ? rest : rest[..space];
        }
        return null;
    }

    /// <summary>TargetType="{x:Type Button}" 與 TargetType="Button" 是同一件事。</summary>
    internal static string? NormalizeType(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        if (t.StartsWith('{'))
        {
            var inner = t[1..^1].Trim();
            if (!inner.StartsWith("x:Type", StringComparison.Ordinal)) return null;
            t = inner["x:Type".Length..].Trim();
        }
        var colon = t.LastIndexOf(':');
        return colon >= 0 ? t[(colon + 1)..] : t;
    }
}
