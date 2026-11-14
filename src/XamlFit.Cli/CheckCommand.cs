using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Report;
using XamlFit.Core.Xaml;

namespace XamlFit.Cli;

internal static class CheckCommand
{
    public static int Run(string[] args)
    {
        var opts = CommonOptions.Parse(args, out var error);
        if (error is not null) { Console.Error.WriteLine(error); return 2; }
        if (opts.Root is null) { Console.Error.WriteLine("check needs a root directory"); return 2; }
        if (!Directory.Exists(opts.Root)) { Console.Error.WriteLine($"not a directory: {opts.Root}"); return 2; }

        // check 是閘門，所以覆蓋率下限預設是 60（budget 是匯出，預設 0）
        if (!opts.WasGiven("--min-coverage")) opts.MinCoverage = 60;

        // ⚠ 設定檔要在「必填參數檢查」**之前**套用。
        //   反過來的話，設定檔裡寫了 resx 也永遠不會生效 —— 檢查會先把人擋掉。
        if (CommonOptions.ApplyConfig(opts) is var cfgCode and not 0) return cfgCode;

        if (!opts.Pseudo && opts.ResxDirs.Count == 0)
        {
            Console.Error.WriteLine(
                "check needs translations: pass --resx <dir>, set \"resx\" in xamlfit.config.json, " +
                "or use --pseudo to simulate expansion");
            return 2;
        }

        var fonts = CommonOptions.LoadFonts(opts, out var fontError);
        if (fontError is not null) { Console.Error.WriteLine(fontError); return 2; }

        var chrome = ChromeTable.Builtin();
        var scanner = new XamlScanner(chrome, new ScanOptions
        {
            DefaultFontFamily = opts.DefaultFont,
            DefaultFontSize = opts.DefaultSize,
            ExcludeGlobs = opts.Exclude,
        }, fonts);

        var scan = scanner.Scan(opts.Root);
        var budget = new BudgetAnalyzer(fonts, opts.DefaultFont).Analyze(scan, chrome.Theme);

        var cultures = new List<CultureStrings>();
        foreach (var dir in opts.ResxDirs)
        {
            var found = ResxReader.ReadDirectory(dir);
            if (found.Count == 0) Console.Error.WriteLine($"no .resx files under {dir}");
            cultures.AddRange(found);
        }

        if (opts.Cultures.Count > 0)
            cultures = cultures.Where(c => opts.Cultures.Contains(c.Culture, StringComparer.OrdinalIgnoreCase)).ToList();

        var result = new CheckAnalyzer(fonts, opts.DefaultFont, opts.MarginPercent)
            .Analyze(scan, budget, cultures, opts.Pseudo);

        var all = result.Findings;

        // ===== baseline =====
        if (opts.WriteBaselinePath is not null)
        {
            File.WriteAllText(opts.WriteBaselinePath,
                Baseline.FromFindings(all, DateOnly.FromDateTime(DateTime.Now)).ToJson());
            Console.WriteLine($"wrote baseline with {all.Count} known finding(s) → {opts.WriteBaselinePath}");
            return 0;
        }

        var reported = all;
        var baselined = 0;
        if (opts.BaselinePath is not null)
        {
            if (!File.Exists(opts.BaselinePath))
            { Console.Error.WriteLine($"baseline not found: {opts.BaselinePath}"); return 2; }

            try
            {
                var known = Baseline.Parse(File.ReadAllText(opts.BaselinePath)).AsSet();
                reported = all.Where(f => !known.Contains(f.BaselineKey)).ToList();
                baselined = all.Count - reported.Count;
            }
            catch (Exception ex) { Console.Error.WriteLine($"cannot read baseline: {ex.Message}"); return 2; }
        }

        // ===== console =====
        var s = result.Summary;
        Console.WriteLine($"xamlfit check — {s.Budget.Files} XAML file(s), {s.Budget.MeasuredStrings} string(s) with a budget");
        Console.WriteLine(opts.Pseudo
            ? "  mode             pseudo (simulated expansion — reports risk, never fails the build)"
            : $"  cultures         {string.Join(", ", s.Cultures.Where(c => c != ResxReader.NeutralCulture))}");
        Console.WriteLine($"  measurements     {s.StringsChecked}");
        // ⚠ 解析失敗的檔案一定要講。一個壞掉的 XAML 靜默消失 = 謊報健康
        //   （示範專案的註解裡放了連續兩個減號，整份解析失敗，而 check 模式一聲不吭）。
        if (s.Budget.ParseErrors > 0)
            Console.Error.WriteLine($"  !! {s.Budget.ParseErrors} file(s) failed to parse and were not analysed");
        if (s.MissingTranslations > 0) Console.WriteLine($"  missing strings  {s.MissingTranslations}  (key has no value in that culture)");
        if (s.Suppressed > 0) Console.WriteLine($"  suppressed       {s.Suppressed}");
        if (s.Budget.InvalidIgnores > 0)
            Console.Error.WriteLine(
                $"  !! {s.Budget.InvalidIgnores} xamlfit-ignore comment(s) have no reason and were NOT applied");
        if (baselined > 0) Console.WriteLine($"  in baseline      {baselined}");
        Console.WriteLine($"  resolution       {s.Budget.Resolution:F1}%");

        foreach (var kind in new[] { "overflow", "vertical-clip", "trimmed", "tight", "reflow" })
            if (s.Counts.TryGetValue(kind, out var n) && n > 0)
                Console.WriteLine($"    {kind,-14} {n}");

        if (reported.Count > 0)
        {
            Console.WriteLine();
            foreach (var f in reported.Take(40))
            {
                Console.WriteLine(
                    $"  {FitFinding.SeveritySlug(f.Severity),-4} {FitFinding.KindSlug(f.Kind),-13} " +
                    $"[{f.Culture}] {f.Site}");
                Console.WriteLine(
                    $"       {Trunc(f.Text, 60)}");
                Console.WriteLine(
                    $"       needs {f.ActualPx:F1} px, has {f.LimitPx:F1} px" +
                    (f.OverflowPx > 0 ? $" — over by {f.OverflowPx:F1} px" : "") +
                    (f.Warning is not null ? $"  ({f.Warning})" : ""));
            }
            if (reported.Count > 40) Console.WriteLine($"  … and {reported.Count - 40} more");
        }

        // ⚠ 退出碼要先算出來：MSBuild 的 XF0003（結果不可信）需要它，
        //   而那一行必須跟其他診斷一起出現在同一次輸出裡。
        var gate = opts.FailOn == "warn" ? FitSeverity.Warn : FitSeverity.Fail;
        var exitCode = DecideExit(s, reported, opts, gate);

        if (opts.JsonPath is not null)
        {
            File.WriteAllText(opts.JsonPath, CheckReport.ToJson(result, reported, CommonOptions.ToolVersion()));
            Console.WriteLine($"\nwrote {opts.JsonPath}");
        }

        if (opts.SarifPath is not null)
        {
            File.WriteAllText(opts.SarifPath, CheckDiagnostics.ToSarif(reported, CommonOptions.ToolVersion()));
            Console.WriteLine($"wrote {opts.SarifPath}");
        }

        // MSBuild 的診斷走 stdout，Exec 才逐行抓得到
        if (opts.MsBuild)
            Console.Write(CheckDiagnostics.ToMsBuild(reported, opts.Root, gate, exitCode));

        return exitCode;
    }

    /// <summary>
    /// 退出碼契約。⚠ 判定順序就是語意：**「結果不可信」排在「有沒有 finding」前面** ——
    /// 只看到三成就說「沒問題」是謊報健康。
    /// </summary>
    private static int DecideExit(
        CheckSummary s, IReadOnlyList<FitFinding> reported, CommonOptions opts, FitSeverity gate)
    {
        if (s.Budget.LocalizableSites == 0)
        { Console.Error.WriteLine("\nno localizable text sites found — result is not trustworthy"); return 3; }

        // ⚠ 閘門看的是 Resolution 不是 Coverage。Coverage 的分母含「查不到上界」的位置，
        //   那是靜態分析的天花板 —— 拿它當閘門會對每個專案開火，而永遠開火的閘門會被關掉。
        if (s.Budget.Resolution < opts.MinCoverage)
        {
            Console.Error.WriteLine(
                $"\nresolution {s.Budget.Resolution:F1}% is below --min-coverage {opts.MinCoverage:F0}% — result is not trustworthy");
            return 3;
        }

        if (!opts.Pseudo && s.StringsChecked == 0)
        {
            Console.Error.WriteLine(
                "\nno translated string was actually measured — check --resx and that the keys match");
            return 3;
        }

        var blocking = reported.Count(f => f.Severity >= gate);
        if (blocking > 0)
        {
            Console.Error.WriteLine($"\n{blocking} finding(s) at or above '{opts.FailOn}'");
            return 1;
        }

        return 0;
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
