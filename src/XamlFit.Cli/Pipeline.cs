using XamlFit.Core.Analysis;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.Report;
using XamlFit.Core.Xaml;

namespace XamlFit.Cli;

/// <summary>budget 與 check 共用的前半段：載入度量、套用宣告的內距、掃描。</summary>
internal sealed record Prepared(ChromeTable Chrome, FontResolver Fonts, XamlScanner Scanner, ScanResult Scan);

internal static class Pipeline
{
    /// <param name="sharing">budget：整排（給譯者的預算要讓後面的按鈕也留在畫面裡）；
    /// check：WPF 實際的順序（要報的是哪一個真的被裁）。</param>
    public static Prepared? Prepare(CommonOptions opts, out int exitCode, RowSharing sharing = RowSharing.WholeRow)
    {
        exitCode = 0;

        var fonts = CommonOptions.LoadFonts(opts, out var fontError);
        if (fontError is not null)
        {
            Console.Error.WriteLine(fontError);
            exitCode = 2;
            return null;
        }

        var chrome = ChromeTable.Builtin();
        if (opts.Chrome.Count > 0) chrome = chrome.WithOverrides(opts.Chrome);

        opts.ResolveSourceFont();

        var scanner = new XamlScanner(chrome, new ScanOptions
        {
            DefaultFontFamily = opts.DefaultFont,
            DefaultFontSize = opts.DefaultSize,
            ExcludeGlobs = opts.Exclude,
            TrustDefaultChrome = opts.AssumeDefaultTheme,
            TreatAs = opts.TreatAs,
            GridPanels = opts.GridPanels,
            ControlWidths = opts.ControlWidths,
            HiddenScrollBars = opts.HiddenScrollBars,
            DisplayScales = opts.DisplayScales ?? ScanOptions.AllDisplayScales,
            DefaultFontPinned = opts.DefaultFontPinned,
            ElementLanguage = opts.SourceElementLanguage,
            Sharing = sharing,
            AssumeWindowMinWidth = opts.AssumeWindowMinWidth,
            PopupMaxWidth = opts.PopupMaxWidth,
            WindowTitleBarWindows11 = opts.WindowTitleBarWindows11,
        }, fonts);

        return new Prepared(chrome, fonts, scanner, scanner.Scan(opts.Root!));
    }

    /// <summary>把一個語言的字串疊在來源語言上面：那個語言沒翻的鍵，執行期顯示的就是來源語言的字。</summary>
    public static Dictionary<string, string> Overlay(
        IReadOnlyDictionary<string, string>? source, IReadOnlyDictionary<string, string>? culture)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source is not null) foreach (var (k, v) in source) result[k] = v;
        if (culture is not null) foreach (var (k, v) in culture) result[k] = v;
        return result;
    }

    /// <summary>
    /// 「工具沒看到什麼」的清單。每一條都是結果的一部分 —— 少講一條，
    /// 使用者就會把「沒分析」讀成「沒問題」。走 stderr，不混進可以被解析的 stdout。
    /// </summary>
    public static void PrintBlindSpots(ScanResult scan, BudgetSummary s)
    {
        // ⚠ 解析失敗的檔案一定要講。一個壞掉的 XAML 靜默消失 = 謊報健康
        //   （示範專案的註解裡放了連續兩個減號，整份解析失敗，而 check 模式一聲不吭）。
        if (scan.ParseErrors > 0)
        {
            Console.Error.WriteLine($"  !! {scan.ParseErrors} file(s) failed to parse and were not analysed:");
            foreach (var f in scan.ParseErrorFiles.Take(5)) Console.Error.WriteLine($"       {CheckDiagnostics.ConsoleSafe(f)}");
            if (scan.ParseErrorFiles.Count > 5) Console.Error.WriteLine($"       … and {scan.ParseErrorFiles.Count - 5} more");
        }

        if (scan.LayoutRoundingFiles.Count > 0)
            Console.WriteLine(
                $"  layout rounding  {scan.LayoutRoundingFiles.Count} file(s) set UseLayoutRounding: WPF rounds every size to whole " +
                "device pixels, and text sized to its content can lose about one pixel at its right edge. XamlFit measures without rounding.");

        if (scan.ForeignFiles.Count > 0)
        {
            var byDialect = string.Join(", ", scan.ForeignFiles
                .GroupBy(f => f.Dialect)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}: {g.Count()}"));
            Console.Error.WriteLine(
                $"  !! {scan.ForeignFiles.Count} file(s) are not WPF XAML ({byDialect}) and were NOT analysed. " +
                "XamlFit's chrome table, default font and layout rules are WPF's.");
        }

        foreach (var theme in scan.ExternalThemes.Where(t => t.Profile is not null).DistinctBy(t => (t.Profile, t.Scope)))
        {
            Console.Error.WriteLine(
                $"  external theme '{CheckDiagnostics.ConsoleSafe(theme.Theme)}' (merged in {CheckDiagnostics.ConsoleSafe(theme.DeclaredIn)}): " +
                $"using the chrome, fonts and casing measured for {theme.Profile}.");
        }
        var unmeasured = scan.ExternalThemes.Where(t => t.Profile is null).DistinctBy(t => (t.Theme, t.Scope)).ToList();
        foreach (var theme in unmeasured)
        {
            var scope = theme.Scope.Length == 0 ? "every scanned file"
                : theme.Scope.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? theme.Scope
                : $"files under {theme.Scope}";
            var note = theme.ProfileNote is { } n && n != "not measured" ? $" ({CheckDiagnostics.ConsoleSafe(n)})" : "";
            Console.Error.WriteLine(
                $"  !! external theme '{CheckDiagnostics.ConsoleSafe(theme.Theme)}' (merged in {CheckDiagnostics.ConsoleSafe(theme.DeclaredIn)}) restyles standard controls in {scope}{note}.");
        }
        if (unmeasured.Count > 0)
            Console.Error.WriteLine(
                "     Their chrome is defined outside this project, so text inside them is reported as blocked.\n" +
                "     Declare the real chrome under \"chrome\" in xamlfit.config.json, or pass --assume-default-theme\n" +
                "     if the theme does not change control padding.");

        if (s.MissingFonts.Count > 0)
            Console.Error.WriteLine(
                $"  !! no font metrics for: {string.Join(", ", s.MissingFonts.Select(CheckDiagnostics.ConsoleSafe))}  " +
                "(run 'xamlfit fonts export --family \"<name>\"', or list the family under \"absentFonts\" if target machines do not have it)");

        if (s.InvalidIgnores > 0)
            Console.Error.WriteLine(
                $"  !! {s.InvalidIgnores} xamlfit-ignore comment(s) have no reason and were NOT applied");
    }

    public static void PrintBlockedReasons(BudgetSummary s, int take = 10)
    {
        if (s.BlockedReasons.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("why sites are blocked:");
        foreach (var (reason, n) in s.BlockedReasons.Take(take))
            Console.WriteLine($"  {n,6}  {CheckDiagnostics.ConsoleSafe(reason)}");
        if (s.BlockedReasons.Count > take)
            Console.WriteLine($"  {s.BlockedReasons.Skip(take).Sum(r => r.Count),6}  (other reasons — see --json)");

        if (s.UndeclaredChrome.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("controls whose chrome is not known (declare under \"chrome\" in xamlfit.config.json to unblock):");
            foreach (var (element, n) in s.UndeclaredChrome.Take(8))
                Console.WriteLine($"  {n,6}  {CheckDiagnostics.ConsoleSafe(element)}");
        }
    }

    /// <summary>
    /// 寫輸出檔。寫不進去是輸入／環境錯誤（exit 2），不是當掉 ——
    /// 第一版讓例外直接飛出去，CI 上看到的是一串堆疊追蹤而不是「這個路徑寫不進去」。
    /// </summary>
    public static bool TryWrite(string path, string content)
    {
        try
        {
            // ⚠ 不替使用者建立目錄。路徑打錯（或被 shell 解讀成別的地方）時，
            //   默默在別處建出一棵目錄樹比直接說「那個目錄不存在」糟得多 ——
            //   實測在 Git Bash 下 /tmp/x/out.json 會被當成目前磁碟根目錄底下的 tmp/x。
            File.WriteAllText(path, content);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"cannot write {path}: {ex.Message}");
            return false;
        }
    }

    public static string Trunc(string s, int n)
    {
        s = CheckDiagnostics.ConsoleSafe(s);
        return s.Length <= n ? s : s[..(n - 1)] + "…";
    }

    /// <summary>
    /// 截斷並補空白到固定的**終端機欄寬**。中日韓全形字佔兩欄 —— 用字元數補，
    /// 實際用在 Kindling 時表格的檔名欄整個歪掉。
    /// </summary>
    public static string Column(string s, int cells)
    {
        s = CheckDiagnostics.ConsoleSafe(s);
        var total = 0;
        foreach (var rune in s.EnumerateRunes()) total += CellWidth(rune.Value);

        var sb = new System.Text.StringBuilder();
        var used = 0;
        if (total <= cells)
        {
            sb.Append(s);
            used = total;
        }
        else
        {
            foreach (var rune in s.EnumerateRunes())
            {
                var w = CellWidth(rune.Value);
                if (used + w > cells - 1) break;   // 留一欄給刪節號
                sb.Append(rune.ToString());
                used += w;
            }
            sb.Append('…');
            used++;
        }
        return sb.Append(' ', Math.Max(0, cells - used)).ToString();
    }

    /// <summary>終端機欄寬：東亞全形與寬字元 2 欄，組合用字元與零寬字元 0 欄，其餘 1 欄。</summary>
    internal static int CellWidth(int cp) =>
        XamlFit.Core.Fonts.TextMeasure.IsDefaultIgnorable(cp)
        || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.EnclosingMark ? 0
        : cp is (>= 0x1100 and <= 0x115F) or (>= 0x2E80 and <= 0x303E) or (>= 0x3041 and <= 0x33FF)
            or (>= 0x3400 and <= 0x4DBF) or (>= 0x4E00 and <= 0x9FFF) or (>= 0xA000 and <= 0xA4CF)
            or (>= 0xAC00 and <= 0xD7A3) or (>= 0xF900 and <= 0xFAFF) or (>= 0xFE30 and <= 0xFE4F)
            or (>= 0xFF00 and <= 0xFF60) or (>= 0xFFE0 and <= 0xFFE6) or (>= 0x1F300 and <= 0x1F64F)
            or (>= 0x1F900 and <= 0x1F9FF) or (>= 0x20000 and <= 0x3FFFD) ? 2
        : 1;
}
