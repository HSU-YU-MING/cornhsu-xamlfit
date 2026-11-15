using System.Globalization;
using System.Text;
using System.Text.Json;
using XamlFit.Core.Analysis;

namespace XamlFit.Core.Report;

/// <summary>budget 模式的輸出：給翻譯平台的 CSV，與給 CI 的 JSON。</summary>
public static class BudgetReport
{
    public const int SchemaVersion = 1;

    /// <summary>
    /// CSV。欄位順序照 Smartling 的慣例：**長度限制放在來源字串前面一欄**。
    ///
    /// ⚠ `limit_px` 是權威，`limit_chars` 只是估計 —— 每個字母寬度不同，
    ///   字元數過關的翻譯還是可能要重寫。表頭會帶這句話。
    /// ⚠ 預算欄永遠不是空白：沒有數字時寫 `no-declared-limit` 或 `not-analysed`。
    /// </summary>
    /// <param name="neutralizeFormulas">
    /// 把以 <c>= + - @</c>、Tab 或 CR 開頭的欄位前面加一個單引號。
    ///
    /// **為什麼預設開啟**：這份 CSV 的用途就是讓人在 Excel／Sheets 打開再交給譯者。
    /// 試算表會把 <c>=cmd|…</c> 當公式執行，這是標準的 CSV 公式注入。
    ///
    /// **為什麼要有關掉的選項**：UI 字串真的會以 <c>+</c> 或 <c>-</c> 開頭
    /// （實查 QuillNest 有「+ 新增事件」），加上去的單引號會**汙染交給譯者的原文**。
    /// 直接餵進翻譯平台 API 的流程要用 <c>--csv-raw</c> 拿到逐位元組相同的值。
    /// </param>
    public static string ToCsv(BudgetResult result, bool neutralizeFormulas = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# xamlfit budget — limit_px is authoritative; limit_chars is an estimate (glyph widths differ)");
        sb.AppendLine("# status: measured = limit_px applies | wraps = the text wraps freely (no horizontal limit) | " +
                      "no-declared-limit = no width bound found in the XAML (NOT a guarantee) | not-analysed = a bound exists but could not be resolved");
        if (neutralizeFormulas)
            sb.AppendLine("# text fields starting with = + - @ are prefixed with ' to stop spreadsheets " +
                          "treating them as formulas; use --csv-raw for byte-exact values");
        sb.AppendLine("key,limit_px,limit_chars,status,font,size,binding_site,source,source_px,headroom_px,fits_expanded,expansion,sites,measured_sites,backend,warning");

        foreach (var b in result.Budgets)
        {
            sb.Append(Csv(b.Key, neutralizeFormulas)).Append(',');
            sb.Append(b.LimitPx?.ToString("F1", CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(b.LimitChars?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(StatusSlug(b.Status)).Append(',');
            sb.Append(Csv(b.Font ?? "", neutralizeFormulas)).Append(',');
            sb.Append(b.FontSize > 0 ? b.FontSize.ToString("G", CultureInfo.InvariantCulture) : "").Append(',');
            sb.Append(Csv(b.BindingSite ?? "", neutralizeFormulas)).Append(',');
            sb.Append(Csv(b.Text, neutralizeFormulas)).Append(',');
            sb.Append(b.SourceWidthPx?.ToString("F1", CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(b.HeadroomPx?.ToString("F1", CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(b.FitsWhenExpanded is null ? "" : b.FitsWhenExpanded.Value ? "yes" : "no").Append(',');
            sb.Append(b.ExpansionFactor > 0 ? b.ExpansionFactor.ToString("F2", CultureInfo.InvariantCulture) : "").Append(',');
            sb.Append(b.SiteCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(b.MeasuredSiteCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(b.Backend.ToString().ToLowerInvariant()).Append(',');
            sb.Append(Csv(b.MeasureWarning ?? "", neutralizeFormulas));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 每個文字位置一列：狀態、上界從哪來、為什麼卡住。
    /// 摘要只說「有 46 個位置卡在 Padding 讀不出來」；要知道是**哪 46 個**、該改哪一行，看這份。
    /// </summary>
    public static string SitesCsv(Xaml.ScanResult scan, bool neutralizeFormulas = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("file,line,element,source,status,available_px,bound,font,size,weight,wrap,key,text,reason");

        foreach (var s in scan.Sites.OrderBy(s => s.File, StringComparer.Ordinal).ThenBy(s => s.Line))
        {
            var status = s.Source == Xaml.TextSource.DataBound ? "data-bound"
                : !BudgetAnalyzer.IsLocalizable(s) ? "symbol-only"
                : s.SuppressedReason is not null ? "suppressed"
                : s.Width.Status switch
                {
                    Layout.WidthStatus.Known => "measured",
                    Layout.WidthStatus.NoDeclaredLimit => "no-declared-limit",
                    Layout.WidthStatus.InTemplate => "in-template",
                    _ => "blocked",
                };

            sb.Append(Csv(s.File, neutralizeFormulas)).Append(',');
            sb.Append(s.Line.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(s.Element, neutralizeFormulas)).Append(',');
            sb.Append(s.Source.ToString().ToLowerInvariant()).Append(',');
            sb.Append(status).Append(',');
            sb.Append(s.Width.IsMeasurable ? s.Width.Px.ToString("F1", CultureInfo.InvariantCulture) : "").Append(',');
            sb.Append(Csv(s.Width.BoundSource, neutralizeFormulas)).Append(',');
            sb.Append(Csv(s.FontFamily ?? "", neutralizeFormulas)).Append(',');
            sb.Append(s.FontSize.ToString("G", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.FontWeight.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.Width.WrapDefeated ? "nowrap(defeated)" : s.Wrap.ToString().ToLowerInvariant()).Append(',');
            sb.Append(Csv(s.ResourceKey ?? "", neutralizeFormulas)).Append(',');
            sb.Append(Csv(s.SourceText ?? s.Text, neutralizeFormulas)).Append(',');
            sb.Append(Csv(s.SuppressedReason ?? s.Width.Reason, neutralizeFormulas));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string ToJson(BudgetResult result, string toolVersion)
    {
        var s = result.Summary;
        var b = s;
        var payload = new
        {
            schemaVersion = SchemaVersion,
            toolVersion,
            mode = "budget",
            summary = new
            {
                files = s.Files,
                parseErrors = s.ParseErrors,
                textSites = s.TextSites,
                localizableSites = s.LocalizableSites,
                symbolOnlySites = s.SymbolOnlySites,
                measuredSites = s.MeasuredSites,
                blockedSites = s.BlockedSites,
                noDeclaredLimitSites = s.NoDeclaredLimitSites,
                inTemplateSites = b.InTemplateSites,
                blockedReasons = b.BlockedReasons.Select(r => new { reason = r.Reason, sites = r.Count }),
                undeclaredChrome = b.UndeclaredChrome.Select(c => new { element = c.Element, sites = c.Sites }),
                foreignFiles = b.ForeignFiles.GroupBy(f => f.Dialect).Select(g => new { dialect = g.Key, files = g.Count() }),
                externalThemes = b.ExternalThemes.Select(t => new { theme = t.Theme, declaredIn = t.DeclaredIn, scope = t.Scope.Length == 0 ? "(everything scanned)" : t.Scope, measured = t.Profile, note = t.Profile is null ? t.ProfileNote : null }),
                resolution = Math.Round(s.Resolution, 1),
                coverage = Math.Round(s.Coverage, 1),
                strings = s.Strings,
                measuredStrings = s.MeasuredStrings,
                noDeclaredLimitStrings = s.NoDeclaredLimitStrings,
                wrappingStrings = s.WrappingStrings,
                notAnalysedStrings = s.NotAnalysedStrings,
                suppressed = s.Suppressed,
                missingFonts = s.MissingFonts,
                chromeTheme = s.ChromeTheme,
            },
            budgets = result.Budgets.Select(b => new
            {
                key = b.Key,
                text = b.Text,
                status = StatusSlug(b.Status),
                limitPx = b.LimitPx,
                limitChars = b.LimitChars,
                bindingSite = b.BindingSite,
                font = b.Font,
                fontSize = b.FontSize > 0 ? b.FontSize : (double?)null,
                sourceWidthPx = b.SourceWidthPx,
                headroomPx = b.HeadroomPx,
                fitsWhenExpanded = b.FitsWhenExpanded,
                expansionFactor = b.ExpansionFactor > 0 ? b.ExpansionFactor : (double?)null,
                sites = b.SiteCount,
                measuredSites = b.MeasuredSiteCount,
                backend = b.Backend.ToString().ToLowerInvariant(),
                warning = b.MeasureWarning,
            }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }

    public static string StatusSlug(BudgetStatus s) => s switch
    {
        BudgetStatus.Measured => "measured",
        BudgetStatus.NoDeclaredLimit => "no-declared-limit",
        BudgetStatus.Wraps => "wraps",
        _ => "not-analysed",
    };

    private static string Csv(string v, bool neutralizeFormulas = false)
    {
        if (v.Length == 0) return "";

        // CSV 公式注入：試算表會把 = + - @、Tab、CR 開頭的儲存格當成公式。
        // 這份檔案的用途就是在 Excel／Sheets 打開，所以預設要擋。
        // ⚠ 只套用在文字欄位；數字欄位是我們自己格式化出來的。
        if (neutralizeFormulas && IsFormulaLike(v)) v = "'" + v;

        var needsQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
                         || v.StartsWith('\'');
        if (!needsQuote) return v;
        return '"' + v.Replace("\"", "\"\"") + '"';
    }

    internal static bool IsFormulaLike(string v) =>
        v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
}
