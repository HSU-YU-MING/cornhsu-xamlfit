// M0 可量測比例調查 —— 拋棄式工具，只為了回答一個 go/no-go 問題：
//
//   真實專案裡的「可在地化文字位置」，有多少比例的可用寬度是靜態算得出來的？
//
// 規劃書第 6 節的判準：加上控制項內距表後 >= 40% 就做；15~40% 只做 budget；< 15% 關掉題目。
//
// ⚠ 這份程式碼刻意不求精確，只求分類不偏。它的輸出是用來「決定要不要做」，
//   不是用來報 finding。真正的引擎在 src/ 重寫。

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace M0Survey;

/// <summary>可用寬度能不能靜態算出來。</summary>
internal enum Verdict
{
    /// <summary>XAML 裡直接看得到硬上界（Width／MaxWidth／固定像素的 Grid 欄），
    /// 而且路上沒有需要內距表的控制項。</summary>
    Fixed,

    /// <summary>找得到硬上界，但路上經過樣板化控制項（Button、Label…），
    /// 它的內距不在 XAML 裡 —— 要靠「控制項預設內距表」才能扣準。</summary>
    NeedsChrome,

    /// <summary>找得到硬上界，但路上有 Grid 的 Auto／星號欄 —— v2 解得出來。</summary>
    NeedsGrid,

    /// <summary>在 ControlTemplate／DataTemplate／Style 裡：寬度由「誰用它」決定，
    /// 靜態看不到使用端。</summary>
    InTemplate,

    /// <summary>找得到硬上界，但路上經過會限制項目寬度的 ItemsControl ——
    /// 要扣掉項目容器的內距與捲軸寬，v2 處理。</summary>
    NeedsItems,

    /// <summary>在 ItemsControl 的項目裡，而且那個 ItemsControl 本身也沒有寬度上界。</summary>
    InItems,

    /// <summary>一路到根都沒有任何寬度上界：文字不會被裁，它會把版面撐開。
    /// ⚠ 這是「安全」，不是「不知道」—— 兩者混在一起會低估工具的可用性。</summary>
    Unconstrained,

    /// <summary>其他說不出來的情形。</summary>
    Indeterminate,
}

internal enum TextKind { Literal, Resource, Bound }

internal sealed record Site(
    string File, int Line, string Element, TextKind Text, Verdict Verdict, string Reason, string RootKind,
    string Content);

internal static class Program
{
    private const string Pres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    // 會顯示文字的元素 → 它的文字放在哪個屬性
    private static readonly Dictionary<string, string[]> TextElements = new(StringComparer.Ordinal)
    {
        ["TextBlock"] = ["Text"],
        ["Run"] = ["Text"],
        ["Label"] = ["Content"],
        ["Button"] = ["Content"],
        ["ToggleButton"] = ["Content"],
        ["RepeatButton"] = ["Content"],
        ["RadioButton"] = ["Content"],
        ["CheckBox"] = ["Content"],
        ["HyperlinkButton"] = ["Content"],
        ["MenuItem"] = ["Header"],
        ["TabItem"] = ["Header"],
        ["GroupBox"] = ["Header"],
        ["Expander"] = ["Header"],
        ["HeaderedContentControl"] = ["Header"],
        ["GridViewColumn"] = ["Header"],
        ["DataGridTextColumn"] = ["Header"],
        ["DataGridTemplateColumn"] = ["Header"],
        ["ColumnDefinition"] = [], // 佔位，不是文字元素
    };

    // 樣板化控制項：內距來自範本，不在 XAML 裡 —— 需要「控制項預設內距表」
    private static readonly HashSet<string> Templated = new(StringComparer.Ordinal)
    {
        "Button", "ToggleButton", "RepeatButton", "RadioButton", "CheckBox", "Label",
        "ComboBox", "ComboBoxItem", "ListBoxItem", "ListViewItem", "MenuItem", "TabItem",
        "GroupBox", "Expander", "TextBox", "PasswordBox", "DataGridColumnHeader",
        "HyperlinkButton", "ToolBar", "StatusBar", "StatusBarItem", "TreeViewItem",
    };

    // ⚠ 只放「會把項目寬度壓到自己寬度」的容器。
    //   Menu／ContextMenu／TabControl／ToolBar／StatusBar 是**依內容決定大小**的，
    //   把它們放進來會把一大堆其實不會被裁的選單文字誤歸成「不可知」（第一版就是這樣算錯的）。
    private static readonly HashSet<string> ItemsControls = new(StringComparer.Ordinal)
    {
        "ItemsControl", "ListBox", "ListView", "DataGrid", "TreeView", "ComboBox",
    };

    private static readonly HashSet<string> TemplateHosts = new(StringComparer.Ordinal)
    {
        "ControlTemplate", "DataTemplate", "ItemsPanelTemplate", "HierarchicalDataTemplate",
        "Style", "Setter", "ItemContainerStyle",
    };

    // ⚠ 快顯與依內容決定大小的容器：走到這裡就要停，不能繼續往上認視窗的寬度。
    //   ContextMenu／ToolTip／Popup 根本不在視窗的版面裡（它們是另一個 HWND）；
    //   Menu／MenuItem 的項目寬度等於內容寬度。
    //   實查：QuillNest NoteEditorWindow.xaml:838 的 <MenuItem> 在 <RichTextBox.ContextMenu> 裡，
    //   第一版認成「Width on Window」—— 完全錯誤的預算。
    private static readonly HashSet<string> ContentSized = new(StringComparer.Ordinal)
    {
        "ContextMenu", "ToolTip", "Popup", "Menu", "MenuItem", "Separator",
    };

    /// <summary>後綴比對。MahApps 的 MetroTabControl 要對得上 TabControl、
    /// MetroProgressBar 要對得上 ProgressBar —— 精確比對會整組漏掉第三方控制項庫。
    /// 兩邊都中時取最長的（名字愈長愈具體）。這條規則直接抄自 XamlContrast 的實查結論。</summary>
    private static bool MatchesAny(string name, HashSet<string> set)
    {
        if (set.Contains(name)) return true;
        var best = 0;
        foreach (var s in set)
            if (name.Length > s.Length && name.EndsWith(s, StringComparison.Ordinal) && s.Length > best)
                best = s.Length;
        return best > 0;
    }

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: m0survey <corpus-name>=<path> [<name>=<path> ...] [--json <out>] [--samples <n>]");
            return 2;
        }

        var roots = new List<(string Name, string Path)>();
        string? jsonOut = null;
        var sampleCount = 0;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--json" && i + 1 < args.Length) { jsonOut = args[++i]; continue; }
            if (args[i] == "--samples" && i + 1 < args.Length) { sampleCount = int.Parse(args[++i], CultureInfo.InvariantCulture); continue; }
            var eq = args[i].IndexOf('=');
            if (eq <= 0) { Console.Error.WriteLine($"bad arg: {args[i]}"); return 2; }
            roots.Add((args[i][..eq], args[i][(eq + 1)..]));
        }

        var all = new List<(string Corpus, Site Site)>();
        var parseErrors = 0;
        var filesScanned = 0;

        foreach (var (name, path) in roots)
        {
            if (!Directory.Exists(path)) { Console.Error.WriteLine($"missing: {path}"); continue; }

            foreach (var file in EnumerateXaml(path))
            {
                filesScanned++;
                XDocument doc;
                try { doc = XDocument.Load(file, LoadOptions.SetLineInfo); }
                catch { parseErrors++; continue; }
                if (doc.Root is null) continue;

                var rootKind = doc.Root.Name.LocalName;
                var rel = Path.GetRelativePath(path, file).Replace('\\', '/');

                foreach (var site in Survey(doc.Root, rel, rootKind))
                    all.Add((name, site));
            }
        }

        Report(all, filesScanned, parseErrors, roots.Select(r => r.Name).ToList(), sampleCount);

        if (jsonOut is not null)
        {
            var payload = all.Select(x => new
            {
                corpus = x.Corpus,
                file = x.Site.File,
                line = x.Site.Line,
                element = x.Site.Element,
                text = x.Site.Text.ToString(),
                verdict = x.Site.Verdict.ToString(),
                reason = x.Site.Reason,
                rootKind = x.Site.RootKind,
            });
            File.WriteAllText(jsonOut,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false }));
            Console.WriteLine($"\nraw → {jsonOut}");
        }

        return 0;
    }

    private static IEnumerable<string> EnumerateXaml(string root)
    {
        foreach (var f in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            var p = f.Replace('\\', '/');
            // ⚠ 預設排除：bin/obj 是建置產物；點開頭的目錄含 .claude/worktrees ——
            //   那是同一個專案的副本，不排除會重複計算好幾倍（實查踩到）。
            if (p.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Contains("/.", StringComparison.Ordinal)) continue;
            if (p.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)) continue;
            yield return f;
        }
    }

    private static IEnumerable<Site> Survey(XElement root, string file, string rootKind)
    {
        foreach (var el in root.DescendantsAndSelf())
        {
            var local = el.Name.LocalName;
            if (!TextElements.TryGetValue(local, out var props) || props.Length == 0) continue;

            // 設計時命名空間的東西不算（d:Text 之類）
            if (IsIgnorableNs(el.Name.NamespaceName)) continue;

            var (kind, hasText, content) = ClassifyText(el, props);
            if (!hasText) continue;

            var (verdict, reason) = Classify(el);
            var line = (el as IXmlLineInfo).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
            yield return new Site(file, line, local, kind, verdict, reason, rootKind, content);
        }
    }

    private static bool IsIgnorableNs(string ns) =>
        ns.Contains("ignore", StringComparison.OrdinalIgnoreCase) ||
        ns.Contains("blend", StringComparison.OrdinalIgnoreCase) ||
        ns == "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static (TextKind Kind, bool Has, string Content) ClassifyText(XElement el, string[] props)
    {
        foreach (var p in props)
        {
            var v = el.Attribute(p)?.Value;
            if (v is null)
            {
                // 屬性元素寫法：<Button.Content>…</Button.Content>
                var pe = el.Element(XName.Get($"{el.Name.LocalName}.{p}", el.Name.NamespaceName));
                if (pe is not null && !pe.HasElements && !string.IsNullOrWhiteSpace(pe.Value))
                    return (TextKind.Literal, true, pe.Value.Trim());
                continue;
            }

            v = v.Trim();
            if (v.Length == 0) continue;

            if (v.StartsWith('{') && !v.StartsWith("{}", StringComparison.Ordinal))
            {
                // markup extension：分成「指向資源字串」與「綁資料」
                if (v.Contains("x:Static", StringComparison.Ordinal) ||
                    v.Contains("Resources.", StringComparison.Ordinal) ||
                    v.Contains("loc:", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("Localization", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("DynamicResource", StringComparison.Ordinal) ||
                    v.Contains("StaticResource", StringComparison.Ordinal))
                    return (TextKind.Resource, true, v);
                return (TextKind.Bound, true, v);
            }

            return (TextKind.Literal, true, v);
        }

        // TextBlock 的直接文字內容：<TextBlock>Save</TextBlock>
        if (el.Name.LocalName is "TextBlock" or "Run" && !el.HasElements && !string.IsNullOrWhiteSpace(el.Value))
            return (TextKind.Literal, true, el.Value.Trim());

        return (TextKind.Literal, false, "");
    }

    /// <summary>從文字元素往上走，看可用寬度算不算得出來。</summary>
    private static (Verdict, string) Classify(XElement site)
    {
        // ⚠ 文字位置**自己**就是樣板化控制項時（<Button Content="Save"/>），
        //   它的內距一樣來自範本 —— 第一版漏了這條，NeedsChrome 被嚴重低估。
        var chrome = MatchesAny(site.Name.LocalName, Templated);
        var gridFlexible = false;
        var inItems = false;
        XElement? cur = site;

        while (cur is not null)
        {
            var local = cur.Name.LocalName;

            // 在範本／樣式裡 —— 寬度由使用端決定，靜態看不到
            if (TemplateHosts.Contains(local))
                return (Verdict.InTemplate, local);

            // 快顯或依內容決定大小 —— 不受上層寬度約束，文字不會被裁
            if (ContentSized.Contains(local) && !ReferenceEquals(cur, site))
                return (Verdict.Unconstrained, $"content-sized host: {local}");

            // 自己身上有硬上界
            if (TryLength(cur.Attribute("Width")?.Value, out _))
                return (Pick(chrome, gridFlexible, inItems), $"Width on {local}");
            if (TryLength(cur.Attribute("MaxWidth")?.Value, out _))
                return (Pick(chrome, gridFlexible, inItems), $"MaxWidth on {local}");

            var parent = cur.Parent;
            if (parent is null) break;

            // 屬性元素（<Grid.RowDefinitions> 之類）不是版面層級，跳過它
            if (parent.Name.LocalName.Contains('.', StringComparison.Ordinal))
            {
                cur = parent;
                continue;
            }

            var pLocal = parent.Name.LocalName;

            // 快顯／內容決定大小的宿主：到此為止
            if (ContentSized.Contains(pLocal))
                return (Verdict.Unconstrained, $"content-sized host: {pLocal}");

            // ⚠ 不要在這裡中止。ItemsControl 自己可能有寬度上界 ——
            //   「固定寬 ListBox 裡的項目」是可量測的，中止就把它丟進黑洞了。
            if (MatchesAny(pLocal, ItemsControls)) inItems = true;

            if (pLocal.EndsWith("Grid", StringComparison.Ordinal) && pLocal != "DataGrid" && pLocal != "UniformGrid")
            {
                var (state, why) = GridColumnState(parent, cur);
                if (state == GridState.Fixed)
                    return (Pick(chrome, gridFlexible, inItems), $"fixed Grid column ({why})");
                if (state == GridState.Flexible)
                    gridFlexible = true;
            }

            if (MatchesAny(pLocal, Templated)) chrome = true;

            cur = parent;
        }

        // 一路到根都沒有上界
        return (inItems ? Verdict.InItems : Verdict.Unconstrained, "no width bound up to root");
    }

    /// <summary>找到上界之後，決定「還差什麼才算得出來」。由難到易取最難的那一項。</summary>
    private static Verdict Pick(bool chrome, bool gridFlexible, bool inItems) =>
        gridFlexible ? Verdict.NeedsGrid
        : inItems ? Verdict.NeedsItems
        : chrome ? Verdict.NeedsChrome
        : Verdict.Fixed;

    private enum GridState { Fixed, Flexible, Unknown }

    private static (GridState, string) GridColumnState(XElement grid, XElement child)
    {
        var colAttr = child.Attribute("Grid.Column")?.Value
                      ?? child.Attributes().FirstOrDefault(a => a.Name.LocalName == "Column" && a.Name.NamespaceName.Length == 0)?.Value;
        var col = 0;
        if (colAttr is not null && !int.TryParse(colAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out col))
            return (GridState.Unknown, "non-numeric Grid.Column");

        var spanAttr = child.Attribute("Grid.ColumnSpan")?.Value;
        var span = 1;
        if (spanAttr is not null) int.TryParse(spanAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out span);
        if (span < 1) span = 1;

        var defs = ColumnWidths(grid);
        if (defs.Count == 0) return (GridState.Unknown, "no ColumnDefinitions");

        for (var i = col; i < col + span && i < defs.Count; i++)
            if (!defs[i]) return (GridState.Flexible, $"col {i} is Auto/*");

        return (GridState.Fixed, $"col {col}");
    }

    /// <summary>每一欄是不是固定像素。支援屬性元素與 ColumnDefinitions="Auto,*,100" 簡寫。</summary>
    private static List<bool> ColumnWidths(XElement grid)
    {
        var result = new List<bool>();

        var shorthand = grid.Attribute("ColumnDefinitions")?.Value;
        if (!string.IsNullOrWhiteSpace(shorthand))
        {
            foreach (var part in shorthand.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                result.Add(TryLength(part, out _));
            return result;
        }

        var holder = grid.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid.ColumnDefinitions");
        if (holder is null) return result;

        foreach (var cd in holder.Elements().Where(e => e.Name.LocalName == "ColumnDefinition"))
        {
            var w = cd.Attribute("Width")?.Value;
            // Width 省略時預設是 "*"（彈性）
            result.Add(w is not null && TryLength(w, out _));
        }

        return result;
    }

    /// <summary>純數字的長度才算固定。Auto、*、2* 都不是。</summary>
    private static bool TryLength(string? v, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(v)) return false;
        v = v.Trim();
        if (v.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return false;
        if (v.EndsWith('*')) return false;
        if (v.StartsWith('{')) return false; // 綁定
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static void Report(
        List<(string Corpus, Site Site)> all, int files, int parseErrors, List<string> corpora, int samples)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("================ M0 可量測比例調查 ================");
        sb.AppendLine($"掃描 {files} 個 XAML 檔，解析失敗 {parseErrors} 個，找到 {all.Count} 個文字位置");
        sb.AppendLine();

        // 可在地化 = 字面字串或資源字串。綁資料的不是翻譯對象。
        var loc = all.Where(x => x.Site.Text is TextKind.Literal or TextKind.Resource).ToList();
        sb.AppendLine($"其中可在地化（字面＋資源）：{loc.Count}　綁資料：{all.Count - loc.Count}");
        sb.AppendLine();

        void Table(string title, List<(string Corpus, Site Site)> rows)
        {
            if (rows.Count == 0) return;
            sb.AppendLine($"--- {title}（{rows.Count}）---");
            var total = rows.Count;
            foreach (var g in rows.GroupBy(r => r.Site.Verdict).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {g.Key,-15} {g.Count(),6}　{100.0 * g.Count() / total,5:F1}%");
            sb.AppendLine();
        }

        Table("全部可在地化位置", loc);

        foreach (var c in corpora)
        {
            var rows = loc.Where(x => x.Corpus == c).ToList();
            Table($"語料：{c}", rows);
        }

        // 應用程式畫面 vs 控制項樣板庫：分開看才公平
        var screens = loc.Where(x => x.Site.RootKind is "Window" or "UserControl" or "Page").ToList();
        var dicts = loc.Where(x => x.Site.RootKind is "ResourceDictionary").ToList();
        Table("只看 App 畫面（Window／UserControl／Page）", screens);
        Table("只看 ResourceDictionary（樣板庫）", dicts);

        // ===== 閘門判定 =====
        sb.AppendLine("================ 閘門判定 ================");

        void Gate(string label, List<(string Corpus, Site Site)> rows)
        {
            if (rows.Count == 0) return;
            var n = rows.Count;
            var fixedN = rows.Count(r => r.Site.Verdict == Verdict.Fixed);
            var chromeN = rows.Count(r => r.Site.Verdict == Verdict.NeedsChrome);
            var gridN = rows.Count(r => r.Site.Verdict == Verdict.NeedsGrid);
            var itemsN = rows.Count(r => r.Site.Verdict == Verdict.NeedsItems);
            var uncon = rows.Count(r => r.Site.Verdict == Verdict.Unconstrained);

            // 「有風險」= 存在硬上界，文字真的可能被裁。無上界的位置不會裁，只會把版面撐開。
            var atRisk = fixedN + chromeN + gridN + itemsN;
            var v1 = fixedN + chromeN;

            sb.AppendLine($"[{label}]  n={n}");
            sb.AppendLine($"  有裁切風險（有硬上界）        {atRisk,6}　{100.0 * atRisk / n,5:F1}%");
            sb.AppendLine($"  不會裁（無上界，只會撐版面）  {uncon,6}　{100.0 * uncon / n,5:F1}%");
            if (atRisk > 0)
            {
                sb.AppendLine($"  └ v1 算得出來（fixed＋chrome） {v1,6}　佔有風險的 {100.0 * v1 / atRisk,5:F1}%");
                sb.AppendLine($"  └ 要 v2 解 Grid               {gridN,6}　佔有風險的 {100.0 * gridN / atRisk,5:F1}%");
                sb.AppendLine($"  └ 要 v2 解 ItemsControl       {itemsN,6}　佔有風險的 {100.0 * itemsN / atRisk,5:F1}%");
            }
            sb.AppendLine($"  v1 可量測佔全體                {v1,6}　{100.0 * v1 / n,5:F1}%");
            sb.AppendLine();
        }

        Gate("全部", loc);
        Gate("App 畫面", screens);
        foreach (var c in corpora)
            Gate(c, loc.Where(x => x.Corpus == c).ToList());

        // ===== 主產品（budget）的真正指標 =====
        // 預算 = 一個字串在所有出現位置裡的最小可用寬度。
        // 所以該問的不是「多少位置可量測」，而是「多少**字串**至少有一個可量測的位置」。
        // 一個字串出現十次、只有一次可量測，照樣給得出預算（只是那個預算可能偏鬆）。
        sb.AppendLine("================ 字串層級（budget 模式的真正指標）================");

        static bool Measurable(Verdict v) => v is Verdict.Fixed or Verdict.NeedsChrome;

        void Strings(string label, List<(string Corpus, Site Site)> rows)
        {
            var groups = rows
                .Where(r => r.Site.Content.Length > 0)
                .GroupBy(r => (r.Corpus, r.Site.Content))
                .ToList();
            if (groups.Count == 0) return;

            var total = groups.Count;
            var anyMeasurable = groups.Count(g => g.Any(r => Measurable(r.Site.Verdict)));
            var allMeasurable = groups.Count(g => g.All(r => Measurable(r.Site.Verdict)));
            var multiSite = groups.Count(g => g.Count() > 1);
            var partial = anyMeasurable - allMeasurable;

            sb.AppendLine($"[{label}]  相異字串 {total}（其中 {multiSite} 個出現在多個位置）");
            sb.AppendLine($"  至少一個位置可量測 → 給得出預算   {anyMeasurable,6}　{100.0 * anyMeasurable / total,5:F1}%");
            sb.AppendLine($"  └ 全部位置都可量測 → 預算可信     {allMeasurable,6}　{100.0 * allMeasurable / total,5:F1}%");
            sb.AppendLine($"  └ 只有部分可量測 → 預算偏鬆要標註 {partial,6}　{100.0 * partial / total,5:F1}%");
            sb.AppendLine();
        }

        Strings("全部", loc);
        Strings("App 畫面", screens);

        if (samples > 0)
        {
            sb.AppendLine("================ 抽樣（人工核對用）================");
            var rnd = new Random(20261114);
            foreach (var g in loc.GroupBy(x => x.Site.Verdict))
            {
                sb.AppendLine($"--- {g.Key} ---");
                foreach (var s in g.OrderBy(_ => rnd.Next()).Take(samples))
                    sb.AppendLine($"  {s.Corpus}/{s.Site.File}:{s.Site.Line}  <{s.Site.Element}>  {s.Site.Reason}");
                sb.AppendLine();
            }
        }

        Console.Write(sb.ToString());
    }
}
