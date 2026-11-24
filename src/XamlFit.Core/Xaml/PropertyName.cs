namespace XamlFit.Core.Xaml;

/// <summary>
/// 樣式的 <c>&lt;Setter Property="…"&gt;</c> 與 <c>{TemplateBinding …}</c> 裡寫的屬性名字 → 查詢用的名字。
///
/// 「擁有者.名字」有兩種意思：
///   - <c>Control.FontSize</c>、<c>FrameworkElement.Width</c>：元素自己的屬性，擁有者只是寫法 → <c>FontSize</c>、<c>Width</c>。
///   - <c>hc:IconElement.Width</c>、<c>TextOptions.TextFormattingMode</c>：**附加屬性**，跟元素自己的 Width 沒有關係
///     → 擁有者要留著（前綴去掉）。
///
/// ⚠ 0.12.0 以前一律取最後一段：HandyControl 的 <c>&lt;Setter Property="hc:IconElement.Width" Value="12"/&gt;</c>
///   （圖示的寬度）變成「這個元素寬 12」，裡面的文字被報成溢出。反方向更糟：
///   <c>hc:TitleElement.Padding</c> 蓋過真正的 Padding，會把可用寬度算大。
/// </summary>
internal static class PropertyName
{
    /// <summary>
    /// 繼承自 UIElement／ContentElement 的 WPF 型別（PresentationFramework 公開的全部，反射列出）。
    /// 用它們當擁有者時，WPF 會沿基底型別找到 FrameworkElement.Width 這種共用的屬性，所以前綴只是寫法；
    /// 跨型別共用的也是同一個 DependencyProperty（AddOwner）：TextElement／TextBlock／Control 的 Font*、
    /// Border／Control 的 BorderThickness、ScrollViewer／TextBoxBase／DataGrid 的 *ScrollBarVisibility。
    ///
    /// 只比名字不比命名空間：HandyControl 的 <c>hc:ScrollViewer</c>、專案自己的 <c>ui:TextBox</c> 是同名的衍生類別。
    /// 不列 ColumnDefinition、TableColumn、Figure、Floater：它們的 Width 是自己註冊的另一個屬性
    /// （樣式的 TargetType 就是它時仍然會去掉，見 <see cref="Normalize"/>）。
    /// </summary>
    private static readonly HashSet<string> ElementTypes = new(
        """
        AccessText AdornedElementPlaceholder Adorner AdornerDecorator AdornerLayer AnchoredBlock Block BlockUIContainer Bold
        Border BulletDecorator Button ButtonBase Calendar CalendarButton CalendarDayButton CalendarItem Canvas CheckBox
        ComboBox ComboBoxItem ContentControl ContentElement ContentPresenter ContextMenu Control DataGrid DataGridCell
        DataGridCellsPanel DataGridCellsPresenter DataGridColumnHeader DataGridColumnHeadersPresenter DataGridDetailsPresenter
        DataGridRow DataGridRowHeader DataGridRowsPresenter DatePicker DatePickerTextBox Decorator DockPanel DocumentPageView
        DocumentReference DocumentViewer DocumentViewerBase Ellipse Expander FixedDocument FixedDocumentSequence FixedPage
        FlowDocument FlowDocumentPageViewer FlowDocumentReader FlowDocumentScrollViewer Frame FrameworkContentElement
        FrameworkElement Glyphs Grid GridSplitter GridViewColumnHeader GridViewHeaderRowPresenter GridViewRowPresenter
        GridViewRowPresenterBase GroupBox GroupItem HeaderedContentControl HeaderedItemsControl Hyperlink Image InkCanvas
        InkPresenter Inline InlineUIContainer Italic ItemsControl ItemsPresenter Label Line LineBreak List ListBox ListBoxItem
        ListItem ListView ListViewItem MediaElement Menu MenuBase MenuItem MultiSelector NavigationWindow Page PageContent
        PageFunctionBase Panel Paragraph PasswordBox Path Polygon Polyline Popup ProgressBar RadioButton RangeBase Rectangle
        RepeatButton ResizeGrip RichTextBox Run ScrollBar ScrollContentPresenter ScrollViewer Section SelectiveScrollingGrid
        Selector Separator Shape Slider Span StackPanel StatusBar StatusBarItem StickyNoteControl TabControl TabItem Table
        TableCell TableRow TableRowGroup TabPanel TextBlock TextBox TextBoxBase TextElement Thumb TickBar ToggleButton ToolBar
        ToolBarOverflowPanel ToolBarPanel ToolBarTray ToolTip Track TreeView TreeViewItem UIElement Underline UniformGrid
        UserControl Viewbox Viewport3D VirtualizingPanel VirtualizingStackPanel WebBrowser Window WrapPanel
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    /// <summary>擁有者是元素型別、但仍然是附加屬性的（面板定義給子元素用的位置）。</summary>
    private static readonly HashSet<string> AttachedByElements = new(StringComparer.Ordinal)
    {
        "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan", "Grid.IsSharedSizeScope",
        "Canvas.Left", "Canvas.Top", "Canvas.Right", "Canvas.Bottom",
        "InkCanvas.Left", "InkCanvas.Top", "InkCanvas.Right", "InkCanvas.Bottom",
        "FixedPage.Left", "FixedPage.Top", "FixedPage.Right", "FixedPage.Bottom",
        "DockPanel.Dock", "ToolBar.OverflowMode", "SelectiveScrollingGrid.SelectiveScrollingOrientation",
    };

    /// <param name="property">原始寫法：<c>Width</c>、<c>Control.FontSize</c>、<c>hc:IconElement.Width</c>。</param>
    /// <param name="selfType">這個屬性設在哪個型別上（樣式的 TargetType、範本的控制項）的區域名稱；不知道就 null。
    /// 擁有者就是它的（TargetType="local:Card" 的 <c>local:Card.Width</c>）是它自己的屬性。</param>
    internal static string Normalize(string property, string? selfType = null)
    {
        var t = property.Trim();
        var dot = t.LastIndexOf('.');
        if (dot < 0) return t;

        var name = t[(dot + 1)..];
        var owner = t[..dot];
        var colon = owner.LastIndexOf(':');
        if (colon >= 0) owner = owner[(colon + 1)..];
        var full = owner + "." + name;

        if (AttachedByElements.Contains(full)) return full;
        if (owner == selfType || ElementTypes.Contains(owner)) return name;

        // MahApps 的大小寫：附加屬性 ControlsHelper.X 與 ContentControlEx 自己的 X 是同一件事（見 StyleIndex.CasingOf）
        return full == "ControlsHelper." + StyleIndex.CasingSetter ? name : full;
    }
}
