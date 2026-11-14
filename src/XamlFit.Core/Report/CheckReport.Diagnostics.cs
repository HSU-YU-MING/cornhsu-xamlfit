using System.Globalization;
using System.Text;
using System.Text.Json;
using XamlFit.Core.Analysis;

namespace XamlFit.Core.Report;

public static partial class CheckDiagnostics
{
    /// <summary>
    /// 把控制字元逃掉。
    ///
    /// ⚠ 這不是美觀問題：MSBuild 與 console 是**逐行**解析診斷的，
    ///   訊息裡夾一個換行就會讓下半行變成另一筆看起來很真的診斷。
    ///   文字來自使用者的 XAML 與譯文，所以那是真的會發生的輸入。
    ///   （U+2028／U+2029 也算換行，有些解析器會斷在那裡。）
    /// </summary>
    public static string ConsoleSafe(string s)
    {
        // U+2028 / U+2029（行與段分隔符）用數值比對 —— 寫成字面會在原始碼裡變成真的換行。
        static bool Escape(char c) =>
            (char.IsControl(c) && c != 0x09) || c == (char)0x2028 || c == (char)0x2029;
        if (!s.Any(Escape)) return s;

        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
            sb.Append(c switch
            {
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\t",
                _ when Escape(c) => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        return sb.ToString();
    }

    /// <summary>
    /// MSBuild 的標準診斷格式：<c>完整路徑(行,1): error XF0001: 訊息</c>。
    ///
    /// MSBuild 的 Exec 會把這種行記成錯誤／警告，Visual Studio 就列進錯誤清單、
    /// 點兩下跳到那一行 —— 不必裝擴充套件，CLI 跑在建置裡就好。
    ///
    /// <list type="bullet">
    /// <item><c>XF0001</c>（error）：這次 gate 擋下的。</item>
    /// <item><c>XF0002</c>（warning）：有問題但這次不擋的（低於 gate、baseline 裡的已知債、<c>--pseudo</c>）。</item>
    /// <item><c>XF0003</c>（error）：結果不可信（exit 3）——
    ///   不然建置會在「工具其實只看到一部分」的時候安靜地過。</item>
    /// </list>
    /// 用法／設定錯誤（exit 2）在這之前就結束了，訊息在 stderr。
    /// </summary>
    public static string ToMsBuild(
        IReadOnlyList<FitFinding> reported, string root, FitSeverity gate, int exitCode)
    {
        var sb = new StringBuilder();

        foreach (var f in reported
                     .Where(f => f.Severity >= FitSeverity.Warn)
                     .OrderBy(f => f.Site, StringComparer.Ordinal))
        {
            var (file, line) = SplitSite(f.Site);
            var path = SafeFullPath(root, file);
            var gated = f.Severity >= gate;

            sb.Append(ConsoleSafe(path))
              .Append('(').Append(Math.Max(line, 1).ToString(CultureInfo.InvariantCulture)).Append(",1): ")
              .Append(gated ? "error XF0001: " : "warning XF0002: ")
              .Append(ConsoleSafe(Message(f)))
              .Append('\n');
        }

        if (exitCode == 3)
            sb.Append("XamlFit : error XF0003: the result can't be trusted (exit 3) — see the messages above\n");

        return sb.ToString();
    }

    private static string Message(FitFinding f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{FitFinding.KindSlug(f.Kind)} [{f.Culture}] \"{f.Text}\" needs {f.ActualPx:0.#} px " +
            $"but has {f.LimitPx:0.#} px" +
            $"{(f.OverflowPx > 0 ? $" (over by {f.OverflowPx:0.#} px)" : "")}" +
            $"{(f.Warning is not null ? $" [{f.Warning}]" : "")}");

    /// <summary>
    /// SARIF 2.1.0（GitHub code scanning 消費）。
    ///
    /// 每一種判定是一條規則，而不是全部塞進一個 ruleId —— `overflow` 與 `reflow` 的
    /// 嚴重度與處置完全不同，合成一條會讓 Security tab 無法分流。
    /// `reflow`（info）不輸出：它不是缺陷，灌進去是噪音。
    /// </summary>
    public static string ToSarif(IReadOnlyList<FitFinding> reported, string toolVersion)
    {
        var kinds = new[]
        {
            (FitKind.Overflow, "Text does not fit and is clipped",
                "The translated string is wider than its slot, the text does not wrap and no trimming is set, so it spills out or is cut off. This case does not raise IsTextTrimmedChanged at runtime, so only static measurement finds it."),
            (FitKind.VerticalClip, "Wrapped text does not fit the fixed height",
                "The string wraps to more lines than the declared height can show, so the tail is cut off."),
            (FitKind.Trimmed, "Text is shortened with an ellipsis",
                "The translated string is wider than its slot. Trimming is set, so it renders as an ellipsis rather than spilling out, but information is lost."),
            (FitKind.Tight, "Text nearly fills its slot",
                "The string fits but leaves less slack than the configured safety margin, so a small change to the text or the font will break it."),
        };

        var rules = kinds.Select(k => new
        {
            id = FitFinding.KindSlug(k.Item1),
            name = k.Item1.ToString(),
            shortDescription = new { text = k.Item2 },
            fullDescription = new { text = k.Item3 },
            defaultConfiguration = new { level = LevelOf(k.Item1) },
        });

        var results = reported
            .Where(f => f.Kind != FitKind.Reflow)
            .Select(f =>
            {
                var (file, line) = SplitSite(f.Site);
                return new
                {
                    ruleId = FitFinding.KindSlug(f.Kind),
                    level = f.Severity == FitSeverity.Fail ? "error" : "warning",
                    message = new
                    {
                        // InvariantCulture：de-DE 下的小數點會變成逗號，消費端解析會壞
                        text = string.Create(CultureInfo.InvariantCulture,
                            $"[{f.Culture}] \"{f.Text}\" needs {f.ActualPx:0.#} px but has {f.LimitPx:0.#} px"),
                    },
                    locations = new[]
                    {
                        new
                        {
                            physicalLocation = new
                            {
                                artifactLocation = new { uri = file },   // root 相對、正斜線
                                region = new { startLine = Math.Max(line, 1) },
                            },
                        },
                    },
                };
            });

        var payload = new
        {
            version = "2.1.0",
            schema = "https://json.schemastore.org/sarif-2.1.0.json",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "XamlFit",
                            informationUri = "https://cornhsu.com/xamlfit",
                            version = toolVersion,
                            rules,
                        },
                    },
                    results,
                },
            },
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static string LevelOf(FitKind k) => k switch
    {
        FitKind.Overflow or FitKind.VerticalClip => "error",
        _ => "warning",
    };

    /// <summary>"Views/Main.xaml:42" → ("Views/Main.xaml", 42)</summary>
    internal static (string File, int Line) SplitSite(string site)
    {
        var i = site.LastIndexOf(':');
        if (i <= 0 || !int.TryParse(site[(i + 1)..], out var line)) return (site, 0);
        return (site[..i], line);
    }

    /// <summary>⚠ 路徑可能含不合法字元或長得過頭，Path.GetFullPath 會丟 ——
    /// 報告產生失敗比報告路徑不漂亮嚴重得多。</summary>
    private static string SafeFullPath(string root, string file)
    {
        try { return Path.GetFullPath(Path.Combine(root, file)); }
        catch (Exception) { return file; }
    }
}
