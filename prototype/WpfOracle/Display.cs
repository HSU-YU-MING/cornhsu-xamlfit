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

/// <summary>--display-gdi &lt;wpf.tsv&gt;：假設「Display 模式的字寬 = GDI 在那個像素大小的 hinting 寬度」，拿 GDI 算出來跟 WPF 比。</summary>
internal static class DisplayGdi
{
    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool GetCharWidth32W(IntPtr dc, uint first, uint last, int[] widths);
    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool GetTextExtentPoint32W(IntPtr dc, string s, int n, out SIZE size);
    private struct SIZE { public int cx, cy; }

    public static int Run(string tsv)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        var stats = new Dictionary<string, (int N, int Exact, int Narrow, double Min, double Max, string Worst)>();
        var cache = new Dictionary<(string, int, int), IntPtr>();
        foreach (var line in File.ReadLines(tsv).Skip(1))
        {
            var c = line.Split('\t');
            var family = c[0];
            var weight = int.Parse(c[1], CultureInfo.InvariantCulture);
            var size = double.Parse(c[2], CultureInfo.InvariantCulture);
            var scale = double.Parse(c[3], CultureInfo.InvariantCulture);
            var text = c[4];
            var wpf = double.Parse(c[5], CultureInfo.InvariantCulture);
            if (text.Any(ch => ch > 0x2FFF)) continue;   // 中日韓字會走後備字型，這裡只比主字型
            var ppem = (int)Math.Floor(size * scale + 0.5);
            if (!cache.TryGetValue((family, weight, ppem), out var font))
                cache[(family, weight, ppem)] = font = CreateFontW(-ppem, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 3 /*NONANTIALIASED*/, 0, family);
            SelectObject(dc, font);
            // 逐字加寬度（不含字距），再加四捨五入的字距由 WPF 那邊比 —— 這裡先只看不含字距的字串
            var widths = new int[1];
            var total = 0;
            foreach (var ch in text) { GetCharWidth32W(dc, ch, ch, widths); total += widths[0]; }
            var ours = total / scale;
            var diff = (ours - wpf) / wpf * 100;
            var s = stats.TryGetValue(family, out var v) ? v : (N: 0, Exact: 0, Narrow: 0, Min: 0.0, Max: 0.0, Worst: "");
            s.N++;
            if (Math.Abs(diff) < 0.01) s.Exact++;
            if (diff < -0.01) { s.Narrow++; if (Environment.GetEnvironmentVariable("XF_GDI_NARROW") is not null) Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"NARROW {family} {weight} {size}px@{scale} ppem {size * scale} «{text}» gdi {ours:F2} wpf {wpf:F2}")); }
            if (diff < s.Min) { s.Min = diff; s.Worst = string.Create(CultureInfo.InvariantCulture, $"{weight} {size}px@{scale} «{text}» gdi {ours:F2} wpf {wpf:F2}"); }
            s.Max = Math.Max(s.Max, diff);
            stats[family] = s;
        }
        foreach (var (family, s) in stats.OrderBy(k => k.Key))
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{family,-22} n={s.N,5} exact={s.Exact,5} narrow={s.Narrow,4}  min {s.Min,6:F2}%  max {s.Max,6:F2}%  {s.Worst}"));
        DeleteDC(dc);
        return 0;
    }
}
