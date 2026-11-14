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
    /// <summary>
    /// 量得到的位置全部都會換行、而且高度不固定 —— 譯文再長也只是多幾行，沒有「水平預算」可言。
    /// 唯一的限制是單一個詞不能比行寬長（寫在 warning 欄）。
    /// </summary>
    Wraps,
}

/// <summary>量測用了哪個後端。報告要標 —— 使用者看到數字時要知道它的可信度。</summary>
public enum MeasureBackend { None, Metrics, FontFile }

public sealed record StringBudget
{
    /// <summary>資源鍵；沒有鍵的字面字串就是文字本身。</summary>
    public required string Key { get; init; }
    public required string Text { get; init; }
    public required BudgetStatus Status { get; init; }

    /// <summary>像素預算 = 所有出現位置裡**裝得下最少字**的那一個的寬度。Status 不是 Measured 時是 null。</summary>
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

    /// <summary>量測不可信的原因（缺字、複雜文字系統、格式佔位）。</summary>
    public string? MeasureWarning { get; init; }
}

public sealed record BudgetSummary
{
    public int Files { get; init; }
    public int ParseErrors { get; init; }
    public int TextSites { get; init; }
    public int LocalizableSites { get; init; }
    public int MeasuredSites { get; init; }

    /// <summary>有宣告上界、本來就該算得出數字，卻卡住的位置（解不開的 Grid、內距未知、字型沒有度量…）。
    /// 這些才是**工具的失敗**。</summary>
    public int BlockedSites { get; init; }

    /// <summary>查不到任何寬度上界的位置。不是工具的失敗 —— 靜態分析本來就看不到，
    /// 任何版本都一樣。所以不進閘門的分母。</summary>
    public int NoDeclaredLimitSites { get; init; }

    /// <summary>寫在範本或資源裡的位置：寬度由使用端決定。跟「查不到上界」一樣不進閘門的分母，
    /// 但要單獨列出來 —— 它們是實實在在沒被分析的文字。</summary>
    public int InTemplateSites { get; init; }

    /// <summary>純符號／數字，沒有任何文字 —— 箭頭、圖示字型、「00」。不是翻譯對象。</summary>
    public int SymbolOnlySites { get; init; }

    /// <summary>有 ignore 註解但沒寫理由 —— 沒有生效，要大聲講。</summary>
    public int InvalidIgnores { get; init; }

    /// <summary>卡住的位置依原因分類。使用者要知道「我改什麼才能提高覆蓋率」，
    /// 而不是只看到一個 62% 的數字。</summary>
    public IReadOnlyList<(string Reason, int Count)> BlockedReasons { get; init; } = [];

    /// <summary>
    /// 內距不明、把位置卡住的元素（第三方控制項、被外部佈景重畫的標準控制項），依卡住的位置數排序。
    /// 在設定檔的 <c>chrome</c> 裡宣告它們的內距就能解開 —— 報告直接點名，使用者不必自己找。
    /// </summary>
    public IReadOnlyList<(string Element, int Sites)> UndeclaredChrome { get; init; } = [];

    public int Strings { get; init; }
    public int MeasuredStrings { get; init; }
    public int NoDeclaredLimitStrings { get; init; }
    public int WrappingStrings { get; init; }
    public int NotAnalysedStrings { get; init; }
    public int Suppressed { get; init; }
    public IReadOnlyList<string> MissingFonts { get; init; } = [];
    public string ChromeTheme { get; init; } = "";

    /// <summary>不是 WPF 的 XAML 檔（WinUI／UWP、Avalonia、MAUI）：沒有分析。</summary>
    public IReadOnlyList<ForeignFile> ForeignFiles { get; init; } = [];

    /// <summary>專案套用的外部佈景。</summary>
    public IReadOnlyList<ThemeUse> ExternalThemes { get; init; } = [];

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

    /// <summary>哪些位置算「可在地化」：字面與資源字串，而且裡面真的有文字。</summary>
    public static bool IsLocalizable(TextSite s) =>
        s.Source is TextSource.Literal or TextSource.Resource && HasLetter(s.SourceText ?? s.Text);

    private static bool IsBlocked(WidthStatus s) =>
        s is WidthStatus.NeedsGrid or WidthStatus.NeedsItems or WidthStatus.Unmeasurable;

    /// <param name="sourceStrings">來源語言的字串表（.resx 的中性語言）。有的話資源字串也量得到目前的寬度。</param>
    public BudgetResult Analyze(
        ScanResult scan, string chromeTheme, IReadOnlyDictionary<string, string>? sourceStrings = null)
    {
        // 只有字面與資源字串是翻譯對象；綁資料的不是
        var candidates = scan.Sites
            .Where(s => s.Source is TextSource.Literal or TextSource.Resource)
            .ToList();

        // ⚠ 純符號／數字不是翻譯對象。實跑 QuillNest 時「最擠的位置」前十名有八名是
        //   箭頭與圖示字元（→ ↑ ▲ : 00）—— 最有用的那個欄位被雜訊佔滿。
        var localizable = candidates.Where(IsLocalizable).ToList();
        var symbolOnly = candidates.Count - localizable.Count;

        var budgets = new List<StringBudget>();

        // 鍵：有資源鍵就用鍵（同一個鍵在不同地方是同一個字串），否則用字面文字
        foreach (var group in localizable.GroupBy(s => s.ResourceKey ?? s.Text, StringComparer.Ordinal))
        {
            var sites = group.ToList();
            var active = sites.Where(s => s.SuppressedReason is null).ToList();
            if (active.Count == 0) continue;

            var sample = active[0];
            var source = sample.SourceText
                         ?? (sample.ResourceKey is { } rk && sourceStrings is not null && sourceStrings.TryGetValue(rk, out var sv) ? sv : null);
            var text = source ?? sample.ResourceKey ?? sample.Text;
            // 不知道來源文字就不知道膨脹率（0 = 不知道；報表印成空白）
            var factor = source is null ? 0 : ExpansionTable.FactorFor(source);

            var measurable = active.Where(s => s.Width.IsMeasurable).ToList();

            if (measurable.Count == 0)
            {
                budgets.Add(new StringBudget
                {
                    Key = group.Key,
                    Text = text,
                    Status = active.All(s => s.Width.Status is WidthStatus.NoDeclaredLimit or WidthStatus.InTemplate)
                        ? BudgetStatus.NoDeclaredLimit
                        : BudgetStatus.NotAnalysed,
                    SiteCount = sites.Count,
                    MeasuredSiteCount = 0,
                    Backend = MeasureBackend.None,
                    ExpansionFactor = factor,
                });
                continue;
            }

            // ⚠ 卡點 = **裝得下最少字**的位置，不是像素最小的位置。
            //   同一個字串在 200px／字級 24 的標題與 150px／字級 11 的按鈕上各出現一次時，
            //   像素最小的是按鈕，但真正先爆的是標題（它只裝得下一半的字）。
            //   第一版取像素最小，把預算報在不會先出事的那個位置上。
            SiteFit? binding = null;
            foreach (var site in measurable)
            {
                var fit = Fit(site, source);
                if (fit is null) continue;
                if (binding is null || fit.Capacity < binding.Capacity) binding = fit;
            }

            if (binding is null) continue;   // 可量測的位置一定解得出字型（掃描時已經確認過）

            // ⚠ 會換行、高度又不固定的位置沒有水平預算。第一版照樣把行寬當成預算，
            //   於是一段本來就排成三行的說明文字被列在「最擠的位置」第一名、餘裕 −134 px。
            if (binding.Lines == 0)
            {
                budgets.Add(new StringBudget
                {
                    Key = group.Key,
                    Text = text,
                    Status = BudgetStatus.Wraps,
                    BindingSite = binding.Site.Location,
                    Font = binding.Site.FontFamily,
                    FontSize = binding.Site.FontSize,
                    ExpansionFactor = factor,
                    SiteCount = sites.Count,
                    MeasuredSiteCount = measurable.Count,
                    Backend = MeasureBackend.Metrics,
                    MeasureWarning = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"wraps freely; only a single word wider than {binding.Site.Width.Px:F0} px would be cut"),
                });
                continue;
            }

            string? warning = null;
            double? sourceWidth = null;

            if (source is not null)
            {
                if (TextRules.HasPlaceholder(source))
                    warning = "contains a format placeholder: the displayed length depends on run-time values";
                else if (binding.Source is { } m)
                {
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
            }

            var limit = binding.LimitPx;
            var limitChars = binding.AverageChar > 0 ? (int)Math.Floor(limit / binding.AverageChar) : (int?)null;

            if (binding.Lines > 1)
                warning = Join(warning, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"wraps into {binding.Lines} lines of {binding.Site.Width.Px:F0} px (an estimate: line breaks waste space)"));
            if (binding.Site.Width.DependsOnNeighbourText)
                warning = Join(warning, "shares its row with other translatable strings: this limit assumes they keep their source-language width");
            if (binding.Site.Width.AssumedScrollBar)
                warning = Join(warning, "assumes the vertical scroll bar is visible");

            budgets.Add(new StringBudget
            {
                Key = group.Key,
                Text = text,
                Status = BudgetStatus.Measured,
                LimitPx = Math.Round(limit, 2),
                BindingSite = binding.Site.Location,
                Font = binding.Site.FontFamily,
                FontSize = binding.Site.FontSize,
                SourceWidthPx = sourceWidth,
                LimitChars = limitChars,
                FitsWhenExpanded = sourceWidth is { } sw ? sw * factor <= limit : null,
                ExpansionFactor = factor,
                SiteCount = sites.Count,
                MeasuredSiteCount = measurable.Count,
                Backend = MeasureBackend.Metrics,
                MeasureWarning = warning,
            });
        }

        var blocked = localizable.Where(s => IsBlocked(s.Width.Status)).ToList();

        var summary = new BudgetSummary
        {
            Files = scan.FilesScanned,
            ParseErrors = scan.ParseErrors,
            TextSites = scan.Sites.Count,
            LocalizableSites = localizable.Count,
            MeasuredSites = localizable.Count(s => s.Width.IsMeasurable),
            BlockedSites = blocked.Count,
            NoDeclaredLimitSites = localizable.Count(s => s.Width.Status == WidthStatus.NoDeclaredLimit),
            InTemplateSites = localizable.Count(s => s.Width.Status == WidthStatus.InTemplate),
            SymbolOnlySites = symbolOnly,
            InvalidIgnores = localizable.Count(s => s.InvalidIgnore),
            BlockedReasons = blocked
                .GroupBy(s => NormalizeReason(s.Width.Reason))
                .Select(g => (g.Key, g.Count()))
                .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal)
                .ToList(),
            UndeclaredChrome = blocked
                .Select(s => UnknownChromeElement(s.Width.Reason))
                .Where(e => e is not null)
                .GroupBy(e => e!, StringComparer.Ordinal)
                .Select(g => (g.Key, g.Count()))
                .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal)
                .ToList(),
            Strings = budgets.Count,
            MeasuredStrings = budgets.Count(b => b.Status == BudgetStatus.Measured),
            NoDeclaredLimitStrings = budgets.Count(b => b.Status == BudgetStatus.NoDeclaredLimit),
            WrappingStrings = budgets.Count(b => b.Status == BudgetStatus.Wraps),
            NotAnalysedStrings = budgets.Count(b => b.Status == BudgetStatus.NotAnalysed),
            Suppressed = localizable.Count(s => s.SuppressedReason is not null),
            MissingFonts = scan.MissingFonts,
            ChromeTheme = chromeTheme,
            ForeignFiles = scan.ForeignFiles,
            ExternalThemes = scan.ExternalThemes,
        };

        return new BudgetResult(summary, budgets.OrderBy(b => b.Key, StringComparer.Ordinal).ToList());
    }

    /// <summary>一個位置對一個字串的容納度。</summary>
    /// <param name="Lines">1 = 單行；大於 1 = 會換行而且高度固定，放得下這麼多行；
    /// 0 = 會換行而且高度不固定（沒有水平預算）。</param>
    /// <param name="LimitPx">這個位置總共放得下多寬的文字（行寬 × 行數）。</param>
    private sealed record SiteFit(
        TextSite Site, double AverageChar, double Capacity, Measurement? Source, int Lines, double LimitPx);

    private static string Join(string? a, string b) => a is null ? b : a + "; " + b;

    private SiteFit? Fit(TextSite site, string? source)
    {
        var lookup = _fonts.Resolve(site.FontFamily, site.FontWeight, site.FontStyle, _defaultFamily);
        if (lookup.Chain.Count == 0) return null;

        var average = AverageCharWidth(lookup.Chain, site.FontSize);
        Measurement? measured = source is null ? null : SiteMeasure.Widest(site, source, lookup);

        // 換行有沒有作用：水平 StackPanel、Auto 欄、WrapPanel 裡的 Wrap 不會換行
        var wraps = site.Wrap != WrapMode.NoWrap && !site.Width.WrapDefeated;
        var lines = 1;
        if (wraps)
        {
            var lineHeight = SiteMeasure.LineHeight(site, lookup);
            lines = site.Height is { } h && lineHeight > 0 ? Math.Max(1, (int)Math.Floor(h / lineHeight)) : 0;
        }

        var limit = site.Width.Px * Math.Max(1, lines);

        // 排序用的容納度（愈小愈擠）。自由換行的位置排最後：有任何一個會裁切的位置就以它為準。
        var capacity = lines == 0 ? double.MaxValue
            : measured is { IsReliable: true } m ? limit - m.WidthPx
            : average > 0 ? limit / average : limit / Math.Max(1, site.FontSize);

        return new SiteFit(site, average, capacity, measured, lines, limit);
    }

    /// <summary>拉丁字母的平均前進寬度。字元數估計用的 —— 標明它是估計值。</summary>
    private const string LatinSample = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ";

    private static double AverageCharWidth(IReadOnlyList<FontMetrics> chain, double size)
    {
        var m = TextMeasure.Measure(LatinSample, chain, size);
        return m.MissingGlyphs > LatinSample.Length / 2 ? 0 : m.WidthPx / LatinSample.Length;
    }

    /// <summary>「chrome of &lt;ui:Card&gt; is unknown: …」裡的元素名。其他原因回 null。</summary>
    private static string? UnknownChromeElement(string reason)
    {
        const string Head = "chrome of ";
        var start = reason.IndexOf(Head, StringComparison.Ordinal);
        if (start < 0) return null;
        var lt = reason.IndexOf('<', start);
        var gt = lt < 0 ? -1 : reason.IndexOf('>', lt);
        if (gt < 0) return null;

        // 範本被換掉、內容寬度不往外傳：宣告內距解決不了，不列
        if (reason.Contains("replaces the ControlTemplate", StringComparison.Ordinal)
            || reason.Contains("has its own ControlTemplate", StringComparison.Ordinal))
            return null;

        var name = reason[(lt + 1)..gt];
        return reason.Contains("the header of", StringComparison.Ordinal) ? name + " (header)" : name;
    }

    /// <summary>把原因收斂成少數幾類，否則每個位置一條訊息、統計沒有意義。</summary>
    private static string NormalizeReason(string reason)
    {
        bool Has(string s) => reason.Contains(s, StringComparison.Ordinal);

        // 保留控制項名稱：使用者要知道卡的是哪一個，才能判斷值不值得處理
        if (Has("chrome of"))
        {
            var lt = reason.IndexOf('<');
            var gt = reason.IndexOf('>');
            var name = lt >= 0 && gt > lt ? reason[(lt + 1)..gt] : "?";
            if (Has("external theme")) return $"control chrome is unknown under the external theme: {name}";
            if (Has("ControlTemplate")) return $"control template is replaced: {name}";
            if (Has("not defined in the project") || Has("cannot read")) return $"control is styled from outside the project: {name}";
            if (Has("not a built-in WPF element")) return $"third-party control, chrome not declared: {name}";
            return $"control chrome is unknown: {name}";
        }

        if (Has("no metrics for")) return reason;   // 已經夠短，而且字型名是使用者要的資訊
        if (Has("TextFormattingMode")) return "TextFormattingMode=\"Display\" (pixel-snapped glyph widths)";
        if (Has("FontStretch")) return "FontStretch selects a face this tool does not measure";
        if (Has("FontFamily is not statically readable")) return "FontFamily is not statically readable";
        if (Has("FontSize is not statically readable") || Has("FontSize=")) return "FontSize is not statically readable";
        if (Has("FontWeight")) return "FontWeight is not statically readable";
        if (Has("FontStyle is not")) return "FontStyle is not statically readable";

        if (Has("shares its line")) return "a Run that shares its line with other inline content";
        if (Has("shares the width of")) return "shares a panel with siblings whose width is unknown";
        if (Has("DockPanel")) return "DockPanel layout is not statically readable";
        if (Has("UniformGrid")) return "UniformGrid column count is unknown";

        if (Has("not statically knowable")) return "Auto column: a child's width is unknown";
        if (Has("spanning child") || Has("spans an Auto column")) return "Auto column: a spanning child contributes";
        if (Has("SharedSizeGroup")) return "SharedSizeGroup column";
        if (Has("own width is")) return "the grid's own width is not resolvable";
        if (Has("a width this tool cannot read")) return "column width this tool cannot read";
        if (Has("already fill the grid")) return "the other columns already fill the grid";
        if (Has("too deep")) return "layout nesting too deep";

        if (Has("custom ItemsPanel")) return "list with a custom ItemsPanel";
        if (Has("GridView columns") || Has("cell widths come from")) return "inside a DataGrid / GridView (column-sized)";
        if (Has("depends on the other columns")) return "column header whose width depends on other columns";

        if (Has("Margin of") || Has("Margin=")) return "Margin is not statically readable";
        if (Has("Padding of") || Has("Padding=")) return "Padding is not statically readable";
        if (Has("BorderThickness")) return "BorderThickness is not statically readable";
        if (Has("MaxWidth")) return "MaxWidth is not statically readable";
        if (Has("Width of") || Has("Width=")) return "Width is not statically readable";
        if (Has("scrolling of") || Has("ScrollBarVisibility")) return "scroll bar visibility is not statically readable";
        if (Has("Orientation of")) return "Orientation is not statically readable";
        if (Has("LayoutTransform")) return "LayoutTransform changes the layout size";
        if (Has("placed through")) return "placed through a property this tool does not model";
        if (Has("frame of <Window>")) return "window frame is not statically readable";
        if (Has("tab strip") || Has("header of <Expander>")) return "header placed beside the content";
        if (Has("exceed the bound") || Has("exceeds the column")) return "insets exceed the bound";
        return reason;
    }

    /// <summary>字串裡有沒有任何「文字」。箭頭、圖示字型的私有區字元、純數字都沒有。
    /// ⚠ 這決定了什麼東西會出現在給譯者的清單裡 —— 放行符號等於交出一份沒人看的表。</summary>
    public static bool HasLetter(string s)
    {
        if (TextRules.IsPathData(s)) return false;

        foreach (var rune in s.AsSpan().EnumerateRunes())
        {
            if (System.Text.Rune.IsLetter(rune)) return true;
        }
        return false;
    }
}

/// <summary>在一個位置上量一段文字：套用顯示規則（快速鍵底線）、多行取最寬的一行。</summary>
public static class SiteMeasure
{
    /// <summary>行高（DIP），用主要字型的 ascender／descender／lineGap。</summary>
    public static double LineHeight(TextSite site, FontLookup lookup)
    {
        if (lookup.Chain.Count == 0) return 0;
        var f = lookup.Chain[0];
        return f.UnitsPerEm == 0 ? 0 : (double)f.LineHeight * site.FontSize / f.UnitsPerEm;
    }

    public static Measurement Widest(TextSite site, string text, FontLookup lookup)
    {
        double widest = 0;
        var missing = 0;
        var complex = false;
        var usedAuto = false;
        var firstMissing = -1;

        foreach (var line in TextRules.Lines(TextRules.Display(site.Element, text)))
        {
            var m = TextMeasure.Measure(line, lookup.Chain, site.FontSize, lookup.DeclaredCount);
            widest = Math.Max(widest, m.WidthPx);
            missing += m.MissingGlyphs;
            complex |= m.ComplexScript;
            usedAuto |= m.UsedAutoFallback;
            if (firstMissing < 0) firstMissing = m.FirstMissing;
        }

        return new Measurement(widest, missing, complex, usedAuto) { FirstMissing = firstMissing };
    }
}
