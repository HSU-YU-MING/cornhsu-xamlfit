using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Analysis;

/// <summary>
/// 判定矩陣（規畫書 §4.5）。「塞不下」有四種結局，後果完全不同，不能混成一個 finding。
/// </summary>
public enum FitKind
{
    /// <summary>不換行 + TextTrimming=None：文字溢出，疊到隔壁或被父層 clip。
    /// ⚠ 這一類**連執行期的 IsTextTrimmedChanged 都不會觸發** —— 只有靜態量得出來。</summary>
    Overflow,
    /// <summary>不換行 + 省略號：顯示得下但資訊遺失。</summary>
    Trimmed,
    /// <summary>換行 + 固定高度：行數超出，垂直被裁。</summary>
    VerticalClip,
    /// <summary>換行 + 自動高度：行數比來源語言多，版面被撐高，水平沒事。</summary>
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
    public IReadOnlyList<string> Cultures { get; init; } = [];

    /// <summary>實際做了判定的（位置 × 語言）筆數。</summary>
    public int Measurements { get; init; }

    /// <summary>其中屬於譯文（不是來源語言自己）的筆數。</summary>
    public int TranslatedMeasurements { get; init; }

    /// <summary>每個語言判定了幾筆、有幾筆因為量測不可信而沒有判定。
    /// 一個語言整批沒判定時，呼叫端要把它當成「結果不可信」。</summary>
    public IReadOnlyList<(string Culture, int Measured, int Unreliable)> PerCulture { get; init; } = [];

    /// <summary>
    /// 量了但**不可信、所以沒有判定**的筆數（譯文裡有度量檔沒涵蓋的字、複雜文字系統）。
    /// ⚠ 第一版對這些一聲不吭地跳過 —— 於是「阿拉伯文全部沒檢查」看起來跟「阿拉伯文全部通過」一樣。
    /// </summary>
    public int Unreliable { get; init; }

    /// <summary>不可信的原因與筆數，依語言。</summary>
    public IReadOnlyList<(string Culture, string Reason, int Count)> UnreliableReasons { get; init; } = [];

    /// <summary>度量檔沒涵蓋的字元與出現次數（最多的在前）。使用者要知道缺的是表情符號、
    /// 還是某個語言的文字，才知道該匯出哪支字型。</summary>
    public IReadOnlyList<(int Codepoint, int Count)> UncoveredCodepoints { get; init; } = [];

    /// <summary>含執行期格式佔位（<c>{0}</c>）的字串：實際長度不可知，沒有判定。</summary>
    public int Placeholders { get; init; }

    /// <summary>（鍵 × 語言）沒有譯文的組合數。</summary>
    public int MissingTranslations { get; init; }

    /// <summary>資源字串查不到來源語言的值（沒給中性語言的 .resx）—— <c>--pseudo</c> 量不了它們。</summary>
    public int NoSourceText { get; init; }

    public int Suppressed { get; init; }
    public bool Pseudo { get; init; }
    public Dictionary<string, int> Counts { get; init; } = [];
}

public sealed record CheckResult(CheckSummary Summary, IReadOnlyList<FitFinding> Findings);

public sealed class CheckAnalyzer
{
    /// <summary>來源語言自己（寫死的字面文字、中性語言的資源值）的語言標籤。</summary>
    public const string SourceCulture = "(source)";

    public const string PseudoCulture = "(pseudo)";

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
    ///
    /// ⚠ **逐位置判定，不是只看最窄的那一個。** 預算取一個位置是為了匯出給譯者（一個數字就夠）；
    ///   但檢查不同 —— 同一個字串在 A 位置可能水平溢出、在 B 位置可能垂直被裁。
    /// </summary>
    /// <param name="judgeSource">要不要也判定來源語言自己。同一個專案用不同的預設字型掃好幾次時
    /// （見 <see cref="CultureFonts"/>），來源語言只在第一次判。</param>
    /// <param name="measurableInSource">來源語言的掃描裡量得到的位置（檔案:行）。換成這個語言重排之後
    /// 量不到的，要算進「沒有判定」—— 不能默默消失。</param>
    public CheckResult Analyze(
        ScanResult scan,
        BudgetResult budget,
        IReadOnlyList<CultureStrings> cultures,
        bool pseudo,
        bool judgeSource = true,
        IReadOnlySet<string>? measurableInSource = null)
    {
        var findings = new List<FitFinding>();
        var missing = new HashSet<(string Key, string Culture)>();
        var noSource = new HashSet<string>(StringComparer.Ordinal);
        var unreliable = new Dictionary<(string Culture, string Reason), int>();
        var uncovered = new Dictionary<int, int>();
        var measurements = 0;
        var perCulture = new Dictionary<string, (int Measured, int Unreliable)>(StringComparer.OrdinalIgnoreCase);
        var placeholders = 0;
        var suppressed = 0;

        // ⚠ 同一個語言可能來自好幾個檔案（Strings.de.resx + Errors.de.resx，或 XAML 字串字典）。
        //   第一版逐檔比對，於是 Strings 的鍵在 Errors.de 裡「沒有譯文」，被算成漏翻。
        var tables = MergeByCulture(cultures);
        tables.TryGetValue(ResxReader.NeutralCulture, out var neutral);
        var translated = tables
            .Where(t => t.Key != ResxReader.NeutralCulture)
            .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var site in scan.Sites)
        {
            if (!BudgetAnalyzer.IsLocalizable(site)) continue;
            if (!site.Width.IsMeasurable)
            {
                // ⚠ 來源語言量得到、這個語言卻量不到：版面跟著譯文變了（例如旁邊的欄被譯文撐爆）。
                //   第一版直接略過 —— 這個語言的這一筆就默默沒有檢查。
                if (measurableInSource is not null && measurableInSource.Contains(site.Location) && site.SuppressedReason is null)
                    foreach (var (culture, _) in translated)
                    {
                        if (site.Source != TextSource.Resource) break;
                        var why = "the layout could not be resolved with this language's strings";
                        unreliable[(culture, why)] = unreliable.GetValueOrDefault((culture, why)) + 1;
                        var u = perCulture.GetValueOrDefault(culture);
                        perCulture[culture] = (u.Measured, u.Unreliable + 1);
                    }
                continue;
            }
            if (site.SuppressedReason is not null) { suppressed++; continue; }

            var lookup = _fonts.Resolve(site.FontFamily, site.FontWeight, site.FontStyle, _defaultFamily, site.Language);
            if (lookup.Chain.Count == 0) continue;   // 可量測的位置掃描時已經確認過字型；防禦用

            var key = site.ResourceKey ?? site.Text;
            var source = site.SourceText
                         ?? (site.ResourceKey is { } rk && neutral is not null && neutral.TryGetValue(rk, out var nv) ? nv : null);

            var candidates = new List<(string Culture, string Text, bool Pseudo)>();

            if (pseudo)
            {
                // 沒有翻譯時的退路：照分段膨脹表把來源字串拉長。
                // ⚠ 這報的是**風險**不是事實，所以嚴重度一律降級（見 Judge）。
                if (source is null) noSource.Add(key);
                else candidates.Add((PseudoCulture, Pseudoize(source, ExpansionTable.FactorFor(source)), true));
            }
            else
            {
                // 來源語言自己。寫死的英文已經裝不下，也是真的裝不下 ——
                // 而且它是工具自我檢查的基準：出貨的畫面上英文大概都裝得下，這裡報出一堆就是工具算錯了。
                if (source is not null && judgeSource) candidates.Add((SourceCulture, source, false));

                // 寫死的字面文字不會被翻譯，只有資源字串要對每個語言量
                if (site.Source == TextSource.Resource)
                {
                    foreach (var (culture, values) in translated)
                    {
                        if (values.TryGetValue(key, out var text)) candidates.Add((culture, text, false));
                        else missing.Add((key, culture));
                    }
                }
            }

            foreach (var (culture, text, isPseudo) in candidates)
            {
                if (text.Length == 0) continue;

                // 執行期才填值的字串：量 "{0}" 這三個字的寬度沒有意義
                if (TextRules.HasPlaceholder(text)) { placeholders++; continue; }

                var layout = Layout(site, text, lookup);
                if (layout.Unreliable is { } why)
                {
                    if (layout.FirstMissing >= 0)
                        uncovered[layout.FirstMissing] = uncovered.GetValueOrDefault(layout.FirstMissing) + 1;
                    unreliable[(culture, why)] = unreliable.GetValueOrDefault((culture, why)) + 1;
                    var u = perCulture.GetValueOrDefault(culture);
                    perCulture[culture] = (u.Measured, u.Unreliable + 1);
                    continue;
                }

                measurements++;
                var pc = perCulture.GetValueOrDefault(culture);
                perCulture[culture] = (pc.Measured + 1, pc.Unreliable);

                // 來源語言自己排成幾行：譯文比它多行才算「版面被撐高」。
                // 這個語言就是來源語言時（沒有中性字典、基底是 en 的專案）行數當然一樣。
                int? sourceLines = source is null || TextRules.HasPlaceholder(source) ? null
                    : string.Equals(source, text, StringComparison.Ordinal) ? layout.Lines
                    : Layout(site, source, lookup) is { Unreliable: null } sl ? sl.Lines : null;

                var finding = Judge(site, key, culture, text, layout, sourceLines, isPseudo);
                if (finding is not null) findings.Add(finding);
            }
        }

        var counts = findings
            .GroupBy(f => FitFinding.KindSlug(f.Kind))
            .ToDictionary(g => g.Key, g => g.Count());

        return new CheckResult(
            new CheckSummary
            {
                Budget = budget.Summary,
                Cultures = [.. translated.Select(t => t.Key)],
                Measurements = measurements,
                TranslatedMeasurements = perCulture.Where(p => p.Key != SourceCulture).Sum(p => p.Value.Measured),
                PerCulture = perCulture
                    .Select(p => (p.Key, p.Value.Measured, p.Value.Unreliable))
                    .OrderBy(p => p.Key, StringComparer.Ordinal)
                    .ToList(),
                Unreliable = unreliable.Values.Sum(),
                UnreliableReasons = unreliable
                    .Select(u => (u.Key.Culture, u.Key.Reason, u.Value))
                    .OrderByDescending(u => u.Value).ThenBy(u => u.Culture, StringComparer.Ordinal)
                    .ToList(),
                UncoveredCodepoints = uncovered
                    .OrderByDescending(u => u.Value).ThenBy(u => u.Key)
                    .Select(u => (u.Key, u.Value))
                    .ToList(),
                Placeholders = placeholders,
                MissingTranslations = missing.Count,
                NoSourceText = noSource.Count,
                Suppressed = suppressed,
                Pseudo = pseudo,
                Counts = counts,
            },
            findings
                .OrderByDescending(f => f.Severity)
                .ThenByDescending(f => f.OverflowPx)
                .ThenBy(f => f.Site, StringComparer.Ordinal)
                .ThenBy(f => f.Culture, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>把好幾次分析（每次用不同的預設字型）的結果合成一份。摘要裡的掃描統計取第一份的。</summary>
    public static CheckResult Merge(IReadOnlyList<CheckResult> parts)
    {
        if (parts.Count == 1) return parts[0];

        var counts = new Dictionary<string, int>();
        foreach (var p in parts)
            foreach (var (kind, n) in p.Summary.Counts)
                counts[kind] = counts.GetValueOrDefault(kind) + n;

        var first = parts[0].Summary;
        return new CheckResult(
            first with
            {
                Cultures = [.. parts.SelectMany(p => p.Summary.Cultures).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)],
                Measurements = parts.Sum(p => p.Summary.Measurements),
                TranslatedMeasurements = parts.Sum(p => p.Summary.TranslatedMeasurements),
                PerCulture = [.. parts.SelectMany(p => p.Summary.PerCulture).OrderBy(p => p.Culture, StringComparer.Ordinal)],
                Unreliable = parts.Sum(p => p.Summary.Unreliable),
                UnreliableReasons = [.. parts.SelectMany(p => p.Summary.UnreliableReasons).OrderByDescending(u => u.Count)],
                UncoveredCodepoints = [.. parts.SelectMany(p => p.Summary.UncoveredCodepoints)
                    .GroupBy(u => u.Codepoint)
                    .Select(g => (g.Key, g.Sum(u => u.Count)))
                    .OrderByDescending(u => u.Item2).ThenBy(u => u.Key)],
                Placeholders = parts.Sum(p => p.Summary.Placeholders),
                MissingTranslations = parts.Sum(p => p.Summary.MissingTranslations),
                NoSourceText = first.NoSourceText,
                Suppressed = first.Suppressed,
                Counts = counts,
            },
            parts.SelectMany(p => p.Findings)
                .OrderByDescending(f => f.Severity)
                .ThenByDescending(f => f.OverflowPx)
                .ThenBy(f => f.Site, StringComparer.Ordinal)
                .ThenBy(f => f.Culture, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>同一個語言的字串表合併成一份（先出現的優先）。中性語言的鍵是 <see cref="ResxReader.NeutralCulture"/>。</summary>
    public static Dictionary<string, Dictionary<string, string>> MergeByCulture(IReadOnlyList<CultureStrings> cultures)
    {
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cultures)
        {
            if (!tables.TryGetValue(c.Culture, out var table))
                tables[c.Culture] = table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in c.Values) table.TryAdd(k, v);
        }
        return tables;
    }

    /// <summary>把字串拉長到目標倍率。用重複原字串的字來補，寬度才有代表性。</summary>
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

    /// <summary>一段文字在一個位置上排出來的樣子。</summary>
    /// <param name="WidestLine">不換行時最寬的那一行（明寫的換行仍然會換）。</param>
    /// <param name="Lines">照位置的換行設定排出來的行數。</param>
    /// <param name="UnbreakableOverflow">換行模式下，有一個不能斷開的詞比整行還寬（WrapWithOverflow）。</param>
    private sealed record TextLayout(
        double WidestLine, int Lines, double WidestUnbreakable, bool UnbreakableOverflow, bool UsedAutoFallback, string? Unreliable)
    {
        public int FirstMissing { get; init; } = -1;
    }

    private static bool Wraps(TextSite site) =>
        site.Wrap != WrapMode.NoWrap && !site.Width.WrapDefeated && site.Width.Px > 0;

    private TextLayout Layout(TextSite site, string text, FontLookup lookup)
    {
        var limit = site.Width.Px;
        var wraps = Wraps(site);
        var display = TextRules.Display(site.Element, text);

        double widest = 0, widestWord = 0;
        var lines = 0;
        var usedAuto = false;
        var unbreakable = false;

        foreach (var line in TextRules.Lines(display))
        {
            var whole = TextMeasure.Measure(line, lookup, site.FontSize);
            if (!whole.IsReliable)
                return new TextLayout(0, 0, 0, false, false, whole.ComplexScript
                    ? "complex script (advance widths do not sum correctly)"
                    : "glyphs not covered by the font metrics") { FirstMissing = whole.ComplexScript ? -1 : whole.FirstMissing };

            usedAuto |= whole.UsedAutoFallback;
            widest = Math.Max(widest, whole.WidthPx);

            if (!wraps || whole.WidthPx <= limit) { lines++; continue; }

            // 貪婪換行：一個詞一個詞往行裡放，放不下就換行。
            // 中日韓文字每個字之間都可以斷。
            double used = 0;
            var lineCount = 1;
            var space = TextMeasure.Measure(" ", lookup, site.FontSize).WidthPx;

            foreach (var word in BreakUnits(line))
            {
                if (word.Text.Length == 0) continue;
                var w = TextMeasure.Measure(word.Text, lookup, site.FontSize).WidthPx;
                widestWord = Math.Max(widestWord, w);

                var lead = used > 0 && word.SpaceBefore ? space : 0;
                if (used > 0 && used + lead + w > limit)
                {
                    lineCount++;
                    used = 0;
                    lead = 0;
                }

                if (w > limit)
                {
                    if (site.Wrap == WrapMode.WrapWithOverflow)
                    {
                        // 不能斷開的詞比整行寬：WrapWithOverflow 讓它直接超出去
                        unbreakable = true;
                        used = limit;
                    }
                    else
                    {
                        // Wrap：WPF 會在詞的中間硬斷
                        var extra = (int)Math.Ceiling((used + lead + w) / limit) - 1;
                        lineCount += extra;
                        used = (used + lead + w) - extra * limit;
                    }
                    continue;
                }

                used += lead + w;
            }

            lines += lineCount;
        }

        return new TextLayout(widest, Math.Max(1, lines), widestWord, unbreakable, usedAuto, null);
    }

    /// <summary>可以換行的單位：以空白分開的詞；中日韓文字（與全形標點）每個字自成一個單位。</summary>
    private static IEnumerable<(string Text, bool SpaceBefore)> BreakUnits(string line)
    {
        var sb = new System.Text.StringBuilder();
        var spaceBefore = false;
        var pendingSpace = false;

        foreach (var rune in line.EnumerateRunes())
        {
            if (System.Text.Rune.IsWhiteSpace(rune))
            {
                if (sb.Length > 0) { yield return (sb.ToString(), spaceBefore); sb.Clear(); }
                pendingSpace = true;
                continue;
            }

            if (IsIdeographic(rune.Value))
            {
                if (sb.Length > 0) { yield return (sb.ToString(), spaceBefore); sb.Clear(); }
                yield return (rune.ToString(), pendingSpace);
                pendingSpace = false;
                spaceBefore = false;
                continue;
            }

            if (sb.Length == 0) { spaceBefore = pendingSpace; pendingSpace = false; }
            sb.Append(rune.ToString());
        }

        if (sb.Length > 0) yield return (sb.ToString(), spaceBefore);
    }

    private static bool IsIdeographic(int cp) =>
        cp is (>= 0x2E80 and <= 0x9FFF)      // 部首、假名、注音、CJK 統一表意文字
           or (>= 0xAC00 and <= 0xD7AF)      // 韓文音節
           or (>= 0xF900 and <= 0xFAFF)      // 相容表意文字
           or (>= 0xFF00 and <= 0xFFEF)      // 全形符號
           or (>= 0x20000 and <= 0x3FFFF);   // 擴充區

    private FitFinding? Judge(
        TextSite site, string key, string culture, string text, TextLayout layout, int? sourceLines, bool pseudo)
    {
        var limit = site.Width.Px;
        var actual = layout.WidestLine;
        var tightAt = limit * (1 - _marginPercent / 100.0);

        FitKind kind;
        FitSeverity severity;

        if (!Wraps(site))
        {
            if (actual > limit)
            {
                if (site.Trim == TrimMode.None) { kind = FitKind.Overflow; severity = FitSeverity.Fail; }
                else { kind = FitKind.Trimmed; severity = FitSeverity.Warn; }
            }
            else if (actual > tightAt) { kind = FitKind.Tight; severity = FitSeverity.Warn; }
            else return null;
        }
        else if (layout.UnbreakableOverflow)
        {
            // 換行救不了一個斷不開的長詞（德文複合詞最常見）
            kind = FitKind.Overflow;
            severity = FitSeverity.Fail;
            actual = layout.WidestUnbreakable;
        }
        else if (site.Height is { } height && LineHeightOf(site) is var lineHeight and > 0 && layout.Lines * lineHeight > height)
        {
            // 來源語言自己就放不下的高度不是翻譯造成的，但仍然是裁切
            kind = FitKind.VerticalClip;
            severity = FitSeverity.Fail;
        }
        else if (layout.Lines > (sourceLines ?? 1) && culture != SourceCulture)
        {
            // 行數比來源語言多 → 版面被撐高。高度是 Auto 的話通常無害，所以只是資訊
            kind = FitKind.Reflow;
            severity = FitSeverity.Info;
        }
        else return null;

        // ⚠ --pseudo 報的是風險不是事實：一律降一級，絕不讓猜測擋別人的 PR。
        if (pseudo)
            severity = severity switch
            {
                FitSeverity.Fail => FitSeverity.Warn,
                _ => FitSeverity.Info,
            };

        var warnings = new List<string>();
        if (layout.UsedAutoFallback)
            warnings.Add("some characters are drawn with WPF's fallback font for them (taken from WPF's own fallback table)");
        if (site.Width.WrapDefeated && site.Wrap != WrapMode.NoWrap)
            warnings.Add("TextWrapping has no effect here: the text is measured with unlimited width (horizontal StackPanel, Auto column or WrapPanel)");
        if (site.Width.AssumedScrollBar)
            warnings.Add("assumes the vertical scroll bar is visible");
        if (site.Width.DependsOnNeighbourText)
            warnings.Add("the available width is what the other strings in the same row leave in this language");
        if (kind is FitKind.VerticalClip or FitKind.Reflow)
            warnings.Add($"{layout.Lines} line(s)");

        return new FitFinding
        {
            Kind = kind,
            Severity = severity,
            Key = key,
            Culture = culture,
            Text = text,
            Site = site.Location,
            LimitPx = Math.Round(limit, 2),
            ActualPx = Math.Round(actual, 2),
            Warning = warnings.Count > 0 ? string.Join("; ", warnings) : null,
        };
    }

    /// <summary>行高（DIP）。度量檔有 ascender/descender/lineGap 就用它。</summary>
    private double LineHeightOf(TextSite site)
    {
        var lookup = _fonts.Resolve(site.FontFamily, site.FontWeight, site.FontStyle, _defaultFamily, site.Language);
        if (lookup.Chain.Count == 0) return 0;
        var f = lookup.Chain[0];
        return f.UnitsPerEm == 0 ? 0 : (double)f.LineHeight * site.FontSize / f.UnitsPerEm;
    }
}
