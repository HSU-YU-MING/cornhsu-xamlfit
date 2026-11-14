using System.Text.Json;
using XamlFit.Core.Analysis;

namespace XamlFit.Core.Report;

/// <summary>
/// check 模式的 JSON。
///
/// summary 與 findings 分兩層：CI 只消費 findings 的話，
/// 「有一支字型沒有度量、212 個位置量不到」這件事等於不存在。
/// </summary>
public static class CheckReport
{
    public const int SchemaVersion = 1;

    public static string ToJson(CheckResult result, IReadOnlyList<FitFinding> reported, string toolVersion)
    {
        var s = result.Summary;
        var b = s.Budget;

        var payload = new
        {
            schemaVersion = SchemaVersion,
            toolVersion,
            mode = s.Pseudo ? "check-pseudo" : "check",
            summary = new
            {
                files = b.Files,
                parseErrors = b.ParseErrors,
                localizableSites = b.LocalizableSites,
                symbolOnlySites = b.SymbolOnlySites,
                measuredSites = b.MeasuredSites,
                blockedSites = b.BlockedSites,
                noDeclaredLimitSites = b.NoDeclaredLimitSites,
                resolution = Math.Round(b.Resolution, 1),
                coverage = Math.Round(b.Coverage, 1),
                stringsWithBudget = b.MeasuredStrings,
                cultures = s.Cultures,
                measurements = s.StringsChecked,
                missingTranslations = s.MissingTranslations,
                suppressed = s.Suppressed,
                missingFonts = b.MissingFonts,
                chromeTheme = b.ChromeTheme,
                counts = s.Counts,
                reportedAfterBaseline = reported.Count,
            },
            findings = reported.Select(f => new
            {
                kind = FitFinding.KindSlug(f.Kind),
                severity = FitFinding.SeveritySlug(f.Severity),
                key = f.Key,
                culture = f.Culture,
                text = f.Text,
                site = f.Site,
                limitPx = f.LimitPx,
                actualPx = f.ActualPx,
                overflowPx = Math.Round(f.OverflowPx, 2),
                warning = f.Warning,
            }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }
}
