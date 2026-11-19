// LayoutOracle —— 同一段 XAML 同時交給**真的 WPF 視窗**與 XamlFit 的解析器，逐案比對。
//
// WPF 那邊：把視窗顯示在螢幕外，量出名為 probe 的元素實際看得見多寬（被祖先裁掉之後）。
// XamlFit 那邊：用正式的 WidthResolver 解同一個元素。
//
// 判定：
//   exact      兩邊差距 ≤ 0.5
//   safe       XamlFit 比較小（保守）。差距會印出來 —— 太大代表誤報會很多
//   OVER       XamlFit 比 WPF 大 —— **這是最嚴重的失敗**（把裝不下說成裝得下），整支程式回 1
//   open       WPF 沒有裁切，XamlFit 也說沒有上界
//   blocked    XamlFit 說量不到（列出原因）
//
// 為什麼要「真的 Show」：沒 Show 過的 Window 不會做版面，也沒有非工作區（邊框）。
// 全面重讀時發現的洞（視窗邊框沒扣、明寫的 Width 也會被外層裁掉、水平 StackPanel 的兄弟分寬度、
// Auto 欄裡的換行失效）都是之前「只量根元素」的對照程式看不到的。
//
// 拋棄式工具，Windows only。用法：
//   dotnet run -c Release                 比對全部案例
//   dotnet run -c Release -- --text       文字量測的幾個事實（快速鍵底線、模擬粗體、Display 模式）
//   dotnet run -c Release -- --defaults   佈景預設的 Padding／BorderThickness

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using XamlFit.Core.Analysis;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.OpenType;
using XamlFit.Core.Xaml;

internal static class Program
{
    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
        "xmlns:shell=\"clr-namespace:System.Windows.Shell;assembly=PresentationFramework\"";

    private static string DefaultFamily = "Segoe UI";

    private const string Long = "The quick brown fox jumps over the lazy dog and keeps running far beyond the edge of anything";

    /// <summary>期待 XamlFit 怎麼回答。</summary>
    private enum Want
    {
        /// <summary>算得出來，而且不大於 WPF。</summary>
        Bound,
        /// <summary>WPF 不裁切 → XamlFit 應該說沒有上界。</summary>
        Open,
        /// <summary>XamlFit 刻意不解（說量不到是對的）。</summary>
        Blocked,
    }

    private sealed record Case(string Name, string Xaml, Want Want = Want.Bound, double Slack = 0.5);

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        if (args.Length > 0 && args[0] == "--text") { TextChecks(); app.Shutdown(); return 0; }
        if (args.Length > 0 && args[0] == "--defaults") { Defaults(); app.Shutdown(); return 0; }
        if (args.Length > 0 && args[0] == "--newlines")
        {
            // 字串裡的換行字元，TextBlock 真的會換行嗎？（ScreenToGif 的字串用 &#x0d; 換行）
            var cr = ((char)13).ToString();
            var lf = ((char)10).ToString();
            foreach (var (name, sep) in new[] { ("CR", cr), ("LF", lf), ("CRLF", cr + lf), ("U+2028", ((char)0x2028).ToString()), ("none", "") })
            {
                var tb = new TextBlock { Text = "AAAAAAAAAA" + sep + "BBBBBBBBBB", FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Console.WriteLine($"{name,-7} width {tb.DesiredSize.Width,7:F2}  height {tb.DesiredSize.Height,6:F2}");
            }
            // 從資源字典（xml:space=preserve、&#x0d;）載入的字串
            var dict = (ResourceDictionary)XamlReader.Parse(
                "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:s='clr-namespace:System;assembly=mscorlib' xml:space='preserve'>" +
                "<s:String x:Key='k'>AAAAAAAAAA&#x0d;BBBBBBBBBB</s:String></ResourceDictionary>");
            foreach (var (label, body, preserve) in new[]
                     {
                         ("&#x0d; preserve", "AA&#x0d;BB", true), ("&#x0a; preserve", "AA&#x0a;BB", true), ("&#10; preserve", "AA&#10;BB", true),
                         ("&#x0d;&#x0a; preserve", "AA&#x0d;&#x0a;BB", true), ("literal newline preserve", "AA" + (char)10 + "BB", true),
                         ("&#x0d; default", "AA&#x0d;BB", false), ("&#x0a; default", "AA&#x0a;BB", false),
                     })
            {
                var x = "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:s='clr-namespace:System;assembly=mscorlib'" +
                        (preserve ? " xml:space='preserve'" : "") + "><s:String x:Key='k'>" + body + "</s:String>" +
                        "<TextBlock x:Key='t' Text='" + body + "'/></ResourceDictionary>";
                var viaParse = (string)((ResourceDictionary)XamlReader.Parse(x))["k"];
                var viaLoad = (string)((ResourceDictionary)XamlReader.Load(System.Xml.XmlReader.Create(new StringReader(x))))["k"];
                var attr = ((TextBlock)((ResourceDictionary)XamlReader.Parse(x))["t"]).Text;
                string Hex(string v) => string.Join(" ", v.Select(c => ((int)c).ToString("X2")));
                Console.WriteLine($"{label,-26} element: Parse [{Hex(viaParse)}]  Load [{Hex(viaLoad)}]   attribute: [{Hex(attr)}]");
            }
            var text = (string)dict["k"];
            Console.WriteLine("from dictionary: " + string.Join(" ", text.Select(c => ((int)c).ToString("X2"))));
            var t2 = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
            t2.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Console.WriteLine($"dictionary string: width {t2.DesiredSize.Width:F2} height {t2.DesiredSize.Height:F2}");
            app.Shutdown();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--composite")
        {
            // WPF 自帶的複合字型：主字型畫不出來的字照這張表補
            var asm = typeof(FontFamily).Assembly;
            foreach (var n in asm.GetManifestResourceNames()) Console.WriteLine("resource: " + n);
            using (var g = asm.GetManifestResourceStream("PresentationCore.g.resources")!)
            using (var reader = new System.Resources.ResourceReader(g))
            {
                foreach (System.Collections.DictionaryEntry e in reader)
                {
                    var key = (string)e.Key;
                    Console.WriteLine("  g: " + key);
                    if (key.EndsWith(".compositefont", StringComparison.OrdinalIgnoreCase) && e.Value is Stream st)
                    {
                        var outPath = Path.Combine(args.Length > 1 ? args[1] : ".", Path.GetFileName(key));
                        using var fsOut = File.Create(outPath);
                        st.CopyTo(fsOut);
                    }
                }
            }
            var dir = Path.GetDirectoryName(asm.Location)!;
            foreach (var file in Directory.EnumerateFiles(dir, "*.CompositeFont", SearchOption.AllDirectories)) Console.WriteLine("file: " + file);
            app.Shutdown();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--scrollbar")
        {
            // 專案把 ScrollBar 的範本換掉（根元素寫死寬度）時，捲軸實際多寬？Width／MinWidth 從哪裡來？
            foreach (var where in new[] { "grid", "app" })
            {
                const string bar = "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ScrollBar\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"ScrollBar\"><Border Width=\"24\" Margin=\"2,0\" Background=\"Gray\"/></ControlTemplate></Setter.Value></Setter></Style>";
                var style = (Style)System.Windows.Markup.XamlReader.Parse(bar);
                if (where == "app") Application.Current.Resources[typeof(ScrollBar)] = style;
                var sv = new ScrollViewer { Width = 300, Height = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, Content = new TextBlock { Text = "x" } };
                var host = new Grid();
                if (where == "grid") host.Resources[typeof(ScrollBar)] = style;
                host.Children.Add(sv);
                var win = new Window { Content = host, Width = 400, Height = 200, Left = -4000, ShowInTaskbar = false };
                win.Show(); win.UpdateLayout();
                var sb = (ScrollBar)sv.Template.FindName("PART_VerticalScrollBar", sv);
                Console.WriteLine($"{where}: bar ActualWidth {sb.ActualWidth:F2} templateRoot {(VisualTreeHelper.GetChildrenCount(sb) > 0 ? VisualTreeHelper.GetChild(sb, 0).GetType().Name : "-")} Width {sb.Width} ({DependencyPropertyHelper.GetValueSource(sb, FrameworkElement.WidthProperty).BaseValueSource}) MinWidth {sb.MinWidth:F2} ({DependencyPropertyHelper.GetValueSource(sb, FrameworkElement.MinWidthProperty).BaseValueSource}) TemplateSource {DependencyPropertyHelper.GetValueSource(sb, Control.TemplateProperty).BaseValueSource}");
                win.Close();
                if (where == "app") Application.Current.Resources.Remove(typeof(ScrollBar));
            }
            app.Shutdown();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--header-font")
        {
            // DataGrid 欄標題的字型從哪裡來：標題樣式、DataGrid、還是欄自己的 FontSize？
            const string xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Width=\"600\" Height=\"300\" Left=\"-4000\" ShowInTaskbar=\"False\">" +
                "<StackPanel>" +
                "<DataGrid x:Name=\"a\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FontSize=\"14\" AutoGenerateColumns=\"False\">" +
                "<DataGrid.Resources><Style TargetType=\"DataGridColumnHeader\"><Setter Property=\"FontWeight\" Value=\"SemiBold\"/><Setter Property=\"FontSize\" Value=\"16\"/></Style></DataGrid.Resources>" +
                "<DataGrid.Columns><DataGridTextColumn Header=\"Styled\" FontSize=\"20\" FontWeight=\"Light\"/></DataGrid.Columns></DataGrid>" +
                "<DataGrid x:Name=\"b\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FontSize=\"14\" AutoGenerateColumns=\"False\">" +
                "<DataGrid.Columns><DataGridTextColumn Header=\"Inherited\" FontSize=\"20\" FontWeight=\"Light\"/></DataGrid.Columns></DataGrid>" +
                "</StackPanel></Window>";
            var win = (Window)System.Windows.Markup.XamlReader.Parse(xaml);
            win.Show();
            win.UpdateLayout();
            foreach (var name in new[] { "a", "b" })
            {
                var grid = (FrameworkElement)win.FindName(name);
                var header = Descendants(grid).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().First(h => h.Column is not null);
                var tb = Descendants(header).OfType<TextBlock>().First();
                Console.WriteLine($"{name}: header text '{tb.Text}' FontSize {tb.FontSize} FontWeight {tb.FontWeight}");
            }
            win.Close();
            app.Shutdown();
            return 0;

            static IEnumerable<DependencyObject> Descendants(DependencyObject root)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    yield return child;
                    foreach (var d in Descendants(child)) yield return d;
                }
            }
        }

        if (args.Length > 0 && args[0] == "--inlines")
        {
            // 行內內容的寬度：Run 之間的空白、LineBreak 兩側的空白、不同字重的 Run 加起來
            const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
            double Tb(string inner)
            {
                var tb = (TextBlock)System.Windows.Markup.XamlReader.Parse($"<TextBlock {ns} FontFamily=\"Segoe UI\" FontSize=\"12\">{inner}</TextBlock>");
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return tb.DesiredSize.Width;
            }
            foreach (var (label, inner) in new[]
            {
                ("text 'Save All'", "Save All"),
                ("text 'SaveAll'", "SaveAll"),
                ("text 'Save'", "Save"),
                ("runs, newline between", "<Run Text=\"Save\"/>\n    <Run Text=\"All\"/>"),
                ("runs, adjacent", "<Run Text=\"Save\"/><Run Text=\"All\"/>"),
                ("runs around LineBreak", "<Run Text=\"Save\"/>\n    <LineBreak/>\n    <Run Text=\"All\"/>"),
                ("run alone after LineBreak", "<Run Text=\"Save\"/>\n <LineBreak/>\n <Run Text=\"Everything\"/>"),
                ("text 'Everything'", "Everything"),
                ("bold run + run", "<Run Text=\"Save\" FontWeight=\"Bold\"/>\n    <Run Text=\"All\"/>"),
                ("text bold 'Save'", "<Bold>Save</Bold>"),
            })
                Console.WriteLine($"{label,-28} {Tb(inner),8:F2}");
            app.Shutdown();
            return 0;
        }

        if (args.Length > 3 && args[0] == "--probe")
        {
            // --probe <family> <size> <text>：整句與逐字的 TextBlock 寬度，以及主字型有沒有那個字
            // family 可以寫成 "Family|700|Italic"
            var spec = args[1].Split('|');
            var fam = spec[0]; var size = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture); var text = args[3];
            var weight = spec.Length > 1 ? FontWeight.FromOpenTypeWeight(int.Parse(spec[1])) : FontWeights.Normal;
            var fstyle = spec.Length > 2 && spec[2] == "Italic" ? FontStyles.Italic : FontStyles.Normal;
            double W(string t) { var tb = new TextBlock { Text = t, FontFamily = new FontFamily(fam), FontSize = size, FontWeight = weight, FontStyle = fstyle }; tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return tb.DesiredSize.Width; }
            new Typeface(new FontFamily(fam), fstyle, weight, FontStretches.Normal).TryGetGlyphTypeface(out var gt);
            Console.WriteLine($"whole {W(text):F2}");
            foreach (var tf in new FontFamily(fam).GetTypefaces())
                if (tf.TryGetGlyphTypeface(out var g))
                    Console.WriteLine($"face {tf.Weight}/{tf.Style}/{tf.Stretch} {g.FontUri.LocalPath} has U+0430: {g.CharacterToGlyphMap.ContainsKey('а')}");
            foreach (var m in new FontFamily(fam).FamilyMaps) Console.WriteLine($"familymap {m.Unicode} -> {m.Target}");
            var sum = 0.0;
            foreach (var ch in text.Distinct().OrderBy(c => c))
            {
                var has = gt is not null && gt.CharacterToGlyphMap.ContainsKey(ch);
                var n = text.Count(c => c == ch); var w = W(ch.ToString());
                if (ch == ' ') w = W("a a") - W("aa");
                sum += w * n;
                Console.WriteLine($"U+{(int)ch:X4} {(ch == ' ' ? "sp" : ch.ToString())} x{n} {w:F3} {(has ? "" : "MISSING")}");
            }
            Console.WriteLine($"sum of chars {sum:F2}");
            // 逐字寬度跟哪一支已安裝的字型完全一致（找出 WPF 實際拿來補字的字型）
            var missing = text.Distinct().Where(c => gt is not null && !gt.CharacterToGlyphMap.ContainsKey(c)).Take(8).ToArray();
            if (missing.Length > 0)
                foreach (var ff in Fonts.SystemFontFamilies.OrderBy(x => x.Source))
                {
                    var ok = missing.All(c =>
                    {
                        var tb = new TextBlock { Text = c.ToString(), FontFamily = ff, FontSize = size, FontWeight = weight, FontStyle = fstyle };
                        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        return Math.Abs(tb.DesiredSize.Width - W(c.ToString())) < 0.002;
                    });
                    if (ok) Console.WriteLine($"same widths as: {ff.Source}");
                }
            app.Shutdown();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--fallback")
        {
            // 主字型畫不出來的字，WPF 拿哪一支補？跟元素的 Language（xml:lang）有沒有關係？
            // 看不見的字（變體選擇符、零寬空白、BOM）：WPF 給它們寬度嗎？
            foreach (var (label, text) in new[]
                     {
                         ("sun", "☀"), ("sun+VS16", "☀️"), ("A+ZWSP+A", "A​A"), ("AA", "AA"),
                         ("A+ZWJ+A", "A‍A"), ("A+BOM+A", "A﻿A"), ("A+WJ+A", "A⁠A"), ("moon", "U0001F319"),
                     })
            {
                var tb = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 14 };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Console.WriteLine($"ignorable {label,-10} width {tb.DesiredSize.Width:F2}");
            }
            foreach (var fam in new[] { "Segoe UI", "Segoe UI Emoji", "Segoe UI Symbol" })
            foreach (var (label, text) in new[] { ("moon", char.ConvertFromUtf32(0x1F319)), ("sun", "☀"), ("grin", char.ConvertFromUtf32(0x1F600)), ("check", "✔") })
            {
                var tb = new TextBlock { Text = text, FontFamily = new FontFamily(fam), FontSize = 14 };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var ft = TextWidth(text, fam, FontWeights.Normal, TextFormattingMode.Ideal, 14);
                Console.WriteLine($"emoji {fam,-16} {label,-6} TextBlock {tb.DesiredSize.Width,7:F2}  FormattedText {ft,7:F2}  utf16 len {text.Length}");
            }
            string[] samples = ["クラッシュレポート", "診断情報を送信", "再起動", "설정 저장", "Перезапуск", "設定儲存", "设置保存", "ภาษาไทย"];
            string[] langs = ["en-us", "ja-jp", "zh-tw", "zh-cn", "ko-kr"];
            string[] candidates = ["Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic", "Microsoft JhengHei UI", "Microsoft YaHei UI", "Malgun Gothic", "Segoe UI", "Microsoft JhengHei", "Microsoft YaHei", "SimSun", "Leelawadee UI"];
            foreach (var primary in new[] { "Trebuchet MS", "Segoe UI" })
            foreach (var sample in samples)
            {
                Console.Write($"{primary,-14} {sample,-12}");
                foreach (var lang in langs)
                {
                    var tb = new TextBlock { Text = sample, FontFamily = new FontFamily(primary), FontSize = 14, Language = XmlLanguage.GetLanguage(lang) };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var w = tb.DesiredSize.Width;
                    var match = candidates.FirstOrDefault(c => Math.Abs(TextWidth(sample, c, FontWeights.Normal, TextFormattingMode.Ideal, 14) - w) < 0.01);
                    Console.Write($"  {lang}:{w,7:F2}={match ?? "?"}");
                }
                Console.WriteLine();
            }
            app.Shutdown();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--popups") { Popups(); app.Shutdown(); return 0; }

        if (args.Length > 1 && args[0] == "--watermark") { Watermark(app, args[1]); app.Shutdown(); return 0; }

        if (args.Length > 0 && args[0] == "--font-source")
        {
            // 沒寫 FontFamily 的文字到底用哪一支：相依屬性的預設值、實際排出來的字型、系統訊息字型
            var tb = new TextBlock { Text = "Name:" };
            var host = Host();
            host.Content = tb;
            host.Show();
            host.UpdateLayout();
            Console.WriteLine($"TextElement.FontFamily default metadata : {TextElement.FontFamilyProperty.DefaultMetadata.DefaultValue}");
            Console.WriteLine($"TextElement.FontSize   default metadata : {TextElement.FontSizeProperty.DefaultMetadata.DefaultValue}");
            Console.WriteLine($"Control.FontFamily     default metadata : {Control.FontFamilyProperty.DefaultMetadata.DefaultValue}");
            Console.WriteLine($"SystemFonts.MessageFontFamily           : {SystemFonts.MessageFontFamily.Source} {SystemFonts.MessageFontSize}");
            Console.WriteLine($"a TextBlock in a window actually uses  : {tb.FontFamily.Source} {tb.FontSize}  (ActualWidth of 'Name:' = {tb.ActualWidth:F2})");
            Console.WriteLine($"CurrentUICulture                        : {System.Globalization.CultureInfo.CurrentUICulture.Name}");
            host.Close();
            app.Shutdown();
            return 0;
        }

        var cases = BuildCases();
        if (args.Length > 1 && args[0] == "--theme")
        {
            ThemeInUse = Themes.Load(app, args[1]);
            cases = [.. Themes.Cases(args[1], Ns, Long, ThemeInUse).Select(c => new Case($"{args[1]} {c.Name}",
                $"<Window {Ns} Width='{c.Width}' Height='300' WindowStyle='None' AllowsTransparency='True'>{c.Inner}</Window>",
                c.Unknown ? Want.Blocked : Want.Bound))];
            Console.WriteLine($"theme: {ThemeInUse.Id} {ThemeInUse.Version}");
        }
        var fonts = LoadFonts();
        var chrome = ChromeTable.Builtin();

        // ⚠ WPF 的預設字型是「系統訊息字型」，跟著 Windows 的顯示語言走：
        //   英文系統是 Segoe UI，繁中是 Microsoft JhengHei UI，日文是 Yu Gothic UI…
        //   對照時 XamlFit 要用同一支，否則兄弟元素的文字寬度會對不上。
        DefaultFamily = SystemFonts.MessageFontFamily.Source;
        Console.WriteLine($"system message font: {DefaultFamily} {SystemFonts.MessageFontSize}");
        Console.WriteLine();

        if (args.Length > 0 && args[0] == "--wrap")
        {
            var mismatches = WrapChecks(fonts, chrome);
            app.Shutdown();
            return mismatches > 0 ? 1 : 0;
        }

        Console.WriteLine($"{"case",-50} {"WPF",8} {"XamlFit",9}  verdict");
        Console.WriteLine(new string('-', 100));

        int exact = 0, safe = 0, over = 0, wrong = 0;
        var only = args.Length > 1 && args[0] == "--only" ? args[1] : args.Length > 3 && args[2] == "--only" ? args[3] : null;

        foreach (var c in cases)
        {
            if (only is not null && !c.Name.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;

            double wpfVisible, wpfActual;
            try
            {
                var window = (Window)XamlReader.Parse(c.Xaml);
                window.Left = -20000;
                window.Top = -20000;
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.Show();
                window.UpdateLayout();
                // 「minwidth:」案例：模擬使用者把視窗縮到 MinWidth（設定檔的 assumeWindowMinWidth 回答的就是這個情況）
                if (c.Name.StartsWith("minwidth:", StringComparison.Ordinal)) { window.Width = window.MinWidth; window.UpdateLayout(); }
                // DataGrid 的星號欄寬是延後算的（排進 Dispatcher）：先讓它跑完再量
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                window.UpdateLayout();

                var probe = (FrameworkElement?)window.FindName("probe")
                            ?? throw new InvalidOperationException("no element named probe");
                wpfActual = probe.ActualWidth;
                wpfVisible = VisibleWidth(probe, window);
                window.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{c.Name,-50} WPF FAILED {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                wrong++;
                continue;
            }

            var ours = ResolveWithXamlFit(c.Xaml, chrome, fonts, assumeMinWidth: c.Name.StartsWith("minwidth:", StringComparison.Ordinal));
            var clipped = wpfVisible < wpfActual - 0.01 || c.Want == Want.Bound;
            string verdict;

            if (ours.Status == WidthStatus.Known)
            {
                var diff = ours.Px - wpfVisible;
                if (c.Want == Want.Open) { verdict = "WRONG: expected no bound"; wrong++; }
                else if (c.Want == Want.Blocked) { verdict = "WRONG: expected blocked"; wrong++; }
                else if (diff > c.Slack) { verdict = $"OVER by {diff:F2}  <<<<<<"; over++; }
                else if (diff >= -0.5) { verdict = "exact"; exact++; }
                else { verdict = $"safe (under by {-diff:F2})"; safe++; }
            }
            else if (ours.Status == WidthStatus.NoDeclaredLimit)
            {
                if (c.Want == Want.Open) { verdict = "open"; exact++; }
                else { verdict = $"WRONG: says no bound ({ours.Reason})"; wrong++; }
            }
            else
            {
                if (c.Want == Want.Blocked) { verdict = $"blocked: {Short(ours.Reason)}"; exact++; }
                else { verdict = $"WRONG: blocked ({ours.Status}: {ours.Reason})"; wrong++; }
            }

            var oursText = ours.Status == WidthStatus.Known
                ? ours.Px.ToString("F2", CultureInfo.InvariantCulture)
                : ours.Status == WidthStatus.NoDeclaredLimit ? "(open)" : "(n/a)";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{c.Name,-50} {wpfVisible,8:F2} {oursText,9}  {verdict}{(clipped ? "" : "")}"));
        }

        Console.WriteLine();
        Console.WriteLine($"{exact} match, {safe} conservative, {over} OVER-ESTIMATES, {wrong} wrong");

        app.Shutdown();
        return over > 0 || wrong > 0 ? 1 : 0;
    }

    /// <summary>--check：用檢查模式的排法（同一排只扣前面的兄弟），對應 WPF 實際看得見的寬度。</summary>
    private static bool CheckMode = Environment.GetCommandLineArgs().Contains("--check");

    /// <summary>--theme：解析器也拿到同一份佈景量測。</summary>
    private static ThemeProfile? ThemeInUse;

    private static string Short(string s) => s.Length <= 70 ? s : s[..69] + "…";

    private static AvailableWidth ResolveWithXamlFit(string xaml, ChromeTable chrome, FontResolver fonts, bool assumeMinWidth = false)
    {
        var doc = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);   // 跟 XamlScanner 一樣保留空白（Run 之間的空格）
        doc.AddAnnotation(new SourceFile("oracle.xaml"));
        if (ThemeInUse is { } theme) doc.AddAnnotation(new ExternalTheme(theme.Id, theme));

        var styles = new StyleIndex();
        styles.Add(doc, "oracle.xaml");
        if (ThemeInUse is { } t) styles.ThemeOfFile = _ => t;

        var measurer = new ElementMeasurer(fonts, styles, chrome, DefaultFamily, 12);
        var resolver = new WidthResolver(chrome, styles, measurer) { Sharing = CheckMode ? RowSharing.Preceding : RowSharing.WholeRow, AssumeWindowMinWidth = assumeMinWidth };

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var probe = doc.Descendants().First(e => (string?)e.Attribute(x + "Name") == "probe");
        return resolver.Resolve(probe);
    }

    private static FontResolver LoadFonts()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var metrics = new List<FontMetrics>();
        foreach (var file in new[]
                 {
                     "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "seguisb.ttf", "segoeuil.ttf",
                     "msjh.ttc", "msjhbd.ttc", "msjhl.ttc", "msyh.ttc", "msyhbd.ttc", "YuGothR.ttc", "YuGothB.ttc", "malgun.ttf", "malgunbd.ttf",
                 })
        {
            var path = Path.Combine(dir, file);
            if (File.Exists(path)) metrics.AddRange(FontMetricsExtractor.FromFile(path));
        }
        return new FontResolver(metrics);
    }

    private static List<Case> BuildCases()
    {
        var cases = new List<Case>();
        void Add(string name, string xaml, Want want = Want.Bound, double slack = 0.5) => cases.Add(new Case(name, xaml, want, slack));

        // ── 1. 視窗邊框：Width 是外框，工作區比較窄 ──
        // 表裡用的是 100% 縮放的值（最大），這台機器是 125%，所以 XamlFit 會比較保守
        foreach (var resize in new[] { "CanResize", "CanResizeWithGrip", "CanMinimize", "NoResize" })
        foreach (var style in new[] { "SingleBorderWindow", "ThreeDBorderWindow", "ToolWindow", "None" })
            Add($"window {resize}/{style}",
                $"<Window {Ns} Width='400' Height='200' ResizeMode='{resize}' WindowStyle='{style}'><Grid x:Name='probe'/></Window>");

        Add("window None+AllowsTransparency",
            $"<Window {Ns} Width='400' Height='200' WindowStyle='None' AllowsTransparency='True'><Grid x:Name='probe'/></Window>");
        Add("window WindowChrome",
            $"<Window {Ns} Width='400' Height='200'><shell:WindowChrome.WindowChrome><shell:WindowChrome/></shell:WindowChrome.WindowChrome><Grid x:Name='probe'/></Window>");
        Add("window MaxWidth + SizeToContent",
            $"<Window {Ns} MaxWidth='400' Height='200' SizeToContent='Width'><TextBlock x:Name='probe' Text='{Long} {Long}'/></Window>");

        // 回報 2 §3-2：可以縮小的視窗，設定檔開 assumeWindowMinWidth 時用 MinWidth 當上界。
        // 對照程式把視窗縮到 MinWidth 再量（名字以 minwidth: 開頭的案例）；解析器也開同一個旗標
        const string threeCols = "<Grid.ColumnDefinitions><ColumnDefinition Width='100'/><ColumnDefinition/><ColumnDefinition Width='100'/></Grid.ColumnDefinitions>";
        Add("minwidth: resizable window shrunk to MinWidth",
            $"<Window {Ns} Width='1280' MinWidth='800' Height='200'><Grid>{threeCols}<TextBlock x:Name='probe' Grid.Column='1' Text='{Long} {Long} {Long}'/></Grid></Window>");
        Add("minwidth: frameless window shrunk to MinWidth",
            $"<Window {Ns} Width='1280' MinWidth='800' Height='200' WindowStyle='None' AllowsTransparency='True'><Border Padding='10,0'><TextBlock x:Name='probe' TextWrapping='Wrap' Text='{Long} {Long} {Long}'/></Border></Window>");
        Add("minwidth: window with MaxWidth below Width",
            $"<Window {Ns} Width='1280' MinWidth='800' MaxWidth='900' Height='200'><Grid>{threeCols}<TextBlock x:Name='probe' Grid.Column='1' Text='{Long} {Long} {Long}'/></Grid></Window>");

        // 之後的案例都放在無邊框視窗裡，工作區 = Width
        static string W(string inner, int width = 700, string resources = "") =>
            $"<Window {Ns} Width='{width}' Height='300' WindowStyle='None' AllowsTransparency='True'>{resources}{inner}</Window>";

        var text = $"<TextBlock x:Name='probe' Text='{Long}'/>";

        // ── 2. 控制項內容區（預設範本） ──
        Add("Button", W($"<Button Width='200' HorizontalAlignment='Left'>{text}</Button>"));
        Add("ToggleButton", W($"<ToggleButton Width='200' HorizontalAlignment='Left'>{text}</ToggleButton>"));
        Add("Label", W($"<Label Width='200' HorizontalAlignment='Left'>{text}</Label>"));
        Add("CheckBox", W($"<CheckBox Width='200' HorizontalAlignment='Left'>{text}</CheckBox>"));
        Add("RadioButton", W($"<RadioButton Width='200' HorizontalAlignment='Left'>{text}</RadioButton>"));
        Add("GroupBox content", W("<GroupBox Width='300' HorizontalAlignment='Left' Header='H'><Border x:Name='probe'/></GroupBox>"));
        Add("GroupBox header", W($"<GroupBox Width='300' HorizontalAlignment='Left'><GroupBox.Header>{text}</GroupBox.Header><Border/></GroupBox>"));
        Add("Expander content", W("<Expander Width='300' HorizontalAlignment='Left' IsExpanded='True' Header='H'><Border x:Name='probe'/></Expander>"));
        Add("Expander header", W($"<Expander Width='300' HorizontalAlignment='Left'><Expander.Header>{text}</Expander.Header></Expander>"));
        // 0.8.0：專案自己的 Expander 範本，標題放在 ToggleButton 裡（ToggleButton 又有自己的範本：箭頭欄 + 標題）
        const string toggleStyle = "<Style x:Key='HeaderToggle' TargetType='ToggleButton'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'><Border Padding='{TemplateBinding Padding}' Background='Transparent'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='20'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Path Data='M 0 0 L 4 4 L 8 0' Stroke='Black' VerticalAlignment='Center'/><ContentPresenter Grid.Column='1' Margin='4,0,0,0'/></Grid></Border></ControlTemplate></Setter.Value></Setter></Style>";
        string expanderTemplate(string toggleAttrs) => $"<ControlTemplate TargetType='Expander'><Border BorderThickness='{{TemplateBinding BorderThickness}}' BorderBrush='Gray' Padding='3'><DockPanel><ToggleButton DockPanel.Dock='Top' Style='{{StaticResource HeaderToggle}}' Content='{{TemplateBinding Header}}' IsChecked='{{Binding IsExpanded, RelativeSource={{RelativeSource TemplatedParent}}, Mode=TwoWay}}' {toggleAttrs}/><ContentPresenter x:Name='ExpandSite' Visibility='Collapsed'/></DockPanel></Border></ControlTemplate>";
        // ExpandDirection 左右：預設範本把標題轉 90 度放在旁邊的一欄，內容拿剩下的寬度
        foreach (var dir in new[] { "Right", "Left" })
        foreach (var size in new[] { 12, 20 })
            Add($"Expander {dir} content, FontSize {size}", W($"<Expander Width='300' HorizontalAlignment='Left' IsExpanded='True' ExpandDirection='{dir}' FontSize='{size}' Header='Header text'><Border x:Name='probe'/></Expander>"));
        Add("Expander Right content, header H", W("<Expander Width='300' HorizontalAlignment='Left' IsExpanded='True' ExpandDirection='Right' Header='H'><Border x:Name='probe'/></Expander>"));
        Add("Expander Right content, BorderThickness 4 Padding 5", W("<Expander Width='300' HorizontalAlignment='Left' IsExpanded='True' ExpandDirection='Right' BorderThickness='4' Padding='5' Header='Header text'><Border x:Name='probe'/></Expander>"));
        Add("Expander Right content, header element", W("<Expander Width='300' HorizontalAlignment='Left' IsExpanded='True' ExpandDirection='Right'><Expander.Header><TextBlock Text='Header text' Margin='3,0'/></Expander.Header><Border x:Name='probe'/></Expander>"));
        Add("Expander template, header in ToggleButton",W($"<Grid Width='400' HorizontalAlignment='Left'><Grid.Resources>{toggleStyle}</Grid.Resources><Expander Width='300' HorizontalAlignment='Left' BorderThickness='1'><Expander.Template>{expanderTemplate("Padding='6,2'")}</Expander.Template><Expander.Header>{text}</Expander.Header></Expander></Grid>"));
        Add("Expander template, ToggleButton Padding from Expander", W($"<Grid Width='400' HorizontalAlignment='Left'><Grid.Resources>{toggleStyle}</Grid.Resources><Expander Width='300' Padding='10,0' HorizontalAlignment='Left' BorderThickness='1'><Expander.Template>{expanderTemplate("Padding='{TemplateBinding Padding}'")}</Expander.Template><Expander.Header>{text}</Expander.Header></Expander></Grid>"));
        Add("Expander implicit style template, header in ToggleButton", W($"<Grid Width='400' HorizontalAlignment='Left'><Grid.Resources>{toggleStyle}<Style TargetType='Expander'><Setter Property='Template'><Setter.Value>{expanderTemplate("Padding='6,2'")}</Setter.Value></Setter></Style></Grid.Resources><Expander Width='300' HorizontalAlignment='Left' BorderThickness='1'><Expander.Header>{text}</Expander.Header></Expander></Grid>"));
        Add("TabControl content (TabItem padded)", W("<TabControl Width='400' HorizontalAlignment='Left'><TabItem Header='A' Padding='30,2' Margin='7,0' Width='90'><Border x:Name='probe'/></TabItem></TabControl>"));
        Add("UserControl Padding 5", W("<UserControl Width='300' HorizontalAlignment='Left' Padding='5'><Border x:Name='probe'/></UserControl>"));
        Add("ContentControl", W($"<ContentControl Width='200' HorizontalAlignment='Left'>{text}</ContentControl>"));
        Add("StatusBar single item", W($"<StatusBar Width='300' HorizontalAlignment='Left'><StatusBarItem>{text}</StatusBarItem></StatusBar>"));
        Add("ScrollViewer default (bar visible)", W($"<ScrollViewer Width='300' HorizontalAlignment='Left'>{text}</ScrollViewer>"), slack: 0.5);
        Add("ScrollViewer V=Auto (bar assumed)", W($"<ScrollViewer Width='300' HorizontalAlignment='Left' VerticalScrollBarVisibility='Auto'>{text}</ScrollViewer>"));
        Add("ScrollViewer V=Disabled", W($"<ScrollViewer Width='300' HorizontalAlignment='Left' VerticalScrollBarVisibility='Disabled'>{text}</ScrollViewer>"));
        Add("ScrollViewer H=Auto", W($"<ScrollViewer Width='300' HorizontalAlignment='Left' HorizontalScrollBarVisibility='Auto'>{text}</ScrollViewer>"), Want.Open);

        // ── 3. 明寫的 Padding／BorderThickness：一比一取代預設值嗎 ──
        foreach (var (tag, extra) in new[]
                 {
                     ("Button", ""), ("ToggleButton", ""), ("Label", ""), ("CheckBox", ""), ("RadioButton", ""),
                     ("GroupBox", "Header='H'"), ("Expander", "IsExpanded='True' Header='H'"), ("UserControl", ""), ("ContentControl", ""),
                 })
        {
            Add($"{tag} Padding=20,0", W($"<{tag} Width='300' HorizontalAlignment='Left' Padding='20,0' {extra}>{text}</{tag}>"));
            Add($"{tag} Padding=0", W($"<{tag} Width='300' HorizontalAlignment='Left' Padding='0' {extra}>{text}</{tag}>"));
            Add($"{tag} BorderThickness=5", W($"<{tag} Width='300' HorizontalAlignment='Left' BorderThickness='5' {extra}>{text}</{tag}>"));
            Add($"{tag} BorderThickness=0", W($"<{tag} Width='300' HorizontalAlignment='Left' BorderThickness='0' {extra}>{text}</{tag}>"));
        }
        Add("TabControl Padding=20", W("<TabControl Width='400' HorizontalAlignment='Left' Padding='20'><TabItem Header='A'><Border x:Name='probe'/></TabItem></TabControl>"));
        Add("TabControl BorderThickness=5", W("<TabControl Width='400' HorizontalAlignment='Left' BorderThickness='5'><TabItem Header='A'><Border x:Name='probe'/></TabItem></TabControl>"));
        Add("ListBoxItem Padding=20,0 (H disabled)", W($"<ListBox Width='300' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListBoxItem Padding='20,0'>{text}</ListBoxItem></ListBox>"));
        Add("StatusBarItem Padding=20,0", W($"<StatusBar Width='300' HorizontalAlignment='Left'><StatusBarItem Padding='20,0'>{text}</StatusBarItem></StatusBar>"));

        // ── 4. 專案自己的範本 ──
        const string buttonTemplate =
            "<Window.Resources>" +
            "<Style x:Key='Flat' TargetType='Button'><Setter Property='Padding' Value='14,6'/><Setter Property='Template'><Setter.Value>" +
            "<ControlTemplate TargetType='Button'><Border BorderThickness='{TemplateBinding BorderThickness}' BorderBrush='Gray' CornerRadius='4' Padding='{TemplateBinding Padding}'>" +
            "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>" +
            "</Setter.Value></Setter></Style>" +
            "<Style x:Key='Margined' TargetType='Button'><Setter Property='Template'><Setter.Value>" +
            "<ControlTemplate TargetType='Button'><Border BorderThickness='2' BorderBrush='Gray'>" +
            "<ContentPresenter Margin='{TemplateBinding Padding}'/></Border></ControlTemplate>" +
            "</Setter.Value></Setter></Style>" +
            "<Style x:Key='Iconed' TargetType='Button'><Setter Property='Template'><Setter.Value>" +
            "<ControlTemplate TargetType='Button'><Border Padding='8,0'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>" +
            "<Border Width='16' Height='16' Margin='0,0,6,0' Background='Gray'/><ContentPresenter Grid.Column='1'/></Grid></Border></ControlTemplate>" +
            "</Setter.Value></Setter></Style>" +
            "<Style x:Key='Check' TargetType='CheckBox'><Setter Property='Template'><Setter.Value>" +
            "<ControlTemplate TargetType='CheckBox'><StackPanel Orientation='Horizontal'><Border Width='18' Height='18' BorderThickness='1' BorderBrush='Gray'/>" +
            "<ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/></StackPanel></ControlTemplate>" +
            "</Setter.Value></Setter></Style>" +
            "<Style x:Key='Group' TargetType='GroupBox'><Setter Property='Template'><Setter.Value>" +
            "<ControlTemplate TargetType='GroupBox'><Border BorderThickness='1' BorderBrush='Gray' Padding='10'><DockPanel>" +
            "<ContentPresenter DockPanel.Dock='Top' ContentSource='Header' Margin='4,0,4,6'/><ContentPresenter/></DockPanel></Border></ControlTemplate>" +
            "</Setter.Value></Setter></Style>" +
            "<Style TargetType='ToggleButton'><Setter Property='Template'><Setter.Value>" +
            "<ControlTemplate TargetType='ToggleButton'><Border Padding='12,0'><ContentPresenter/></Border></ControlTemplate>" +
            "</Setter.Value></Setter></Style>" +
            "</Window.Resources>";

        Add("template: Border Padding=TemplateBinding", W($"<Button Style='{{StaticResource Flat}}' Width='200' HorizontalAlignment='Left'>{text}</Button>", resources: buttonTemplate));
        Add("template: + local Padding override", W($"<Button Style='{{StaticResource Flat}}' Padding='30,0' Width='200' HorizontalAlignment='Left'>{text}</Button>", resources: buttonTemplate));
        Add("template: + local BorderThickness", W($"<Button Style='{{StaticResource Flat}}' BorderThickness='4' Width='200' HorizontalAlignment='Left'>{text}</Button>", resources: buttonTemplate));
        Add("template: presenter Margin=TemplateBinding (default Padding)", W($"<Button Style='{{StaticResource Margined}}' Width='200' HorizontalAlignment='Left'>{text}</Button>", resources: buttonTemplate));
        Add("template: icon in Auto column", W($"<Button Style='{{StaticResource Iconed}}' Width='200' HorizontalAlignment='Left'>{text}</Button>", resources: buttonTemplate));
        Add("template: CheckBox with box in HStack", W($"<CheckBox Style='{{StaticResource Check}}' Width='200' HorizontalAlignment='Left'>{text}</CheckBox>", resources: buttonTemplate));
        Add("template: GroupBox content", W("<GroupBox Style='{StaticResource Group}' Width='300' HorizontalAlignment='Left' Header='H'><Border x:Name='probe'/></GroupBox>", resources: buttonTemplate));
        Add("template: GroupBox header", W($"<GroupBox Style='{{StaticResource Group}}' Width='300' HorizontalAlignment='Left'><GroupBox.Header>{text}</GroupBox.Header><Border/></GroupBox>", resources: buttonTemplate));
        Add("template: implicit style", W($"<ToggleButton Width='200' HorizontalAlignment='Left'>{text}</ToggleButton>", resources: buttonTemplate));
        Add("template: local Button.Template", W($"<Button Width='200' HorizontalAlignment='Left'><Button.Template><ControlTemplate TargetType='Button'><Border Padding='25,0'><ContentPresenter/></Border></ControlTemplate></Button.Template>{text}</Button>"));

        const string scrollTemplate =
            "<Window.Resources><Style TargetType='ScrollViewer'><Setter Property='Template'><Setter.Value>" +
            "<ControlTemplate TargetType='ScrollViewer'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>" +
            "<Grid.RowDefinitions><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>" +
            "<ScrollContentPresenter/><ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Width='10' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>" +
            "<Rectangle Grid.Column='1' Grid.Row='1'/></Grid></ControlTemplate>" +
            "</Setter.Value></Setter></Style></Window.Resources>";
        // ⚠ 佈景的預設樣式把垂直捲軸的 MinWidth 設成系統寬度：只寫 Width='10' 沒有用，實際還是 16.8
        Add("template: ScrollViewer, bar Width=10 only", W($"<ScrollViewer Width='300' HorizontalAlignment='Left'>{text}</ScrollViewer>", resources: scrollTemplate));
        Add("template: ScrollViewer, bar Width=10 MinWidth=10", W($"<ScrollViewer Width='300' HorizontalAlignment='Left'>{text}</ScrollViewer>", resources: scrollTemplate.Replace("Width='10'", "Width='10' MinWidth='10'")));
        Add("template: ScrollViewer, bar disabled", W($"<ScrollViewer Width='300' HorizontalAlignment='Left' VerticalScrollBarVisibility='Disabled'>{text}</ScrollViewer>", resources: scrollTemplate));
        Add("template: system scroll bar column", W($"<Button Width='200' HorizontalAlignment='Left'><Button.Template><ControlTemplate TargetType='Button'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='0' MinWidth='{{DynamicResource {{x:Static SystemParameters.VerticalScrollBarWidthKey}}}}'/></Grid.ColumnDefinitions><ContentPresenter/></Grid></ControlTemplate></Button.Template>{text}</Button>"));
        Add("BulletDecorator", W($"<Border Width='200' HorizontalAlignment='Left'><BulletDecorator><BulletDecorator.Bullet><Border Width='18' Height='18'/></BulletDecorator.Bullet>{text}</BulletDecorator></Border>"));

        // ── 5. 兄弟分寬度的面板 ──
        Add("HStack: probe then Button 100", W($"<Border Width='300' HorizontalAlignment='Left'><StackPanel Orientation='Horizontal'>{text}<Button Width='100'/></StackPanel></Border>"));
        Add("HStack: Button 100 then probe", W($"<Border Width='300' HorizontalAlignment='Left'><StackPanel Orientation='Horizontal'><Button Width='100'/>{text}</StackPanel></Border>"));
        Add("HStack: label text then probe", W($"<Border Width='300' HorizontalAlignment='Left'><StackPanel Orientation='Horizontal'><TextBlock Text='Name:' Margin='0,0,8,0'/>{text}</StackPanel></Border>"));
        Add("HStack: empty Rectangle sibling", W($"<Border Width='300' HorizontalAlignment='Left'><StackPanel Orientation='Horizontal'><Rectangle/>{text}</StackPanel></Border>"));
        Add("HStack: sibling with unknown text", W($"<Border Width='300' HorizontalAlignment='Left'><StackPanel Orientation='Horizontal'><TextBlock Text='{{Binding Missing}}'/>{text}</StackPanel></Border>"), Want.Blocked);
        Add("VStack", W($"<Border Width='300' HorizontalAlignment='Left'><StackPanel>{text}</StackPanel></Border>"));
        Add("DockPanel: left 100, probe fills", W($"<DockPanel Width='300' HorizontalAlignment='Left'><Button Width='100'/>{text}</DockPanel>"));
        Add("DockPanel: probe left, button fills", W($"<DockPanel Width='300' HorizontalAlignment='Left'>{text}<Button/></DockPanel>"));
        Add("DockPanel: probe left, right 100, fill", W($"<DockPanel Width='300' HorizontalAlignment='Left'>{text}<Button DockPanel.Dock='Right' Width='100'/><Border/></DockPanel>"));
        Add("DockPanel: right 100, probe left, fill", W($"<DockPanel Width='300' HorizontalAlignment='Left'><Button DockPanel.Dock='Right' Width='100'/>{text}<Border/></DockPanel>"));
        Add("DockPanel: left 100, probe top, fill", W($"<DockPanel Width='300' HorizontalAlignment='Left'><Button Width='100'/><TextBlock x:Name='probe' DockPanel.Dock='Top' Text='{Long}'/><Border/></DockPanel>"));
        Add("DockPanel: probe top, left 100 after, fill", W($"<DockPanel Width='300' HorizontalAlignment='Left'><TextBlock x:Name='probe' DockPanel.Dock='Top' Text='{Long}'/><Button Width='100'/><Border/></DockPanel>"));
        Add("DockPanel LastChildFill=False", W($"<DockPanel Width='300' HorizontalAlignment='Left' LastChildFill='False'><Button Width='100'/>{text}</DockPanel>"));
        Add("UniformGrid Columns=3", W("<UniformGrid Width='600' HorizontalAlignment='Left' Columns='3'><Border x:Name='probe'/><Border/><Border/></UniformGrid>"));
        Add("UniformGrid 5 children", W("<UniformGrid Width='600' HorizontalAlignment='Left'><Border x:Name='probe'/><Border/><Border/><Border/><Border/></UniformGrid>"));
        Add("UniformGrid Rows=2, 5 children", W("<UniformGrid Width='600' HorizontalAlignment='Left' Rows='2'><Border x:Name='probe'/><Border/><Border/><Border/><Border/></UniformGrid>"));
        Add("UniformGrid text", W($"<UniformGrid Width='600' HorizontalAlignment='Left' Columns='3'>{text}<Border/><Border/></UniformGrid>"));
        Add("WrapPanel", W($"<WrapPanel Width='300' HorizontalAlignment='Left'>{text}</WrapPanel>"));
        Add("WrapPanel ItemWidth=120", W($"<WrapPanel Width='300' HorizontalAlignment='Left' ItemWidth='120'>{text}</WrapPanel>"));
        Add("Canvas", W($"<Border Width='300' HorizontalAlignment='Left'><Canvas>{text}</Canvas></Border>"), Want.Open);
        Add("Viewbox", W($"<Border Width='300' HorizontalAlignment='Left'><Viewbox>{text}</Viewbox></Border>"), Want.Open);

        // ── 6. Grid ──
        Add("Grid fixed column", W($"<Grid Width='400' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='120'/><ColumnDefinition/></Grid.ColumnDefinitions>{text}</Grid>"));
        Add("Grid Auto col + fixed 100", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='100'/></Grid.ColumnDefinitions>{text}</Grid>"));
        Add("Grid Auto col + star Min 80", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*' MinWidth='80'/></Grid.ColumnDefinitions>{text}</Grid>"));
        Add("Grid star after Auto label", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Text='Name:' Margin='0,0,8,0'/><TextBlock Grid.Column='1' x:Name='probe' Text='{Long}'/></Grid>"));
        Add("Grid star after Auto Button MinWidth", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Button Content='OK' MinWidth='75' Margin='0,0,8,0'/><TextBlock Grid.Column='1' x:Name='probe' Text='{Long}'/></Grid>"));
        Add("Grid star after Auto Button _access key", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Button Content='_Save changes' Padding='10,2'/><TextBlock Grid.Column='1' x:Name='probe' Text='{Long}'/></Grid>"));
        Add("MaxWidth 300 in fixed col 100", W($"<Grid Width='400' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='100'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock x:Name='probe' MaxWidth='300' Text='{Long}'/></Grid>"));
        Add("Width 300 in fixed col 100", W($"<Grid Width='400' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='100'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock x:Name='probe' Width='300' Text='{Long}'/></Grid>"));
        Add("MaxWidth 150 in wide Border", W($"<Border Width='400' HorizontalAlignment='Left'><TextBlock x:Name='probe' MaxWidth='150' HorizontalAlignment='Left' Text='{Long}'/></Border>"));
        Add("Width 200 Border in 120 Border", W($"<Border Width='120' HorizontalAlignment='Left'><Border Width='200' HorizontalAlignment='Left'>{text}</Border></Border>"));
        Add("star Min 400 vs star in 500", W("<Grid Width='500' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition MinWidth='400'/><ColumnDefinition/></Grid.ColumnDefinitions><Border Grid.Column='1' x:Name='probe'/></Grid>"));
        Add("star Max 200 vs star in 500", W("<Grid Width='500' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition MaxWidth='200'/><ColumnDefinition/></Grid.ColumnDefinitions><Border Grid.Column='1' x:Name='probe'/></Grid>"));
        Add("stars 1*,2*(Max150),1* in 600", W("<Grid Width='600' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='2*' MaxWidth='150'/><ColumnDefinition/></Grid.ColumnDefinitions><Border Grid.Column='2' x:Name='probe'/></Grid>"));
        Add("stars 1*(Min300),1*,1*(Max50) in 600", W("<Grid Width='600' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition MinWidth='300'/><ColumnDefinition/><ColumnDefinition MaxWidth='50'/></Grid.ColumnDefinitions><Border Grid.Column='1' x:Name='probe'/></Grid>"));
        Add("children inside Grid.Children", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Grid.Children><Border Width='90'/><TextBlock Grid.Column='1' x:Name='probe' Text='{Long}'/></Grid.Children></Grid>"));

        // ── 7. 清單與欄標題 ──
        Add("ListBox default (scrolls)", W($"<ListBox Width='200' HorizontalAlignment='Left'><ListBoxItem>{text}</ListBoxItem></ListBox>"), Want.Open);
        Add("ListBox H=Disabled (bar assumed)", W($"<ListBox Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled'><ListBoxItem>{text}</ListBoxItem></ListBox>"));
        Add("ListBox H=Disabled V=Disabled", W($"<ListBox Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListBoxItem>{text}</ListBoxItem></ListBox>"));
        Add("ListBox H=Disabled V=Visible", W($"<ListBox Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Visible'><ListBoxItem>{text}</ListBoxItem></ListBox>"));
        Add("ListView H=Disabled V=Disabled", W($"<ListView Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListViewItem>{text}</ListViewItem></ListView>"));
        Add("TreeView default (scrolls)", W($"<TreeView Width='200' HorizontalAlignment='Left'><TreeViewItem IsExpanded='True' Header='A'><TreeViewItem><TreeViewItem.Header>{text}</TreeViewItem.Header></TreeViewItem></TreeViewItem></TreeView>"), Want.Open);
        Add("ItemsControl", W($"<ItemsControl Width='200' HorizontalAlignment='Left'>{text}</ItemsControl>"));
        Add("ItemsControl custom ItemsPanel", W($"<ItemsControl Width='200' HorizontalAlignment='Left'><ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation='Horizontal'/></ItemsPanelTemplate></ItemsControl.ItemsPanel>{text}</ItemsControl>"), Want.Blocked);
        // 0.7.0：清單控制項的自訂範本。內容宿主是 ItemsPresenter；範本的 ScrollViewer 跟著清單的捲軸設定
        const string listTemplate = "<ControlTemplate TargetType='ListBox'><Border BorderThickness='2' Padding='5'><ScrollViewer Focusable='False'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>";
        Add("ListBox template, H/V Disabled", W($"<ListBox Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListBox.Template>{listTemplate}</ListBox.Template><ListBoxItem>{text}</ListBoxItem></ListBox>"));
        Add("ListBox template, H Disabled (bar assumed)", W($"<ListBox Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled'><ListBox.Template>{listTemplate}</ListBox.Template><ListBoxItem>{text}</ListBoxItem></ListBox>"));
        Add("ListBox template, scrolls", W($"<ListBox Width='200' HorizontalAlignment='Left'><ListBox.Template>{listTemplate}</ListBox.Template><ListBoxItem>{text}</ListBoxItem></ListBox>"), Want.Open);
        Add("ListBox template, TemplateBinding Padding 8", W($"<ListBox Width='200' Padding='8' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListBox.Template><ControlTemplate TargetType='ListBox'><Border Padding='{{TemplateBinding Padding}}'><ScrollViewer Focusable='False'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate></ListBox.Template><ListBoxItem>{text}</ListBoxItem></ListBox>"));
        Add("ListBox template, ScrollViewer sets its own H=Auto", W($"<ListBox Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListBox.Template><ControlTemplate TargetType='ListBox'><Border Padding='5'><ScrollViewer HorizontalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate></ListBox.Template><ListBoxItem>{text}</ListBoxItem></ListBox>"), Want.Blocked);
        Add("ListBox template, IsItemsHost panel", W($"<ListBox Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListBox.Template><ControlTemplate TargetType='ListBox'><Border Padding='5'><StackPanel IsItemsHost='True'/></Border></ControlTemplate></ListBox.Template><ListBoxItem>{text}</ListBoxItem></ListBox>"), Want.Blocked);
        Add("ListBox implicit style template, H/V Disabled", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.Resources><Style TargetType='ListBox'><Setter Property='Template'><Setter.Value>{listTemplate}</Setter.Value></Setter></Style></Grid.Resources><ListBox Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListBoxItem>{text}</ListBoxItem></ListBox></Grid>"));
        Add("ItemsControl template Padding 10,0,20,0", W($"<ItemsControl Width='200' HorizontalAlignment='Left'><ItemsControl.Template><ControlTemplate TargetType='ItemsControl'><Border Padding='10,0,20,0'><ItemsPresenter/></Border></ControlTemplate></ItemsControl.Template>{text}</ItemsControl>"));
        Add("ItemsControl template with ScrollViewer", W($"<ItemsControl Width='200' HorizontalAlignment='Left'><ItemsControl.Template><ControlTemplate TargetType='ItemsControl'><ScrollViewer HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></ControlTemplate></ItemsControl.Template>{text}</ItemsControl>"));
        Add("ListView template, H/V Disabled", W($"<ListView Width='200' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'><ListView.Template><ControlTemplate TargetType='ListView'><Border BorderThickness='1' Padding='3'><ScrollViewer Focusable='False'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate></ListView.Template><ListViewItem>{text}</ListViewItem></ListView>"));
        // Playnite 的寫法：捲軸範本的根元素寫死寬度、ScrollViewer 範本把捲軸放在 Auto 欄
        Add("ScrollViewer custom bar, Track Width 17", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.Resources><Style TargetType='ScrollBar'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' Width='17'><Track.Thumb><Thumb Width='8'/></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter></Style><Style TargetType='ScrollViewer'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{{TemplateBinding Content}}' CanContentScroll='{{TemplateBinding CanContentScroll}}'/><ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Visibility='{{TemplateBinding ComputedVerticalScrollBarVisibility}}' ViewportSize='{{TemplateBinding ViewportHeight}}' Maximum='{{TemplateBinding ScrollableHeight}}'/></Grid></ControlTemplate></Setter.Value></Setter></Style></Grid.Resources><ScrollViewer VerticalScrollBarVisibility='Visible' HorizontalScrollBarVisibility='Disabled'>{text}</ScrollViewer></Grid>"));
        Add("ScrollViewer custom bar, Border Width 10", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.Resources><Style TargetType='ScrollBar'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Border Width='10' Background='Gray'/></ControlTemplate></Setter.Value></Setter></Style><Style TargetType='ScrollViewer'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{{TemplateBinding Content}}' CanContentScroll='{{TemplateBinding CanContentScroll}}'/><ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Visibility='{{TemplateBinding ComputedVerticalScrollBarVisibility}}' ViewportSize='{{TemplateBinding ViewportHeight}}' Maximum='{{TemplateBinding ScrollableHeight}}'/></Grid></ControlTemplate></Setter.Value></Setter></Style></Grid.Resources><ScrollViewer VerticalScrollBarVisibility='Visible' HorizontalScrollBarVisibility='Disabled'>{text}</ScrollViewer></Grid>"));
        Add("ScrollViewer custom bar, Border Width 24 Margin 2", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.Resources><Style TargetType='ScrollBar'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Border Width='24' Margin='2,0' Background='Gray'/></ControlTemplate></Setter.Value></Setter></Style><Style TargetType='ScrollViewer'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{{TemplateBinding Content}}' CanContentScroll='{{TemplateBinding CanContentScroll}}'/><ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Visibility='{{TemplateBinding ComputedVerticalScrollBarVisibility}}' ViewportSize='{{TemplateBinding ViewportHeight}}' Maximum='{{TemplateBinding ScrollableHeight}}'/></Grid></ControlTemplate></Setter.Value></Setter></Style></Grid.Resources><ScrollViewer VerticalScrollBarVisibility='Visible' HorizontalScrollBarVisibility='Disabled'>{text}</ScrollViewer></Grid>"));
        Add("ScrollViewer custom bar, no fixed width", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.Resources><Style TargetType='ScrollBar'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Border Background='Gray'><Track x:Name='PART_Track'/></Border></ControlTemplate></Setter.Value></Setter></Style><Style TargetType='ScrollViewer'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{{TemplateBinding Content}}' CanContentScroll='{{TemplateBinding CanContentScroll}}'/><ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Visibility='{{TemplateBinding ComputedVerticalScrollBarVisibility}}' ViewportSize='{{TemplateBinding ViewportHeight}}' Maximum='{{TemplateBinding ScrollableHeight}}'/></Grid></ControlTemplate></Setter.Value></Setter></Style></Grid.Resources><ScrollViewer VerticalScrollBarVisibility='Visible' HorizontalScrollBarVisibility='Disabled'>{text}</ScrollViewer></Grid>"));
        Add("ScrollViewer custom bar, OverridesDefaultStyle, Width 24", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.Resources><Style TargetType='ScrollBar'><Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Border Width='24' Background='Gray'/></ControlTemplate></Setter.Value></Setter></Style><Style TargetType='ScrollViewer'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{{TemplateBinding Content}}' CanContentScroll='{{TemplateBinding CanContentScroll}}'/><ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Visibility='{{TemplateBinding ComputedVerticalScrollBarVisibility}}' ViewportSize='{{TemplateBinding ViewportHeight}}' Maximum='{{TemplateBinding ScrollableHeight}}'/></Grid></ControlTemplate></Setter.Value></Setter></Style></Grid.Resources><ScrollViewer VerticalScrollBarVisibility='Visible' HorizontalScrollBarVisibility='Disabled'>{text}</ScrollViewer></Grid>"));
        Add("DataGrid column header 150", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        // 專案的隱含標題樣式（QuillNest 的寫法）：只改 Padding／BorderThickness，範本不變
        Add("DataGrid header, style Padding 8,7 + bottom border", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='8,7'/><Setter Property='BorderThickness' Value='0,0,0,1'/><Setter Property='FontWeight' Value='SemiBold'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 20,0 + Border 3,0", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='20,0'/><Setter Property='BorderThickness' Value='3,0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 8,0 Height 36", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='8,0'/><Setter Property='Height' Value='36'/><Setter Property='VerticalContentAlignment' Value='Center'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 0", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Border 6,0 only", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='BorderThickness' Value='6,0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 1,0,30,0", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='1,0,30,0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 4,0", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='4,0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 6,0", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='6,0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 9,0,0,0", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='9,0,0,0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 0,0,11,0", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='0,0,11,0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 5,0", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='5,0'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style Padding 0,7", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Padding' Value='0,7'/></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("DataGrid header, style replaces Template", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Resources><Style TargetType='DataGridColumnHeader'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridColumnHeader'><Border Padding='30,0'><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style></DataGrid.Resources><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"), Want.Blocked);
        Add("GridViewColumn header 150", W($"<ListView Width='500' HorizontalAlignment='Left'><ListView.View><GridView><GridViewColumn Width='150'><GridViewColumn.Header>{text}</GridViewColumn.Header></GridViewColumn></GridView></ListView.View></ListView>"));

        // ── 7b. 標籤列放在左右兩側（Playnite 的 DatabaseFieldsManagerWindow）──
        // 範本靠 TabStripPlacement 觸發器重排：標籤列變成 Auto 欄，寬度 = 最寬的標題 + 標題 chrome
        const string sideTabControl =
            "<Style TargetType='TabControl'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabControl'>" +
            "<Grid><Grid.ColumnDefinitions><ColumnDefinition x:Name='ColumnDefinition0'/><ColumnDefinition x:Name='ColumnDefinition1' Width='0'/></Grid.ColumnDefinitions>" +
            "<Grid.RowDefinitions><RowDefinition Height='Auto' x:Name='RowDefinition0'/><RowDefinition Height='*' x:Name='RowDefinition1'/></Grid.RowDefinitions>" +
            "<Border x:Name='HeaderBorder' BorderThickness='0,0,0,1' BorderBrush='Gray'><TabPanel x:Name='HeaderPanel' IsItemsHost='True' Panel.ZIndex='1' Width='Auto' Height='Auto'>" +
            "<TabPanel.LayoutTransform><TransformGroup><ScaleTransform ScaleX='1' ScaleY='1'/><SkewTransform AngleX='0' AngleY='0'/><RotateTransform Angle='0'/><TranslateTransform X='0' Y='0'/></TransformGroup></TabPanel.LayoutTransform>" +
            "</TabPanel></Border>" +
            "<Border x:Name='ContentPanel' Grid.Column='0' Grid.Row='1'><ContentPresenter x:Name='PART_SelectedContentHost' ContentSource='SelectedContent'/></Border></Grid>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='TabStripPlacement' Value='Bottom'><Setter Property='Grid.Row' TargetName='ContentPanel' Value='0'/><Setter Property='Height' TargetName='RowDefinition0' Value='*'/><Setter Property='Height' TargetName='RowDefinition1' Value='Auto'/><Setter Property='Grid.Row' TargetName='HeaderBorder' Value='1'/><Setter Property='BorderThickness' TargetName='HeaderBorder' Value='0,1,0,0'/></Trigger>" +
            "<Trigger Property='TabStripPlacement' Value='Left'><Setter Property='Grid.Row' TargetName='HeaderPanel' Value='0'/><Setter Property='Grid.Row' TargetName='ContentPanel' Value='0'/><Setter Property='Grid.Column' TargetName='ContentPanel' Value='1'/><Setter Property='Width' TargetName='ColumnDefinition0' Value='Auto'/><Setter Property='Width' TargetName='ColumnDefinition1' Value='*'/><Setter Property='Height' TargetName='RowDefinition0' Value='*'/><Setter Property='Height' TargetName='RowDefinition1' Value='0'/><Setter Property='Grid.Column' TargetName='HeaderBorder' Value='0'/><Setter Property='BorderThickness' TargetName='HeaderBorder' Value='0,0,1,0'/><Setter Property='Margin' TargetName='HeaderBorder' Value='0,0,0,0'/><Setter Property='Margin' TargetName='ContentPanel' Value='0,0,0,0'/></Trigger>" +
            "<Trigger Property='TabStripPlacement' Value='Right'><Setter Property='Grid.Row' TargetName='HeaderPanel' Value='0'/><Setter Property='Grid.Row' TargetName='ContentPanel' Value='0'/><Setter Property='Grid.Column' TargetName='ContentPanel' Value='0'/><Setter Property='Width' TargetName='ColumnDefinition0' Value='*'/><Setter Property='Width' TargetName='ColumnDefinition1' Value='Auto'/><Setter Property='Height' TargetName='RowDefinition0' Value='*'/><Setter Property='Height' TargetName='RowDefinition1' Value='0'/><Setter Property='Grid.Column' TargetName='HeaderBorder' Value='1'/><Setter Property='BorderThickness' TargetName='HeaderBorder' Value='1,0,0,0'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";
        const string sideTabItem =
            "<Style TargetType='TabItem'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'>" +
            "<Grid x:Name='TabGrid'><Border x:Name='SelectedBorder' Visibility='Collapsed' Margin='1,0,1,0' BorderThickness='0,0,0,2' BorderBrush='Gray'/>" +
            "<TextBlock x:Name='TextContent' Padding='5'><ContentPresenter x:Name='ContentSite' RecognizesAccessKey='True' ContentSource='Header' HorizontalAlignment='Center' Margin='5' VerticalAlignment='Center'/></TextBlock></Grid>" +
            "<ControlTemplate.Triggers><Trigger Property='TabStripPlacement' Value='Left'><Setter Property='BorderThickness' Value='0,0,2,0' TargetName='SelectedBorder'/></Trigger>" +
            "<Trigger Property='IsSelected' Value='True'><Setter Property='Visibility' Value='Visible' TargetName='SelectedBorder'/></Trigger></ControlTemplate.Triggers>" +
            "</ControlTemplate></Setter.Value></Setter></Style>";
        const string plainTabItem =
            "<Style TargetType='TabItem'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'>" +
            "<Border Padding='12,4' BorderThickness='1'><ContentPresenter ContentSource='Header'/></Border>" +
            "</ControlTemplate></Setter.Value></Setter></Style>";
        string Side(string placement, string items, string tabItemStyle = sideTabItem, string extra = "") =>
            W($"<Grid Width='500' HorizontalAlignment='Left'><Grid.Resources>{sideTabControl}{tabItemStyle}</Grid.Resources>" +
              $"<TabControl TabStripPlacement='{placement}'{extra}>{items}</TabControl></Grid>");
        const string probeTab = "<TabItem Header='Platforms'><Border x:Name='probe'/></TabItem>";
        Add("side tabs Left, Playnite templates", Side("Left", probeTab + "<TabItem Header='Categories'/><TabItem Header='Age ratings and regions'/>"));
        Add("side tabs Right, Playnite templates", Side("Right", probeTab + "<TabItem Header='Categories'/><TabItem Header='Age ratings and regions'/>"));
        Add("side tabs Left, probe not in first tab", Side("Left", "<TabItem Header='Categories'/><TabItem Header='Platforms' IsSelected='True'><Border x:Name='probe'/></TabItem>"));
        Add("side tabs Left, TabItem Margin 4 + MinWidth 150", Side("Left", "<TabItem Header='Platforms' Margin='4,0'><Border x:Name='probe'/></TabItem><TabItem Header='Short' MinWidth='150'/>"));
        Add("side tabs Left, bold FontSize 16 headers", Side("Left", probeTab + "<TabItem Header='Age ratings and regions' FontWeight='Bold' FontSize='16'/>"));
        Add("side tabs Left, plain TabItem template", Side("Left", probeTab + "<TabItem Header='Age ratings and regions'/>", plainTabItem));
        Add("side tabs Left, collapsed long tab", Side("Left", probeTab + "<TabItem Header='Age ratings and regions and much more' Visibility='Collapsed'/>"));
        // TabPanel 排在側邊時不算標籤的 Margin（GetDesiredSizeWithoutMargin）：負的不會讓標籤列變窄，正的也不會變寬
        Add("side tabs Left, long tab Margin -10,0", Side("Left", probeTab + "<TabItem Header='Age ratings and regions' Margin='-10,0'/>"));
        Add("side tabs Left, long tab Margin 15,0", Side("Left", probeTab + "<TabItem Header='Age ratings and regions' Margin='15,0'/>"));
        Add("side tabs Top, Playnite templates", Side("Top", probeTab + "<TabItem Header='Categories'/>"));
        Add("side tabs Bottom, Playnite templates", Side("Bottom", probeTab + "<TabItem Header='Categories'/>"));
        Add("side tabs Left, element header", Side("Left", probeTab + "<TabItem><TabItem.Header><StackPanel><TextBlock Text='Wide header here'/></StackPanel></TabItem.Header></TabItem>"), Want.Blocked);
        Add("side tabs Left, bound header", Side("Left", probeTab + "<TabItem Header='{Binding Name}'/>"), Want.Blocked);
        // 預設範本的側邊標籤：拆開量（標籤選取與否、Padding、TabControl 的 Padding／邊框）
        Add("default tabs: Left, long tab selected", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Left'><TabItem Header='Age ratings and regions' IsSelected='True'><Border x:Name='probe'/></TabItem><TabItem Header='Platforms'/></TabControl>"));
        Add("default tabs: Left, TabItem Padding 20,2", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Left'>{probeTab}<TabItem Header='Age ratings and regions' Padding='20,2'/></TabControl>"));
        Add("default tabs: Left, TabControl Padding 0 Border 0", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Left' Padding='0' BorderThickness='0'>{probeTab}<TabItem Header='Age ratings and regions'/></TabControl>"));
        Add("default tabs: Left, TabItem Margin 7,0", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Left'>{probeTab}<TabItem Header='Age ratings and regions' Margin='7,0'/></TabControl>"));
        Add("default tabs: Left, TabItem BorderThickness 4", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Left'>{probeTab}<TabItem Header='Age ratings and regions' BorderThickness='4'/></TabControl>"));
        Add("default tabs: Right, long tab selected", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Right'><TabItem Header='Age ratings and regions' IsSelected='True'><Border x:Name='probe'/></TabItem><TabItem Header='Platforms'/></TabControl>"));
        Add("default tabs: Left, only short tabs", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Left'>{probeTab}<TabItem Header='Ab'/></TabControl>"));

        // WPF 預設範本（Aero2）：標籤列 = 最寬的「標題 + 左右 Padding」+ 2
        // 範本自己決定標籤列在哪：寫死在上方（不理會 TabStripPlacement）、或用 TemplateBinding 讓 DockPanel 跟著放
        const string dockTop = "<TabControl.Template><ControlTemplate TargetType='TabControl'><DockPanel><TabPanel DockPanel.Dock='Top' IsItemsHost='True'/><ContentPresenter ContentSource='SelectedContent'/></DockPanel></ControlTemplate></TabControl.Template>";
        const string dockBound = "<TabControl.Template><ControlTemplate TargetType='TabControl'><DockPanel><TabPanel DockPanel.Dock='{TemplateBinding TabStripPlacement}' IsItemsHost='True'/><ContentPresenter ContentSource='SelectedContent'/></DockPanel></ControlTemplate></TabControl.Template>";
        Add("side tabs Left, template docks the strip on top", W($"<Grid Width='500' HorizontalAlignment='Left'><Grid.Resources>{sideTabItem}</Grid.Resources><TabControl TabStripPlacement='Left'>{dockTop}{probeTab}<TabItem Header='Age ratings and regions'/></TabControl></Grid>"));
        Add("side tabs Left, DockPanel.Dock TemplateBinding", W($"<Grid Width='500' HorizontalAlignment='Left'><Grid.Resources>{sideTabItem}</Grid.Resources><TabControl TabStripPlacement='Left'>{dockBound}{probeTab}<TabItem Header='Age ratings and regions'/></TabControl></Grid>"));
        Add("side tabs Right, DockPanel.Dock TemplateBinding", W($"<Grid Width='500' HorizontalAlignment='Left'><Grid.Resources>{sideTabItem}</Grid.Resources><TabControl TabStripPlacement='Right'>{dockBound}{probeTab}<TabItem Header='Age ratings and regions'/></TabControl></Grid>"));
        Add("side tabs Left, default WPF template", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Left'>{probeTab}<TabItem Header='Age ratings and regions'/></TabControl>"));
        Add("side tabs Right, default WPF template", W($"<TabControl Width='500' HorizontalAlignment='Left' TabStripPlacement='Right'>{probeTab}<TabItem Header='Age ratings and regions'/></TabControl>"));

        // ── 7c. 太長的按鈕被外層裁掉時，按鈕自己右邊的內距／邊距算不算 ──
        // 按鈕：左右 Padding 不對稱（10／30）、Margin 5／20、預設邊框 1。文字超出可用寬度。
        const string longButton = "<Button Padding='10,0,30,0' Margin='5,0,20,0'><TextBlock x:Name='probe' Text='" + Long + "'/></Button>";
        Add("trailing: horizontal StackPanel, after a 100 box", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='100'/>{longButton}</StackPanel>"));
        Add("trailing: horizontal StackPanel in a 300 Border", W($"<Border Width='300' HorizontalAlignment='Left'><StackPanel Orientation='Horizontal'><Border Width='100'/>{longButton}</StackPanel></Border>"));
        Add("trailing: nested horizontal StackPanels", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='100'/><Border Padding='0,0,40,0' Margin='0,0,15,0'><StackPanel Orientation='Horizontal'>{longButton}</StackPanel></Border></StackPanel>"));
        Add("trailing: DockPanel docked left", W($"<DockPanel Width='300' HorizontalAlignment='Left' LastChildFill='False'><Border Width='100'/>{longButton}</DockPanel>"));
        Add("trailing: DockPanel last child fills", W($"<DockPanel Width='300' HorizontalAlignment='Left'><Border Width='100'/>{longButton}</DockPanel>"));
        Add("trailing: Grid star column", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='100'/><ColumnDefinition/></Grid.ColumnDefinitions>" + longButton.Replace("<Button ", "<Button Grid.Column='1' ") + "</Grid>"));
        Add("trailing: Border single child", W($"<Border Width='300' HorizontalAlignment='Left'>{longButton}</Border>"));
        Add("trailing: Button with Width", W("<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='100'/>" + longButton.Replace("<Button ", "<Button Width='150' ") + "</StackPanel>"));
        Add("trailing: TextBlock with right Margin in StackPanel", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='100'/><TextBlock x:Name='probe' Margin='5,0,40,0' Text='{Long}'/></StackPanel>"));

        // Playnite 的按鈕範本：Border(BorderThickness) > ContentPresenter(Margin=Padding)
        const string customButtonStyle = "<Style TargetType='Button'><Setter Property='Padding' Value='10,5,30,5'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border BorderThickness='{TemplateBinding BorderThickness}' BorderBrush='Gray'><ContentPresenter Margin='{TemplateBinding Padding}'/></Border></ControlTemplate></Setter.Value></Setter></Style>";
        const string iconButtonStyle = "<Style TargetType='Button'><Setter Property='Padding' Value='10,5,30,5'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Padding='0,0,12,0'><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><ContentPresenter Margin='{TemplateBinding Padding}'/><Border Grid.Column='1' Width='16'/></Grid></Border></ControlTemplate></Setter.Value></Setter></Style>";
        const string plainButton = "<Button Margin='5,0,20,0'><TextBlock x:Name='probe' Text='" + Long + "'/></Button>";
        Add("trailing: custom template in StackPanel", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><StackPanel.Resources>{customButtonStyle}</StackPanel.Resources><Border Width='100'/>{plainButton}</StackPanel>"));
        Add("trailing: template with an icon column", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><StackPanel.Resources>{iconButtonStyle}</StackPanel.Resources><Border Width='100'/>{plainButton}</StackPanel>"));

        // Canvas 不量子元素：沒寫 Width 的 Canvas 在 Auto 欄裡寬度 0
        Add("Auto column with an empty Canvas", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Canvas/><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("Auto column with a Canvas holding a 200 box", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Canvas Margin='3,0'><Border Width='200' Height='5'/></Canvas><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("Auto column with a bound-visibility CheckBox", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><CheckBox Visibility='{{Binding NoSuchProperty}}'/><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));

        // 跨過 Auto 欄的元素：被跨的欄一起長，長到 Grid 佔滿為止
        const string spanCols = "<Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/><ColumnDefinition Width='60'/></Grid.ColumnDefinitions>";
        Add("spans an Auto column: Auto + star", W($"<Grid Width='300' HorizontalAlignment='Left'>{spanCols}<TextBlock Text='Label'/><TextBlock x:Name='probe' Grid.Row='1' Grid.ColumnSpan='2' Text='{Long}'/></Grid>"));
        Add("spans an Auto column: all three", W($"<Grid Width='300' HorizontalAlignment='Left'>{spanCols}<TextBlock Text='Label'/><TextBlock x:Name='probe' Grid.Row='1' Grid.ColumnSpan='3' Text='{Long}'/></Grid>"));
        Add("spans an Auto column: Auto + fixed", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='50'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='60'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Grid.Column='1' Text='Label'/><TextBlock x:Name='probe' Grid.Row='1' Grid.Column='1' Grid.ColumnSpan='2' Text='{Long}'/></Grid>"));
        Add("spans an Auto column: wrapping text", W($"<Grid Width='300' HorizontalAlignment='Left'>{spanCols}<TextBlock Text='Label'/><TextBlock x:Name='probe' Grid.Row='1' Grid.ColumnSpan='2' TextWrapping='Wrap' Text='{Long}'/></Grid>"));

        // Auto 欄裡只有一個 Run 的 TextBlock（包在 Hyperlink 裡）：兄弟的寬度量得出來
        Add("Auto column with a Hyperlink Run", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Margin='0,0,6,0'>\n  <Hyperlink NavigateUri='https://example.com'>\n    <Run Text='User manual and more'/>\n  </Hyperlink>\n</TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("Auto column with a FontSize 16 Span Run", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock><Span FontSize='16'><Run>Bigger words here</Run></Span></TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));

        // 另一列有跨欄的元素時，它會不會把 Auto 欄撐大（probe 在星號欄）
        const string autoStar = "<Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>";
        Add("spanning sibling: wide, spans Auto + star", W($"<Grid Width='300' HorizontalAlignment='Left'>{autoStar}<TextBlock Text='Label'/><TextBlock Grid.Row='1' Grid.ColumnSpan='2' Text='{Long}'/><TextBlock x:Name='probe' Grid.Row='2' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("spanning sibling: narrow, spans Auto + star", W($"<Grid Width='300' HorizontalAlignment='Left'>{autoStar}<TextBlock Text='Label'/><TextBlock Grid.Row='1' Grid.ColumnSpan='2' Text='ab'/><TextBlock x:Name='probe' Grid.Row='2' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("spanning sibling: Border 200, spans Auto + star", W($"<Grid Width='300' HorizontalAlignment='Left'>{autoStar}<TextBlock Text='Label'/><Border Grid.Row='1' Grid.ColumnSpan='2' Width='200'/><TextBlock x:Name='probe' Grid.Row='2' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("spanning sibling: spans two Auto columns", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions><TextBlock Text='Label'/><Border Grid.Row='1' Grid.ColumnSpan='2' Width='150'/><TextBlock x:Name='probe' Grid.Row='2' Grid.Column='2' Text='{Long}'/></Grid>"), Want.Blocked);
        Add("spanning sibling: Auto + star, star row", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition/><RowDefinition Height='Auto'/></Grid.RowDefinitions><TextBlock Text='Label'/><Border Grid.Row='1' Grid.ColumnSpan='2' Width='200'/><TextBlock x:Name='probe' Grid.Row='2' Grid.Column='1' Text='{Long}'/></Grid>"));

        // 同樣的 Grid 放在水平 StackPanel 裡（用無限寬量）：星號欄變得跟 Auto 一樣，跨欄的元素會撐大 Auto 欄
        Add("spanning sibling: grid in a horizontal StackPanel", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Grid>{autoStar}<TextBlock Text='Label'/><Border Grid.Row='1' Grid.ColumnSpan='2' Width='200'/><TextBlock x:Name='probe' Grid.Row='2' Grid.Column='1' Text='{Long}'/></Grid></StackPanel>"));
        Add("spanning sibling: grid in an Auto column", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Grid>{autoStar}<TextBlock Text='Label'/><Border Grid.Row='1' Grid.ColumnSpan='2' Width='200'/><TextBlock x:Name='probe' Grid.Row='2' Grid.Column='1' Text='{Long}'/></Grid></Grid>"));

        // Auto 欄裡一行好幾個 Run（字級不同）、勾選框的內容是一個 TextBlock（Kindling 的「動態補間（實驗性）」）
        const string autoCol = "<Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions>";
        Add("inline line: two Runs, sizes 12 and 10", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<TextBlock>\n  <Run Text='Motion fill'/>\n  <Run Text='(experimental)' FontSize='10'/>\n</TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("inline line: Runs without space between", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<TextBlock><Run Text='Motion'/><Run Text='fill' FontWeight='Bold'/></TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("inline line: CheckBox with a two-Run TextBlock", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<CheckBox Margin='0,0,8,0'><TextBlock>\n  <Run Text='Motion fill'/>\n  <Run Text='(experimental)' FontSize='10'/>\n</TextBlock></CheckBox><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("inline line: Button with a TextBlock child", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<Button Padding='12,4'><TextBlock Text='Regenerate all'/></Button><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));

        Add("ws probe: gap newline only", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<TextBlock><Run Text='Motion fill'/>\n  <Run Text='(experimental)' FontSize='10'/></TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("ws probe: leading and trailing only", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<TextBlock>\n  <Run Text='Motion fill'/><Run Text='(experimental)' FontSize='10'/>\n</TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("ws probe: gap single space", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<TextBlock><Run Text='Motion fill'/> <Run Text='(experimental)' FontSize='10'/></TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("ws probe: gap single space, same size", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<TextBlock><Run Text='Motion fill'/> <Run Text='(experimental)'/></TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));
        Add("ws probe: no gap", W($"<Grid Width='400' HorizontalAlignment='Left'>{autoCol}<TextBlock><Run Text='Motion fill'/><Run Text='(experimental)' FontSize='10'/></TextBlock><TextBlock x:Name='probe' Grid.Column='1' Text='{Long}'/></Grid>"));

        // 文字由內嵌樣式（含 DataTrigger）給的 TextBlock 當鄰居：取每種文字裡最寬的（Kindling 的「進階 ▾／▴」）
        Add("style text: button beside the text", W($"<DockPanel Width='400' HorizontalAlignment='Left'><Button DockPanel.Dock='Right' Padding='10,4'><TextBlock><TextBlock.Style><Style TargetType='TextBlock'><Setter Property='Text' Value='Advanced options'/><Style.Triggers><DataTrigger Binding='{{Binding Open}}' Value='True'><Setter Property='Text' Value='Hide'/></DataTrigger></Style.Triggers></Style></TextBlock.Style></TextBlock></Button><TextBlock x:Name='probe' Text='{Long}'/></DockPanel>"));

        // DataGrid 的星號欄：欄寬 = DataGrid 內容寬 − 其他欄；標題再扣標題的內距（QuillNest 的工作量模板）
        Add("star column: rows overflow (bar shows)", W($"<DataGrid xmlns:sys='clr-namespace:System;assembly=mscorlib' Width='500' Height='120' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column' CanUserAddRows='False'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80' Binding='{{Binding}}'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn><DataGridTextColumn Width='100' Header='A'/></DataGrid.Columns><sys:String>row 0</sys:String><sys:String>row 1</sys:String><sys:String>row 2</sys:String><sys:String>row 3</sys:String><sys:String>row 4</sys:String><sys:String>row 5</sys:String><sys:String>row 6</sys:String><sys:String>row 7</sys:String><sys:String>row 8</sys:String><sys:String>row 9</sys:String><sys:String>row 10</sys:String><sys:String>row 11</sys:String></DataGrid>"));
        Add("star column: alone", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("star column: with fixed 100 and 75", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn><DataGridTextColumn Width='100' Header='A'/><DataGridCheckBoxColumn Width='75' Header='B'/></DataGrid.Columns></DataGrid>"));
        Add("star column: Auto MaxWidth 120 beside (short header)", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn><DataGridTextColumn Width='Auto' MinWidth='70' MaxWidth='120' Header='Type'/></DataGrid.Columns></DataGrid>"), Want.Blocked);
        Add("star column: Auto MaxWidth 120 beside (long header)", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn><DataGridTextColumn Width='Auto' MinWidth='70' MaxWidth='120' Header='A very long column header'/></DataGrid.Columns></DataGrid>"), Want.Blocked);
        Add("star column: two stars", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn><DataGridTextColumn Width='*' MinWidth='120' Header='Other'/><DataGridTextColumn Width='130' Header='C'/></DataGrid.Columns></DataGrid>"));
        Add("star column: 2* and *", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column'><DataGrid.Columns><DataGridTextColumn Width='2*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn><DataGridTextColumn Width='*' Header='Other'/></DataGrid.Columns></DataGrid>"));
        Add("star column: MinWidth wins", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn><DataGridTextColumn Width='450' Header='A'/></DataGrid.Columns></DataGrid>"));
        Add("star column: bar disabled", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column' ScrollViewer.VerticalScrollBarVisibility='Disabled'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn><DataGridTextColumn Width='100' Header='A'/></DataGrid.Columns></DataGrid>"));
        Add("star column: border 4", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False' HeadersVisibility='Column' BorderThickness='4'><DataGrid.Columns><DataGridTextColumn Width='*' MinWidth='80'><DataGridTextColumn.Header><TextBlock x:Name='probe' Text='{Long}'/></DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));

        // ── 8. 快顯 ──
        Add("text in Button.ToolTip", W($"<Button Width='80' HorizontalAlignment='Left' Content='x'><Button.ToolTip><TextBlock x:Name='probe' Text='{Long}'/></Button.ToolTip></Button>"), Want.Open);

        // ── 9. 元素自己有寬度、放在用無限寬量子元素的容器裡（回報 1：ZeroCel CanvasView.xaml:1171）──
        //    看得見的寬度是它自己的 Width；換行有沒有發生由 --wrap 另外比
        foreach (var (name, xaml) in WrapCases(wrap: false))
            Add(name, xaml);

        return cases;
    }

    /// <summary>
    /// 換行寬度的案例：probe 一定是 TextBlock、文字夠長。寬度比對照一般案例跑；<c>--wrap</c> 另外比行數。
    /// </summary>
    /// <param name="wrap">false = 寬度比對用：不換行。有 MaxWidth 的元素換行之後會縮到最寬的那一行（117.5，不是 120），
    /// 那不是裁切，拿 ActualWidth 比會誤判成高估；工具報的 120 是 Measure 的約束，對文字來說才是對的。</param>
    private static IEnumerable<(string Name, string Xaml)> WrapCases(bool wrap)
    {
        static string W(string inner) => $"<Window {Ns} Width='700' Height='300' WindowStyle='None' AllowsTransparency='True'>{inner}</Window>";
        var wrapping = wrap ? " TextWrapping='Wrap'" : "";
        var probe = $"x:Name='probe'{wrapping} Text='{Long}'";

        yield return ("own width in a horizontal stack", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='50'/><TextBlock Width='120' {probe}/></StackPanel>"));
        yield return ("own MaxWidth in a horizontal stack", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='50'/><TextBlock MaxWidth='120' {probe}/></StackPanel>"));
        yield return ("Border width in a horizontal stack", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='50'/><Border Width='120'><TextBlock {probe}/></Border></StackPanel>"));
        yield return ("no own width in a horizontal stack", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='50'/><TextBlock {probe}/></StackPanel>"));
        yield return ("own width wider than the stack", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left'><Border Width='100'/><TextBlock Width='400' {probe}/></StackPanel>"));
        yield return ("own width in an Auto column", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Width='120' {probe}/><Border Grid.Column='1'/></Grid>"));
        yield return ("own width wider than a fixed column", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='100'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Width='300' {probe}/><Border Grid.Column='1'/></Grid>"));
        yield return ("own MaxWidth narrower than a fixed column", W($"<Grid Width='300' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='200'/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock MaxWidth='120' HorizontalAlignment='Left' {probe}/><Border Grid.Column='1'/></Grid>"));
        yield return ("WrapPanel ItemWidth", W($"<WrapPanel ItemWidth='120' Width='300' HorizontalAlignment='Left'><TextBlock {probe}/></WrapPanel>"));
        yield return ("vertical stack (control)", W($"<StackPanel Width='120' HorizontalAlignment='Left'><TextBlock {probe}/></StackPanel>"));
    }

    /// <summary>
    /// 回報 4（ZeroCel）：按鈕／勾選框的 ContentTemplate 是「TextBlock Text="{Binding}" TextWrapping="Wrap"」。
    /// probe 是控制項；WPF 這邊數的是範本產生的那個 TextBlock 的行數。
    /// </summary>
    private static IEnumerable<(string Name, string Xaml)> ContentTemplateWrapCases()
    {
        const string res = "<Window.Resources>"
            + "<DataTemplate x:Key='WrapCenterText'><TextBlock Text='{Binding}' TextWrapping='Wrap' TextAlignment='Center'/></DataTemplate>"
            + "<DataTemplate x:Key='WrapLeftText'><TextBlock Text='{Binding}' TextWrapping='Wrap'/></DataTemplate>"
            + "<DataTemplate x:Key='Plain'><TextBlock Text='{Binding}'/></DataTemplate>"
            + "</Window.Resources>";
        static string W(string inner) => $"<Window {Ns} Width='700' Height='400' WindowStyle='None' AllowsTransparency='True'>{res}{inner}</Window>";
        var center = $"x:Name='probe' ContentTemplate='{{StaticResource WrapCenterText}}' Content='{Long}'";
        var left = $"x:Name='probe' ContentTemplate='{{StaticResource WrapLeftText}}' Content='{Long}'";
        const string cols = "<Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>";

        yield return ("template: Button in a star column", W($"<Grid Width='450' HorizontalAlignment='Left' VerticalAlignment='Top'>{cols}<Button Grid.Column='1' {center}/></Grid>"));
        yield return ("template: Button with Padding", W($"<Grid Width='200' HorizontalAlignment='Left' VerticalAlignment='Top'><Button Padding='20,4' {center}/></Grid>"));
        yield return ("template: Button aligned left", W($"<Grid Width='200' HorizontalAlignment='Left' VerticalAlignment='Top'><Button HorizontalAlignment='Left' {center}/></Grid>"));
        yield return ("template: CheckBox in a vertical stack", W($"<StackPanel Width='200' HorizontalAlignment='Left'><CheckBox {left}/></StackPanel>"));
        yield return ("template: Button in a horizontal stack", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left' VerticalAlignment='Top'><Border Width='50'/><Button {center}/></StackPanel>"));
        yield return ("template: Button Width in a horizontal stack", W($"<StackPanel Orientation='Horizontal' Width='300' HorizontalAlignment='Left' VerticalAlignment='Top'><Border Width='50'/><Button Width='120' {center}/></StackPanel>"));
        yield return ("template: no wrapping (control)", W($"<StackPanel Width='200' HorizontalAlignment='Left'><Button x:Name='probe' ContentTemplate='{{StaticResource Plain}}' Content='{Long}'/></StackPanel>"));
    }

    /// <summary>
    /// --wrap：同一段 XAML 的 probe TextBlock，WPF 實際排成幾行 vs 工具算幾行。
    /// 比寬度看不出「換行有沒有發生」—— 回報 1 的案例寬度對（120）、行數錯（工具說 1 行、WPF 兩行）。
    /// 工具這邊走正式的掃描器（字型、寬度、換行旗標都照 check 的路），行數用 CheckAnalyzer 的排法。
    /// </summary>
    private static int WrapChecks(FontResolver fonts, ChromeTable chrome)
    {
        Console.WriteLine($"{"case",-46} {"WPF lines",9} {"XamlFit",8}  wrap at / visible    verdict");
        Console.WriteLine(new string('-', 100));
        var bad = 0;

        foreach (var (name, xaml) in WrapCases(wrap: true).Concat(ContentTemplateWrapCases()))
        {
            int wpfLines;
            double wpfWidth;
            var window = (Window)XamlReader.Parse(xaml);
            window.Left = -20000; window.Top = -20000; window.ShowInTaskbar = false; window.ShowActivated = false;
            window.Show();
            window.UpdateLayout();
            // 控制項的 probe（回報 4）：範本產生的 TextBlock 在視覺樹裡，FindName 找不到
            var named = (FrameworkElement)window.FindName("probe");
            var probe = named as TextBlock ?? FindText(named, Long)
                ?? throw new InvalidOperationException($"{name}: no TextBlock showing the probe text");
            // ⚠ 不能用 ActualHeight：直接放在視窗裡的 TextBlock 會被拉高到整個視窗。DesiredSize 是文字自己的高度
            wpfLines = (int)Math.Round(probe.DesiredSize.Height / (probe.FontSize * probe.FontFamily.LineSpacing));
            wpfWidth = probe.ActualWidth;
            window.Close();

            var dir = Path.Combine(Path.GetTempPath(), "xamlfit-oracle-wrap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "Case.xaml"), xaml);
                var scan = new XamlScanner(chrome, new ScanOptions { DefaultFontFamily = DefaultFamily, DefaultFontSize = 12 }, fonts).Scan(dir);
                var site = scan.Sites.FirstOrDefault(s => s.Text == Long);
                var ours = site is null ? null : CheckAnalyzer.LineCount(site, Long, fonts, DefaultFamily);
                var width = site?.Width;
                var wrapAt = width is null ? "(n/a)"
                    : width.Status != WidthStatus.Known ? $"({width.Status})"
                    : width.WrapDefeated ? "defeated"
                    : $"{width.WrapPx ?? width.Px,6:F1}";
                var visible = width is { Status: WidthStatus.Known } ? $"{width.Px,6:F1}" : "";
                string verdict;
                if (ours is null) { verdict = "WRONG: not measured"; bad++; }
                else if (ours == wpfLines) verdict = "match";
                else { verdict = $"MISMATCH (WPF {wpfLines}, XamlFit {ours})"; bad++; }
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{name,-46} {wpfLines,9} {ours?.ToString() ?? "-",8}  {wrapAt} / {visible,-8}   {verdict}   (WPF ActualWidth {wpfWidth:F1})"));
            }
            finally { Directory.Delete(dir, true); }
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "all line counts match" : $"{bad} mismatch(es)");
        return bad;
    }

    private static TextBlock? FindText(DependencyObject root, string text)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock t && t.Text == text) return t;
            if (FindText(child, text) is { } found) return found;
        }
        return null;
    }

    /// <summary>probe 的水平範圍，被每一層祖先的版面裁切區交集之後還剩多寬。</summary>
    private static double VisibleWidth(FrameworkElement probe, Window root)
    {
        if (PresentationSource.FromVisual(probe) is null) return probe.ActualWidth;   // 不在畫面上（快顯還沒開）

        double left = 0, right = probe.ActualWidth;

        DependencyObject? cur = probe;
        while (cur is not null)
        {
            if (cur is FrameworkElement fe)
            {
                Rect? clip = null;
                if (LayoutInformation.GetLayoutClip(fe) is { } g) clip = g.Bounds;
                if (fe.ClipToBounds || fe is ScrollContentPresenter)
                {
                    var b = new Rect(0, 0, fe.ActualWidth, fe.ActualHeight);
                    clip = clip is { } c ? Rect.Intersect(c, b) : b;
                }
                if (ReferenceEquals(fe, root) && root.Content is FrameworkElement content)
                {
                    // 視窗的工作區就是最外層的裁切
                    var origin = content.TransformToAncestor(root).Transform(new Point(0, 0));
                    var parent = VisualTreeHelper.GetParent(content) as FrameworkElement;
                    var b = new Rect(origin.X, origin.Y, parent?.ActualWidth ?? content.ActualWidth, 10000);
                    clip = clip is { } c ? Rect.Intersect(c, b) : b;
                }

                if (clip is { } r)
                {
                    if (r.IsEmpty) return 0;
                    var toAncestor = ReferenceEquals(probe, fe) ? Transform.Identity : probe.TransformToAncestor(fe);
                    if (toAncestor.Inverse is { } inv)
                    {
                        var p0 = inv.Transform(new Point(r.Left, r.Top));
                        var p1 = inv.Transform(new Point(r.Right, r.Bottom));
                        left = Math.Max(left, Math.Min(p0.X, p1.X));
                        right = Math.Min(right, Math.Max(p0.X, p1.X));
                    }
                }
            }
            cur = VisualTreeHelper.GetParent(cur);
        }

        return Math.Max(0, right - left);
    }

    /// <summary>佈景的預設樣式給每種控制項的 Padding／BorderThickness（左右合計）。
    /// 自訂範本裡的 {TemplateBinding Padding} 在專案沒有設定時拿到的就是這個值。</summary>
    private static void Defaults()
    {
        Func<Control>[] makers =
        [
            () => new Button(), () => new ToggleButton(), () => new RepeatButton(), () => new CheckBox(), () => new RadioButton(),
            () => new Label(), () => new ContentControl(), () => new UserControl(), () => new ComboBox(), () => new ComboBoxItem(),
            () => new ListBoxItem(), () => new ListViewItem(), () => new TreeViewItem(), () => new MenuItem(), () => new TabItem(),
            () => new TabControl(), () => new GroupBox(), () => new Expander(), () => new TextBox(), () => new PasswordBox(),
            () => new StatusBar(), () => new StatusBarItem(), () => new HeaderedContentControl(), () => new ScrollViewer(),
            () => new ListBox(), () => new ListView(), () => new TreeView(), () => new ItemsControl(), () => new DataGrid(),
            () => new DataGridColumnHeader(), () => new GridViewColumnHeader(),
        ];
        var host = Host();
        var panel = new StackPanel();
        host.Content = panel;
        host.Show();
        foreach (var make in makers)
        {
            var c = make();
            if (c is MenuItem mi) { var menu = new Menu(); menu.Items.Add(mi); panel.Children.Add(menu); }
            else if (c is TabItem ti) { var tc = new TabControl(); tc.Items.Add(ti); panel.Children.Add(tc); }
            else panel.Children.Add(c);
            host.UpdateLayout();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{c.GetType().Name,-26} padding {c.Padding.Left + c.Padding.Right,5:F1} ({c.Padding})  border {c.BorderThickness.Left + c.BorderThickness.Right,5:F1}  minWidth {c.MinWidth}  fontSize {c.FontSize}"));
        }
        host.Close();
    }

    /// <summary>
    /// --popups：快顯宿主（ContextMenu、ToolTip、子選單的 Popup、Popup）在內容外面吃掉多少寬度。
    /// 回報 2 §3-3 要給選單一個宣告的最大寬度（popupMaxWidth），內距表就得有這幾個；快顯沒打開過就沒排版（坑 61），
    /// 所以放進真的視窗、真的打開再量。內容放一個已知寬度的 Border，量兩個寬度確認斜率是 1。
    /// </summary>
    private static void Popups()
    {
        var host = Host();
        var panel = new StackPanel();
        host.Content = panel;
        host.Show();
        host.UpdateLayout();
        void Idle() => host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        static T? Find<T>(DependencyObject d) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (c is T t) return t;
                if (Find<T>(c) is { } deeper) return deeper;
            }
            return null;
        }

        foreach (var w in new[] { 100.0, 200.0 })
        {
            // ContextMenu：一個 MenuItem，Header 是已知寬度的 Border
            var cm = new ContextMenu();
            var mi = new MenuItem { Header = new Border { Width = w, Height = 20 } };
            cm.Items.Add(mi);
            var btn = new Button { Content = "x", ContextMenu = cm };
            panel.Children.Add(btn);
            host.UpdateLayout();
            cm.PlacementTarget = btn;
            cm.IsOpen = true;
            Idle();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ContextMenu  content {w,5:F0}: menu {cm.ActualWidth,7:F2}  item {mi.ActualWidth,7:F2}  → menu chrome {cm.ActualWidth - mi.ActualWidth,6:F2}  item chrome {mi.ActualWidth - w,6:F2}  (menu Padding {cm.Padding} Border {cm.BorderThickness}; item Padding {mi.Padding} Border {mi.BorderThickness})"));
            cm.IsOpen = false;

            // ToolTip
            var tt = new ToolTip { Content = new Border { Width = w, Height = 20 } };
            var b2 = new Button { Content = "y", ToolTip = tt };
            panel.Children.Add(b2);
            host.UpdateLayout();
            tt.PlacementTarget = b2;
            tt.IsOpen = true;
            Idle();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ToolTip      content {w,5:F0}: tip  {tt.ActualWidth,7:F2}  → chrome {tt.ActualWidth - w,6:F2}  (Padding {tt.Padding} Border {tt.BorderThickness})"));
            tt.IsOpen = false;

            // 子選單：Menu > MenuItem(頂層) > MenuItem(子項，Header 是 Border)
            var menu = new Menu();
            var top = new MenuItem { Header = "Top" };
            var sub = new MenuItem { Header = new Border { Width = w, Height = 20 } };
            top.Items.Add(sub);
            menu.Items.Add(top);
            panel.Children.Add(menu);
            host.UpdateLayout();
            top.IsSubmenuOpen = true;
            Idle();
            var popup = Find<Popup>(top);
            var popupChild = popup?.Child as FrameworkElement;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Submenu      content {w,5:F0}: popup child {(popupChild?.ActualWidth ?? double.NaN),7:F2}  sub item {sub.ActualWidth,7:F2}  → submenu chrome {(popupChild?.ActualWidth ?? double.NaN) - sub.ActualWidth,6:F2}  sub item chrome {sub.ActualWidth - w,6:F2}  (sub Padding {sub.Padding} Border {sub.BorderThickness}; role {sub.Role})"));
            top.IsSubmenuOpen = false;

            // 裸 Popup：沒有範本，子元素直接排
            var pop = new Popup { Child = new Border { Width = w, Height = 20 }, PlacementTarget = panel, IsOpen = true };
            Idle();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Popup        content {w,5:F0}: child {((FrameworkElement)pop.Child).ActualWidth,7:F2}"));
            pop.IsOpen = false;
        }

        // 0.7.0：快顯的字型不從擁有者繼承。視窗設 Courier New 30，看 ToolTip／ContextMenu／MenuItem 實際用哪支字型，
        // 再看 ToolTip 的字串內容是不是「系統字型量出來的寬度 + 內距 14」（ToolTip="…" 位置的量法）
        host.FontFamily = new System.Windows.Media.FontFamily("Courier New");
        host.FontSize = 30;
        var sys = SystemFonts.StatusFontFamily;
        var sysSize = SystemFonts.StatusFontSize;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"== popup fonts (window: Courier New 30; system status font: {sys.Source} {sysSize}) =="));
        const string tipText = "Save the file as a new name";
        var stringTip = new ToolTip { Content = tipText };
        var owner = new Button { Content = "z", ToolTip = stringTip };
        var menu2 = new ContextMenu();
        var item2 = new MenuItem { Header = "Open" };
        menu2.Items.Add(item2);
        owner.ContextMenu = menu2;
        panel.Children.Add(owner);
        host.UpdateLayout();
        stringTip.PlacementTarget = owner;
        stringTip.IsOpen = true;
        menu2.PlacementTarget = owner;
        menu2.IsOpen = true;
        Idle();
        var typeface = new System.Windows.Media.Typeface(stringTip.FontFamily, stringTip.FontStyle, stringTip.FontWeight, stringTip.FontStretch);
        var text = new System.Windows.Media.FormattedText(tipText, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
            typeface, stringTip.FontSize, System.Windows.Media.Brushes.Black, 1.0);
        var same = (string a, double s) => a == sys.Source && Math.Abs(s - sysSize) < 0.01 ? "system font" : "NOT the system font";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ToolTip      {stringTip.FontFamily.Source} {stringTip.FontSize}  ({same(stringTip.FontFamily.Source, stringTip.FontSize)})"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ContextMenu  {menu2.FontFamily.Source} {menu2.FontSize}  ({same(menu2.FontFamily.Source, menu2.FontSize)})"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"MenuItem     {item2.FontFamily.Source} {item2.FontSize}  ({same(item2.FontFamily.Source, item2.FontSize)})"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"string tip   width {stringTip.ActualWidth:F2} = text {text.WidthIncludingTrailingWhitespace:F2} + {stringTip.ActualWidth - text.WidthIncludingTrailingWhitespace:F2} (table: 14)"));
        stringTip.IsOpen = false;
        menu2.IsOpen = false;
        host.Close();
    }

    /// <summary>
    /// --watermark mahapps|md2|md3|handycontrol|wpfui：佈景的浮水印／提示文字畫在哪裡、多寬、用什麼字型。
    /// 0.7.0 要把它們當成文字位置；假設是「跟 TextBox 的文字同一塊區域」（寬 = TextBox 寬 − 佈景量到的 TextBox chrome）。
    /// 這裡在真的視窗裡打開，找出畫著浮水印的 TextBlock，量它的版面槽寬度跟字型，跟那個假設比。
    /// </summary>
    private static void Watermark(Application app, string which)
    {
        var profile = Themes.Load(app, which);
        const string mark = "Type the name of the file you want to open here please";
        var (xmlns, attr, element) = which switch
        {
            "mahapps" => ("xmlns:t='http://metro.mahapps.com/winfx/xaml/controls'", $"t:TextBoxHelper.Watermark='{mark}'", "TextBox"),
            "md2" or "md3" => ("xmlns:t='http://materialdesigninxaml.net/winfx/xaml/themes'", $"t:HintAssist.Hint='{mark}'", "TextBox"),
            "handycontrol" => ("xmlns:t='https://handyorg.github.io/handycontrol'", $"t:InfoElement.Placeholder='{mark}'", "TextBox"),
            "wpfui" => ("xmlns:t='http://schemas.lepo.co/wpfui/2022/xaml'", $"PlaceholderText='{mark}'", "t:TextBox"),
            _ => throw new ArgumentException(which),
        };
        var chrome = profile.Controls.TryGetValue("TextBox", out var c) ? c.Chrome : null;
        Console.WriteLine($"theme: {profile.Id}; TextBox chrome in the data: {chrome?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        foreach (var width in new[] { 200, 300 })
        foreach (var extra in new[] { "", "Padding='10,0,10,0'", "FontSize='20'" })
        {
            var host = (Window)XamlReader.Parse(
                $"<Window {Ns} {xmlns} Width='700' Height='200' WindowStyle='None' AllowsTransparency='True'>" +
                $"<StackPanel><{element} Width='{width}' HorizontalAlignment='Left' {attr} {extra}/></StackPanel></Window>");
            host.Left = -20000; host.Top = -20000; host.ShowInTaskbar = false; host.ShowActivated = false;
            host.Show();
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var box = (FrameworkElement)((StackPanel)host.Content).Children[0];
            TextBlock? found = null;
            void Walk(DependencyObject d)
            {
                for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d) && found is null; i++)
                {
                    var ch = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                    if (ch is TextBlock tb && tb.Text == mark && tb.IsVisible) { found = tb; return; }
                    Walk(ch);
                }
            }
            Walk(box);
            if (found is null) { Console.WriteLine($"  {element} {width} {extra,-22}: watermark not drawn"); host.Close(); continue; }
            var slot = LayoutInformation.GetLayoutSlot(found);
            var visible = slot.Width - found.Margin.Left - found.Margin.Right;
            var scale = found.RenderTransform is System.Windows.Media.ScaleTransform st ? st.ScaleX : 1;
            var expected = chrome is { } ch2 ? width - ch2 : double.NaN;
            var boxFont = (Control)box;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {element} {width} {extra,-22}: slot {visible,7:F2}  expected {expected,7:F2}  diff {visible - expected,6:F2}  " +
                $"font {found.FontFamily.Source} {found.FontSize} {found.FontWeight} (box {boxFont.FontFamily.Source} {boxFont.FontSize})  trim {found.TextTrimming} scale {scale:F2}"));
            host.Close();
        }
    }

    private static Window Host()
    {
        var host = (Window)XamlReader.Parse($"<Window {Ns} Width='600' Height='200' WindowStyle='None' AllowsTransparency='True'/>");
        host.Left = -20000;
        host.Top = -20000;
        host.ShowInTaskbar = false;
        host.ShowActivated = false;
        return host;
    }

    /// <summary>跟版面無關、只跟文字量測有關的幾個事實。</summary>
    private static void TextChecks()
    {
        Console.WriteLine("== access key: is the underscore of _Save drawn? ==");
        foreach (var make in new Func<string, FrameworkElement>[]
                 {
                     s => new TextBlock { Text = s },
                     s => new Button { Content = s }, s => new Label { Content = s }, s => new CheckBox { Content = s },
                     s => new RadioButton { Content = s }, s => new ToggleButton { Content = s },
                     s => new GroupBox { Header = s }, s => new TabItem { Header = s }, s => new MenuItem { Header = s },
                     s => new Expander { Header = s }, s => new ListBoxItem { Content = s }, s => new ComboBoxItem { Content = s },
                     s => new StatusBarItem { Content = s }, s => new ContentControl { Content = s },
                     s => new HeaderedContentControl { Header = s },
                 })
        {
            var with = Desired(make("_Save"));
            var without = Desired(make("Save"));
            var doubled = Desired(make("A__B"));
            var single = Desired(make("A_B"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{make("x").GetType().Name,-24} '_Save' {with,7:F2}  'Save' {without,7:F2}  'A__B' {doubled,7:F2}  'A_B' {single,7:F2}  -> {(Math.Abs(with - without) < 0.01 ? "ACCESS KEY (not drawn)" : "drawn")}"));
        }

        Console.WriteLine();
        Console.WriteLine("== simulated bold: wider than regular? ==");
        foreach (var family in new[] { "Segoe UI Symbol", "Segoe MDL2 Assets", "Webdings", "Segoe UI", "Microsoft JhengHei UI", "Consolas" })
        {
            var regular = TextWidth("Hello World 123", family, FontWeights.Normal, TextFormattingMode.Ideal, 12);
            var bold = TextWidth("Hello World 123", family, FontWeights.Bold, TextFormattingMode.Ideal, 12);
            var tf = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            tf.TryGetGlyphTypeface(out var gt);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{family,-24} regular {regular,8:F3}  bold {bold,8:F3}  simulations={gt?.StyleSimulations}  face weight={gt?.Weight}"));
        }

        Console.WriteLine();
        Console.WriteLine("== TextFormattingMode: Display relative to Ideal ==");
        string[] samples =
        [
            "Save", "Cancel", "Einstellungen speichern", "Paramètres de l'application", "OK",
            "Wiederherstellen", "Benutzerdefinierte Tastenkombinationen", "iiiiiiiiiiiiiiiiiiii", "WWWWWWWWWW",
            "The quick brown fox jumps over the lazy dog", "設定", "アプリケーションの設定", "Dateiname: report_final_v2.docx",
        ];
        foreach (var family in new[] { "Segoe UI", "Tahoma", "Arial", "Calibri", "Consolas", "Microsoft JhengHei UI" })
        {
            double worstWider = 0, worstNarrower = 0, worstPx = 0;
            var worstText = "";
            foreach (var size in new[] { 9.0, 10, 11, 12, 13, 14, 16, 18, 20, 24 })
            foreach (var s in samples)
            {
                var ideal = TextWidth(s, family, FontWeights.Normal, TextFormattingMode.Ideal, size);
                var display = TextWidth(s, family, FontWeights.Normal, TextFormattingMode.Display, size);
                var rel = (display - ideal) / ideal * 100;
                if (rel > worstWider) { worstWider = rel; worstPx = display - ideal; worstText = $"'{s}' @{size}"; }
                if (rel < worstNarrower) worstNarrower = rel;
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{family,-24} Display wider by up to {worstWider,6:F2}% ({worstPx:F2}px, {worstText}); narrower by up to {-worstNarrower,6:F2}%"));
        }
    }

    private static double Desired(FrameworkElement e)
    {
        var host = Host();
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (e is MenuItem mi) { var menu = new Menu(); menu.Items.Add(mi); panel.Children.Add(menu); }
        else if (e is TabItem ti) { var tc = new TabControl(); tc.Items.Add(ti); panel.Children.Add(tc); }
        else panel.Children.Add(e);
        host.Content = panel;
        host.Show();
        host.UpdateLayout();
        var w = e.ActualWidth;
        host.Close();
        return w;
    }

    private static double TextWidth(string s, string family, FontWeight weight, TextFormattingMode mode, double size)
    {
        var ft = new FormattedText(
            s, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal),
            size, Brushes.Black, null, mode, 1.0);
        return ft.WidthIncludingTrailingWhitespace;
    }
}
