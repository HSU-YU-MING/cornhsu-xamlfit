using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

/// <summary>
/// --indic &lt;strings.json&gt;：印度系文字（馬拉地文、坦米爾文）能不能用「每個音節的寬度加起來」量。
/// 比 FormattedText(整個字) 跟 Σ FormattedText(每個字素叢集)。叢集用 .NET 的 StringInfo（Unicode 15.1 起連字號 + 子音算同一個叢集）。
/// </summary>
internal static class Indic
{
    public static int Run(string path)
    {
        var sets = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))!;
        foreach (var (script, strings) in sets)
        foreach (var family in new[] { "Nirmala UI" })
        {
            var tf = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double W(string t) => new FormattedText(t, CultureInfo.GetCultureInfo(script == "mr" ? "mr-IN" : "ta-IN"), FlowDirection.LeftToRight, tf, 12, Brushes.Black, null, TextFormattingMode.Ideal, 1.0).WidthIncludingTrailingWhitespace;
            int words = 0, exact = 0; double worstNarrow = 0, worstWide = 0; string wn = "", ww = "";
            var clusters = new HashSet<string>();
            foreach (var s in strings)
            foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = new List<string>();
                var e = StringInfo.GetTextElementEnumerator(word);
                while (e.MoveNext()) parts.Add((string)e.Current);
                foreach (var p in parts) clusters.Add(p);
                var whole = W(word);
                if (whole <= 0) continue;
                var sum = parts.Sum(W);
                var rel = (sum - whole) / whole * 100;
                words++;
                if (Math.Abs(rel) < 0.01) exact++;
                if (rel < worstNarrow) { worstNarrow = rel; wn = $"{word} [{string.Join("|", parts)}] sum {sum:F2} whole {whole:F2}"; }
                if (rel > worstWide) { worstWide = rel; ww = $"{word} [{string.Join("|", parts)}] sum {sum:F2} whole {whole:F2}"; }
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{script} {family}: words {words}, sum of clusters exact {exact} ({100.0 * exact / Math.Max(1, words):F1}%), distinct clusters {clusters.Count}\n   narrowest {worstNarrow:F2}% {wn}\n   widest {worstWide:F2}% {ww}"));
        }
        return 0;
    }
}
