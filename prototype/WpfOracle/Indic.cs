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

/// <summary>--indic-gen &lt;out.json&gt;：從 IndicClusters 的定義產生測試字串（每個叢集單獨、隨機的叢集序列、多子音連寫的字）。</summary>
internal static class IndicGen
{
    public static readonly Dictionary<string, string> Languages = new()
    {
        ["deva"] = "mr", ["beng"] = "bn", ["guru"] = "pa", ["gujr"] = "gu", ["orya"] = "or",
        ["taml"] = "ta", ["telu"] = "te", ["knda"] = "kn", ["mlym"] = "ml", ["sinh"] = "si",
    };

    public static int Run(string outPath)
    {
        var rng = new Random(31);
        var result = new Dictionary<string, List<string>>();
        foreach (var script in XamlFit.Core.Fonts.IndicClusters.All)
        {
            string S(IEnumerable<int> cps) => string.Concat(cps.Select(char.ConvertFromUtf32));
            var clusters = XamlFit.Core.Fonts.IndicClusters.Enumerate(script, _ => true).ToList();
            var list = clusters.Select(c => S(c)).ToList();
            for (var n = 0; n < 600; n++)
                list.Add(string.Concat(Enumerable.Range(0, rng.Next(2, 6)).Select(_ => S(clusters[rng.Next(clusters.Count)]))));
            var cons = script.Consonants.Order().ToList();
            var signs = script.Signs.Order().ToList();
            for (var n = 0; n < 800; n++)
            {
                var word = "";
                for (var syl = rng.Next(1, 4); syl > 0; syl--)
                {
                    for (var k = rng.Next(0, 3); k > 0; k--) word += char.ConvertFromUtf32(cons[rng.Next(cons.Count)]) + char.ConvertFromUtf32(script.Virama);
                    word += char.ConvertFromUtf32(cons[rng.Next(cons.Count)]);
                    if (rng.Next(2) == 0) word += char.ConvertFromUtf32(signs[rng.Next(signs.Count)]);
                }
                list.Add(word);
            }
            result[Languages[script.Tag]] = list;
        }
        File.WriteAllText(outPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine(string.Join(" ", result.Select(kv => $"{kv.Key}:{kv.Value.Count}")));
        return 0;
    }
}
