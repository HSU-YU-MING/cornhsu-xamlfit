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
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
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

        var cases = BuildCases();
        var fonts = LoadFonts();
        var chrome = ChromeTable.Builtin();

        // ⚠ WPF 的預設字型是「系統訊息字型」，跟著 Windows 的顯示語言走：
        //   英文系統是 Segoe UI，繁中是 Microsoft JhengHei UI，日文是 Yu Gothic UI…
        //   對照時 XamlFit 要用同一支，否則兄弟元素的文字寬度會對不上。
        DefaultFamily = SystemFonts.MessageFontFamily.Source;
        Console.WriteLine($"system message font: {DefaultFamily} {SystemFonts.MessageFontSize}");
        Console.WriteLine();

        Console.WriteLine($"{"case",-50} {"WPF",8} {"XamlFit",9}  verdict");
        Console.WriteLine(new string('-', 100));

        int exact = 0, safe = 0, over = 0, wrong = 0;
        var only = args.Length > 1 && args[0] == "--only" ? args[1] : null;

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

            var ours = ResolveWithXamlFit(c.Xaml, chrome, fonts);
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

    private static string Short(string s) => s.Length <= 70 ? s : s[..69] + "…";

    private static AvailableWidth ResolveWithXamlFit(string xaml, ChromeTable chrome, FontResolver fonts)
    {
        var doc = XDocument.Parse(xaml);
        doc.AddAnnotation(new SourceFile("oracle.xaml"));

        var styles = new StyleIndex();
        styles.Add(doc, "oracle.xaml");

        var measurer = new ElementMeasurer(fonts, styles, chrome, DefaultFamily, 12);
        var resolver = new WidthResolver(chrome, styles, measurer);

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
        Add("DataGrid column header 150", W($"<DataGrid Width='500' HorizontalAlignment='Left' AutoGenerateColumns='False'><DataGrid.Columns><DataGridTextColumn Width='150'><DataGridTextColumn.Header>{text}</DataGridTextColumn.Header></DataGridTextColumn></DataGrid.Columns></DataGrid>"));
        Add("GridViewColumn header 150", W($"<ListView Width='500' HorizontalAlignment='Left'><ListView.View><GridView><GridViewColumn Width='150'><GridViewColumn.Header>{text}</GridViewColumn.Header></GridViewColumn></GridView></ListView.View></ListView>"));

        // ── 8. 快顯 ──
        Add("text in Button.ToolTip", W($"<Button Width='80' HorizontalAlignment='Left' Content='x'><Button.ToolTip><TextBlock x:Name='probe' Text='{Long}'/></Button.ToolTip></Button>"), Want.Open);

        return cases;
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
