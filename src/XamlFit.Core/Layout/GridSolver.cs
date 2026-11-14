using System.Globalization;
using System.Xml.Linq;
using XamlFit.Core.Xaml;

namespace XamlFit.Core.Layout;

/// <summary>欄的尺寸種類。省略 Width 時 WPF 的預設是 <c>*</c>，不是 Auto 也不是 0。</summary>
internal enum ColumnKind { Fixed, Auto, Star, Unknown }

internal sealed record ColumnSpec(ColumnKind Kind, double Value, double Min, double Max, bool Shared = false);

/// <summary>解不出來時說明卡在哪，報告要講得出原因。</summary>
/// <param name="Unconstrained">文字自己在 Auto 欄裡：WPF 用無限寬量它，換行不會發生。</param>
/// <param name="Squeezed">其他欄已經把 Grid 佔滿：這一欄拿到 0（WPF 照樣這樣排，文字被擠到看不見）。</param>
public sealed record GridSolveResult(double? Width, string Reason, bool Unconstrained = false, bool Squeezed = false);

/// <summary>
/// 解 Grid 的 <c>Auto</c> 與 <c>*</c> 欄。
///
/// WPF 的欄寬演算法（只看水平方向）：
///   1. 固定像素欄取自己的值
///   2. Auto 欄取該欄子元素期望寬度的最大值
///   3. 剩餘空間按星號權重分配；被 MinWidth／MaxWidth 夾住的欄，多出來或不夠的部分
///      要**重新分配**給其他星號欄
///
/// ⚠ 任何一步算不出來就整個放棄，回 <c>NeedsGrid</c>。
///   半猜出來的欄寬會讓星號分配跟著錯，而錯的方向不可控。
///
/// ⚠ **文字自己在 Auto 欄裡時，問法不一樣。** Auto 欄會跟著內容長大，所以
///   「這一欄現在多寬」不是文字的上界 —— 第一版回傳的就是那個，等於每個放在 Auto 欄的
///   標籤都被算成「預算＝自己現在的寬度、餘裕 0」，一翻譯就報溢出。
///   真正的上界是「它長到把整個 Grid 佔滿之前還有多少」：
///   Grid 的可用寬度 − 其他欄的最低需求（固定欄的值、其他 Auto 欄的內容、星號欄的 MinWidth）。
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

    /// <param name="available">Grid 自己可用的內容寬度（已經由呼叫端往上解出來）。</param>
    public GridSolveResult Solve(XElement grid, XElement child, double available, int depth)
    {
        if (depth > 6) return new GridSolveResult(null, "grid nesting is too deep to solve");

        var specs = ReadColumns(grid);
        if (specs.Count == 0) return new GridSolveResult(null, "no ColumnDefinitions");

        for (var i = 0; i < specs.Count; i++)
        {
            // SharedSizeGroup 的欄寬由整個 scope 的其他 Grid 共同決定 —— 不猜
            if (specs[i].Shared)
                return new GridSolveResult(null, "SharedSizeGroup columns are decided across grids");
            if (specs[i].Kind == ColumnKind.Unknown)
                return new GridSolveResult(null, $"column {i} has a width this tool cannot read");
        }

        var (col, span) = ColumnOf(child);
        if (col < 0) return new GridSolveResult(null, "non-numeric Grid.Column or Grid.ColumnSpan");

        // WPF 會把超出範圍的欄號夾到最後一欄
        col = Math.Min(col, specs.Count - 1);
        span = Math.Min(span, specs.Count - col);

        var probeTouchesAuto = false;
        for (var i = col; i < col + span; i++)
            if (specs[i].Kind == ColumnKind.Auto) probeTouchesAuto = true;

        // 文字（或它的容器）佔了 Auto 欄：跨好幾欄時，被跨的 Auto 欄一起長大，整組能長到 Grid 被佔滿為止
        //（Playnite 的編輯視窗：跨兩欄的 DockPanel，33 處。LayoutOracle「spans an Auto column」實測）
        if (probeTouchesAuto)
            return SolveAutoProbe(grid, specs, col, span, available, depth);

        var widths = new double[specs.Count];

        for (var i = 0; i < specs.Count; i++)
            if (specs[i].Kind == ColumnKind.Fixed) widths[i] = Clamp(specs[i], specs[i].Value);

        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].Kind != ColumnKind.Auto) continue;
            var (w, reason) = AutoWidth(grid, i, depth);
            if (w is null) return new GridSolveResult(null, $"column {i} is Auto and {reason}");
            widths[i] = Clamp(specs[i], w.Value);
        }

        DistributeStars(specs, widths, available);

        double total = 0;
        for (var i = col; i < col + span; i++) total += widths[i];

        return total > 0
            ? new GridSolveResult(total, span > 1 ? $"columns {col}..{col + span - 1}" : $"column {col}")
            // ⚠ 固定欄與 Auto 欄已經超過 Grid 的寬度時，星號欄拿到 0 —— 那是確定的答案，不是「解不開」。
            //   CorpusRender 實測 ScreenToGif 的 Troubleshoot 視窗：希臘文的說明文字（Auto 欄）比整個視窗還寬，
            //   標題（* 欄）寬度是 0。第一版回「解不開」，於是那一筆在希臘文裡默默沒有檢查。
            : new GridSolveResult(0, $"column {col} (the other columns already take the whole grid)", Squeezed: true);
    }

    /// <summary>
    /// 文字自己在 Auto 欄裡：它能長到多寬才會被 Grid 的邊界切掉。
    /// = Grid 可用寬度 − 其他每一欄的最低需求。
    /// </summary>
    private GridSolveResult SolveAutoProbe(
        XElement grid, List<ColumnSpec> specs, int col, int span, double available, int depth)
    {
        double others = 0;

        for (var i = 0; i < specs.Count; i++)
        {
            if (i >= col && i < col + span) continue;
            switch (specs[i].Kind)
            {
                case ColumnKind.Fixed:
                    others += Clamp(specs[i], specs[i].Value);
                    break;
                case ColumnKind.Auto:
                    var (w, reason) = AutoWidth(grid, i, depth);
                    if (w is null) return new GridSolveResult(null, $"column {i} is Auto and {reason}");
                    others += Clamp(specs[i], w.Value);
                    break;
                case ColumnKind.Star:
                    others += specs[i].Min;   // 星號欄會被擠到它的 MinWidth（預設 0）
                    break;
            }
        }

        // 被跨的欄最多長到各自的 MaxWidth（固定欄就是它的值）
        double cap = 0;
        for (var i = col; i < col + span; i++)
            cap += specs[i].Kind == ColumnKind.Fixed ? Clamp(specs[i], specs[i].Value) : specs[i].Max;

        var which = span > 1 ? $"columns {col}..{col + span - 1}" : $"column {col}";
        var width = Math.Min(cap, available - others);
        return width > 0
            ? new GridSolveResult(width, $"{which} (Auto: it can grow until the grid is full)", Unconstrained: true)
            : new GridSolveResult(0, $"{which} (the other columns already take the whole grid)", Unconstrained: true, Squeezed: true);
    }

    /// <summary>
    /// 星號欄的分配，含 MinWidth／MaxWidth 的重新分配。
    ///
    /// ⚠ 第一版只做「分完再夾」，沒有重新分配：`*`(MaxWidth=200) 與 `*` 分 500，
    ///   WPF 給 200／300，第一版給 200／250（偏小，誤報）；
    ///   `*`(MinWidth=400) 與 `*` 分 500，WPF 給 400／100，第一版給 400／250
    ///   （**偏大，會漏報溢出**）。
    ///
    /// 做法：每一輪把還沒定案的欄按權重分、夾住；總違反量 &gt; 0（下限佔上風）就把撞下限的定案，
    /// &lt; 0 就把撞上限的定案，= 0 全部定案。跟 CSS flexbox 解彈性長度是同一個演算法。
    /// </summary>
    private static void DistributeStars(List<ColumnSpec> specs, double[] widths, double available)
    {
        var open = new List<int>();
        double used = 0;
        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].Kind == ColumnKind.Star) open.Add(i);
            else used += widths[i];
        }

        var remaining = Math.Max(0, available - used);

        for (var guard = 0; open.Count > 0 && guard <= specs.Count; guard++)
        {
            double weight = 0;
            foreach (var i in open) weight += specs[i].Value;
            if (weight <= 0) { foreach (var i in open) widths[i] = Clamp(specs[i], 0); break; }

            double violation = 0;
            var shares = new Dictionary<int, (double Raw, double Clamped)>();
            foreach (var i in open)
            {
                var raw = remaining * specs[i].Value / weight;
                var clamped = Clamp(specs[i], raw);
                shares[i] = (raw, clamped);
                violation += clamped - raw;
            }

            List<int> freeze;
            if (Math.Abs(violation) < 1e-9) freeze = [.. open];
            else if (violation > 0) freeze = [.. open.Where(i => shares[i].Clamped > shares[i].Raw)];
            else freeze = [.. open.Where(i => shares[i].Clamped < shares[i].Raw)];

            foreach (var i in freeze)
            {
                widths[i] = shares[i].Clamped;
                remaining = Math.Max(0, remaining - shares[i].Clamped);
                open.Remove(i);
            }
        }
    }

    /// <summary>Auto 欄的寬度 = 該欄子元素期望寬度的最大值。</summary>
    private (double? Width, string Reason) AutoWidth(XElement grid, int column, int depth)
    {
        double max = 0;
        var seen = false;
        var specs = ReadColumns(grid);
        var lastColumn = specs.Count - 1;

        foreach (var child in WidthResolver.LayoutChildren(grid))
        {
            // 屬性元素（<Grid.ColumnDefinitions>）不是版面子元素
            if (child.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;

            var (col, span) = ColumnOf(child);
            if (col < 0) return (null, "a child has a non-numeric Grid.Column");
            col = Math.Min(col, lastColumn);

            // ⚠ 跨欄的子元素會讓 WPF 把超出的寬度分攤到跨到的欄上，規則複雜。
            //   例外（LayoutOracle「spanning sibling」實測）：跨到星號欄的子元素**不會**撐大 Auto 欄 ——
            //   放在水平 StackPanel 或外層 Auto 欄裡（用無限寬量）也一樣。Playnite 編輯視窗的區段標題就是這樣，30 處。
            //   只跨 Auto／固定欄的：照樣放棄 —— 分攤算錯會連帶弄錯星號分配。
            if (span > 1 && column >= col && column < col + span)
            {
                var spansStar = false;
                for (var i = col; i < Math.Min(col + span, specs.Count); i++)
                    if (specs[i].Kind == ColumnKind.Star) spansStar = true;
                if (spansStar) continue;
                return (null, "a spanning child contributes to it");
            }

            if (col != column) continue;

            var w = _measurer.DesiredWidth(child, depth + 1);
            if (w is null) return (null, $"the desired width of <{child.Name.LocalName}> is not statically knowable");

            seen = true;
            if (w.Value > max) max = w.Value;
        }

        // 空的 Auto 欄寬度是 0，那是確定的答案不是未知
        return (seen ? max : 0, "");
    }

    /// <summary>⚠ 不要用 Math.Clamp：MinWidth 大於 MaxWidth 時它會丟例外（寫錯的 XAML 不該讓工具當掉）。
    /// WPF 的規則是 MinWidth 贏。</summary>
    private static double Clamp(ColumnSpec s, double w) => Math.Max(s.Min, Math.Min(w, s.Max));

    private static (int Column, int Span) ColumnOf(XElement el)
    {
        var col = 0;
        if (el.Attribute("Grid.Column")?.Value is { } c
            && !int.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out col))
            return (-1, 1);

        var span = 1;
        if (el.Attribute("Grid.ColumnSpan")?.Value is { } s
            && !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out span))
            return (-1, 1);

        return (Math.Max(0, col), Math.Max(1, span));
    }

    /// <summary>讀欄定義。支援屬性元素與 <c>ColumnDefinitions="Auto,*,100"</c> 簡寫。</summary>
    private List<ColumnSpec> ReadColumns(XElement grid)
    {
        var result = new List<ColumnSpec>();

        if (grid.Attribute("ColumnDefinitions")?.Value is { Length: > 0 } shorthand)
        {
            foreach (var part in shorthand.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                result.Add(ParseLength(part, 0, double.PositiveInfinity));
            return result;
        }

        var holder = grid.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith(".ColumnDefinitions", StringComparison.Ordinal));
        if (holder is null) return result;

        foreach (var cd in holder.Elements().Where(e => e.Name.LocalName == "ColumnDefinition"))
        {
            // 值可能是資源引用（<c>Width="{StaticResource LabelColumn}"</c>）或系統參數 —— 交給樣式索引解
            var minR = _styles.AttributeValue(cd, "MinWidth");
            var maxR = _styles.AttributeValue(cd, "MaxWidth");
            var widthR = _styles.AttributeValue(cd, "Width");
            var min = minR.IsValue ? Number(minR.Value) : null;
            var max = maxR.IsValue ? Number(maxR.Value) : null;

            // 寫了但讀不懂的（綁定、專案外的資源、樣式）→ 整欄當成讀不出來
            if (minR.IsUnreadable || maxR.IsUnreadable || widthR.IsUnreadable
                || (minR.IsValue && min is null) || (maxR.IsValue && max is null)
                || cd.Attribute("Style") is not null)
            {
                result.Add(new ColumnSpec(ColumnKind.Unknown, 0, 0, double.PositiveInfinity));
                continue;
            }

            // ⚠ Width 省略時預設是 "*"，不是 Auto 也不是 0
            var spec = ParseLength(widthR.IsValue ? widthR.Value : "*", min ?? 0, max ?? double.PositiveInfinity);
            result.Add(cd.Attribute("SharedSizeGroup") is null ? spec : spec with { Shared = true });
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
            if (head.Length == 0) return new ColumnSpec(ColumnKind.Star, 1, min, max);
            return double.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w >= 0 && double.IsFinite(w)
                ? new ColumnSpec(ColumnKind.Star, w, min, max)
                : new ColumnSpec(ColumnKind.Unknown, 0, min, max);
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
