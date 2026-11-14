using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Layout;

/// <summary>
/// 從文字元素往上走，算出它實際能畫文字的像素寬。
///
/// 這個類別裡每一條規則都來自 M0 的實查（docs/M0-可量測比例調查.md），
/// 其中四條是**修正過的錯誤**，不要「簡化」掉：
///
/// 1. 文字位置自己就是樣板化控制項時也要算 chrome（&lt;Button Content="Save"/&gt;）
/// 2. 快顯內容（ContextMenu／ToolTip／Popup）不受視窗寬度約束
/// 3. 控制項名稱用**後綴比對**（MahApps 的 MetroTabControl）
/// 4. ItemsControl 不中止走訪，它自己可能有上界
///
/// 另外兩條是 M2 的必要條件（驗證者實跑後發現，語料裡各命中 79／60 處）：
///
/// 5. <c>SizeToContent</c> 會讓視窗的 <c>Width</c> 失效
/// 6. 水平捲動的 <c>ScrollViewer</c> 讓上界失效（文字可捲動，不會被裁）
/// </summary>
public sealed class WidthResolver
{
    private readonly ChromeTable _chrome;
    private readonly StyleIndex _styles;
    private readonly IElementMeasurer? _measurer;

    /// <param name="measurer">給得出元素期望寬度時，Grid 的 <c>Auto</c>／<c>*</c> 欄就解得開
    /// （M0 實測那佔「有裁切風險」位置的 21.6%）。傳 null 就退回原本的 <c>NeedsGrid</c>。</param>
    public WidthResolver(ChromeTable chrome, StyleIndex? styles = null, IElementMeasurer? measurer = null)
    {
        _chrome = chrome;
        _styles = styles ?? new StyleIndex();
        _measurer = measurer;
    }

    /// <summary>屬性的生效值：本地屬性優先，沒有就查樣式。
    /// ⚠ 不查樣式會漏掉大量版面資訊 —— 實查 QuillNest 的 SectionHint 樣式設了
    /// FontSize 與 Margin，兩者都影響量測。版面屬性不會從祖先繼承，所以 inheritable: false。</summary>
    private string? Attr(XElement el, string name) =>
        _styles.Resolve(el, name, inheritable: false);

    // 範本／樣式宿主：寬度由使用端決定
    private static readonly HashSet<string> TemplateHosts = new(StringComparer.Ordinal)
    {
        "ControlTemplate", "DataTemplate", "ItemsPanelTemplate", "HierarchicalDataTemplate",
        "Style", "Setter",
    };

    // 快顯與依內容決定大小的宿主：走到這裡就停（實查：QuillNest NoteEditorWindow.xaml:838）
    private static readonly HashSet<string> ContentSized = new(StringComparer.Ordinal)
    {
        "ContextMenu", "ToolTip", "Popup", "Menu", "MenuItem",
    };

    // 會把項目寬度壓到自己寬度的容器（Menu／TabControl／ToolBar 是依內容決定大小的，不在此列）
    private static readonly HashSet<string> ItemsControls = new(StringComparer.Ordinal)
    {
        "ItemsControl", "ListBox", "ListView", "DataGrid", "TreeView",
    };

    /// <summary>
    /// 項目寬度算得出來的 ItemsControl：可用寬度 = 自己的寬 − chrome − 捲軸 − 項目容器 chrome。
    ///
    /// ⚠ <c>DataGrid</c> 不在此列：儲存格的寬度來自**欄**（DataGridColumn 有自己的
    ///   Auto／star／SizeToHeader 尺寸系統），跟 DataGrid 本身的寬度不是同一回事。
    /// ⚠ <c>TreeView</c> 不在此列：項目會依巢狀深度往右縮排（每層約 19 DIP），
    ///   而深度對綁資料的項目是執行期才知道的。
    /// </summary>
    private static readonly HashSet<string> SolvableItemsControls = new(StringComparer.Ordinal)
    {
        "ItemsControl", "ListBox", "ListView",
    };

    public AvailableWidth Resolve(XElement site) => Resolve(site, 0);

    /// <param name="depth">解巢狀 Grid 時會再呼叫自己（Grid 裡的 Grid），這是遞迴深度上限。</param>
    private AvailableWidth Resolve(XElement site, int depth)
    {
        if (depth > 6)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "layout nesting is too deep to resolve");

        var siteName = site.Name.LocalName;

        // 規則 1：位置自己是樣板化控制項（<Button Content="Save"/>）
        var (siteKnown, siteChrome) = _chrome.Lookup(siteName);
        if (siteKnown && siteChrome is null)
            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable,
                $"chrome of <{siteName}> is unknown (content width does not propagate)");

        // ⚠ 記帳規則（WPF 的版面語意）：
        //   一個元素的 Width 是**外框**寬 —— 它自己的 Padding 與 BorderThickness 在 Width 裡面，
        //   但它的 Margin 在 Width 外面。
        //   所以：上界在元素 E 身上時，只扣 E 的 Padding/Border/chrome，不扣 E 的 Margin；
        //        經過元素 E 往上走時，E 的 Margin 與 Padding/Border/chrome 都要扣。
        var deductions = 0.0;
        var usedChrome = siteKnown && siteChrome!.Value > 0;
        var gridFlexible = false;
        var gridReason = "Auto/* Grid column in the chain";
        var inItems = false;
        var itemsReason = "ItemsControl in the chain";
        var assumedScrollBar = false;

        XElement? cur = site;

        while (cur is not null)
        {
            var local = cur.Name.LocalName;

            if (TemplateHosts.Contains(local))
                return AvailableWidth.NotKnown(WidthStatus.InTemplate, $"inside <{local}>");

            // 規則 2：快顯／依內容決定大小
            if (!ReferenceEquals(cur, site) && ContentSized.Contains(local))
                return AvailableWidth.NotKnown(WidthStatus.NoDeclaredLimit, $"content-sized host <{local}>");

            // 規則 6：水平可捲動 → 上界失效，文字不會被裁（語料 60 處）
            if (IsHorizontallyScrollable(cur))
                return AvailableWidth.NotKnown(WidthStatus.NoDeclaredLimit,
                    $"<{local}> scrolls horizontally; text is not clipped");

            // 自己身上的硬上界
            if (TryWidthBound(cur, out var bound, out var which))
            {
                // 規則 5：SizeToContent 讓 Width 失效（語料 46 處同時寫了兩者）
                if (IsSizeToContentWidth(cur))
                    return AvailableWidth.NotKnown(WidthStatus.Unmeasurable,
                        $"<{local}> has SizeToContent, so its Width does not bind");

                // 上界就在這個元素身上：扣它自己的內距與 chrome，但不扣它的 Margin
                var (inner, innerChrome, innerUnknown) = InnerInset(cur);
                if (innerUnknown)
                    return AvailableWidth.NotKnown(WidthStatus.Unmeasurable,
                        $"chrome of <{local}> is unknown (content width does not propagate)");
                if (innerChrome) usedChrome = true;

                var total = deductions + inner;
                var px = bound - total;
                if (px <= 0)
                    return AvailableWidth.NotKnown(WidthStatus.Unmeasurable,
                        $"insets ({total:F1}) exceed the bound ({bound:F1}) on <{local}>");

                if (gridFlexible) return AvailableWidth.NotKnown(WidthStatus.NeedsGrid, gridReason);
                if (inItems) return AvailableWidth.NotKnown(WidthStatus.NeedsItems, itemsReason);

                return new AvailableWidth(
                    WidthStatus.Known, px, $"{which}={bound:G} on <{local}>", LineOf(cur), usedChrome,
                    $"minus {total:F1} DIP of insets{(usedChrome ? " and control chrome" : "")}" +
                    (assumedScrollBar ? $", assuming a visible vertical scroll bar ({_chrome.ScrollBarWidth:F1} DIP)" : ""));
            }

            // 要往上走了：這個元素的 Margin 與內距都吃掉寬度
            {
                var (inner, innerChrome, innerUnknown) = InnerInset(cur);
                if (innerUnknown)
                    return AvailableWidth.NotKnown(WidthStatus.Unmeasurable,
                        $"chrome of <{local}> is unknown (content width does not propagate)");
                if (innerChrome) usedChrome = true;
                var (ml, mr) = ParseThicknessLeftRight(Attr(cur, "Margin"));
                deductions += inner + ml + mr;
            }

            var parent = cur.Parent;
            if (parent is null) break;

            // 屬性元素（<Grid.ColumnDefinitions>）不是版面層級
            if (parent.Name.LocalName.Contains('.', StringComparison.Ordinal)) { cur = parent; continue; }

            var pLocal = parent.Name.LocalName;

            if (ContentSized.Contains(pLocal))
                return AvailableWidth.NotKnown(WidthStatus.NoDeclaredLimit, $"content-sized host <{pLocal}>");

            // 規則 4：不中止，繼續往上找 ItemsControl 自己的上界
            if (MatchesSuffix(pLocal, ItemsControls))
            {
                if (MatchesSuffix(pLocal, SolvableItemsControls))
                {
                    // ⚠ 垂直捲軸出不出現取決於執行期的項目數，靜態不可知。
                    //   **假設它在**：少算可用寬度只會誤報，多算會漏報溢出。
                    //   只有明寫 Disabled／Hidden 時才確定它不佔空間。
                    var vis = Attr(parent, "ScrollViewer.VerticalScrollBarVisibility");
                    var barHidden = vis is not null &&
                        (vis.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                         vis.Equals("Hidden", StringComparison.OrdinalIgnoreCase));
                    if (!barHidden)
                    {
                        deductions += _chrome.ScrollBarWidth;
                        assumedScrollBar = true;
                    }
                }
                else
                {
                    inItems = true;
                    itemsReason = $"inside <{pLocal}>, whose item width comes from a separate sizing system";
                }
            }

            if (IsGrid(pLocal))
            {
                var (state, why) = GridColumn(parent, cur, out var fixedWidth);
                if (state == GridState.Fixed)
                {
                    var px = fixedWidth - deductions;
                    if (px <= 0)
                        return AvailableWidth.NotKnown(WidthStatus.Unmeasurable, "insets exceed the Grid column width");
                    // ⚠ 內層有解不開的 Auto／星號欄時，不能拿外層的欄寬當答案 ——
                    //   真正的寬度比它窄，照用會高估可用寬度而漏報溢出（全面重讀時發現漏了這條）。
                    if (gridFlexible) return AvailableWidth.NotKnown(WidthStatus.NeedsGrid, gridReason);
                    if (inItems) return AvailableWidth.NotKnown(WidthStatus.NeedsItems, itemsReason);

                    return new AvailableWidth(
                        WidthStatus.Known, px, $"fixed Grid column ({why})", LineOf(parent), usedChrome,
                        $"minus {deductions:F1} DIP of insets{(usedChrome ? " and control chrome" : "")}" +
                        (assumedScrollBar ? $", assuming a visible vertical scroll bar ({_chrome.ScrollBarWidth:F1} DIP)" : ""));
                }
                if (state == GridState.Flexible)
                {
                    // Auto／星號欄：試著真的解開它（M0：佔有風險位置的 21.6%）
                    var solved = TrySolveGrid(parent, cur, depth);
                    if (solved.Width is { } solvedWidth)
                    {
                        var px = solvedWidth - deductions;
                        if (px <= 0)
                            return AvailableWidth.NotKnown(WidthStatus.Unmeasurable,
                                "insets exceed the solved Grid column width");
                        if (gridFlexible) return AvailableWidth.NotKnown(WidthStatus.NeedsGrid, gridReason);
                        if (inItems) return AvailableWidth.NotKnown(WidthStatus.NeedsItems, itemsReason);

                        return new AvailableWidth(
                            WidthStatus.Known, px, $"solved Grid {solved.Reason}", LineOf(parent), usedChrome,
                            $"minus {deductions:F1} DIP of insets{(usedChrome ? " and control chrome" : "")}" +
                        (assumedScrollBar ? $", assuming a visible vertical scroll bar ({_chrome.ScrollBarWidth:F1} DIP)" : ""));
                    }

                    gridFlexible = true;
                    gridReason = solved.Reason;
                }
            }

            cur = parent;
        }

        return AvailableWidth.NotKnown(WidthStatus.NoDeclaredLimit, "no width bound found up to the root");
    }

    /// <summary>試著解開 Grid 的 Auto/星號欄。先把 Grid 自己的可用寬度解出來（可能又是一層 Grid），
    /// 再跑 WPF 的欄寬演算法。任何一步算不出來就回 null，讓呼叫端退回 NeedsGrid。</summary>
    private GridSolveResult TrySolveGrid(XElement grid, XElement child, int depth)
    {
        if (_measurer is null) return new GridSolveResult(null, "Auto/* Grid column (no measurer available)");

        var own = Resolve(grid, depth + 1);
        if (own.Status != WidthStatus.Known)
            return new GridSolveResult(null, $"Auto/* Grid column whose own width is {own.Status}");

        var result = new GridSolver(_measurer, _styles).Solve(grid, child, own.Px, depth);
        return result.Width is null
            ? new GridSolveResult(null, $"Auto/* Grid column: {result.Reason}")
            : result;
    }

    private static bool IsGrid(string name) =>
        name.EndsWith("Grid", StringComparison.Ordinal) && name is not ("DataGrid" or "UniformGrid");

    /// <summary>Width 或 MaxWidth。MaxWidth 一樣是硬上界 —— 超過就一定裁或換行。</summary>
    private bool TryWidthBound(XElement el, out double bound, out string which)
    {
        if (TryLength(Attr(el, "Width"), out bound)) { which = "Width"; return true; }
        if (TryLength(Attr(el, "MaxWidth"), out bound)) { which = "MaxWidth"; return true; }
        which = ""; return false;
    }

    /// <summary>規則 5。SizeToContent="Width" 或 "WidthAndHeight" 時視窗依內容決定寬度，
    /// 寫在 XAML 裡的 Width 只是初始值，不構成上界。</summary>
    private static bool IsSizeToContentWidth(XElement el)
    {
        var v = el.Attribute("SizeToContent")?.Value?.Trim();
        return v is not null &&
               (v.Equals("Width", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("WidthAndHeight", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>規則 6。ScrollViewer 的水平捲軸開著時，內容可以比視埠寬，文字不會被裁。
    /// ⚠ ScrollViewer 的 HorizontalScrollBarVisibility 預設是 Disabled（垂直才是 Visible），
    ///   所以只有明寫 Auto／Visible 才算。</summary>
    private static bool IsHorizontallyScrollable(XElement el)
    {
        if (!el.Name.LocalName.EndsWith("ScrollViewer", StringComparison.Ordinal)) return false;
        var v = el.Attribute("HorizontalScrollBarVisibility")?.Value?.Trim();
        return v is not null &&
               (v.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("Visible", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 元素「內部」吃掉的寬度：XAML 明寫的 Padding、BorderThickness，加上控制項範本的 chrome。
    /// **不含 Margin** —— Margin 在 Width 外面。
    ///
    /// ⚠ chrome 表的值是在控制項**沒有被覆寫**的狀態下量的。XAML 又明寫 Padding 時，
    ///   這裡是「chrome ＋ 明寫值」—— 嚴格說會把佈景的預設內距算兩次。
    ///   實測 Aero2 的 Padding 屬性在量測時讀到 0（chrome 全來自範本），所以多數情況沒有重複；
    ///   真的重複時結果是**高估扣除量 → 低估可用寬度 → 偏保守**，不會漏報溢出。
    /// </summary>
    private (double Inset, bool UsedChrome, bool Unknown) InnerInset(XElement el)
    {
        double sum = 0;
        foreach (var attr in (ReadOnlySpan<string>)["Padding", "BorderThickness"])
        {
            var (l, r) = ParseThicknessLeftRight(Attr(el, attr));
            sum += l + r;
        }

        // ⚠ ComboBoxItem 的 chrome 只存在於**展開的下拉清單**裡。
        //   關起來的選取框是把項目的內容直接畫在 ComboBox 的 ContentPresenter 上，
        //   那個容器的內距不在場。而「會不會被裁」問的正是關起來那個框，
        //   所以這裡不扣它 —— 兩個都扣會重複計算約 10 DIP（約 6% 的誤差）。
        if (el.Name.LocalName.EndsWith("ComboBoxItem", StringComparison.Ordinal)
            && el.Parent?.Name.LocalName.EndsWith("ComboBox", StringComparison.Ordinal) == true)
            return (sum, false, false);

        var (known, chrome) = _chrome.Lookup(el.Name.LocalName);
        if (known && chrome is null) return (0, false, true);
        if (known && chrome!.Value > 0) return (sum + chrome.Value, true, false);
        return (sum, false, false);
    }

    /// <summary>Thickness 的四種寫法：「全」「左右,上下」「左,上,右,下」。</summary>
    internal static (double Left, double Right) ParseThicknessLeftRight(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return (0, 0);
        v = v.Trim();
        if (v.StartsWith('{')) return (0, 0);   // 綁定／資源：靜態不可知，當 0（偏保守）

        var parts = v.Split([',', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        double P(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

        return parts.Length switch
        {
            1 => (P(parts[0]), P(parts[0])),
            2 => (P(parts[0]), P(parts[0])),
            4 => (P(parts[0]), P(parts[2])),
            _ => (0, 0),
        };
    }

    private enum GridState { Fixed, Flexible, Unknown }

    private static (GridState, string) GridColumn(XElement grid, XElement child, out double width)
    {
        width = 0;
        var colAttr = child.Attribute("Grid.Column")?.Value;
        var col = 0;
        if (colAttr is not null && !int.TryParse(colAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out col))
            return (GridState.Unknown, "non-numeric Grid.Column");

        var span = 1;
        if (child.Attribute("Grid.ColumnSpan")?.Value is { } sp)
            int.TryParse(sp, NumberStyles.Integer, CultureInfo.InvariantCulture, out span);
        if (span < 1) span = 1;

        var defs = ColumnWidths(grid);
        if (defs.Count == 0) return (GridState.Unknown, "no ColumnDefinitions");

        double total = 0;
        for (var i = col; i < col + span && i < defs.Count; i++)
        {
            if (defs[i] is null) return (GridState.Flexible, $"column {i} is Auto/*");
            total += defs[i]!.Value;
        }

        width = total;
        return total > 0 ? (GridState.Fixed, $"column {col}") : (GridState.Unknown, "zero width");
    }

    /// <summary>每一欄的固定寬度；Auto／星號是 null。支援 ColumnDefinitions="Auto,*,100" 簡寫。</summary>
    private static List<double?> ColumnWidths(XElement grid)
    {
        var result = new List<double?>();

        if (grid.Attribute("ColumnDefinitions")?.Value is { Length: > 0 } shorthand)
        {
            foreach (var part in shorthand.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                result.Add(TryLength(part, out var w) ? w : null);
            return result;
        }

        var holder = grid.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid.ColumnDefinitions");
        if (holder is null) return result;

        foreach (var cd in holder.Elements().Where(e => e.Name.LocalName == "ColumnDefinition"))
        {
            var w = cd.Attribute("Width")?.Value;
            // ⚠ Width 省略時預設是 "*"（彈性），不是 Auto，也不是 0
            result.Add(w is not null && TryLength(w, out var px) ? px : null);
        }

        return result;
    }

    internal static bool TryLength(string? v, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(v)) return false;
        v = v.Trim();
        if (v.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return false;
        if (v.EndsWith('*')) return false;
        if (v.StartsWith('{')) return false;
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static bool MatchesSuffix(string name, HashSet<string> set)
    {
        if (set.Contains(name)) return true;
        foreach (var s in set)
            if (name.Length > s.Length && name.EndsWith(s, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>⚠ IXmlLineInfo.HasLineInfo 是方法不是屬性，不能用屬性模式比對。</summary>
    internal static int LineOf(XObject o) =>
        o is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
}
