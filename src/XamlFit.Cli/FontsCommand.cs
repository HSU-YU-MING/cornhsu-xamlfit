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
        var outPath = "xamlfit.fonts.json";
        var merge = false;

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

        foreach (var f in files)
        {
            IReadOnlyList<FontMetrics> got;
            try { got = FontMetricsExtractor.FromFile(f); }
            catch { unreadable++; continue; }   // 一個資料夾裡有壞字型是常態，不要整批失敗

            foreach (var m in got)
            {
                if (wanted.Count > 0 && !wanted.Contains(m.Family)) continue;
                metrics.Add(m);
            }
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
                var existing = MetricsFile.Parse(File.ReadAllText(outPath)).ToMetrics();
                var ids = new HashSet<string>(deduped.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
                deduped.AddRange(existing.Where(m => !ids.Contains(m.Id)));
            }
            catch (Exception ex)
            {
                return Fail($"--merge: cannot read existing {outPath}: {ex.Message}");
            }
        }

        var file = MetricsFile.FromMetrics(deduped, ToolVersion(), DateOnly.FromDateTime(DateTime.Now));
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outPath, file.ToJson());

        var bytes = new FileInfo(outPath).Length;
        Console.WriteLine($"wrote {outPath}  ({deduped.Count} faces, " +
                          $"{deduped.Select(m => m.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count()} families, " +
                          $"{bytes / 1024.0:F0} KB)");
        if (unreadable > 0) Console.WriteLine($"  skipped {unreadable} unreadable file(s)");
        Console.WriteLine("  contains advance widths only — no glyph outlines, no font software");
        return 0;
    }

    private static int List(string[] args)
    {
        var path = "xamlfit.fonts.json";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--metrics" && i + 1 < args.Length) { path = args[++i]; continue; }
            return Fail($"unknown argument: {args[i]}");
        }

        if (!File.Exists(path)) return Fail($"metrics file not found: {path}");

        MetricsFile file;
        try { file = MetricsFile.Parse(File.ReadAllText(path)); }
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
