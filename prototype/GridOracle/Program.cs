// 驗證 GridSolver：拿 WPF 真實的版面當真值。
//
// 單元測試只能證明「程式符合我對 Grid 演算法的理解」。
// 這支工具把同一段 XAML 交給 WPF 自己量，比對我們算出來的欄寬 ——
// 跟 M1 用 FormattedText 驗字型度量是同一套紀律。
//
// ⚠ 拋棄式工具，Windows only。正式工具不相依 WPF。

using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.OpenType;
using XamlFit.Core.Xaml;

internal static class Program
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    // 自家解析器用合成字型（確定性）；WPF 側用同一支字型是不可能的，
    // 所以案例刻意避開「Auto 欄寬度由文字決定」以外的文字影響，
    // 而需要文字的案例改用固定寬度的 Border 當子元素。
    private sealed record Case(string Name, string Body, double Expected);

    [STAThread]
    private static int Main()
    {
        var cases = new List<Case>
        {
            new("single star", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                  <Border x:Name="probe" Grid.Column="0"/>
                </Grid>
                """, 0),

            new("fixed + star", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="120"/>
                    <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <Border x:Name="probe" Grid.Column="1"/>
                </Grid>
                """, 0),

            new("star weights", """
                <Grid Width="600">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="2*"/>
                  </Grid.ColumnDefinitions>
                  <Border x:Name="probe" Grid.Column="1"/>
                </Grid>
                """, 0),

            new("auto sized by a fixed-width child", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <Border Grid.Column="0" Width="137"/>
                  <Border x:Name="probe" Grid.Column="1"/>
                </Grid>
                """, 0),

            new("auto child with margin", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <Border Grid.Column="0" Width="100" Margin="7,0,11,0"/>
                  <Border x:Name="probe" Grid.Column="1"/>
                </Grid>
                """, 0),

            new("auto takes the widest child", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <Border Grid.Column="0" Width="60"/>
                  <Border Grid.Column="0" Width="140"/>
                  <Border x:Name="probe" Grid.Column="1"/>
                </Grid>
                """, 0),

            new("empty auto column is zero", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <Border x:Name="probe" Grid.Column="1"/>
                </Grid>
                """, 0),

            new("MaxWidth clamps a star column", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" MaxWidth="200"/>
                    <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <Border x:Name="probe" Grid.Column="0"/>
                </Grid>
                """, 0),

            new("MinWidth raises an auto column", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" MinWidth="180"/>
                    <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <Border Grid.Column="0" Width="40"/>
                  <Border x:Name="probe" Grid.Column="1"/>
                </Grid>
                """, 0),

            new("column span sums", """
                <Grid Width="600">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="200"/>
                  </Grid.ColumnDefinitions>
                  <Border x:Name="probe" Grid.Column="0" Grid.ColumnSpan="2"/>
                </Grid>
                """, 0),

            new("nested grids", """
                <Grid Width="600">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="200"/>
                  </Grid.ColumnDefinitions>
                  <Grid Grid.Column="0">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="100"/>
                      <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Border x:Name="probe" Grid.Column="1"/>
                  </Grid>
                </Grid>
                """, 0),

            new("border padding inside the column", """
                <Grid Width="400">
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                  <Border Grid.Column="0" Padding="13,0,17,0">
                    <Border x:Name="probe"/>
                  </Border>
                </Grid>
                """, 0),

            new("margin inside the column", """
                <Grid Width="400">
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                  <Border x:Name="probe" Grid.Column="0" Margin="9,0,21,0"/>
                </Grid>
                """, 0),

            // ItemsControl：項目的可用寬度 = 清單寬 − 清單 chrome − 捲軸 − 項目容器 chrome。
            // ⚠ 真值側要明寫 VerticalScrollBarVisibility="Visible"，因為我們的解析器
            //   對預設的 Auto 是「假設捲軸在」—— 兩邊要比的是同一個情境。
            new("list item in a fixed-width ListBox", """
                <ListBox Width="300" Height="60" ScrollViewer.VerticalScrollBarVisibility="Visible"
                         HorizontalContentAlignment="Stretch">
                  <ListBoxItem><Border x:Name="probe"/></ListBoxItem>
                </ListBox>
                """, 0),

            new("list item with the scroll bar disabled", """
                <ListBox Width="300" Height="60" ScrollViewer.VerticalScrollBarVisibility="Disabled"
                         HorizontalContentAlignment="Stretch">
                  <ListBoxItem><Border x:Name="probe"/></ListBoxItem>
                </ListBox>
                """, 0),

            new("auto column fed by a horizontal stackpanel", """
                <Grid Width="500">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <StackPanel Grid.Column="0" Orientation="Horizontal">
                    <Border Width="40"/>
                    <Border Width="55" Margin="5,0,0,0"/>
                  </StackPanel>
                  <Border x:Name="probe" Grid.Column="1"/>
                </Grid>
                """, 0),
        };

        var chrome = ChromeTable.Builtin();
        var fonts = new FontResolver([FontMetricsExtractor.FromBytes(TinyFont()).Single()]);

        var pass = 0;
        var fail = 0;

        Console.WriteLine($"{"case",-44} {"ours",9} {"wpf",9}  {"diff",8}");
        Console.WriteLine(new string('-', 78));

        foreach (var c in cases)
        {
            double wpf;
            try { wpf = MeasureWithWpf(c.Body); }
            catch (Exception ex) { Console.WriteLine($"{c.Name,-44}  WPF 失敗：{ex.GetType().Name}"); fail++; continue; }

            var ours = MeasureWithOurs(c.Body, chrome, fonts);

            if (ours is null)
            {
                Console.WriteLine($"{c.Name,-44} {"n/a",9} {wpf,9:F2}  ← 我們放棄了（保守，但這些案例應該解得開）");
                fail++;
                continue;
            }

            var diff = ours.Value - wpf;
            var ok = Math.Abs(diff) < 0.05;
            if (ok) pass++; else fail++;
            Console.WriteLine($"{c.Name,-44} {ours.Value,9:F2} {wpf,9:F2}  {diff,8:F3}{(ok ? "" : "   ← 不符")}");
        }

        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"吻合 {pass} ／ 不符或放棄 {fail}");
        Console.WriteLine(fail == 0 ? "\nGrid 求解驗收：通過" : "\nGrid 求解驗收：未通過");
        return fail == 0 ? 0 : 1;
    }

    /// <summary>把 XAML 交給 WPF 量，回傳 probe 的實際寬度。</summary>
    private static double MeasureWithWpf(string body)
    {
        var xaml = $"<Grid {Ns}>{body}</Grid>";
        var root = (FrameworkElement)XamlReader.Parse(xaml);

        // ⚠ 不要包在 Window 裡。未顯示的 Window 的版面由 HWND 驅動，
        //   對它呼叫 Measure／Arrange 不會傳遞下去（第一版這樣寫，WPF 側全部回 0）。
        //   直接量根元素就行。
        root.Measure(new Size(1200, 800));
        root.Arrange(new Rect(0, 0, 1200, 800));
        root.UpdateLayout();

        var probe = (FrameworkElement)root.FindName("probe")!;
        return probe.ActualWidth;
    }

    /// <summary>同一段 XAML 交給自家解析器。</summary>
    private static double? MeasureWithOurs(string body, ChromeTable chrome, FontResolver fonts)
    {
        var doc = XDocument.Parse($"<Grid {Ns}>{body}</Grid>", LoadOptions.SetLineInfo);
        var styles = new StyleIndex();
        styles.Add(doc);
        var measurer = new ElementMeasurer(fonts, styles, chrome, "Tiny", 10);
        var probe = doc.Descendants().First(e =>
            e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "probe");

        var w = new WidthResolver(chrome, styles, measurer).Resolve(probe);
        return w.Status == WidthStatus.Known ? w.Px : null;
    }

    /// <summary>最小合成字型。案例裡沒有文字，但 ElementMeasurer 需要一個字型解析器。</summary>
    private static byte[] TinyFont()
    {
        // 直接重用測試用的合成字型產生器太麻煩（在 tests 專案裡），
        // 這裡只需要一支「存在」的字型，案例都不含文字。
        var path = Path.Combine(Path.GetTempPath(), "xamlfit-grid-oracle-font.bin");
        if (File.Exists(path)) return File.ReadAllBytes(path);

        // 用系統字型；案例不含文字，所以具體是哪一支不影響結果
        foreach (var candidate in new[] { "segoeui.ttf", "arial.ttf", "tahoma.ttf" })
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", candidate);
            if (File.Exists(p)) return File.ReadAllBytes(p);
        }
        throw new FileNotFoundException("no system font found for the oracle");
    }
}
