namespace XamlFit.Core.OpenType;

/// <summary>
/// 可變字型的具名實例（fvar 的 instance），照 WPF 看到的樣子：
/// WPF 不認得字型的家族名（「Segoe UI Variable」會被拆成「Segoe UI」加一個不認得的字面名稱，畫的是 Segoe UI），
/// 它認得的家族是「家族名 + 光學尺寸那組實例共同的字」（Segoe UI Variable Small／Text／Display），字重是各實例的 wght。
/// 前進寬度 = hmtx（預設位置）+ HVAR 在實例座標上的變化量（無條件進位到整數：寧可寬一點）。
/// 沒有 HVAR（寬度只在 gvar 的幽靈點裡）就不展開，只有預設實例。
/// </summary>
internal static class VariableFonts
{
    public sealed record Instance(string Family, string? LegacyFamily, int Weight, bool Italic, int[] Advances, bool IsDefault, double[] Normalized);

    // 子家族名裡表示字重、樣式的字（其他的字是光學尺寸那組的名字：Small、Text、Display）
    private static readonly HashSet<string> StyleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thin", "Hairline", "ExtraLight", "UltraLight", "Light", "SemiLight", "DemiLight", "Regular", "Normal", "Book", "Medium",
        "SemiBold", "DemiBold", "Bold", "ExtraBold", "UltraBold", "Black", "Heavy", "Italic", "Oblique", "Extra", "Ultra", "Semi", "Demi",
    };

    public static List<Instance> Read(SfntReader r, int[] advances, string family, Func<int, string?> name)
    {
        if (!r.Has("fvar")) return [];
        var fvar = r.TableOffset("fvar");
        var axesOffset = r.U16(fvar + 4);
        int axisCount = r.U16(fvar + 8), axisSize = r.U16(fvar + 10), instanceCount = r.U16(fvar + 12), instanceSize = r.U16(fvar + 14);
        if (axisCount == 0 || instanceCount == 0) return [];
        var axes = new (string Tag, double Min, double Default, double Max)[axisCount];
        for (var a = 0; a < axisCount; a++)
        {
            var o = fvar + axesOffset + a * axisSize;
            axes[a] = (r.AsciiString(o, 4), Fixed(r, o + 4), Fixed(r, o + 8), Fixed(r, o + 12));
        }
        var raw = new List<(string Subfamily, double[] Coords)>();
        for (var i = 0; i < instanceCount; i++)
        {
            var o = fvar + axesOffset + axisCount * axisSize + i * instanceSize;
            var coords = new double[axisCount];
            for (var a = 0; a < axisCount; a++) coords[a] = Fixed(r, o + 4 + a * 4);
            raw.Add((name(r.U16(o)) ?? "", coords));
        }

        var variation = r.Has("HVAR") ? ReadHvar(r, axisCount) : null;
        if (variation is null) return [];
        var avar = ReadAvar(r, axisCount);

        // 光學尺寸那一組（wght 以外的座標都一樣的實例）共同的名字
        var wght = Array.FindIndex(axes, x => x.Tag == "wght");
        string GroupKey(double[] c) => string.Join(",", c.Where((_, a) => a != wght));
        var groupWord = raw.GroupBy(x => GroupKey(x.Coords)).ToDictionary(g => g.Key, g =>
            g.Select(x => string.Join(" ", x.Subfamily.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !StyleWords.Contains(w))))
             .Where(w => w.Length > 0).GroupBy(w => w).OrderByDescending(w => w.Count()).Select(w => w.Key).FirstOrDefault());

        var result = new List<Instance>();
        foreach (var (subfamily, coords) in raw)
        {
            var normalized = new double[axisCount];
            for (var a = 0; a < axisCount; a++)
            {
                var (_, min, def, max) = axes[a];
                var v = Math.Clamp(coords[a], min, max);
                normalized[a] = v < def ? (def > min ? (v - def) / (def - min) : 0) : v > def ? (max > def ? (v - def) / (max - def) : 0) : 0;
                if (avar?[a] is { Length: > 0 } map) normalized[a] = Map(map, normalized[a]);
            }
            var widths = new int[advances.Length];
            for (var g = 0; g < advances.Length; g++)
                widths[g] = advances[g] + (int)Math.Ceiling(variation(g, normalized) - 1e-6);
            var word = groupWord.GetValueOrDefault(GroupKey(coords));
            var instanceFamily = word is null ? family : $"{family} {word}";
            var styleWords = string.Join(" ", subfamily.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => StyleWords.Contains(w) && !w.Equals("Regular", StringComparison.OrdinalIgnoreCase) && !w.Equals("Italic", StringComparison.OrdinalIgnoreCase)));
            var weight = wght >= 0 ? (int)Math.Round(coords[wght]) : 400;
            var italic = subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase);
            // 舊式（GDI）家族名：一般與粗體跟家族同名，其他字重是「家族 字重」（Segoe UI Variable Text Semibold）
            var legacy = styleWords.Length == 0 || styleWords.Equals("Bold", StringComparison.OrdinalIgnoreCase) ? null : $"{instanceFamily} {styleWords}";
            result.Add(new Instance(instanceFamily, legacy, weight, italic, widths,
                coords.Select((c, a) => Math.Abs(c - axes[a].Default) < 1e-6).All(x => x), normalized));
        }
        return result;
    }

    private static double Fixed(SfntReader r, int o) => unchecked((int)r.U32(o)) / 65536.0;

    private static double F2Dot14(SfntReader r, int o) => r.S16(o) / 16384.0;

    private static (double From, double To)[]?[]? ReadAvar(SfntReader r, int axisCount)
    {
        if (!r.Has("avar")) return null;
        var o = r.TableOffset("avar");
        if (r.U16(o + 6) != axisCount) return null;
        var maps = new (double, double)[]?[axisCount];
        var p = o + 8;
        for (var a = 0; a < axisCount; a++)
        {
            var count = r.U16(p);
            p += 2;
            var map = new (double, double)[count];
            for (var k = 0; k < count; k++, p += 4) map[k] = (F2Dot14(r, p), F2Dot14(r, p + 2));
            maps[a] = map;
        }
        return maps;
    }

    private static double Map((double From, double To)[] map, double v)
    {
        for (var k = 1; k < map.Length; k++)
            if (v <= map[k].From)
            {
                var (f0, t0) = map[k - 1];
                var (f1, t1) = map[k];
                return f1 == f0 ? t1 : t0 + (v - f0) * (t1 - t0) / (f1 - f0);
            }
        return v;
    }

    /// <summary>HVAR：字符 → （正規化座標 → 前進寬度的變化量）。讀不了回 null。</summary>
    private static Func<int, double[], double>? ReadHvar(SfntReader r, int axisCount)
    {
        try
        {
            var hvar = r.TableOffset("HVAR");
            var store = hvar + (int)r.U32(hvar + 4);
            var mapOffset = r.U32(hvar + 8);

            var evaluate = ItemVariationStore(r, store, axisCount);
            if (evaluate is null) return null;

            // DeltaSetIndexMap（沒有就是 outer 0、inner = 字符編號）
            (int Outer, int Inner) Index(int glyph)
            {
                if (mapOffset == 0) return (0, glyph);
                var map = hvar + (int)mapOffset;
                int format = r.U8(map), entryFormat = r.U8(map + 1);
                var mapCount = format == 0 ? r.U16(map + 2) : (int)r.U32(map + 2);
                var start = map + (format == 0 ? 4 : 6);
                var size = ((entryFormat >> 4) & 3) + 1;
                var innerBits = (entryFormat & 0xF) + 1;
                var at = start + Math.Min(glyph, mapCount - 1) * size;
                var v = 0;
                for (var b = 0; b < size; b++) v = (v << 8) | r.U8(at + b);
                return (v >> innerBits, v & ((1 << innerBits) - 1));
            }

            return (glyph, coords) =>
            {
                var (outer, inner) = Index(glyph);
                return evaluate(outer, inner, coords);
            };
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// ItemVariationStore（HVAR、GDEF 共用的格式）：(outer, inner, 正規化座標) → 變化量。讀不了回 null。
    /// </summary>
    internal static Func<int, int, double[], double>? ItemVariationStore(SfntReader r, int store, int axisCount)
    {
        var regionList = store + (int)r.U32(store + 2);
        if (r.U16(regionList) != axisCount) return null;
        var regionCount = r.U16(regionList + 2);
        var regions = new (double Start, double Peak, double End)[regionCount][];
        for (var k = 0; k < regionCount; k++)
        {
            regions[k] = new (double, double, double)[axisCount];
            for (var a = 0; a < axisCount; a++)
            {
                var o = regionList + 4 + (k * axisCount + a) * 6;
                regions[k][a] = (F2Dot14(r, o), F2Dot14(r, o + 2), F2Dot14(r, o + 4));
            }
        }
        var dataCount = r.U16(store + 6);
        var data = new int[dataCount];
        for (var d = 0; d < dataCount; d++) data[d] = store + (int)r.U32(store + 8 + d * 4);

        return (outer, inner, coords) =>
        {
            if (outer >= data.Length) return 0;
            var d = data[outer];
            int itemCount = r.U16(d), wordDeltaCount = r.U16(d + 2), regionIndexCount = r.U16(d + 4);
            if (inner >= itemCount) return 0;
            var longWords = (wordDeltaCount & 0x8000) != 0;
            var wordCount = wordDeltaCount & 0x7FFF;
            var rowSize = longWords ? wordCount * 4 + (regionIndexCount - wordCount) * 2 : wordCount * 2 + (regionIndexCount - wordCount);
            var p = d + 6 + regionIndexCount * 2 + inner * rowSize;
            double total = 0;
            for (var k = 0; k < regionIndexCount; k++)
            {
                int delta;
                if (k < wordCount) { delta = longWords ? unchecked((int)r.U32(p)) : r.S16(p); p += longWords ? 4 : 2; }
                else { delta = longWords ? r.S16(p) : unchecked((sbyte)r.U8(p)); p += longWords ? 2 : 1; }
                if (delta == 0) continue;
                var region = regions[r.U16(d + 6 + k * 2)];
                double scalar = 1;
                for (var a = 0; a < axisCount && scalar > 0; a++)
                {
                    var (st, peak, e) = region[a];
                    var c = coords[a];
                    if (peak == 0 || st > peak || peak > e || st < 0 && e > 0 || c == peak) continue;
                    if (c <= st || c >= e) { scalar = 0; break; }
                    scalar *= c < peak ? (c - st) / (peak - st) : (e - c) / (e - peak);
                }
                total += delta * scalar;
            }
            return total;
        };
    }

    /// <summary>GDEF 的 ItemVariationStore（GPOS 的字距變化量在這裡）；沒有回 null。</summary>
    internal static Func<int, int, double[], double>? GdefStore(SfntReader r, int axisCount)
    {
        if (!r.Has("GDEF")) return null;
        var gdef = r.TableOffset("GDEF");
        if (r.U16(gdef) != 1 || r.U16(gdef + 2) < 3) return null;
        var offset = r.U32(gdef + 14);
        return offset == 0 ? null : ItemVariationStore(r, gdef + (int)offset, axisCount);
    }
}
