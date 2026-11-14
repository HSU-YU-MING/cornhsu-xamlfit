using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Analysis;

/// <summary>
/// 判定矩陣（規畫書 §4.5）。「塞不下」有四種結局，後果完全不同，不能混成一個 finding。
/// </summary>
public enum FitKind
{
    /// <summary>NoWrap + TextTrimming=None：文字溢出，疊到隔壁或被父層 clip。
    /// ⚠ 這一類**連執行期的 IsTextTrimmedChanged 都不會觸發** —— 只有靜態量得出來。</summary>
    Overflow,
    /// <summary>NoWrap + 省略號：顯示得下但資訊遺失。</summary>
    Trimmed,
    /// <summary>Wrap + 固定高度：行數超出，垂直被裁。</summary>
    VerticalClip,
    /// <summary>Wrap + 自動高度：版面被撐高，水平沒事。</summary>
    Reflow,
    /// <summary>餘裕低於安全邊界。</summary>
    Tight,
}

public enum FitSeverity { Info, Warn, Fail }

public sealed record FitFinding
{
    public required FitKind Kind { get; init; }
    public required FitSeverity Severity { get; init; }
    public required string Key { get; init; }
    public required string Culture { get; init; }
    public required string Text { get; init; }
    public required string Site { get; init; }
    public required double LimitPx { get; init; }
    public required double ActualPx { get; init; }
    public double OverflowPx => ActualPx - LimitPx;
    public string? Warning { get; init; }
    public string? SuppressedReason { get; init; }

    /// <summary>baseline 的比對鍵。⚠ 刻意不含行號也不含像素值 ——
    /// 行號會隨無關編輯漂移，像素值會隨字型度量重產而變。</summary>
    public string BaselineKey => $"{KindSlug(Kind)}|{Culture}|{SiteFile}|{Key}";

    private string SiteFile => Site.Contains(':') ? Site[..Site.LastIndexOf(':')] : Site;

    public static string KindSlug(FitKind k) => k switch
    {
        FitKind.Overflow => "overflow",
        FitKind.Trimmed => "trimmed",
        FitKind.VerticalClip => "vertical-clip",
        FitKind.Reflow => "reflow",
        _ => "tight",
    };

    public static string SeveritySlug(FitSeverity s) => s switch
    {
        FitSeverity.Fail => "fail",
        FitSeverity.Warn => "warn",
        _ => "info",
    };
}

public sealed record CheckSummary
{
    public BudgetSummary Budget { get; init; } = new();
    public int CulturesChecked { get; init; }
    public IReadOnlyList<string> Cultures { get; init; } = [];
    public int StringsChecked { get; init; }
    public int MissingTranslations { get; init; }
    public int Suppressed { get; init; }
    public bool Pseudo { get; init; }
    public Dictionary<string, int> Counts { get; init; } = [];
}

public sealed record CheckResult(CheckSummary Summary, IReadOnlyList<FitFinding> Findings);

public sealed class CheckAnalyzer
{
    private readonly FontResolver _fonts;
    private readonly string _defaultFamily;
    private readonly double _marginPercent;

    public CheckAnalyzer(FontResolver fonts, string defaultFamily, double marginPercent)
    {
        _fonts = fonts;
        _defaultFamily = defaultFamily;
        _marginPercent = marginPercent;
    }

    /// <summary>
    /// 拿真實譯文（或 --pseudo 的模擬值）去量每個可量測的位置。
    /// </summary>
    public CheckResult Analyze(
        ScanResult scan,
        BudgetResult budget,
        IReadOnlyList<CultureStrings> cultures,
        bool pseudo)
    {
        var findings = new List<FitFinding>();
        var missing = 0;
        var checkedStrings = 0;
        var suppressed = 0;

        // 位置依 (資源鍵 or 字面文字) 分組，跟 budget 用同一把鑰匙
        var byKey = scan.Sites
            .Where(s => s.Source is TextSource.Literal or TextSource.Resource)
            .Where(s => BudgetAnalyzer.HasLetter(s.Text))
            .GroupBy(s => s.ResourceKey ?? s.Text, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var b in budget.Budgets)
        {
            if (b.Status != BudgetStatus.Measured) continue;
            if (!byKey.TryGetValue(b.Key, out var sites)) continue;

            // ⚠ **逐位置判定，不是只看最窄的那一個。**
            //   預算取「所有位置的最小值」是為了匯出給譯者（一個數字就夠）；
            //   但檢查不同 —— 同一個字串在 A 位置可能水平溢出、在 B 位置可能垂直被裁，
            //   只量最窄的那個會漏掉另一種。示範專案就是這樣漏掉 vertical-clip 的。
            foreach (var site in sites.Where(s => s.Width.IsMeasurable))
            {
                if (site.SuppressedReason is not null) { suppressed++; continue; }

                var lookup = _fonts.Resolve(site.FontFamily, site.FontWeight, site.FontStyle, _defaultFamily);
                if (lookup.Chain.Count == 0) continue;

                foreach (var (culture, text, isPseudo) in Candidates(b, site, cultures, pseudo))
                {
                    if (text is null) { missing++; continue; }
                    checkedStrings++;

                    var m = TextMeasure.Measure(text, lookup.Chain, site.FontSize, lookup.DeclaredCount);
                    if (!m.IsReliable) continue;   // 量不可信就不評 —— 寧可不報也不要報錯的

                    var finding = Judge(b, site, culture, text, site.Width.Px, m, isPseudo);
                    if (finding is not null) findings.Add(finding);
                }
            }
        }

        var counts = findings
            .GroupBy(f => FitFinding.KindSlug(f.Kind))
            .ToDictionary(g => g.Key, g => g.Count());

        return new CheckResult(
            new CheckSummary
            {
                Budget = budget.Summary,
                CulturesChecked = cultures.Count,
                Cultures = [.. cultures.Select(c => c.Culture).Distinct()],
                StringsChecked = checkedStrings,
                MissingTranslations = missing,
                Suppressed = suppressed,
                Pseudo = pseudo,
                Counts = counts,
            },
            findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.OverflowPx).ToList());
    }

    private IEnumerable<(string Culture, string? Text, bool Pseudo)> Candidates(
        StringBudget b, TextSite site, IReadOnlyList<CultureStrings> cultures, bool pseudo)
    {
        if (pseudo)
        {
            // 沒有翻譯時的退路：照分段膨脹表把來源字串拉長。
            // ⚠ 這報的是**風險**不是事實，所以嚴重度一律降級（見 Judge）。
            //
            // ⚠ 來源字串不只有字面文字。第一版只接受 TextSource.Literal，
            //   於是對 `{x:Static p:Strings.Save}` 這種寫法一筆都量不到 ——
            //   而用 .resx 的專案正是這個模式的主要使用者，等於 --pseudo 對
            //   目標受眾完全無效（示範專案實跑是 measurements 0）。
            //   資源引用要去中性語言的 .resx 取它的值。
            var source = site.Source == TextSource.Literal
                ? site.Text
                : NeutralValueOf(b.Key, cultures);

            if (source is not null)
                yield return ("(pseudo)", Pseudoize(source, ExpansionTable.FactorFor(source)), true);
            yield break;
        }

        foreach (var c in cultures)
        {
            if (c.Culture == ResxReader.NeutralCulture) continue;   // 中性語言就是來源，不用比
            yield return (c.Culture, c.Values.GetValueOrDefault(b.Key), false);
        }
    }

    /// <summary>中性語言（來源語言）的字串值。<c>--pseudo</c> 拿它當膨脹的基底。</summary>
    private static string? NeutralValueOf(string key, IReadOnlyList<CultureStrings> cultures)
    {
        foreach (var c in cultures)
            if (c.Culture == ResxReader.NeutralCulture && c.Values.TryGetValue(key, out var v))
                return v;
        return null;
    }

    /// <summary>把字串拉長到目標倍率。用重複最後一個字來補，寬度才有代表性。</summary>
    internal static string Pseudoize(string source, double factor)
    {
        if (source.Length == 0) return source;
        var target = (int)Math.Ceiling(source.Length * factor);
        if (target <= source.Length) return source;

        var sb = new System.Text.StringBuilder(source);
        var i = 0;
        while (sb.Length < target)
        {
            sb.Append(source[i % source.Length]);
            i++;
        }
        return sb.ToString();
    }

    private FitFinding? Judge(
        StringBudget b, TextSite site, string culture, string text, double limit, Measurement m, bool pseudo)
    {
        var actual = m.WidthPx;
        var overflowing = actual > limit;
        var margin = limit * (1 - _marginPercent / 100.0);

        FitKind kind;
        FitSeverity severity;

        if (overflowing)
        {
            if (site.Wrap == WrapMode.NoWrap)
            {
                if (site.Trim == TrimMode.None) { kind = FitKind.Overflow; severity = FitSeverity.Fail; }
                else { kind = FitKind.Trimmed; severity = FitSeverity.Warn; }
            }
            else if (site.Height is { } h)
            {
                // Wrap + 固定高度：算得出行數才判
                var lines = (int)Math.Ceiling(actual / limit);
                var lineHeight = LineHeightOf(site);
                if (lineHeight > 0 && lines * lineHeight > h)
                { kind = FitKind.VerticalClip; severity = FitSeverity.Fail; }
                else { kind = FitKind.Reflow; severity = FitSeverity.Info; }
            }
            else { kind = FitKind.Reflow; severity = FitSeverity.Info; }
        }
        else if (actual > margin)
        {
            kind = FitKind.Tight; severity = FitSeverity.Warn;
        }
        else return null;

        // ⚠ --pseudo 報的是風險不是事實：一律降一級，絕不讓猜測擋別人的 PR。
        if (pseudo)
            severity = severity switch
            {
                FitSeverity.Fail => FitSeverity.Warn,
                FitSeverity.Warn => FitSeverity.Info,
                _ => FitSeverity.Info,
            };

        return new FitFinding
        {
            Kind = kind,
            Severity = severity,
            Key = b.Key,
            Culture = culture,
            Text = text,
            Site = site.Location,
            LimitPx = Math.Round(limit, 2),
            ActualPx = Math.Round(actual, 2),
            Warning = m.UsedAutoFallback
                ? "measured with an automatic fallback font; Windows font linking may pick a different one"
                : null,
        };
    }

    /// <summary>行高（DIP）。度量檔有 ascender/descender/lineGap 就用它。</summary>
    private double LineHeightOf(TextSite site)
    {
        var lookup = _fonts.Resolve(site.FontFamily, site.FontWeight, site.FontStyle, _defaultFamily);
        if (lookup.Chain.Count == 0) return 0;
        var f = lookup.Chain[0];
        return f.UnitsPerEm == 0 ? 0 : (double)f.LineHeight * site.FontSize / f.UnitsPerEm;
    }
}
