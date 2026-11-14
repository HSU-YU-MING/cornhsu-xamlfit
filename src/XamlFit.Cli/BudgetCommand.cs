using XamlFit.Core.Analysis;
using XamlFit.Core.Report;

namespace XamlFit.Cli;

internal static class BudgetCommand
{
    public static int Run(string[] args)
    {
        var opts = CommonOptions.Parse(args, out var error);
        if (error is not null) { Console.Error.WriteLine(error); return 2; }
        if (opts.ForeignFlag("budget", CommonOptions.CheckOnly) is { } foreign) { Console.Error.WriteLine(foreign); return 2; }
        if (opts.Root is null) { Console.Error.WriteLine("budget needs a root directory"); return 2; }
        if (!Directory.Exists(opts.Root)) { Console.Error.WriteLine($"not a directory: {opts.Root}"); return 2; }

        if (CommonOptions.ApplyConfig(opts) is var cfgCode and not 0) return cfgCode;

        if (Pipeline.Prepare(opts, out var prepareCode) is not { } prepared) return prepareCode;
        var scan = prepared.Scan;

        // 給了 --resx 的話，資源字串「目前有多寬」也算得出來（來源語言的值在中性語言的 .resx 裡）
        var cultures = new List<CultureStrings>(scan.Strings);
        foreach (var dir in opts.ResxDirs)
        {
            if (!Directory.Exists(dir)) { Console.Error.WriteLine($"--resx directory not found: {dir}"); return 2; }
            cultures.AddRange(ResxReader.ReadDirectory(dir));
        }
        CheckAnalyzer.MergeByCulture(cultures).TryGetValue(ResxReader.NeutralCulture, out var neutral);

        // 同一排的兄弟也是 .resx 字串時，要有來源語言的值才量得出它們多寬
        if (neutral is { Count: > 0 } && scan.NeighbourTextUses > 0)
            scan = prepared.Scanner.Rescan(opts.DefaultFont, neutral);

        var result = new BudgetAnalyzer(prepared.Fonts, opts.DefaultFont).Analyze(scan, prepared.Chrome.Theme, neutral);
        var s = result.Summary;

        // ===== console =====
        Console.WriteLine($"xamlfit budget — {s.Files} XAML file(s), {s.LocalizableSites} localizable text site(s)");
        Console.WriteLine($"  strings            {s.Strings}");
        Console.WriteLine($"    measured         {s.MeasuredStrings}  (a pixel budget was computed)");
        Console.WriteLine($"    wraps            {s.WrappingStrings}  (text wraps freely: no horizontal limit)");
        Console.WriteLine($"    no-declared-limit {s.NoDeclaredLimitStrings}  (no width bound found — NOT a guarantee it cannot clip)");
        Console.WriteLine($"    not-analysed     {s.NotAnalysedStrings}  (a bound exists but could not be resolved)");
        Console.WriteLine($"  sites              {s.LocalizableSites} localizable, {s.SymbolOnlySites} symbol-only (skipped)");
        Console.WriteLine($"    measured         {s.MeasuredSites}");
        Console.WriteLine($"    blocked          {s.BlockedSites}  (a bound exists but could not be resolved)");
        Console.WriteLine($"    no width bound   {s.NoDeclaredLimitSites}");
        Console.WriteLine($"    in a template    {s.InTemplateSites}  (width is decided where the template is used)");
        Console.WriteLine($"  resolution         {s.Resolution:F1}%  (measured ÷ (measured + blocked))");
        Console.WriteLine($"  coverage           {s.Coverage:F1}%  (of all localizable sites, informational)");
        if (s.Suppressed > 0) Console.WriteLine($"  suppressed         {s.Suppressed}");

        Pipeline.PrintBlindSpots(scan, s);
        Pipeline.PrintBlockedReasons(s);

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
                Console.WriteLine($"  {b.HeadroomPx,7:F1} px   {Pipeline.Column(b.Text, 28)}  {b.BindingSite}" +
                                  (b.HeadroomPx < 0 ? "   ← does not fit even now, before translation"
                                   : b.FitsWhenExpanded == false ? "   ← will not fit when translated" : ""));
        }

        if (opts.CsvPath is not null)
        {
            if (!Pipeline.TryWrite(opts.CsvPath, BudgetReport.ToCsv(result, neutralizeFormulas: !opts.CsvRaw))) return 2;
            Console.WriteLine($"\nwrote {opts.CsvPath}");
        }

        if (opts.JsonPath is not null)
        {
            if (!Pipeline.TryWrite(opts.JsonPath, BudgetReport.ToJson(result, CommonOptions.ToolVersion()))) return 2;
            Console.WriteLine($"wrote {opts.JsonPath}");
        }

        if (opts.SitesPath is not null)
        {
            if (!Pipeline.TryWrite(opts.SitesPath, BudgetReport.SitesCsv(scan, neutralizeFormulas: !opts.CsvRaw))) return 2;
            Console.WriteLine($"wrote {opts.SitesPath}");
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
}
