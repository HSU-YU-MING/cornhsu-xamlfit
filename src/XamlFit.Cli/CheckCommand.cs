using XamlFit.Core.Analysis;
using XamlFit.Core.Report;

namespace XamlFit.Cli;

internal static class CheckCommand
{
    public static int Run(string[] args)
    {
        var opts = CommonOptions.Parse(args, out var error);
        if (error is not null) { Console.Error.WriteLine(error); return 2; }
        if (opts.ForeignFlag("check", CommonOptions.BudgetOnly) is { } foreign) { Console.Error.WriteLine(foreign); return 2; }
        if (opts.Root is null) { Console.Error.WriteLine("check needs a root directory"); return 2; }
        if (!Directory.Exists(opts.Root)) { Console.Error.WriteLine($"not a directory: {opts.Root}"); return 2; }

        // check 是閘門，所以可解析率下限預設是 60（budget 是匯出，預設 0）
        if (!opts.WasGiven("--min-coverage")) opts.MinCoverage = 60;

        // ⚠ 設定檔要在「必填參數檢查」**之前**套用。
        //   反過來的話，設定檔裡寫了 resx 也永遠不會生效 —— 檢查會先把人擋掉。
        if (CommonOptions.ApplyConfig(opts) is var cfgCode and not 0) return cfgCode;

        foreach (var dir in opts.ResxDirs)
            if (!Directory.Exists(dir)) { Console.Error.WriteLine($"--resx directory not found: {dir}"); return 2; }

        if (opts.BaselinePath is not null && !File.Exists(opts.BaselinePath))
        { Console.Error.WriteLine($"baseline not found: {opts.BaselinePath}"); return 2; }

        if (Pipeline.Prepare(opts, out var prepareCode, XamlFit.Core.Layout.RowSharing.Preceding) is not { } prepared) return prepareCode;
        var scan = prepared.Scan;

        // ===== 字串表：.resx 加上掃描時找到的 XAML 字串字典 =====
        var cultures = new List<CultureStrings>(scan.Strings);
        foreach (var dir in opts.ResxDirs)
        {
            var found = ResxReader.ReadDirectory(dir);
            if (found.Count == 0) Console.Error.WriteLine($"no .resx files under {dir}");
            cultures.AddRange(found);
        }

        // ⚠ 什麼字串表都沒有、又不是 --pseudo：使用者多半是忘了給 --resx。
        //   照樣跑下去只會檢查寫死的文字然後亮綠燈，看起來像「譯文檢查過了」。
        if (opts.SourceOnly && opts.Pseudo)
        { Console.Error.WriteLine("--source-only and --pseudo cannot be combined"); return 2; }

        if (opts.SourceOnly)
            cultures = cultures.Where(c => c.Culture == ResxReader.NeutralCulture).ToList();

        if (!opts.Pseudo && !opts.SourceOnly && cultures.Count == 0)
        {
            Console.Error.WriteLine(
                "check needs translations: pass --resx <dir>, set \"resx\" in xamlfit.config.json, " +
                "keep XAML string dictionaries (<sys:String x:Key=…>) under <root>, " +
                "use --pseudo to simulate expansion, or --source-only to check just the source-language text");
            return 2;
        }

        if (opts.Cultures.Count > 0 && !opts.SourceOnly)
        {
            var available = cultures.Select(c => c.Culture).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var unknown = opts.Cultures.Where(c => !available.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0)
            {
                Console.Error.WriteLine(
                    $"--culture {string.Join(", ", unknown)}: no string table for it. Found: " +
                    string.Join(", ", available.Where(c => c != ResxReader.NeutralCulture).DefaultIfEmpty("(none)")));
                return 2;
            }

            // ⚠ 中性語言一定要留著：它是來源字串，--pseudo 與「目前有多寬」都靠它。
            //   第一版的篩選把它一起濾掉，於是 `--pseudo --culture de` 量到 0 筆。
            cultures = cultures
                .Where(c => c.Culture == ResxReader.NeutralCulture
                            || opts.Cultures.Contains(c.Culture, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        CheckAnalyzer.MergeByCulture(cultures).TryGetValue(ResxReader.NeutralCulture, out var neutral);

        // 同一排的兄弟也是 .resx 字串時，要有來源語言的值才量得出它們多寬
        if (neutral is { Count: > 0 } && scan.NeighbourTextUses > 0)
            scan = prepared.Scanner.Rescan(opts.DefaultFont, neutral);

        var budget = new BudgetAnalyzer(prepared.Fonts, opts.DefaultFont).Analyze(scan, prepared.Chrome.Theme, neutral);

        var neutralTables = cultures.Where(c => c.Culture == ResxReader.NeutralCulture).ToList();
        var byCulture = CheckAnalyzer.MergeByCulture(cultures);

        var measurableInSource = scan.Sites.Where(s => s.Width.IsMeasurable).Select(s => s.Location).ToHashSet(StringComparer.Ordinal);

        var parts = new List<CheckResult>
        {
            // 來源語言自己（--pseudo 也在這裡：拿來源字串去膨脹）
            new CheckAnalyzer(prepared.Fonts, opts.DefaultFont, opts.MarginPercent)
                .Analyze(scan, budget, neutralTables, opts.Pseudo),
        };

        // ⚠ 每個語言各掃一次，理由有兩個（都是 LayoutOracle 實測出來的）：
        //   1. 沒寫 FontFamily 的文字用「系統訊息字型」畫，而它跟著 Windows 的語言走（見 CultureFonts）。
        //   2. 同一排的兄弟也是在地化字串時，留給這個位置的寬度取決於兄弟**在那個語言**有多寬。
        //   兩者都不成立時（同一支字型、版面不依賴兄弟的文字）沿用同一次掃描。
        var unmeasuredCultures = new List<(string Font, string Cultures)>();
        var scansByFont = new Dictionary<string, XamlFit.Core.Xaml.ScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [opts.DefaultFont] = scan,
        };

        if (!opts.Pseudo)
        {
            foreach (var culture in byCulture.Keys
                         .Where(c => c != ResxReader.NeutralCulture)
                         .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            {
                var font = CultureFonts.DefaultFor(culture, opts.DefaultFontByCulture) ?? opts.DefaultFont;

                if (prepared.Fonts.Resolve(font, 400, XamlFit.Core.Fonts.FontStyleKind.Normal, font).Chain.Count == 0)
                {
                    unmeasuredCultures.Add((font, culture));
                    continue;
                }

                XamlFit.Core.Xaml.ScanResult localized;
                var language = opts.ElementLanguageFollowsCulture ? culture : null;
                if (scan.NeighbourTextUses > 0 || language is not null)
                    localized = prepared.Scanner.Rescan(font, Pipeline.Overlay(neutral, byCulture[culture]), language);
                else if (!scansByFont.TryGetValue(font, out localized!))
                    scansByFont[font] = localized = prepared.Scanner.Rescan(font, null);

                parts.Add(new CheckAnalyzer(prepared.Fonts, font, opts.MarginPercent)
                    .Analyze(localized, budget,
                        [.. neutralTables, .. cultures.Where(c => c.Culture.Equals(culture, StringComparison.OrdinalIgnoreCase))],
                        pseudo: false, judgeSource: false, measurableInSource,
                        cultureFontDiffers: !string.Equals(font, opts.DefaultFont, StringComparison.OrdinalIgnoreCase)));
            }
        }

        var result = CheckAnalyzer.Merge(parts);

        var all = result.Findings;

        // ===== baseline =====
        if (opts.WriteBaselinePath is not null)
        {
            if (!Pipeline.TryWrite(opts.WriteBaselinePath,
                    Baseline.FromFindings(all, DateOnly.FromDateTime(DateTime.Now)).ToJson())) return 2;
            Console.WriteLine($"wrote baseline with {all.Count} known finding(s) → {opts.WriteBaselinePath}");
            return 0;
        }

        var reported = all;
        var baselined = 0;
        if (opts.BaselinePath is not null)
        {
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
        Console.WriteLine($"xamlfit check — {s.Budget.Files} XAML file(s), {s.Budget.MeasuredSites} measurable text site(s)");
        Console.WriteLine(opts.Pseudo
            ? "  mode             pseudo (simulated expansion — reports risk, never fails the build)"
            : $"  cultures         {(s.Cultures.Count > 0 ? string.Join(", ", s.Cultures) : "(none — only the source language was checked)")}");
        Console.WriteLine($"  default font     {opts.DefaultFont} {opts.DefaultSize:G}  (used where XAML sets no FontFamily; WPF takes it from the Windows display language)");
        Console.WriteLine($"  measurements     {s.Measurements}" +
                          (opts.Pseudo ? "" : $"  ({s.TranslatedMeasurements} translated, {s.Measurements - s.TranslatedMeasurements} source-language)"));

        if (s.Unreliable > 0)
        {
            Console.Error.WriteLine(
                $"  !! {s.Unreliable} measurement(s) were NOT judged because the width cannot be trusted:");
            foreach (var (culture, reason, count) in s.UnreliableReasons.Take(6))
                Console.Error.WriteLine($"       {count,5}  [{culture}] {reason}");
            if (s.UncoveredCodepoints.Count > 0)
                Console.Error.WriteLine(
                    "       characters with no metrics: " +
                    string.Join(", ", s.UncoveredCodepoints.Take(10).Select(u => $"U+{u.Codepoint:X4} ×{u.Count}")) +
                    (s.UncoveredCodepoints.Count > 10 ? ", …" : "") +
                    "  (export a font that has them, e.g. Segoe UI Emoji or Segoe UI Symbol)");
        }
        if (s.Placeholders > 0)
            Console.WriteLine($"  not judged       {s.Placeholders}  (format placeholders like {{0}}: the displayed length depends on run-time values)");
        if (s.NoSourceText > 0)
            Console.Error.WriteLine(
                $"  !! {s.NoSourceText} resource string(s) have no source text to expand " +
                "(pass --resx pointing at the neutral-culture .resx)");
        if (s.MissingTranslations > 0) Console.WriteLine($"  missing strings  {s.MissingTranslations}  (key has no value in that culture)");
        if (s.Suppressed > 0) Console.WriteLine($"  suppressed       {s.Suppressed}");
        if (baselined > 0) Console.WriteLine($"  in baseline      {baselined}");
        Console.WriteLine($"  resolution       {s.Budget.Resolution:F1}%  ({s.Budget.MeasuredSites} measured, {s.Budget.BlockedSites} blocked; " +
                          $"{s.Budget.NoDeclaredLimitSites} with no width bound and {s.Budget.InTemplateSites} in templates are not checked)");

        Pipeline.PrintBlindSpots(scan, s.Budget);

        // ⚠ 用 baseline 時要說清楚幾筆是新的：實際用在 QuillNest 時，摘要寫著「overflow 2」而退出碼是 0，看起來互相矛盾
        foreach (var kind in new[] { "overflow", "vertical-clip", "trimmed", "tight", "reflow" })
            if (s.Counts.TryGetValue(kind, out var n) && n > 0)
            {
                var fresh = reported.Count(f => FitFinding.KindSlug(f.Kind) == kind);
                Console.WriteLine(baselined > 0
                    ? $"    {kind,-14} {n}  ({fresh} new, {n - fresh} already in the baseline)"
                    : $"    {kind,-14} {n}");
            }

        if (reported.Count > 0)
        {
            Console.WriteLine();
            foreach (var f in reported.Take(40))
            {
                Console.WriteLine(
                    $"  {FitFinding.SeveritySlug(f.Severity),-4} {FitFinding.KindSlug(f.Kind),-13} " +
                    $"[{f.Culture}] {CheckDiagnostics.ConsoleSafe(f.Site)}");
                Console.WriteLine(
                    $"       {Pipeline.Trunc(f.Text, 60)}");
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

        // 某個語言的系統字型沒有度量：那個語言整批沒檢查，不能當成通過
        foreach (var (font, names) in unmeasuredCultures
                     .GroupBy(u => u.Font, StringComparer.OrdinalIgnoreCase)
                     .Select(g => (g.Key, string.Join(", ", g.Select(u => u.Cultures)))))
            Console.Error.WriteLine(
                $"\n{names}: NOT checked. Text without an explicit FontFamily is drawn with '{font}' on that " +
                $"language's Windows, and the metrics file has no such font. Export it " +
                $"(xamlfit fonts export --family \"{font}\" --merge), leave the culture out with --culture, " +
                "or map it to another font under \"defaultFontByCulture\" in xamlfit.config.json.");
        if (unmeasuredCultures.Count > 0 && exitCode is 0 or 1) exitCode = 3;

        if (opts.JsonPath is not null)
        {
            if (!Pipeline.TryWrite(opts.JsonPath, CheckReport.ToJson(result, reported, CommonOptions.ToolVersion()))) return 2;
            Console.WriteLine($"\nwrote {opts.JsonPath}");
        }

        if (opts.SarifPath is not null)
        {
            if (!Pipeline.TryWrite(opts.SarifPath, CheckDiagnostics.ToSarif(reported, CommonOptions.ToolVersion()))) return 2;
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

        // ⚠ 量到 0 筆就回 exit 0，等於在什麼都沒看的情況下說「沒問題」。
        //   這條原本把 --pseudo 排除在外，而 --pseudo 剛好是最容易量到 0 的模式
        //   （資源引用取不到來源字串時）—— 那個排除本身就是一個謊報健康的洞。
        if (s.Measurements == 0)
        {
            Console.Error.WriteLine(opts.Pseudo
                ? "\nnothing was measured — --pseudo needs source strings: either literal text in the " +
                  "XAML, or --resx pointing at the neutral-culture .resx the keys come from"
                : "\nnothing was measured — result is not trustworthy");
            return 3;
        }

        // 有譯文的語言，卻一筆譯文都沒量到：鍵對不上，或那些字串都不在量得到的位置上
        if (!opts.Pseudo && s.Cultures.Count > 0 && s.TranslatedMeasurements == 0)
        {
            Console.Error.WriteLine(
                "\nno translated string was actually measured — check --resx and that the keys match " +
                "the ones the XAML refers to");
            return 3;
        }

        // 某個語言整批沒有判定（阿拉伯文、泰文…，或度量檔沒有那個文字的字型）：
        // 「沒檢查」不能長得跟「通過」一樣
        var skipped = s.PerCulture.Where(p => p.Measured == 0 && p.Unreliable > 0).Select(p => p.Culture).ToList();
        if (skipped.Count > 0)
        {
            Console.Error.WriteLine(
                $"\nnot one string could be measured reliably for: {string.Join(", ", skipped)} — " +
                "result is not trustworthy for those cultures (export a font that covers the script, " +
                "or leave them out with --culture)");
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
}
