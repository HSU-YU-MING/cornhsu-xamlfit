using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

/// <summary>
/// --glyphs &lt;字型&gt; &lt;語言&gt; &lt;文字…&gt;：印出 WPF 實際排出來的字形編號與寬度（字型單位），拿來跟匯出引擎（XAMLFIT_INDIC_TRACE）逐個比對。
/// 文字可以寫成碼位（09B9,09CD,09AF）。
/// </summary>
internal static class Glyphs
{
    public static int Run(string family, string culture, IEnumerable<string> texts)
    {
        var tf = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        tf.TryGetGlyphTypeface(out var gt);
        var upem = 2048.0;
        foreach (var raw in texts)
        {
            var text = raw.Contains(',') || raw.All(Uri.IsHexDigit) && raw.Length >= 4
                ? string.Concat(raw.Split(',').Select(h => char.ConvertFromUtf32(Convert.ToInt32(h, 16)))) : raw;
            var props = new Props(tf, CultureInfo.GetCultureInfo(culture));
            var source = new Source(text, props);
            using var line = TextFormatter.Create(TextFormattingMode.Ideal).FormatLine(source, 0, 10000, new ParaProps(props), null);
            var parts = new List<string>();
            foreach (var run in line.GetIndexedGlyphRuns())
            {
                var g = run.GlyphRun;
                var scale = upem / g.FontRenderingEmSize;
                for (var k = 0; k < g.GlyphIndices.Count; k++)
                    parts.Add($"{g.GlyphIndices[k]}:{Math.Round(g.AdvanceWidths[k] * scale)}");
            }
            Console.WriteLine($"{string.Join(" ", text.EnumerateRunes().Select(r => r.Value.ToString("X4")))}  wpf width {Math.Round(line.WidthIncludingTrailingWhitespace * upem / 100)}  [{string.Join(" ", parts)}]");
        }
        return 0;
    }

    private sealed class Props(Typeface tf, CultureInfo culture) : TextRunProperties
    {
        public override Typeface Typeface => tf;
        public override double FontRenderingEmSize => 100;
        public override double FontHintingEmSize => 100;
        public override TextDecorationCollection? TextDecorations => null;
        public override Brush ForegroundBrush => Brushes.Black;
        public override Brush? BackgroundBrush => null;
        public override CultureInfo CultureInfo => culture;
        public override TextEffectCollection? TextEffects => null;
    }

    private sealed class ParaProps(TextRunProperties props) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => props;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties? TextMarkerProperties => null;
        public override double Indent => 0;
    }

    private sealed class Source(string text, TextRunProperties props) : TextSource
    {
        public override TextRun GetTextRun(int i) => i < text.Length ? new TextCharacters(text, i, text.Length - i, props) : new TextEndOfParagraph(1);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int i) => new(0, new CultureSpecificCharacterBufferRange(props.CultureInfo, CharacterBufferRange.Empty));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int i) => i;
    }
}

/// <summary>
/// --mong-attr &lt;metrics&gt; &lt;strings.json&gt;：WPF 排出來的每個字形照 ClusterMap 分給字（跟 MongolianExport 一樣的分法），
/// 跟度量檔的蒙古文表比，依鍵（碼位, 形狀）加總多算的量，印出最多的。
/// </summary>
internal static class MongolianAttribution
{
    public static int Run(string metricsPath, string stringsPath)
    {
        var file = XamlFit.Core.Fonts.MetricsFile.Load(metricsPath).ToMetrics();
        var face = file.First(f => f.Indic?.ContainsKey(XamlFit.Core.Fonts.MongolianJoining.DataKey) == true);
        var table = face.Indic![XamlFit.Core.Fonts.MongolianJoining.DataKey];
        var sets = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(System.IO.File.ReadAllText(stringsPath))!;
        var tf = new Typeface(new FontFamily("Mongolian Baiti"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var excess = new Dictionary<string, (double Sum, int Count, int Ours, int MinWpf, int MaxWpf)>();
        var missing = new List<string>();
        var unknownCount = 0;
        foreach (var text in sets["mn"])
        {
            var props = new GlyphsProps(tf, CultureInfo.GetCultureInfo("mn-Mong-CN"));
            using var line = TextFormatter.Create(TextFormattingMode.Ideal).FormatLine(new GlyphsSource(text, props), 0, 100000, new GlyphsPara(props), null);
            var perChar = new double[text.Length];
            foreach (var run in line.GetIndexedGlyphRuns())
            {
                var g = run.GlyphRun;
                var scale = 2048.0 / g.FontRenderingEmSize;
                // ClusterMap[k] = 第 k 個字（run 內）對應的第一個字形；同一群字共用一段字形，全部算給第一個字
                var map = g.ClusterMap;
                for (var k = 0; k < map.Count; k++)
                {
                    if (k > 0 && map[k] == map[k - 1]) continue;
                    var end = k + 1;
                    while (end < map.Count && map[end] == map[k]) end++;
                    var glyphEnd = end < map.Count ? map[end] : g.GlyphIndices.Count;
                    double w = 0;
                    for (var gi = map[k]; gi < glyphEnd; gi++) w += g.AdvanceWidths[gi] * scale;
                    perChar[run.TextSourceCharacterIndex + k] += w;
                }
            }
            var cps = text.Select(c => (int)c).ToList();
            var forms = XamlFit.Core.Fonts.MongolianJoining.FormsOf(cps);
            var unknown = false;
            foreach (var (start, length) in XamlFit.Core.Fonts.MongolianJoining.Segments(cps, (x, y) => table.ContainsKey(XamlFit.Core.Fonts.MongolianJoining.PairKey(x, y))))
            {
                if (!XamlFit.Core.Fonts.MongolianJoining.Handles(cps[start])) continue;
                var key = XamlFit.Core.Fonts.MongolianJoining.SegmentKey(cps, start, length, forms);
                if (!table.TryGetValue(key, out var ours)) { unknown = true; if (missing.Count < 30) missing.Add(key + "  in " + string.Join(" ", cps.Select(c => c.ToString("X4")))); continue; }
                var wpf = (int)Math.Round(perChar[start] + (length == 2 ? perChar[start + 1] : 0));
                var old = excess.GetValueOrDefault(key, (0, 0, ours, int.MaxValue, int.MinValue));
                excess[key] = (old.Sum + ours - wpf, old.Count + 1, ours, Math.Min(old.MinWpf, wpf), Math.Max(old.MaxWpf, wpf));
            }
            if (unknown) unknownCount++;
        }
        Console.WriteLine($"unknown strings: {unknownCount}");
        foreach (var m in missing) Console.WriteLine("  missing " + m);
        foreach (var (key, v) in excess.OrderByDescending(kv => kv.Value.Sum).Take(40))
        {
            var parts = key.Split(',').Select(int.Parse).ToArray();
            string F(int form) => "p" + (form & 3) + "g" + ((form >> 3) & 3) + (((form >> 2) & 1) == 1 ? "x" : "") + (form >> 5 > 0 ? "f" + (form >> 5) : "");
            var label = parts.Length == 2 ? $"{parts[0]:X4} {F(parts[1])}" : $"{parts[0]:X4}+{parts[1]:X4} {F(parts[2])}/{F(parts[3])}";
            Console.WriteLine($"{label,-28} n={v.Count,5} excess={v.Sum,9:F0}  ours={v.Ours} wpf {v.MinWpf}..{v.MaxWpf}");
        }
        return 0;
    }

    private sealed class GlyphsProps(Typeface tf, CultureInfo culture) : TextRunProperties
    {
        public override Typeface Typeface => tf;
        public override double FontRenderingEmSize => 100;
        public override double FontHintingEmSize => 100;
        public override TextDecorationCollection? TextDecorations => null;
        public override Brush ForegroundBrush => Brushes.Black;
        public override Brush? BackgroundBrush => null;
        public override CultureInfo CultureInfo => culture;
        public override TextEffectCollection? TextEffects => null;
    }

    private sealed class GlyphsPara(TextRunProperties props) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => props;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties? TextMarkerProperties => null;
        public override double Indent => 0;
    }

    private sealed class GlyphsSource(string text, TextRunProperties props) : TextSource
    {
        public override TextRun GetTextRun(int i) => i < text.Length ? new TextCharacters(text, i, text.Length - i, props) : new TextEndOfParagraph(1);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int i) => new(0, new CultureSpecificCharacterBufferRange(props.CultureInfo, CharacterBufferRange.Empty));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int i) => i;
    }
}
