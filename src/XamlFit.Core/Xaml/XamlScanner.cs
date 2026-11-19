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

    /// <summary>使用者宣告的「用屬性產生欄的 Grid 子類別」（見 <see cref="GridPanel"/>）。</summary>
    public IReadOnlyDictionary<string, GridPanel> GridPanels { get; init; } = new Dictionary<string, GridPanel>();

    /// <summary>使用者宣告的「垂直捲軸實際上不會出現」的 ScrollViewer／清單（x:Name）。</summary>
    public IReadOnlyCollection<string> HiddenScrollBars { get; init; } = [];

    /// <summary>使用者宣告的「執行期才知道寬度的控制項最寬多寬」（見 <see cref="ElementMeasurer.DeclaredWidths"/>）。</summary>
    public IReadOnlyDictionary<string, double> ControlWidths { get; init; } = new Dictionary<string, double>();

    /// <summary>Display 模式要檢查的顯示縮放比例（設定檔的 displayScales）。</summary>
    public IReadOnlyList<double> DisplayScales { get; init; } = AllDisplayScales;

    public static readonly IReadOnlyList<double> AllDisplayScales = [1.0, 1.25, 1.5, 1.75, 2.0];

    /// <summary>使用者明確指定了預設字型（設定檔或旗標）。視窗之間對字型的設定不一致時，
    /// 以使用者指定的為準，不再把 UserControl 裡的文字判成「不知道繼承到什麼」。</summary>
    public bool DefaultFontPinned { get; init; }

    /// <summary>同一排的兄弟怎麼算（見 <see cref="RowSharing"/>）。budget 用整排，check 用 WPF 的實際順序。</summary>
    public RowSharing Sharing { get; init; } = RowSharing.WholeRow;

    /// <summary>沒有寫 xml:lang 的元素的語言。WPF 的預設是 en-US；App 在程式碼裡改了預設值時由設定檔指定。</summary>
    public string ElementLanguage { get; init; } = Fonts.FontFallback.DefaultLanguage;

    /// <summary>可以縮小的視窗用 MinWidth 當上界（設定檔的 assumeWindowMinWidth；見 <see cref="WidthResolver.AssumeWindowMinWidth"/>）。</summary>
    public bool AssumeWindowMinWidth { get; init; }

    /// <summary>快顯最寬多寬（設定檔的 popupMaxWidth；見 <see cref="WidthResolver.PopupMaxWidth"/>）。null = 不檢查快顯。</summary>
    public double? PopupMaxWidth { get; init; }

    /// <summary>標題照 Windows 11 實量的標題列檢查（設定檔的 windowTitleBar: "windows11"）。false = 標題是 drawn-by-windows。</summary>
    public bool WindowTitleBarWindows11 { get; init; }
}

/// <summary>一個不是 WPF 的 XAML 檔（WinUI／UWP、Avalonia、MAUI）。不分析，但要講出來。</summary>
public sealed record ForeignFile(string File, string Dialect);

/// <summary>偵測到的外部佈景，以及它影響哪些檔案。</summary>
/// <param name="Profile">套用的量測資料（data/theme-chrome.json 的 id）；null 時 <paramref name="ProfileNote"/> 說為什麼沒套。</param>
public sealed record ThemeUse(string Theme, string DeclaredIn, string Scope)
{
    /// <summary>pack URI（合併的是 ResourceDictionary 時）。</summary>
    public string? Source { get; init; }
    /// <summary>字典型別的 XML 命名空間（&lt;materialDesign:BundledTheme/&gt;）。</summary>
    public string? Namespace { get; init; }
    public string? Profile { get; set; }
    public string? ProfileNote { get; set; }
}

/// <summary>一個合併進來的專案外字典：名稱（組件名或 前綴:型別）、pack URI、字典型別的命名空間。</summary>
internal sealed record ThemeRef(string Name, string? Source, string? Namespace);

public sealed record ScanResult(
    IReadOnlyList<TextSite> Sites,
    int FilesScanned,
    int ParseErrors,
    IReadOnlyList<string> ParseErrorFiles)
{
    /// <summary>
    /// 開了 UseLayoutRounding 的檔案。WPF 把每個尺寸捨入到裝置像素，範本裡一層層的內距各自捨入，
    /// 依內容決定寬度的文字右緣可能被削掉約一個像素 —— CorpusRender 實測 ScreenToGif 編輯器的 Expander 標題，
    /// 旁邊還有 200 px 空間也短了 0.5～1.2 px。工具不模擬像素捨入，只說出來。
    /// </summary>
    public IReadOnlyList<string> LayoutRoundingFiles { get; init; } = [];

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
    private string _language = Fonts.FontFallback.DefaultLanguage;

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
        return Rescan(_options.DefaultFontFamily, null, _options.ElementLanguage);
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
        var hosting = new HostingInfo?[files.Count];       // 誰裝誰（UserControl 的字型要往宿主找）
        var themeDeclarations = new List<(int File, ThemeRef Theme, bool AppWide)>();
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
            hosting[i] = HostingOf(doc.Root);

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

        ComputeHostFonts(files, rels, dialects, appDirs, hosting);

        _files = files;
        _rels = rels;
        _dialects = dialects;
        _themes = ScopeThemes(themeDeclarations, rels, appDirs);
        _profiles.Clear();
        AssignProfiles(_themes, root);
        _styles.ThemeOfFile = rel => ProfileFor(rel, _themes);
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
    /// <param name="language">沒寫 xml:lang 的元素的語言（null = 設定的預設）。</param>
    public ScanResult Rescan(string defaultFontFamily, IReadOnlyDictionary<string, string>? strings, string? language = null)
    {
        _language = language ?? _options.ElementLanguage;
        var files = _files;
        var rels = _rels;
        var dialects = _dialects;
        var themes = _themes;
        _defaultFont = defaultFontFamily;

        var measurer = _fonts is null
            ? null
            : new ElementMeasurer(_fonts, _styles, _chrome, defaultFontFamily, _options.DefaultFontSize)
            {
                DefaultLanguage = _language,
                DeclaredWidths = _options.ControlWidths,
                DisplayScales = _options.DisplayScales,
                ResourceText = key =>
                    strings is not null && strings.TryGetValue(key, out var localized) ? localized
                    : _sourceStrings.TryGetValue(key, out var source) ? source
                    : null,
            };
        _width = new WidthResolver(_chrome, _styles, measurer)
        {
            Sharing = _options.Sharing,
            AssumeWindowMinWidth = _options.AssumeWindowMinWidth,
            PopupMaxWidth = _options.PopupMaxWidth,
        };
        var hostDocs = new Dictionary<string, XDocument?>(StringComparer.Ordinal);
        _width.PlacementsOf = root => PlacementsOf(root, hostDocs);
        _measurer = measurer;
        _missingFonts.Clear();

        // ── 第二趟：文字位置 ──
        var sites = new List<TextSite>();
        var errors = new List<string>();
        var foreign = new List<ForeignFile>();
        var rounding = new List<string>();

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
                doc.AddAnnotation(new ExternalTheme(themeName, ProfileFor(rels[i], themes)));

            // ToList：ToolTip 的字型要在一個暫時掛上去的 <ToolTip> 上讀（見 TryToolTipSite），列舉中不能改文件
            foreach (var el in doc.Root.DescendantsAndSelf().ToList())
            {
                var site = TrySite(el, rels[i]);
                if (site is not null) sites.Add(site);
                if (TryToolTipSite(el, rels[i]) is { } tip) sites.Add(tip);
                if (TryWatermarkSite(el, rels[i]) is { } mark) sites.Add(mark);
                if (TryTitleSite(el, rels[i]) is { } title) sites.Add(title);
            }
            if (doc.Root.DescendantsAndSelf().Any(e => string.Equals((string?)e.Attribute("UseLayoutRounding"), "True", StringComparison.OrdinalIgnoreCase)))
                rounding.Add(rels[i]);
        }

        return new ScanResult(sites, files.Count, errors.Count, errors)
        {
            ForeignFiles = foreign,
            ExternalThemes = _options.TrustDefaultChrome ? [] : themes,
            MissingFonts = [.. _missingFonts],
            Strings = _strings,
            NeighbourTextUses = (measurer?.ResourceLookups ?? 0) + (measurer?.ResourceMisses ?? 0),
            LayoutRoundingFiles = rounding,
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
    /// 哪個控制項裝在哪個視窗裡，**從 XAML 看得到**：UserControl 的根元素有 <c>x:Class</c>，放它的檔案寫
    /// <c>&lt;views:CanvasView/&gt;</c>（命名空間是 <c>clr-namespace:</c>）。所以先照這張圖找：
    ///   - 在放它的那個元素上算繼承值（它自己設的、它祖先設的、宿主檔案根元素設的）；
    ///     宿主自己也是 UserControl 就再往它的宿主找，一路到視窗
    ///   - 每個放它的地方算出來都一樣 → 就是它；不一樣、或有一個算不出來（放在資源字典裡、循環）→ **不知道**，
    ///     原因裡列出是哪幾個檔案不一致
    ///
    /// ⚠ 回報 2（ZeroCel）：主視窗沒設字型、兩個對話框設 Segoe UI，第一版因為「視窗們不一致」把三個 UserControl 的
    ///   100 處全部擋掉 —— 但它們只被放在主視窗裡（CanvasView、ToolStripView 直接放，ToolbarView 經 ToolOptionsView），
    ///   順著放置關係看就知道。
    ///
    /// 沒有任何 XAML 放它（程式碼 new 出來、Frame.Source 載入、沒有 x:Class）時才退回「同一個應用程式裡所有視窗的共識」：
    ///   - 視窗們都沒設 → 沒人設定，用預設值
    ///   - 視窗們設的值都一樣 → 就是它
    ///   - 設得不一樣、或有的讀不出來 → **不知道**（除非使用者自己指定了預設字型）
    /// 專案裡一個視窗都沒有（純控制項庫）時無從得知，當成沒人設定 —— 這是假設，文件裡有寫。
    /// </summary>
    private void ComputeHostFonts(List<string> files, List<string> rels, string?[] dialects, List<string> appDirs, HostingInfo?[] hosting)
    {
        string ProjectOf(string rel)
        {
            var best = "";
            foreach (var dir in appDirs)
                if (rel.StartsWith(dir, StringComparison.Ordinal) && dir.Length > best.Length) best = dir;
            return best;
        }

        // 專案裡有 x:Class、不是視窗的根元素（UserControl、Page…）：放進別的 XAML 的候選
        var hostedClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in hosting)
            if (h is { Class: not null, IsWindow: false } && h.RootName is not ("Application" or "ResourceDictionary"))
                hostedClasses.Add(h.Class);

        // 類別 → 放它的檔案（寬度解析時從放它的元素接著往上走，見 WidthResolver.PlacementsOf）
        _placedIn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < files.Count; i++)
            if (dialects[i] is null && hosting[i] is { } info)
                foreach (var cls in info.Uses)
                    if (hostedClasses.Contains(cls))
                    {
                        if (!_placedIn.TryGetValue(cls, out var list)) _placedIn[cls] = list = [];
                        list.Add(rels[i]);
                    }

        // (專案, 屬性) → 各個視窗的答案
        var declared = new Dictionary<(string Project, string Property), List<Resolved>>();
        // (類別, 屬性) → 每一個放它的地方算出來的值
        var placements = new Dictionary<(string Class, string Property), List<Placement>>();
        _styles.InheritedFromHost = null;

        for (var i = 0; i < files.Count; i++)
        {
            if (dialects[i] is not null) continue;
            var h = hosting[i];
            var isWindow = h?.IsWindow ?? false;
            var places = h is not null && h.Uses.Overlaps(hostedClasses);
            if (!isWindow && !places) continue;

            XDocument doc;
            try { doc = SafeXml.Load(files[i]); }
            catch (Exception) { continue; }
            if (doc.Root is null) continue;

            ApplyAliases(doc);
            doc.AddAnnotation(new SourceFile(rels[i]));

            if (isWindow)
            {
                var project = ProjectOf(rels[i]);
                foreach (var property in HostInherited)
                {
                    if (!declared.TryGetValue((project, property), out var list))
                        declared[(project, property)] = list = [];
                    list.Add(_styles.ResolveEx(doc.Root, property, inheritable: false));
                }
            }

            if (!places) continue;
            foreach (var el in doc.Root.Descendants())
            {
                if (el.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;
                if (ClrClassOf(el) is not { } cls || !hostedClasses.Contains(cls)) continue;
                foreach (var property in HostInherited)
                {
                    if (!placements.TryGetValue((cls, property), out var list))
                        placements[(cls, property)] = list = [];
                    // 這裡 InheritedFromHost 還是 null：走到宿主檔案的根元素就停，根元素自己是不是視窗之後再看
                    list.Add(new Placement(rels[i], h!, _styles.ResolveEx(el, property, inheritable: true)));
                }
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
                    "which window hosts this control (no XAML file in the project places it, so the host is unknown); " +
                    "set defaultFontFamily/defaultFontSize in xamlfit.config.json to pin it");
            }

            consensus[key] = Pinned(result, key.Property);
        }

        Resolved ConsensusFor(string file, string property) =>
            consensus.TryGetValue((ProjectOf(file), property), out var answer) ? answer : Resolved.NotSet;

        var memo = new Dictionary<(string Class, string Property), Resolved>();

        // 順著「誰放它」往上找到視窗。回 null = 沒有任何 XAML 放它，退回視窗的共識
        Resolved? FromPlacements(string cls, string property, HashSet<string> visiting)
        {
            if (memo.TryGetValue((cls, property), out var known)) return known;
            if (!placements.TryGetValue((cls, property), out var list)) return null;
            if (!visiting.Add(cls))
                return Resolved.Unknown($"<{cls}> is placed inside itself (the hosting chain is circular), so the {property} it inherits is unknown");

            var answers = new List<(string File, Resolved R)>();
            foreach (var p in list)
            {
                var r = p.Local;
                if (r.IsUnreadable)
                    r = Resolved.Unknown($"where <{cls}> is placed ({p.File}), {property} is not statically readable: {r.Why}");
                else if (r.Kind == ResolveKind.NotSet)
                {
                    if (p.Host.IsWindow) r = Resolved.NotSet;
                    else if (p.Host.Class is { } hostClass && hostedClasses.Contains(hostClass))
                        r = FromPlacements(hostClass, property, visiting) ?? ConsensusFor(p.File, property);
                    else
                        r = Resolved.Unknown($"<{cls}> is placed in {p.File}, whose root <{p.Host.RootName}> is not a window, so the {property} it inherits is unknown");
                }
                answers.Add((p.File, r));
            }
            visiting.Remove(cls);

            Resolved result;
            if (answers.FirstOrDefault(a => a.R.IsUnreadable).R is { IsUnreadable: true } unknown)
                result = unknown;
            else if (answers.All(a => a.R.Kind == ResolveKind.NotSet))
                result = Resolved.NotSet;
            else if (answers.All(a => a.R.IsValue && a.R.Value == answers[0].R.Value))
                result = answers[0].R;
            else
            {
                var values = string.Join(", ", answers.Select(a => $"{a.File}: {(a.R.IsValue ? a.R.Value : "(not set)")}").Distinct().Take(4));
                result = Resolved.Unknown(
                    $"the files that place <{cls}> give it different {property} values ({values}); " +
                    "set defaultFontFamily/defaultFontSize in xamlfit.config.json to pin it");
            }

            return memo[(cls, property)] = Pinned(result, property);
        }

        _styles.InheritedFromHost = (element, property) =>
        {
            var root = element.Document?.Root;
            if (root is null || IsWindowRoot(root)) return Resolved.NotSet;

            var file = element.Document?.Annotation<SourceFile>()?.Path ?? "";
            var cls = ((string?)root.Attribute(XClass))?.Trim();
            return (cls is not null ? FromPlacements(cls, property, []) : null) ?? ConsensusFor(file, property);
        };
    }

    /// <summary>類別（x:Class）→ 放它的檔案（相對路徑）。ComputeHostFonts 算出來，Rescan 用。</summary>
    private Dictionary<string, List<string>> _placedIn = new(StringComparer.Ordinal);

    /// <summary>
    /// 給 <see cref="WidthResolver.PlacementsOf"/>：這份檔案的根被放在哪些元素裡。放它的檔案每次 Rescan 只載入一次
    ///（照第二趟同樣的方式：別名、來源檔、外部佈景），跨檔案的解析就用正式的樣式索引與內距表。
    /// </summary>
    private IReadOnlyList<(XElement Element, string File)>? PlacementsOf(XElement root, Dictionary<string, XDocument?> cache)
    {
        var cls = ((string?)root.Attribute(XClass))?.Trim();
        if (cls is null || !_placedIn.TryGetValue(cls, out var hosts)) return null;

        var found = new List<(XElement, string)>();
        foreach (var rel in hosts)
        {
            if (!cache.TryGetValue(rel, out var doc))
            {
                var i = _rels.IndexOf(rel);
                try { doc = i < 0 ? null : SafeXml.Load(_files[i], lineInfo: true); }
                catch (Exception) { doc = null; }
                if (doc?.Root is not null)
                {
                    ApplyAliases(doc);
                    doc.AddAnnotation(new SourceFile(rel));
                    if (!_options.TrustDefaultChrome && ThemeFor(rel, _themes) is { } themeName)
                        doc.AddAnnotation(new ExternalTheme(themeName, ProfileFor(rel, _themes)));
                }
                cache[rel] = doc;
            }
            if (doc?.Root is null) continue;
            foreach (var el in doc.Root.Descendants())
                if (!el.Name.LocalName.Contains('.', StringComparison.Ordinal) && ClrClassOf(el) == cls)
                    found.Add((el, rel));
        }
        return found;
    }

    /// <summary>使用者指定了預設字型：家族與字級的不一致以使用者說的為準。</summary>
    private Resolved Pinned(Resolved result, string property) =>
        result.IsUnreadable && _options.DefaultFontPinned && property is "FontFamily" or "FontSize" ? Resolved.NotSet : result;

    /// <summary>一份檔案在「誰裝誰」的圖裡的位置：根元素的 x:Class、是不是視窗、它放了專案裡的哪些類別。</summary>
    private sealed record HostingInfo(string? Class, bool IsWindow, string RootName, HashSet<string> Uses);

    /// <summary>一個放了 UserControl 的地方：在哪個檔案、那個檔案的根是什麼、在放它的元素上算出來的繼承值（到根為止）。</summary>
    private sealed record Placement(string File, HostingInfo Host, Resolved Local);

    private static readonly XName XClass = XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml");

    private static HostingInfo HostingOf(XElement root)
    {
        var uses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var el in root.Descendants())
        {
            if (el.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;
            if (ClrClassOf(el) is { } full) uses.Add(full);
        }
        return new(((string?)root.Attribute(XClass))?.Trim(), IsWindowRoot(root), root.Name.LocalName, uses);
    }

    /// <summary>元素的完整類別名（命名空間是 <c>clr-namespace:</c> 時）。assembly 不比 —— x:Class 也沒寫它。</summary>
    private static string? ClrClassOf(XElement el)
    {
        var ns = el.Name.NamespaceName;
        if (!ns.StartsWith("clr-namespace:", StringComparison.Ordinal)) return null;
        var end = ns.IndexOf(';');
        var clr = (end < 0 ? ns["clr-namespace:".Length..] : ns["clr-namespace:".Length..end]).Trim();
        return clr.Length == 0 ? el.Name.LocalName : clr + "." + el.Name.LocalName;
    }

    /// <summary>
    /// 使用者宣告「垂直捲軸不會出現」的元素：當成 VerticalScrollBarVisibility="Hidden"（不佔寬度）。
    /// ScrollViewer 改它自己的屬性；清單（ListBox、TreeView…）改附加屬性 ScrollViewer.VerticalScrollBarVisibility。
    /// </summary>
    private static void HideScrollBars(XDocument doc, IReadOnlyCollection<string> names)
    {
        if (names.Count == 0 || doc.Root is null) return;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var e in doc.Descendants().ToList())
        {
            var name = (string?)e.Attribute(x + "Name") ?? (string?)e.Attribute("Name");
            if (name is null || !names.Contains(name)) continue;
            foreach (var child in e.Elements().Where(c => c.Name.LocalName.EndsWith(".VerticalScrollBarVisibility", StringComparison.Ordinal)).ToList())
                child.Remove();
            if (e.Name.LocalName.EndsWith("ScrollViewer", StringComparison.Ordinal))
                e.SetAttributeValue("VerticalScrollBarVisibility", "Hidden");
            else
                e.SetAttributeValue("ScrollViewer.VerticalScrollBarVisibility", "Hidden");
        }
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
        GridPanel.Rewrite(doc, _options.GridPanels);
        HideScrollBars(doc, _options.HiddenScrollBars);
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
    private static IEnumerable<ThemeRef> ExternalThemesIn(XElement root, List<string> scannedRels)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var merged in root.Descendants().Where(e => e.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal)))
        {
            foreach (var entry in merged.Elements())
            {
                string? name = null;
                string? packUri = null;

                if (entry.Name.LocalName == "ResourceDictionary" && ChromeTable.IsWpf(entry))
                {
                    if (entry.Attribute("Source")?.Value is { } source)
                    {
                        name = ExternalAssemblyOf(source, scannedRels);
                        packUri = source;
                    }
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

                // 同一個組件合併好幾個字典時，每個都記（MaterialDesign 的 2／3 差在哪一個 Defaults 檔）
                if (!string.IsNullOrEmpty(name) && seen.Add(name + "|" + packUri))
                    yield return new ThemeRef(name, packUri, ChromeTable.IsWpfNamespace(entry.Name.NamespaceName) ? null : entry.Name.NamespaceName);
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
        List<(int File, ThemeRef Theme, bool AppWide)> declarations, List<string> rels, List<string> appDirs)
    {
        var result = new List<ThemeUse>();
        foreach (var (file, reference, appWide) in declarations)
        {
            var theme = reference.Name;
            var declaredIn = rels[file];
            string scope;

            if (!appWide) scope = declaredIn;
            else
            {
                scope = "";
                foreach (var dir in appDirs)
                    if (declaredIn.StartsWith(dir, StringComparison.Ordinal) && dir.Length > scope.Length) scope = dir;
            }

            if (!result.Any(t => t.Theme == theme && t.Scope == scope && t.Source == reference.Source))
                result.Add(new ThemeUse(theme, declaredIn, scope) { Source = reference.Source, Namespace = reference.Namespace });
        }
        return result;
    }

    private static bool Applies(ThemeUse t, string rel)
    {
        var isFileScope = t.Scope.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
        return isFileScope ? t.Scope == rel : rel.StartsWith(t.Scope, StringComparison.Ordinal);
    }

    private readonly Dictionary<string, ThemeProfile?> _profiles = new(StringComparer.Ordinal);

    /// <summary>
    /// 套在這個檔案上的佈景都屬於同一個量過的佈景、而且版本相符時，回傳那份量測；否則 null（不知道）。
    /// ⚠ 範圍裡只要有一個沒量過的佈景（NETworkManager 的 Dragablz），就不知道它改了哪些控制項 —— 整個不用。
    /// </summary>
    private ThemeProfile? ProfileFor(string rel, List<ThemeUse> themes)
    {
        if (_profiles.TryGetValue(rel, out var cached)) return cached;
        var uses = themes.Where(t => Applies(t, rel)).ToList();
        ThemeProfile? result = null;
        if (uses.Count > 0 && uses.All(u => u.Profile is not null) && uses.Select(u => u.Profile).Distinct().Count() == 1)
            result = ThemeProfiles.Builtin().FirstOrDefault(p => p.Id == uses[0].Profile);
        _profiles[rel] = result;
        return result;
    }

    /// <summary>
    /// 每個佈景用法對到哪份量測（同一個範圍裡的用法一起看：MaterialDesign 要合併了 2 或 3 的 Defaults 檔才知道是哪一份）。
    /// 版本從專案檔讀（PackageReference、Directory.Packages.props、packages.config），major.minor 要跟量的一樣。
    /// </summary>
    private static void AssignProfiles(List<ThemeUse> themes, string root)
    {
        var versions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> VersionsOf(string package) =>
            versions.TryGetValue(package, out var v) ? v : versions[package] = PackageVersions(root, package);

        foreach (var scope in themes.GroupBy(t => t.Scope))
        {
            foreach (var use in scope)
            {
                var candidates = ThemeProfiles.Builtin().Where(p => p.Covers(use.Theme, use.Namespace)).ToList();
                if (candidates.Count == 0) { use.ProfileNote = "not measured"; continue; }

                // 同一個範圍裡要合併了指定的字典（MaterialDesign2.Defaults.xaml／MaterialDesign3.Defaults.xaml）
                candidates = candidates.Where(p => p.Requires is null || scope.Any(u =>
                    u.Source?.EndsWith("/" + p.Requires, StringComparison.OrdinalIgnoreCase) == true)).ToList();
                if (candidates.Count != 1)
                {
                    use.ProfileNote = candidates.Count == 0
                        ? "measured only with " + string.Join(" or ", ThemeProfiles.Builtin().Where(p => p.Covers(use.Theme, use.Namespace)).Select(p => p.Requires ?? p.Id).Distinct()) + " merged"
                        : "more than one measured variant could apply";
                    continue;
                }

                var profile = candidates[0];
                var found = VersionsOf(profile.Package);
                if (found.Count == 0) { use.ProfileNote = $"no reference to the {profile.Package} package found in the project files; measured with {profile.Version}"; continue; }
                if (found.Any(v => ThemeProfile.MajorMinorOf(v) != profile.MajorMinor))
                {
                    use.ProfileNote = $"the project references {profile.Package} {string.Join(", ", found.Order())}; measured with {profile.Version}";
                    continue;
                }
                use.Profile = profile.Id;
            }
        }

        // 同一個範圍裡有沒量過的佈景：那個範圍的量測都不用（不知道另一個佈景改了哪些控制項）
        foreach (var scope in themes.GroupBy(t => t.Scope))
            if (scope.FirstOrDefault(u => u.Profile is null) is { } other)
                foreach (var use in scope.Where(u => u.Profile is not null))
                {
                    use.ProfileNote = $"measured, but not used: '{other.Theme}' in the same scope is not";
                    use.Profile = null;
                }
    }

    /// <summary>專案檔裡寫的某個套件的版本（PackageReference／PackageVersion 的 Version、packages.config 的 version）。</summary>
    internal static HashSet<string> PackageVersions(string root, string package)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        try
        {
            // 跟掃 XAML 一樣剪掉 bin、obj、node_modules、點開頭的目錄
            foreach (var ext in (ReadOnlySpan<string>)[".csproj", ".vbproj", ".props", ".targets", ".config"])
                files.AddRange(EnumerateFiltered(root, ext, [])
                    .Where(f => ext != ".config" || Path.GetFileName(f).Equals("packages.config", StringComparison.OrdinalIgnoreCase)));
            // 中央套件管理的檔案常在上層目錄
            for (var dir = Directory.GetParent(Path.GetFullPath(root)); dir is not null && files.Count < 10_000; dir = dir.Parent)
                foreach (var name in (ReadOnlySpan<string>)["Directory.Packages.props", "Directory.Build.props"])
                    if (File.Exists(Path.Combine(dir.FullName, name))) files.Add(Path.Combine(dir.FullName, name));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        foreach (var file in files.Distinct())
        {
            XDocument doc;
            try { doc = SafeXml.Load(file); }
            catch (Exception) { continue; }
            foreach (var el in doc.Descendants())
            {
                var id = (string?)el.Attribute("Include") ?? (string?)el.Attribute("Update") ?? (string?)el.Attribute("id");
                if (!string.Equals(id?.Trim(), package, StringComparison.OrdinalIgnoreCase)) continue;
                var version = (string?)el.Attribute("Version") ?? (string?)el.Attribute("VersionOverride") ?? (string?)el.Attribute("version")
                              ?? el.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value;
                if (string.IsNullOrWhiteSpace(version)) continue;   // 版本寫在中央套件管理檔：那邊的 PackageVersion 會被讀到
                found.Add(version.Trim().Trim('[', ']', '(', ')'));
            }
        }
        return found;
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
            // sr_SP 這種 .NET 不認得的舊代碼：當成一個語言，不要當成來源語言（見 ResxReader.LooksLikeCultureTag）
            return ResxReader.LooksLikeCultureTag(name) ? name : ResxReader.NeutralCulture;
        }
    }

    private static List<CultureStrings> MergeStringTables(
        List<(string Rel, string Culture, Dictionary<string, string> Values)> tables)
    {
        var result = new List<CultureStrings>();
        foreach (var group in tables.GroupBy(t => t.Culture, StringComparer.OrdinalIgnoreCase))
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            var origins = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
            foreach (var t in group)
                foreach (var (k, v) in t.Values)
                    if (merged.TryAdd(k, v)) origins[k] = (t.Rel, 0);   // 字串字典讀的時候沒記行號：只指得出檔案
            result.Add(new CultureStrings(group.Key, merged, string.Join(", ", group.Select(t => t.Rel).Take(3))) { Origins = origins });
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

    private ElementMeasurer? _measurer;

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

        var font = FontSpec.Read(_styles, el, _defaultFont, _options.DefaultFontSize, _language);
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
            Language = font.Language,
            Wrap = textHost is null ? WrapMode.NoWrap : ParseWrap(_styles.Resolve(textHost, "TextWrapping", inheritable: false)),
            // 控制項的字串內容（Button Content="…"）：樣式可能把它畫成大寫（MahApps）
            Casing = textHost is null && local != "Run" ? _styles.CasingOf(el) : TextCasing.AsIs,
            DisplayScales = font.Display ? _options.DisplayScales : null,
            Trim = textHost is null ? TrimMode.None : ParseTrim(_styles.Resolve(textHost, "TextTrimming", inheritable: false)),
            Height = HeightOf(textHost ?? el),
            Width = width,
            SuppressedReason = ignore is { Length: > 0 } ? ignore : null,
            // 理由必填：空理由的 ignore 不生效，但要讓使用者知道它沒生效
            InvalidIgnore = ignore is { Length: 0 },
        };
    }

    /// <summary>
    /// 任何元素的 <c>ToolTip="…"</c>（或 <c>&lt;X.ToolTip&gt;純文字&lt;/X.ToolTip&gt;</c>）。
    ///
    /// ⚠ 0.7.0 之前工具根本不知道它們存在 —— 比「量不到」更糟，因為報告不會提到。
    /// WPF 把字串包進一個 ToolTip 控制項顯示：
    /// - 寬度：快顯跟著內容變寬、不裁切，所以沒宣告 <c>popupMaxWidth</c> 時是「查不到上界」（照樣計入、照樣做翻譯完整度）；
    ///   宣告了就是 popupMaxWidth 扣掉 ToolTip 的內距（LayoutOracle --popups 量的 14）。
    /// - 字型：**不是**擁有者的。WPF 實測：視窗設 Courier New 30，Button 的 ToolTip 是系統字型 12
    ///   （ToolTip 的預設樣式設了 SystemFonts.StatusFont）。專案的隱含 ToolTip 樣式設了字型就用它的。
    /// - 專案的隱含 ToolTip 樣式換了範本或內距：內距不知道 → 量不到。
    /// </summary>
    private TextSite? TryToolTipSite(XElement el, string file)
    {
        var local = el.Name.LocalName;
        if (local.Contains('.', StringComparison.Ordinal) || IsIgnorableNamespace(el.Name.NamespaceName)) return null;
        // ToolTip 元素自己、ToolTipService 之類的不是「某個元素的 ToolTip 屬性」
        if (local.EndsWith("ToolTip", StringComparison.Ordinal)) return null;

        var (source, text, key) = ExtractText(el, "ToolTip");
        if (text is null) return null;

        var sourceText = source == TextSource.Literal ? text
            : key is not null && _sourceStrings.TryGetValue(key, out var known) ? known : null;
        var localizable = source != TextSource.DataBound && BudgetAnalyzer.HasLetter(sourceText ?? text);

        // 字型在一個暫時掛上去的 <X.ToolTip><ToolTip/></X.ToolTip> 上讀：隱含 ToolTip 樣式照常生效，
        // 走到 .ToolTip 屬性元素就停（StyleIndex.ResolveEx），不會繼承到擁有者的字型
        var wpf = XNamespace.Get(WpfNamespace);
        var probe = new XElement(wpf + "ToolTip");
        var holder = new XElement(el.Name.Namespace + $"{local}.ToolTip", probe);
        el.Add(holder);
        FontSpec font;
        bool styled;
        try
        {
            font = FontSpec.Read(_styles, probe, _defaultFont, _options.DefaultFontSize, _language);
            styled = _styles.ImplicitStyleMaySetTemplate(probe, "ToolTip")
                     || _styles.ImplicitSetter(probe, "ToolTip", "Padding").Kind != ResolveKind.NotSet
                     || _styles.ImplicitSetter(probe, "ToolTip", "BorderThickness").Kind != ResolveKind.NotSet;
        }
        finally { holder.Remove(); }

        AvailableWidth width;
        if (_options.PopupMaxWidth is not { } max)
            width = AvailableWidth.NotKnown(WidthStatus.NoDeclaredLimit, "popup content (ToolTip); declare popupMaxWidth to check tooltips");
        else if (styled)
            width = AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "the project's ToolTip style changes its template or padding");
        else
        {
            var tip = _chrome.Lookup(XName.Get("ToolTip", WpfNamespace), WidthResolver.ThemeOf(el));
            width = tip.Kind != ChromeKind.Control
                ? AvailableWidth.NotKnown(WidthStatus.Unmeasurable, $"the chrome of ToolTip is unknown ({tip.Why})")
                : new AvailableWidth(WidthStatus.Known, max - tip.Chrome, $"popupMaxWidth={max:G} (declared) on the ToolTip",
                    WidthResolver.LineOf(el), true, "popup content (ToolTip, popupMaxWidth)");
        }
        if (width.Status == WidthStatus.Known)
            width = FinalFont(width, font, localizable);

        var ignore = IgnoreReason(el);
        return new TextSite
        {
            File = file,
            Line = WidthResolver.LineOf(el),
            Element = "ToolTip",
            Source = source,
            Text = text,
            ResourceKey = key,
            SourceText = sourceText,
            FontFamily = font.Family,
            FontSize = font.Size,
            FontWeight = font.Weight,
            FontStyle = font.Style,
            Language = font.Language,
            DisplayScales = font.Display ? _options.DisplayScales : null,
            Width = width,
            SuppressedReason = ignore is { Length: > 0 } ? ignore : null,
            InvalidIgnore = ignore is { Length: 0 },
        };
    }

    private const string MahAppsNamespace = "http://metro.mahapps.com/winfx/xaml/controls";
    private const string MaterialDesignNamespace = "http://materialdesigninxaml.net/winfx/xaml/themes";
    private const string WpfUiNamespace = "http://schemas.lepo.co/wpfui/2022/xaml";

    /// <summary>
    /// 輸入框的浮水印／提示文字（0.7.0 之前工具不知道它們存在）：MahApps 的 <c>TextBoxHelper.Watermark</c>、
    /// MaterialDesign 的 <c>HintAssist.Hint</c>、WPF-UI 的 <c>ui:TextBox PlaceholderText</c>，
    /// 以及其他函式庫名為 *.Watermark／*.Hint／*.Placeholder 的附加屬性。
    ///
    /// ⚠ 浮水印不是畫在「TextBox 打字的那塊區域」。LayoutOracle --watermark 在真的視窗裡量（字型一律是 TextBox 自己的）：
    /// - MahApps 2.4 的 TextBox：TextBox 外框 − 10，明寫的 Padding 不影響（打字區是 − 14）；
    /// - MaterialDesign 5.3（2、3 都一樣）的 TextBox：外框 − 明寫的左右 Padding（預設 0；打字區是 − 2）；
    /// - WPF-UI 4.3 的 ui:TextBox：外框 − 21.6，Padding 不影響；
    /// - HandyControl 3.5 的一般 TextBox 根本不畫 InfoElement.Placeholder（要它自己的樣式或控制項）。
    /// 量過的組合以外（別的控制項、別的佈景、明寫的 BorderThickness、換了樣式或範本）一律量不到 —— 位置照樣列出來。
    /// </summary>
    private TextSite? TryWatermarkSite(XElement el, string file)
    {
        if (IsIgnorableNamespace(el.Name.NamespaceName) || el.Name.LocalName.Contains('.', StringComparison.Ordinal)) return null;

        var attr = el.Attributes().FirstOrDefault(a =>
            !a.IsNamespaceDeclaration && a.Name.LocalName.Contains('.', StringComparison.Ordinal)
            && (a.Name.LocalName.EndsWith(".Watermark", StringComparison.Ordinal)
                || a.Name.LocalName.EndsWith(".Hint", StringComparison.Ordinal)
                || a.Name.LocalName.EndsWith(".Placeholder", StringComparison.Ordinal)));
        var wpfUiBox = el.Name == XName.Get("TextBox", WpfUiNamespace) && el.Attribute("PlaceholderText") is not null;
        if (attr is null && !wpfUiBox) return null;

        var (source, text, key) = ClassifyValue(wpfUiBox ? el.Attribute("PlaceholderText")!.Value : attr!.Value);
        if (text is null) return null;

        var sourceText = source == TextSource.Literal ? text
            : key is not null && _sourceStrings.TryGetValue(key, out var known) ? known : null;
        var localizable = source != TextSource.DataBound && BudgetAnalyzer.HasLetter(sourceText ?? text);
        var font = FontSpec.Read(_styles, el, _defaultFont, _options.DefaultFontSize, _language);

        var profile = el.Document?.Annotation<ExternalTheme>()?.Profile?.Id ?? "";
        var isWpfTextBox = ChromeTable.IsWpf(el, "TextBox");
        var lib = wpfUiBox ? "WPF-UI"
            : attr!.Name == XName.Get("TextBoxHelper.Watermark", MahAppsNamespace) ? "MahApps"
            : attr.Name == XName.Get("HintAssist.Hint", MaterialDesignNamespace) ? "MaterialDesign"
            : null;

        AvailableWidth Blocked(string why) => AvailableWidth.NotKnown(WidthStatus.Unmeasurable, why);
        AvailableWidth width;
        double? deduction = null;
        string? why = null;
        if (lib is null)
            why = $"where {(attr is null ? "this placeholder" : attr.Name.LocalName)} is drawn has not been measured";
        else if (!wpfUiBox && !isWpfTextBox)
            why = $"where the {lib} watermark of <{el.Name.LocalName}> is drawn has not been measured (only TextBox)";
        else if (lib == "MahApps" && !profile.StartsWith("MahApps.Metro", StringComparison.Ordinal)
                 || lib == "MaterialDesign" && !profile.StartsWith("MaterialDesignThemes", StringComparison.Ordinal)
                 || lib == "WPF-UI" && !profile.StartsWith("WPF-UI", StringComparison.Ordinal))
            why = $"the {lib} watermark needs the {lib} theme this tool has measured, and this file does not use it";
        else if (el.Attribute("Style") is not null || el.Attribute("Template") is not null
                 || el.Elements().Any(e => e.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal) || e.Name.LocalName.EndsWith(".Template", StringComparison.Ordinal))
                 || _styles.ImplicitStyleMaySetTemplate(el, el.Name.LocalName))
            why = "the box has a style or template other than the theme's default";
        else if (_styles.ResolveEx(el, "BorderThickness", inheritable: false).Kind != ResolveKind.NotSet)
            why = "BorderThickness is set, and the watermark was measured only with the theme's border";
        else if (lib == "MaterialDesign")
        {
            var padding = _styles.ResolveEx(el, "Padding", inheritable: false);
            if (padding.Kind == ResolveKind.NotSet) deduction = 0;
            else if (padding.IsValue && TryHorizontal(padding.Value!, out var lr)) deduction = lr;
            else why = "the box's Padding is not statically readable";
        }
        else deduction = lib == "MahApps" ? 10 : 21.6;

        if (why is not null) width = Blocked(why);
        else
        {
            var outer = _width.OuterWidth(el, 0);
            width = outer.Status != WidthStatus.Known
                ? outer
                : FinalFont(outer with { Px = outer.Px - deduction!.Value, Reason = $"{lib} watermark (LayoutOracle --watermark)" }, font, localizable);
        }

        var ignore = IgnoreReason(el);
        return new TextSite
        {
            File = file,
            Line = WidthResolver.LineOf(el),
            Element = "Watermark",
            Source = source,
            Text = text,
            ResourceKey = key,
            SourceText = sourceText,
            FontFamily = font.Family,
            FontSize = font.Size,
            FontWeight = font.Weight,
            FontStyle = font.Style,
            Language = font.Language,
            DisplayScales = font.Display ? _options.DisplayScales : null,
            Width = width,
            SuppressedReason = ignore is { Length: > 0 } ? ignore : null,
            InvalidIgnore = ignore is { Length: 0 },
        };
    }

    /// <summary>
    /// 一般視窗的 <c>Title</c>：Windows 自己畫在標題列上，放不下時用刪節號縮短。
    /// 標題列的排法（圖示、最小化／最大化／關閉按鈕、系統的標題字型）工具沒有建模，所以量不到，
    /// 但位置要列出來 —— 0.7.0 之前工具不知道它存在（寫死的標題連 hard-coded-text 都不會報）。
    /// 狀態是 <see cref="WidthStatus.DrawnByWindows"/>，不是 Unmeasurable：專案沒有任何辦法讓它量得到，
    /// 算進可解析率的分母只會讓每個專案永久掉幾個百分點（實測 Cornea 100% → 95.2%），那就是永遠開火的閘門。
    /// WindowStyle="None"、WindowChrome（本地或樣式設的）：標題由視窗自己的範本畫（或根本不畫），那裡的文字另外是位置 → 不列。
    /// 只認 WPF 的 Window：MetroWindow 這類子類別的標題畫在它自己的範本裡。
    /// </summary>
    private TextSite? TryTitleSite(XElement el, string file)
    {
        if (el.Parent is not null || !ChromeTable.IsWpf(el, "Window")) return null;
        var (source, text, key) = ExtractText(el, "Title");
        if (text is null) return null;

        var style = _styles.ResolveEx(el, "WindowStyle", inheritable: false);
        if (style.IsValue && IsNone(style.Value)) return null;
        if (el.Attribute(XName.Get("WindowChrome.WindowChrome")) is not null
            || el.Elements().Any(e => e.Name.LocalName == "WindowChrome.WindowChrome")
            || _styles.ResolveEx(el, "WindowChrome.WindowChrome", inheritable: false).Kind != ResolveKind.NotSet)
            return null;

        var sourceText = source == TextSource.Literal ? text
            : key is not null && _sourceStrings.TryGetValue(key, out var known) ? known : null;
        var ignore = IgnoreReason(el);
        var localizable = source != TextSource.DataBound && BudgetAnalyzer.HasLetter(sourceText ?? text);
        var measured = _options.WindowTitleBarWindows11 ? Windows11Title(el, style) : null;
        var width = measured ?? AvailableWidth.NotKnown(WidthStatus.DrawnByWindows,
            "drawn by Windows in the title bar, shortened with an ellipsis; the title bar's layout is not modeled" +
            (_options.WindowTitleBarWindows11 ? "" : " (declare windowTitleBar: \"windows11\" to check titles)"));
        if (measured is { Status: WidthStatus.Known })
            width = FinalFont(width, new FontSpec(TitleFont, TitleSize, 400, FontStyleKind.Normal, null), localizable);

        return new TextSite
        {
            File = file,
            Line = WidthResolver.LineOf(el),
            Element = "Window.Title",
            Source = source,
            Text = text,
            ResourceKey = key,
            SourceText = sourceText,
            FontFamily = measured is null ? _defaultFont : TitleFont,
            FontSize = measured is null ? _options.DefaultFontSize : TitleSize,
            Trim = measured is null ? TrimMode.None : TrimMode.CharacterEllipsis,
            DrawnBySystem = measured is not null,
            Width = width,
            SuppressedReason = ignore is { Length: > 0 } ? ignore : null,
            InvalidIgnore = ignore is { Length: 0 },
        };

        static bool IsNone(string? v) => string.Equals(v?.Trim(), "None", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(v?.Trim(), "WindowStyle.None", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Windows 11 畫標題用的字型與大小（實量：跟系統回報的標題字型無關，繁中 Windows 回報 Microsoft JhengHei UI 也一樣）。</summary>
    private const string TitleFont = "Segoe UI";
    private const double TitleSize = 12;

    /// <summary>
    /// Windows 11 的標題列吃掉多少寬度（DIP），鍵是 (WindowStyle, ResizeMode)。
    /// 2026-11-19 在一台 Windows 11 上實量（prototype/TitleBar）：視窗寬 300／400／550／800、顯示縮放 100%／125%／150%／175%，
    /// 一長串 l 的刪節號停在哪裡（比真正的右緣早 1.6～4 DIP，保守），每一組取最大的 —— 全部出自 100%。
    /// 標題文字的寬度：22 句（拉丁、西里爾、希臘、帶重音的字）畫出來的字跟 Segoe UI 12 的線性寬度差在 1 DIP 以內；
    /// GDI 的 hinting 寬度反而寬 7～9 DIP（Windows 11 的標題不是用 GDI 畫的）。
    /// 沒量過的組合（ThreeDBorderWindow 的其他 ResizeMode…）不猜。200% 以上沒量過。
    /// </summary>
    /// 每一組記四種縮放各自的損失（100%、125%、150%、175%），檢查用最大的；報告把四個都寫出來 ——
    /// 回報 3：ZeroCel 的「分割檢視」在 MinWidth 240 下差 2 px，使用者在 125% 看是完整的（125% 只損失 192），
    /// 報告只寫 194 的話使用者不知道該拿哪個縮放去驗。
    /// </summary>
    private static readonly Dictionary<(string Style, string Resize), double[]> Windows11TitleBar = new()
    {
        [("SingleBorderWindow", "CanResize")] = [194.0, 192.0, 192.0, 190.9],
        [("SingleBorderWindow", "CanMinimize")] = [194.0, 192.0, 192.0, 190.9],
        [("SingleBorderWindow", "CanResizeWithGrip")] = [194.0, 192.0, 192.0, 190.9],
        [("SingleBorderWindow", "NoResize")] = [96.0, 93.6, 94.7, 93.1],
        [("ThreeDBorderWindow", "CanResize")] = [200.0, 196.8, 194.7, 194.9],
        [("ToolWindow", "CanResize")] = [76.0, 72.8, 72.7, 72.0],
    };

    private static readonly string[] TitleBarScales = ["100%", "125%", "150%", "175%"];

    /// <summary>宣告了 windowTitleBar: "windows11" 時，一般視窗的標題看得見多寬。這個組合沒量過 → null（照舊 drawn-by-windows）。</summary>
    private AvailableWidth? Windows11Title(XElement window, Resolved style)
    {
        var resize = _styles.ResolveEx(window, "ResizeMode", inheritable: false);
        if (style.IsUnreadable || resize.IsUnreadable)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "WindowStyle or ResizeMode of the window is not statically readable");
        var s = style.IsValue ? style.Value!.Trim().Replace("WindowStyle.", "", StringComparison.Ordinal) : "SingleBorderWindow";
        var r = resize.IsValue ? resize.Value!.Trim().Replace("ResizeMode.", "", StringComparison.Ordinal) : "CanResize";
        if (!Windows11TitleBar.TryGetValue((s, r), out var byScale)) return null;
        var lost = byScale.Max();

        // SizeToContent 讓 Width 失效（坑 6）；可以縮的視窗在 assumeWindowMinWidth 下用 MinWidth
        var sizeToContent = _styles.ResolveEx(window, "SizeToContent", inheritable: false);
        if (sizeToContent.IsUnreadable)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "SizeToContent of the window is not statically readable");
        if (sizeToContent.IsValue && sizeToContent.Value!.Trim() is "Width" or "WidthAndHeight")
            return AvailableWidth.NotKnown(WidthStatus.NoDeclaredLimit, "the window sizes to its content, so its title bar does too");

        static double? Number(Resolved v) =>
            v.IsValue && double.TryParse(v.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        var widthValue = _styles.ResolveEx(window, "Width", inheritable: false);
        if (widthValue.IsUnreadable)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "Width of the window is not statically readable");
        if (Number(widthValue) is not { } w)
            return AvailableWidth.NotKnown(WidthStatus.NoDeclaredLimit, "the window declares no Width");
        var source = $"Width={w:G}";
        if (_options.AssumeWindowMinWidth && r is "CanResize" or "CanResizeWithGrip"
            && Number(_styles.ResolveEx(window, "MinWidth", inheritable: false)) is { } min && min < w)
        {
            w = min;
            source = $"MinWidth={min:G} (assumeWindowMinWidth)";
        }
        // 每個縮放各剩多少：使用者可以拿報告寫的那個縮放自己驗（回報 3）
        var perScale = string.Join(", ", byScale.Select((l, i) =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Max(0, w - l):0.#} at {TitleBarScales[i]}")));
        return new AvailableWidth(WidthStatus.Known, Math.Max(0, w - lost),
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{source} minus the Windows 11 title bar ({s}, {r}); the title has {perScale}, and the narrowest is used"),
            WidthResolver.LineOf(window), true, "window title (windowTitleBar: windows11)");
    }

    /// <summary>Thickness 的左右合計（"5"、"5,2"、"1,2,3,4"）。</summary>
    private static bool TryHorizontal(string thickness, out double sum)
    {
        sum = 0;
        var parts = thickness.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var nums = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out nums[i])) return false;
        switch (nums.Length)
        {
            case 1: sum = nums[0] * 2; return true;
            case 2: sum = nums[0] * 2; return true;
            case 4: sum = nums[0] + nums[2]; return true;
            default: return false;
        }
    }

    /// <summary>
    /// 版面寬度算得出來還不夠 —— 字型也要讀得出來、度量檔裡要有，這個位置才算「量得到」。
    /// 第一版把這兩種情形照樣算成量到了，然後在判定時默默跳過。
    /// </summary>
    private AvailableWidth FinalWidth(XElement el, FontSpec font, bool localizable)
    {
        var width = _width.Resolve(el);
        if (width.Status != WidthStatus.Known) return width;

        if (el.Name.LocalName == "Run" && el.Parent is { } parent)
        {
            width = RunOnItsLine(el, parent, width);
            if (width.Status != WidthStatus.Known) return width;
        }

        return FinalFont(width, font, localizable);
    }

    /// <summary>寬度已經算得出來的位置：字型也要讀得出來、度量檔裡要有。</summary>
    private AvailableWidth FinalFont(AvailableWidth width, FontSpec font, bool localizable)
    {
        if (font.Unreadable is { } why)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, why);

        if (_fonts is not null)
        {
            var lookup = _fonts.Resolve(font.Family, font.Weight, font.Style, _defaultFont, font.Language);
            if (lookup.Chain.Count == 0)
            {
                // 圖示字型上的符號不是翻譯對象 —— 不要為了它叫使用者去匯出 Segoe MDL2 Assets
                if (localizable && lookup.MissingFamily is { } family) _missingFonts.Add(family);
                return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, lookup.MissDescription ?? "no font metrics");
            }
        }

        return width;
    }

    /// <summary>
    /// 一個 Run 在它那一行裡看得見多寬。
    ///
    /// ⚠ 第一版只要 Run 有任何兄弟（連 <c>&lt;LineBreak/&gt;</c> 也算）就整個放棄：QuillNest 的條列說明
    ///   （一個 TextBlock 裡好幾個 Run，用 LineBreak 分行）32 處全部量不到。
    ///
    /// - 一個人佔一行（前後是 LineBreak 或段落頭尾）：就是一行普通的文字。
    /// - 跟別的內容同一行、**不換行**：一行從左邊畫起、右邊被裁，所以它看得見的是整行寬**扣掉它前面的內容**
    ///   （跟水平 StackPanel 同一個規則；前面的 Run 是譯文時，check 會用每個語言自己的譯文重算）。
    ///   前面有寬度算不出來的東西（綁定、Span、InlineUIContainer）就量不到。
    /// - 跟別的內容同一行、**會換行**、沒有高度上限：水平方向裁不到任何字，照整行寬度量。
    ///   有高度上限時要整段一起排才知道幾行 —— 不猜。
    /// - 包在 Hyperlink／Span／Underline 裡、而且是裡面唯一的內容：把那層包裝當成這個 Run 本身
    ///   （這三種不改字寬；實查 Playnite 的「Having issues?」連結就是這樣寫的）。
    ///   Bold／Italic 會改字重與樣式，字型讀取沒有套用它們 → 不解。其他情形（包裝裡還有別的內容）也不解。
    /// </summary>
    private AvailableWidth RunOnItsLine(XElement run, XElement parent, AvailableWidth width)
    {
        while (parent.Name.LocalName is "Hyperlink" or "Span" or "Underline" && OnlyContent(parent) == run
               && parent.Parent is { } outer)
        {
            run = parent;
            parent = outer;
        }

        // 這一行：父元素裡、前後兩個 LineBreak 之間的行內內容。
        // 元素之間的空白在 XAML 裡會變成一個空格（LineBreak 兩側的空白會被修掉）。
        var segment = new List<(XNode Node, bool SpaceBefore)>();
        var found = false;
        var pendingSpace = false;
        foreach (var node in parent.Nodes())
        {
            if (node is XComment or XProcessingInstruction) continue;
            if (node is XText t && string.IsNullOrWhiteSpace(t.Value)) { pendingSpace = true; continue; }
            if (node is XElement e && e.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;   // 屬性元素
            if (node is XElement br && br.Name.LocalName == "LineBreak")
            {
                if (found) break;
                segment.Clear();
                pendingSpace = false;
                continue;
            }
            segment.Add((node, pendingSpace && segment.Count > 0));
            pendingSpace = false;
            if (node == run) found = true;
        }

        var hostIsTextBlock = parent.Name.LocalName.EndsWith("TextBlock", StringComparison.Ordinal);
        if (segment.Count <= 1 && hostIsTextBlock) return width;
        if (!hostIsTextBlock)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "shares its line with other inline content");

        var wraps = ParseWrap(_styles.Resolve(parent, "TextWrapping", inheritable: false)) != WrapMode.NoWrap && !width.WrapDefeated;
        if (wraps)
            return HeightOf(parent) is null
                ? width
                : AvailableWidth.NotKnown(WidthStatus.Unmeasurable,
                    "shares a wrapping paragraph with other inline content, and the paragraph's height is limited");

        if (_measurer is null || _fonts is null)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "shares its line with other inline content");

        var hostFont = FontSpec.Read(_styles, parent, _defaultFont, _options.DefaultFontSize, _language);
        if (hostFont.Unreadable is { } why) return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, why);
        var hostLookup = _fonts.Resolve(hostFont.Family, hostFont.Weight, hostFont.Style, _defaultFont, hostFont.Language);
        var space = TextMeasure.Measure(" ", hostLookup, hostFont.Size);
        if (hostLookup.Chain.Count == 0 || !space.IsReliable)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "shares its line with other inline content");

        var lookupsBefore = _measurer.ResourceLookups;
        double used = 0;
        foreach (var (node, spaceBefore) in segment)
        {
            if (spaceBefore) used += space.WidthPx;
            if (node == run) break;
            if (node is XElement e && e.Name.LocalName == "Run" && _measurer.DesiredWidth(e, 0) is { } w)
                used += w;
            else
                return AvailableWidth.NotKnown(WidthStatus.Unmeasurable,
                    "follows inline content on the same line whose width is unknown");
        }

        if (used == 0) return width;
        return width with
        {
            Px = Math.Max(0, width.Px - used),
            TakenByInlineBefore = used,
            DependsOnNeighbourText = width.DependsOnNeighbourText || _measurer.ResourceLookups > lookupsBefore,
        };
    }

    /// <summary>元素裡唯一的內容（不算空白、註解與屬性元素）；有零個或好幾個時回 null。</summary>
    private static XNode? OnlyContent(XElement e)
    {
        XNode? only = null;
        foreach (var node in e.Nodes())
        {
            if (node is XComment or XProcessingInstruction) continue;
            if (node is XText t && string.IsNullOrWhiteSpace(t.Value)) continue;
            if (node is XElement p && p.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;
            if (only is not null) return null;
            only = node;
        }
        return only;
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

    /// <summary>屬性值是字面文字、資源字串還是綁定。</summary>
    private (TextSource, string?, string?) ClassifyValue(string raw)
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

    private (TextSource, string?, string?) ExtractText(XElement el, string property)
    {
        var local = el.Name.LocalName;

        if (el.Attribute(property)?.Value is { } raw)
            return ClassifyValue(raw);

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
