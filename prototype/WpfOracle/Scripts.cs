using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using XamlFit.Core.Fonts;

/// <summary>
/// --scripts：需要字形組合的文字系統（希伯來文、阿拉伯文、波斯文、泰文），拿真實譯文比 WPF 的 FormattedText。
/// 用法：wpforacle --scripts &lt;metrics.json&gt; &lt;strings.json&gt;（strings.json：{ "he": [...], "ar": [...], ... }）
/// 印出每個（文字系統, 字型）的平均誤差、最大偏寬、最大偏窄（偏窄 = 低估 = 危險的方向）。
/// </summary>
internal static class Scripts
{
    public static int Run(string metricsPath, string stringsPath)
    {
        var resolver = new FontResolver(MetricsFile.Load(metricsPath).ToMetrics());
        var sets = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(stringsPath))!;
        var fonts = new Dictionary<string, string[]>
        {
            ["he"] = ["Segoe UI", "Arial", "Tahoma"],
            ["ar"] = ["Segoe UI", "Arial", "Tahoma"],
            ["fa"] = ["Segoe UI", "Arial", "Tahoma"],
            ["th"] = ["Leelawadee UI", "Tahoma"],
            ["mr"] = ["Nirmala UI"],
            ["ta"] = ["Nirmala UI"],
            ["km"] = ["Leelawadee UI"],
            ["lo"] = ["Leelawadee UI"],
            ["my"] = ["Myanmar Text"],
            ["bo"] = ["Microsoft Himalaya"],
            ["mn"] = ["Mongolian Baiti"],
        };
        if (Environment.GetEnvironmentVariable("XF_SCRIPT_FONTS") is { Length: > 0 } extra)   // 例："Calibri,Courier New"
            foreach (var key in new[] { "ar", "fa" }) fonts[key] = extra.Split(',');
        var cultures = new Dictionary<string, string> { ["he"] = "he-IL", ["ar"] = "ar-SA", ["fa"] = "fa-IR", ["th"] = "th-TH", ["mr"] = "mr-IN", ["ta"] = "ta-IN",
            ["km"] = "km-KH", ["lo"] = "lo-LA", ["my"] = "my-MM", ["bo"] = "bo-CN", ["mn"] = "mn-Mong-CN" };
        // XF_SCRIPT_SET="dz=dz-BT/Microsoft Himalaya;ps=ps-AF/Segoe UI"：其他語言的文化與字型
        if (Environment.GetEnvironmentVariable("XF_SCRIPT_SET") is { Length: > 0 } set)
            foreach (var item in set.Split(';'))
            {
                var (key, rest) = (item.Split('=')[0], item.Split('=')[1].Split('/'));
                cultures[key] = rest[0];
                fonts[key] = [rest[1]];
            }
        var worstAll = 0.0;

        foreach (var (script, strings) in sets)
        {
            foreach (var family in fonts.GetValueOrDefault(script, ["Nirmala UI"]))
            {
                var culture = cultures.GetValueOrDefault(script, script == "si" ? "si-LK" : script + "-IN");
                var lookup = resolver.Resolve(family, 400, FontStyleKind.Normal, family, culture);
                if (lookup.Chain.Count == 0) { Console.WriteLine($"{script} {family}: no metrics"); continue; }
                var tf = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                double sumAbs = 0, worstWide = 0, worstNarrow = 0;
                string wideText = "", narrowText = "";
                var n = 0;
                var unjudged = 0;
                foreach (var text in strings)
                {
                    var ours = TextMeasure.Measure(text, lookup, 12);
                    if (ours.MissingGlyphs > 0) continue;
                    if (ours.ComplexScript) { unjudged++; if (Environment.GetEnvironmentVariable("XF_SCRIPT_UNJUDGED") is null) continue; }
                    var ft = new FormattedText(text, CultureInfo.GetCultureInfo(culture),
                        script is not ("he" or "ar" or "fa") || Environment.GetEnvironmentVariable("XF_SCRIPT_LTR") is not null ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
                        tf, 12, Brushes.Black, null, TextFormattingMode.Ideal, 1.0);
                    var wpf = ft.WidthIncludingTrailingWhitespace;
                    if (wpf <= 0.01) continue;
                    var rel = (ours.WidthPx - wpf) / wpf * 100;
                    sumAbs += Math.Abs(rel); n++;
                    if (rel > worstWide) { worstWide = rel; wideText = text; }
                    if (rel < worstNarrow) { worstNarrow = rel; narrowText = text; }
                    if (Environment.GetEnvironmentVariable("XF_SCRIPT_DUMP") is { } dump && dump == script + "/" + family && (Math.Abs(rel) > 0.5 || Environment.GetEnvironmentVariable("XF_SCRIPT_ALL") is not null))
                        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {(ours.ComplexScript ? "C" : " ")} {rel,7:F2}%  ours {ours.WidthPx,7:F2} wpf {wpf,7:F2}  «{text}»  {string.Join(' ', text.EnumerateRunes().Select(r => r.Value.ToString("X4")))}"));
                }
                worstAll = Math.Min(worstAll, worstNarrow);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{script} {family,-14} n={n,4} unjudged={unjudged,3}  mean {sumAbs / Math.Max(n, 1),6:F2}%  wide {worstWide,6:F2}% «{Short(wideText)}»  narrow {worstNarrow,6:F2}% «{Short(narrowText)}»"));
            }
        }
        return 0;
    }

    private static string Short(string s) => s.Length <= 30 ? s : s[..29] + "…";
}
