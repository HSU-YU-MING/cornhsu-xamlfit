using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

internal static class Diag
{
    public static void Dump()
    {
        const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        foreach (var tag in new[] { "ComboBox", "TextBox", "Button" })
        {
            // 用 XamlReader 建樹（GridOracle 證明這條路的版面會真的跑）
            var root = (FrameworkElement)XamlReader.Parse(
                $"<Grid {Ns}><{tag} Width=\"300\" Height=\"40\"/></Grid>");
            root.Measure(new Size(600, 100));
            root.Arrange(new Rect(0, 0, 600, 100));
            root.UpdateLayout();
            var c = (FrameworkElement)VisualTreeHelper.GetChild(root, 0);
            Console.WriteLine($"--- {c.GetType().Name}: ActualWidth={c.ActualWidth}, children={VisualTreeHelper.GetChildrenCount(c)} ---");
            Walk(c, 1);
        }
    }

    private static void Walk(DependencyObject o, int d)
    {
        if (d > 6) return;
        var n = VisualTreeHelper.GetChildrenCount(o);
        for (var i = 0; i < n; i++)
        {
            var ch = VisualTreeHelper.GetChild(o, i);
            if (ch is FrameworkElement fe)
                Console.WriteLine($"{new string(' ', d * 2)}{fe.GetType().Name} name='{fe.Name}' w={fe.ActualWidth:F1}");
            Walk(ch, d + 1);
        }
    }
}
