using System.Globalization;
using XamlFit.Core.Analysis;
using XamlFit.Core.Fonts;

namespace XamlFit.Cli;

/// <summary>budget 與 check 共用的旗標。</summary>
internal sealed class CommonOptions
{
    public string? Root { get; set; }
    public string? CsvPath { get; set; }
    public string? JsonPath { get; set; }
    public string? SitesPath { get; set; }
    public string? MetricsPath { get; set; }
    public string DefaultFont { get; set; } = "Segoe UI";
    public double DefaultSize { get; set; } = 12;
    /// <summary>⚠ budget 是匯出不是閘門，預設 0（只報告）；check 是閘門，預設 60。
    /// 永遠開火的閘門會被關掉。</summary>
    public double MinCoverage { get; set; } = 0;
    public double MarginPercent { get; set; } = 5;
    public List<string> ResxDirs { get; } = [];
    public List<string> Cultures { get; } = [];
    public List<string> Exclude { get; } = [];
    public List<string> AbsentFonts { get; } = [];
    public Dictionary<string, double> Chrome { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> DefaultFontByCulture { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> TreatAs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, XamlFit.Core.Xaml.GridPanel> GridPanels { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> ControlWidths { get; } = new(StringComparer.Ordinal);
    public HashSet<string> HiddenScrollBars { get; } = new(StringComparer.Ordinal);
    public List<double>? DisplayScales { get; set; }
    public string? SourceCulture { get; set; }

    /// <summary>沒寫 xml:lang 的元素的語言；"culture" = 跟著正在檢查的語言。</summary>
    public string ElementLanguage { get; set; } = XamlFit.Core.Fonts.FontFallback.DefaultLanguage;

    public bool ElementLanguageFollowsCulture =>
        ElementLanguage.Equals("culture", StringComparison.OrdinalIgnoreCase);

    /// <summary>掃描來源語言時用的元素語言。</summary>
    public string SourceElementLanguage =>
        ElementLanguageFollowsCulture ? SourceCulture ?? XamlFit.Core.Fonts.FontFallback.DefaultLanguage : ElementLanguage;

    /// <summary>專案套了外部佈景也照樣用內建的內距表。</summary>
    public bool AssumeDefaultTheme { get; set; }

    /// <summary>可以縮小的視窗用 MinWidth 當上界（設定檔 assumeWindowMinWidth／--assume-window-min-width）。</summary>
    public bool AssumeWindowMinWidth { get; set; }

    /// <summary>快顯最寬多寬（設定檔 popupMaxWidth／--popup-max-width）。null = 不檢查快顯。</summary>
    public double? PopupMaxWidth { get; set; }

    /// <summary>CSV 不做公式中和。餵進翻譯平台 API、需要逐位元組相同的原文時用。</summary>
    public bool CsvRaw { get; set; }
    public bool Pseudo { get; set; }

    /// <summary>只檢查來源語言自己（寫死的文字、中性語言的資源值），不需要任何譯文。</summary>
    public bool SourceOnly { get; set; }
    public string FailOn { get; set; } = "fail";
    public string? BaselinePath { get; set; }
    public string? WriteBaselinePath { get; set; }
    public string? ConfigPath { get; set; }
    public string? SarifPath { get; set; }

    /// <summary>--md：Markdown 報告（給 PR 留言用）。</summary>
    public string? MdPath { get; set; }
    public bool MsBuild { get; set; }

    /// <summary>旗標有沒有被明寫。⚠ 設定檔只能補旗標**沒給**的值，不能蓋掉旗標 ——
    /// 命令列是當下的意圖，設定檔是預設值。</summary>
    private readonly HashSet<string> _explicitFlags = new(StringComparer.Ordinal);

    public bool WasGiven(string flag) => _explicitFlags.Contains(flag);

    /// <summary>只屬於 budget 的旗標。</summary>
    public static readonly string[] BudgetOnly = ["--csv", "--csv-raw", "--sites"];

    /// <summary>只屬於 check 的旗標。</summary>
    public static readonly string[] CheckOnly = ["--baseline", "--write-baseline", "--pseudo", "--source-only", "--sarif", "--md", "--msbuild", "--fail-on"];

    /// <summary>
    /// 給了別的子指令才有的旗標：回傳錯誤訊息（exit 2）。
    /// ⚠ 第一版兩個子指令共用一份解析，<c>check --sites x.csv</c> 照收不誤、什麼都沒寫 ——
    ///   跟設定檔裡沒作用的鍵是同一種問題：寫了以為有生效。對照腳本因此讀到一份舊的檔案。
    /// </summary>
    public string? ForeignFlag(string command, string[] foreign) =>
        foreign.FirstOrDefault(WasGiven) is { } flag ? $"{flag} is not an option of {command}" : null;

    public static CommonOptions Parse(string[] args, out string? error)
    {
        var o = new CommonOptions();
        string? err = null;   // out 參數不能被區域函式捕捉，所以先用區域變數

        var endOfOptions = false;   // 「--」之後的都是路徑（GitHub Action 把路徑放在 -- 後面：叫 --version 的資料夾也不會被當成旗標）
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!endOfOptions && a == "--") { endOfOptions = true; continue; }
            if (endOfOptions)
            {
                if (o.Root is not null) { error = $"unexpected extra argument: {a}"; return o; }
                o.Root = a;
                continue;
            }
            if (a.StartsWith("--", StringComparison.Ordinal)) o._explicitFlags.Add(a);

            string? Next(string flag)
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                { err = $"{flag} needs a value"; return null; }
                return args[++i];
            }

            switch (a)
            {
                case "--csv": o.CsvPath = Next(a); break;
                case "--json": o.JsonPath = Next(a); break;
                case "--sites": o.SitesPath = Next(a); break;
                case "--metrics": o.MetricsPath = Next(a); break;
                case "--default-font": o.DefaultFont = Next(a) ?? o.DefaultFont; o._defaultFontPinned = true; break;
                case "--resx": if (Next(a) is { } rd) o.ResxDirs.Add(rd); break;
                case "--culture": if (Next(a) is { } cu) o.Cultures.Add(cu); break;
                case "--baseline": o.BaselinePath = Next(a); break;
                case "--write-baseline": o.WriteBaselinePath = Next(a); break;
                case "--pseudo": o.Pseudo = true; break;
                case "--source-only": o.SourceOnly = true; break;
                case "--csv-raw": o.CsvRaw = true; break;
                case "--config": o.ConfigPath = Next(a); break;
                case "--sarif": o.SarifPath = Next(a); break;
                case "--md": o.MdPath = Next(a); break;
                case "--msbuild": o.MsBuild = true; break;
                case "--assume-default-theme": o.AssumeDefaultTheme = true; break;
                case "--assume-window-min-width": o.AssumeWindowMinWidth = true; break;
                case "--popup-max-width":
                    if (!double.TryParse(Next(a), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var popupMax)
                        || !double.IsFinite(popupMax) || popupMax < 1 || popupMax > 4000)
                    { err = "--popup-max-width takes a width in DIP between 1 and 4000"; error = err; return o; }
                    o.PopupMaxWidth = popupMax; break;
                case "--source-culture": o.SourceCulture = Next(a); break;

                case "--fail-on":
                    var fo = Next(a);
                    if (fo is not ("fail" or "warn")) { err = "--fail-on takes 'fail' or 'warn'"; error = err; return o; }
                    o.FailOn = fo; break;

                case "--default-size":
                    if (!TryNumber(Next(a), 1, 400, out var ds)) { err = "--default-size takes a number between 1 and 400"; error = err; return o; }
                    o.DefaultSize = ds; break;

                case "--min-coverage":
                    if (!TryNumber(Next(a), 0, 100, out var mc)) { err = "--min-coverage takes a number between 0 and 100"; error = err; return o; }
                    o.MinCoverage = mc; break;

                case "--margin":
                    if (!TryNumber(Next(a), 0, 100, out var mg)) { err = "--margin takes a number between 0 and 100"; error = err; return o; }
                    o.MarginPercent = mg; break;

                default:
                    if (a.StartsWith('-')) { error = $"unknown argument: {a}"; return o; }
                    if (o.Root is not null) { error = $"unexpected extra argument: {a}"; return o; }

                    o.Root = a;
                    break;
            }

            if (err is not null) { error = err; return o; }
        }

        error = err;
        return o;
    }

    // ⚠ NaN 對任何比較都是 false，範圍檢查擋不住它；IsFinite 一併擋掉 Infinity。
    //   （這條來自 XamlContrast 的 --min-coverage，同一個坑。）
    private static bool TryNumber(string? v, double min, double max, out double value)
    {
        value = 0;
        return v is not null
            && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value)
            && value >= min && value <= max;
    }

    /// <summary>
    /// 套用 <c>xamlfit.config.json</c>。
    ///
    /// ⚠ 只補旗標**沒給**的值。命令列是當下的意圖，設定檔是預設值 ——
    ///   反過來會讓使用者在命令列寫了東西卻沒生效，而且不會有任何提示。
    /// ⚠ 設定檔寫錯要 exit 2 並指出欄位名，不能靜默忽略。
    /// </summary>
    public static int ApplyConfig(CommonOptions o)
    {
        var path = o.ConfigPath;
        if (path is null)
        {
            var beside = Path.Combine(o.Root ?? ".", FitConfig.DefaultFileName);
            if (!File.Exists(beside)) return 0;      // 沒有設定檔是正常的
            path = beside;
        }
        else if (!File.Exists(path))
        {
            Console.Error.WriteLine($"config file not found: {path}");
            return 2;
        }

        var (config, problems) = FitConfig.Load(path);
        if (problems.Count > 0)
        {
            Console.Error.WriteLine($"{path} has {problems.Count} problem(s):");
            foreach (var p in problems) Console.Error.WriteLine($"  {p}");
            return 2;
        }
        if (config is null) return 2;

        Console.Error.WriteLine($"config: {path}");

        // ⚠ 設定檔裡的相對路徑是**相對於設定檔**的，不是相對於目前的工作目錄 ——
        //   否則同一份設定檔在本機（從 repo 根跑）與 CI（從別的目錄跑）會指到不同的地方。
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string Resolve(string p) => Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(baseDir, p));

        if (!o.WasGiven("--default-font") && config.DefaultFontFamily is { } fam) { o.DefaultFont = fam; o._defaultFontPinned = true; }
        if (!o.WasGiven("--default-size") && config.DefaultFontSize is { } size) o.DefaultSize = size;
        if (!o.WasGiven("--margin") && config.MarginPercent is { } margin) o.MarginPercent = margin;
        if (!o.WasGiven("--min-coverage") && config.MinCoverage is { } cov) o.MinCoverage = cov;
        if (!o.WasGiven("--metrics") && config.Metrics is { } metrics) o.MetricsPath = Resolve(metrics);
        if (!o.WasGiven("--assume-default-theme") && config.AssumeDefaultTheme is { } assume) o.AssumeDefaultTheme = assume;
        if (!o.WasGiven("--assume-window-min-width") && config.AssumeWindowMinWidth is { } minWidth) o.AssumeWindowMinWidth = minWidth;
        if (!o.WasGiven("--popup-max-width") && config.PopupMaxWidth is { } popupMaxWidth) o.PopupMaxWidth = popupMaxWidth;

        if (!o.WasGiven("--source-culture") && config.SourceCulture is { } sourceCulture) o.SourceCulture = sourceCulture;
        if (config.ElementLanguage is { } elementLanguage) o.ElementLanguage = elementLanguage;
        if (config.DefaultFontByCulture is { Count: > 0 } byCulture)
            foreach (var (culture, font) in byCulture) o.DefaultFontByCulture[culture] = font;

        if (config.Exclude is { Count: > 0 } excl) o.Exclude.AddRange(excl);
        if (config.AbsentFonts is { Count: > 0 } absent) o.AbsentFonts.AddRange(absent);
        if (config.Chrome is { Count: > 0 } chrome)
            foreach (var (name, value) in chrome) o.Chrome[name] = value;
        if (config.TreatAs is { Count: > 0 } treatAs)
            foreach (var (from, to) in treatAs) o.TreatAs[from] = to;
        if (config.GridPanels is { Count: > 0 } gridPanels)
            foreach (var (name, p) in gridPanels)
                o.GridPanels[name] = new XamlFit.Core.Xaml.GridPanel(p.ColumnCount, p.StarColumns, p.AutoLayoutColumns);
        if (config.HiddenScrollBars is { Count: > 0 } hiddenBars)
            foreach (var name in hiddenBars) o.HiddenScrollBars.Add(name);
        if (config.ControlWidths is { Count: > 0 } controlWidths)
            foreach (var (name, width) in controlWidths) o.ControlWidths[name] = width;
        if (config.DisplayScales is { Count: > 0 } scales) o.DisplayScales = [.. scales.Distinct().Order()];

        // 清單型的：旗標給了就不從設定檔補（不要混成一個難以預測的聯集）
        if (o.ResxDirs.Count == 0 && config.Resx is { Count: > 0 } resx) o.ResxDirs.AddRange(resx.Select(Resolve));
        if (o.Cultures.Count == 0 && config.Cultures is { Count: > 0 } cultures) o.Cultures.AddRange(cultures);

        return 0;
    }

    /// <summary>
    /// 來源語言的預設字型。優先序：<c>--default-font</c>／設定檔明寫的 &gt; 來源語言的系統字型 &gt; Segoe UI。
    /// 要在 <see cref="ApplyConfig"/> 之後呼叫。
    /// </summary>
    public void ResolveSourceFont()
    {
        if (_defaultFontPinned || SourceCulture is null) return;
        if (CultureFonts.DefaultFor(SourceCulture, DefaultFontByCulture) is { } font) DefaultFont = font;
    }

    private bool _defaultFontPinned;

    /// <summary>使用者有沒有明確指定預設字型（旗標或設定檔）。來源語言不算 —— 那只是在說 Windows 是哪個語言。</summary>
    public bool DefaultFontPinned => _defaultFontPinned;

    public static FontResolver LoadFonts(CommonOptions o, out string? error)
    {
        error = null;

        string? path;
        if (o.MetricsPath is not null)
        {
            // ⚠ 明確指定的度量檔不存在是輸入錯誤（exit 2）。第一版會默默退回去找預設檔名，
            //   於是打錯路徑的人拿到的是用**別的**度量檔算出來的結果，而且沒有任何提示。
            if (!File.Exists(o.MetricsPath))
            {
                error = $"metrics file not found: {o.MetricsPath}";
                return new FontResolver([]);
            }
            path = o.MetricsPath;
        }
        else
        {
            // 找 xamlfit.fonts.json.gz 或 xamlfit.fonts.json（先 <root> 旁邊、再目前的資料夾；兩個都有用比較新的）
            path = MetricsFile.Find(o.Root ?? ".", ".") ?? MetricsFile.DefaultFileName;
        }

        if (!File.Exists(path))
        {
            // 沒有度量檔不是致命錯誤 —— 所有位置會變成 unmeasurable，
            // 可解析率歸零，然後 exit 3。使用者看到的是「結果不可信」而不是一堆假的零。
            Console.Error.WriteLine(
                "no metrics file found (looked for xamlfit.fonts.json.gz and xamlfit.fonts.json); run 'xamlfit fonts export' first");
            return new FontResolver([], o.AbsentFonts);
        }

        try
        {
            var mf = MetricsFile.Load(path);
            Console.Error.WriteLine($"metrics: {path} ({mf.Fonts.Count} faces, generated {mf.GeneratedOn})");
            return new FontResolver(mf.ToMetrics(), o.AbsentFonts, mf.InstalledFamilies);
        }
        catch (Exception ex)
        {
            error = $"cannot read metrics file {path}: {ex.Message}";
            return new FontResolver([]);
        }
    }

    public static string ToolVersion() =>
        typeof(CommonOptions).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion is { } v
                ? (v.IndexOf('+') > 0 ? v[..v.IndexOf('+')] : v)
                : "0.0.0-dev";
}
