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
                inTemplateSites = b.InTemplateSites,
                blockedReasons = b.BlockedReasons.Select(r => new { reason = r.Reason, sites = r.Count }),
                undeclaredChrome = b.UndeclaredChrome.Select(c => new { element = c.Element, sites = c.Sites }),
                foreignFiles = b.ForeignFiles.GroupBy(f => f.Dialect).Select(g => new { dialect = g.Key, files = g.Count() }),
                externalThemes = b.ExternalThemes.Select(t => new { theme = t.Theme, declaredIn = t.DeclaredIn, scope = t.Scope.Length == 0 ? "(everything scanned)" : t.Scope, measured = t.Profile, note = t.Profile is null ? t.ProfileNote : null }),
                resolution = Math.Round(b.Resolution, 1),
                coverage = Math.Round(b.Coverage, 1),
                stringsWithBudget = b.MeasuredStrings,
                cultures = s.Cultures,
                measurements = s.Measurements,
                translatedMeasurements = s.TranslatedMeasurements,
                notJudgedUnreliable = s.Unreliable,
                unreliableReasons = s.UnreliableReasons.Select(u => new { culture = u.Culture, reason = u.Reason, count = u.Count }),
                uncoveredCodepoints = s.UncoveredCodepoints.Select(u => new { codepoint = $"U+{u.Codepoint:X4}", count = u.Count }),
                notJudgedPlaceholders = s.Placeholders,
                noSourceText = s.NoSourceText,
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
                // 翻譯完整度的 finding 沒有寬度：null，不是 0（0 會被讀成「剛好放得下」）
                limitPx = f.IsFit ? f.LimitPx : (double?)null,
                actualPx = f.IsFit ? f.ActualPx : (double?)null,
                overflowPx = f.IsFit ? Math.Round(f.OverflowPx, 2) : (double?)null,
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
