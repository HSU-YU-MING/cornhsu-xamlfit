using System.Globalization;
using System.Text;
using XamlFit.Core.Analysis;

namespace XamlFit.Core.Report;

/// <summary>
/// Markdown 報告（<c>--md</c>）：給 PR 留言用（照 XamlContrast 的 ToMarkdown）。
///
/// 行內註記只標在出問題的那一行，PR 上沒有總覽：reviewer 看不到「總共幾筆、哪些語言整批沒檢查、解析度多少」。
/// 「看不到的東西」放最前面 —— 本工具的憲法是「工具沒看到」與「看到了沒問題」要分得開，那必須出現在人會看的那份報告裡。
///
/// ⚠ 這份報告會貼成 PR 留言，裡面的鍵名、檔案路徑、譯文、語言名稱都來自 repo（不可信的輸入）。
///   不是工具自己寫的字串一律走 <see cref="Code"/>：GitHub 只有在 code span 裡不處理連結、圖片、@ 提及與 www. 自動連結
///   （Parity 與 XamlContrast 都因此出過安全修正）。
/// </summary>
public static class CheckMarkdown
{
    /// <summary>每一個嚴重度最多列幾筆；完整的在 JSON 報告裡。</summary>
    private const int RowsPerSection = 50;

    /// <param name="notChecked">整批沒檢查的語言與原因（工具自己寫的句子，語言名稱照樣走 code span）。</param>
    public static string ToMarkdown(
        CheckResult result, IReadOnlyList<FitFinding> reported, int exitCode, int baselined,
        IReadOnlyList<string> parseErrorFiles, IReadOnlyList<(string Cultures, string Font)> notChecked)
    {
        var inv = CultureInfo.InvariantCulture;
        var s = result.Summary;
        var b = s.Budget;
        var sb = new StringBuilder();
        int Count(FitSeverity v) => reported.Count(f => f.Severity == v);

        sb.AppendLine("## XamlFit — text-fit check");
        sb.AppendLine();
        var verdict = exitCode switch
        {
            0 => "✅ **PASS**",
            1 => "❌ **FAIL**",
            3 => "⚠️ **NOT TRUSTWORTHY** (exit 3)",
            _ => $"exit {exitCode.ToString(inv)}",
        };
        sb.AppendLine(string.Create(inv,
            $"{verdict} · **{Count(FitSeverity.Fail)} fail**, {Count(FitSeverity.Warn)} warn, {Count(FitSeverity.Info)} info · " +
            $"{s.Measurements} measurement(s) of {b.MeasuredSites} site(s) in {b.Files} XAML file(s) · " +
            $"resolution {b.Resolution:F1}%"));
        sb.AppendLine();
        sb.AppendLine(s.Pseudo
            ? "Mode: `--pseudo` (simulated expansion — reports risk, never fails the build)."
            : "Cultures: " + (s.Cultures.Count > 0 ? string.Join(", ", s.Cultures.Select(c => Code(c, table: false))) : "none (source language only)") + ".");
        sb.AppendLine();

        // ── 看不到的東西，放最前面 ──
        if (exitCode == 3)
        {
            sb.AppendLine("> ⚠️ **The result can't be trusted.** Too little was measured to call this a pass; see the console output of the run for the reason.");
            sb.AppendLine();
        }
        foreach (var (cultures, font) in notChecked)
        {
            sb.AppendLine($"> ⚠️ **Not checked:** {Code(cultures, table: false)} — text without a FontFamily is drawn with {Code(font, table: false)} on that language's Windows, and the metrics file has no such font.");
            sb.AppendLine();
        }
        foreach (var file in parseErrorFiles)
        {
            sb.AppendLine($"> ⚠️ **Parse failed:** {Code(file, table: false)} — this file is missing from the check above.");
            sb.AppendLine();
        }
        if (s.Unreliable > 0)
        {
            sb.AppendLine(string.Create(inv, $"> ⚠️ **{s.Unreliable} measurement(s) not judged** because the width can't be trusted:"));
            foreach (var (culture, reason, n) in s.UnreliableReasons.Take(5))
                sb.AppendLine(string.Create(inv, $"> - {n} × {Code(culture, table: false)} {Code(reason, table: false)}"));
            sb.AppendLine();
        }

        // ── findings ──
        foreach (var severity in (ReadOnlySpan<FitSeverity>)[FitSeverity.Fail, FitSeverity.Warn])
        {
            var group = reported.Where(f => f.Severity == severity).ToList();
            if (group.Count == 0) continue;
            sb.AppendLine(string.Create(inv, $"### {(severity == FitSeverity.Fail ? "Fail" : "Warn")} ({group.Count})"));
            sb.AppendLine();
            Table(sb, group.Take(RowsPerSection));
            if (group.Count > RowsPerSection)
                sb.AppendLine(string.Create(inv, $"<sub>… and {group.Count - RowsPerSection} more — see the JSON report artifact.</sub>")).AppendLine();
        }

        var info = reported.Where(f => f.Severity == FitSeverity.Info).ToList();
        if (info.Count > 0)
        {
            sb.AppendLine(string.Create(inv, $"<details><summary>{info.Count} info finding(s) (do not fail the build)</summary>"));
            sb.AppendLine();
            Table(sb, info.Take(RowsPerSection));
            if (info.Count > RowsPerSection)
                sb.AppendLine(string.Create(inv, $"<sub>… and {info.Count - RowsPerSection} more — see the JSON report artifact.</sub>")).AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        // ── 放過的東西一律計數 ──
        var notes = new List<string>();
        if (baselined > 0) notes.Add(string.Create(inv, $"{baselined} in the baseline"));
        if (b.BlockedSites > 0) notes.Add(string.Create(inv, $"{b.BlockedSites} site(s) blocked (width not readable)"));
        if (b.NoDeclaredLimitSites > 0) notes.Add(string.Create(inv, $"{b.NoDeclaredLimitSites} with no width bound"));
        if (b.InTemplateSites > 0) notes.Add(string.Create(inv, $"{b.InTemplateSites} in templates"));
        if (s.Placeholders > 0) notes.Add(string.Create(inv, $"{s.Placeholders} with run-time placeholders like {{0}}"));
        if (s.Suppressed > 0) notes.Add(string.Create(inv, $"{s.Suppressed} suppressed via xamlfit-ignore"));
        if (notes.Count > 0)
        {
            sb.AppendLine($"**Not checked for width:** {string.Join(" · ", notes)}");
            sb.AppendLine();
        }

        sb.AppendLine("<sub>Generated by XamlFit · static text-fit check for WPF XAML</sub>");
        return sb.ToString();
    }

    private static void Table(StringBuilder sb, IEnumerable<FitFinding> findings)
    {
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine("| Location | Kind | Culture | Key | Text | Detail |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var f in findings)
        {
            var detail = f.IsFit
                ? string.Create(inv, $"needs {f.ActualPx:0.#} px, has {f.LimitPx:0.#} px")
                  + (f.Warning is { } w ? " · " + Code(w) : "")
                : Code(f.Warning ?? "");
            sb.AppendLine(
                $"| {Code(f.Site)} | {FitFinding.KindSlug(f.Kind)} | {Code(f.Culture)} | {Code(Trunc(f.Key, 60))} | {Code(Trunc(f.Text, 80))} | {detail} |");
        }
        sb.AppendLine();
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    /// <summary>Markdown 的 code span（照 XamlContrast 的 Report.Code）：圍欄用比內容裡最長的反引號串多一個的反引號（CommonMark），
    /// 內容裡有反引號時兩邊各墊一個空白。換行收成空白。表格裡的 | 要跳脫（GFM）；表格外的 code span 不處理反斜線，所以 table: false 不跳脫。</summary>
    internal static string Code(string s, bool table = true)
    {
        var body = (table ? s.Replace("|", "\\|") : s).Replace("\r", " ").Replace("\n", " ");
        var longest = 0;
        for (int i = 0, run = 0; i < body.Length; i++)
        {
            run = body[i] == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', longest + 1);
        var pad = longest > 0 ? " " : "";
        return fence + pad + body + pad + fence;
    }
}
