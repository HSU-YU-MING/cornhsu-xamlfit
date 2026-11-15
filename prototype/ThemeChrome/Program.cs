// 量控制項函式庫的佈景（MahApps.Metro、MaterialDesignThemes）把標準控制項改成多寬的 chrome、
// 用什麼字型字級、文字會不會被轉成大寫 —— 照專案在 App.xaml 合併字典的方式載入佈景，再用跟 ChromeGen 一樣的量法量。
//
// 用法：themechrome <mahapps|md2|md3> <out.json>
//
// ⚠ 拋棄式工具，Windows only。量的是 csproj 裡寫的那個版本；正式工具只讀產出的 JSON。

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

internal static class Program
{
    private const double ProbeWidth = 100.0;
    private const double ProbeHeight = 20.0;
    private const string Sample = "Hamburgefonstiv";

    private static readonly (string Name, Type Type, Func<FrameworkElement> Make, Action<FrameworkElement, object> SetContent)[] Specs =
    [
        ("Button", typeof(Button), () => new Button(), (c, v) => ((Button)c).Content = v),
        ("ToggleButton", typeof(ToggleButton), () => new ToggleButton(), (c, v) => ((ToggleButton)c).Content = v),
        ("RepeatButton", typeof(RepeatButton), () => new RepeatButton(), (c, v) => ((RepeatButton)c).Content = v),
        ("CheckBox", typeof(CheckBox), () => new CheckBox(), (c, v) => ((CheckBox)c).Content = v),
        ("RadioButton", typeof(RadioButton), () => new RadioButton(), (c, v) => ((RadioButton)c).Content = v),
        ("Label", typeof(Label), () => new Label(), (c, v) => ((Label)c).Content = v),
        ("ComboBoxItem", typeof(ComboBoxItem), () => new ComboBoxItem(), (c, v) => ((ComboBoxItem)c).Content = v),
        ("ListBoxItem", typeof(ListBoxItem), () => new ListBoxItem(), (c, v) => ((ListBoxItem)c).Content = v),
        ("ListViewItem", typeof(ListViewItem), () => new ListViewItem(), (c, v) => ((ListViewItem)c).Content = v),
        ("TreeViewItem", typeof(TreeViewItem), () => new TreeViewItem(), (c, v) => ((TreeViewItem)c).Header = v),
        ("MenuItem", typeof(MenuItem), () => new MenuItem(), (c, v) => ((MenuItem)c).Header = v),
        ("TabItem", typeof(TabItem), () => new TabItem(), (c, v) => ((TabItem)c).Header = v),
        ("GroupBox", typeof(GroupBox), () => new GroupBox(), (c, v) => ((GroupBox)c).Content = v),
        ("Expander", typeof(Expander), () => new Expander(), (c, v) => { var e = (Expander)c; e.Content = v; e.IsExpanded = true; }),
        ("StatusBarItem", typeof(StatusBarItem), () => new StatusBarItem(), (c, v) => ((StatusBarItem)c).Content = v),
        // 垂直捲軸關掉時的 chrome（捲軸本身的寬度另外記在 ScrollBar）
        ("ScrollViewer", typeof(ScrollViewer), () => new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, (c, v) => ((ScrollViewer)c).Content = v),
    ];

    // 內容寬度不往外傳的：用「固定寬度 − 內容宿主寬度」量
    private static readonly (string Name, Type Type)[] HostSpecs =
    [
        ("TextBox", typeof(TextBox)), ("PasswordBox", typeof(PasswordBox)), ("ComboBox", typeof(ComboBox)),
        ("ListBox", typeof(ListBox)), ("ListView", typeof(ListView)),
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: themechrome <mahapps|md2|md3> <out.json>"); return 2; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var (themeName, assembly, version, sources) = Load(app, args[0]);

        var controls = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, type, make, set) in Specs)
            controls[name] = Measure(make, set, null);
        foreach (var (name, type) in HostSpecs)
            controls[name] = MeasureHost(type, null);
        controls["ScrollBar"] = ScrollBarWidth(null);

        // 佈景裡有 x:Key 的樣式（專案用 Style="{StaticResource …}" 或 BasedOn 指名的那些）
        var styles = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, style) in KeyedStyles(app.Resources))
        {
            // 樣式的 TargetType 可能是基底型別（MahApps.Styles.Button 是 ButtonBase）：量在第一個適用的具體控制項上，
            // 記下量在哪個控制項 —— 工具只把它套在同一種控制項上
            var spec = Specs.FirstOrDefault(s => s.Type == style.TargetType);
            if (spec.Name is null && style.TargetType != typeof(Control) && style.TargetType != typeof(FrameworkElement))
                spec = Specs.FirstOrDefault(s => style.TargetType.IsAssignableFrom(s.Type));
            if (spec.Name is not null)
            {
                styles[key] = new SortedDictionary<string, object>(Measure(spec.Make, spec.SetContent, style))
                    { ["targetType"] = spec.Name, ["styleTargetType"] = style.TargetType.Name };
                continue;
            }
            if (style.TargetType == typeof(ScrollBar))
            {
                styles[key] = new SortedDictionary<string, object>(ScrollBarWidth(style)) { ["targetType"] = "ScrollBar", ["styleTargetType"] = "ScrollBar" };
                continue;
            }
            var host = HostSpecs.FirstOrDefault(h => h.Type == style.TargetType);
            if (host.Name is not null)
                styles[key] = new SortedDictionary<string, object>(MeasureHost(host.Type, style))
                    { ["targetType"] = host.Name, ["styleTargetType"] = style.TargetType.Name };
        }

        var output = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["theme"] = themeName,
            ["assembly"] = assembly,
            ["version"] = version,
            ["dictionaries"] = sources,
            ["generatedOn"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["note"] = "Measured with WPF by prototype/ThemeChrome: the theme's dictionaries merged into Application.Resources as a project would, " +
                       "then the same probes as ChromeGen. chromeWidth includes the style's Padding and BorderThickness. " +
                       "casing: what the style does to string content (upper = drawn in capitals). Numbers only; nothing from the theme's XAML is copied.",
            ["controls"] = controls,
            ["styles"] = styles,
        };
        File.WriteAllText(args[1], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{themeName} {version}: {controls.Count} controls, {styles.Count} keyed styles -> {args[1]}");
        return 0;
    }

    private static (string Name, string Assembly, string Version, string[] Sources) Load(Application app, string which)
    {
        string[] sources;
        string name, asm;
        switch (which)
        {
            case "mahapps":
                name = "MahApps.Metro"; asm = "MahApps.Metro";
                sources =
                [
                    "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
                    "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
                    "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml",
                ];
                break;
            case "md2":
            case "md3":
                name = which == "md2" ? "MaterialDesignThemes (MaterialDesign2.Defaults)" : "MaterialDesignThemes (MaterialDesign3.Defaults)";
                asm = "MaterialDesignThemes.Wpf";
                app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
                {
                    BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Light,
                    PrimaryColor = MaterialDesignColors.PrimaryColor.DeepPurple,
                    SecondaryColor = MaterialDesignColors.SecondaryColor.Lime,
                });
                sources = [$"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign{(which == "md2" ? 2 : 3)}.Defaults.xaml"];
                break;
            default:
                throw new ArgumentException(which);
        }
        foreach (var s in sources) app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(s, UriKind.Absolute) });
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == asm);
        var version = loaded?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
            ?? loaded?.GetName().Version?.ToString() ?? "?";
        return (name, asm, version, sources);
    }

    private static IEnumerable<(string Key, Style Style)> KeyedStyles(ResourceDictionary dict)
    {
        var seen = new HashSet<string>();
        foreach (var d in Flatten(dict))
            foreach (var key in d.Keys)
                if (key is string k && seen.Add(k) && d[key] is Style s && s.TargetType is not null)
                    yield return (k, s);
    }

    private static IEnumerable<ResourceDictionary> Flatten(ResourceDictionary dict)
    {
        yield return dict;
        foreach (var m in dict.MergedDictionaries)
            foreach (var inner in Flatten(m)) yield return inner;
    }

    private static Dictionary<string, object> Measure(Func<FrameworkElement> make, Action<FrameworkElement, object> set, Style? style)
    {
        var row = new Dictionary<string, object>();
        try
        {
            double Width(object content)
            {
                var c = make();
                if (style is not null) c.Style = style;
                set(c, content);
                c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (c is Control ctl) ctl.ApplyTemplate();
                c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return c.DesiredSize.Width;
            }

            string Mode(DependencyProperty property, Thickness a, Thickness b, double delta)
            {
                double With(Thickness t)
                {
                    var c = make();
                    if (style is not null) c.Style = style;
                    c.SetValue(property, t);
                    set(c, new Border { Width = ProbeWidth, Height = ProbeHeight });
                    c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (c is Control ctl) ctl.ApplyTemplate();
                    c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    return c.DesiredSize.Width;
                }
                var change = With(b) - With(a);
                return Math.Abs(change - delta) < 0.01 ? "replace" : Math.Abs(change) < 0.01 ? "ignore" : $"other ({change:F2})";
            }

            var d1 = Width(new Border { Width = ProbeWidth, Height = ProbeHeight });
            var d2 = Width(new Border { Width = ProbeWidth * 2, Height = ProbeHeight });
            var slope = (d2 - d1) / ProbeWidth;
            if (Math.Abs(slope - 1) < 0.001) row["chromeWidth"] = Math.Round(Math.Max(0, d1 - ProbeWidth), 3);
            else row["chromeWidthUnknown"] = $"content width does not propagate (slope {slope:F3})";

            var probe = make();
            if (style is not null) probe.Style = style;
            set(probe, Sample);
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (probe is Control pc) pc.ApplyTemplate();
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            if (probe is Control control)
            {
                row["padding"] = Math.Round(control.Padding.Left + control.Padding.Right, 3);
                row["border"] = Math.Round(control.BorderThickness.Left + control.BorderThickness.Right, 3);
                // 只記樣式真的設了的（沒設的跟著視窗與系統走，工具照原本的規則算）
                if (FromStyle(control, Control.FontFamilyProperty) && control.FontFamily.Source != SystemFonts.MessageFontFamily.Source) row["fontFamily"] = control.FontFamily.Source;
                if (FromStyle(control, Control.FontSizeProperty)) row["fontSize"] = Math.Round(control.FontSize, 3);
                if (FromStyle(control, Control.FontWeightProperty)) row["fontWeight"] = control.FontWeight.ToOpenTypeWeight();
                if (!double.IsNaN(control.MinWidth) && control.MinWidth > 0) row["minWidth"] = Math.Round(control.MinWidth, 3);

                // 明寫的 Padding／BorderThickness 是一比一取代樣式的值，還是被範本忽略
                if (row.ContainsKey("chromeWidth"))
                {
                    row["paddingMode"] = Mode(Control.PaddingProperty, new Thickness(0), new Thickness(10, 0, 10, 0), 20);
                    row["borderMode"] = Mode(Control.BorderThicknessProperty, new Thickness(0), new Thickness(5, 0, 5, 0), 10);
                }

                // 文字被畫成什麼樣：找視覺樹裡畫字串的 TextBlock，看它的 Text
                if (FindText(probe) is { } shown)
                {
                    row["casing"] = shown == Sample ? "as-is"
                        : shown == Sample.ToUpperInvariant() ? "upper"
                        : shown == Sample.ToLowerInvariant() ? "lower" : $"other: {shown}";
                    if (FindTextBlock(probe) is { } tb && tb.FontFamily.Source != control.FontFamily.Source)
                        row["textFontFamily"] = tb.FontFamily.Source;
                    if (FindTextBlock(probe) is { } tb2 && Math.Abs(tb2.FontSize - control.FontSize) > 0.001)
                        row["textFontSize"] = Math.Round(tb2.FontSize, 3);
                    if (FindTextBlock(probe) is { } tb3 && tb3.FontWeight != control.FontWeight)
                        row["textFontWeight"] = tb3.FontWeight.ToOpenTypeWeight();
                }
                else row["casing"] = "not drawn as text";
            }
        }
        catch (Exception e)
        {
            row["failed"] = e.GetType().Name + ": " + e.Message.Split('\n')[0];
        }
        return row;
    }

    private static Dictionary<string, object> MeasureHost(Type type, Style? style)
    {
        const double Fixed = 300.0;
        var row = new Dictionary<string, object>();
        try
        {
            var control = (Control)Activator.CreateInstance(type)!;
            if (style is not null) control.Style = style;
            control.Width = Fixed;
            control.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (control is ComboBox cb) { cb.Items.Add(new ComboBoxItem { Content = new Border() }); cb.SelectedIndex = 0; }
            if (control is ListBox lb) lb.Items.Add(new ListBoxItem { Content = new Border() });
            var root = new Grid();
            root.Children.Add(control);
            var window = new Window { Content = root, Width = 800, Height = 300, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Top = -10000, ShowActivated = false };
            window.Show();
            window.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var host = FindContentHost(control);
            if (host is not null && host.ActualWidth > 0)
                row["chromeWidth"] = Math.Round(control.ActualWidth - host.ActualWidth, 3);
            else row["chromeWidthUnknown"] = "content host not found";
            row["padding"] = Math.Round(control.Padding.Left + control.Padding.Right, 3);
            row["border"] = Math.Round(control.BorderThickness.Left + control.BorderThickness.Right, 3);
            if (FromStyle(control, Control.FontFamilyProperty) && control.FontFamily.Source != SystemFonts.MessageFontFamily.Source) row["fontFamily"] = control.FontFamily.Source;
            if (FromStyle(control, Control.FontSizeProperty)) row["fontSize"] = Math.Round(control.FontSize, 3);
            if (FromStyle(control, Control.FontWeightProperty)) row["fontWeight"] = control.FontWeight.ToOpenTypeWeight();
            window.Close();
        }
        catch (Exception e)
        {
            row["failed"] = e.GetType().Name + ": " + e.Message.Split('\n')[0];
        }
        return row;
    }

    /// <summary>垂直捲軸的寬度（chromeWidth 欄位放的就是它）。</summary>
    private static Dictionary<string, object> ScrollBarWidth(Style? style)
    {
        var row = new Dictionary<string, object>();
        try
        {
            // 跟 ScrollViewer 裡一樣：靠左、不給寬度，看它排出來多寬（要放進真的視窗，範本才會完整排版）
            var bar = new ScrollBar { Orientation = Orientation.Vertical, Height = 200, HorizontalAlignment = HorizontalAlignment.Left, Maximum = 100, ViewportSize = 10 };
            if (style is not null) bar.Style = style;
            var root = new Grid();
            root.Children.Add(bar);
            var window = new Window { Content = root, Width = 400, Height = 300, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Top = -10000, ShowActivated = false };
            window.Show();
            window.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            row["chromeWidth"] = Math.Round(bar.ActualWidth, 3);
            // 等於系統捲軸寬：跟著顯示縮放變（工具用它自己的上限 17.5）
            if (Math.Abs(bar.ActualWidth - SystemParameters.VerticalScrollBarWidth) < 0.01) row["followsSystem"] = true;
            window.Close();
            row["note"] = "vertical scroll bar width";
        }
        catch (Exception e) { row["failed"] = e.GetType().Name; }
        return row;
    }

    private static bool FromStyle(DependencyObject d, DependencyProperty p) =>
        DependencyPropertyHelper.GetValueSource(d, p).BaseValueSource is BaseValueSource.Style or BaseValueSource.StyleTrigger;

    private static string? FindText(DependencyObject root) => FindTextBlock(root)?.Text;

    private static TextBlock? FindTextBlock(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb && tb.Text.Length > 0 && tb.Text.Replace("_", "").Equals(Sample, StringComparison.OrdinalIgnoreCase)) return tb;
            if (FindTextBlock(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>同 ChromeGen：要分層找 —— 先找真正畫字的那一層。</summary>
    private static FrameworkElement? FindContentHost(DependencyObject root)
    {
        if (Search(root, fe => fe.GetType().Name == "TextBoxView") is { } tbv) return tbv;
        if (Search(root, fe => fe.Name is "PART_ContentHost" or "ContentSite") is { } part) return part;
        if (Search(root, fe => fe.GetType().Name == "ItemsPresenter") is { } items) return items;
        return Search(root, fe => fe is ContentPresenter && fe.ActualWidth > 0);
    }

    private static FrameworkElement? Search(DependencyObject root, Func<FrameworkElement, bool> match)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && match(fe)) return fe;
            if (Search(child, match) is { } found) return found;
        }
        return null;
    }
}
