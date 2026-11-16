namespace XamlFit.Tests;

/// <summary>
/// 組出一個最小但合法的 sfnt 位元組陣列，給解析器當確定性測試輸入。
///
/// 為什麼不拿系統字型測：CI 上沒有 Segoe UI，而拿「機器上剛好有的字型」測，
/// 等於測試結果隨機器變動。合成字型讓「cmap format 4 的 idRangeOffset 算錯」
/// 這種錯有一個穩定的照妖鏡。
/// </summary>
internal static class SyntheticFont
{
    /// <summary>
    /// 產生一支字型：unitsPerEm=1000，字符 0=.notdef(500)、1='A'(600)、2='B'(700)、3='中'(1000)。
    /// cmap 用 format 4（'A'=0x41、'B'=0x42 連續一段；'中'=0x4E2D 自己一段）。
    /// </summary>
    public static byte[] Build(string familyName = "Test Sans", int weight = 400, bool italic = false) =>
        Assemble(BaseTables(familyName, weight, italic, []));

    /// <summary>
    /// 可變字型版：wght 軸 300–700（預設 400），三個具名實例「Light Text」「Regular」「Bold Text」，
    /// HVAR 讓 'A' 在 wght=700 寬 60、在 300 窄 30（'B' 是 70 與 40），沒有寬度對應表（inner = 字符編號）。
    /// </summary>
    public static byte[] BuildVariable(string familyName = "Test Var")
    {
        var fvar = new byte[16 + 20 + 3 * 8];
        WriteU16(fvar, 0, 1);                    // majorVersion
        WriteU16(fvar, 4, 16);                   // axesArrayOffset
        WriteU16(fvar, 6, 2);                    // reserved
        WriteU16(fvar, 8, 1);                    // axisCount
        WriteU16(fvar, 10, 20);                  // axisSize
        WriteU16(fvar, 12, 3);                   // instanceCount
        WriteU16(fvar, 14, 8);                   // instanceSize（沒有 postScriptNameID）
        System.Text.Encoding.ASCII.GetBytes("wght").CopyTo(fvar, 16);
        WriteU32(fvar, 20, 300u << 16);
        WriteU32(fvar, 24, 400u << 16);
        WriteU32(fvar, 28, 700u << 16);
        WriteU16(fvar, 34, 256);                 // axisNameID
        (int Name, uint Wght)[] instances = [(257, 300), (258, 400), (259, 700)];
        for (var i = 0; i < 3; i++)
        {
            var o = 36 + i * 8;
            WriteU16(fvar, o, (ushort)instances[i].Name);
            WriteU32(fvar, o + 4, instances[i].Wght << 16);
        }

        // HVAR：表頭 20 位元組，ItemVariationStore 接在後面
        var hvar = new byte[20 + 12 + 16 + 18];
        WriteU16(hvar, 0, 1);
        WriteU32(hvar, 4, 20);                   // itemVariationStoreOffset
        var store = 20;
        WriteU16(hvar, store, 1);                // format
        WriteU32(hvar, store + 2, 12);           // variationRegionListOffset
        WriteU16(hvar, store + 6, 1);            // itemVariationDataCount
        WriteU32(hvar, store + 8, 28);           // itemVariationDataOffsets[0]
        var regions = store + 12;
        WriteU16(hvar, regions, 1);              // axisCount
        WriteU16(hvar, regions + 2, 2);          // regionCount
        WriteU16(hvar, regions + 4, 0); WriteU16(hvar, regions + 6, 0x4000); WriteU16(hvar, regions + 8, 0x4000);        // 0 → 1
        WriteU16(hvar, regions + 10, 0xC000); WriteU16(hvar, regions + 12, 0xC000); WriteU16(hvar, regions + 14, 0);    // -1 → 0
        var data = store + 28;
        WriteU16(hvar, data, 4);                 // itemCount
        WriteU16(hvar, data + 2, 0);             // wordDeltaCount：全部一個位元組
        WriteU16(hvar, data + 4, 2);             // regionIndexCount
        WriteU16(hvar, data + 6, 0); WriteU16(hvar, data + 8, 1);
        sbyte[] deltas = [0, 0, 60, -30, 70, -40, 0, 0];
        for (var k = 0; k < deltas.Length; k++) hvar[data + 10 + k] = unchecked((byte)deltas[k]);

        return Assemble(BaseTables(familyName, 400, false,
            [(256, "Weight"), (257, "Light Text"), (258, "Regular"), (259, "Bold Text")],
            [("fvar", fvar), ("HVAR", hvar)]));
    }

    private static List<(string Tag, byte[] Data)> BaseTables(string familyName, int weight, bool italic, (int Id, string Value)[] extraNames,
        List<(string Tag, byte[] Data)>? extraTables = null)
    {
        var head = new byte[54];
        WriteU32(head, 0, 0x00010000);          // version
        WriteU32(head, 12, 0x5F0F3CF5);         // magicNumber
        WriteU16(head, 18, 1000);               // unitsPerEm
        WriteU16(head, 44, (ushort)(italic ? 0x0002 : 0));   // macStyle
        WriteU16(head, 50, 0);                  // indexToLocFormat

        var maxp = new byte[6];
        WriteU32(maxp, 0, 0x00010000);
        WriteU16(maxp, 4, 4);                   // numGlyphs

        var hhea = new byte[36];
        WriteU32(hhea, 0, 0x00010000);
        WriteU16(hhea, 4, 800);                 // ascender
        WriteU16(hhea, 6, unchecked((ushort)-200));  // descender
        WriteU16(hhea, 8, 0);                   // lineGap
        WriteU16(hhea, 34, 4);                  // numberOfHMetrics

        var hmtx = new byte[4 * 4];
        int[] advances = [500, 600, 700, 1000];
        for (var i = 0; i < 4; i++) { WriteU16(hmtx, i * 4, (ushort)advances[i]); WriteU16(hmtx, i * 4 + 2, 0); }

        var cmap = BuildCmapFormat4();
        var os2 = new byte[78];
        WriteU16(os2, 0, 1);                    // version
        WriteU16(os2, 4, (ushort)weight);       // usWeightClass
        WriteU16(os2, 6, 5);                    // usWidthClass = Normal
        WriteU16(os2, 62, (ushort)(italic ? 0x0001 : 0));    // fsSelection

        var name = BuildName([(1, familyName), (2, italic ? "Italic" : "Regular"), .. extraNames]);

        return
        [
            ("OS/2", os2), ("cmap", cmap), ("head", head),
            ("hhea", hhea), ("hmtx", hmtx), ("maxp", maxp), ("name", name), .. extraTables ?? [],
        ];
    }

    private static byte[] BuildCmapFormat4()
    {
        // 兩個實際區段 + 必要的 0xFFFF 結尾區段
        int[] starts = [0x41, 0x4E2D, 0xFFFF];
        int[] ends = [0x42, 0x4E2D, 0xFFFF];
        int[] deltas = [1 - 0x41, 3 - 0x4E2D, 1];   // 'A'→字符1、'B'→字符2、'中'→字符3
        var segCount = starts.Length;

        var sub = new byte[14 + segCount * 8 + 2];
        WriteU16(sub, 0, 4);                       // format
        WriteU16(sub, 2, (ushort)sub.Length);      // length
        WriteU16(sub, 4, 0);                       // language
        WriteU16(sub, 6, (ushort)(segCount * 2));  // segCountX2
        for (var i = 0; i < segCount; i++) WriteU16(sub, 14 + i * 2, (ushort)ends[i]);
        WriteU16(sub, 14 + segCount * 2, 0);       // reservedPad
        for (var i = 0; i < segCount; i++) WriteU16(sub, 16 + segCount * 2 + i * 2, (ushort)starts[i]);
        for (var i = 0; i < segCount; i++) WriteU16(sub, 16 + segCount * 4 + i * 2, unchecked((ushort)deltas[i]));
        for (var i = 0; i < segCount; i++) WriteU16(sub, 16 + segCount * 6 + i * 2, 0);   // idRangeOffset

        var table = new byte[4 + 8 + sub.Length];
        WriteU16(table, 0, 0);                     // version
        WriteU16(table, 2, 1);                     // numTables
        WriteU16(table, 4, 3);                     // platformID = Windows
        WriteU16(table, 6, 1);                     // encodingID = BMP
        WriteU32(table, 8, 12);                    // offset to subtable
        Array.Copy(sub, 0, table, 12, sub.Length);
        return table;
    }

    private static byte[] BuildName((int Id, string Value)[] names)
    {
        var strings = names.Select(n => System.Text.Encoding.BigEndianUnicode.GetBytes(n.Value)).ToArray();
        var storage = 6 + names.Length * 12;
        var table = new byte[storage + strings.Sum(x => x.Length)];
        WriteU16(table, 0, 0);                     // format
        WriteU16(table, 2, (ushort)names.Length);  // count
        WriteU16(table, 4, (ushort)storage);       // stringOffset
        var off = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var o = 6 + i * 12;
            WriteU16(table, o, 3);                 // platformID Windows
            WriteU16(table, o + 2, 1);             // encodingID UCS-2
            WriteU16(table, o + 4, 0x0409);        // languageID en-US
            WriteU16(table, o + 6, (ushort)names[i].Id);
            WriteU16(table, o + 8, (ushort)strings[i].Length);
            WriteU16(table, o + 10, (ushort)off);
            Array.Copy(strings[i], 0, table, storage + off, strings[i].Length);
            off += strings[i].Length;
        }
        return table;
    }

    private static byte[] Assemble(List<(string Tag, byte[] Data)> tables)
    {
        tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));   // 規格要求 tag 升冪
        var n = tables.Count;
        var dirSize = 12 + n * 16;
        var total = dirSize + tables.Sum(t => Align4(t.Data.Length));
        var buf = new byte[total];

        WriteU32(buf, 0, 0x00010000);
        WriteU16(buf, 4, (ushort)n);

        var offset = dirSize;
        for (var i = 0; i < n; i++)
        {
            var (tag, data) = tables[i];
            var rec = 12 + i * 16;
            System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(buf, rec);
            WriteU32(buf, rec + 8, (uint)offset);
            WriteU32(buf, rec + 12, (uint)data.Length);
            data.CopyTo(buf, offset);
            offset += Align4(data.Length);
        }

        return buf;
    }

    private static int Align4(int n) => (n + 3) & ~3;
    private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
    private static void WriteU32(byte[] b, int o, uint v)
    { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
}
