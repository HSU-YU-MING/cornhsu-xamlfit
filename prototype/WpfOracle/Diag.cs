using System.Globalization;
using System.Windows;
using System.Windows.Media;
using XamlFit.Core.Fonts;

internal static class Diag
{
    public static void Run(FontResolver resolver)
    {
        var tf = new Typeface(new FontFamily("Yu Gothic UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var chain = resolver.Resolve("Yu Gothic UI", 400, FontStyleKind.Normal, "Yu Gothic UI").Chain;
        Console.WriteLine("\n=== 逐字診斷：Yu Gothic UI @14 ===");
        Console.WriteLine($"{"text",-12} {"ours",8} {"wpf",8} {"diff",8}");
        foreach (var s in new[] { "\u201c", "\u201d", "q", "u", "o", "t", "e", "d", "qu", "\u201cq", "d\u201d", "quoted", "\u201cquoted\u201d" })
        {
            var ours = TextMeasure.Measure(s, chain, 14).WidthPx;
            var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 14,
                Brushes.Black, null, TextFormattingMode.Ideal, 1.0);
            Console.WriteLine($"{Escape(s),-12} {ours,8:F3} {ft.WidthIncludingTrailingWhitespace,8:F3} {ours - ft.WidthIncludingTrailingWhitespace,8:F3}");
        }
    }

    private static string Escape(string s) => string.Concat(s.Select(c => c < 128 ? c.ToString() : $"U+{(int)c:X4}"));
}
