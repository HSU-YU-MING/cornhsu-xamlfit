using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Analysis;

/// <summary>一個字串的預算狀態。⚠ 三者必須分開 —— 輸出裡不准有空白儲存格。</summary>
public enum BudgetStatus
{
    /// <summary>至少有一個位置算得出可用寬度 → 給得出數字預算。</summary>
    Measured,
    /// <summary>所有位置都查不到寬度上界。**這不等於「安全」** ——
    /// 執行期的視窗有寬度。它的意思是「分析範圍內沒有宣告上界」。</summary>
    NoDeclaredLimit,
    /// <summary>有位置看不下去（範本、Grid 星號欄、chrome 未知…）。</summary>
    NotAnalysed,
}

/// <summary>量測用了哪個後端。報告要標 —— 使用者看到數字時要知道它的可信度。</summary>
public enum MeasureBackend { None, Metrics, FontFile }

public sealed record StringBudget
{
    /// <summary>資源鍵；沒有鍵的字面字串就是文字本身。</summary>
    public required string Key { get; init; }
    public required string Text { get; init; }
    public required BudgetStatus Status { get; init; }

    /// <summary>像素預算 = 所有出現位置裡最窄的那一個。Status 不是 Measured 時是 null。</summary>
    public double? LimitPx { get; init; }

    /// <summary>把預算壓到這個值的那個位置。使用者要知道該放寬哪裡。</summary>
    public string? BindingSite { get; init; }

    public string? Font { get; init; }
    public double FontSize { get; init; }

    /// <summary>目前這個字串在卡點位置的實際寬度。</summary>
    public double? SourceWidthPx { get; init; }

    /// <summary>剩餘空間。⚠ 英文就只剩幾 px 的位置，是還沒翻譯就在倒數的地雷。</summary>
    public double? HeadroomPx => LimitPx is { } l && SourceWidthPx is { } s ? l - s : null;

    /// <summary>以目標語言的平均字寬估算裝得下幾個字。
    /// ⚠ 像素才是權威，字元數只是方便值 —— 每個字母寬度不同。</summary>
    public int? LimitChars { get; init; }

    /// <summary>照分段膨脹表，這個字串翻譯後還塞得下嗎。不需要任何翻譯檔就算得出來。</summary>
    public bool? FitsWhenExpanded { get; init; }

    public double ExpansionFactor { get; init; }

    public int SiteCount { get; init; }
    public int MeasuredSiteCount { get; init; }
    public MeasureBackend Backend { get; init; }

    /// <summary>量測不可信的原因（缺字、複雜文字系統）。</summary>
    public string? MeasureWarning { get; init; }
}

public sealed record BudgetSummary
{
    public int Files { get; init; }
    public int ParseErrors { get; init; }
    public int TextSites { get; init; }
    public int LocalizableSites { get; init; }
    public int MeasuredSites { get; init; }

    /// <summary>有宣告上界、本來就該算得出數字，卻卡住的位置（Grid 星號欄、ItemsControl、chrome 未知…）。
    /// 這些才是**工具的失敗**。</summary>
    public int BlockedSites { get; init; }

    /// <summary>查不到任何寬度上界的位置。不是工具的失敗 —— 靜態分析本來就看不到，
    /// 任何版本都一樣。所以不進閘門的分母。</summary>
    public int NoDeclaredLimitSites { get; init; }

    /// <summary>純符號／數字，沒有任何文字 —— 箭頭、圖示字型、「00」。不是翻譯對象。</summary>
    public int SymbolOnlySites { get; init; }

    public int Strings { get; init; }
    public int MeasuredStrings { get; init; }
    public int NoDeclaredLimitStrings { get; init; }
    public int NotAnalysedStrings { get; init; }
    public int Suppressed { get; init; }
    public IReadOnlyList<string> MissingFonts { get; init; } = [];
    public string ChromeTheme { get; init; } = "";

    /// <summary>可量測佔全部可在地化位置的比例（%）。**資訊用，不進閘門。**
    /// 分母含「查不到上界」的位置，而那是靜態分析的天花板，不是這次執行的失敗。</summary>
    public double Coverage => LocalizableSites == 0 ? 0 : 100.0 * MeasuredSites / LocalizableSites;

    /// <summary>
    /// **閘門用的指標**：本來就該算得出數字的位置裡，實際算出來的比例。
    ///
    /// 分母 = 量到的 + 卡住的。刻意**不含**「查不到上界」——
    /// 那是靜態分析的天花板，任何版本都一樣；把它算進分母，閘門會對每個專案開火，
    /// 而永遠開火的閘門會被關掉。
    /// </summary>
    public double Resolution =>
        MeasuredSites + BlockedSites == 0 ? 100 : 100.0 * MeasuredSites / (MeasuredSites + BlockedSites);
}

public sealed record BudgetResult(BudgetSummary Summary, IReadOnlyList<StringBudget> Budgets);

public sealed class BudgetAnalyzer
{
    private readonly FontResolver _fonts;
    private readonly string _defaultFamily;

    public BudgetAnalyzer(FontResolver fonts, string defaultFamily)
    {
        _fonts = fonts;
        _defaultFamily = defaultFamily;
    }

    public BudgetResult Analyze(ScanResult scan, string chromeTheme)
    {
        // 只有字面與資源字串是翻譯對象；綁資料的不是
        var candidates = scan.Sites
            .Where(s => s.Source is TextSource.Literal or TextSource.Resource)
            .ToList();

        // ⚠ 純符號／數字不是翻譯對象。實跑 QuillNest 時「最擠的位置」前十名有八名是
        //   箭頭與圖示字元（→ ↑ ▲ : 00）—— 最有用的那個欄位被雜訊佔滿。
        var symbolOnly = candidates.Count(s => !HasLetter(s.Text));
        var localizable = candidates.Where(s => HasLetter(s.Text)).ToList();

        var missingFonts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var budgets = new List<StringBudget>();

        // 鍵：有資源鍵就用鍵（同一個鍵在不同地方是同一個字串），否則用字面文字
        foreach (var group in localizable.GroupBy(s => s.ResourceKey ?? s.Text, StringComparer.Ordinal))
        {
            var sites = group.ToList();
            var active = sites.Where(s => s.SuppressedReason is null).ToList();
            if (active.Count == 0) continue;

            var measurableSites = active.Where(s => s.Width.IsMeasurable).ToList();

            BudgetStatus status;
            if (measurableSites.Count > 0) status = BudgetStatus.Measured;
            else if (active.All(s => s.Width.Status == WidthStatus.NoDeclaredLimit)) status = BudgetStatus.NoDeclaredLimit;
            else status = BudgetStatus.NotAnalysed;

            var sample = active[0];
            var text = sample.Source == TextSource.Resource ? (sample.ResourceKey ?? sample.Text) : sample.Text;
            var factor = ExpansionTable.FactorFor(sample.Source == TextSource.Literal ? sample.Text : null);

            if (status != BudgetStatus.Measured)
            {
                budgets.Add(new StringBudget
                {
                    Key = group.Key,
                    Text = text,
                    Status = status,
                    SiteCount = sites.Count,
                    MeasuredSiteCount = 0,
                    Backend = MeasureBackend.None,
                    ExpansionFactor = factor,
                });
                continue;
            }

            // 預算 = 最窄的那一個位置
            var binding = measurableSites.MinBy(s => s.Width.Px)!;
            var limit = binding.Width.Px;

            var lookup = _fonts.Resolve(binding.FontFamily, binding.FontWeight, binding.FontStyle, _defaultFamily);
            if (lookup.Chain.Count == 0)
            {
                if (lookup.MissingFamily is { } mf) missingFonts.Add(mf);
                budgets.Add(new StringBudget
                {
                    Key = group.Key,
                    Text = text,
                    Status = BudgetStatus.Measured,
                    LimitPx = Math.Round(limit, 2),
                    BindingSite = binding.Location,
                    Font = binding.FontFamily,
                    FontSize = binding.FontSize,
                    SiteCount = sites.Count,
                    MeasuredSiteCount = measurableSites.Count,
                    Backend = MeasureBackend.None,
                    ExpansionFactor = factor,
                    MeasureWarning = $"no metrics for font family '{lookup.MissingFamily}'",
                });
                continue;
            }

            // 字面字串才量得到來源寬度；資源字串要等 check 模式讀 .resx
            double? sourceWidth = null;
            string? warning = null;
            if (binding.Source == TextSource.Literal)
            {
                var m = TextMeasure.Measure(binding.Text, lookup.Chain, binding.FontSize, lookup.DeclaredCount);
                if (m.IsReliable)
                {
                    sourceWidth = Math.Round(m.WidthPx, 2);
                    if (m.UsedAutoFallback)
                        warning = "measured with an automatic fallback font; Windows font linking may pick a different one";
                }
                else warning = m.ComplexScript
                    ? "complex script: advance widths do not sum correctly"
                    : $"{m.MissingGlyphs} glyph(s) not covered by the metrics";
            }

            var avgChar = AverageCharWidth(lookup.Chain, binding.FontSize);
            var limitChars = avgChar > 0 ? (int)Math.Floor(limit / avgChar) : (int?)null;

            bool? fitsExpanded = sourceWidth is { } sw ? sw * factor <= limit : null;

            budgets.Add(new StringBudget
            {
                Key = group.Key,
                Text = text,
                Status = BudgetStatus.Measured,
                LimitPx = Math.Round(limit, 2),
                BindingSite = binding.Location,
                Font = binding.FontFamily,
                FontSize = binding.FontSize,
                SourceWidthPx = sourceWidth,
                LimitChars = limitChars,
                FitsWhenExpanded = fitsExpanded,
                ExpansionFactor = factor,
                SiteCount = sites.Count,
                MeasuredSiteCount = measurableSites.Count,
                Backend = MeasureBackend.Metrics,
                MeasureWarning = warning,
            });
        }

        var summary = new BudgetSummary
        {
            Files = scan.FilesScanned,
            ParseErrors = scan.ParseErrors,
            TextSites = scan.Sites.Count,
            LocalizableSites = localizable.Count,
            MeasuredSites = localizable.Count(s => s.Width.IsMeasurable),
            BlockedSites = localizable.Count(s => s.Width.Status
                is WidthStatus.NeedsGrid or WidthStatus.NeedsItems or WidthStatus.Unmeasurable),
            NoDeclaredLimitSites = localizable.Count(s => s.Width.Status == WidthStatus.NoDeclaredLimit),
            SymbolOnlySites = symbolOnly,
            Strings = budgets.Count,
            MeasuredStrings = budgets.Count(b => b.Status == BudgetStatus.Measured),
            NoDeclaredLimitStrings = budgets.Count(b => b.Status == BudgetStatus.NoDeclaredLimit),
            NotAnalysedStrings = budgets.Count(b => b.Status == BudgetStatus.NotAnalysed),
            Suppressed = localizable.Count(s => s.SuppressedReason is not null),
            MissingFonts = [.. missingFonts],
            ChromeTheme = chromeTheme,
        };

        return new BudgetResult(summary, budgets.OrderBy(b => b.Key, StringComparer.Ordinal).ToList());
    }

    /// <summary>拉丁字母的平均前進寬度。字元數估計用的 —— 標明它是估計值。</summary>
    private const string LatinSample = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ";

    private static double AverageCharWidth(IReadOnlyList<FontMetrics> chain, double size)
    {
        var m = TextMeasure.Measure(LatinSample, chain, size);
        return m.MissingGlyphs > LatinSample.Length / 2 ? 0 : m.WidthPx / LatinSample.Length;
    }

    /// <summary>字串裡有沒有任何「文字」。箭頭、圖示字型的私有區字元、純數字都沒有。
    /// ⚠ 這決定了什麼東西會出現在給譯者的清單裡 —— 放行符號等於交出一份沒人看的表。</summary>
    internal static bool HasLetter(string s)
    {
        foreach (var rune in s.AsSpan().EnumerateRunes())
        {
            if (System.Text.Rune.IsLetter(rune)) return true;
        }
        return false;
    }
}
