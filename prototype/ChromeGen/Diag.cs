using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

internal static class Diag
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    public static void Dump()
    {
        // 事實 1：各 ItemsControl 預設的水平捲軸可見性（決定「超出時是捲動還是裁切」）
        foreach (var tag in new[] { "ListBox", "ListView", "ItemsControl", "TreeView", "DataGrid" })
        {
            var root = (FrameworkElement)XamlReader.Parse($"<Grid {Ns}><{tag} Width=\"200\" Height=\"60\"/></Grid>");
            root.Measure(new Size(600, 200)); root.Arrange(new Rect(0, 0, 600, 200)); root.UpdateLayout();
            var c = (FrameworkElement)VisualTreeHelper.GetChild(root, 0);
            var sv = Find<ScrollViewer>(c);
            Console.WriteLine($"{tag,-13} attached H={ScrollViewer.GetHorizontalScrollBarVisibility(c),-8} V={ScrollViewer.GetVerticalScrollBarVisibility(c),-8} " +
                              $"templateScrollViewer={(sv is null ? "無" : $"H={sv.HorizontalScrollBarVisibility} V={sv.VerticalScrollBarVisibility}")}");
        }

        // 事實 2：ListBox 項目內容比清單寬時，是被裁還是出現水平捲軸
        var r2 = (FrameworkElement)XamlReader.Parse(
            $"<Grid {Ns}><ListBox Width=\"200\" Height=\"80\"><ListBoxItem><Border Width=\"500\" Height=\"10\"/></ListBoxItem></ListBox></Grid>");
        r2.Measure(new Size(600, 200)); r2.Arrange(new Rect(0, 0, 600, 200)); r2.UpdateLayout();
        var sv2 = Find<ScrollViewer>((FrameworkElement)VisualTreeHelper.GetChild(r2, 0))!;
        Console.WriteLine($"ListBox 內容 500 寬於 200：ExtentWidth={sv2.ExtentWidth:F0} ViewportWidth={sv2.ViewportWidth:F0} 水平捲軸={sv2.ComputedHorizontalScrollBarVisibility}");
    }

    private static T? Find<T>(DependencyObject o) where T : class
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var ch = VisualTreeHelper.GetChild(o, i);
            if (ch is T t) return t;
            if (Find<T>(ch) is { } f) return f;
        }
        return null;
    }
}
