using System.Globalization;
using System.Xml.Linq;

namespace XamlFit.Core.Xaml;

/// <summary>
/// 使用者宣告的「用屬性產生欄的 Grid 子類別」，例如 Playnite 的 <c>GridEx</c>：
/// <c>ColumnCount="2" StarColumns="1" AutoLayoutColumns="2"</c> 就產生兩欄（第 0 欄 Auto、第 1 欄 *），
/// 子元素依序填進格子。每個屬性填的是**那個類別用的屬性名**，沒有的就留空。
///
/// 語意照 Playnite 的 GridEx.cs（MIT）：
/// <list type="bullet">
/// <item>欄數屬性：產生那麼多欄，全部 <c>Auto</c>（會清掉原本寫的欄定義）。</item>
/// <item>星號欄屬性：逗號分隔的欄索引，那幾欄變成 <c>*</c>，其餘都是 <c>Auto</c>（也套在原本寫的欄定義上）。</item>
/// <item>自動排列屬性：每次量版面時把子元素依序放進格子（左到右、再換列），會<b>蓋掉</b>子元素自己寫的
///   <c>Grid.Column</c>；<c>ColumnSpan</c> 照算；<c>Collapsed</c> 的子元素一樣佔一格。</item>
/// </list>
///
/// ⚠ 不內建任何第三方類別的語意：別的專案可能也有一個叫 GridEx、但規則完全不同的類別，
///   猜錯就是高估。所以只認使用者在設定檔宣告的。
/// </summary>
public sealed record GridPanel(string? ColumnCount, string? StarColumns, string? AutoLayoutColumns)
{
    private const string Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// 把宣告過的元素改寫成普通的 WPF Grid（明寫欄定義與每個子元素的 Grid.Column），之後交給已驗證的 Grid 求解。
    /// 任何一個屬性讀不出來（綁定、不是數字）就整個不改 —— 它會照原樣維持「不認得的控制項」，不會被猜。
    /// </summary>
    public static void Rewrite(XDocument doc, IReadOnlyDictionary<string, GridPanel> panels)
    {
        if (panels.Count == 0 || doc.Root is null) return;

        foreach (var el in doc.Root.DescendantsAndSelf().ToList())
        {
            var local = el.Name.LocalName;
            if (local.Contains('.', StringComparison.Ordinal) || !panels.TryGetValue(local, out var spec)) continue;
            spec.Apply(el, local);
        }
    }

    private void Apply(XElement el, string local)
    {
        // 屬性可以寫成 ColumnCount="2"，也可以寫成附加屬性的樣子 GridEx.ColumnCount="2"
        XAttribute? Attr(string? name) => name is null ? null
            : el.Attribute(name) ?? el.Attribute($"{local}.{name}");

        int? columnCount = null, autoColumns = null;
        HashSet<int>? stars = null;

        if (Attr(ColumnCount) is { } cc)
        {
            if (!int.TryParse(cc.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0) return;
            columnCount = n;
        }
        if (Attr(StarColumns) is { } sc && sc.Value.Trim().Length > 0)
        {
            stars = [];
            foreach (var part in sc.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return;
                stars.Add(i);
            }
        }
        if (Attr(AutoLayoutColumns) is { } al)
        {
            if (!int.TryParse(al.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n == 0) return;
            if (n > 0) autoColumns = n;   // -1 是關閉
        }

        // 自動排列要先確定每個子元素的 ColumnSpan 讀得出來，否則後面每一格的位置都不知道
        var children = el.Elements().Where(c => !c.Name.LocalName.Contains('.', StringComparison.Ordinal)).ToList();
        var spans = new List<int>();
        if (autoColumns is not null)
            foreach (var child in children)
            {
                var raw = child.Attribute("Grid.ColumnSpan")?.Value.Trim();
                if (raw is null) { spans.Add(1); continue; }
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var span)) return;
                spans.Add(span);
            }

        // ── 讀得懂，開始改寫 ──
        el.Name = XName.Get("Grid", Wpf);
        foreach (var prop in el.Elements().Where(c => c.Name.LocalName.StartsWith(local + ".", StringComparison.Ordinal)).ToList())
            prop.Name = XName.Get("Grid" + prop.Name.LocalName[local.Length..], Wpf);
        foreach (var name in new[] { ColumnCount, StarColumns, AutoLayoutColumns })
            Attr(name)?.Remove();

        var columns = el.Elements().FirstOrDefault(c => c.Name.LocalName == "Grid.ColumnDefinitions");
        if (columnCount is { } count)
        {
            columns?.Remove();
            columns = new XElement(XName.Get("Grid.ColumnDefinitions", Wpf),
                Enumerable.Range(0, count).Select(_ => new XElement(XName.Get("ColumnDefinition", Wpf))));
            el.AddFirst(columns);
        }
        if (columns is not null && (columnCount is not null || stars is not null))
        {
            var i = 0;
            foreach (var def in columns.Elements())
                def.SetAttributeValue("Width", stars?.Contains(i++) == true ? "*" : "Auto");
        }

        if (autoColumns is { } per)
        {
            var index = 0;
            for (var k = 0; k < children.Count; k++)
            {
                children[k].SetAttributeValue("Grid.Column", (index % per).ToString(CultureInfo.InvariantCulture));
                children[k].SetAttributeValue("Grid.Row", (index / per).ToString(CultureInfo.InvariantCulture));
                index += spans[k] > 0 ? spans[k] : 1;
            }
        }
    }
}
