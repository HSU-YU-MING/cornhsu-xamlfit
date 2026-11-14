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
    public static string ToCsv(BudgetResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# xamlfit budget — limit_px is authoritative; limit_chars is an estimate (glyph widths differ)");
        sb.AppendLine("key,limit_px,limit_chars,status,font,size,binding_site,source,source_px,headroom_px,fits_expanded,expansion,sites,measured_sites,backend,warning");

        foreach (var b in result.Budgets)
        {
            sb.Append(Csv(b.Key)).Append(',');
            sb.Append(b.LimitPx?.ToString("F1", CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(b.LimitChars?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(StatusSlug(b.Status)).Append(',');
            sb.Append(Csv(b.Font ?? "")).Append(',');
            sb.Append(b.FontSize > 0 ? b.FontSize.ToString("G", CultureInfo.InvariantCulture) : "").Append(',');
            sb.Append(Csv(b.BindingSite ?? "")).Append(',');
            sb.Append(Csv(b.Text)).Append(',');
            sb.Append(b.SourceWidthPx?.ToString("F1", CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(b.HeadroomPx?.ToString("F1", CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(b.FitsWhenExpanded is null ? "" : b.FitsWhenExpanded.Value ? "yes" : "no").Append(',');
            sb.Append(b.ExpansionFactor.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(b.SiteCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(b.MeasuredSiteCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(b.Backend.ToString().ToLowerInvariant()).Append(',');
            sb.Append(Csv(b.MeasureWarning ?? ""));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string ToJson(BudgetResult result, string toolVersion)
    {
        var s = result.Summary;
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
                measuredSites = s.MeasuredSites,
                coverage = Math.Round(s.Coverage, 1),
                strings = s.Strings,
                measuredStrings = s.MeasuredStrings,
                noDeclaredLimitStrings = s.NoDeclaredLimitStrings,
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
                expansionFactor = b.ExpansionFactor,
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
        _ => "not-analysed",
    };

    private static string Csv(string v)
    {
        if (v.Length == 0) return "";
        var needsQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        if (!needsQuote) return v;
        return '"' + v.Replace("\"", "\"\"") + '"';
    }
}
