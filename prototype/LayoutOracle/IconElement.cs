using System.Windows;

namespace OracleTypes;

/// <summary>附加屬性剛好跟元素自己的屬性同名（HandyControl 的 IconElement、TitleElement 的形狀）。給「attached setter:」案例用。</summary>
public static class IconElement
{
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.RegisterAttached("Width", typeof(double), typeof(IconElement), new PropertyMetadata(double.NaN));
    public static void SetWidth(DependencyObject o, double v) => o.SetValue(WidthProperty, v);
    public static double GetWidth(DependencyObject o) => (double)o.GetValue(WidthProperty);

    public static readonly DependencyProperty MarginProperty =
        DependencyProperty.RegisterAttached("Margin", typeof(Thickness), typeof(IconElement), new PropertyMetadata(default(Thickness)));
    public static void SetMargin(DependencyObject o, Thickness v) => o.SetValue(MarginProperty, v);
    public static Thickness GetMargin(DependencyObject o) => (Thickness)o.GetValue(MarginProperty);

    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.RegisterAttached("Padding", typeof(Thickness), typeof(IconElement), new PropertyMetadata(default(Thickness)));
    public static void SetPadding(DependencyObject o, Thickness v) => o.SetValue(PaddingProperty, v);
    public static Thickness GetPadding(DependencyObject o) => (Thickness)o.GetValue(PaddingProperty);
}
