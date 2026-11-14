// 產生 data/control-chrome.json —— 各控制項「吃掉多少水平空間」的表。
//
// 做法：把一個**已知寬度**的內容塞進控制項，讓 WPF 量一次，
//       控制項的 DesiredSize.Width 減掉內容寬度就是全部的 chrome。
//
// 這個方法的好處是它不在乎 chrome 從哪裡來 —— Padding、BorderThickness、
// CheckBox 的方框、ComboBox 的下拉按鈕、MenuItem 的圖示欄，一律一次量進去。
// 解析佈景 XAML 做不到這點（Aero2 是壓縮過的，而且 chrome 散在 ControlTemplate 裡）。
//
// ⚠ 拋棄式工具，Windows only。重產方式寫在 data/control-chrome.json 的 note 欄位。
//   正式工具只讀產出的 JSON，不相依 WPF。

using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

internal static class Program
{
    private const double ProbeWidth = 100.0;
    private const double ProbeHeight = 20.0;

    [STAThread]
    private static int Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : "control-chrome.json";

        // (名稱, 產生器, 內容放在哪個屬性)
        (string Name, Func<FrameworkElement> Make, Action<FrameworkElement, UIElement> SetContent)[] specs =
        [
            ("Button", () => new Button(), (c, v) => ((Button)c).Content = v),
            ("ToggleButton", () => new ToggleButton(), (c, v) => ((ToggleButton)c).Content = v),
            ("RepeatButton", () => new RepeatButton(), (c, v) => ((RepeatButton)c).Content = v),
            ("CheckBox", () => new CheckBox(), (c, v) => ((CheckBox)c).Content = v),
            ("RadioButton", () => new RadioButton(), (c, v) => ((RadioButton)c).Content = v),
            ("Label", () => new Label(), (c, v) => ((Label)c).Content = v),
            ("ContentControl", () => new ContentControl(), (c, v) => ((ContentControl)c).Content = v),
            ("ComboBox", () => new ComboBox(), (c, v) => { var cb = (ComboBox)c; cb.Items.Add(new ComboBoxItem { Content = v }); cb.SelectedIndex = 0; }),
            ("ComboBoxItem", () => new ComboBoxItem(), (c, v) => ((ComboBoxItem)c).Content = v),
            ("ListBoxItem", () => new ListBoxItem(), (c, v) => ((ListBoxItem)c).Content = v),
            ("ListViewItem", () => new ListViewItem(), (c, v) => ((ListViewItem)c).Content = v),
            ("TreeViewItem", () => new TreeViewItem(), (c, v) => ((TreeViewItem)c).Header = v),
            ("MenuItem", () => new MenuItem(), (c, v) => ((MenuItem)c).Header = v),
            ("TabItem", () => new TabItem(), (c, v) => ((TabItem)c).Header = v),
            ("GroupBox", () => new GroupBox(), (c, v) => ((GroupBox)c).Content = v),
            ("Expander", () => new Expander(), (c, v) => { var e = (Expander)c; e.Content = v; e.IsExpanded = true; }),
            ("TextBox", () => new TextBox(), (_, _) => { }),
            ("PasswordBox", () => new PasswordBox(), (_, _) => { }),
            ("StatusBarItem", () => new StatusBarItem(), (c, v) => ((StatusBarItem)c).Content = v),
            ("HeaderedContentControl", () => new HeaderedContentControl(), (c, v) => ((HeaderedContentControl)c).Content = v),
            ("ScrollViewer", () => new ScrollViewer(), (c, v) => ((ScrollViewer)c).Content = v),
            ("Border", () => new Border(), (c, v) => ((Border)c).Child = v),
        ];

        var rows = new List<(string Name, double Chrome, double PaddingBorder, double MinWidth, string Note)>();

        foreach (var (name, make, setContent) in specs)
        {
            try
            {
                // 兩點量測：內容寬 100 與 200 各量一次。
                // 斜率必須是 1（控制項把內容寬度原封不動往外傳），chrome 才有意義。
                // 斜率不是 1 → 這個控制項不以內容決定寬度（例如 TextBox），不給數字，改讀屬性。
                var d1 = MeasureWith(make, setContent, ProbeWidth);
                var d2 = MeasureWith(make, setContent, ProbeWidth * 2);
                var slope = (d2 - d1) / ProbeWidth;

                double chrome;
                var note = "";

                if (Math.Abs(slope - 1.0) < 0.001)
                {
                    chrome = d1 - ProbeWidth;
                }
                else
                {
                    // ⚠ 不要退回「只讀 Padding + BorderThickness」。
                    //   那個數字量不到範本裡的裝飾（ComboBox 的下拉按鈕約 17px），
                    //   於是 chrome 偏小 → 可用寬度偏大 → **漏報溢出**。
                    //   不完整的數字比沒有數字更危險，因為它錯在「看起來裝得下」那一邊。
                    //   記成 NaN，正式工具會把這類控制項裡的文字標成 unmeasurable。
                    chrome = double.NaN;
                    note = $"content width does not propagate (slope={slope:F3}); chrome unknown — sites inside are unmeasurable";
                }

                // 同時記錄「預設 Padding + BorderThickness」。
                // chrome 總值裡已經含了它 —— 使用端如果在 XAML 明寫 Padding，
                // 要用 (chrome - paddingBorder) + 明寫的值，否則會重複計算。
                var probe2 = make();
                probe2.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (probe2 is Control cpb) cpb.ApplyTemplate();
                probe2.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var pad = probe2 is Control cp ? cp.Padding : new Thickness(0);
                var bor = probe2 is Control cb2 ? cb2.BorderThickness : new Thickness(0);
                var paddingBorder = pad.Left + pad.Right + bor.Left + bor.Right;

                var minWidth = probe2 is Control c3 && !double.IsNaN(c3.MinWidth) && !double.IsInfinity(c3.MinWidth)
                    ? c3.MinWidth : 0;

                if (chrome < 0) { note = "negative, clamped to 0 (control clips its content)"; chrome = 0; }

                rows.Add((name, double.IsNaN(chrome) ? double.NaN : Math.Round(chrome, 3), Math.Round(paddingBorder, 3), Math.Round(minWidth, 3), note));
            }
            catch (Exception ex)
            {
                rows.Add((name, double.NaN, 0, 0, $"failed: {ex.GetType().Name}"));
            }
        }

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"schemaVersion\": 1,\n");
        sb.Append($"  \"generatedOn\": \"{DateTime.Now:yyyy-MM-dd}\",\n");
        sb.Append($"  \"theme\": \"{DetectTheme()}\",\n");
        sb.Append($"  \"dotnet\": \"{Environment.Version}\",\n");
        sb.Append("  \"note\": \"Horizontal chrome in DIPs: how much width a control consumes around its content (padding + border + glyph + dropdown). Measured, not hand-copied: a Border of known width is placed in each control and WPF is asked to measure it. Regenerate with prototype/ChromeGen on Windows.\",\n");
        sb.Append("  \"controls\": {\n");

        // 量不到的也要寫進檔案（chromeWidth: null），正式工具據此把那些控制項裡的文字標成 unmeasurable
        var valid = rows;
        for (var i = 0; i < valid.Count; i++)
        {
            var r = valid[i];
            var chromeJson = double.IsNaN(r.Chrome) ? "null" : r.Chrome.ToString(CultureInfo.InvariantCulture);
            sb.Append($"    \"{r.Name}\": {{ \"chromeWidth\": {chromeJson}, \"paddingBorder\": {r.PaddingBorder.ToString(CultureInfo.InvariantCulture)}, \"minWidth\": {r.MinWidth.ToString(CultureInfo.InvariantCulture)}");
            if (r.Note.Length > 0) sb.Append($", \"note\": \"{r.Note}\"");
            sb.Append(i == valid.Count - 1 ? " }\n" : " },\n");
        }

        sb.Append("  }\n}\n");
        File.WriteAllText(outPath, sb.ToString());

        Console.WriteLine($"wrote {outPath}");
        Console.WriteLine($"{"control",-24} {"chrome",8} {"pad+bd",8} {"extra",7}  note");
        foreach (var r in rows)
            Console.WriteLine($"{r.Name,-24} {(double.IsNaN(r.Chrome) ? "n/a" : r.Chrome.ToString("F2")),8} {r.PaddingBorder,8:F2} {(double.IsNaN(r.Chrome) ? "n/a" : (r.Chrome - r.PaddingBorder).ToString("F2")),7}  {r.Note}");

        var failed = rows.Count(r => double.IsNaN(r.Chrome));
        if (failed > 0) Console.Error.WriteLine($"\n{failed} control(s) could not be measured");
        return failed == 0 ? 0 : 1;
    }

    private static double MeasureWith(
        Func<FrameworkElement> make, Action<FrameworkElement, UIElement> setContent, double contentWidth)
    {
        var control = make();
        setContent(control, new Border { Width = contentWidth, Height = ProbeHeight });
        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (control is Control c) c.ApplyTemplate();
        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return control.DesiredSize.Width;
    }

    private static string DetectTheme()
    {
        // Fluent（.NET 9+）啟用時 ThemeMode 不是 None
        try
        {
            var app = Application.Current ?? new Application();
            var mode = app.GetType().GetProperty("ThemeMode")?.GetValue(app)?.ToString();
            if (!string.IsNullOrEmpty(mode) && mode != "None") return $"Fluent/{mode}";
        }
        catch { /* 舊版沒有 ThemeMode */ }
        return "Aero2.NormalColor (default on Windows 8+)";
    }
}
