using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

/// <summary>
/// --display &lt;out.csv&gt;：TextFormattingMode.Display 的寬度（各字型、字級、顯示縮放），給離線比對用。
/// Display 模式把每個字的寬度對齊到整數像素（用字型的 hinting 結果），跟線性加總不同。
/// </summary>
internal static class Display
{
    public static int Run(string outPath)
    {
        string[] families = ["Segoe UI", "Tahoma", "Arial", "Trebuchet MS", "Verdana", "Calibri", "Consolas", "Microsoft JhengHei UI", "Yu Gothic UI", "Malgun Gothic"];
        double[] sizes = [9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 24];
        double[] scales = [1.0, 1.25, 1.5, 1.75, 2.0];
        string[] texts = ["Save", "Cancel", "Restart in Safe Mode", "The quick brown fox jumps over the lazy dog", "Einstellungen übernehmen", "AVAWAY To Ty", "1,234,567.89", "設定儲存", "設定を保存", "설정 저장", "iiiiiiiiii", "WWWWWWWWWW"];
        using var w = new StreamWriter(outPath);
        w.WriteLine("family\tweight\tsize\tscale\ttext\twidth");
        foreach (var family in families)
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.Bold })
        {
            var tf = new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal);
            foreach (var size in sizes)
            foreach (var scale in scales)
            foreach (var text in texts)
            {
                var ft = new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight, tf, size, Brushes.Black, null, TextFormattingMode.Display, scale);
                w.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{family}\t{weight.ToOpenTypeWeight()}\t{size}\t{scale}\t{text}\t{ft.WidthIncludingTrailingWhitespace:R}"));
            }
        }
        return 0;
    }
}

/// <summary>--display-check &lt;metrics&gt; &lt;wpf.tsv&gt;：工具的 Display 量法對 --display 存下來的 WPF 寬度。</summary>
internal static class DisplayCheck
{
    public static int Run(string metricsPath, string tsv)
    {
        var resolver = new XamlFit.Core.Fonts.FontResolver(XamlFit.Core.Fonts.MetricsFile.Parse(File.ReadAllText(metricsPath)).ToMetrics());
        var stats = new Dictionary<string, (int N, int Exact, int Unknown, double Min, double Max, string Worst)>();
        foreach (var line in File.ReadLines(tsv).Skip(1))
        {
            var c = line.Split('\t');
            var family = c[0];
            var weight = int.Parse(c[1], CultureInfo.InvariantCulture);
            var size = double.Parse(c[2], CultureInfo.InvariantCulture);
            var scale = double.Parse(c[3], CultureInfo.InvariantCulture);
            var text = c[4];
            var wpf = double.Parse(c[5], CultureInfo.InvariantCulture);
            var lookup = resolver.Resolve(family, weight, XamlFit.Core.Fonts.FontStyleKind.Normal, family, "en-US");
            var m = XamlFit.Core.Fonts.TextMeasure.MeasureDisplay(text, lookup, size, [scale]);
            var s = stats.TryGetValue(family, out var v) ? v : (N: 0, Exact: 0, Unknown: 0, Min: 0.0, Max: 0.0, Worst: "");
            s.N++;
            if (!m.IsReliable) { s.Unknown++; stats[family] = s; continue; }
            var diff = (m.WidthPx - wpf) / wpf * 100;
            if (Math.Abs(diff) < 0.01) s.Exact++;
            if (diff < s.Min) { s.Min = diff; s.Worst = string.Create(CultureInfo.InvariantCulture, $"{size}px@{scale} «{text}» ours {m.WidthPx:F2} wpf {wpf:F2}"); }
            s.Max = Math.Max(s.Max, diff);
            stats[family] = s;
        }
        foreach (var (family, s) in stats.OrderBy(k => k.Key))
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{family,-22} n={s.N,5} unknown={s.Unknown,5} exact={s.Exact,5}  narrow {s.Min,6:F2}%  wide {s.Max,6:F2}%  {s.Worst}"));
        return 0;
    }
}
