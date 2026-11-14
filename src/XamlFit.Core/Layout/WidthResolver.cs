using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Layout;

/// <summary>掛在 XDocument 上：這個檔案受哪個外部佈景影響（MaterialDesign、wpfui…）。</summary>
public sealed record ExternalTheme(string Name);

/// <summary>同一排（水平 StackPanel、DockPanel）的兄弟怎麼算進一個元素的寬度。</summary>
public enum RowSharing
{
    /// <summary>
    /// 扣掉整排其他所有兄弟：這個元素長到多寬，**整排**都還留在畫面裡。給譯者的預算用這個 ——
    /// 一個按鈕的譯文把後面的按鈕擠出去，問題出在它身上。
    /// </summary>
    WholeRow,

    /// <summary>
    /// 只扣排在它**前面**的兄弟：WPF 實際的排法（依文件順序，前面的拿到它要的寬度，後面的被擠掉）。
    /// 檢查模式用這個 —— 要報的是**哪一個真的被裁**。
    ///
    /// ⚠ CorpusRender 實測 Playnite 的崩潰視窗：用 WholeRow 檢查時工具把整排都報出來（111 筆誤報），
    ///   而真正被擠掉的最後一個按鈕在空間變成 0 時被當成「算不出來」而漏報（43 筆）。
    /// </summary>
    Preceding,
}

/// <summary>
/// 從文字元素往上走到根，算出它實際**看得見**的像素寬。
///
/// 最重要的一條原則：
///
/// > **寧可說「量不到」，也不要算出一個偏大的數字。**
/// > 可用寬度高估 = 把「裝不下」說成「裝得下」，那是這支工具最嚴重的失敗。
///
/// 所以凡是「有人設定了、但靜態讀不出來」的東西（綁定的 Width、專案外的樣式、被換掉的範本、
/// 第三方控制項的內距），一律是 <see cref="WidthStatus.Unmeasurable"/>，不是「當成沒設定」。
///
/// ── 演算法 ──
///
/// 一路走到根，沿途每一個寬度上界都是一個**候選**，答案是所有候選裡最小的那個。
/// 第一版在遇到第一個上界就停，WPF 實測那是錯的：
/// <c>MaxWidth="300"</c> 的 TextBlock 放在 100 寬的欄裡只看得到 100；連明寫 <c>Width="300"</c> 也一樣。
///
/// 路上遇到算不下去的東西（擋路者）時不立刻放棄 —— 它上面如果根本沒有上界，
/// 答案仍然是「查不到上界」而不是「量不到」。規則是：
///
/// - 擋路者**之前**記下的候選仍然有效（它們不經過擋路者）
/// - 擋路者之後又遇到上界：外層可能更窄但算不出來。
///   這時如果手上有一個**明寫的 <c>Width</c>** 候選，就採用它（作者明確決定了那個框的大小；
///   框本身被外層裁掉屬於版面問題，列在已知限制）；只有 <c>MaxWidth</c> 候選則不行 ——
///   MaxWidth 只是天花板，真正的寬度來自外層。
///
/// 規則的實查來源見 CLAUDE.md 的坑表與 docs/M6-全面重讀.md。
/// </summary>
public sealed class WidthResolver
{
    private readonly ChromeTable _chrome;
    private readonly StyleIndex _styles;
    private readonly IElementMeasurer? _measurer;

    /// <param name="measurer">給得出元素期望寬度時，Grid 的 <c>Auto</c>／<c>*</c> 欄與兄弟分寬度的面板就解得開。
    /// 傳 null 就退回量不到。</param>
    public RowSharing Sharing { get; init; } = RowSharing.WholeRow;

    public WidthResolver(ChromeTable chrome, StyleIndex? styles = null, IElementMeasurer? measurer = null)
    {
        _chrome = chrome;
        _styles = styles ?? new StyleIndex();
        _measurer = measurer;
        // 範本裡常見的「系統捲軸寬度」寫法要解得開（見 StyleIndex.SystemScrollBarWidth）
        _styles.SystemScrollBarWidth ??= chrome.ScrollBarWidth;
        if (measurer is ElementMeasurer own) own.Attach(this);
    }

    // 範本／樣式宿主：寬度由使用端決定
    private static readonly HashSet<string> TemplateHosts = new(StringComparer.Ordinal)
    {
        "ControlTemplate", "DataTemplate", "ItemsPanelTemplate", "HierarchicalDataTemplate",
        "Style", "Setter", "ResourceDictionary",
    };

    /// <summary>項目寬度來自另一套尺寸系統的容器 —— 不解。DataGrid：儲存格寬度來自 DataGridColumn。</summary>
    private static readonly string[] SeparatelySized = ["DataGrid"];

    /// <summary>範本裡有 ScrollViewer、而且**預設水平捲軸是 Auto** 的清單控制項。</summary>
    private static readonly string[] ScrollingLists = ["ListBox", "ListView", "TreeView"];

    /// <summary>會把項目排進面板的控制項（換了 ItemsPanel 就不知道怎麼排）。</summary>
    private static readonly string[] ItemsHosts = ["ItemsControl", "ListBox", "ListView", "ComboBox", "TreeView", "StatusBar"];

    /// <summary>子元素放在這些屬性裡是一般的內容位置。其他屬性元素（Icon、Footer、Tag…）不知道會被排在哪。</summary>
    private static readonly HashSet<string> ContentProperties = new(StringComparer.Ordinal)
    {
        "Content", "Child", "Children", "Items", "Inlines", "Blocks",
    };

    public AvailableWidth Resolve(XElement site) => Resolve(site, 0);

    /// <param name="depth">解巢狀 Grid 時會再呼叫自己（Grid 裡的 Grid），這是遞迴深度上限。</param>
    internal AvailableWidth Resolve(XElement site, int depth)
    {
        if (depth > 6)
            return Blocked(WidthStatus.Unmeasurable, "layout nesting is too deep to resolve");

        var siteName = site.Name.LocalName;

        // 分頁標籤排在一列、各自依內容決定寬度，放不下會換到下一列 —— 不會被裁
        if (EndsWithAny(siteName, "TabItem"))
            return Blocked(WidthStatus.NoDeclaredLimit, "tab headers size to their content");

        // 欄標題：寬度來自欄自己，跟清單多寬無關
        if (ColumnHeaderKey(siteName) is { } headerKey)
            return ResolveColumnHeader(site, headerKey);

        var walk = new Walk(this);
        return walk.Run(site, depth);
    }

    /// <summary>一次往上走的狀態。拆成類別是因為區域函式抓太多變數之後很難讀。</summary>
    private sealed class Walk(WidthResolver owner)
    {
        // ⚠ 記帳規則（WPF 的版面語意）：
        //   一個元素的 Width 是**外框**寬 —— 它自己的 Padding 與 BorderThickness 在 Width 裡面，
        //   但它的 Margin 在 Width 外面。
        //   所以：上界在元素 E 身上時，只扣 E 的內距／chrome，不扣 E 的 Margin；
        //        經過元素 E 往上走時，E 的 Margin 與內距／chrome 都要扣。
        private double _deductions;
        private bool _usedChrome;
        private bool _assumedScrollBar;
        private bool _wrapDefeated;

        /// <summary>扣過同一排兄弟的寬度。這時「沒有剩下空間」是真的結果（WPF 把它擠到看不見），
        /// 不是「內距比上界還大、我們一定算錯了」。</summary>
        private bool _rowShared;

        private (double Px, string Source, XElement At, double Deducted)? _best;
        private bool _anchored;                              // 手上有明寫 Width 的候選
        private (WidthStatus Status, string Reason)? _pending;   // 第一個擋路者
        private bool _boundAfterPending;                     // 擋路者之後還有上界

        private void Block(WidthStatus status, string reason) => _pending ??= (status, reason);

        private void Candidate(double boundPx, string source, XElement at, double deducted, bool definitive)
        {
            if (_pending is not null) { _boundAfterPending = true; return; }
            var px = boundPx - deducted;
            if (_best is null || px < _best.Value.Px) _best = (px, source, at, deducted);
            if (definitive) _anchored = true;
        }

        /// <summary>上面不會再有上界了（走到根、快顯、可水平捲動）。</summary>
        private AvailableWidth Open(string reason)
        {
            if (_best is { } b && (!_boundAfterPending || _anchored)) return Known(b);
            if (_boundAfterPending && _pending is { } p) return Blocked(p.Status, p.Reason);
            return Blocked(WidthStatus.NoDeclaredLimit, reason);
        }

        /// <summary>遇到最後一個硬上界（固定欄、解出來的欄、UniformGrid 的格子）—— 再往上不是加減關係，在這裡結算。</summary>
        private AvailableWidth Closed(double boundPx, string source, XElement at)
        {
            Candidate(boundPx, source, at, _deductions, definitive: true);
            return Open("");
        }

        /// <summary>上面有上界，但那個上界算不出來。</summary>
        private AvailableWidth Unknown(WidthStatus status, string reason)
        {
            Block(status, reason);
            _boundAfterPending = true;
            return Open("");
        }

        /// <summary>走進範本或資源字典：寬度由使用端決定。</summary>
        private AvailableWidth TemplateExit(string where)
        {
            if (_best is { } b && _anchored) return Known(b);
            return Blocked(WidthStatus.InTemplate, where);
        }

        private int _resourceLookupsAtStart;

        private AvailableWidth Known((double Px, string Source, XElement At, double Deducted) b)
        {
            if (b.Px <= 0 && !_rowShared)
                return Blocked(WidthStatus.Unmeasurable,
                    $"insets ({b.Deducted:F1}) exceed the bound ({b.Source})");

            if (b.Px <= 0)
                return new AvailableWidth(WidthStatus.Known, 0, b.Source, LineOf(b.At), _usedChrome,
                    "the siblings before it in the same row already take all the width")
                {
                    WrapDefeated = true,
                    DependsOnNeighbourText = owner.ResourceLookups > _resourceLookupsAtStart,
                };

            return new AvailableWidth(
                WidthStatus.Known, b.Px, b.Source, LineOf(b.At), _usedChrome,
                $"minus {b.Deducted:F1} DIP of insets{(_usedChrome ? " and control chrome" : "")}" +
                (_assumedScrollBar
                    ? $", assuming a visible vertical scroll bar ({owner._chrome.ScrollBarWidth:F1} DIP)"
                    : ""))
            {
                AssumedScrollBar = _assumedScrollBar,
                WrapDefeated = _wrapDefeated,
                DependsOnNeighbourText = owner.ResourceLookups > _resourceLookupsAtStart,
            };
        }

        public AvailableWidth Run(XElement site, int depth)
        {
            _resourceLookupsAtStart = owner.ResourceLookups;
            var theme = ThemeOf(site);
            XElement? headerOwner = null;   // 下一個元素是「標題內容」的擁有者 → 用標題區的內距
            var siteHasHeaderText = site.Attribute("Header") is not null
                                    && EndsWithAny(site.Name.LocalName, "GroupBox", "Expander", "HeaderedContentControl");

            XElement? cur = site;

            while (cur is not null)
            {
                var local = cur.Name.LocalName;
                var isSite = ReferenceEquals(cur, site);

                if (TemplateHosts.Contains(local))
                    return TemplateExit($"inside <{local}>");

                // 規則：快顯與依內容決定大小的宿主不受外層寬度約束（實查：QuillNest NoteEditorWindow.xaml:838）
                if (!isSite && ContentSizedReason(cur) is { } sized)
                    return Open(sized);

                // 分頁的內容畫在 TabControl 的內容區，不在 TabItem 裡面 ——
                // TabItem 自己的 Width／Padding／Margin 描述的是標籤，跟內容無關（LayoutOracle 實測）
                if (!isSite && EndsWithAny(local, "TabItem")) { cur = LayoutParent(cur, ref headerOwner, out var exit0); if (exit0 is not null) return exit0; continue; }

                // LayoutTransform 會改變版面尺寸（放大 1.5 倍的文字需要 1.5 倍的空間）
                if (cur.Attribute("LayoutTransform") is not null
                    || cur.Elements().Any(e => e.Name.LocalName.EndsWith(".LayoutTransform", StringComparison.Ordinal)))
                    Block(WidthStatus.Unmeasurable, $"<{local}> has a LayoutTransform");

                // 水平可捲動 → 文字不會被裁
                switch (owner.HorizontalScroll(cur))
                {
                    case (true, var why): return Open(why);
                    case (null, var why): Block(WidthStatus.Unmeasurable, why); break;
                }

                // 這個元素內部吃掉的寬度
                var useHeader = ReferenceEquals(cur, headerOwner) || (isSite && siteHasHeaderText);
                var inner = useHeader ? owner.HeaderInset(cur, theme) : owner.InnerInset(cur, theme);
                if (inner.Blocked is { } innerWhy) Block(WidthStatus.Unmeasurable, innerWhy);
                else { _usedChrome |= inner.UsedChrome; _assumedScrollBar |= inner.AssumedScrollBar; }

                // 這個元素身上的寬度上界
                var bound = owner.WidthBound(cur);
                switch (bound.Kind)
                {
                    case BoundKind.Unreadable:
                        return Unknown(WidthStatus.Unmeasurable, bound.Why);
                    case BoundKind.SizesToContent:
                        return Open(bound.Why);
                    case BoundKind.Value:
                        Candidate(bound.Px, $"{bound.Which}={bound.Px:G} on <{local}>", cur,
                            _deductions + inner.Inset, bound.Definitive);
                        break;
                }

                // 往上走：這個元素的 Margin 與內距都吃掉寬度
                var margin = owner.Thickness(cur, "Margin");
                if (margin.Blocked is { } marginWhy) Block(WidthStatus.Unmeasurable, marginWhy);
                _deductions += inner.Inset + margin.Sum;

                var parent = LayoutParent(cur, ref headerOwner, out var exit);
                if (exit is not null) return exit;
                if (parent is null) break;

                var step = Step(parent, cur, depth);
                if (step is not null) return step;

                cur = parent;
            }

            return Open("no width bound found up to the root");
        }

        /// <summary>
        /// 最近的版面父層，穿過屬性元素（&lt;Grid.Children&gt;、&lt;Button.Content&gt;）。
        /// ⚠ 屬性元素不是版面層級，但**放在哪個屬性裡**決定了語意：
        ///   <c>.ToolTip</c> 是快顯、<c>.Resources</c> 是資源、<c>.Header</c> 用的是標題區的內距。
        /// </summary>
        private XElement? LayoutParent(XElement cur, ref XElement? headerOwner, out AvailableWidth? exit)
        {
            exit = null;
            var parent = cur.Parent;

            while (parent is not null && parent.Name.LocalName.Contains('.', StringComparison.Ordinal))
            {
                var full = parent.Name.LocalName;
                var prop = full[(full.LastIndexOf('.') + 1)..];
                var holder = parent.Parent;

                switch (prop)
                {
                    case "Resources":
                        exit = TemplateExit($"inside <{full}>");
                        return null;

                    case "ToolTip" or "ContextMenu":
                        exit = Open($"popup content (<{full}>)");
                        return null;

                    case "Header" when holder is not null:
                    {
                        var holderName = holder.Name.LocalName;
                        if (EndsWithAny(holderName, "TabItem"))
                        {
                            exit = Open("tab headers size to their content");
                            return null;
                        }
                        if (ColumnHeaderKey(holderName) is { } key)
                        {
                            var column = owner.ResolveColumnHeader(holder, key);
                            exit = column.Status switch
                            {
                                WidthStatus.Known => Closed(column.Px, column.BoundSource, holder),
                                WidthStatus.NoDeclaredLimit => Open(column.Reason),
                                _ => Unknown(column.Status, column.Reason),
                            };
                            if (column.Status == WidthStatus.Known) _usedChrome = true;
                            return null;
                        }
                        headerOwner = holder;
                        break;
                    }

                    default:
                        if (!ContentProperties.Contains(prop))
                            Block(WidthStatus.Unmeasurable,
                                $"placed through <{full}>, a property this tool does not model");
                        break;
                }

                parent = holder;
            }

            return parent;
        }

        /// <summary>從 child 跨到 parent 這一步：parent 怎麼把寬度分給 child。回傳非 null 表示在這裡結算。</summary>
        private AvailableWidth? Step(XElement parent, XElement child, int depth)
        {
            var pLocal = parent.Name.LocalName;

            if (EndsWithAny(pLocal, SeparatelySized))
            {
                Block(WidthStatus.NeedsItems,
                    $"inside <{pLocal}>, whose cell widths come from its columns");
                return null;
            }

            if (EndsWithAny(pLocal, ItemsHosts) && HasCustomItemsPanel(parent))
            {
                Block(WidthStatus.NeedsItems, $"<{pLocal}> has a custom ItemsPanel");
                return null;
            }

            if (EndsWithAny(pLocal, ScrollingLists))
            {
                if (pLocal.EndsWith("ListView", StringComparison.Ordinal) && HasProperty(parent, "View"))
                {
                    Block(WidthStatus.NeedsItems, $"<{pLocal}> lays its items out in GridView columns");
                    return null;
                }

                var list = owner.ListItemBinding(parent);
                if (list.Open is { } openWhy) return Open(openWhy);
                if (list.Blocked is { } listWhy) Block(WidthStatus.Unmeasurable, listWhy);
                _deductions += list.ScrollBar;
                _assumedScrollBar |= list.ScrollBar > 0;
                return null;
            }

            if (!ChromeTable.IsWpf(parent)) return null;   // 第三方面板：InnerInset 已經把它標成擋路者

            switch (pLocal)
            {
                case "StackPanel" or "VirtualizingStackPanel":
                {
                    var o = owner._styles.ResolveEx(parent, "Orientation", inheritable: false);
                    if (o.IsUnreadable)
                        Block(WidthStatus.Unmeasurable, $"Orientation of <{pLocal}> is not statically readable");
                    else if (o.IsValue && IsOneOf(o.Value, "Horizontal"))
                    {
                        // WPF 實測：水平 StackPanel 用無限寬量子元素 —— Wrap 不會換行，
                        // 排在前面的兄弟把它往右推，整排超出面板的部分被裁掉。
                        // 預算 = 面板寬 − 其他**所有**兄弟（讓後面的兄弟也留在畫面裡）。
                        _wrapDefeated = true;
                        Share(parent, child, $"a horizontal <{pLocal}>", _ => true, depth);
                    }
                    return null;
                }

                case "StatusBar":
                    // 預設的 ItemsPanel 是 DockPanel：項目往左靠、各自依內容決定寬度
                    _wrapDefeated = true;
                    Share(parent, child, "a <StatusBar>", _ => true, depth);
                    return null;

                case "DockPanel":
                    DockShare(parent, child, depth);
                    return null;

                case "BulletDecorator":
                {
                    // 項目符號（核取方塊的方框、選項按鈕的圓點）排在左邊，內容拿剩下的寬度
                    foreach (var bullet in parent.Elements()
                                 .Where(e => e.Name.LocalName == "BulletDecorator.Bullet")
                                 .SelectMany(e => e.Elements()))
                    {
                        if (owner._measurer?.DesiredWidth(bullet, depth + 1) is { } w) { _deductions += w; continue; }
                        Block(WidthStatus.Unmeasurable,
                            $"shares the width of a <BulletDecorator> with its bullet <{bullet.Name.LocalName}>, whose width is not statically knowable");
                        break;
                    }
                    return null;
                }

                case "WrapPanel":
                {
                    // 子元素用 ItemWidth（沒設就是無限寬）去量，超出面板寬的部分被裁掉
                    _wrapDefeated = true;
                    var item = owner._styles.ResolveEx(parent, "ItemWidth", inheritable: false);
                    if (item.IsUnreadable)
                        Block(WidthStatus.Unmeasurable, "ItemWidth of <WrapPanel> is not statically readable");
                    else if (item.IsValue && TryLength(item.Value, out var itemWidth))
                        Candidate(itemWidth, $"ItemWidth={itemWidth:G} on <WrapPanel>", parent, _deductions, definitive: true);
                    return null;
                }

                case "UniformGrid":
                {
                    if (owner.UniformColumns(parent) is not { } columns)
                    {
                        Block(WidthStatus.Unmeasurable, "the column count of <UniformGrid> is not statically knowable");
                        return null;
                    }
                    if (_pending is not null) return null;

                    var own = owner.Resolve(parent, depth + 1);
                    return own.Status switch
                    {
                        WidthStatus.Known => ClosedVia(own, own.Px / columns, $"UniformGrid cell (1/{columns} of {own.Px:F1})", parent),
                        WidthStatus.NoDeclaredLimit => Open(own.Reason),
                        WidthStatus.InTemplate => TemplateExit(own.Reason),
                        _ => Unknown(own.Status, own.Reason),
                    };
                }
            }

            if (!IsGrid(pLocal)) return null;

            var (state, why, fixedWidth) = owner.GridColumn(parent, child);

            if (state == GridState.Fixed)
                return Closed(fixedWidth, $"fixed Grid column ({why})", parent);

            if (state == GridState.Flexible)
            {
                if (_pending is not null) return null;   // 反正算不出來，交給上面決定是「量不到」還是「沒上界」

                var (solved, ownWidth) = owner.TrySolveGrid(parent, child, depth);
                if (solved.Width is { } solvedWidth && ownWidth is not null)
                {
                    _wrapDefeated |= solved.Unconstrained;
                    _rowShared |= solved.Squeezed;
                    return ClosedVia(ownWidth, solvedWidth, $"solved Grid {solved.Reason}", parent);
                }

                // 解不開。Grid 自己如果沒有上界，往上走也不會遇到上界，結果仍然是「查不到上界」
                Block(WidthStatus.NeedsGrid, solved.Reason);
            }

            return null;
        }

        /// <summary>經由另一次解析（容器自己的寬度）結算：把那次解析的旗標併進來。</summary>
        private AvailableWidth ClosedVia(AvailableWidth own, double boundPx, string source, XElement at)
        {
            _usedChrome |= own.UsedChromeTable;
            _assumedScrollBar |= own.AssumedScrollBar;
            _wrapDefeated |= own.WrapDefeated;
            return Closed(boundPx, source, at);
        }

        /// <summary>跟兄弟元素共用寬度：把符合條件的兄弟的期望寬度扣掉。算不出來就是擋路者。</summary>
        private void Share(XElement panel, XElement child, string what, Func<XElement, bool> counts, int depth)
        {
            _rowShared = true;
            var passedChild = false;
            foreach (var sibling in LayoutChildren(panel))
            {
                if (ReferenceEquals(sibling, child)) { passedChild = true; continue; }
                if (passedChild && owner.Sharing == RowSharing.Preceding) break;
                if (!counts(sibling)) continue;

                if (owner._measurer?.DesiredWidth(sibling, depth + 1) is { } w)
                {
                    _deductions += w;
                    continue;
                }

                Block(WidthStatus.Unmeasurable,
                    $"shares the width of {what} with <{sibling.Name.LocalName}>, whose width is not statically knowable");
                return;
            }
        }

        /// <summary>
        /// DockPanel（LayoutOracle 實測）：每個子元素拿到「前面的兄弟佔剩的」寬度。
        /// 靠左／靠右的兄弟佔掉自己的期望寬度；靠上／靠下的不佔水平空間。
        /// 最後一個子元素預設填滿剩餘空間（LastChildFill），它沒有最低需求。
        /// </summary>
        private void DockShare(XElement panel, XElement child, int depth)
        {
            var fill = owner._styles.ResolveEx(panel, "LastChildFill", inheritable: false);
            if (fill.IsUnreadable)
            {
                Block(WidthStatus.Unmeasurable, "LastChildFill of <DockPanel> is not statically readable");
                return;
            }
            var lastFills = !(fill.IsValue && IsOneOf(fill.Value, "False"));

            var children = LayoutChildren(panel);
            var last = children.Count > 0 ? children[^1] : null;
            var childIndex = children.FindIndex(c => ReferenceEquals(c, child));

            bool? Horizontal(XElement e)
            {
                var dock = e.Attribute("DockPanel.Dock")?.Value.Trim();
                if (dock is null) return true;                       // 預設 Left
                if (dock.StartsWith('{')) return null;
                return IsOneOf(dock, "Left", "Right");
            }

            if (Horizontal(child) is not { } childHorizontal)
            {
                Block(WidthStatus.Unmeasurable, "DockPanel.Dock is not statically readable");
                return;
            }

            // WPF 依文件順序排：每個子元素拿到「前面的兄弟佔剩的」。
            // 預算模式（WholeRow）另外把後面靠左右的兄弟也扣掉，讓整排都留在畫面裡；
            // 靠上／靠下的子元素不受後面的兄弟影響（它們排在它下面）。
            var childFills = lastFills && ReferenceEquals(child, last);
            var onlyPreceding = owner.Sharing == RowSharing.Preceding || (!childHorizontal && !childFills);
            _rowShared = true;

            for (var i = 0; i < children.Count; i++)
            {
                var sibling = children[i];
                if (ReferenceEquals(sibling, child)) continue;
                if (lastFills && ReferenceEquals(sibling, last)) continue;
                if (onlyPreceding && i > childIndex) continue;

                if (Horizontal(sibling) is not { } siblingHorizontal)
                {
                    Block(WidthStatus.Unmeasurable, "DockPanel.Dock is not statically readable");
                    return;
                }
                if (!siblingHorizontal) continue;

                if (owner._measurer?.DesiredWidth(sibling, depth + 1) is { } w)
                {
                    _deductions += w;
                    continue;
                }

                Block(WidthStatus.Unmeasurable,
                    $"shares the width of a <DockPanel> with <{sibling.Name.LocalName}>, whose width is not statically knowable");
                return;
            }
        }
    }

    private static AvailableWidth Blocked(WidthStatus status, string reason) =>
        AvailableWidth.NotKnown(status, reason);

    /// <summary>量兄弟元素時查了幾次在地化字串。寬度用到它們的位置要標出來。</summary>
    private int ResourceLookups => (_measurer as ElementMeasurer)?.ResourceLookups ?? 0;

    private static string? ThemeOf(XElement el) => el.Document?.Annotation<ExternalTheme>()?.Name;

    internal static List<XElement> LayoutChildren(XElement element)
    {
        var list = new List<XElement>();
        foreach (var e in element.Elements())
        {
            var name = e.Name.LocalName;
            if (!name.Contains('.', StringComparison.Ordinal)) { list.Add(e); continue; }

            // <Grid.Children>／<ItemsControl.Items> 裡的才是子元素；其他屬性元素（Resources、ColumnDefinitions）不是
            var prop = name[(name.LastIndexOf('.') + 1)..];
            if (prop is "Children" or "Items" or "Content" or "Child")
                list.AddRange(e.Elements().Where(x => !x.Name.LocalName.Contains('.', StringComparison.Ordinal)));
        }
        return list;
    }

    private static bool HasProperty(XElement el, string property) =>
        el.Attribute(property) is not null
        || el.Elements().Any(e => e.Name.LocalName.EndsWith("." + property, StringComparison.Ordinal));

    private static bool HasCustomItemsPanel(XElement el) => HasProperty(el, "ItemsPanel");

    // ───────────────────────── 不受外層約束的宿主 ─────────────────────────

    /// <summary>
    /// 這個元素的內容不受外層寬度約束（快顯、依內容決定大小、縮放）。回傳原因；不是的話回 null。
    /// </summary>
    private static string? ContentSizedReason(XElement el)
    {
        var local = el.Name.LocalName;

        if (EndsWithAny(local, "ContextMenu", "ToolTip", "Popup", "MenuItem") || local == "Menu")
            return $"content-sized host <{local}>";

        // 工具列放不下的項目會收進溢位選單，不是被裁
        if (EndsWithAny(local, "ToolBar", "ToolBarTray"))
            return $"<{local}> moves items that do not fit into its overflow menu";

        if (!ChromeTable.IsWpf(el)) return null;

        return local switch
        {
            // LayoutOracle 實測：Canvas 不約束也不裁切子元素；Viewbox 把內容縮小到放得下
            "Canvas" => "<Canvas> neither constrains nor clips its children",
            "Viewbox" => "<Viewbox> scales its content to fit instead of clipping it",
            _ => null,
        };
    }

    // ───────────────────────── 上界 ─────────────────────────

    private enum BoundKind { None, Value, SizesToContent, Unreadable }

    private readonly record struct BoundResult(BoundKind Kind, double Px, string Which, bool Definitive, string Why);

    /// <summary>
    /// 這個元素自己的寬度上界：<c>Width</c> 與 <c>MaxWidth</c> 取小的。
    ///
    /// ⚠ <c>SizeToContent</c> 讓 <c>Width</c> 失效（視窗依內容決定寬度，Width 只是初始值），
    ///   但 <c>MaxWidth</c> 仍然是硬上界。兩者都沒有時視窗會長到裝得下文字 —— 沒有上界。
    /// ⚠ 讀不出來的 Width／MaxWidth **不是「沒有上界」**：跳過它去用外層更寬的上界，
    ///   會算出偏大的數字。
    /// ⚠ 只有 MaxWidth 時它不是「確定的寬度」，只是天花板 —— 見類別說明。
    /// </summary>
    private BoundResult WidthBound(XElement el)
    {
        var local = el.Name.LocalName;
        var sizeToContent = IsSizeToContentWidth(el);

        double? w = null, m = null;
        var widthLabel = "Width";

        if (!sizeToContent)
        {
            var width = _styles.ResolveEx(el, "Width", inheritable: false);
            if (width.IsUnreadable)
                return new(BoundKind.Unreadable, 0, "", false, $"Width of <{local}> is not statically readable ({width.Why})");
            if (width.IsValue)
            {
                if (TryLength(width.Value, out var wv)) w = wv;
                else if (width.Value!.Trim() is "0" or "0.0")
                {
                    // 寬度 0 的容器是收起來的面板。展開的寬度寫在同一份檔案的動畫裡時，就讀得到
                    var (opened, why) = OpenedWidth(el);
                    if (opened is not { } ow)
                        return new(BoundKind.Unreadable, 0, "", false, why ?? $"<{local}> has Width=\"0\" (a collapsed panel; its run-time width is set elsewhere)");
                    w = ow;
                    widthLabel = "Width (opened by a storyboard)";
                }
                else if (!IsAutoLength(width.Value!))
                    return new(BoundKind.Unreadable, 0, "", false, $"Width=\"{width.Value}\" on <{local}> is not a length this tool can read");
            }
        }

        var max = _styles.ResolveEx(el, "MaxWidth", inheritable: false);
        if (max.IsUnreadable)
            return new(BoundKind.Unreadable, 0, "", false, $"MaxWidth of <{local}> is not statically readable ({max.Why})");
        if (max.IsValue)
        {
            if (TryLength(max.Value, out var mv)) m = mv;
            else if (!IsInfiniteLength(max.Value!))
                return new(BoundKind.Unreadable, 0, "", false, $"MaxWidth=\"{max.Value}\" on <{local}> is not a length this tool can read");
        }

        if (sizeToContent && m is null)
            return new(BoundKind.SizesToContent, 0, "", false, $"<{local}> sizes to its content");

        return (w, m) switch
        {
            // Width 會被 MaxWidth 夾住，兩者都在時寬度是確定的
            ({ } a, { } b) => a <= b ? new(BoundKind.Value, a, widthLabel, true, "") : new(BoundKind.Value, b, "MaxWidth", true, ""),
            ({ } a, null) => new(BoundKind.Value, a, widthLabel, true, ""),
            (null, { } b) => new(BoundKind.Value, b, "MaxWidth", false, ""),
            _ => default,
        };
    }

    /// <summary>
    /// 收起來（Width="0"）的面板展開後有多寬：同一份檔案裡、以這個元素為目標、改 Width 或 MaxWidth 的
    /// DoubleAnimation，取**最小的非 0 字面 To**。
    ///
    /// 實查 ScreenToGif 的編輯器：右側的動作面板寫 Width="0"，ShowPanelStoryboard 把它開到 280、
    /// ShowExtendedPanelStoryboard 開到更寬；程式碼只負責啟動這兩個動畫，從不自己設寬度。
    /// 取最小的：用比較寬的那個開的面板，工具只會少算、不會多算。
    /// 任何一個 To 不是字面數字（綁定、只寫 By、關鍵畫格）→ 不知道。
    /// ⚠ 程式碼裡直接設寬度的情形看不到 —— 那是靜態分析的極限，報告會註明寬度來自動畫。
    /// </summary>
    private static (double? Px, string? Why) OpenedWidth(XElement el)
    {
        var name = (string?)el.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") ?? (string?)el.Attribute("Name");
        if (name is null || el.Document?.Root is not { } root) return (null, null);

        double? smallest = null;
        foreach (var anim in root.Descendants())
        {
            var local = anim.Name.LocalName;
            if (!local.Contains("Animation", StringComparison.Ordinal)) continue;
            if ((string?)anim.Attribute("Storyboard.TargetName") != name) continue;
            var path = ((string?)anim.Attribute("Storyboard.TargetProperty") ?? "").Trim().Trim('(', ')');
            var prop = path[(path.LastIndexOf('.') + 1)..];
            if (prop is not ("Width" or "MaxWidth")) continue;

            if (local != "DoubleAnimation")
                return (null, $"<{el.Name.LocalName}> has Width=\"0\" and is opened by a {local}, whose widths this tool does not read");
            var to = (string?)anim.Attribute("To");
            if (to?.Trim() is "0" or "0.0") continue;   // 關起來的動畫
            if (to is null || !TryLength(to, out var px))
                return (null, $"<{el.Name.LocalName}> has Width=\"0\" and the storyboard that opens it has no literal To");
            if (px > 0 && (smallest is null || px < smallest)) smallest = px;
        }
        return (smallest, null);
    }

    private static bool IsAutoLength(string v) =>
        v.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) || v.Trim().Equals("NaN", StringComparison.OrdinalIgnoreCase);

    private static bool IsInfiniteLength(string v) =>
        v.Trim().Equals("Infinity", StringComparison.OrdinalIgnoreCase) || IsAutoLength(v);

    /// <summary>SizeToContent="Width" 或 "WidthAndHeight"。</summary>
    private static bool IsSizeToContentWidth(XElement el)
    {
        var v = el.Attribute("SizeToContent")?.Value?.Trim();
        return v is not null &&
               (v.Equals("Width", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("WidthAndHeight", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith('{'));   // 綁定的 SizeToContent：不知道，當成會依內容決定（不宣稱有上界）
    }

    // ───────────────────────── 欄標題 ─────────────────────────

    /// <summary>欄元素（DataGrid*Column、GridViewColumn）對應的標題控制項。不是欄就回 null。</summary>
    internal static string? ColumnHeaderKey(string local) =>
        local.EndsWith("GridViewColumn", StringComparison.Ordinal) ? "GridViewColumnHeader"
        : local.Contains("DataGrid", StringComparison.Ordinal) && local.EndsWith("Column", StringComparison.Ordinal)
            ? "DataGridColumnHeader"
            : null;

    /// <summary>
    /// 欄標題的可用寬度 = 欄自己的 <c>Width</c> − 標題控制項的內距。跟清單多寬無關。
    ///
    /// - 沒寫 Width／<c>Auto</c>／<c>SizeToHeader</c>：欄會跟著標題長大 → 沒有上界
    /// - <c>*</c>／<c>SizeToCells</c>：寬度取決於別的欄或儲存格內容 → 量不到
    /// </summary>
    private AvailableWidth ResolveColumnHeader(XElement column, string headerKey)
    {
        var local = column.Name.LocalName;
        var theme = ThemeOf(column);

        if (column.Ancestors().FirstOrDefault(a => TemplateHosts.Contains(a.Name.LocalName)) is { } host)
            return Blocked(WidthStatus.InTemplate, $"inside <{host.Name.LocalName}>");

        double? bound = null;
        var which = "Width";

        var width = _styles.ResolveEx(column, "Width", inheritable: false);
        if (width.IsUnreadable)
            return Blocked(WidthStatus.Unmeasurable, $"Width of <{local}> is not statically readable ({width.Why})");
        if (width.IsValue)
        {
            var v = width.Value!.Trim();
            if (TryLength(v, out var px)) bound = px;
            else if (!(IsAutoLength(v) || v.Equals("SizeToHeader", StringComparison.OrdinalIgnoreCase)))
                return Blocked(WidthStatus.Unmeasurable,
                    $"Width=\"{v}\" on <{local}> depends on the other columns or on cell content");
        }

        var max = _styles.ResolveEx(column, "MaxWidth", inheritable: false);
        if (max.IsUnreadable)
            return Blocked(WidthStatus.Unmeasurable, $"MaxWidth of <{local}> is not statically readable ({max.Why})");
        if (max.IsValue && TryLength(max.Value, out var maxPx) && (bound is null || maxPx < bound))
        {
            bound = maxPx;
            which = "MaxWidth";
        }

        if (bound is not { } boundPx)
            return Blocked(WidthStatus.NoDeclaredLimit, $"<{local}> sizes to its header");

        // 標題控制項的樣式被換掉 → 表裡的內距不適用
        if (HeaderRestyled(column, headerKey) is { } restyled)
            return Blocked(WidthStatus.Unmeasurable, $"chrome of <{headerKey}> is unknown: {restyled}");

        if (_chrome.HeaderChrome(headerKey, ChromeTable.IsWpf(column), theme) is not { } chrome)
            return Blocked(WidthStatus.Unmeasurable,
                $"chrome of <{headerKey}> is unknown: " +
                (theme is not null ? $"the external theme {theme} restyles it" : "there is no chrome data for it"));

        var (stylePadding, styleBlocks) = ImplicitHeaderStyle(column, headerKey);
        if (styleBlocks is not null)
            return Blocked(WidthStatus.Unmeasurable, $"chrome of <{headerKey}> is unknown: {styleBlocks}");
        if (stylePadding is { } padding) chrome = padding;

        var available = boundPx - chrome;
        return available > 0
            ? new AvailableWidth(WidthStatus.Known, available, $"{which}={boundPx:G} on <{local}>", LineOf(column), true,
                $"minus {chrome:F1} DIP of column header chrome")
            : Blocked(WidthStatus.Unmeasurable, $"header chrome ({chrome:F1}) exceeds the column width ({boundPx:F1})");
    }

    private string? HeaderRestyled(XElement column, string headerKey)
    {
        foreach (var attr in (ReadOnlySpan<string>)["HeaderStyle", "HeaderTemplate", "HeaderContainerStyle", "HeaderTemplateSelector"])
            if (HasProperty(column, attr)) return $"the column sets {attr}";

        foreach (var ancestor in column.Ancestors())
        {
            var name = ancestor.Name.LocalName;
            if (name.Contains('.', StringComparison.Ordinal)) continue;

            foreach (var attr in (ReadOnlySpan<string>)["ColumnHeaderStyle", "ColumnHeaderContainerStyle", "ColumnHeaderTemplate"])
                if (HasProperty(ancestor, attr)) return $"<{name}> sets {attr}";

            if (EndsWithAny(name, "DataGrid", "ListView")) break;
        }

        return null;
    }

    /// <summary>
    /// 專案的隱含標題樣式沒換範本時，DataGridColumnHeader 的內距算得出來。
    /// 回傳 Padding 的左右和（樣式沒設 Padding、或設成全 0 時是 null = 用表裡的值）。
    ///
    /// LayoutOracle 實測（150 寬的欄）：預設範本（DataGridHeaderBorder）的 Padding 全是 0 時自己用 5,0,5,0，
    /// 表裡的 10 就是這個；Padding 只要有一邊不是 0 就照它的左右和（4,0 → 8、9,0,0,0 → 9、1,0,30,0 → 31）。
    /// BorderThickness 不影響寬度（6,0 照樣是 10）。
    ///
    /// ⚠ 第一版只要專案有隱含標題樣式就整個放棄：QuillNest 的 14 個欄標題都是這樣，
    ///   而它們的樣式只設了顏色、字型、Padding 與底線。
    /// 換了範本、設了 Margin 或寬度，或是 GridViewColumnHeader（沒有實測）→ 不知道。
    /// </summary>
    private (double? Padding, string? Blocks) ImplicitHeaderStyle(XElement column, string headerKey)
    {
        if (!_styles.HasImplicitStyle(column, headerKey)) return (null, null);
        if (headerKey != "DataGridColumnHeader") return (null, $"the project defines an implicit Style for {headerKey}");

        if (_styles.ImplicitStyleMaySetTemplate(column, headerKey))
            return (null, $"the project's implicit Style for {headerKey} replaces its template");

        foreach (var property in (ReadOnlySpan<string>)["ContentTemplate", "ContentTemplateSelector", "Margin", "Width", "MinWidth", "MaxWidth"])
            if (_styles.ImplicitSetter(column, headerKey, property).Kind != ResolveKind.NotSet)
                return (null, $"the project's implicit Style for {headerKey} sets {property}");

        var padding = _styles.ImplicitSetter(column, headerKey, "Padding");
        if (padding.IsUnreadable) return (null, $"Padding in the project's implicit Style for {headerKey} is not statically readable");
        if (!padding.IsValue) return (null, null);

        var (l, r, ok) = ParseThickness(padding.Value);
        if (!ok) return (null, $"Padding=\"{padding.Value}\" in the project's implicit Style for {headerKey} cannot be read");
        var allZero = padding.Value!.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == 0);
        return (allZero ? null : l + r, null);
    }

    // ───────────────────────── 捲動 ─────────────────────────

    /// <summary>
    /// 明寫的 ScrollViewer 開了水平捲軸時，內容可以比視埠寬，文字不會被裁。
    /// 回傳 (true, 原因) = 可捲動；(null, 原因) = 讀不出來；(false, _) = 不是。
    ///
    /// ⚠ ScrollViewer 的 HorizontalScrollBarVisibility 預設是 **Disabled**，所以只有明寫
    ///   Auto／Visible 才算。（ListBox／ListView 相反，預設是 Auto —— 見 <see cref="ListItemBinding"/>。）
    /// </summary>
    private (bool? Scrolls, string Why) HorizontalScroll(XElement el)
    {
        var local = el.Name.LocalName;
        if (!local.EndsWith("ScrollViewer", StringComparison.Ordinal)) return (false, "");

        var v = _styles.ResolveEx(el, "HorizontalScrollBarVisibility", inheritable: false);
        if (v.IsUnreadable)
            return (null, $"HorizontalScrollBarVisibility of <{local}> is not statically readable");

        return v.IsValue && IsOneOf(v.Value, "Auto", "Visible")
            ? (true, $"<{local}> scrolls horizontally; text is not clipped")
            : (false, "");
    }

    private readonly record struct ListResult(double ScrollBar, string? Open, string? Blocked);

    /// <summary>
    /// ListBox／ListView／TreeView 的項目受不受清單寬度約束。
    ///
    /// ⚠ **預設不受。** WPF 實測：它們預設的水平捲軸可見性是 <c>Auto</c>，
    ///   項目內容 500 寬於清單 200 時出現水平捲軸（ExtentWidth 510、ViewportWidth 196），
    ///   **不是被裁**。只有明寫 <c>Disabled</c>／<c>Hidden</c> 時清單寬度才是硬上界。
    ///   第一版把清單寬度一律當硬上界，等於把「會出現捲軸」報成「會溢出」。
    ///
    /// 清單寬度有效時，垂直捲軸出不出現取決於執行期的項目數 —— **假設它在**
    /// （少算可用寬度只會誤報，多算會漏報），除非明寫 Disabled／Hidden。
    /// </summary>
    private ListResult ListItemBinding(XElement list)
    {
        var local = list.Name.LocalName;

        var h = _styles.ResolveEx(list, "ScrollViewer.HorizontalScrollBarVisibility", inheritable: false);
        if (h.IsUnreadable)
            return new(0, null, $"horizontal scrolling of <{local}> is not statically readable");

        if (!(h.IsValue && IsOneOf(h.Value, "Disabled", "Hidden")))
            return new(0,
                $"<{local}> scrolls horizontally by default, so items are not clipped " +
                "(set ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\" to make its width bind)", null);

        var v = _styles.ResolveEx(list, "ScrollViewer.VerticalScrollBarVisibility", inheritable: false);
        if (v.IsUnreadable)
            return new(0, null, $"vertical scrolling of <{local}> is not statically readable");

        return v.IsValue && IsOneOf(v.Value, "Disabled", "Hidden")
            ? new(0, null, null)
            : new(_chrome.ScrollBarWidth, null, null);
    }

    // ───────────────────────── 內距與 chrome ─────────────────────────

    private readonly record struct InsetResult(double Inset, bool UsedChrome, bool AssumedScrollBar, string? Blocked);

    /// <summary>
    /// 元素「內部」吃掉的寬度：Padding、BorderThickness，加上控制項範本的 chrome。
    /// **不含 Margin** —— Margin 在 Width 外面。
    ///
    /// ⚠ chrome 表量的是 **WPF 內建控制項的預設範本**。不是內建的、範本被換掉的、
    ///   樣式來自專案外的，一律是「不知道」，不是「照舊用表」也不是「當成 0」。
    /// </summary>
    private InsetResult InnerInset(XElement el, string? theme)
    {
        var local = el.Name.LocalName;
        var info = _chrome.Lookup(el.Name, theme);

        // 沒有範本的元素（面板、Border、TextBlock）：只有它自己明寫的 Padding／BorderThickness
        if (info.Kind == ChromeKind.NoTemplate)
        {
            double plain = 0;
            foreach (var attr in (ReadOnlySpan<string>)["Padding", "BorderThickness"])
            {
                var t = Thickness(el, attr);
                if (t.Blocked is { } blocked) return new(0, false, false, blocked);
                plain += t.Sum;
            }
            return new(plain, false, false, null);
        }

        // ⚠ ComboBoxItem 的 chrome 只存在於**展開的下拉清單**裡。關起來的選取框是把項目的內容
        //   直接畫在 ComboBox 的 ContentPresenter 上，那個容器的內距不在場。
        //   「會不會被裁」問的正是關起來那個框，所以這裡不扣它。
        if (local.EndsWith("ComboBoxItem", StringComparison.Ordinal)
            && el.Parent?.Name.LocalName.EndsWith("ComboBox", StringComparison.Ordinal) == true)
            return new(0, false, false, null);

        var frame = 0.0;
        if (IsWindow(el))
        {
            var f = WindowFrame(el);
            if (f.Blocked is { } frameWhy) return new(0, false, false, frameWhy);
            frame = f.Sum;
        }

        // ⚠ 標籤列／標題放在左右兩側時會吃掉內容區的寬度，而那一欄多寬取決於標題文字。
        //   這個檢查要在「從範本算」之前：範本通常靠觸發器依位置重排，靜態讀範本只會看到預設（上方）的排法。
        //   （CorpusRender 實測 Playnite 的 DatabaseFieldsManagerWindow：換了範本的 TabControl、
        //    TabStripPlacement="Left"，工具算 463、實際 294。）
        if (EndsWithAny(local, "TabControl"))
        {
            var placement = _styles.ResolveEx(el, "TabStripPlacement", inheritable: false);
            if (placement.IsUnreadable || (placement.IsValue && IsOneOf(placement.Value, "Left", "Right")))
                return new(0, false, false, "the tab strip of <TabControl> is placed beside the content");
        }

        if (EndsWithAny(local, "Expander"))
        {
            var direction = _styles.ResolveEx(el, "ExpandDirection", inheritable: false);
            if (direction.IsUnreadable || (direction.IsValue && IsOneOf(direction.Value, "Left", "Right")))
                return new(0, false, false, "the header of <Expander> is placed beside the content");
        }

        // 範本被換掉了？換成專案裡看得到的範本就從它算；看不到就是不知道
        var template = _styles.TemplateOf(el);
        switch (template.Kind)
        {
            case TemplateKind.Unknown:
                return new(0, false, false, $"chrome of <{DisplayName(el)}> is unknown: it {template.Why}");

            case TemplateKind.Custom:
            {
                var (custom, why) = AgreedTemplateChrome(el, template, header: false, () => DefaultChrome(el, info, theme));
                return custom is { } c
                    ? new(c + frame, frame > 0, false, null)
                    : new(0, false, false,
                        $"chrome of <{DisplayName(el)}> is unknown: {template.Why} replaces the ControlTemplate, and {why}");
            }
        }

        var byTable = DefaultChrome(el, info, theme);
        return byTable.Blocked is not null
            ? byTable
            : new(byTable.Inset + frame, byTable.UsedChrome || frame > 0, byTable.AssumedScrollBar, null);
    }

    /// <summary>
    /// 同一個樣式有好幾份定義時：每一份的範本都算一次內距，**全部一致才採用**。
    /// 候選裡的 null 代表「那一份沒換範本」，用預設範本的表值去比。
    /// </summary>
    private (double? Chrome, string Why) AgreedTemplateChrome(
        XElement control, TemplateInfo template, bool header, Func<InsetResult> byDefaultTemplate)
    {
        var alternatives = template.Alternatives ?? [template.Template];
        double? agreed = null;

        foreach (var alternative in alternatives)
        {
            double value;
            if (alternative is null)
            {
                var fallback = byDefaultTemplate();
                if (fallback.Blocked is { } blocked) return (null, $"one definition keeps the default template, whose {blocked}");
                value = fallback.Inset;
            }
            else
            {
                var (chrome, why) = TemplateChrome(control, alternative, header);
                if (chrome is not { } c) return (null, why);
                value = c;
            }

            if (agreed is { } a && Math.Abs(a - value) > 0.01)
                return (null, string.Create(CultureInfo.InvariantCulture,
                    $"its {alternatives.Count} definitions give different chrome ({a:F1} and {value:F1} DIP)"));
            agreed = value;
        }

        return (agreed, "");
    }

    /// <summary>預設範本的內距：查表，再依明寫的 Padding／BorderThickness 調整。</summary>
    private InsetResult DefaultChrome(XElement el, ChromeInfo info, string? theme)
    {
        var local = el.Name.LocalName;

        if (info.Kind == ChromeKind.Unknown)
            return new(0, false, false, $"chrome of <{DisplayName(el)}> is unknown: {info.Why}");

        // ── 預設範本：查表 ──
        // 表裡的數字已經含了預設的 Padding 與 BorderThickness。明寫了就換掉那一份，不是再加一份
        //（第一版是再加一份：<Label Padding="0"> 被算成還有 10 的內距）。
        // 只有實測過「一比一換算」的控制項才這樣做；其他的照舊加上去（偏保守）。
        double sum = info.Chrome;

        var padding = Thickness(el, "Padding");
        if (padding.Blocked is { } paddingWhy) return new(0, false, false, paddingWhy);
        var border = Thickness(el, "BorderThickness");
        if (border.Blocked is { } borderWhy) return new(0, false, false, borderWhy);

        if (padding.IsSet) sum += info.PaddingDelta(padding.Sum);
        if (border.IsSet) sum += info.BorderDelta(border.Sum);

        // ScrollViewer 的 chrome 就是垂直捲軸：Disabled／Hidden 時不佔空間
        if (ChromeTable.IsWpf(el, "ScrollViewer"))
        {
            var v = _styles.ResolveEx(el, "VerticalScrollBarVisibility", inheritable: false);
            if (v.IsUnreadable)
                return new(0, false, false, $"VerticalScrollBarVisibility of <{local}> is not statically readable");
            if (v.IsValue && IsOneOf(v.Value, "Disabled", "Hidden")) return new(sum - info.Chrome, false, false, null);
            return new(sum, true, true, null);
        }

        sum = Math.Max(0, sum);
        return new(sum, sum > 0, false, null);
    }

    /// <summary>給 <see cref="ElementMeasurer"/> 用：元素內部吃掉的寬度；不知道回 null。</summary>
    internal double? ContentInset(XElement el)
    {
        var inner = InnerInset(el, ThemeOf(el));
        return inner.Blocked is null ? inner.Inset : null;
    }

    /// <summary>標題區的內距（GroupBox／Expander 的 Header）。跟內容區的不一樣（LayoutOracle 實測 9 與 27）。</summary>
    private InsetResult HeaderInset(XElement el, string? theme)
    {
        var local = el.Name.LocalName;

        var template = _styles.TemplateOf(el);
        switch (template.Kind)
        {
            case TemplateKind.Unknown:
                return new(0, false, false, $"chrome of the header of <{DisplayName(el)}> is unknown: it {template.Why}");

            case TemplateKind.Custom:
            {
                var (custom, why) = AgreedTemplateChrome(el, template, header: true, () => DefaultHeader(el, theme));
                return custom is { } c
                    ? new(c, false, false, null)
                    : new(0, false, false,
                        $"chrome of the header of <{DisplayName(el)}> is unknown: {template.Why} replaces the ControlTemplate, and {why}");
            }
        }

        return DefaultHeader(el, theme);
    }

    private InsetResult DefaultHeader(XElement el, string? theme)
    {
        var local = el.Name.LocalName;

        var border = Thickness(el, "BorderThickness");
        if (border.Blocked is { } blocked) return new(0, false, false, blocked);

        if (_chrome.HeaderChrome(local, ChromeTable.IsWpf(el), theme) is not { } chrome)
            return new(0, false, false,
                $"chrome of the header of <{DisplayName(el)}> is unknown: " +
                (theme is not null && ChromeTable.IsWpf(el) ? $"the external theme {theme} restyles it" : "there is no chrome data for it"));

        return new(chrome + border.Sum, chrome > 0, false, null);
    }

    // ───────────────────────── 從專案自己的範本算內距 ─────────────────────────

    private const string WpfNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Dictionary<(XElement Template, string Signature), (double? Chrome, string Why)> _templateChrome = [];
    private int _templateDepth;

    /// <summary>
    /// 控制項換了範本時，內距表不適用 —— 但範本就在專案裡，內距可以從它算出來。
    ///
    /// 做法：把範本的內容複製出來、外面套一個已知寬度的框，然後用**同一個** <see cref="Resolve(XElement)"/>
    /// 去問「ContentPresenter 拿得到多寬」。框寬減掉它就是範本吃掉的寬度。
    /// 範本裡的 <c>{TemplateBinding Padding}</c> 換成控制項實際生效的值（沒設定就用佈景的預設值）。
    ///
    /// 用兩個不同的框寬各算一次：差額必須一樣，內距才是一個常數。不一樣（範本裡有比例分配）就放棄。
    ///
    /// ⚠ 實跑 QuillNest：130 個位置的 Button／ComboBox／RadioButton 用的是專案自己的深色範本。
    ///   第一版照樣套 Aero2 的數字（錯）；改成「不知道」之後可解析率從 96% 掉到 35%（對，但沒用）。
    ///   這個函式是第三條路：算出真的數字。
    /// </summary>
    private (double? Chrome, string Why) TemplateChrome(XElement control, XElement template, bool header)
    {
        if (_templateDepth >= 3) return (null, "that template nests other templated controls too deeply");

        var roots = template.Elements().Where(e => !e.Name.LocalName.Contains('.', StringComparison.Ordinal)).ToList();
        if (roots.Count != 1) return (null, "that template does not have exactly one root element");

        // 會把項目排進捲動區的控制項：範本裡的捲動行為不是一個「內距」說得完的
        var controlName = control.Name.LocalName;
        if (EndsWithAny(controlName, "ListBox", "ListView", "TreeView", "DataGrid", "ItemsControl"))
            return (null, "the control lays out items, which a single chrome figure cannot describe");

        // ScrollViewer 的內容宿主是 ScrollContentPresenter。換了捲軸樣式的 App 幾乎都會重寫它的範本
        //（實查 ScreenToGif：296 個位置卡在這裡）。垂直捲軸照舊假設它在。
        var isScrollViewer = controlName.EndsWith("ScrollViewer", StringComparison.Ordinal);

        // 這個控制項的「內容」是哪個屬性：ComboBox 關起來時顯示的是選中的項目，TabControl 是選中的分頁
        var contentSource = header ? "Header"
            : controlName.EndsWith("ComboBox", StringComparison.Ordinal) ? "SelectionBoxItem"
            : controlName.EndsWith("TabControl", StringComparison.Ordinal) ? "SelectedContent"
            : "Content";

        var hosts = new List<(List<int> Path, string Source)>();
        FindHosts(roots[0], [], hosts, isScrollViewer ? "ScrollContentPresenter" : "ContentPresenter");

        // 範本裡常常不只一個內容宿主（標題、浮動提示、圖示…）。要的是顯示這個控制項「內容」的那一個
        var presenters = hosts.Where(h => h.Source == contentSource).Select(h => h.Path).ToList();
        if (presenters.Count == 0 && !header && contentSource != "Content")
            presenters = hosts.Where(h => h.Source == "Content").Select(h => h.Path).ToList();

        if (presenters.Count != 1)
            return (null, presenters.Count == 0
                ? $"that template has no element that hosts the {contentSource}"
                : $"that template has {presenters.Count} elements that host the {contentSource}");

        var clone = new XElement(roots[0]);
        var presenter = clone;
        foreach (var index in presenters[0]) presenter = presenter.Elements().ElementAt(index);

        // 範本自己在內容上設了字型 → 我們量文字用的字型就不對了
        for (XElement? e = presenter; e is not null; e = e.Parent)
            foreach (var a in e.Attributes())
            {
                var name = a.Name.LocalName;
                var property = name[(name.LastIndexOf('.') + 1)..];
                if (property is "FontSize" or "FontFamily" or "FontWeight" or "FontStretch"
                    && TemplateBindingProperty(a.Value) != property)
                    return (null, $"that template sets {property} on the content");
            }

        // {TemplateBinding X} → 控制項實際生效的 X
        var signature = new System.Text.StringBuilder();
        var defaults = ThemeOf(control) is null ? _chrome.DefaultsOf(control.Name) : null;
        foreach (var e in clone.DescendantsAndSelf())
        {
            foreach (var a in e.Attributes().ToList())
            {
                if (TemplateBindingProperty(a.Value) is not { } property) continue;

                // ScrollViewer 算出來的捲軸可見性：垂直的照控制項的設定（沒關掉就假設它在）；
                // 水平的不佔寬度
                if (isScrollViewer && property is "ComputedVerticalScrollBarVisibility" or "ComputedHorizontalScrollBarVisibility")
                {
                    var hidden = property == "ComputedHorizontalScrollBarVisibility";
                    if (!hidden)
                    {
                        var bar = _styles.ResolveEx(control, "VerticalScrollBarVisibility", inheritable: false);
                        if (bar.IsUnreadable) continue;
                        hidden = bar.IsValue && IsOneOf(bar.Value, "Disabled", "Hidden");
                    }
                    a.Value = hidden ? "Collapsed" : "Visible";
                    signature.Append(property).Append('=').Append(a.Value).Append(';');
                    continue;
                }

                var r = _styles.ResolveEx(control, property, inheritable: false);
                if (r.IsUnreadable) continue;   // 留著：用到它的地方會變成讀不出來

                string? value = r.IsValue ? r.Value : property switch
                {
                    // 沒人設定：拿到的是佈景預設樣式的值。只知道左右合計，平分到兩邊（只影響水平）
                    "Padding" when defaults is { } d => HalfAndHalf(d.Padding),
                    "BorderThickness" when defaults is { } d => HalfAndHalf(d.Border),
                    // 專案自己定義的控制項型別（clr-namespace 而且沒有指到別的組件）：
                    // 它的預設樣式就在專案裡、我們已經讀過了，沒設定就是相依屬性的預設值 0
                    "Padding" or "BorderThickness" when IsProjectType(control) => "0",
                    "Padding" or "BorderThickness" => null,
                    // 其他屬性（Background、HorizontalContentAlignment、Width…）沒設定就是沒有
                    _ => "",
                };

                if (value is null) continue;                 // 預設值不知道：留著，變成讀不出來
                if (value.Length == 0) a.Remove(); else a.Value = value;
                signature.Append(property).Append('=').Append(value).Append(';');
            }
        }

        var key = (template, signature.Append(header ? "H" : "C").ToString());
        if (_templateChrome.TryGetValue(key, out var cached)) return cached;

        (double?, string) result;
        _templateDepth++;
        try
        {
            var frame = new XElement(XName.Get("Border", WpfNamespace), new XAttribute("Width", "10000"), clone);
            var doc = new XDocument(frame);
            if (control.Document?.Annotation<SourceFile>() is { } file) doc.AddAnnotation(file);
            if (control.Document?.Annotation<ExternalTheme>() is { } theme) doc.AddAnnotation(theme);

            var first = Resolve(presenter, 2);
            frame.SetAttributeValue("Width", "20000");
            var second = Resolve(presenter, 2);

            if (first.Status != WidthStatus.Known || second.Status != WidthStatus.Known)
            {
                var failed = first.Status != WidthStatus.Known ? first : second;
                result = (null, $"the content width inside that template could not be resolved ({failed.Reason})");
            }
            else if (Math.Abs((10000 - first.Px) - (20000 - second.Px)) > 0.01)
                result = (null, "that template does not give the content a fixed share of the width");
            else
                result = (Math.Max(0, 10000 - first.Px), "");
        }
        finally { _templateDepth--; }

        _templateChrome[key] = result;
        return result;
    }

    private static bool IsProjectType(XElement el)
    {
        var ns = el.Name.NamespaceName;
        return ns.StartsWith("clr-namespace:", StringComparison.Ordinal)
               && !ns.Contains(";assembly=", StringComparison.Ordinal);
    }

    private static string HalfAndHalf(double total) =>
        string.Create(CultureInfo.InvariantCulture, $"{total / 2:R},0,{total / 2:R},0");

    /// <summary>
    /// 範本裡的內容宿主，以及每一個顯示的是控制項的哪個屬性。不進巢狀的範本、樣式、資源與快顯。
    ///
    /// 宿主不一定是 ContentPresenter：控制項庫常用自己的包裝
    /// （<c>&lt;materialDesign:Ripple Content="{TemplateBinding Content}"/&gt;</c>、<c>mah:ContentControlEx</c>）。
    /// 任何把控制項的屬性綁到自己 <c>Content</c> 上的元素都算 —— 它自己的內距由解析器照常去算。
    /// </summary>
    private static void FindHosts(
        XElement e, List<int> path, List<(List<int> Path, string Source)> found, string presenterName)
    {
        var local = e.Name.LocalName;
        if (TemplateHosts.Contains(local) || local.EndsWith("Popup", StringComparison.Ordinal)
            || local.EndsWith(".Resources", StringComparison.Ordinal) || local.EndsWith(".Triggers", StringComparison.Ordinal))
            return;

        var bound = TemplateBindingProperty(e.Attribute("Content")?.Value ?? "");

        if (local == presenterName)
        {
            found.Add(([.. path], e.Attribute("ContentSource")?.Value.Trim() ?? bound ?? "Content"));
            return;
        }

        if (bound is not null && !local.Contains('.', StringComparison.Ordinal))
        {
            found.Add(([.. path], bound));
            return;
        }

        var i = 0;
        foreach (var child in e.Elements())
        {
            path.Add(i++);
            FindHosts(child, path, found, presenterName);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary><c>{TemplateBinding X}</c> 或 <c>{Binding X, RelativeSource={RelativeSource TemplatedParent}}</c> 的 X。</summary>
    private static string? TemplateBindingProperty(string raw)
    {
        var t = raw.Trim();
        if (!t.StartsWith('{') || !t.EndsWith('}')) return null;
        var inner = t[1..^1].Trim();

        string? property = null;
        if (inner.StartsWith("TemplateBinding", StringComparison.Ordinal))
            property = inner["TemplateBinding".Length..].Trim();
        else if (inner.StartsWith("Binding", StringComparison.Ordinal)
                 && inner.Contains("TemplatedParent", StringComparison.Ordinal))
        {
            var rest = inner["Binding".Length..].Trim();
            var comma = rest.IndexOf(',');
            property = (comma >= 0 ? rest[..comma] : rest).Trim();
            if (property.StartsWith("RelativeSource", StringComparison.Ordinal)) return null;
        }

        if (property is null) return null;
        if (property.StartsWith("Path=", StringComparison.Ordinal)) property = property["Path=".Length..].Trim();
        if (property.StartsWith("Property=", StringComparison.Ordinal)) property = property["Property=".Length..].Trim();
        property = property[(property.LastIndexOf('.') + 1)..];
        return property.Length > 0 && property.All(c => char.IsLetterOrDigit(c) || c == '_') ? property : null;
    }

    /// <summary>
    /// 視窗邊框。<c>Window.Width</c> 是**外框**寬，工作區比較窄（LayoutOracle 實測，見 control-chrome.json）。
    /// 第一版完全沒扣，等於每一個以視窗寬度為上界的位置都高估了 16 DIP。
    /// </summary>
    private ThicknessResult WindowFrame(XElement window)
    {
        // WindowChrome：內容畫滿整個視窗。
        // ⚠ 常常是寫在**樣式**裡（實查 Playnite 的 StandardWindowStyle），不只看元素本身。
        //   Setter 的值是一個物件（<Setter.Value><WindowChrome …/>），讀出來是「讀不出來」——
        //   那仍然代表有設定，而 WindowChrome 一設就沒有標準邊框。
        if (window.Attributes().Any(a => a.Name.LocalName.EndsWith("WindowChrome.WindowChrome", StringComparison.Ordinal))
            || window.Elements().Any(e => e.Name.LocalName.EndsWith("WindowChrome.WindowChrome", StringComparison.Ordinal))
            || _styles.ResolveEx(window, "WindowChrome.WindowChrome", inheritable: false).Kind != ResolveKind.NotSet)
            return new(0, null);

        var style = _styles.ResolveEx(window, "WindowStyle", inheritable: false);
        var resize = _styles.ResolveEx(window, "ResizeMode", inheritable: false);
        var transparent = _styles.ResolveEx(window, "AllowsTransparency", inheritable: false);

        if (style.IsUnreadable || resize.IsUnreadable || transparent.IsUnreadable)
            return new(0, "the frame of <Window> is unknown: WindowStyle, ResizeMode or AllowsTransparency is not statically readable");

        if (transparent.IsValue && IsOneOf(transparent.Value, "True")) return new(0, null);

        if (style.IsValue && IsOneOf(style.Value, "None"))
        {
            // 無邊框但可調整大小的視窗仍然有調整大小用的框
            var resizable = !resize.IsValue || IsOneOf(resize.Value, "CanResize", "CanResizeWithGrip");
            return new(resizable ? _chrome.WindowFrame : 0, null);
        }

        return style.IsValue && IsOneOf(style.Value, "ThreeDBorderWindow")
            ? new(_chrome.WindowFrameThreeD, null)
            : new(_chrome.WindowFrame, null);
    }

    /// <summary>
    /// 這個元素是不是一個 WPF 視窗（會有作業系統畫的邊框）：WPF 的 <c>Window</c>，
    /// 或檔案的根元素是名字以 Window 結尾、帶 <c>x:Class</c> 的子類別。
    ///
    /// ⚠ 實查 ScreenToGif：每個視窗都是專案自訂的 <c>&lt;n:ExWindow x:Class=…&gt;</c>，繼承自 Window、
    ///   沒有用 WindowChrome。第一版只對 WPF 內建的 Window 扣邊框，於是每個以視窗寬為上界的位置都多算 16 DIP。
    /// </summary>
    private static bool IsWindow(XElement el) =>
        ChromeTable.IsWpf(el, "Window")
        || (el.Parent is null && el.Name.LocalName.EndsWith("Window", StringComparison.Ordinal)
            && el.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml")) is not null);

    /// <summary>給訊息用的名稱：第三方元素帶上命名空間前綴（ui:Card），使用者才找得到。</summary>
    private static string DisplayName(XElement el)
    {
        var prefix = el.GetPrefixOfNamespace(el.Name.Namespace);
        return string.IsNullOrEmpty(prefix) ? el.Name.LocalName : $"{prefix}:{el.Name.LocalName}";
    }

    private readonly record struct ThicknessResult(double Sum, string? Blocked, bool IsSet = false);

    /// <summary>
    /// 左右兩邊加起來。
    /// ⚠ 讀不出來時**不能當成 0**。第一版這樣做還註明「偏保守」—— 方向寫反了：
    ///   少扣內距 = 高估可用寬度 = 漏報溢出。
    /// </summary>
    private ThicknessResult Thickness(XElement el, string property)
    {
        var r = _styles.ResolveEx(el, property, inheritable: false);
        if (r.IsUnreadable)
            return new(0, $"{property} of <{el.Name.LocalName}> is not statically readable ({r.Why})");
        if (!r.IsValue) return new(0, null);

        var (l, rr, ok) = ParseThickness(r.Value);
        return ok
            ? new(l + rr, null, IsSet: true)
            : new(0, $"{property}=\"{r.Value}\" on <{el.Name.LocalName}> is not a thickness this tool can read");
    }

    /// <summary>Thickness 的三種寫法：「全」「左右,上下」「左,上,右,下」。</summary>
    internal static (double Left, double Right, bool Ok) ParseThickness(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return (0, 0, true);

        var parts = v.Trim().Split([',', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var n = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || !double.IsFinite(n[i]))
                return (0, 0, false);

        return parts.Length switch
        {
            1 => (n[0], n[0], true),
            2 => (n[0], n[0], true),
            4 => (n[0], n[2], true),
            _ => (0, 0, false),
        };
    }

    // ───────────────────────── Grid ─────────────────────────

    private (GridSolveResult Result, AvailableWidth? Own) TrySolveGrid(XElement grid, XElement child, int depth)
    {
        if (_measurer is null) return (new GridSolveResult(null, "Auto/* Grid column (no measurer available)"), null);

        var own = Resolve(grid, depth + 1);
        if (own.Status != WidthStatus.Known)
            return (new GridSolveResult(null,
                own.Status == WidthStatus.NoDeclaredLimit
                    ? "Auto/* Grid column whose own width is NoDeclaredLimit"
                    // 把根因帶出來：「Grid 自己的寬度算不出來」對使用者沒有用，他要知道是哪個東西擋住的
                    : own.Reason), null);

        var result = new GridSolver(_measurer, _styles).Solve(grid, child, own.Px, depth);
        return result.Width is null
            ? (new GridSolveResult(null, $"Auto/* Grid column: {result.Reason}"), null)
            : (result, own);
    }

    private static bool IsGrid(string name) =>
        name.EndsWith("Grid", StringComparison.Ordinal)
        && !name.EndsWith("DataGrid", StringComparison.Ordinal)
        && !name.EndsWith("UniformGrid", StringComparison.Ordinal)
        && !name.EndsWith("PropertyGrid", StringComparison.Ordinal);

    private enum GridState { Fixed, Flexible, NoColumns }

    /// <summary>
    /// 子元素所在的欄是不是純固定像素。
    ///
    /// ⚠ 任何說不準的情形都回 Flexible（交給求解器，解不開就是 NeedsGrid）——
    ///   **不要回「不知道所以略過」**，略過等於當這個 Grid 不存在、直接用外層更寬的上界。
    ///   只有「完全沒有欄定義」才真的可以略過（那是單一欄，寬度就是 Grid 自己的寬）。
    /// </summary>
    private (GridState State, string Why, double Width) GridColumn(XElement grid, XElement child)
    {
        var defs = ColumnWidths(grid);
        if (defs.Count == 0) return (GridState.NoColumns, "no ColumnDefinitions", 0);

        var col = 0;
        if (child.Attribute("Grid.Column")?.Value is { } colAttr
            && !int.TryParse(colAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out col))
            return (GridState.Flexible, "non-numeric Grid.Column", 0);

        var span = 1;
        if (child.Attribute("Grid.ColumnSpan")?.Value is { } sp
            && !int.TryParse(sp, NumberStyles.Integer, CultureInfo.InvariantCulture, out span))
            return (GridState.Flexible, "non-numeric Grid.ColumnSpan", 0);

        // WPF 會把超出範圍的欄號夾到最後一欄
        col = Math.Clamp(col, 0, defs.Count - 1);
        span = Math.Max(1, span);

        double total = 0;
        for (var i = col; i < col + span && i < defs.Count; i++)
        {
            if (defs[i] is not { } w) return (GridState.Flexible, $"column {i} is not a plain fixed width", 0);
            total += w;
        }

        return total > 0 ? (GridState.Fixed, $"column {col}", total) : (GridState.Flexible, "zero-width column", 0);
    }

    /// <summary>每一欄的固定寬度；Auto／星號／帶 Min/Max 限制的是 null（交給求解器）。</summary>
    private List<double?> ColumnWidths(XElement grid)
    {
        var result = new List<double?>();

        if (grid.Attribute("ColumnDefinitions")?.Value is { Length: > 0 } shorthand)
        {
            foreach (var part in shorthand.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                result.Add(TryLength(part, out var w) ? w : null);
            return result;
        }

        var holder = grid.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith(".ColumnDefinitions", StringComparison.Ordinal));
        if (holder is null) return result;

        foreach (var cd in holder.Elements().Where(e => e.Name.LocalName == "ColumnDefinition"))
        {
            var w = _styles.AttributeValue(cd, "Width");
            var constrained = cd.Attribute("MinWidth") is not null || cd.Attribute("MaxWidth") is not null
                              || cd.Attribute("SharedSizeGroup") is not null || cd.Attribute("Style") is not null;
            // ⚠ Width 省略時預設是 "*"（彈性），不是 Auto，也不是 0
            result.Add(!constrained && w.IsValue && TryLength(w.Value, out var px) ? px : null);
        }

        return result;
    }

    /// <summary>
    /// UniformGrid 的欄數（LayoutOracle 實測）：有 <c>Columns</c> 就用它；只有 <c>Rows</c> 時是
    /// ⌈子元素數 ÷ Rows⌉；都沒有時是 ⌈√子元素數⌉。算不出來回 null。
    /// </summary>
    private int? UniformColumns(XElement grid)
    {
        if (grid.Attribute("FirstColumn") is not null) return null;

        var columns = _styles.ResolveEx(grid, "Columns", inheritable: false);
        if (columns.IsUnreadable) return null;
        if (columns.IsValue)
            return int.TryParse(columns.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && c > 0 ? c : null;

        var children = LayoutChildren(grid);
        // 折疊的子元素不佔格子，而 Visibility 常常是綁定的
        if (children.Count == 0 || children.Any(c => c.Attribute("Visibility") is not null)) return null;

        var rows = _styles.ResolveEx(grid, "Rows", inheritable: false);
        if (rows.IsUnreadable) return null;
        if (rows.IsValue)
            return int.TryParse(rows.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r > 0
                ? (int)Math.Ceiling(children.Count / (double)r)
                : null;

        return (int)Math.Ceiling(Math.Sqrt(children.Count));
    }

    // ───────────────────────── 小工具 ─────────────────────────

    internal static bool TryLength(string? v, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(v)) return false;
        v = v.Trim();
        if (v.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return false;
        if (v.Equals("NaN", StringComparison.OrdinalIgnoreCase)) return false;   // Width="NaN" 就是 Auto
        if (v.EndsWith('*')) return false;
        if (v.StartsWith('{')) return false;
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value) && value > 0;
    }

    internal static bool IsOneOf(string? v, params string[] options)
    {
        if (v is null) return false;
        foreach (var o in options)
            if (v.Trim().Equals(o, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>後綴比對：MahApps 的 MetroTabItem 要對得上 TabItem。
    /// ⚠ 只用來判斷**行為類別**（它是分頁標籤、是清單）。內距的數字不能這樣套 —— 見 ChromeTable。</summary>
    internal static bool EndsWithAny(string name, params string[] suffixes)
    {
        foreach (var s in suffixes)
            if (name.EndsWith(s, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>⚠ IXmlLineInfo.HasLineInfo 是方法不是屬性，不能用屬性模式比對。</summary>
    internal static int LineOf(XObject o) =>
        o is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
}
