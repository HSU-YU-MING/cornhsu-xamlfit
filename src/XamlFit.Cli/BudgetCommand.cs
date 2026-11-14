using System.Globalization;
using XamlFit.Core.Analysis;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.Report;
using XamlFit.Core.Xaml;

namespace XamlFit.Cli;

internal static class BudgetCommand
{
    public static int Run(string[] args)
    {
        var opts = CommonOptions.Parse(args, out var error);
        if (error is not null) { Console.Error.WriteLine(error); return 2; }
        if (opts.Root is null) { Console.Error.WriteLine("budget needs a root directory"); return 2; }
        if (!Directory.Exists(opts.Root)) { Console.Error.WriteLine($"not a directory: {opts.Root}"); return 2; }

        var fonts = CommonOptions.LoadFonts(opts, out var fontError);
        if (fontError is not null) { Console.Error.WriteLine(fontError); return 2; }

        var chrome = ChromeTable.Builtin();
        var scanner = new XamlScanner(chrome, new ScanOptions
        {
            DefaultFontFamily = opts.DefaultFont,
            DefaultFontSize = opts.DefaultSize,
        });

        var scan = scanner.Scan(opts.Root);
        var result = new BudgetAnalyzer(fonts, opts.DefaultFont).Analyze(scan, chrome.Theme);
        var s = result.Summary;

        // ===== console =====
        Console.WriteLine($"xamlfit budget — {s.Files} XAML file(s), {s.LocalizableSites} localizable text site(s)");
        Console.WriteLine($"  strings            {s.Strings}");
        Console.WriteLine($"    measured         {s.MeasuredStrings}  (a pixel budget was computed)");
        Console.WriteLine($"    no-declared-limit {s.NoDeclaredLimitStrings}  (no width bound found — NOT a guarantee it cannot clip)");
        Console.WriteLine($"    not-analysed     {s.NotAnalysedStrings}  (template, Auto/* column, unknown chrome)");
        Console.WriteLine($"  sites              {s.LocalizableSites} localizable, {s.SymbolOnlySites} symbol-only (skipped)");
        Console.WriteLine($"    measured         {s.MeasuredSites}");
        Console.WriteLine($"    blocked          {s.BlockedSites}  (Auto/* column, ItemsControl, unknown chrome)");
        Console.WriteLine($"    no width bound   {s.NoDeclaredLimitSites}");
        Console.WriteLine($"  resolution         {s.Resolution:F1}%  (of sites that should yield a number)");
        Console.WriteLine($"  coverage           {s.Coverage:F1}%  (of all localizable sites, informational)");
        if (s.Suppressed > 0) Console.WriteLine($"  suppressed         {s.Suppressed}");
        if (s.InvalidIgnores > 0)
            Console.Error.WriteLine(
                $"  !! {s.InvalidIgnores} xamlfit-ignore comment(s) have no reason and were NOT applied");
        if (s.ParseErrors > 0) Console.WriteLine($"  parse errors       {s.ParseErrors}");
        if (s.MissingFonts.Count > 0)
            Console.WriteLine($"  !! no metrics for: {string.Join(", ", s.MissingFonts)}  (run 'xamlfit fonts export')");

        // 最擠的幾個 —— 英文就只剩幾 px 的位置是還沒翻譯就在倒數的地雷
        var tight = result.Budgets
            .Where(b => b.HeadroomPx is not null)
            .OrderBy(b => b.HeadroomPx)
            .Take(10)
            .ToList();

        if (tight.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("tightest slots (headroom before translation):");
            foreach (var b in tight)
                Console.WriteLine($"  {b.HeadroomPx,7:F1} px   {Trunc(b.Text, 28),-28}  {b.BindingSite}" +
                                  (b.FitsWhenExpanded == false ? "   ← will not fit when translated" : ""));
        }

        if (opts.CsvPath is not null)
        {
            File.WriteAllText(opts.CsvPath, BudgetReport.ToCsv(result, neutralizeFormulas: !opts.CsvRaw));
            Console.WriteLine($"\nwrote {opts.CsvPath}");
        }

        if (opts.JsonPath is not null)
        {
            File.WriteAllText(opts.JsonPath, BudgetReport.ToJson(result, CommonOptions.ToolVersion()));
            Console.WriteLine($"wrote {opts.JsonPath}");
        }

        // ===== 退出碼 =====
        // exit 3：工具不准在只看到一小部分的情況下讓 CI 綠燈。
        if (s.LocalizableSites == 0)
        {
            Console.Error.WriteLine("\nno localizable text sites found — result is not trustworthy");
            return 3;
        }

        // ⚠ 閘門看的是 Resolution 不是 Coverage。Coverage 的分母含「查不到上界」的位置，
        //   那是靜態分析的天花板，任何版本都一樣 —— 拿它當閘門會對每個專案開火，
        //   而永遠開火的閘門會被關掉。
        if (s.Resolution < opts.MinCoverage)
        {
            Console.Error.WriteLine(
                $"\nresolution {s.Resolution:F1}% is below --min-coverage {opts.MinCoverage:F0}% — result is not trustworthy");
            return 3;
        }

        return 0;
    }

    private static string Trunc(string s, int n) =>
        s.Length <= n ? s : s[..(n - 1)] + "…";
}

/// <summary>budget 與 check 共用的旗標。</summary>
internal sealed class CommonOptions
{
    public string? Root { get; set; }
    public string? CsvPath { get; set; }
    public string? JsonPath { get; set; }
    public string? MetricsPath { get; set; }
    public string DefaultFont { get; set; } = "Segoe UI";
    public double DefaultSize { get; set; } = 12;
    /// <summary>⚠ budget 是匯出不是閘門，預設 0（只報告）；check 是閘門，預設 60。
    /// 永遠開火的閘門會被關掉。</summary>
    public double MinCoverage { get; set; } = 0;
    public double MarginPercent { get; set; } = 5;
    public List<string> ResxDirs { get; } = [];
    public List<string> Cultures { get; } = [];

    /// <summary>CSV 不做公式中和。餵進翻譯平台 API、需要逐位元組相同的原文時用。</summary>
    public bool CsvRaw { get; set; }
    public bool Pseudo { get; set; }
    public string FailOn { get; set; } = "fail";
    public string? BaselinePath { get; set; }
    public string? WriteBaselinePath { get; set; }

    public static CommonOptions Parse(string[] args, out string? error)
    {
        var o = new CommonOptions();
        string? err = null;   // out 參數不能被區域函式捕捉，所以先用區域變數

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? Next(string flag)
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                { err = $"{flag} needs a value"; return null; }
                return args[++i];
            }

            switch (a)
            {
                case "--csv": o.CsvPath = Next(a); break;
                case "--json": o.JsonPath = Next(a); break;
                case "--metrics": o.MetricsPath = Next(a); break;
                case "--default-font": o.DefaultFont = Next(a) ?? o.DefaultFont; break;
                case "--resx": if (Next(a) is { } rd) o.ResxDirs.Add(rd); break;
                case "--culture": if (Next(a) is { } cu) o.Cultures.Add(cu); break;
                case "--baseline": o.BaselinePath = Next(a); break;
                case "--write-baseline": o.WriteBaselinePath = Next(a); break;
                case "--pseudo": o.Pseudo = true; break;
                case "--csv-raw": o.CsvRaw = true; break;

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

    public static FontResolver LoadFonts(CommonOptions o, out string? error)
    {
        error = null;
        var path = o.MetricsPath ?? Path.Combine(o.Root ?? ".", "xamlfit.fonts.json");
        if (!File.Exists(path)) path = "xamlfit.fonts.json";

        if (!File.Exists(path))
        {
            // 沒有度量檔不是致命錯誤 —— 所有位置會變成 unmeasurable，
            // 覆蓋率歸零，然後 exit 3。使用者看到的是「結果不可信」而不是一堆假的零。
            Console.Error.WriteLine(
                "no metrics file found (looked for xamlfit.fonts.json); run 'xamlfit fonts export' first");
            return new FontResolver([]);
        }

        try
        {
            var mf = MetricsFile.Parse(File.ReadAllText(path));
            Console.Error.WriteLine($"metrics: {path} ({mf.Fonts.Count} faces, generated {mf.GeneratedOn})");
            return new FontResolver(mf.ToMetrics());
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
