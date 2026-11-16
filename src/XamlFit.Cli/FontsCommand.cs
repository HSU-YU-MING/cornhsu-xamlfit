using System.Globalization;
using XamlFit.Core.Fonts;
using XamlFit.Core.OpenType;

namespace XamlFit.Cli;

internal static class FontsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("usage: xamlfit fonts <export|list>"); return 2; }

        return args[0] switch
        {
            "export" => Export(args[1..]),
            "list" => List(args[1..]),
            _ => Fail($"unknown subcommand: fonts {args[0]}"),
        };
    }

    private static int Fail(string msg) { Console.Error.WriteLine(msg); return 2; }

    private static int Export(string[] args)
    {
        var sources = new List<string>();
        var families = new List<string>();
        var outPath = MetricsFile.DefaultFileName;
        var merge = false;
        var hinted = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from":
                    if (i + 1 >= args.Length) return Fail("--from needs a path");
                    sources.Add(args[++i]); break;
                case "--family":
                    if (i + 1 >= args.Length) return Fail("--family needs a name");
                    families.Add(args[++i]); break;
                case "--out":
                    if (i + 1 >= args.Length) return Fail("--out needs a path");
                    outPath = args[++i]; break;
                case "--merge":
                    merge = true; break;
                case "--hinted":
                    hinted = true; break;
                default:
                    return Fail($"unknown argument: {args[i]}");
            }
        }

        if (sources.Count == 0)
        {
            var sys = SystemFontDirectory();
            if (sys is null)
                return Fail("no --from given and the system font directory could not be found; pass --from <dir>");
            sources.Add(sys);
            Console.Error.WriteLine($"no --from given, using {sys}");
        }

        var files = new List<string>();
        foreach (var s in sources)
        {
            if (File.Exists(s)) { files.Add(s); continue; }
            if (Directory.Exists(s))
            {
                files.AddRange(Directory.EnumerateFiles(s, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)));
                continue;
            }
            return Fail($"not found: {s}");
        }

        if (files.Count == 0) return Fail("no font files found");

        var wanted = new HashSet<string>(families.Select(f => f.Trim()), StringComparer.OrdinalIgnoreCase);
        var metrics = new List<FontMetrics>();
        var unreadable = 0;
        var hintSkipped = new List<string>();
        if (hinted && !OperatingSystem.IsWindows())
            return Fail("--hinted asks Windows GDI for hinted widths; run it on Windows");

        // 每支字型檔各自獨立（阿拉伯文、印度系文字的形狀表要跑幾十萬次替換規則），平行處理；結果照檔案順序放回去
        var perFile = new List<FontMetrics>?[files.Count];
        var debug = Environment.GetEnvironmentVariable("XAMLFIT_DEBUG") is not null;   // 印每支字型花的時間
        // GDI 的 hinting 寬度：哪支字型的形狀表算完就排進去，一支一支在另一條執行緒做，跟還在算的形狀表重疊（原本全部算完才開始，多 4 分鐘）。
        // 同時只做一支：同名字型同時註冊到 GDI，會選到別支檔案的字面
        var hintedPerFile = new List<FontMetrics>?[files.Count];
        var hintSkippedPerFile = new List<string>?[files.Count];
        using var gdiQueue = new System.Collections.Concurrent.BlockingCollection<int>();
        var gdiTask = hinted && OperatingSystem.IsWindows()
            ? Task.Run(() =>
            {
                foreach (var i in gdiQueue.GetConsumingEnumerable())
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var (withHints, skipped) = GdiHinting.Add(files[i], perFile[i]!);
                    (hintedPerFile[i], hintSkippedPerFile[i]) = ([.. withHints], [.. skipped]);
                    if (debug) Console.Error.WriteLine($"[time] {Path.GetFileName(files[i])} gdi {sw.Elapsed.TotalSeconds:F1}s");
                }
            })
            : null;
        // 掃過的每支字型的家族名（含沒匯出的）：讓解析時能照 WPF 把「Segoe UI Variable」拆成 Segoe UI + 字面名稱
        var installed = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            var f = files[i];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // 先只讀名稱就知道要不要：沒在 --family 裡的字型不跑形狀表
            IReadOnlyList<FontMetrics> got;
            try { got = FontMetricsExtractor.FromFile(f, m => { installed.TryAdd(m, true); return wanted.Count == 0 || wanted.Contains(m); }); }
            catch (Exception e) { if (Environment.GetEnvironmentVariable("XAMLFIT_DEBUG") is not null) Console.Error.WriteLine($"{f}: {e}"); return; }   // 一個資料夾裡有壞字型是常態，不要整批失敗

            foreach (var m in got) installed.TryAdd(m.Family, true);
            if (debug && got.Count > 0) Console.Error.WriteLine($"[time] {Path.GetFileName(f)} shapes {sw.Elapsed.TotalSeconds:F1}s");
            perFile[i] = [.. got];   // 已經在讀的時候篩過（可變字型的實例家族名 Segoe UI Variable Text 也算 --family "Segoe UI Variable"）
            if (gdiTask is not null && got.Count > 0) gdiQueue.Add(i);
        });
        gdiQueue.CompleteAdding();
        gdiTask?.Wait();
        for (var i = 0; i < files.Count; i++)
        {
            if (perFile[i] is not { } picked) { unreadable++; continue; }
            if (hintedPerFile[i] is { } withHints) picked = withHints;
            if (hintSkippedPerFile[i] is { } skipped) hintSkipped.AddRange(skipped);
            metrics.AddRange(picked);
        }

        if (metrics.Count == 0)
        {
            Console.Error.WriteLine($"no metrics extracted from {files.Count} file(s)" +
                (wanted.Count > 0 ? " matching --family" : ""));
            return 2;
        }

        // 同一個 family|weight|style|stretch 只留一份（系統字型目錄常有重複）
        var deduped = metrics
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(m => m.Ranges.Count).First())
            .ToList();

        if (merge && File.Exists(outPath))
        {
            try
            {
                var existingFile = MetricsFile.Load(outPath);
                foreach (var name in existingFile.InstalledFamilies ?? []) installed.TryAdd(name, true);
                var existing = existingFile.ToMetrics();
                var ids = new HashSet<string>(deduped.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
                deduped.AddRange(existing.Where(m => !ids.Contains(m.Id)));
            }
            catch (Exception ex)
            {
                return Fail($"--merge: cannot read existing {outPath}: {ex.Message}");
            }
        }

        var file = MetricsFile.FromMetrics(deduped, ToolVersion(), DateOnly.FromDateTime(DateTime.Now));
        file.InstalledFamilies = [.. installed.Keys.Concat(deduped.Select(m => m.Family)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        MetricsFile.Save(outPath, file.ToJson());

        var bytes = new FileInfo(outPath).Length;
        Console.WriteLine($"wrote {outPath}  ({deduped.Count} faces, " +
                          $"{deduped.Select(m => m.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count()} families, " +
                          $"{bytes / 1024.0:F0} KB)");
        if (unreadable > 0) Console.WriteLine($"  skipped {unreadable} unreadable file(s)");
        if (hinted)
        {
            Console.WriteLine("  hinted widths for 8–48 px added from GDI (TextFormattingMode=\"Display\")");
            if (hintSkipped.Count > 0)
            {
                Console.WriteLine($"  {hintSkipped.Count} face(s) left without them (Display-mode text in them is not measurable):");
                foreach (var s in hintSkipped) Console.WriteLine($"    {s}");
            }
        }
        Console.WriteLine("  contains advance widths, kerning adjustments and the widths of shaped letters and clusters only — no glyph outlines, no font software");
        return 0;
    }

    private static int List(string[] args)
    {
        var path = MetricsFile.Find(".") ?? MetricsFile.DefaultFileName;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--metrics" && i + 1 < args.Length) { path = args[++i]; continue; }
            return Fail($"unknown argument: {args[i]}");
        }

        if (!File.Exists(path)) return Fail($"metrics file not found: {path}");

        MetricsFile file;
        try { file = MetricsFile.Load(path); }
        catch (Exception ex) { return Fail($"cannot read {path}: {ex.Message}"); }

        Console.WriteLine($"{path}  (schemaVersion {file.SchemaVersion}, generated by {file.GeneratedBy} on {file.GeneratedOn})");
        foreach (var g in file.Fonts.GroupBy(f => f.Family, StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var faces = g.OrderBy(f => f.Weight).ThenBy(f => f.Style)
                         .Select(f => $"{f.Weight}{(f.Style == "Normal" ? "" : " " + f.Style)}");
            Console.WriteLine($"  {g.Key,-32} {string.Join(", ", faces)}");
        }
        Console.WriteLine($"  {file.Fonts.Count} faces total");
        return 0;
    }

    private static string? SystemFontDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            return Directory.Exists(d) ? d : null;
        }
        foreach (var d in new[] { "/usr/share/fonts", "/Library/Fonts", "/System/Library/Fonts" })
            if (Directory.Exists(d)) return d;
        return null;
    }

    private static string ToolVersion() =>
        typeof(FontsCommand).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion is { } v
                ? (v.IndexOf('+') > 0 ? v[..v.IndexOf('+')] : v)
                : "0.0.0-dev";
}
