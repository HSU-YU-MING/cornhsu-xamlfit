using XamlFit.Core.Fonts;

namespace XamlFit.Core.OpenType;

/// <summary>
/// 從 GPOS 讀 'kern' 特性的成對調整（lookup type 2，格式 1 與 2；type 9 延伸包著的也算），換成以碼位表示的
/// <see cref="KerningTable"/>。只取寬度的改變量：第一個字的 XAdvance 加第二個字的 XAdvance。
/// XPlacement、裝置表、y 方向都不影響一行的寬度，不讀。
///
/// 所有文字系統（DFLT、latn、cyrl、grek…）與語言系統裡掛 'kern' 的 lookup 取聯集 —— 字型通常讓它們共用同一組。
/// 只有 cmap 對應得到的字符有意義（我們不做字形替換），其他字符略過。
/// </summary>
internal static class GposKerning
{
    /// <param name="coords">可變字型的實例（正規化座標）：成對調整的 XAdvance 加上裝置表（VariationIndex）指到的變化量，無條件進位。
    /// Segoe UI Variable Text Bold 的「rn」比預設位置寬 15 單位（WpfOracle 對照：不加偏窄 0.16%）。</param>
    public static KerningTable? Read(SfntReader r, IReadOnlyDictionary<int, List<int>> glyphToCodepoints, double[]? coords = null)
    {
        if (!r.Has("GPOS") || glyphToCodepoints.Count == 0) return null;
        try
        {
            Vary = null;
            if (coords is not null && VariableFonts.GdefStore(r, coords.Length) is { } store)
                Vary = device => r.U16(device + 4) == 0x8000 ? (int)Math.Ceiling(store(r.U16(device), r.U16(device + 2), coords) - 1e-6) : 0;
            var gpos = r.TableOffset("GPOS");
            var scriptList = gpos + r.U16(gpos + 4);
            var featureList = gpos + r.U16(gpos + 6);
            var lookupList = gpos + r.U16(gpos + 8);

            var kernFeatures = KernFeatureIndices(r, scriptList, featureList);
            if (kernFeatures.Count == 0) return null;

            var lookupIndices = new SortedSet<int>();
            foreach (var fi in kernFeatures)
            {
                var feature = featureList + r.U16(featureList + 2 + fi * 6 + 4);
                var count = r.U16(feature + 2);
                for (var i = 0; i < count; i++) lookupIndices.Add(r.U16(feature + 4 + i * 2));
            }

            var lookupCount = r.U16(lookupList);
            var lookups = new List<KerningLookup>();
            foreach (var li in lookupIndices)
            {
                if (li >= lookupCount) continue;
                var lookup = lookupList + r.U16(lookupList + 2 + li * 2);
                var type = r.U16(lookup);
                var subCount = r.U16(lookup + 4);
                var subs = new List<KerningSubtable>();
                for (var s = 0; s < subCount; s++)
                {
                    var sub = lookup + r.U16(lookup + 6 + s * 2);
                    var subType = type;
                    if (type == 9)
                    {
                        subType = r.U16(sub + 2);
                        sub += (int)r.U32(sub + 4);
                    }
                    if (subType != 2) continue;
                    KerningSubtable? parsed = r.U16(sub) switch
                    {
                        1 => PairFormat1(r, sub, glyphToCodepoints),
                        2 => PairFormat2(r, sub, glyphToCodepoints),
                        _ => null,
                    };
                    if (parsed is not null) subs.Add(parsed);
                }
                if (subs.Count > 0) lookups.Add(new KerningLookup { Subtables = subs });
            }
            return lookups.Count > 0 ? new KerningTable { Lookups = lookups } : null;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;   // 壞掉的 GPOS：當成沒有字距（偏寬，保守）
        }
    }

    private static HashSet<int> KernFeatureIndices(SfntReader r, int scriptList, int featureList)
    {
        var featureCount = r.U16(featureList);
        var kern = new HashSet<int>();
        for (var i = 0; i < featureCount; i++)
        {
            var rec = featureList + 2 + i * 6;
            if (r.AsciiString(rec, 4) == "kern") kern.Add(i);
        }
        if (kern.Count == 0) return kern;

        // 只取文字系統真的掛上的
        var used = new HashSet<int>();
        var scriptCount = r.U16(scriptList);
        for (var s = 0; s < scriptCount; s++)
        {
            var script = scriptList + r.U16(scriptList + 2 + s * 6 + 4);
            var defaultLang = r.U16(script);
            if (defaultLang != 0) AddLangSys(r, script + defaultLang, kern, used);
            var langCount = r.U16(script + 2);
            for (var l = 0; l < langCount; l++)
                AddLangSys(r, script + r.U16(script + 4 + l * 6 + 4), kern, used);
        }
        return used;
    }

    private static void AddLangSys(SfntReader r, int langSys, HashSet<int> kern, HashSet<int> used)
    {
        var required = r.U16(langSys + 2);
        if (required != 0xFFFF && kern.Contains(required)) used.Add(required);
        var count = r.U16(langSys + 4);
        for (var i = 0; i < count; i++)
        {
            var fi = r.U16(langSys + 6 + i * 2);
            if (kern.Contains(fi)) used.Add(fi);
        }
    }

    private static int ValueSize(int format) => 2 * System.Numerics.BitOperations.PopCount((uint)format & 0xFF);

    // 讀可變字型的實例時：裝置表（絕對位置）→ 變化量。一次只讀一支字型的一個實例（每條執行緒自己的）
    [ThreadStatic] private static Func<int, int>? Vary;

    /// <param name="parent">裝置表的位置從哪裡起算：格式 1 是 PairSet、格式 2 是子表。</param>
    private static int XAdvance(SfntReader r, int record, int format, int parent)
    {
        var value = (format & 0x4) == 0 ? 0 : r.S16(record + 2 * System.Numerics.BitOperations.PopCount((uint)format & 0x3));
        if (Vary is { } vary && (format & 0x40) != 0 && r.U16(record + 2 * System.Numerics.BitOperations.PopCount((uint)format & 0x3F)) is var device and > 0)
            value += vary(parent + device);
        return value;
    }

    /// <summary>涵蓋表：字符 → 在涵蓋表裡的序號。</summary>
    private static Dictionary<int, int> Coverage(SfntReader r, int offset)
    {
        var map = new Dictionary<int, int>();
        var format = r.U16(offset);
        if (format == 1)
        {
            var count = r.U16(offset + 2);
            for (var i = 0; i < count; i++) map[r.U16(offset + 4 + i * 2)] = i;
        }
        else if (format == 2)
        {
            var count = r.U16(offset + 2);
            for (var i = 0; i < count; i++)
            {
                var rec = offset + 4 + i * 6;
                int start = r.U16(rec), end = r.U16(rec + 2), index = r.U16(rec + 4);
                for (var g = start; g <= end; g++) map[g] = index + (g - start);
            }
        }
        return map;
    }

    private static Dictionary<int, int> ClassDef(SfntReader r, int offset)
    {
        var map = new Dictionary<int, int>();
        var format = r.U16(offset);
        if (format == 1)
        {
            var start = r.U16(offset + 2);
            var count = r.U16(offset + 4);
            for (var i = 0; i < count; i++) map[start + i] = r.U16(offset + 6 + i * 2);
        }
        else if (format == 2)
        {
            var count = r.U16(offset + 2);
            for (var i = 0; i < count; i++)
            {
                var rec = offset + 4 + i * 6;
                int start = r.U16(rec), end = r.U16(rec + 2), cls = r.U16(rec + 4);
                for (var g = start; g <= end; g++) map[g] = cls;
            }
        }
        return map;
    }

    private static KerningPairs? PairFormat1(SfntReader r, int sub, IReadOnlyDictionary<int, List<int>> cps)
    {
        var coverage = Coverage(r, sub + r.U16(sub + 2));
        int fmt1 = r.U16(sub + 4), fmt2 = r.U16(sub + 6);
        var pairSetCount = r.U16(sub + 8);
        var recordSize = 2 + ValueSize(fmt1) + ValueSize(fmt2);
        var pairs = new Dictionary<long, int>();
        foreach (var (first, index) in coverage)
        {
            if (index >= pairSetCount || !cps.TryGetValue(first, out var firstCps)) continue;
            var set = sub + r.U16(sub + 10 + index * 2);
            var count = r.U16(set);
            for (var i = 0; i < count; i++)
            {
                var rec = set + 2 + i * recordSize;
                var second = r.U16(rec);
                if (!cps.TryGetValue(second, out var secondCps)) continue;
                // 格式 1 的裝置表從 PairSet 起算（格式 2 從子表起算）
                var delta = XAdvance(r, rec + 2, fmt1, set) + XAdvance(r, rec + 2 + ValueSize(fmt1), fmt2, set);
                foreach (var a in firstCps)
                    foreach (var b in secondCps)
                        pairs.TryAdd(KerningPairs.Key(a, b), delta);
            }
        }
        return pairs.Count > 0 ? new KerningPairs { Pairs = pairs } : null;
    }

    private static KerningClasses? PairFormat2(SfntReader r, int sub, IReadOnlyDictionary<int, List<int>> cps)
    {
        var coverage = Coverage(r, sub + r.U16(sub + 2));
        int fmt1 = r.U16(sub + 4), fmt2 = r.U16(sub + 6);
        var class1 = ClassDef(r, sub + r.U16(sub + 8));
        var class2 = ClassDef(r, sub + r.U16(sub + 10));
        int c1Count = r.U16(sub + 12), c2Count = r.U16(sub + 14);
        var size1 = ValueSize(fmt1);
        var recordSize = size1 + ValueSize(fmt2);
        var matrix = sub + 16;

        var cells = new Dictionary<int, int>();
        for (var c1 = 0; c1 < c1Count; c1++)
            for (var c2 = 0; c2 < c2Count; c2++)
            {
                var rec = matrix + (c1 * c2Count + c2) * recordSize;
                var delta = XAdvance(r, rec, fmt1, sub) + XAdvance(r, rec + size1, fmt2, sub);
                if (delta != 0) cells[c1 * c2Count + c2] = delta;
            }

        static List<(int, int, int)> Runs(IEnumerable<(int Cp, int Value)> items)
        {
            var runs = new List<(int, int, int)>();
            foreach (var (cp, v) in items.OrderBy(i => i.Cp))
            {
                if (runs.Count > 0 && runs[^1].Item2 == cp - 1 && runs[^1].Item3 == v)
                    runs[^1] = (runs[^1].Item1, cp, v);
                else runs.Add((cp, cp, v));
            }
            return runs;
        }

        var coverageCps = coverage.Keys.Where(cps.ContainsKey).SelectMany(g => cps[g]).Select(cp => (cp, 0));
        var firstCps = class1.Where(kv => kv.Value != 0 && cps.ContainsKey(kv.Key)).SelectMany(kv => cps[kv.Key].Select(cp => (cp, kv.Value)));
        var secondCps = class2.Where(kv => kv.Value != 0 && cps.ContainsKey(kv.Key)).SelectMany(kv => cps[kv.Key].Select(cp => (cp, kv.Value)));

        var coverageRuns = Runs(coverageCps).Select(x => (x.Item1, x.Item2)).ToList();
        if (coverageRuns.Count == 0) return null;
        return new KerningClasses
        {
            Coverage = coverageRuns,
            First = Runs(firstCps),
            Second = Runs(secondCps),
            FirstCount = c1Count,
            SecondCount = c2Count,
            Cells = cells,
        };
    }
}
