using System.Text.RegularExpressions;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Analysis;

/// <summary>
/// 翻譯完整度（0.5.0）：不量寬度，看字串表本身。三種：
///
/// <list type="bullet">
/// <item><c>missing-translation</c>（warn）：來源語言有這個鍵、這個語言沒有。WPF 顯示來源語言的字。
///   看的是**整份字串表**，不只是 XAML 引用到的鍵 —— 程式碼裡用的訊息也是翻譯對象。</item>
/// <item><c>placeholder-mismatch</c>：來源語言是組合格式字串（有 <c>{0}</c>），譯文跟它對不上。
///   編號超過來源最大的、或大括號不成對 → <b>fail</b>：照來源的引數數量呼叫 String.Format 會丟 FormatException，
///   用到那個字串的畫面直接當掉。少了來源有的編號 → warn：那個值在這個語言裡不見了。</item>
/// <item><c>hard-coded-text</c>（info）：專案有譯文，但這段字寫死在 XAML 裡，每個語言都一樣。</item>
/// </list>
///
/// 預設的 gate（fail）只擋會當掉的那一種；其他的照樣出現在報告與 PR 註記裡，<c>--fail-on warn</c> 才擋。
/// 理由跟 Resolution 不看 Coverage 一樣：一升級就對每個專案開火的閘門會被關掉。
///
/// 只在有譯文時跑（<c>--source-only</c>、<c>--pseudo</c> 不跑）。來源語言 = 中性語言的表；
/// 沒有中性語言時退到 en-US／en（ScreenToGif 的基底是 <c>StringResources.en.xaml</c>），那一份不算譯文。
/// </summary>
public static class TranslationCompleteness
{
    /// <param name="cultures">已經依 <c>--culture</c> 篩過的字串表（含中性語言）。</param>
    /// <param name="sites">來源語言那一次掃描的文字位置（找寫死的字）。</param>
    /// <param name="relativize">字串表的檔案路徑 → 報告裡的位置（掃描根相對，跟 XAML 的位置同一種寫法）。</param>
    public static IReadOnlyList<FitFinding> Analyze(
        IReadOnlyList<CultureStrings> cultures, IEnumerable<TextSite> sites, Func<string, string> relativize)
    {
        var byCulture = cultures
            .GroupBy(c => c.Culture, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var referenceCulture = byCulture.ContainsKey(ResxReader.NeutralCulture) ? ResxReader.NeutralCulture
            : byCulture.Keys.FirstOrDefault(c => c.Equals("en-US", StringComparison.OrdinalIgnoreCase))
              ?? byCulture.Keys.FirstOrDefault(c => c.Equals("en", StringComparison.OrdinalIgnoreCase));

        var translatedCultures = byCulture.Keys
            .Where(c => !c.Equals(referenceCulture, StringComparison.OrdinalIgnoreCase) && c != ResxReader.NeutralCulture)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var findings = new List<FitFinding>();
        if (translatedCultures.Count == 0) return findings;   // 沒有譯文：沒有完整度可言

        if (referenceCulture is not null)
        {
            var (reference, referenceAt) = Merge(byCulture[referenceCulture]);

            foreach (var culture in translatedCultures)
            {
                var (values, at) = Merge(byCulture[culture]);

                foreach (var (key, source) in reference.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (!values.TryGetValue(key, out var translation))
                    {
                        findings.Add(new FitFinding
                        {
                            Kind = FitKind.MissingTranslation,
                            Severity = FitSeverity.Warn,
                            Key = key,
                            Culture = culture,
                            Text = source,
                            Site = Location(referenceAt, key, relativize),
                            LimitPx = 0,
                            ActualPx = 0,
                            Warning = $"no value in the {culture} string table, so WPF shows the source text",
                        });
                        continue;
                    }

                    if (Placeholders(source, translation) is { } mismatch)
                        findings.Add(new FitFinding
                        {
                            Kind = FitKind.PlaceholderMismatch,
                            Severity = mismatch.Severity,
                            Key = key,
                            Culture = culture,
                            Text = translation,
                            Site = Location(at, key, relativize),
                            LimitPx = 0,
                            ActualPx = 0,
                            Warning = mismatch.Why,
                        });
                }
            }
        }

        // 寫死的字：每個位置一筆（不分語言 —— 每個語言都一樣）
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in sites)
        {
            if (site.Source != TextSource.Literal || site.SuppressedReason is not null) continue;
            if (!BudgetAnalyzer.IsLocalizable(site) || !seen.Add(site.Location)) continue;
            findings.Add(new FitFinding
            {
                Kind = FitKind.HardCodedText,
                Severity = FitSeverity.Info,
                Key = site.Text,
                Culture = CheckAnalyzer.SourceCulture,
                Text = site.Text,
                Site = site.Location,
                LimitPx = 0,
                ActualPx = 0,
                Warning = "written in the XAML, so every language shows this text; move it to a resource if it should be translated",
            });
        }

        return findings;
    }

    /// <summary>同一個語言的幾份表合成一份（先出現的優先，跟 CheckAnalyzer.MergeByCulture 一樣），連同每個鍵的位置。</summary>
    private static (Dictionary<string, string> Values, Dictionary<string, (string File, int Line)> At) Merge(List<CultureStrings> tables)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var at = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        foreach (var t in tables)
            foreach (var (k, v) in t.Values)
                if (values.TryAdd(k, v))
                    at[k] = t.Origins is { } o && o.TryGetValue(k, out var where) ? where : (t.File, 0);
        return (values, at);
    }

    private static string Location(Dictionary<string, (string File, int Line)> at, string key, Func<string, string> relativize)
    {
        if (!at.TryGetValue(key, out var where)) return "";
        var file = relativize(where.File);
        return where.Line > 0 ? $"{file}:{where.Line}" : file;
    }

    /// <summary>
    /// 譯文的格式項目跟來源語言對不上的話，回傳嚴重度與說明；對得上（或來源不是組合格式字串）回 null。
    /// </summary>
    internal static (FitSeverity Severity, string Why)? Placeholders(string source, string translation)
    {
        // 來源不是合法的組合格式字串、或一個格式項目都沒有：它不會被拿去 String.Format（或我們不知道怎麼用），不比
        var (sourceItems, sourceMalformed) = FormatItems(source);
        if (sourceMalformed || sourceItems.Count == 0) return null;

        var (items, malformed) = FormatItems(translation);
        if (malformed)
            return (FitSeverity.Fail,
                "the translation has a brace that is not a format item ({ and } must be doubled), so String.Format throws FormatException");

        var max = sourceItems.Max();
        var tooHigh = items.Where(i => i > max).ToList();
        if (tooHigh.Count > 0)
            return (FitSeverity.Fail,
                $"the translation uses {Names(tooHigh)} but the source only has {Names(sourceItems)}, " +
                "so String.Format throws FormatException when the code passes the source's arguments");

        var dropped = sourceItems.Where(i => !items.Contains(i)).ToList();
        var added = items.Where(i => !sourceItems.Contains(i)).ToList();
        if (dropped.Count == 0 && added.Count == 0) return null;

        var parts = new List<string>();
        if (dropped.Count > 0) parts.Add($"drops {Names(dropped)}, so that value is not shown in this language");
        if (added.Count > 0) parts.Add($"uses {Names(added)}, which the source does not show");
        return (FitSeverity.Warn, "the translation " + string.Join(" and ", parts));
    }

    private static string Names(IEnumerable<int> indices) =>
        string.Join(", ", indices.Distinct().Order().Select(i => "{" + i + "}"));

    // .NET 的組合格式項目：{編號[,對齊][:格式]}。編號後面可以有空白，前面不行（StringBuilder.AppendFormat 照這個解析）
    private static readonly Regex Item = new(@"^(\d{1,6})\s*(,\s*-?\d+\s*)?(:[^{}]*)?$", RegexOptions.CultureInvariant);

    /// <summary>字串裡的格式項目編號，以及有沒有不成對的大括號（<c>{{</c>、<c>}}</c> 是跳脫）。</summary>
    internal static (SortedSet<int> Indices, bool Malformed) FormatItems(string s)
    {
        var indices = new SortedSet<int>();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{') { i++; continue; }
                var close = s.IndexOf('}', i + 1);
                if (close < 0) return (indices, true);
                var m = Item.Match(s[(i + 1)..close]);
                if (!m.Success) return (indices, true);
                indices.Add(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                i = close;
            }
            else if (c == '}')
            {
                if (i + 1 < s.Length && s[i + 1] == '}') { i++; continue; }
                return (indices, true);
            }
        }
        return (indices, false);
    }
}
