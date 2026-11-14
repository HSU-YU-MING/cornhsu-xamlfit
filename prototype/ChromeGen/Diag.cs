using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

internal static class Diag
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    public static void Dump()
    {
        foreach (var inner in new[]
                 {
                     $"<ComboBox {Ns} Width=\"300\" Height=\"40\" SelectedIndex=\"0\" HorizontalContentAlignment=\"Stretch\"><ComboBoxItem><Border/></ComboBoxItem></ComboBox>",
                     $"<ComboBox {Ns} Width=\"300\" Height=\"40\" SelectedIndex=\"0\"><ComboBoxItem><Border Width=\"10000\"/></ComboBoxItem></ComboBox>",
                 })
        {
            var root = (FrameworkElement)XamlReader.Parse($"<Grid {Ns}>{inner}</Grid>");
            root.Measure(new Size(600, 100));
            root.Arrange(new Rect(0, 0, 600, 100));
            root.UpdateLayout();
            var c = (FrameworkElement)VisualTreeHelper.GetChild(root, 0);
            Console.WriteLine($"--- ComboBox editable={inner.Contains("IsEditable")} ActualWidth={c.ActualWidth} ---");
            Walk(c, c, 1);
            Console.WriteLine();
        }
    }

    private static void Walk(Visual rootRef, DependencyObject o, int d)
    {
        if (d > 7) return;
        var n = VisualTreeHelper.GetChildrenCount(o);
        for (var i = 0; i < n; i++)
        {
            var ch = VisualTreeHelper.GetChild(o, i);
            if (ch is FrameworkElement fe)
            {
                var x = "?";
                try
                {
                    if (fe.IsVisible || fe.ActualWidth > 0)
                        x = fe.TransformToAncestor(rootRef).Transform(new Point(0, 0)).X.ToString("F1");
                }
                catch { }
                Console.WriteLine($"{new string(' ', d * 2)}{fe.GetType().Name} name='{fe.Name}' x={x} w={fe.ActualWidth:F1} margin={fe.Margin}");
            }
            Walk(rootRef, ch, d + 1);
        }
    }
}
