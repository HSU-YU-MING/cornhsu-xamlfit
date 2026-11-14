// CorpusRender —— 把語料裡**真實的**視窗 XAML，連同那個 App 自己的佈景字典與某個語言的譯文，
// 交給真的 WPF 視窗畫出來，量指定那幾行的文字實際需要多寬、實際看得見多寬。
//
// 用途：確認 XamlFit 在公開專案上報出的溢出是真的，而不是工具算錯。
//
// 語料只有 XAML、沒有那些 App 的組件，所以載入時一定會遇到不認得的型別、事件處理器、
// 指到 App 程式碼的 x:Static……這支程式用「載入 → 讀例外的行列 → 修掉那一處 → 再載入」的迴圈處理，
// 而且**每一次修補都記下來**。修補碰到要量的那一排時，結果標成「近似」，不能當成確認。
//
// 拋棄式工具，Windows only。
//   dotnet run -c Release -- <spec.json>

using System.Globalization;
using System.IO;
using System.Windows.Controls.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

internal static partial class Program
{
    private const string Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <param name="Source">來源語言的字串字典（每個語言都疊在它上面：沒翻的鍵顯示來源語言）。</param>
    /// <param name="Cultures">語言 → 字串字典。逐一換上，視窗只載入一次。</param>
    private sealed record Spec(
        string Root, string App, string[]? Source, Dictionary<string, string> Cultures, string Window, int[] Lines,
        string? RootReplacement, Dictionary<string, string>? Replace, string? Output);

    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var spec = JsonSerializer.Deserialize<Spec>(File.ReadAllText(args[0]),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var log = new List<string>();

        // ── 1. 應用程式的資源：照 App.xaml 的合併順序載入每一個字典 ──
        var appDoc = XDocument.Load(Path.Combine(spec.Root, spec.App));
        var merged = appDoc.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute("Source") is not null)
            .Select(e => e.Attribute("Source")!.Value).ToList();

        var loaded = 0;
        foreach (var source in merged)
        {
            var path = ResolveSource(spec.Root, Path.GetDirectoryName(Path.Combine(spec.Root, spec.App))!, source);
            if (path is null) { log.Add($"dictionary not found: {source}"); continue; }
            if (IsStringFile(spec, path)) continue;

            var dict = LoadRepaired<ResourceDictionary>(Retarget(File.ReadAllText(path), spec), Path.GetFileName(path), log, null);
            if (dict is not null && AddSafely(app.Resources, dict, Path.GetFileName(path), log)) loaded++;
        }

        // App.xaml 自己寫在 Application.Resources 裡的資源（不是合併進來的）。
        // ⚠ ScreenToGif 的 FontFamilyLight 就寫在這裡；漏掉它，標題會掉回這台機器的預設字型。
        {
            var resources = appDoc.Root!.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal));
            var inner = resources?.Elements().FirstOrDefault(e => e.Name.LocalName == "ResourceDictionary") ?? resources;
            if (inner is not null)
            {
                var dict = new XElement(XName.Get("ResourceDictionary", Wpf));
                foreach (var ns in appDoc.Root!.Attributes().Where(at => at.IsNamespaceDeclaration)) dict.SetAttributeValue(ns.Name, ns.Value);
                foreach (var child in inner.Elements().Where(c => !c.Name.LocalName.Contains('.', StringComparison.Ordinal)))
                    dict.Add(new XElement(child));
                var inline = LoadRepaired<ResourceDictionary>(Retarget(dict.ToString(), spec), "App.xaml (inline resources)", log, null);
                if (inline is not null) AddSafely(app.Resources, inline, "App.xaml (inline resources)", log);
            }
        }

        // 來源語言的字串
        foreach (var s in spec.Source ?? [])
        {
            var dict = LoadRepaired<ResourceDictionary>(File.ReadAllText(Path.Combine(spec.Root, s)), Path.GetFileName(s), log, null);
            if (dict is not null) AddSafely(app.Resources, dict, Path.GetFileName(s), log);
        }
        var dictionaryRepairs = log.Count;

        // ── 2. 視窗：在要量的那幾行的元素上掛名字，再載入 ──
        var windowPath = Path.Combine(spec.Root, spec.Window);
        var doc = XDocument.Load(windowPath, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        var probes = new Dictionary<int, string>();
        foreach (var line in spec.Lines)
        {
            var el = doc.Descendants().FirstOrDefault(e => ((IXmlLineInfo)e).LineNumber == line)
                     ?? throw new InvalidOperationException($"no element starts on line {line}");
            var name = (string?)el.Attribute(X + "Name") ?? (string?)el.Attribute("Name");
            if (name is null) { name = $"__probe{line}"; el.SetAttributeValue(X + "Name", name); }
            probes[line] = name;
        }

        // 根元素是 App 自己的視窗子類別：換成 Window（它的 x:Class 也拿掉）
        var root = doc.Root!;
        root.Attribute(X + "Class")?.Remove();
        if (root.Name.NamespaceName != Wpf)
        {
            var to = spec.Replace?.GetValueOrDefault(root.Name.LocalName) ?? spec.RootReplacement ?? "Window";
            log.Add($"root <{root.Name.LocalName}> replaced by <{to}>");
            Rename(root, XName.Get(to, Wpf));
        }
        // 視窗自己 Resources 裡指到原本型別的樣式
        var retargeted = XDocument.Parse(Retarget(doc.ToString(SaveOptions.DisableFormatting), spec), LoadOptions.SetLineInfo);
        foreach (var (line, name) in probes) { }
        doc = retargeted;

        foreach (var (from, to) in spec.Replace ?? [])
            foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == from).ToList())
            {
                log.Add($"<{from}> replaced by <{to}> (configured)");
                Rename(e, XName.Get(to, Wpf));
            }

        var protectedNames = probes.Values.ToHashSet();
        var loadedRoot = LoadRepaired<FrameworkElement>(doc.ToString(SaveOptions.DisableFormatting), Path.GetFileName(windowPath), log, protectedNames);
        if (loadedRoot is null) { Console.WriteLine("window could not be loaded"); Print(log); return 2; }

        // UserControl／Page 這種不是視窗的根元素：放進一個沒有邊框的視窗。寬度用它自己的 Width；
        // 沒寫的話給一個很寬的視窗 —— XamlFit 在這種檔案裡量得到的位置，上界都在控制項裡面。
        Window window;
        if (loadedRoot is Window w) window = w;
        else
        {
            var width = double.IsNaN(loadedRoot.Width) ? 2000 : loadedRoot.Width;
            window = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Width = width, Height = 2000, Content = loadedRoot };
            log.Add($"<{loadedRoot.GetType().Name}> hosted in a {width}-wide frameless window");
        }
        // XF_LOG=1：只印放棄／沒合併的；XF_LOG=<字串>：印所有含這個字串的修補紀錄
        if (Environment.GetEnvironmentVariable("XF_LOG") is { } logFilter)
            foreach (var l in log.Where(l => logFilter == "1" ? l.Contains("gave up") || l.Contains("not merged") || l.Contains("not found") : l.Contains(logFilter))) Console.WriteLine("    ! " + l);

        window.Left = -20000;
        window.Top = -20000;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();

        Console.WriteLine($"{spec.Window}: window {window.ActualWidth:F1} wide; {loaded}/{merged.Count} dictionaries loaded ({dictionaryRepairs} repairs in dictionaries)");
        var windowRepairs = log.Skip(dictionaryRepairs).Where(l => l.StartsWith(Path.GetFileName(windowPath), StringComparison.Ordinal)).ToList();
        Console.WriteLine($"  repairs inside the window ({windowRepairs.Count}):");
        foreach (var r in windowRepairs) Console.WriteLine($"    - {r}");

        var rows = new List<string> { "culture,line,element,text,needed,visible,clipped" };
        var current = (ResourceDictionary?)null;

        foreach (var (culture, file) in spec.Cultures.OrderBy(c => c.Key))
        {
            if (current is not null) app.Resources.MergedDictionaries.Remove(current);
            current = null;
            if (file.Length > 0)
            {
                var cultureLog = new List<string>();
                current = LoadRepaired<ResourceDictionary>(File.ReadAllText(Path.Combine(spec.Root, file)), Path.GetFileName(file), cultureLog, null);
                if (current is not null && !AddSafely(app.Resources, current, file, cultureLog)) current = null;
            }
            window.UpdateLayout();

            foreach (var (line, name) in probes.OrderBy(p => p.Key))
            {
                // DataGrid 的欄不是畫面元素：找它的 DataGrid，量 DataGrid 為它產生的欄標題
                if (loadedRoot.FindName(name) is DataGridColumn column)
                {
                    var grid = LogicalDescendants(loadedRoot).OfType<DataGrid>().FirstOrDefault(g => g.Columns.Contains(column));
                    if (grid is null) { rows.Add($"{culture},{line},DataGridColumn,no-grid,,,"); continue; }
                    Reveal(grid);
                    window.UpdateLayout();
                    var header = Descendants(grid).OfType<DataGridColumnHeader>().FirstOrDefault(h => h.Column == column);
                    var headerText = header is null ? null : FindText(header);
                    if (headerText is null) { rows.Add($"{culture},{line},DataGridColumnHeader,no-text,,,"); continue; }
                    var neededH = NeededWidth(headerText);
                    var visibleH = VisibleWidth(headerText, window);
                    rows.Add($"{culture},{line},DataGridColumnHeader,\"{TextOf(headerText).Replace("\"", "'")}\",{neededH:F2},{visibleH:F2},{(neededH > visibleH + 0.5 ? 1 : 0)}");
                    continue;
                }
                if (loadedRoot.FindName(name) is not FrameworkElement el) { rows.Add($"{culture},{line},,removed,,,"); continue; }
                Reveal(el);
                // 關起來的 ComboBox 顯示的是選中的那一項 —— XamlFit 量的就是那個框。選它，量框裡的文字。
                // 直接放在 ComboBox 裡的元素（不是 ComboBoxItem）：選它，看關起來的框裡實際顯示什麼
                // 選項可以是一整塊（StackPanel 裡有圖示與文字）：往上找到「直接放在 ComboBox 裡」的那一層才是選項
                var itemRoot = (DependencyObject)el;
                while (itemRoot is FrameworkElement && LogicalTreeHelper.GetParent(itemRoot) is { } up && up is not ComboBox && up is not Window)
                    itemRoot = up;
                if (el is not ComboBoxItem && itemRoot is FrameworkElement itemElement && itemRoot is not ComboBoxItem
                    && LogicalTreeHelper.GetParent(itemRoot) is ComboBox ownerCombo)
                {
                    Reveal(ownerCombo);
                    // ⚠ 框裡畫的是選項的 VisualBrush，大小照選項自己的版面。下拉清單從沒打開過的選項沒排過版，
                    //   寬度是 0 —— 第一版因此把英文的「Top」量成看得見 0 px。先打開一次讓每個選項排版。
                    ownerCombo.IsDropDownOpen = true;
                    window.UpdateLayout();
                    // 文字在選項裡的位置（選項打開時才有版面）
                    var offsetInItem = ReferenceEquals(el, itemElement) || !el.IsDescendantOf(itemElement) ? 0
                        : el.TransformToAncestor(itemElement).Transform(new Point()).X;
                    ownerCombo.SelectedItem = itemElement;
                    ownerCombo.IsDropDownOpen = false;
                    window.UpdateLayout();
                    var site = ownerCombo.Template.FindName("contentPresenter", ownerCombo) as FrameworkElement;
                    var shownItem = ownerCombo.SelectionBoxItem;
                    if (Environment.GetEnvironmentVariable("XF_DEBUG") is { } dbgC && (culture == dbgC || culture == "(source)" && dbgC == "1"))
                        Console.WriteLine($"  line {line}: SelectionBoxItem {shownItem?.GetType().Name}; box {site?.ActualWidth:F2}; item natural {(el as FrameworkElement)?.DesiredSize.Width:F2}; " +
                                          string.Join(" ", site is null ? [] : Descendants(site).Select(d => d is FrameworkElement fe ? $"{d.GetType().Name}:{fe.ActualWidth:F2}@{fe.TransformToAncestor(ownerCombo).Transform(new Point()).X:F2} clip={(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(fe)?.Bounds.Width.ToString("F2") ?? "-")}" : d.GetType().Name).Take(6)) + $" presenter@{site?.TransformToAncestor(ownerCombo).Transform(new Point()).X:F2} clip={(site is null ? "-" : System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(site)?.Bounds.Width.ToString("F2") ?? "-")} combo {ownerCombo.ActualWidth:F2} padding {ownerCombo.Padding} margin {(site as FrameworkElement)?.Margin} hca {ownerCombo.HorizontalContentAlignment} rounding {ownerCombo.UseLayoutRounding} src {DependencyPropertyHelper.GetValueSource(ownerCombo, Control.PaddingProperty).BaseValueSource} found {(ownerCombo.TryFindResource(typeof(ComboBox)) is Style fs ? $"style with {fs.Setters.Count} setters, sealed {fs.IsSealed}" : "none")} style {(ownerCombo.Style is null ? "null" : "set")}");
                    var boxText = site is null ? null : FirstText(site);
                    var boxShape = site is null ? null : Descendants(site).OfType<System.Windows.Shapes.Rectangle>().FirstOrDefault();
                    if (boxText is not null)
                    {
                        var n = NeededWidth(boxText); var v = VisibleWidth(boxText, window);
                        rows.Add($"{culture},{line},ComboBox(selected),\"{TextOf(boxText).Replace("\"", "'")}\",{n:F2},{v:F2},{(n > v + 0.5 ? 1 : 0)}");
                    }
                    else if (boxShape is not null && boxShape.ActualWidth <= 0)
                        rows.Add($"{culture},{line},ComboBox(selected visual),not-laid-out,,,");   // 下拉清單打不開（ComboBox 本身沒顯示）：沒有量到東西
                    else if (boxShape is not null && FindText(el) is { } itemText2)
                    {
                        // WPF 把畫面元素當成選項時，框裡畫的是那個元素的 VisualBrush（大小照元素本身）
                        var n = NeededWidth(itemText2);
                        var v = Math.Clamp(VisibleWidth(boxShape, window) - offsetInItem, 0, Math.Max(n, 0));
                        rows.Add($"{culture},{line},ComboBox(selected visual),\"{TextOf(itemText2).Replace("\"", "'")}\",{n:F2},{v:F2},{(n > v + 0.5 ? 1 : 0)}");
                    }
                    else rows.Add($"{culture},{line},ComboBox(selected),no-text,,,");
                    continue;
                }
                if (el is ComboBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is ComboBox combo)
                {
                    combo.SelectedItem = item;
                    window.UpdateLayout();
                    // 可編輯的 ComboBox：選中的文字在 TextBox 裡。用字元位置量：最後一個字的右緣有沒有超出捲動視埠
                    if (combo.IsEditable && combo.Template.FindName("PART_EditableTextBox", combo) is TextBox box && box.Text.Length > 0)
                    {
                        box.UpdateLayout();
                        var first = box.GetRectFromCharacterIndex(0);
                        var last = box.GetRectFromCharacterIndex(box.Text.Length - 1, true);
                        var host = box.Template.FindName("PART_ContentHost", box) as ScrollViewer;
                        if (host is null || first.IsEmpty || last.IsEmpty) { rows.Add($"{culture},{line},ComboBox(editable),no-text,,,"); continue; }
                        // ⚠ 字元位置與視埠右緣直接相減會固定差 2 px（TextBox 內部的邊距）——
                        //   有空間時也報成被裁。看捲動：內容比視埠寬多少，就有多少看不到；外層再裁的另外扣。
                        var neededE = last.Right - first.Left;
                        var hidden = Math.Max(0, host.ExtentWidth - host.ViewportWidth);
                        var hiddenByParents = Math.Max(0, host.ActualWidth - VisibleWidth(host, window));
                        var visibleE = Math.Max(0, neededE - hidden - hiddenByParents);
                        rows.Add($"{culture},{line},ComboBox(editable),\"{box.Text.Replace("\"", "'")}\",{neededE:F2},{visibleE:F2},{(neededE > visibleE + 0.5 ? 1 : 0)}");
                        continue;
                    }
                    if (item.Content is string itemText)
                    {
                        var shown = FindMatching(combo, t => TextOf(t) == itemText || TextOf(t) == itemText.Replace("_", ""));
                        if (shown is null) { rows.Add($"{culture},{line},ComboBoxItem,no-text,,,"); continue; }
                        var neededC = NeededWidth(shown);
                        var visibleC = VisibleWidth(shown, window);
                        rows.Add($"{culture},{line},ComboBoxItem,\"{itemText.Replace("\"", "'")}\",{neededC:F2},{visibleC:F2},{(neededC > visibleC + 0.5 ? 1 : 0)}");
                        continue;
                    }
                }
                window.UpdateLayout();
                if (!el.IsVisible && Environment.GetEnvironmentVariable("XF_WHY") is not null && culture == "(source)")
                {
                    var chain = new List<string>();
                    for (DependencyObject? c = el; c is not null; c = LogicalTreeHelper.GetParent(c))
                        chain.Add(c is UIElement u ? $"{c.GetType().Name}[{u.Visibility},{u.IsVisible}]" : c.GetType().Name);
                    Console.WriteLine($"  line {line}: " + string.Join(" < ", chain.Take(12)) + $"  window shown={window.IsVisible}");
                }
                if (!el.IsVisible) { rows.Add($"{culture},{line},{el.GetType().Name},not-shown,,,"); continue; }
                var text = FindText(el);
                if (text is null) { rows.Add($"{culture},{line},{el.GetType().Name},no-text,,,"); continue; }

                var needed = NeededWidth(text);
                var visible = VisibleWidth(text, window);
                var clipped = needed > visible + 0.5;
                if (Environment.GetEnvironmentVariable("XF_DEBUG") is { } dbg && culture == dbg)
                    Console.WriteLine($"  [{culture}] line {line}: <{el.GetType().Name}> outer {el.ActualWidth + el.Margin.Left + el.Margin.Right:F2} (actual {el.ActualWidth:F2}, margin {el.Margin}, min {el.MinWidth}) text {TextOf(text)} font {TextElement.GetFontFamily(text)} {TextElement.GetFontSize(text)}  needed {needed:F2} visible {visible:F2}; parent {(VisualTreeHelper.GetParent(el) as FrameworkElement)?.ActualWidth:F2}");
                rows.Add($"{culture},{line},{el.GetType().Name},\"{TextOf(text).Replace("\"", "'")}\",{needed:F2},{visible:F2},{(clipped ? 1 : 0)}");
            }
        }

        if (spec.Output is not null) File.WriteAllLines(spec.Output, rows);
        Console.WriteLine($"  {rows.Count - 1} measurements, {rows.Count(r => r.EndsWith(",1", StringComparison.Ordinal))} clipped");
        window.Close();
        app.Shutdown();
        return 0;
    }

    /// <summary>
    /// 合併字典時 WPF 會把裡面的樣式封存，TargetType 被修補拿掉的樣式（指到 App 自己的型別）在這裡才失敗。
    /// 把封存不了的樣式一個一個拿掉，其餘的照樣合併。
    /// </summary>
    private static bool AddSafely(ResourceDictionary target, ResourceDictionary dict, string what, List<string> log)
    {
        void Clean(ResourceDictionary d)
        {
            foreach (var key in d.Keys.Cast<object>().ToList())
            {
                object? value;
                try { value = d[key]; }
                catch (Exception ex) { d.Remove(key); log.Add($"{what}: removed resource {key} ({First(ex.Message)})"); continue; }
                if (value is Style st)
                {
                    try { st.Seal(); }
                    catch (Exception ex) { d.Remove(key); log.Add($"{what}: removed style {key} ({First(ex.Message)})"); }
                }
            }
            foreach (var m in d.MergedDictionaries) Clean(m);
        }

        try { target.MergedDictionaries.Add(dict); return true; }
        catch (Exception)
        {
            target.MergedDictionaries.Remove(dict);
            Clean(dict);
            try { target.MergedDictionaries.Add(dict); return true; }
            catch (Exception ex) { target.MergedDictionaries.Remove(dict); log.Add($"{what}: not merged ({First(ex.Message)})"); return false; }
        }
    }

    /// <summary>TargetType="WindowBase"、{x:Type controls:WindowBase} → 替換後的 WPF 型別。</summary>
    private static string Retarget(string xaml, Spec spec)
    {
        foreach (var (from, to) in spec.Replace ?? [])
        {
            xaml = Regex.Replace(xaml, $"TargetType=\"(\\w+:)?{from}\"", $"TargetType=\"{to}\"");
            xaml = Regex.Replace(xaml, $"\\{{x:Type\\s+(\\w+:)?{from}\\}}", $"{{x:Type {to}}}");
            // 範本裡直接用到的元素也要換（ScreenToGif 的可編輯 ComboBox 範本裡有 <c:ExtendedTextBox>；
            // 第一版只換 TargetType，整份 ComboBox.xaml 載入失敗，畫出來的是 WPF 預設的 ComboBox）
            xaml = Regex.Replace(xaml, $"<(\\w+:)?{from}(?=[\\s/>])", $"<{to}");
            xaml = Regex.Replace(xaml, $"</(\\w+:)?{from}>", $"</{to}>");
        }
        return xaml;
    }

    /// <summary>文字在沒選中的分頁、沒展開的 Expander 裡時，先把它們打開 —— 不然根本沒畫出來。</summary>
    private static void Reveal(FrameworkElement el)
    {
        for (DependencyObject? cur = el; cur is not null; cur = LogicalTreeHelper.GetParent(cur))
        {
            if (cur is TabItem ti) ti.IsSelected = true;
            if (cur is Expander ex) ex.IsExpanded = true;
            // 程式碼才會打開的面板（ScreenToGif 的設定頁、進度面板）：照 App 用到它時的樣子打開
            if (cur is UIElement ui && ui.Visibility != Visibility.Visible) ui.Visibility = Visibility.Visible;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>邏輯樹：沒選中的分頁還沒有視覺樹，但邏輯樹裡找得到。</summary>
    private static IEnumerable<object> LogicalDescendants(object root)
    {
        if (root is not DependencyObject d) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(d))
        {
            yield return child;
            foreach (var x in LogicalDescendants(child)) yield return x;
        }
    }

    private static void Print(List<string> log)
    {
        if (log.Count == 0) return;
        Console.WriteLine($"  repairs ({log.Count}):");
        foreach (var l in log.Distinct().Take(40)) Console.WriteLine($"    - {l}");
    }

    private static string Shorten(string s) => s.Length <= 40 ? s : s[..39] + "…";

    private static bool IsStringFile(Spec spec, string path)
    {
        var full = Path.GetFullPath(path);
        return (spec.Source ?? []).Concat(spec.Cultures.Values).Where(v => v.Length > 0)
            .Any(s => Path.GetFullPath(Path.Combine(spec.Root, s)) == full);
    }

    private static void Rename(XElement e, XName name)
    {
        var oldLocal = e.Name.LocalName;
        e.Name = name;
        // 屬性元素 <old.Prop> 也要跟著改
        foreach (var child in e.Elements().Where(c => c.Name.LocalName.StartsWith(oldLocal + ".", StringComparison.Ordinal)).ToList())
            child.Name = XName.Get(name.LocalName + child.Name.LocalName[oldLocal.Length..], name.NamespaceName);
    }

    /// <summary>pack URI 或相對路徑 → 語料裡的檔案。</summary>
    private static string? ResolveSource(string root, string baseDir, string source)
    {
        var marker = source.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        var rel = marker >= 0 ? source[(marker + ";component/".Length)..] : source;
        rel = rel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        var direct = Path.Combine(source.StartsWith('/') && marker < 0 ? baseDir : baseDir, rel);
        if (File.Exists(direct)) return direct;

        var suffix = Path.DirectorySeparatorChar + rel;
        return Directory.EnumerateFiles(root, Path.GetFileName(rel), SearchOption.AllDirectories)
            .FirstOrDefault(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    // ───────────────────────── 修補迴圈 ─────────────────────────

    /// <summary>
    /// ⚠ 執行期的 XamlReader 會把單獨的 &amp;#x0d; 刪掉，**編譯過的 XAML（BAML）不會**（實測：0D 留在字串裡，
    ///   TextBlock 在那裡換行）。語料裡的 App 是編譯過的，所以載入前把單獨的 CR 換成 LF —— 效果相同，而且 XamlReader 會保留。
    /// </summary>
    private static string EmulateBaml(string xaml) =>
        Regex.Replace(xaml, "&#(x0*[dD]|0*13);(?!&#(x0*[aA]|0*10);)", "&#x0a;");

    private static T? LoadRepaired<T>(string xaml, string what, List<string> log, HashSet<string>? protectedNames) where T : class
    {
        xaml = EmulateBaml(xaml);
        for (var attempt = 0; attempt < 400; attempt++)
        {
            try
            {
                var context = new ParserContext { XmlnsDictionary = { { "", Wpf } } };
                return XamlReader.Parse(xaml) as T;
            }
            catch (XamlParseException ex)
            {
                var doc = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
                var (fixedXaml, note) = Repair(doc, ex, protectedNames);
                if (fixedXaml is null) { log.Add($"{what}: gave up — {First(ex.Message)}"); return null; }
                log.Add($"{what}: {note}");
                xaml = fixedXaml;
            }
            catch (Exception ex)
            {
                log.Add($"{what}: gave up — {ex.GetType().Name}: {First(ex.Message)}");
                return null;
            }
        }
        log.Add($"{what}: too many repairs");
        return null;
    }

    private static string First(string s) => s.Split('\n')[0].Trim();

    /// <summary>例外指到的那一處：是屬性就拿掉屬性，是元素就拿掉元素。</summary>
    private static (string? Xaml, string Note) Repair(XDocument doc, XamlParseException ex, HashSet<string>? protectedNames)
    {
        var line = ex.LineNumber;
        var pos = ex.LinePosition;
        var message = First(ex.Message);

        // 例外的位置是「標記延伸或屬性值的起點」或「元素的起點」。找最接近、而且不在它之後的那個節點。
        XObject? best = null;
        var bestPos = -1;
        foreach (var el in doc.Descendants())
        {
            var li = (IXmlLineInfo)el;
            if (li.LineNumber == line && li.LinePosition <= pos && li.LinePosition > bestPos) { best = el; bestPos = li.LinePosition; }
            foreach (var a in el.Attributes())
            {
                var ai = (IXmlLineInfo)a;
                if (ai.LineNumber == line && ai.LinePosition <= pos && ai.LinePosition > bestPos) { best = a; bestPos = ai.LinePosition; }
            }
        }

        // 行號是 0（沒有位置的例外，例如找不到資源）：從訊息裡抓鍵名，拿掉引用它的屬性
        if (best is null && KeyInMessage().Match(message) is { Success: true } km)
        {
            var key = km.Groups[1].Value;
            var attr = doc.Descendants().SelectMany(e => e.Attributes())
                .FirstOrDefault(a => a.Value.Contains("Resource " + key + "}", StringComparison.Ordinal)
                                     || a.Value.Contains("Resource " + key + ",", StringComparison.Ordinal));
            if (attr is not null) best = attr;
        }

        switch (best)
        {
            case XAttribute a when a.Parent is { } owner:
            {
                if (a.Name == X + "Name" && protectedNames?.Contains(a.Value) == true)
                    return (null, "");
                var desc = $"removed {owner.Name.LocalName}.{a.Name.LocalName}=\"{Shorten(a.Value)}\" ({Shorten(message)})";
                // Setter 的 Value 拿不到就整個 Setter 拿掉
                if (owner.Name.LocalName is "Setter" or "Trigger" or "DataTrigger" or "Condition" && a.Name.LocalName is "Value" or "Property" or "Binding")
                {
                    var target = owner.Name.LocalName == "Condition" ? owner.Parent?.Parent ?? owner : owner;
                    target.Remove();
                    return (doc.ToString(SaveOptions.DisableFormatting), $"removed <{target.Name.LocalName}> ({Shorten(message)})");
                }
                a.Remove();
                return (doc.ToString(SaveOptions.DisableFormatting), desc);
            }
            case XElement e when e.Parent is not null:
            {
                if (protectedNames is not null && e.DescendantsAndSelf().Any(d => protectedNames.Contains((string?)d.Attribute(X + "Name") ?? "")))
                {
                    // 要量的元素在裡面：不能整個拿掉。換成同類的 WPF 型別，或拿掉它的屬性元素
                    if (!e.Name.LocalName.Contains('.', StringComparison.Ordinal) && e.Name.NamespaceName != Wpf)
                    {
                        var to = Substitute(e.Name.LocalName);
                        var from = e.Name.LocalName;
                        Rename(e, XName.Get(to, Wpf));
                        foreach (var attr in e.Attributes().Where(x => !x.IsNamespaceDeclaration && x.Name.Namespace != XNamespace.None && x.Name.Namespace != X).ToList()) attr.Remove();
                        return (doc.ToString(SaveOptions.DisableFormatting), $"<{from}> replaced by <{to}> (contains a probe; {Shorten(message)})");
                    }
                    return (null, "");
                }
                var name = e.Name.LocalName + (e.Attribute("Property") is { } p ? $" Property={p.Value}" : "");
                e.Remove();
                return (doc.ToString(SaveOptions.DisableFormatting), $"removed <{name}> ({Shorten(message)})");
            }
        }
        return (null, "");
    }

    private static string Substitute(string local)
    {
        foreach (var t in new[] { "Window", "Button", "CheckBox", "RadioButton", "TextBlock", "TextBox", "ComboBox", "ListBox", "StackPanel", "DockPanel", "Grid", "Border", "Label", "Expander", "GroupBox" })
            if (local.EndsWith(t, StringComparison.Ordinal)) return t;
        return "ContentControl";
    }

    [GeneratedRegex(@"'([^']+)'")]
    private static partial Regex KeyInMessage();

    // ───────────────────────── 量測 ─────────────────────────

    private static FrameworkElement? FindText(DependencyObject el)
    {
        var wanted = el switch
        {
            HeaderedContentControl h when h.Header is string hs => hs,
            HeaderedItemsControl hi when hi.Header is string his => his,
            ContentControl c when c.Content is string cs => cs,
            _ => null,
        };
        if (wanted is null) return FirstText(el);
        var plain = wanted.Replace("_", "");
        return FindMatching(el, t => TextOf(t) == wanted || TextOf(t) == plain) ?? FirstText(el);
    }

    private static FrameworkElement? FirstText(DependencyObject el) => FindMatching(el, _ => true);

    private static FrameworkElement? FindMatching(DependencyObject el, Func<FrameworkElement, bool> ok)
    {
        if (el is TextBlock or AccessText && ok((FrameworkElement)el)) return (FrameworkElement)el;
        var n = VisualTreeHelper.GetChildrenCount(el);
        for (var i = 0; i < n; i++)
            if (FindMatching(VisualTreeHelper.GetChild(el, i), ok) is { } found) return found;
        return null;
    }

    private static string TextOf(FrameworkElement t) => t switch
    {
        TextBlock tb => tb.Text.Length > 0 ? tb.Text : new TextRange(tb.ContentStart, tb.ContentEnd).Text,
        AccessText at => at.Text,
        _ => "",
    };

    /// <summary>文字不受限制時要多寬（單行、不換行）。</summary>
    private static double NeededWidth(FrameworkElement t)
    {
        var text = TextOf(t).Replace("_", t is AccessText ? "" : "_");
        var ft = new FormattedText(text, t.Language.GetSpecificCulture(), FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(t), TextElement.GetFontStyle(t), TextElement.GetFontWeight(t), TextElement.GetFontStretch(t)),
            TextElement.GetFontSize(t), Brushes.Black, null, TextOptions.GetTextFormattingMode(t), 1.0);
        var wraps = t is TextBlock tb && tb.TextWrapping != TextWrapping.NoWrap;
        return wraps ? Math.Max(t.ActualWidth, LongestWord(t, text)) : ft.WidthIncludingTrailingWhitespace;
    }

    // ⚠ 中日韓文字沒有空格，但每兩個字之間都可以斷行 —— 以空格切的話，一整句中文被當成一個「字」，
    //   換行的說明文字明明全部顯示了，也會報成被切掉（QuillNest 修好之後實測踩到）。
    private static double LongestWord(FrameworkElement t, string text) =>
        Regex.Matches(text, @"[⺀-鿿가-힯豈-﫿＀-￯]|[^\s⺀-鿿가-힯豈-﫿＀-￯]+")
            .Select(m => m.Value).Select(w => new FormattedText(w, t.Language.GetSpecificCulture(), FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(t), TextElement.GetFontStyle(t), TextElement.GetFontWeight(t), TextElement.GetFontStretch(t)),
            TextElement.GetFontSize(t), Brushes.Black, null, TextOptions.GetTextFormattingMode(t), 1.0).Width).DefaultIfEmpty(0).Max();

    /// <summary>文字的水平範圍，被每一層祖先的版面裁切區與視窗工作區交集之後還剩多寬。</summary>
    private static double VisibleWidth(FrameworkElement probe, Window root)
    {
        double left = 0, right = Math.Max(probe.ActualWidth, NeededWidth(probe));

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
                    var origin = content.TransformToAncestor(root).Transform(new Point(0, 0));
                    var parent = VisualTreeHelper.GetParent(content) as FrameworkElement;
                    var b = new Rect(origin.X - (content.Margin.Left), origin.Y, (parent?.ActualWidth ?? content.ActualWidth), 100000);
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
}
