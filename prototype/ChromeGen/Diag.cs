using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

internal static class Diag
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    public static void Dump()
    {
        string[] cases =
        [
            $"<ListBox {Ns} Width=\"300\" Height=\"40\" HorizontalContentAlignment=\"Stretch\"><ListBoxItem><Border/></ListBoxItem></ListBox>",
            $"<ListBox {Ns} Width=\"300\" Height=\"40\" ScrollViewer.VerticalScrollBarVisibility=\"Visible\" HorizontalContentAlignment=\"Stretch\"><ListBoxItem><Border/></ListBoxItem></ListBox>",
            $"<ListBox {Ns} Width=\"300\" Height=\"40\" ScrollViewer.VerticalScrollBarVisibility=\"Disabled\" HorizontalContentAlignment=\"Stretch\"><ListBoxItem><Border/></ListBoxItem></ListBox>",
        ];

        foreach (var inner in cases)
        {
            var root = (FrameworkElement)XamlReader.Parse($"<Grid {Ns}>{inner}</Grid>");
            root.Measure(new Size(600, 200));
            root.Arrange(new Rect(0, 0, 600, 200));
            root.UpdateLayout();
            var c = (FrameworkElement)VisualTreeHelper.GetChild(root, 0);
            var mode = inner.Contains("Visible") ? "ScrollBar=Visible" : inner.Contains("Disabled") ? "ScrollBar=Disabled" : "ScrollBar=Auto(default)";
            Console.WriteLine($"--- ListBox {mode} w={c.ActualWidth} ---");
            Walk(c, c, 1);
            Console.WriteLine();
        }
    }

    private static void Walk(Visual rootRef, DependencyObject o, int d)
    {
        if (d > 8) return;
        var n = VisualTreeHelper.GetChildrenCount(o);
        for (var i = 0; i < n; i++)
        {
            var ch = VisualTreeHelper.GetChild(o, i);
            if (ch is FrameworkElement fe && fe.ActualWidth > 0)
                Console.WriteLine($"{new string(' ', d * 2)}{fe.GetType().Name} name='{fe.Name}' w={fe.ActualWidth:F1} margin={fe.Margin} pad={(fe as Control)?.Padding}");
            Walk(rootRef, ch, d + 1);
        }
    }
}
