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
        if (args.Contains("--diag")) { Diag.Dump(); return 0; }

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
            // ItemsControl 家族：chrome 是「控制項寬 − ItemsPresenter 寬」，不含捲軸
            //（捲軸另外記在 scrollBarWidth，因為它出不出現是執行期才知道的）
            ("ListBox", () => new ListBox(), (c, v) => ((ListBox)c).Items.Add(new ListBoxItem { Content = v })),
            ("ListView", () => new ListView(), (c, v) => ((ListView)c).Items.Add(new ListViewItem { Content = v })),
            ("ItemsControl", () => new ItemsControl(), (c, v) => ((ItemsControl)c).Items.Add(v)),
        ];

        var rows = new List<(string Name, double Chrome, double PaddingBorder, double MinWidth, string Note)>();

        foreach (var (name, make, setContent) in specs)
        {
            try
            {
                // 兩點量測：內容寬 100 與 200 各量一次。
                // 斜率必須是 1（控制項把內容寬度原封不動往外傳），chrome 才有意義。
                // 斜率不是 1 → 這個控制項不以內容決定寬度（例如 TextBox），不給數字，改讀屬性。
                // ⚠ ItemsControl 家族一定要走內容宿主那條路。兩點量法對它們「成功」，
                //   但量到的是「控制項 chrome ＋ 項目容器 chrome」（ListBox 得到 14 而不是 4），
                //   而解析器會另外再扣一次項目容器 → 重複計算。
                var forceContentHost = name is "ListBox" or "ListView" or "ItemsControl";

                var d1 = MeasureWith(make, setContent, ProbeWidth);
                var d2 = MeasureWith(make, setContent, ProbeWidth * 2);
                var slope = (d2 - d1) / ProbeWidth;

                double chrome;
                var note = "";

                if (!forceContentHost && Math.Abs(slope - 1.0) < 0.001)
                {
                    chrome = d1 - ProbeWidth;
                }
                else if (ContentHostChrome(name) is { } viaTree)
                {
                    // 第二種量法：給控制項固定寬度、排版之後走視覺樹找到**內容宿主**
                    //（ContentPresenter 或 PART_ContentHost），相減就是 chrome。
                    //
                    // 為什麼需要它：ComboBox／TextBox 不把內容寬度往外傳，所以兩點量法失敗。
                    // 而它們是真實專案裡最大的卡點 —— 實測 WinUI-Gallery 有 101 個文字位置
                    // 因為「chrome 未知」而放棄，Playnite 有 27 個。
                    chrome = viaTree;
                    note = "measured via the templated content host (content width does not propagate)";
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
        sb.Append($"  \"scrollBarWidth\": {VerticalScrollBarWidth().ToString(CultureInfo.InvariantCulture)},\n");
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

    /// <summary>
    /// 第二種量法：給控制項一個固定寬度、跑完 Measure/Arrange，然後在視覺樹裡找
    /// 內容宿主（<c>ContentPresenter</c>、<c>PART_ContentHost</c>、<c>TextBoxView</c>），
    /// 用「控制項寬度 − 內容寬度」當 chrome。
    ///
    /// 給 ComboBox／TextBox 這種「內容寬度不往外傳」的控制項用。
    /// </summary>
    private static double? ContentHostChrome(string controlName)
    {
        const double Fixed = 300.0;
        const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

        // ⚠ 一定要用 XamlReader 建樹。對程式碼 new 出來的控制項做 Measure/Arrange，
        //   VisualTreeHelper 看到的子元素數是 0 —— 範本沒有展開，走不到內容宿主。
        //   （第一版就是這樣，三個控制項全部回 null。）
        var inner = controlName switch
        {
            // ComboBox 要有一個「會拉伸」的選取項目，contentPresenter 才會被排到實際可用寬度
            // ⚠ ComboBox 的 ContentPresenter 預設靠左、不拉伸，所以它的 ActualWidth 是
            //   「內容本身的寬」而不是「可用區域的寬」。
            //   HorizontalContentAlignment="Stretch" 讓它撐滿可用區域 ——
            //   那只改變內容在區域裡的對齊，不改變區域大小，所以量出來的就是可用寬度。
            //   （試過的無效做法：空內容 → presenter 排成 0 寬；
            //     塞 10000 寬的內容 → ComboBox 不排它，presenter 仍是 0。）
            "ListBox" or "ListView" or "ItemsControl" =>
                $"<{controlName} {Ns} Width=\"{Fixed}\" Height=\"60\" HorizontalContentAlignment=\"Stretch\"/>",
            "ComboBox" => $"<ComboBox {Ns} Width=\"{Fixed}\" Height=\"40\" SelectedIndex=\"0\" " +
                          "HorizontalContentAlignment=\"Stretch\">" +
                          "<ComboBoxItem><Border/></ComboBoxItem></ComboBox>",
            _ => $"<{controlName} {Ns} Width=\"{Fixed}\" Height=\"40\"/>",
        };

        try
        {
            var root = (FrameworkElement)XamlReader.Parse($"<Grid {Ns}>{inner}</Grid>");
            root.Measure(new Size(Fixed * 2, 100));
            root.Arrange(new Rect(0, 0, Fixed * 2, 100));
            root.UpdateLayout();

            var control = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(root, 0);
            var host = FindContentHost(control);
            if (host is null || host.ActualWidth <= 0) return null;

            var chrome = control.ActualWidth - host.ActualWidth;
            // 合理範圍的防線：負數或大得離譜都代表找錯元素
            return chrome is >= 0 and < 200 ? Math.Round(chrome, 3) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 找內容宿主。⚠ **要分層找，不能一趟深度優先走完。**
    /// TextBox 的樹是 border(300) → PART_ContentHost(298) → … → TextBoxView(294)；
    /// 深度優先會先撞到 PART_ContentHost，算出 chrome=2，但真正畫文字的是 TextBoxView，
    /// 正確答案是 6。
    /// </summary>
    private static FrameworkElement? FindContentHost(DependencyObject root)
    {
        // 第一層偏好：真正畫文字的那一層
        if (Search(root, fe => fe.GetType().Name == "TextBoxView") is { } tbv) return tbv;
        // 第二層：範本宣告的內容宿主
        if (Search(root, fe => fe.Name is "PART_ContentHost" or "ContentSite") is { } part) return part;
        // 第三層：ItemsControl 的項目區（要排在 ContentPresenter 前面 ——
        // 否則會找到項目容器裡面的 ContentPresenter，量到的是項目內容而不是項目區）
        if (Search(root, fe => fe.GetType().Name == "ItemsPresenter") is { } items) return items;
        // 第三層：一般的 ContentPresenter
        return Search(root, fe => fe is ContentPresenter && fe.ActualWidth > 0);
    }

    private static FrameworkElement? Search(DependencyObject root, Func<FrameworkElement, bool> match)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && match(fe)) return fe;
            if (Search(child, match) is { } found) return found;
        }
        return null;
    }

    private static FrameworkElement? FindContentHostUnused(DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

            if (child is FrameworkElement fe)
            {
                // TextBox 的內容宿主是 PART_ContentHost；ComboBox 的選取區是 ContentPresenter
                // TextBoxView 是真正畫文字的那一層，優先；再退到 PART_ContentHost；最後是 ContentPresenter
                if (fe.GetType().Name == "TextBoxView") return fe;
                if (fe.Name is "PART_ContentHost" or "ContentSite") return fe;
                if (fe is ContentPresenter && fe.ActualWidth > 0) return fe;
            }

            var found = FindContentHost(child);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>垂直捲軸的寬度。⚠ 捲軸出不出現取決於執行期的項目數，靜態不可知 ——
    /// 解析器預設「假設它在」（偏保守：少算可用寬度會誤報，多算會漏報）。</summary>
    private static double VerticalScrollBarWidth()
    {
        const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        try
        {
            var root = (FrameworkElement)XamlReader.Parse(
                $"<Grid {Ns}><ListBox Width=\"300\" Height=\"60\" ScrollViewer.VerticalScrollBarVisibility=\"Visible\"/></Grid>");
            root.Measure(new Size(600, 200));
            root.Arrange(new Rect(0, 0, 600, 200));
            root.UpdateLayout();
            var bar = Search(root, fe => fe is System.Windows.Controls.Primitives.ScrollBar && fe.ActualWidth > 0);
            return bar is null ? 0 : Math.Round(bar.ActualWidth, 3);
        }
        catch { return 0; }
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
