using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using XamlFit.Core.Fonts;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;

namespace XamlFit.Core.Xaml;

public sealed record ScanOptions
{
    public string DefaultFontFamily { get; init; } = "Segoe UI";
    public double DefaultFontSize { get; init; } = 12;
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];
}

public sealed record ScanResult(
    IReadOnlyList<TextSite> Sites,
    int FilesScanned,
    int ParseErrors,
    IReadOnlyList<string> ParseErrorFiles);

/// <summary>
/// 掃描 XAML，找出所有文字位置並算出它們的可用寬度。
/// </summary>
public sealed partial class XamlScanner
{
    private readonly ChromeTable _chrome;
    private readonly ScanOptions _options;
    private WidthResolver _width = null!;
    private StyleIndex _styles = new();

    private readonly FontResolver? _fonts;

    /// <param name="fonts">給得出字型度量時，Grid 的 Auto 欄就解得開（要量子元素的文字寬度）。
    /// 傳 null 的話那些位置退回 NeedsGrid。</param>
    public XamlScanner(ChromeTable chrome, ScanOptions? options = null, FontResolver? fonts = null)
    {
        _chrome = chrome;
        _options = options ?? new ScanOptions();
        _fonts = fonts;
    }

    public StyleIndex Styles => _styles;

    // 會顯示文字的元素 → 文字放在哪個屬性
    private static readonly Dictionary<string, string[]> TextProperties = new(StringComparer.Ordinal)
    {
        ["TextBlock"] = ["Text"],
        ["Run"] = ["Text"],
        ["Label"] = ["Content"],
        ["Button"] = ["Content"],
        ["ToggleButton"] = ["Content"],
        ["RepeatButton"] = ["Content"],
        ["RadioButton"] = ["Content"],
        ["CheckBox"] = ["Content"],
        ["HyperlinkButton"] = ["Content"],
        ["MenuItem"] = ["Header"],
        ["TabItem"] = ["Header"],
        ["GroupBox"] = ["Header"],
        ["Expander"] = ["Header"],
        ["HeaderedContentControl"] = ["Header"],
        ["GridViewColumn"] = ["Header"],
        ["DataGridTextColumn"] = ["Header"],
        ["DataGridTemplateColumn"] = ["Header"],
        ["DataGridCheckBoxColumn"] = ["Header"],
        ["StatusBarItem"] = ["Content"],
        ["ListBoxItem"] = ["Content"],
        ["ComboBoxItem"] = ["Content"],
        ["TreeViewItem"] = ["Header"],
    };

    public ScanResult Scan(string root)
    {
        var sites = new List<TextSite>();
        var errors = new List<string>();
        var files = 0;

        // ⚠ 兩趟。第一趟先把全專案的樣式收齊 —— 字型大小與版面內距大量寫在樣式裡，
        //   不先收就會用預設值去量（實查：QuillNest 的 SectionHint 設 FontSize=11，
        //   用預設 12 量出一個「已經溢出 2.9px」的假警報）。
        //   重新 parse 一次比把 1900 份 XDocument 抱在記憶體裡便宜。
        _styles = new StyleIndex();
        foreach (var file in EnumerateXaml(root))
        {
            try { _styles.Add(XDocument.Load(file)); }
            catch (Exception) { /* 第二趟會記成 parse error */ }
        }

        var measurer = _fonts is null
            ? null
            : new ElementMeasurer(_fonts, _styles, _chrome, _options.DefaultFontFamily, _options.DefaultFontSize);
        _width = new WidthResolver(_chrome, _styles, measurer);

        foreach (var file in EnumerateXaml(root))
        {
            files++;
            XDocument doc;
            try { doc = XDocument.Load(file, LoadOptions.SetLineInfo); }
            catch (Exception) { errors.Add(Rel(root, file)); continue; }
            if (doc.Root is null) continue;

            var rel = Rel(root, file);
            foreach (var el in doc.Root.DescendantsAndSelf())
            {
                var site = TrySite(el, rel);
                if (site is not null) sites.Add(site);
            }
        }

        return new ScanResult(sites, files, errors.Count, errors);
    }

    private static string Rel(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>預設排除。⚠ 點開頭的目錄一定要排除 ——
    /// .claude/worktrees 裡是同一個專案的副本，不排除會重複計算好幾倍（M0 實查踩到）。</summary>
    public IEnumerable<string> EnumerateXaml(string root)
    {
        foreach (var f in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            var p = f.Replace('\\', '/');
            if (p.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Contains("/.", StringComparison.Ordinal)) continue;
            if (p.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Contains("/packages/", StringComparison.OrdinalIgnoreCase)) continue;
            if (_options.ExcludeGlobs.Any(g => p.Contains(g, StringComparison.OrdinalIgnoreCase))) continue;
            yield return f;
        }
    }

    private TextSite? TrySite(XElement el, string file)
    {
        var local = el.Name.LocalName;
        if (!TextProperties.TryGetValue(local, out var props)) return null;
        if (IsIgnorableNamespace(el.Name.NamespaceName)) return null;

        var (source, text, key) = ExtractText(el, props);
        if (text is null) return null;

        var (family, size, weight, style) = InheritedFont(el);

        return new TextSite
        {
            File = file,
            Line = WidthResolver.LineOf(el),
            Element = local,
            Source = source,
            Text = text,
            ResourceKey = key,
            FontFamily = family,
            FontSize = size,
            FontWeight = weight,
            FontStyle = style,
            Wrap = ParseWrap(Nearest(el, "TextWrapping")),
            Trim = ParseTrim(Nearest(el, "TextTrimming")),
            Height = WidthResolver.TryLength(el.Attribute("Height")?.Value, out var h) ? h : null,
            Width = _width.Resolve(el),
            SuppressedReason = IgnoreReason(el) is { Length: > 0 } r ? r : null,
            // 理由必填：空理由的 ignore 不生效，但要讓使用者知道它沒生效
            InvalidIgnore = IgnoreReason(el) is { Length: 0 },
        };
    }

    private static bool IsIgnorableNamespace(string ns) =>
        ns.Contains("ignore", StringComparison.OrdinalIgnoreCase) ||
        ns.Contains("/blend/", StringComparison.OrdinalIgnoreCase) ||
        ns == "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static (TextSource, string?, string?) ExtractText(XElement el, string[] props)
    {
        foreach (var p in props)
        {
            var v = el.Attribute(p)?.Value;
            if (v is null)
            {
                var pe = el.Element(XName.Get($"{el.Name.LocalName}.{p}", el.Name.NamespaceName));
                if (pe is not null && !pe.HasElements && !string.IsNullOrWhiteSpace(pe.Value))
                    return (TextSource.Literal, pe.Value.Trim(), null);
                continue;
            }

            v = v.Trim();
            if (v.Length == 0) continue;

            // "{}" 是 XAML 的跳脫前綴：{}{0} 其實是字面的 "{0}"
            if (v.StartsWith("{}", StringComparison.Ordinal))
                return (TextSource.Literal, v[2..], null);

            if (v.StartsWith('{'))
            {
                var key = ResourceKeyOf(v);
                return key is not null
                    ? (TextSource.Resource, v, key)
                    : (TextSource.DataBound, v, null);
            }

            return (TextSource.Literal, v, null);
        }

        if (el.Name.LocalName is "TextBlock" or "Run" && !el.HasElements && !string.IsNullOrWhiteSpace(el.Value))
            return (TextSource.Literal, el.Value.Trim(), null);

        return (TextSource.Literal, null, null);
    }

    /// <summary>從標記延伸抽出資源鍵。認得的寫法：
    /// <c>{x:Static p:Resources.Save}</c>、<c>{x:Static local:Strings.Save}</c>、
    /// 以及常見的本地化標記延伸 <c>{loc:Translate Save}</c>／<c>{lex:Loc Save}</c>。</summary>
    internal static string? ResourceKeyOf(string markup)
    {
        var m = StaticResourceStringRegex().Match(markup);
        if (m.Success)
        {
            // path 長這樣：p:Strings.Save、local:Resources.Dialog_Title、Strings.Save
            // 鍵是最後一段。⚠ 第一版的 regex 用 [\w.]*? 去吃前綴，而 \w 不含冒號 ——
            //   於是 {x:Static p:Strings.Save} 整個比對失敗，所有資源字串被誤判成「綁資料」
            //   然後被濾掉。示範專案第一次跑就是 0 個位置。
            var path = m.Groups["path"].Value;
            var dot = path.LastIndexOf('.');
            if (dot >= 0 && dot + 1 < path.Length) return path[(dot + 1)..];
        }

        m = LocExtensionRegex().Match(markup);
        if (m.Success) return m.Groups["key"].Value;

        return null;
    }

    // {x:Static p:Resources.Save} → Save ；{x:Static local:Strings.Dialog_Title} → Dialog_Title
    [GeneratedRegex(@"\{\s*x:Static\s+(?<path>[\w.:]+)\s*\}", RegexOptions.Compiled)]
    private static partial Regex StaticResourceStringRegex();

    // {loc:Translate Save} / {lex:Loc Save} / {i18n:Translate Save}
    [GeneratedRegex(@"\{\s*(?:loc|lex|i18n|tr)\s*:\s*\w+\s+(?<key>[\w.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex LocExtensionRegex();

    /// <summary>字型屬性在 WPF 是會繼承的 —— 往上找最近一個寫了的。</summary>
    private (string? Family, double Size, int Weight, FontStyleKind Style) InheritedFont(XElement el)
    {
        var family = Nearest(el, "FontFamily") ?? _options.DefaultFontFamily;

        var sizeText = Nearest(el, "FontSize");
        var size = sizeText is not null &&
                   double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0
            ? s
            : _options.DefaultFontSize;

        return (family, size, FontResolver.ParseWeight(Nearest(el, "FontWeight")),
                FontResolver.ParseStyle(Nearest(el, "FontStyle")));
    }

    /// <summary>往上找最近一個寫了這個屬性的祖先（含自己）。</summary>
    private string? Nearest(XElement el, string attribute) =>
        _styles.Resolve(el, attribute, inheritable: true);

    private static WrapMode ParseWrap(string? v) => v?.ToLowerInvariant() switch
    {
        "wrap" => WrapMode.Wrap,
        "wrapwithoverflow" => WrapMode.WrapWithOverflow,
        _ => WrapMode.NoWrap,
    };

    private static TrimMode ParseTrim(string? v) => v?.ToLowerInvariant() switch
    {
        "characterellipsis" => TrimMode.CharacterEllipsis,
        "wordellipsis" => TrimMode.WordEllipsis,
        _ => TrimMode.None,
    };

    /// <summary>&lt;!-- xamlfit-ignore: 理由 --&gt; 管它後面的下一個元素。理由必填。</summary>
    internal static string? IgnoreReason(XElement el)
    {
        for (var node = el.PreviousNode; node is not null; node = node.PreviousNode)
        {
            if (node is XComment c)
            {
                var m = IgnoreRegex().Match(c.Value);
                if (!m.Success) return null;
                var reason = m.Groups["reason"].Value.Trim();
                // 理由必填：沒理由的 ignore 視為無效（呼叫端會警告）
                return reason.Length > 0 ? reason : "";
            }
            if (node is XElement) return null;   // 中間隔了別的元素就不算
        }
        return null;
    }

    [GeneratedRegex(@"xamlfit-ignore\s*:?\s*(?<reason>.*)", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex IgnoreRegex();
}
