using XamlFit.Core.Analysis;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 回報 3（ZeroCel，0.9.0 開 assumeWindowMinWidth）：
/// §1 分割檢視：右邊停靠的 ZoomText 在 XAML 裡沒寫字（程式填「25%」）。當成空字串量是 0 寬，旁邊的按鈕多拿到空間 ——
///    實機「實際大小」被切、工具說裝得下。現在它的寬度不知道；同一排的位置只知道上界，連上界都放不下的照樣報（「鏡像檢視」）。
/// §2 批次匯出：「跟隨各 .zcel 內設定」與「輸入框 + 1–60，僅整數」綁同一個屬性、轉換器相反，不會同時出現；
///    宣告 visibilityConverters 之後，算其中一個的寬度不扣另一個。
/// Mono 字級 10：每個字 5 px。
/// </summary>
public class Report3Tests
{
    [Fact]
    public void A_named_text_element_with_no_text_is_filled_by_code()
    {
        var w = In("<DockPanel Width=\"200\"><TextBlock x:Name=\"Zoom\" DockPanel.Dock=\"Right\" Margin=\"6,0,0,0\"/><TextBlock x:Name=\"probe\"/></DockPanel>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Equal(200 - 6, w.UpperBoundPx!.Value, 3);   // 只扣它確定的 Margin
    }

    [Fact]
    public void An_unnamed_empty_text_element_is_really_empty()
    {
        var w = In("<DockPanel Width=\"200\"><TextBlock DockPanel.Dock=\"Right\" Margin=\"6,0,0,0\"/><TextBlock x:Name=\"probe\"/></DockPanel>");
        Assert.Equal(WidthStatus.Known, w.Status);
        Assert.Equal(194, w.Px, 3);
    }

    [Fact]
    public void A_bound_neighbour_gives_an_upper_bound_from_its_margin_and_min_width()
    {
        var w = In("<StackPanel Orientation=\"Horizontal\" Width=\"200\"><TextBlock Text=\"{Binding Name}\" MinWidth=\"40\" Margin=\"0,0,10,0\"/><TextBlock x:Name=\"probe\"/></StackPanel>");
        Assert.Equal(WidthStatus.Unmeasurable, w.Status);
        Assert.Equal(200 - 50, w.UpperBoundPx!.Value, 3);
    }

    private const string Exclusive =
        "<StackPanel Orientation=\"Horizontal\" Width=\"300\">" +
        "<StackPanel.Resources><BooleanToVisibilityConverter x:Key=\"Show\"/></StackPanel.Resources>" +
        "<TextBlock Text=\"{0}\" Visibility=\"{{Binding UseOverride, Converter={{StaticResource Hide}}}}\"/>" +
        "<StackPanel Orientation=\"Horizontal\" Visibility=\"{{Binding UseOverride, Converter={{StaticResource Show}}}}\"><TextBlock x:Name=\"probe\"/></StackPanel>" +
        "</StackPanel>";

    [Fact]
    public void A_sibling_shown_under_the_opposite_condition_is_not_deducted()
    {
        var xaml = string.Format(System.Globalization.CultureInfo.InvariantCulture, Exclusive, new string('A', 20));
        // 沒宣告 Hide 的意思：兩個都當成看得見（坑 79），扣掉 20 × 5 = 100
        Assert.Equal(200, In(xaml).Px, 3);
        // 宣告了：Hide 是 false 時看得見、Show（WPF 內建的 BooleanToVisibilityConverter）是 true 時 —— 不會同時出現
        Assert.Equal(300, Resolve($"<Grid {Ns}>{xaml}</Grid>", visibilityConverters: new Dictionary<string, bool> { ["Hide"] = false }).Px, 3);
    }

    [Fact]
    public void The_same_condition_on_both_is_not_exclusive()
    {
        var xaml = string.Format(System.Globalization.CultureInfo.InvariantCulture, Exclusive, new string('A', 20)).Replace("StaticResource Hide", "StaticResource Show");
        Assert.Equal(200, Resolve($"<Grid {Ns}>{xaml}</Grid>").Px, 3);
    }

    [Fact]
    public void A_converter_parameter_or_another_source_is_not_trusted()
    {
        var withParameter = string.Format(System.Globalization.CultureInfo.InvariantCulture, Exclusive, new string('A', 20))
            .Replace("Converter={StaticResource Hide}}", "Converter={StaticResource Hide}, ConverterParameter=x}");
        Assert.Equal(200, Resolve($"<Grid {Ns}>{withParameter}</Grid>", visibilityConverters: new Dictionary<string, bool> { ["Hide"] = false }).Px, 3);
        var otherPath = string.Format(System.Globalization.CultureInfo.InvariantCulture, Exclusive, new string('A', 20))
            .Replace("{Binding UseOverride, Converter={StaticResource Hide}}", "{Binding Other, Converter={StaticResource Hide}}");
        Assert.Equal(200, Resolve($"<Grid {Ns}>{otherPath}</Grid>", visibilityConverters: new Dictionary<string, bool> { ["Hide"] = false }).Px, 3);
    }

    [Fact]
    public void The_budget_says_the_whole_row_not_the_ones_before_it()
    {
        // 第一個元素前面什麼都沒有，budget 照樣是 0：說「前面的」讓人看不懂（回報 3 §1）
        var w = In("<StackPanel Orientation=\"Horizontal\" Width=\"50\"><TextBlock x:Name=\"probe\"/><TextBlock Text=\"" + new string('A', 20) + "\"/></StackPanel>");
        Assert.Equal(0, w.Px, 3);
        Assert.Contains("other controls in the same row", w.Reason);
    }

    private static CheckResult Check(string body)
    {
        var dir = TempProject(("Main.xaml", $"<Window {Ns} WindowStyle=\"None\" AllowsTransparency=\"True\">{body}</Window>"));
        try
        {
            var fonts = Fonts();
            var scan = new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize, Sharing = RowSharing.Preceding }, fonts).Scan(dir);
            var budget = new BudgetAnalyzer(fonts, MonoFamily).Analyze(scan, "test");
            return new CheckAnalyzer(fonts, MonoFamily, marginPercent: 5).Analyze(scan, budget, [], false);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Text_that_does_not_fit_even_the_upper_bound_is_a_certain_overflow()
    {
        // 100 寬、右邊一個程式填字的 TextBlock：文字最多 94 px，要 150 px（30 字）→ 確定放不下
        var result = Check("<DockPanel Width=\"100\" HorizontalAlignment=\"Left\"><TextBlock x:Name=\"Zoom\" DockPanel.Dock=\"Right\" Margin=\"6,0,0,0\"/><TextBlock Text=\"" + new string('A', 30) + "\"/></DockPanel>");
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FitKind.Overflow, finding.Kind);
        Assert.Contains("real limit is smaller still", finding.Warning);
    }

    [Fact]
    public void Text_that_fits_the_upper_bound_is_not_judged()
    {
        var result = Check("<DockPanel Width=\"100\" HorizontalAlignment=\"Left\"><TextBlock x:Name=\"Zoom\" DockPanel.Dock=\"Right\" Margin=\"6,0,0,0\"/><TextBlock Text=\"AAAA\"/></DockPanel>");
        Assert.Empty(result.Findings);
        Assert.True(result.Summary.Unreliable > 0);
    }
}
