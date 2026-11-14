// Playnite 的 GridEx 的等效實作，只為了讓 CorpusRender 畫得出 Playnite 的視窗。
// 行為照 Playnite 的 source/Playnite/Controls/GridEx.cs（JosefNemec/Playnite，MIT）：
//   ColumnCount／RowCount 產生那麼多欄／列，全部 Auto；StarColumns／StarRows 是逗號分隔的索引，那幾個變成 *；
//   AutoLayoutColumns 每次量版面都把子元素依序放進格子（蓋掉子元素自己的 Grid.Column／Row），ColumnSpan 照算。
// 用途：讓真的 WPF 去檢驗 XamlFit 的 gridPanels 規則。拋棄式，不是 XamlFit 的一部分。
using System.Windows;
using System.Windows.Controls;

namespace CorpusRender;

public class GridEx : Grid
{
    public static readonly DependencyProperty RowCountProperty = DependencyProperty.Register(
        nameof(RowCount), typeof(int), typeof(GridEx), new PropertyMetadata(-1, (d, e) => ((GridEx)d).Rebuild()));
    public static readonly DependencyProperty ColumnCountProperty = DependencyProperty.Register(
        nameof(ColumnCount), typeof(int), typeof(GridEx), new PropertyMetadata(-1, (d, e) => ((GridEx)d).Rebuild()));
    public static readonly DependencyProperty StarRowsProperty = DependencyProperty.Register(
        nameof(StarRows), typeof(string), typeof(GridEx), new PropertyMetadata("", (d, e) => ((GridEx)d).Rebuild()));
    public static readonly DependencyProperty StarColumnsProperty = DependencyProperty.Register(
        nameof(StarColumns), typeof(string), typeof(GridEx), new PropertyMetadata("", (d, e) => ((GridEx)d).Rebuild()));
    public static readonly DependencyProperty AutoLayoutColumnsProperty = DependencyProperty.Register(
        nameof(AutoLayoutColumns), typeof(int), typeof(GridEx), new PropertyMetadata(-1));

    public int RowCount { get => (int)GetValue(RowCountProperty); set => SetValue(RowCountProperty, value); }
    public int ColumnCount { get => (int)GetValue(ColumnCountProperty); set => SetValue(ColumnCountProperty, value); }
    public string StarRows { get => (string)GetValue(StarRowsProperty); set => SetValue(StarRowsProperty, value); }
    public string StarColumns { get => (string)GetValue(StarColumnsProperty); set => SetValue(StarColumnsProperty, value); }
    public int AutoLayoutColumns { get => (int)GetValue(AutoLayoutColumnsProperty); set => SetValue(AutoLayoutColumnsProperty, value); }

    private void Rebuild()
    {
        if (RowCount >= 0 && RowDefinitions.Count != RowCount)
        {
            RowDefinitions.Clear();
            for (var i = 0; i < RowCount; i++) RowDefinitions.Add(new RowDefinition());
        }
        if (ColumnCount >= 0 && ColumnDefinitions.Count != ColumnCount)
        {
            ColumnDefinitions.Clear();
            for (var i = 0; i < ColumnCount; i++) ColumnDefinitions.Add(new ColumnDefinition());
        }
        var starRows = (StarRows ?? "").Split(',');
        for (var i = 0; i < RowDefinitions.Count; i++)
            RowDefinitions[i].Height = starRows.Contains(i.ToString()) ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        var starColumns = (StarColumns ?? "").Split(',');
        for (var i = 0; i < ColumnDefinitions.Count; i++)
            ColumnDefinitions[i].Width = starColumns.Contains(i.ToString()) ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var columns = AutoLayoutColumns;
        if (columns > 0)
        {
            var index = 0;
            foreach (UIElement child in Children)
            {
                var span = GetColumnSpan(child);
                SetColumn(child, index % columns);
                SetRow(child, index / columns);
                index += span > 0 ? span : 1;
            }
        }
        return base.MeasureOverride(constraint);
    }
}
