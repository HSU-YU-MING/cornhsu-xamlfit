namespace XamlFit.Core.Fonts;

/// <summary>
/// 字型的字距調整（GPOS 'kern' 特性裡的成對調整，以碼位表示）。WPF 預設會套用它：
/// Segoe UI 100 px 的「To」比「T」加「o」窄 9.8 px（LayoutOracle／kerntest 實測）。
/// 只存寬度的改變量（兩個字的 XAdvance 調整之和），沒有任何字形資料。
///
/// 套用規則照 OpenType（與 HarfBuzz 相同）：同一個 lookup 裡，第一個「適用」的子表決定這一對的值
/// （格式 1：找得到這一對才算適用；格式 2：第一個字在涵蓋範圍內、兩個類別都在範圍內就算適用，值可以是 0）；
/// 不同 lookup 的值相加。舊式 'kern' 表 WPF 不用（Tahoma 有，WPF 不調），所以不在這裡。
/// </summary>
public sealed class KerningTable
{
    public required IReadOnlyList<KerningLookup> Lookups { get; init; }

    /// <summary>a 後面接 b 時，寬度的改變量（字型單位）。</summary>
    public int Adjust(int a, int b)
    {
        var total = 0;
        foreach (var lookup in Lookups)
            foreach (var sub in lookup.Subtables)
                if (sub.TryAdjust(a, b, out var delta)) { total += delta; break; }
        return total;
    }
}

public sealed class KerningLookup
{
    public required IReadOnlyList<KerningSubtable> Subtables { get; init; }
}

public abstract class KerningSubtable
{
    public abstract bool TryAdjust(int a, int b, out int delta);
}

/// <summary>格式 1：逐對列出。</summary>
public sealed class KerningPairs : KerningSubtable
{
    public required IReadOnlyDictionary<long, int> Pairs { get; init; }

    public static long Key(int a, int b) => ((long)a << 32) | (uint)b;

    public override bool TryAdjust(int a, int b, out int delta) => Pairs.TryGetValue(Key(a, b), out delta);
}

/// <summary>格式 2：兩邊各分類別，查類別矩陣。</summary>
public sealed class KerningClasses : KerningSubtable
{
    /// <summary>第一個字的涵蓋範圍（[起, 迄] 遞增、不重疊）。</summary>
    public required IReadOnlyList<(int Start, int End)> Coverage { get; init; }
    /// <summary>[起, 迄, 類別]，沒列到的碼位是類別 0。</summary>
    public required IReadOnlyList<(int Start, int End, int Class)> First { get; init; }
    public required IReadOnlyList<(int Start, int End, int Class)> Second { get; init; }
    public required int FirstCount { get; init; }
    public required int SecondCount { get; init; }
    /// <summary>非零的格子：(類別1 × SecondCount + 類別2) → 改變量。</summary>
    public required IReadOnlyDictionary<int, int> Cells { get; init; }

    public override bool TryAdjust(int a, int b, out int delta)
    {
        delta = 0;
        if (!InRanges(Coverage, a)) return false;
        var c1 = ClassOf(First, a);
        var c2 = ClassOf(Second, b);
        if (c1 >= FirstCount || c2 >= SecondCount) return false;
        Cells.TryGetValue(c1 * SecondCount + c2, out delta);
        return true;
    }

    private static bool InRanges(IReadOnlyList<(int Start, int End)> ranges, int cp)
    {
        int lo = 0, hi = ranges.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (cp < ranges[mid].Start) hi = mid - 1;
            else if (cp > ranges[mid].End) lo = mid + 1;
            else return true;
        }
        return false;
    }

    private static int ClassOf(IReadOnlyList<(int Start, int End, int Class)> runs, int cp)
    {
        int lo = 0, hi = runs.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (cp < runs[mid].Start) hi = mid - 1;
            else if (cp > runs[mid].End) lo = mid + 1;
            else return runs[mid].Class;
        }
        return 0;
    }
}
