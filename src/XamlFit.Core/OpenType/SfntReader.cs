namespace XamlFit.Core.OpenType;

/// <summary>
/// OpenType／TrueType 檔案的最小讀取器。
///
/// **只讀度量需要的五張表**：head、hhea、hmtx、cmap、OS/2（外加 maxp 取字符數、name 取名稱）。
/// 不解字形外框、不做 shaping、不光柵化 —— 那些是 SkiaSharp／HarfBuzz 的事，而本工具
/// 刻意不相依它們（規畫書第 8 節）。
///
/// 位元組序：sfnt 一律是 big-endian。
/// </summary>
internal sealed class SfntReader
{
    private readonly byte[] _data;

    private SfntReader(byte[] data) => _data = data;

    /// <summary>表目錄：四字母 tag → (offset, length)。</summary>
    public Dictionary<string, (uint Offset, uint Length)> Tables { get; } = new(StringComparer.Ordinal);

    public static IReadOnlyList<SfntReader> Open(byte[] data)
    {
        if (data.Length < 12) throw new InvalidDataException("file too short to be a font");

        var tag = ReadTag(data, 0);

        // TTC（字型集合）：裡面有多支字型，各自有自己的表目錄
        if (tag == "ttcf")
        {
            var numFonts = ReadU32(data, 8);
            if (numFonts > 1024) throw new InvalidDataException($"implausible TTC font count: {numFonts}");
            var list = new List<SfntReader>((int)numFonts);
            for (var i = 0u; i < numFonts; i++)
            {
                var off = ReadU32(data, (int)(12 + i * 4));
                var r = new SfntReader(data);
                r.ReadDirectory((int)off);
                list.Add(r);
            }
            return list;
        }

        // 0x00010000 = TrueType outlines；'OTTO' = CFF outlines；'true'/'typ1' 是舊 Mac
        var ver = ReadU32(data, 0);
        if (ver != 0x00010000 && tag is not ("OTTO" or "true" or "typ1"))
            throw new InvalidDataException($"not an sfnt font (sfntVersion=0x{ver:X8})");

        var single = new SfntReader(data);
        single.ReadDirectory(0);
        return [single];
    }

    private void ReadDirectory(int baseOffset)
    {
        var numTables = ReadU16(_data, baseOffset + 4);
        for (var i = 0; i < numTables; i++)
        {
            var rec = baseOffset + 12 + i * 16;
            if (rec + 16 > _data.Length) break;
            var tag = ReadTag(_data, rec);
            var off = ReadU32(_data, rec + 8);
            var len = ReadU32(_data, rec + 12);
            if (off >= (uint)_data.Length) continue;           // 壞掉的表直接跳過，不要讓整支字型讀不了
            if (off + len > (uint)_data.Length) len = (uint)_data.Length - off;
            Tables[tag] = (off, len);
        }
    }

    public bool Has(string tag) => Tables.ContainsKey(tag);

    public int TableOffset(string tag) =>
        Tables.TryGetValue(tag, out var t)
            ? (int)t.Offset
            : throw new InvalidDataException($"required table '{tag}' is missing");

    public int TableLength(string tag) => Tables.TryGetValue(tag, out var t) ? (int)t.Length : 0;

    public byte U8(int o) => _data[o];
    public ushort U16(int o) => ReadU16(_data, o);
    public short S16(int o) => unchecked((short)ReadU16(_data, o));
    public uint U32(int o) => ReadU32(_data, o);
    public int Length => _data.Length;

    public string Utf16BeString(int offset, int byteLength)
    {
        if (offset < 0 || offset + byteLength > _data.Length || byteLength <= 0) return "";
        var sb = new System.Text.StringBuilder(byteLength / 2);
        for (var i = 0; i + 1 < byteLength; i += 2)
            sb.Append((char)ReadU16(_data, offset + i));
        return sb.ToString();
    }

    public string AsciiString(int offset, int byteLength)
    {
        if (offset < 0 || offset + byteLength > _data.Length || byteLength <= 0) return "";
        return System.Text.Encoding.ASCII.GetString(_data, offset, byteLength);
    }

    private static string ReadTag(byte[] d, int o) =>
        o + 4 <= d.Length ? System.Text.Encoding.ASCII.GetString(d, o, 4) : "";

    private static ushort ReadU16(byte[] d, int o) =>
        o + 2 <= d.Length ? (ushort)((d[o] << 8) | d[o + 1]) : (ushort)0;

    private static uint ReadU32(byte[] d, int o) =>
        o + 4 <= d.Length ? ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3] : 0u;
}
