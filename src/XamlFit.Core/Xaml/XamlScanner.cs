using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlFit.Core.Analysis;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;

namespace XamlFit.Core.Xaml;

public sealed record ScanOptions
{
    public string DefaultFontFamily { get; init; } = "Segoe UI";
    public double DefaultFontSize { get; init; } = 12;
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];

    /// <summary>true：專案套了外部佈景也照樣用內建的 Aero2 內距表（使用者自己確認過佈景沒動內距）。</summary>
    public bool TrustDefaultChrome { get; init; }

    /// <summary>
    /// 「把 A 當成 WPF 的 B」：<c>{ "GridEx": "Grid", "MetroWindow": "Window" }</c>。
    /// 給只加了行為、沒改版面與範本的子類別用 —— 使用者自己擔保它跟基底型別排起來一樣。
    /// 鍵是元素的區域名稱。
    /// </summary>
    public IReadOnlyDictionary<string, string> TreatAs { get; init; } = new Dictionary<string, string>();

    /// <summary>使用者明確指定了預設字型（設定檔或旗標）。視窗之間對字型的設定不一致時，
    /// 以使用者指定的為準，不再把 UserControl 裡的文字判成「不知道繼承到什麼」。</summary>
    public bool DefaultFontPinned { get; init; }
}

/// <summary>一個不是 WPF 的 XAML 檔（WinUI／UWP、Avalonia、MAUI）。不分析，但要講出來。</summary>
public sealed record ForeignFile(string File, string Dialect);

/// <summary>偵測到的外部佈景，以及它影響哪些檔案。</summary>
public sealed record ThemeUse(string Theme, string DeclaredIn, string Scope);

public sealed record ScanResult(
    IReadOnlyList<TextSite> Sites,
    int FilesScanned,
    int ParseErrors,
    IReadOnlyList<string> ParseErrorFiles)
{
    /// <summary>不是 WPF 的 XAML 檔。內距表、預設字型、版面語意都是 WPF 的，套上去只會得到錯的數字。</summary>
    public IReadOnlyList<ForeignFile> ForeignFiles { get; init; } = [];

    /// <summary>專案套用的外部佈景。受影響的檔案裡，標準控制項的內距不再適用內建表。</summary>
    public IReadOnlyList<ThemeUse> ExternalThemes { get; init; } = [];

    /// <summary>XAML 用到、但度量檔裡沒有的字型家族。</summary>
    public IReadOnlyList<string> MissingFonts { get; init; } = [];

    /// <summary>從 XAML 字串資源字典（&lt;sys:String x:Key="…"&gt;）讀到的字串表，一個文化一份。</summary>
    public IReadOnlyList<CultureStrings> Strings { get; init; } = [];

    /// <summary>
    /// 量兄弟元素時，有幾次用到（或想用卻查不到）在地化字串。
    /// 大於 0 代表版面寬度跟著語言變：check 要用每個語言自己的字串表重掃一次才準。
    /// </summary>
    public int NeighbourTextUses { get; init; }
}

/// <summary>
/// 掃描 XAML，找出所有文字位置並算出它們的可用寬度。
/// </summary>
public sealed partial class XamlScanner
{
    private readonly ChromeTable _chrome;
    private readonly ScanOptions _options;
    private readonly FontResolver? _fonts;

    private WidthResolver _width = null!;
    private StyleIndex _styles = new();
    private string _defaultFont = "Segoe UI";

    // 第一趟的結果。換預設字型或換語言重掃時不必重做。
    private List<string> _files = [];
    private List<string> _rels = [];
    private string?[] _dialects = [];
    private List<ThemeUse> _themes = [];
    private List<CultureStrings> _strings = [];
    private Dictionary<string, string> _sourceStrings = new(StringComparer.Ordinal);
    private HashSet<string> _stringKeys = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _missingFonts = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="fonts">給得出字型度量時，Grid 的 Auto 欄就解得開（要量子元素的文字寬度），
    /// 沒有度量的字型也會被標成量不到。傳 null 的話那些位置退回 NeedsGrid。</param>
    public XamlScanner(ChromeTable chrome, ScanOptions? options = null, FontResolver? fonts = null)
    {
        _chrome = chrome;
        _options = options ?? new ScanOptions();
        _fonts = fonts;
    }

    public StyleIndex Styles => _styles;

    /// <summary>
    /// 會顯示文字的元素 → 文字放在哪個屬性。用**後綴**比對，長的優先：
    /// 第三方控制項庫幾乎都沿用 WPF 的命名（<c>mah:MetroTabItem</c>、<c>ui:Button</c>）。
    /// 第一版用精確名稱，第三方控制項上的文字整批沒被看到 —— 那不是「量不到」，是「不知道它存在」。
    /// </summary>
    private static readonly (string Suffix, string Property)[] TextElements =
    [
        ("HeaderedContentControl", "Header"),
        ("GridViewColumn", "Header"),
        ("StatusBarItem", "Content"),
        ("ComboBoxItem", "Content"),
        ("ListBoxItem", "Content"),
        ("TreeViewItem", "Header"),
        ("AccessText", "Text"),
        ("TextBlock", "Text"),
        ("CheckBox", "Content"),
        ("GroupBox", "Header"),
        ("Expander", "Header"),
        ("MenuItem", "Header"),
        ("TabItem", "Header"),
        ("Button", "Content"),
        ("Label", "Content"),
    ];

    internal static string? TextPropertyOf(string local)
    {
        if (local.Contains('.', StringComparison.Ordinal)) return null;
        if (local == "Run") return "Text";
        if (WidthResolver.ColumnHeaderKey(local) is not null) return "Header";

        foreach (var (suffix, property) in TextElements)
            if (local.EndsWith(suffix, StringComparison.Ordinal)) return property;
        return null;
    }

    public ScanResult Scan(string root)
    {
        Prepare(root);
        return Rescan(_options.DefaultFontFamily, null);
    }

    /// <summary>
    /// 第一趟：方言、樣式、字串字典、外部佈景。跟預設字型與語言無關，做一次就好。
    /// </summary>
    private void Prepare(string root)
    {
        var files = EnumerateXaml(root).ToList();
        var rels = files.Select(f => Rel(root, f)).ToList();

        // ── 第一趟：方言、樣式、字串字典、外部佈景 ──
        // ⚠ 一定要先收齊再算。字型大小與版面內距大量寫在樣式裡，不先收就會用預設值去量
        //   （實查：QuillNest 的 SectionHint 設 FontSize=11，用預設 12 量出一個「已經溢出 2.9px」的假警報）。
        //   重新 parse 一次比把上千份 XDocument 抱在記憶體裡便宜。
        _styles = new StyleIndex { SystemScrollBarWidth = _chrome.ScrollBarWidth };
        var dialects = new string?[files.Count];           // null = WPF
        var marked = new bool[files.Count];
        var themeDeclarations = new List<(int File, string Theme, bool AppWide)>();
        var appDirs = new List<string>();
        var stringTables = new List<(string Rel, string Culture, Dictionary<string, string> Values)>();

        for (var i = 0; i < files.Count; i++)
        {
            XDocument doc;
            try { doc = SafeXml.Load(files[i]); }
            catch (Exception) { continue; /* 第二趟會記成 parse error */ }
            if (doc.Root is null) continue;

            (dialects[i], marked[i]) = DialectOf(doc.Root);
        }

        ResolveAmbiguousDialects(rels, dialects, marked);

        for (var i = 0; i < files.Count; i++)
        {
            if (dialects[i] is not null) continue;

            XDocument doc;
            try { doc = SafeXml.Load(files[i]); }
            catch (Exception) { continue; }
            if (doc.Root is null) continue;

            ApplyAliases(doc);
            doc.AddAnnotation(new SourceFile(rels[i]));
            _styles.Add(doc, rels[i]);

            var rootName = doc.Root.Name.LocalName;
            if (rootName == "Application") appDirs.Add(DirOf(rels[i]));

            foreach (var theme in ExternalThemesIn(doc.Root, rels))
                themeDeclarations.Add((i, theme, rootName is "Application" or "ResourceDictionary"));

            if (ReadStringTable(doc.Root) is { Count: > 0 } table)
                stringTables.Add((rels[i], CultureOfDictionary(files[i]), table));
        }

        _styles.SetProjectDirectories(appDirs);

        var strings = MergeStringTables(stringTables);
        _stringKeys = [.. strings.SelectMany(s => s.Values.Keys)];
        _sourceStrings = SourceStrings(strings);

        ComputeHostFonts(files, rels, dialects, appDirs);

        _files = files;
        _rels = rels;
        _dialects = dialects;
        _themes = ScopeThemes(themeDeclarations, rels, appDirs);
        _strings = strings;
    }

    /// <summary>
    /// 第二趟：文字位置。可以用不同的預設字型與字串表重跑（要先 <see cref="Scan"/> 過一次）。
    ///
    /// 為什麼要重跑：同一排的兄弟元素也是在地化字串時（一排按鈕、標籤加輸入框），
    /// 留給這個位置的寬度取決於**兄弟在那個語言有多寬**。而沒寫 FontFamily 的文字用的字型
    /// 也跟著 Windows 的語言走（見 <see cref="CultureFonts"/>）。
    /// </summary>
    /// <param name="strings">資源鍵 → 這個語言的文字。null = 只用掃描時找到的來源語言字串。</param>
    public ScanResult Rescan(string defaultFontFamily, IReadOnlyDictionary<string, string>? strings)
    {
        var files = _files;
        var rels = _rels;
        var dialects = _dialects;
        var themes = _themes;
        _defaultFont = defaultFontFamily;

        var measurer = _fonts is null
            ? null
            : new ElementMeasurer(_fonts, _styles, _chrome, defaultFontFamily, _options.DefaultFontSize)
            {
                ResourceText = key =>
                    strings is not null && strings.TryGetValue(key, out var localized) ? localized
                    : _sourceStrings.TryGetValue(key, out var source) ? source
                    : null,
            };
        _width = new WidthResolver(_chrome, _styles, measurer);
        _missingFonts.Clear();

        // ── 第二趟：文字位置 ──
        var sites = new List<TextSite>();
        var errors = new List<string>();
        var foreign = new List<ForeignFile>();

        for (var i = 0; i < files.Count; i++)
        {
            XDocument doc;
            try { doc = SafeXml.Load(files[i], lineInfo: true); }
            catch (Exception) { errors.Add(rels[i]); continue; }
            if (doc.Root is null) continue;

            if (dialects[i] is { } dialect)
            {
                foreign.Add(new ForeignFile(rels[i], dialect));
                continue;
            }

            ApplyAliases(doc);
            doc.AddAnnotation(new SourceFile(rels[i]));
            if (!_options.TrustDefaultChrome && ThemeFor(rels[i], themes) is { } themeName)
                doc.AddAnnotation(new ExternalTheme(themeName));

            foreach (var el in doc.Root.DescendantsAndSelf())
            {
                var site = TrySite(el, rels[i]);
                if (site is not null) sites.Add(site);
            }
        }

        return new ScanResult(sites, files.Count, errors.Count, errors)
        {
            ForeignFiles = foreign,
            ExternalThemes = _options.TrustDefaultChrome ? [] : themes,
            MissingFonts = [.. _missingFonts],
            Strings = _strings,
            NeighbourTextUses = (measurer?.ResourceLookups ?? 0) + (measurer?.ResourceMisses ?? 0),
        };
    }

    private const string WpfNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>會從宿主視窗繼承下來、而且影響文字寬度的屬性。</summary>
    private static readonly string[] HostInherited =
        ["FontFamily", "FontSize", "FontWeight", "FontStyle", "FontStretch", "TextOptions.TextFormattingMode"];

    /// <summary>
    /// UserControl 與 Page 裡沒有自己設定字型的文字，用的是**裝它的視窗**的字型 —— 而那個視窗在別的檔案裡。
    ///
    /// ⚠ 自我檢驗抓到的（MaterialDesign 的示範程式）：主視窗設了字型，各個分頁是獨立的 UserControl 檔案。
    ///   第一版每個檔案各自從預設值（Segoe UI 12）算起，等於把整個 App 的字型設定丟掉。
    ///
    /// 靜態不知道哪個控制項裝在哪個視窗裡，所以用「同一個應用程式裡所有視窗的共識」：
    ///   - 視窗們都沒設 → 沒人設定，用預設值
    ///   - 視窗們設的值都一樣 → 就是它
    ///   - 設得不一樣、或有的讀不出來 → **不知道**（除非使用者自己指定了預設字型）
    /// 專案裡一個視窗都沒有（純控制項庫）時無從得知，當成沒人設定 —— 這是假設，文件裡有寫。
    /// </summary>
    private void ComputeHostFonts(List<string> files, List<string> rels, string?[] dialects, List<string> appDirs)
    {
        string ProjectOf(string rel)
        {
            var best = "";
            foreach (var dir in appDirs)
                if (rel.StartsWith(dir, StringComparison.Ordinal) && dir.Length > best.Length) best = dir;
            return best;
        }

        // (專案, 屬性) → 各個視窗的答案
        var declared = new Dictionary<(string Project, string Property), List<Resolved>>();
        _styles.InheritedFromHost = null;

        for (var i = 0; i < files.Count; i++)
        {
            if (dialects[i] is not null) continue;

            XDocument doc;
            try { doc = SafeXml.Load(files[i]); }
            catch (Exception) { continue; }
            if (doc.Root is null || !IsWindowRoot(doc.Root)) continue;

            ApplyAliases(doc);
            doc.AddAnnotation(new SourceFile(rels[i]));

            var project = ProjectOf(rels[i]);
            foreach (var property in HostInherited)
            {
                if (!declared.TryGetValue((project, property), out var list))
                    declared[(project, property)] = list = [];
                list.Add(_styles.ResolveEx(doc.Root, property, inheritable: false));
            }
        }

        var consensus = new Dictionary<(string Project, string Property), Resolved>();
        foreach (var (key, answers) in declared)
        {
            Resolved result;
            if (answers.FirstOrDefault(a => a.IsUnreadable) is { IsUnreadable: true })
                result = Resolved.Unknown($"the hosting window sets {key.Property} to something this tool cannot read");
            else if (answers.All(a => a.Kind == ResolveKind.NotSet))
                result = Resolved.NotSet;
            else if (answers.All(a => a.IsValue && a.Value == answers[0].Value))
                result = answers[0];
            else
            {
                var values = string.Join(", ", answers.Select(a => a.IsValue ? a.Value : "(not set)").Distinct().Take(4));
                result = Resolved.Unknown(
                    $"windows in this project set different {key.Property} values ({values}), and an inherited value depends on " +
                    "which window hosts this control; set defaultFontFamily/defaultFontSize in xamlfit.config.json to pin it");
            }

            // 使用者指定了預設字型：家族與字級的不一致以使用者說的為準
            if (result.IsUnreadable && _options.DefaultFontPinned && key.Property is "FontFamily" or "FontSize")
                result = Resolved.NotSet;

            consensus[key] = result;
        }

        _styles.InheritedFromHost = (element, property) =>
        {
            var root = element.Document?.Root;
            if (root is null || IsWindowRoot(root)) return Resolved.NotSet;

            var file = element.Document?.Annotation<SourceFile>()?.Path ?? "";
            return consensus.TryGetValue((ProjectOf(file), property), out var answer) ? answer : Resolved.NotSet;
        };
    }

    /// <summary>根元素是不是視窗（含第三方的 MetroWindow、FluentWindow…）。</summary>
    private static bool IsWindowRoot(XElement root) =>
        root.Name.LocalName.EndsWith("Window", StringComparison.Ordinal);

    /// <summary>
    /// 套用使用者宣告的「把 A 當成 B」：直接把元素改名成 WPF 的 B，之後的規則就一視同仁。
    /// 屬性元素一起改（&lt;controls:GridEx.ColumnDefinitions&gt; → &lt;Grid.ColumnDefinitions&gt;）。
    /// </summary>
    private void ApplyAliases(XDocument doc)
    {
        if (_options.TreatAs.Count == 0 || doc.Root is null) return;

        foreach (var el in doc.Root.DescendantsAndSelf().ToList())
        {
            var local = el.Name.LocalName;
            var dot = local.IndexOf('.');
            var head = dot < 0 ? local : local[..dot];
            if (!_options.TreatAs.TryGetValue(head, out var target)) continue;

            el.Name = XName.Get(dot < 0 ? target : target + local[dot..], WpfNamespace);
        }
    }

    private static string Rel(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string DirOf(string rel)
    {
        var slash = rel.LastIndexOf('/');
        return slash < 0 ? "" : rel[..(slash + 1)];
    }

    // ───────────────────────── 方言 ─────────────────────────

    /// <summary>
    /// 這份 XAML 是哪一種。回傳 (方言或 null=WPF, 有沒有明確的記號)。
    ///
    /// ⚠ 全面重讀時發現：語料裡的 WinUI-Gallery 一直被當成 WPF 在算，還報出「可解析率 99.2%」。
    ///   WinUI 的預設字級是 14 不是 12、Button 的內距是 22 不是 4、版面語意也不一樣 ——
    ///   那 99.2% 的每一個數字都是錯的。**量錯比量不到糟。**
    ///
    /// WinUI／UWP 與 WPF 的預設命名空間是同一個 URI，唯一可靠的記號是
    /// <c>xmlns:x="using:…"</c>（WinUI／UWP）對 <c>clr-namespace:</c>（WPF）。
    /// 兩種記號都沒有的檔案交給 <see cref="ResolveAmbiguousDialects"/> 看鄰居決定。
    /// </summary>
    private static (string? Dialect, bool Marked) DialectOf(XElement root)
    {
        var ns = root.Name.NamespaceName;
        if (ns.Contains("avaloniaui", StringComparison.OrdinalIgnoreCase)) return ("Avalonia", true);
        if (ns.Contains("/dotnet/2021/maui", StringComparison.OrdinalIgnoreCase)) return (".NET MAUI", true);
        if (ns.Contains("xamarin.com/schemas", StringComparison.OrdinalIgnoreCase)) return ("Xamarin.Forms", true);

        var sawUsing = false;
        var sawClr = false;
        foreach (var el in root.DescendantsAndSelf())
        {
            foreach (var a in el.Attributes())
            {
                if (!a.IsNamespaceDeclaration) continue;
                if (a.Value.StartsWith("using:", StringComparison.Ordinal)) sawUsing = true;
                else if (a.Value.StartsWith("clr-namespace:", StringComparison.Ordinal)) sawClr = true;
            }
        }

        if (sawUsing && !sawClr) return ("WinUI/UWP", true);
        return (null, sawClr);
    }

    /// <summary>
    /// 沒有記號的檔案（只用預設命名空間的小頁面或資源字典）：往上找最近一個「底下有帶記號的檔案」
    /// 的目錄，跟那裡的多數一樣。整個掃描範圍都沒有記號時當成 WPF。
    /// </summary>
    private static void ResolveAmbiguousDialects(List<string> rels, string?[] dialects, bool[] marked)
    {
        var votes = new Dictionary<string, (int Wpf, int Foreign, string? Dialect)>(StringComparer.Ordinal);

        for (var i = 0; i < rels.Count; i++)
        {
            if (!marked[i]) continue;
            for (var dir = DirOf(rels[i]); ; dir = ParentDir(dir))
            {
                var v = votes.GetValueOrDefault(dir);
                votes[dir] = dialects[i] is null
                    ? (v.Wpf + 1, v.Foreign, v.Dialect)
                    : (v.Wpf, v.Foreign + 1, v.Dialect ?? dialects[i]);
                if (dir.Length == 0) break;
            }
        }

        for (var i = 0; i < rels.Count; i++)
        {
            if (marked[i]) continue;
            for (var dir = DirOf(rels[i]); ; dir = ParentDir(dir))
            {
                if (votes.TryGetValue(dir, out var v) && v.Wpf + v.Foreign > 0)
                {
                    if (v.Foreign > v.Wpf) dialects[i] = v.Dialect;
                    break;
                }
                if (dir.Length == 0) break;
            }
        }
    }

    private static string ParentDir(string dir)
    {
        if (dir.Length == 0) return "";
        var slash = dir.LastIndexOf('/', dir.Length - 2);
        return slash < 0 ? "" : dir[..(slash + 1)];
    }

    // ───────────────────────── 外部佈景 ─────────────────────────

    /// <summary>
    /// 這個檔案合併進來的**專案外**資源字典。
    ///
    /// ⚠ MaterialDesign、wpfui、HandyControl、MahApps 這類佈景會把 Button、ComboBox… 整個重畫
    ///   （MaterialDesign 的 Button 內距是 32 不是 4），而且樣式定義在別的組件裡，我們看不到。
    ///   專案套了它們之後，內建的 Aero2 內距表對標準控制項不再成立。
    /// </summary>
    private static IEnumerable<string> ExternalThemesIn(XElement root, List<string> scannedRels)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var merged in root.Descendants().Where(e => e.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal)))
        {
            foreach (var entry in merged.Elements())
            {
                string? name = null;

                if (entry.Name.LocalName == "ResourceDictionary" && ChromeTable.IsWpf(entry))
                {
                    if (entry.Attribute("Source")?.Value is { } source) name = ExternalAssemblyOf(source, scannedRels);
                }
                else
                {
                    // <ui:ThemesDictionary/>、<materialDesign:BundledTheme/>：第三方的字典型別
                    var ns = entry.Name.NamespaceName;
                    if (ns.StartsWith("clr-namespace:", StringComparison.Ordinal))
                    {
                        var marker = ns.IndexOf(";assembly=", StringComparison.Ordinal);
                        if (marker >= 0) name = ns[(marker + ";assembly=".Length)..].Trim();
                        // 沒有 assembly= 的是專案自己的型別，看得到，不算外部
                    }
                    else if (!ChromeTable.IsWpfNamespace(ns))
                    {
                        name = entry.GetPrefixOfNamespace(entry.Name.Namespace) is { Length: > 0 } prefix
                            ? $"{prefix}:{entry.Name.LocalName}"
                            : entry.Name.LocalName;
                    }
                }

                if (!string.IsNullOrEmpty(name) && seen.Add(name)) yield return name;
            }
        }
    }

    /// <summary>
    /// <c>pack://application:,,,/Asm;component/Themes/X.xaml</c> 指向的組件名；指向專案自己的檔案時回 null。
    /// 判斷「是不是自己的」不靠組件名（靜態不知道專案的組件名），靠那條路徑在掃描範圍裡找不找得到。
    /// </summary>
    private static string? ExternalAssemblyOf(string source, List<string> scannedRels)
    {
        var marker = source.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;   // 相對路徑：專案自己的檔案

        var path = source[(marker + ";component/".Length)..].Replace('\\', '/').TrimStart('/');
        if (scannedRels.Any(r => r.EndsWith(path, StringComparison.OrdinalIgnoreCase)
                                 && (r.Length == path.Length || r[r.Length - path.Length - 1] == '/')))
            return null;

        var head = source[..marker];
        var slash = head.LastIndexOf('/');
        var assembly = slash >= 0 ? head[(slash + 1)..] : head;
        return assembly.Length > 0 ? assembly : null;
    }

    /// <summary>
    /// 佈景影響的範圍。寫在 App.xaml 或共用資源字典裡的：最近一個 App.xaml 所在目錄底下的全部
    /// （找不到 App.xaml 就是整個掃描範圍）。寫在單一視窗／控制項自己的 Resources 裡的：只有那個檔案。
    /// </summary>
    private static List<ThemeUse> ScopeThemes(
        List<(int File, string Theme, bool AppWide)> declarations, List<string> rels, List<string> appDirs)
    {
        var result = new List<ThemeUse>();
        foreach (var (file, theme, appWide) in declarations)
        {
            var declaredIn = rels[file];
            string scope;

            if (!appWide) scope = declaredIn;
            else
            {
                scope = "";
                foreach (var dir in appDirs)
                    if (declaredIn.StartsWith(dir, StringComparison.Ordinal) && dir.Length > scope.Length) scope = dir;
            }

            if (!result.Any(t => t.Theme == theme && t.Scope == scope))
                result.Add(new ThemeUse(theme, declaredIn, scope));
        }
        return result;
    }

    private static string? ThemeFor(string rel, List<ThemeUse> themes)
    {
        foreach (var t in themes)
        {
            var isFileScope = t.Scope.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
            if (isFileScope ? t.Scope == rel : rel.StartsWith(t.Scope, StringComparison.Ordinal))
                return t.Theme;
        }
        return null;
    }

    // ───────────────────────── XAML 字串字典 ─────────────────────────

    private static readonly XName XKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    /// <summary>
    /// 資源字典裡的 <c>&lt;sys:String x:Key="K"&gt;V&lt;/sys:String&gt;</c>。
    ///
    /// ⚠ 這是 WPF 專案最常見的在地化寫法（配 <c>{DynamicResource K}</c>，執行期可以換語言）。
    ///   語料實測：Playnite 1,038 處、ScreenToGif 1,540 處；<c>{x:Static}</c> 加 .resx 的全語料只有 33 處。
    ///   第一版只認後者，等於把真實專案的在地化字串幾乎全部當成「綁資料」略過。
    /// </summary>
    private static Dictionary<string, string>? ReadStringTable(XElement root)
    {
        if (root.Name.LocalName != "ResourceDictionary") return null;

        Dictionary<string, string>? table = null;
        foreach (var el in root.Elements())
        {
            if (el.Name.LocalName != "String" || el.HasElements) continue;
            if (el.Attribute(XKey)?.Value is not { Length: > 0 } key) continue;

            (table ??= new Dictionary<string, string>(StringComparer.Ordinal))[key] = TextRules.ElementText(el);
        }
        return table;
    }

    /// <summary>
    /// 字串字典的文化，從檔名判斷：<c>StringResources.de.xaml</c> → de；<c>de_DE.xaml</c> → de-DE；
    /// <c>LocSource.xaml</c> → 中性。
    /// </summary>
    internal static string CultureOfDictionary(string file)
    {
        var fromSuffix = ResxReader.CultureOf(file);
        if (fromSuffix != ResxReader.NeutralCulture) return fromSuffix;

        var name = Path.GetFileNameWithoutExtension(file).Replace('_', '-');
        if (name.Length is < 2 or > 12 || !name.Contains('-', StringComparison.Ordinal) && name.Length > 3)
            return ResxReader.NeutralCulture;

        try
        {
            return System.Globalization.CultureInfo.GetCultureInfo(name, predefinedOnly: true).Name;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return ResxReader.NeutralCulture;
        }
    }

    private static List<CultureStrings> MergeStringTables(
        List<(string Rel, string Culture, Dictionary<string, string> Values)> tables)
    {
        var result = new List<CultureStrings>();
        foreach (var group in tables.GroupBy(t => t.Culture, StringComparer.OrdinalIgnoreCase))
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in group)
                foreach (var (k, v) in t.Values)
                    merged.TryAdd(k, v);
            result.Add(new CultureStrings(group.Key, merged, string.Join(", ", group.Select(t => t.Rel).Take(3))));
        }
        return result;
    }

    /// <summary>
    /// 來源語言的字串（量「目前有多寬」用）。有中性的字典就用它；沒有的話（ScreenToGif 的基底是
    /// <c>StringResources.en.xaml</c>）退到 en／en-US；再沒有就不知道。
    /// </summary>
    private static Dictionary<string, string> SourceStrings(List<CultureStrings> strings)
    {
        var source = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in (ReadOnlySpan<string>)[ResxReader.NeutralCulture, "en", "en-US", "en-GB"])
            foreach (var table in strings.Where(s => s.Culture.Equals(culture, StringComparison.OrdinalIgnoreCase)))
                foreach (var (k, v) in table.Values)
                    source.TryAdd(k, v);
        return source;
    }

    // ───────────────────────── 檔案列舉 ─────────────────────────

    /// <summary>
    /// 預設排除。⚠ 點開頭的目錄一定要排除 ——
    /// .claude/worktrees 裡是同一個專案的副本，不排除會重複計算好幾倍（M0 實查踩到）。
    /// </summary>
    public IEnumerable<string> EnumerateXaml(string root) =>
        EnumerateFiltered(root, ".xaml", _options.ExcludeGlobs);

    /// <summary>
    /// 列舉指定副檔名的檔案，套用預設排除。
    ///
    /// ⚠ 兩個都是真的踩過才知道的：
    ///
    /// 1. **比對的是「相對於 root 的路徑」，不是完整路徑。**
    ///    用完整路徑比對時，只要掃描根目錄本身位在點開頭的資料夾底下
    ///    （<c>C:\Users\x\.config\app</c>），或路徑裡剛好有個叫 <c>bin</c> 的目錄
    ///    （<c>D:\bin\myproject</c>），**所有檔案都會被排除** ——
    ///    然後吐 exit 3 說「找不到任何文字位置」，完全不解釋為什麼。
    ///
    /// 2. **副檔名用不分大小寫比對，不用 <c>*.xaml</c> 這種萬用字元。**
    ///    <c>Directory.EnumerateFiles(root, "*.xaml")</c> 在 Windows 會配到 <c>.XAML</c>，
    ///    在 Linux 不會（檔案系統區分大小寫）。
    ///    結果是同一個專案在 CI 上掃到的檔案比本機少，而且是**靜默的** ——
    ///    CI 看起來比本機乾淨，那正是謊報健康。
    /// </summary>
    internal static IEnumerable<string> EnumerateFiltered(
        string root, string extension, IReadOnlyList<string> excludeGlobs)
    {
        // ⚠ 目錄剪枝，不要「列舉全部再過濾」。被排除的目錄根本不進去 ——
        //   一個帶 node_modules 的 repo 可能有十幾萬個檔案，而我們要找的只有幾十個 XAML。
        var stack = new Stack<string>();
        stack.Push(root);

        // ⚠ 兩道防線擋住無限遞迴。工具跑在 CI 上、掃的是任意 repo ——
        //   一個 `ln -s .. loop` 就能讓建置掛死，而那不需要任何惡意，手滑就會有。
        //   (1) 不進符號連結與 reparse point（junction）
        //   (2) 記錄走過的真實路徑，擋住 (1) 漏掉的情形
        var visited = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var found = new List<string>();

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] subdirs;
            string[] files;
            try
            {
                if (!visited.Add(Path.GetFullPath(dir))) continue;
                subdirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException) { continue; }   // 權限不足的目錄跳過，不要整個掃描失敗
            // ⚠ IOException 要一起接：斷線的網路磁碟、壞掉的 mount 點
            //   不該讓整個掃描失敗，只該跳過那一個目錄
            catch (IOException) { continue; }

            foreach (var f in files)
            {
                if (!f.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                var rel = "/" + Path.GetRelativePath(root, f).Replace('\\', '/');
                if (excludeGlobs.Any(g => rel.Contains(g, StringComparison.OrdinalIgnoreCase))) continue;
                found.Add(f);
            }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (IsExcludedDirectory(name)) continue;
                if (IsLink(sub)) continue;
                stack.Push(sub);
            }
        }

        // ⚠ 排序。檔案系統的列舉順序在 Windows 與 Linux 不一樣，而報告與 baseline 的內容
        //   不該隨機器而變（「同名定義取第一個」這類規則也會跟著順序走）。
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>
    /// 符號連結或 reparse point（Windows 的 junction）。
    ///
    /// ⚠ 不進去。理由不是安全而是**會掛死**：一個指向上層的連結就是無限遞迴，
    ///   而那在 repo 裡不需要任何惡意，手滑就會有。指向 <c>/</c> 的連結更糟 ——
    ///   不是迴圈，是把整個檔案系統掃一遍。
    /// ⚠ 代價：放在連結底下的真實原始碼會掃不到。這是刻意的取捨，
    ///   「漏掃一個不尋常的目錄」遠好過「CI 卡住直到逾時」。
    /// </summary>
    private static bool IsLink(string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            return info.LinkTarget is not null
                || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException) { return true; }                  // 讀不到屬性就不要進去
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>
    /// 要跳過的目錄名。⚠ 比對的是**目錄名**而不是完整路徑（理由見 <see cref="EnumerateFiltered"/>）。
    /// </summary>
    private static bool IsExcludedDirectory(string name) =>
        // .claude/worktrees 裡是同一個專案的副本，不排除會重複計算好幾倍（M0 實查踩到）
        name.StartsWith('.')
        || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
        || name.Equals("packages", StringComparison.OrdinalIgnoreCase);

    // ───────────────────────── 文字位置 ─────────────────────────

    private TextSite? TrySite(XElement el, string file)
    {
        var local = el.Name.LocalName;
        if (TextPropertyOf(local) is not { } property) return null;
        if (IsIgnorableNamespace(el.Name.NamespaceName)) return null;

        var (source, text, key) = ExtractText(el, property);
        if (text is null) return null;

        var sourceText = source == TextSource.Literal ? text
            : key is not null && _sourceStrings.TryGetValue(key, out var known) ? known : null;
        var localizable = source != TextSource.DataBound && BudgetAnalyzer.HasLetter(sourceText ?? text);

        var font = FontSpec.Read(_styles, el, _defaultFont, _options.DefaultFontSize);
        var width = FinalWidth(el, font, localizable);

        // 換行與裁切是 TextBlock 自己的屬性，不會繼承。Run 的換行由它所在的 TextBlock 決定；
        // 其他控制項（Button Content="…"）內部產生的文字一律不換行。
        var textHost = local == "Run"
            ? el.Ancestors().FirstOrDefault(a => a.Name.LocalName.EndsWith("TextBlock", StringComparison.Ordinal))
            : local.EndsWith("TextBlock", StringComparison.Ordinal) || local.EndsWith("AccessText", StringComparison.Ordinal) ? el : null;

        var ignore = IgnoreReason(el);

        return new TextSite
        {
            File = file,
            Line = WidthResolver.LineOf(el),
            Element = local,
            Source = source,
            Text = text,
            ResourceKey = key,
            SourceText = sourceText,
            FontFamily = font.Family,
            FontSize = font.Size,
            FontWeight = font.Weight,
            FontStyle = font.Style,
            Wrap = textHost is null ? WrapMode.NoWrap : ParseWrap(_styles.Resolve(textHost, "TextWrapping", inheritable: false)),
            Trim = textHost is null ? TrimMode.None : ParseTrim(_styles.Resolve(textHost, "TextTrimming", inheritable: false)),
            Height = HeightOf(textHost ?? el),
            Width = width,
            SuppressedReason = ignore is { Length: > 0 } ? ignore : null,
            // 理由必填：空理由的 ignore 不生效，但要讓使用者知道它沒生效
            InvalidIgnore = ignore is { Length: 0 },
        };
    }

    /// <summary>
    /// 版面寬度算得出來還不夠 —— 字型也要讀得出來、度量檔裡要有，這個位置才算「量得到」。
    /// 第一版把這兩種情形照樣算成量到了，然後在判定時默默跳過。
    /// </summary>
    private AvailableWidth FinalWidth(XElement el, FontSpec font, bool localizable)
    {
        var width = _width.Resolve(el);
        if (width.Status != WidthStatus.Known) return width;

        // 同一行有別的行內內容：這一行的寬度是全部加起來，單獨量一個 Run 沒有意義
        if (el.Name.LocalName == "Run" && el.Parent is { } line
            && (line.Elements().Count() > 1 || line.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))))
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "shares its line with other inline content");

        if (font.Unreadable is { } why)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, why);

        if (_fonts is not null)
        {
            var lookup = _fonts.Resolve(font.Family, font.Weight, font.Style, _defaultFont);
            if (lookup.Chain.Count == 0)
            {
                // 圖示字型上的符號不是翻譯對象 —— 不要為了它叫使用者去匯出 Segoe MDL2 Assets
                if (localizable && lookup.MissingFamily is { } family) _missingFonts.Add(family);
                return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, lookup.MissDescription ?? "no font metrics");
            }
        }

        return width;
    }

    /// <summary>宣告的高度上界（垂直裁切判定要用）：Height 與 MaxHeight 取小的。讀不出來當成沒有。</summary>
    private double? HeightOf(XElement el)
    {
        double? result = null;
        foreach (var property in (ReadOnlySpan<string>)["Height", "MaxHeight"])
        {
            var r = _styles.ResolveEx(el, property, inheritable: false);
            if (r.IsValue && WidthResolver.TryLength(r.Value, out var h) && (result is null || h < result))
                result = h;
        }
        return result;
    }

    private static bool IsIgnorableNamespace(string ns) =>
        ns.Contains("ignore", StringComparison.OrdinalIgnoreCase) ||
        ns.Contains("/blend/", StringComparison.OrdinalIgnoreCase) ||
        ns == "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private (TextSource, string?, string?) ExtractText(XElement el, string property)
    {
        var local = el.Name.LocalName;

        if (el.Attribute(property)?.Value is { } raw)
        {
            var v = raw.Trim();
            if (v.Length == 0) return (TextSource.Literal, null, null);

            // "{}" 是 XAML 的跳脫前綴：{}{0} 其實是字面的 "{0}"
            if (v.StartsWith("{}", StringComparison.Ordinal))
                return (TextSource.Literal, v[2..], null);

            if (v.StartsWith('{'))
            {
                var key = ResourceKeyOf(v) ?? StringResourceKeyOf(v);
                return key is not null
                    ? (TextSource.Resource, v, key)
                    : (TextSource.DataBound, v, null);
            }

            // 屬性值裡的換行在 XML 剖析時已經被正規化成空格；&#x0a; 寫的才是真的換行，保留
            return (TextSource.Literal, v, null);
        }

        // 屬性元素寫法：<Button.Content>Save</Button.Content>
        var holder = el.Elements().FirstOrDefault(e => e.Name.LocalName == $"{local}.{property}");
        if (holder is not null)
            return !holder.HasElements && !string.IsNullOrWhiteSpace(holder.Value)
                ? (TextSource.Literal, TextRules.ElementText(holder), null)
                : (TextSource.Literal, null, null);

        // 直接寫在元素內容裡：<TextBlock>Save</TextBlock>、<Button>Save</Button>。
        // 只有內容屬性就是文字屬性的元素才算（<GroupBox>…</GroupBox> 的內容不是標題）。
        if (property is "Text" or "Content" && !el.HasElements && !string.IsNullOrWhiteSpace(el.Value))
            return (TextSource.Literal, TextRules.ElementText(el), null);

        return (TextSource.Literal, null, null);
    }

    /// <summary>
    /// <c>{DynamicResource K}</c>／<c>{StaticResource K}</c>，而且 K 是某個字串字典裡的字串。
    /// 不是字串的資源（圖示、幾何圖形）不是文字。
    /// </summary>
    private string? StringResourceKeyOf(string markup) =>
        StyleIndex.ResourceKeyOf(markup) is { } key && _stringKeys.Contains(key) ? key : null;

    /// <summary>從標記延伸抽出資源鍵。認得的寫法：
    /// <c>{x:Static p:Resources.Save}</c>、<c>{Binding Source={x:Static p:Resources.Save}}</c>、
    /// 以及常見的本地化標記延伸 <c>{loc:Translate Save}</c>／<c>{lex:Loc Save}</c>。
    ///
    /// ⚠ **整個值就是它**才算。第一版用不設錨點的比對，於是
    ///   <c>{Binding Name, Converter={x:Static local:Converters.Upper}}</c> 被當成資源鍵 <c>Upper</c>。</summary>
    internal static string? ResourceKeyOf(string markup)
    {
        var m = StaticResourceStringRegex().Match(markup);
        if (!m.Success) m = BindingToStaticRegex().Match(markup);
        if (m.Success)
        {
            // path 長這樣：p:Strings.Save、local:Resources.Dialog_Title、Strings.Save
            // 鍵是最後一段。⚠ 第一版的 regex 用 [\w.]*? 去吃前綴，而 \w 不含冒號 ——
            //   於是 {x:Static p:Strings.Save} 整個比對失敗，所有資源字串被誤判成「綁資料」
            //   然後被濾掉。示範專案第一次跑就是 0 個位置。
            var path = m.Groups["path"].Value;
            var dot = path.LastIndexOf('.');
            if (dot >= 0 && dot + 1 < path.Length) return path[(dot + 1)..];
            return null;
        }

        m = LocExtensionRegex().Match(markup);
        return m.Success ? m.Groups["key"].Value : null;
    }

    // {x:Static p:Resources.Save} → Save ；{x:Static local:Strings.Dialog_Title} → Dialog_Title
    [GeneratedRegex(@"^\{\s*x:Static\s+(?:Member\s*=\s*)?(?<path>[\w.:]+)\s*\}$")]
    private static partial Regex StaticResourceStringRegex();

    // {Binding Source={x:Static p:Resources.Save}}（可以帶 Mode=OneWay）
    [GeneratedRegex(@"^\{\s*Binding\s+Source\s*=\s*\{\s*x:Static\s+(?<path>[\w.:]+)\s*\}\s*(?:,\s*Mode\s*=\s*\w+\s*)?\}$")]
    private static partial Regex BindingToStaticRegex();

    // {loc:Translate Save} / {lex:Loc Save} / {i18n:Translate Key=Save}
    [GeneratedRegex(@"^\{\s*(?:loc|lex|i18n|tr|l10n|lang)\s*:\s*\w+\s+(?:Key\s*=\s*)?(?<key>[\w.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LocExtensionRegex();

    private static WrapMode ParseWrap(string? v) => v?.Trim().ToLowerInvariant() switch
    {
        "wrap" => WrapMode.Wrap,
        "wrapwithoverflow" => WrapMode.WrapWithOverflow,
        _ => WrapMode.NoWrap,
    };

    private static TrimMode ParseTrim(string? v) => v?.Trim().ToLowerInvariant() switch
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

    [GeneratedRegex(@"xamlfit-ignore\s*:?\s*(?<reason>.*)", RegexOptions.Singleline)]
    private static partial Regex IgnoreRegex();
}
