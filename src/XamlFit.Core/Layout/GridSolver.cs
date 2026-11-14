using System.Globalization;
using System.Xml.Linq;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Layout;

/// <summary>欄的尺寸種類。省略 Width 時 WPF 的預設是 <c>*</c>，不是 Auto 也不是 0。</summary>
internal enum ColumnKind { Fixed, Auto, Star, Unknown }

internal sealed record ColumnSpec(ColumnKind Kind, double Value, double Min, double Max);

/// <summary>解不出來時說明卡在哪，報告要講得出原因。</summary>
public sealed record GridSolveResult(double? Width, string Reason);

/// <summary>
/// 解 Grid 的 <c>Auto</c> 與 <c>*</c> 欄。
///
/// M0 實測：Auto／星號欄佔「有裁切風險」位置的 **21.6%**，是單一最大的缺口 ——
/// 解掉它會把有風險覆蓋率從 75.9% 推到約 97.5%。
///
/// WPF 的欄寬演算法（只看水平方向）：
///   1. 固定像素欄取自己的值
///   2. Auto 欄取該欄子元素期望寬度的最大值
///   3. 剩餘空間按星號權重分配
///   4. 每一欄再被自己的 MinWidth／MaxWidth 夾住
///
/// ⚠ 任何一步算不出來就整個放棄，回 <c>NeedsGrid</c>。
///   半猜出來的欄寬會讓星號分配跟著錯，而錯的方向不可控。
/// </summary>
internal sealed class GridSolver
{
    private readonly IElementMeasurer _measurer;
    private readonly StyleIndex _styles;

    public GridSolver(IElementMeasurer measurer, StyleIndex styles)
    {
        _measurer = measurer;
        _styles = styles;
    }

    /// <summary>
    /// 算出 <paramref name="child"/> 所在欄（含 ColumnSpan）的寬度。
    /// </summary>
    /// <param name="available">Grid 自己可用的內容寬度（已經由呼叫端往上解出來）。</param>
    public GridSolveResult Solve(XElement grid, XElement child, double available, int depth)
    {
        if (depth > 6) return new GridSolveResult(null, "grid nesting is too deep to solve");

        // SharedSizeGroup 的欄寬由整個 scope 的其他 Grid 共同決定 —— 不猜。
        if (grid.Descendants().Any(e => e.Attribute("SharedSizeGroup") is not null))
            return new GridSolveResult(null, "SharedSizeGroup columns are decided across grids");

        var specs = ReadColumns(grid);
        if (specs.Count == 0) return new GridSolveResult(null, "no ColumnDefinitions");

        var (col, span) = ColumnOf(child);
        if (col < 0) return new GridSolveResult(null, "non-numeric Grid.Column");
        if (col >= specs.Count) return new GridSolveResult(null, "Grid.Column is out of range");

        for (var i = 0; i < specs.Count; i++)
            if (specs[i].Kind == ColumnKind.Unknown)
                return new GridSolveResult(null, $"column {i} has a width this tool cannot read");

        var widths = new double[specs.Count];

        // 1. 固定欄
        for (var i = 0; i < specs.Count; i++)
            if (specs[i].Kind == ColumnKind.Fixed) widths[i] = Clamp(specs[i], specs[i].Value);

        // 2. Auto 欄：取該欄子元素期望寬度的最大值
        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].Kind != ColumnKind.Auto) continue;
            var (w, reason) = AutoWidth(grid, i, depth);
            if (w is null) return new GridSolveResult(null, $"column {i} is Auto and {reason}");
            widths[i] = Clamp(specs[i], w.Value);
        }

        // 3. 剩餘空間按星號權重分配
        var used = 0.0;
        var starWeight = 0.0;
        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].Kind == ColumnKind.Star) starWeight += specs[i].Value;
            else used += widths[i];
        }

        if (starWeight > 0)
        {
            var remaining = Math.Max(0, available - used);
            for (var i = 0; i < specs.Count; i++)
                if (specs[i].Kind == ColumnKind.Star)
                    widths[i] = Clamp(specs[i], remaining * specs[i].Value / starWeight);
        }

        double total = 0;
        for (var i = col; i < col + span && i < specs.Count; i++) total += widths[i];

        return total > 0
            ? new GridSolveResult(total, span > 1 ? $"columns {col}..{col + span - 1}" : $"column {col}")
            : new GridSolveResult(null, "the solved column width is zero");
    }

    /// <summary>Auto 欄的寬度 = 該欄子元素期望寬度的最大值。</summary>
    private (double? Width, string Reason) AutoWidth(XElement grid, int column, int depth)
    {
        double max = 0;
        var seen = false;

        foreach (var child in grid.Elements())
        {
            // 屬性元素（<Grid.ColumnDefinitions>）不是版面子元素
            if (child.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;

            var (col, span) = ColumnOf(child);
            if (col < 0) return (null, "a child has a non-numeric Grid.Column");

            // ⚠ 跨欄的子元素會讓 WPF 把超出的寬度分攤到跨到的欄上，規則複雜。
            //   只要有子元素跨到這一欄就放棄 —— 分攤算錯會連帶弄錯星號分配。
            if (span > 1 && column >= col && column < col + span)
                return (null, "a spanning child contributes to it");

            if (col != column) continue;

            var w = _measurer.DesiredWidth(child, depth + 1);
            if (w is null) return (null, $"the desired width of <{child.Name.LocalName}> is not statically knowable");

            seen = true;
            if (w.Value > max) max = w.Value;
        }

        // 空的 Auto 欄寬度是 0，那是確定的答案不是未知
        return (seen ? max : 0, "");
    }

    private static double Clamp(ColumnSpec s, double w) => Math.Clamp(w, s.Min, s.Max);

    private static (int Column, int Span) ColumnOf(XElement el)
    {
        var col = 0;
        if (el.Attribute("Grid.Column")?.Value is { } c
            && !int.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out col))
            return (-1, 1);

        var span = 1;
        if (el.Attribute("Grid.ColumnSpan")?.Value is { } s)
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out span);

        return (col, Math.Max(1, span));
    }

    /// <summary>讀欄定義。支援屬性元素與 <c>ColumnDefinitions="Auto,*,100"</c> 簡寫。</summary>
    private List<ColumnSpec> ReadColumns(XElement grid)
    {
        var result = new List<ColumnSpec>();

        if (_styles.Resolve(grid, "ColumnDefinitions", inheritable: false) is { Length: > 0 } shorthand)
        {
            foreach (var part in shorthand.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                result.Add(ParseLength(part, 0, double.PositiveInfinity));
            return result;
        }

        var holder = grid.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid.ColumnDefinitions");
        if (holder is null) return result;

        foreach (var cd in holder.Elements().Where(e => e.Name.LocalName == "ColumnDefinition"))
        {
            var min = Number(cd.Attribute("MinWidth")?.Value) ?? 0;
            var max = Number(cd.Attribute("MaxWidth")?.Value) ?? double.PositiveInfinity;
            // ⚠ Width 省略時預設是 "*"，不是 Auto 也不是 0
            result.Add(ParseLength(cd.Attribute("Width")?.Value ?? "*", min, max));
        }

        return result;
    }

    internal static ColumnSpec ParseLength(string? v, double min, double max)
    {
        var t = (v ?? "*").Trim();

        if (t.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return new ColumnSpec(ColumnKind.Auto, 0, min, max);

        if (t.EndsWith('*'))
        {
            var head = t[..^1].Trim();
            var weight = head.Length == 0 ? 1
                : double.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0 ? w
                : 1;
            return new ColumnSpec(ColumnKind.Star, weight, min, max);
        }

        // ⚠ 綁定或看不懂的寫法：當成星號是錯的，當成 Auto 也是錯的，當成 0 更錯。
        //   標成 Unknown 讓整個求解放棄 —— 半猜出來的欄寬會讓星號分配跟著錯。
        return Number(t) is { } px
            ? new ColumnSpec(ColumnKind.Fixed, px, min, max)
            : new ColumnSpec(ColumnKind.Unknown, 0, min, max);
    }

    private static double? Number(string? v) =>
        v is not null
        && double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
        && double.IsFinite(d) && d >= 0
            ? d : null;
}
