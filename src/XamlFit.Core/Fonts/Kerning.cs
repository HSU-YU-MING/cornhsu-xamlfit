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

    /// <summary>
    /// 字型裡有的文字系統與語言系統（「latn」、「latn/TRK」…，有沒有掛 kern 都算）。null = 舊的度量檔，不分：每個 lookup 都套。
    /// WPF 只套這一段文字的文字系統（與元素語言的語言系統）掛的 lookup：Calibri 的阿拉伯文 lookup 裡有「" ,」的字距，
    /// 拉丁文不套（全部套的話「"{0}",」偏窄 3.6%）；Segoe UI 的土耳其文語言系統不套「Ta」「Te」的字距。
    /// </summary>
    public IReadOnlySet<string>? Systems { get; init; }

    /// <summary>a 後面接 b 時，寬度的改變量（字型單位）。</summary>
    public int Adjust(int a, int b) => Adjust(a, b, null);

    /// <summary>只套 <paramref name="system"/>（<see cref="SystemFor"/> 挑的）掛的 lookup；null = 全部。</summary>
    public int Adjust(int a, int b, string? system)
    {
        var total = 0;
        foreach (var lookup in Lookups)
        {
            if (system is not null && lookup.Systems is { } systems && !systems.Contains(system)) continue;
            foreach (var sub in lookup.Subtables)
                if (sub.TryAdjust(a, b, out var delta)) { total += delta; break; }
        }
        return total;
    }

    /// <summary>
    /// 這段文字用哪個系統：有「文字系統/語言」用它，否則文字系統的預設，否則 DFLT；都沒有回 ""（不套任何字距）。
    /// 舊的度量檔或認不得的文字系統回 null（全部套，跟以前一樣）。
    /// </summary>
    public string? SystemFor(string? script, string? language)
    {
        if (Systems is null || script is null) return null;
        if (language is not null && Systems.Contains($"{script}/{language}")) return $"{script}/{language}";
        if (Systems.Contains(script)) return script;
        if (language is not null && Systems.Contains($"DFLT/{language}")) return $"DFLT/{language}";
        return Systems.Contains("DFLT") ? "DFLT" : "";
    }
}

public sealed class KerningLookup
{
    public required IReadOnlyList<KerningSubtable> Subtables { get; init; }
    /// <summary>掛這個 lookup 的文字系統與語言系統（見 <see cref="KerningTable.Systems"/>）；null = 不分。</summary>
    public IReadOnlySet<string>? Systems { get; init; }
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
