using XamlFit.Core.Fonts;

namespace XamlFit.Core.OpenType;

/// <summary>
/// 從字型檔萃取度量。讀的表：
///
/// | 表    | 取什麼 |
/// |-------|--------|
/// | head  | unitsPerEm |
/// | maxp  | numGlyphs |
/// | hhea  | ascender／descender／lineGap／numberOfHMetrics |
/// | hmtx  | 每個字符的 advance width |
/// | cmap  | 碼位 → 字符（format 4／6／12） |
/// | OS/2  | usWeightClass、usWidthClass、fsSelection（斜體旗標） |
/// | name  | 字型家族名與子家族名 |
/// </summary>
public static class FontMetricsExtractor
{
    /// <param name="wantFamily">只要這些家族（讀完名稱就判斷，不要的字面不跑字距、阿拉伯文、印度系文字的形狀表）；null = 全部。</param>
    public static IReadOnlyList<FontMetrics> FromFile(string path, Func<string, bool>? wantFamily = null) => FromBytes(File.ReadAllBytes(path), wantFamily);

    public static IReadOnlyList<FontMetrics> FromBytes(byte[] bytes, Func<string, bool>? wantFamily = null)
    {
        var faces = SfntReader.Open(bytes);
        var result = new List<FontMetrics>(faces.Count);
        foreach (var f in faces)
        {
            var m = TryExtract(f, wantFamily);
            if (m is not null) result.Add(m);
        }
        return result;
    }

    private static FontMetrics? TryExtract(SfntReader r, Func<string, bool>? wantFamily)
    {
        // 五張表缺一不可。缺了就讀不出度量 —— 回 null 讓呼叫端記成「這支讀不了」，
        // 不要丟例外讓整批匯出失敗（一個資料夾裡有一支壞字型是常態）。
        if (!r.Has("head") || !r.Has("hhea") || !r.Has("hmtx") || !r.Has("cmap") || !r.Has("maxp"))
            return null;

        var head = r.TableOffset("head");
        var unitsPerEm = r.U16(head + 18);
        if (unitsPerEm == 0) return null;

        var numGlyphs = r.U16(r.TableOffset("maxp") + 4);
        if (numGlyphs == 0) return null;

        var hhea = r.TableOffset("hhea");
        var ascender = r.S16(hhea + 4);
        var descender = r.S16(hhea + 6);
        var lineGap = r.S16(hhea + 8);
        var numberOfHMetrics = r.U16(hhea + 34);
        if (numberOfHMetrics == 0) return null;

        var advances = ReadHmtx(r, numGlyphs, numberOfHMetrics);
        var cmap = ReadCmap(r);
        if (cmap.Count == 0) return null;

        var (weight, stretch, italic) = ReadOs2(r, head);
        var (family, subfamily, legacyFamily, aliases) = ReadNames(r);
        if (string.IsNullOrWhiteSpace(family)) family = Path.GetFileNameWithoutExtension("unknown");
        if (wantFamily is not null && !wantFamily(family)) return null;

        // 子家族名有時才是斜體的唯一來源（OS/2 的 fsSelection 不一定可靠）
        if (!italic && subfamily.Contains("italic", StringComparison.OrdinalIgnoreCase)) italic = true;
        if (weight == 400 && subfamily.Contains("bold", StringComparison.OrdinalIgnoreCase)) weight = 700;

        return new FontMetrics
        {
            Family = family,
            Weight = weight,
            Style = italic ? FontStyleKind.Italic : FontStyleKind.Normal,
            Stretch = stretch,
            LegacyFamily = string.Equals(legacyFamily, family, StringComparison.OrdinalIgnoreCase) ? null : legacyFamily,
            Aliases = aliases,
            // ⚠ 可變字型：hmtx 只有預設軸位置的寬度。標記出來，解析時只准用這一個字重。
            Variable = r.Has("fvar"),
            UnitsPerEm = unitsPerEm,
            Ascender = ascender,
            Descender = descender,
            LineGap = lineGap,
            DefaultAdvance = advances.Length > 0 ? advances[0] : unitsPerEm / 2,
            Ranges = BuildRanges(cmap, advances),
            Kerning = GposKerning.Read(r, GlyphToCodepoints(cmap)),
            Arabic = ArabicExport.Read(r, cmap, advances),
            Hinted = ReadHdmx(r, cmap, advances, unitsPerEm, numGlyphs),
            Indic = IndicExport.Read(r, cmap, advances)?.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value),
        };
    }

    /// <summary>
    /// hdmx：各像素大小 hinting 後的整數寬度（Display 模式用）。存跟「線性寬度 0.5 進位」的差、只列非零；
    /// 只收 72 px 以下（再大的字 Display 模式跟線性幾乎一樣，用不到）。
    /// </summary>
    private static Dictionary<int, IReadOnlyDictionary<int, int>>? ReadHdmx(SfntReader r, SortedDictionary<int, int> cmap, int[] advances, int unitsPerEm, int numGlyphs)
    {
        if (!r.Has("hdmx")) return null;
        try
        {
            var hdmx = r.TableOffset("hdmx");
            var count = r.U16(hdmx + 2);
            var recordSize = (int)r.U32(hdmx + 4);
            if (recordSize < 2 + numGlyphs) return null;
            var result = new Dictionary<int, IReadOnlyDictionary<int, int>>();
            for (var i = 0; i < count; i++)
            {
                var rec = hdmx + 8 + i * recordSize;
                int ppem = r.U8(rec);
                if (ppem is 0 or > 72 || result.ContainsKey(ppem)) continue;
                var deltas = new Dictionary<int, int>();
                foreach (var (cp, glyph) in cmap)
                {
                    if (glyph >= numGlyphs || glyph >= advances.Length) continue;
                    var width = r.U8(rec + 2 + glyph);
                    var delta = width - Fonts.TextMeasure.RoundHalfUp((double)advances[glyph] * ppem / unitsPerEm);
                    if (delta != 0) deltas[cp] = delta;
                }
                result[ppem] = deltas;
            }
            return result.Count > 0 ? result : null;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static Dictionary<int, List<int>> GlyphToCodepoints(SortedDictionary<int, int> cmap)
    {
        var map = new Dictionary<int, List<int>>();
        foreach (var (cp, glyph) in cmap)
        {
            if (glyph == 0) continue;
            if (!map.TryGetValue(glyph, out var list)) map[glyph] = list = [];
            list.Add(cp);
        }
        return map;
    }

    /// <summary>hmtx：前 numberOfHMetrics 筆有自己的 advance，之後的字符沿用最後一筆
    /// （這是規格，不是最佳化 —— 等寬的尾段字符就是這樣存的）。</summary>
    private static int[] ReadHmtx(SfntReader r, int numGlyphs, int numberOfHMetrics)
    {
        var hmtx = r.TableOffset("hmtx");
        var adv = new int[numGlyphs];
        var last = 0;
        for (var g = 0; g < numGlyphs; g++)
        {
            if (g < numberOfHMetrics)
            {
                var o = hmtx + g * 4;
                last = o + 2 <= r.Length ? r.U16(o) : last;
            }
            adv[g] = last;
        }
        return adv;
    }

    /// <summary>cmap：挑最好的子表。(3,10) format 12 蓋得到 BMP 以外；(3,1) format 4 是最常見的。</summary>
    private static SortedDictionary<int, int> ReadCmap(SfntReader r)
    {
        var cmap = r.TableOffset("cmap");
        var numTables = r.U16(cmap + 2);

        int bestOffset = -1, bestScore = -1;
        for (var i = 0; i < numTables; i++)
        {
            var rec = cmap + 4 + i * 8;
            var platform = r.U16(rec);
            var encoding = r.U16(rec + 2);
            var off = (int)r.U32(rec + 4);
            var score = (platform, encoding) switch
            {
                (3, 10) => 100,   // Windows UCS-4
                (0, 6) => 95,     // Unicode full
                (0, 4) => 95,
                (3, 1) => 90,     // Windows BMP —— 絕大多數字型
                (0, 3) => 85,
                (0, _) => 80,
                (3, 0) => 50,     // Symbol：碼位落在 0xF000 區，另外處理
                _ => 10,
            };
            if (score > bestScore) { bestScore = score; bestOffset = cmap + off; }
        }

        var map = new SortedDictionary<int, int>();
        if (bestOffset < 0) return map;

        var format = r.U16(bestOffset);
        switch (format)
        {
            case 0: ReadCmap0(r, bestOffset, map); break;
            case 4: ReadCmap4(r, bestOffset, map); break;
            case 6: ReadCmap6(r, bestOffset, map); break;
            case 12: ReadCmap12(r, bestOffset, map); break;
        }

        // Symbol 字型把字放在 0xF000~0xF0FF，使用端寫的卻是 0x00~0xFF
        if (bestScore == 50)
        {
            foreach (var (cp, g) in map.Where(kv => kv.Key is >= 0xF000 and <= 0xF0FF).ToList())
                map.TryAdd(cp - 0xF000, g);
        }

        return map;
    }

    private static void ReadCmap0(SfntReader r, int o, SortedDictionary<int, int> map)
    {
        for (var c = 0; c < 256; c++) map[c] = r.U8(o + 6 + c);
    }

    private static void ReadCmap6(SfntReader r, int o, SortedDictionary<int, int> map)
    {
        var first = r.U16(o + 6);
        var count = r.U16(o + 8);
        for (var i = 0; i < count; i++) map[first + i] = r.U16(o + 10 + i * 2);
    }

    private static void ReadCmap4(SfntReader r, int o, SortedDictionary<int, int> map)
    {
        var segCount = r.U16(o + 6) / 2;
        var endBase = o + 14;
        var startBase = endBase + segCount * 2 + 2;      // +2 是 reservedPad
        var deltaBase = startBase + segCount * 2;
        var rangeBase = deltaBase + segCount * 2;

        for (var s = 0; s < segCount; s++)
        {
            int end = r.U16(endBase + s * 2);
            int start = r.U16(startBase + s * 2);
            int delta = r.S16(deltaBase + s * 2);
            int rangeOffset = r.U16(rangeBase + s * 2);
            if (start > end) continue;
            if (start == 0xFFFF) continue;

            for (var c = start; c <= end && c <= 0xFFFF; c++)
            {
                int g;
                if (rangeOffset == 0)
                {
                    g = (c + delta) & 0xFFFF;
                }
                else
                {
                    // 規格的指標算式：&idRangeOffset[s] + rangeOffset + 2*(c - start)
                    var gi = rangeBase + s * 2 + rangeOffset + (c - start) * 2;
                    if (gi + 2 > r.Length) continue;
                    g = r.U16(gi);
                    if (g != 0) g = (g + delta) & 0xFFFF;
                }
                if (g != 0) map[c] = g;
            }
        }
    }

    private static void ReadCmap12(SfntReader r, int o, SortedDictionary<int, int> map)
    {
        var nGroups = (int)r.U32(o + 12);
        if (nGroups is < 0 or > 200_000) return;     // 壞資料的防線
        for (var i = 0; i < nGroups; i++)
        {
            var g = o + 16 + i * 12;
            var start = (int)r.U32(g);
            var end = (int)r.U32(g + 4);
            var startGid = (int)r.U32(g + 8);
            if (start > end || end - start > 0x11_0000) continue;
            for (var c = start; c <= end; c++) map[c] = startGid + (c - start);
        }
    }

    private static (int Weight, string Stretch, bool Italic) ReadOs2(SfntReader r, int head)
    {
        var weight = 400;
        var stretch = "Normal";
        var italic = false;

        if (r.Has("OS/2"))
        {
            var os2 = r.TableOffset("OS/2");
            var w = r.U16(os2 + 4);
            if (w is >= 1 and <= 1000) weight = w < 100 ? w * 100 : w;   // 有些舊字型寫 1~9
            var widthClass = r.U16(os2 + 6);
            stretch = widthClass switch
            {
                1 => "UltraCondensed", 2 => "ExtraCondensed", 3 => "Condensed", 4 => "SemiCondensed",
                6 => "SemiExpanded", 7 => "Expanded", 8 => "ExtraExpanded", 9 => "UltraExpanded",
                _ => "Normal",
            };
            var fsSelection = r.U16(os2 + 62);
            italic = (fsSelection & 0x01) != 0;
        }
        else
        {
            // 沒有 OS/2 就看 head.macStyle
            var macStyle = r.U16(head + 44);
            italic = (macStyle & 0x02) != 0;
            if ((macStyle & 0x01) != 0) weight = 700;
        }

        return (weight, stretch, italic);
    }

    /// <summary>name 表：優先取 typographic family（16／17），沒有才退回 1／2。
    /// ⚠ WPF 的 FontFamily 比對的是 typographic family，用 nameID 1 會讓
    ///   「Segoe UI Semibold」被當成獨立家族而對不上 Segoe UI + Weight=600。</summary>
    /// <summary>
    /// 家族名與子家族名。
    ///
    /// ⚠ **要挑英文（語言 0x0409）的名字。** 字型的 name 表裡同一個名字有好幾種語言的版本，
    ///   而 msjh.ttc 的第一筆是繁體中文的「微軟正黑體」。第一版拿第一筆，於是匯出來的家族叫
    ///   「微軟正黑體」，XAML 裡寫的 <c>FontFamily="Microsoft JhengHei"</c> 對不上 ——
    ///   整支字型變成「沒有度量」。WPF 兩種名字都認，所以其他語言的名字記成別名。
    /// </summary>
    private static (string Family, string Subfamily, string? LegacyFamily, List<string> Aliases) ReadNames(SfntReader r)
    {
        if (!r.Has("name")) return ("", "", null, []);
        var name = r.TableOffset("name");
        var count = r.U16(name + 2);
        var storage = name + r.U16(name + 4);

        // nameId → (英文優先的那一筆, 所有語言的版本)
        var best = new Dictionary<int, (string Value, int Rank)>();
        var all = new Dictionary<int, List<string>>();

        for (var i = 0; i < count; i++)
        {
            var rec = name + 6 + i * 12;
            var platform = r.U16(rec);
            var encoding = r.U16(rec + 2);
            var language = r.U16(rec + 4);
            var nameId = r.U16(rec + 6);
            var len = r.U16(rec + 8);
            var off = r.U16(rec + 10);

            if (nameId is not (1 or 2 or 16 or 17)) continue;

            // 平台 3（Windows）是 UTF-16BE；平台 1（Mac）是單位元組
            var s = platform == 3 || (platform == 0 && encoding is not 0)
                ? r.Utf16BeString(storage + off, len)
                : r.AsciiString(storage + off, len);

            if (string.IsNullOrWhiteSpace(s)) continue;
            s = s.Trim();

            // 排名：Windows 英文（美國）> Windows 其他英文 > Unicode 平台 > Mac 英文 > 其他
            var rank = (platform, language) switch
            {
                (3, 0x0409) => 0,
                (3, _) when (language & 0x3FF) == 0x09 => 1,
                (0, _) => 2,
                (1, 0) => 3,
                _ => 4,
            };

            if (!best.TryGetValue(nameId, out var current) || rank < current.Rank) best[nameId] = (s, rank);

            if (!all.TryGetValue(nameId, out var list)) all[nameId] = list = [];
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        }

        string? Pick(int id) => best.TryGetValue(id, out var b) ? b.Value : null;

        var typoFamily = Pick(16);
        var family = typoFamily ?? Pick(1) ?? "";
        var familyId = typoFamily is not null ? 16 : 1;

        // 別名 = 家族名的其他語言版本（「微軟正黑體」）
        var aliases = all.TryGetValue(familyId, out var names)
            ? names.Where(n => !n.Equals(family, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

        return (family, Pick(17) ?? Pick(2) ?? "", typoFamily is null ? null : Pick(1), aliases);
    }

    /// <summary>把 碼位→字符→advance 壓成連續區間。</summary>
    private static List<AdvanceRange> BuildRanges(SortedDictionary<int, int> cmap, int[] advances)
    {
        var ranges = new List<AdvanceRange>();
        var started = false;
        int rs = 0, re = 0, ra = 0;

        foreach (var (cp, gid) in cmap)
        {
            var adv = gid >= 0 && gid < advances.Length ? advances[gid] : 0;

            // ⚠ 只有「碼位連續」而且「寬度相同」才合併 ——
            //   跨過空洞合併，等於宣稱字型畫得出它其實畫不出的字。
            if (started && cp == re + 1 && adv == ra) { re = cp; continue; }

            if (started) ranges.Add(new AdvanceRange(rs, re, ra));
            rs = re = cp;
            ra = adv;
            started = true;
        }

        if (started) ranges.Add(new AdvanceRange(rs, re, ra));
        return ranges;
    }
}
