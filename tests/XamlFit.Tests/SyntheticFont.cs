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
    public static byte[] Build(string familyName = "Test Sans", int weight = 400, bool italic = false)
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

        var name = BuildName(familyName, italic ? "Italic" : "Regular");

        return Assemble(
        [
            ("OS/2", os2), ("cmap", cmap), ("head", head),
            ("hhea", hhea), ("hmtx", hmtx), ("maxp", maxp), ("name", name),
        ]);
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

    private static byte[] BuildName(string family, string subfamily)
    {
        var fam = System.Text.Encoding.BigEndianUnicode.GetBytes(family);
        var sub = System.Text.Encoding.BigEndianUnicode.GetBytes(subfamily);
        var storage = 6 + 2 * 12;
        var table = new byte[storage + fam.Length + sub.Length];
        WriteU16(table, 0, 0);                     // format
        WriteU16(table, 2, 2);                     // count
        WriteU16(table, 4, (ushort)storage);       // stringOffset

        void Rec(int i, int nameId, int off, int len)
        {
            var o = 6 + i * 12;
            WriteU16(table, o, 3);                 // platformID Windows
            WriteU16(table, o + 2, 1);             // encodingID UCS-2
            WriteU16(table, o + 4, 0x0409);        // languageID en-US
            WriteU16(table, o + 6, (ushort)nameId);
            WriteU16(table, o + 8, (ushort)len);
            WriteU16(table, o + 10, (ushort)off);
        }

        Rec(0, 1, 0, fam.Length);
        Rec(1, 2, fam.Length, sub.Length);
        Array.Copy(fam, 0, table, storage, fam.Length);
        Array.Copy(sub, 0, table, storage + fam.Length, sub.Length);
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
