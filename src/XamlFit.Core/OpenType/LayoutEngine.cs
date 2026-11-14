namespace XamlFit.Core.OpenType;

/// <summary>
/// 匯出度量時用的小型 GSUB／GPOS 引擎：把一串字符照字型的替換規則跑一遍，再套定位規則裡改寬度的部分，
/// 只為了算出**寬度**（見 ArabicExport）。執行期不用它 —— 度量檔裡只存它算出來的數字，不存字型的表。
///
/// GSUB 支援的 lookup：1 單一、2 多重、4 連字、5 上下文、6 串接上下文（三種格式）、7 延伸、8 反向串接。
/// 3（替代字形）沒有預設值，不套。
/// GPOS 支援的 lookup：1 單一調整、2 成對調整（兩種格式）、7 上下文、8 串接上下文、9 延伸；只取 XAdvance
/// （XPlacement、y 方向、裝置表都不改一行的寬度）。附著類（3–6）不改前進寬度，不套。
/// LookupFlag 不處理 —— 呼叫端只餵沒有附加符號的字母序列。
/// 照 OpenType 的規則：lookup 依 LookupList 的順序套；同一個 lookup 在每個位置用第一個適用的子表。
/// </summary>
internal sealed class LayoutEngine
{
    /// <summary>Adv：GPOS 套上去的前進寬度改變量（字型單位）。</summary>
    public readonly record struct Glyph(int Id, int Cluster, string? Form, int Adv = 0);

    private readonly SfntReader _r;
    private readonly int _scriptList, _featureList, _lookupList, _lookupCount;
    private readonly bool _gpos;
    private readonly Dictionary<int, Dictionary<int, int>> _coverage = [];
    private readonly Dictionary<int, Dictionary<int, int>> _classes = [];

    /// <param name="table">"GSUB" 或 "GPOS"。</param>
    public LayoutEngine(SfntReader r, string table = "GSUB")
    {
        _r = r;
        _gpos = table == "GPOS";
        var gsub = r.TableOffset(table);
        _scriptList = gsub + r.U16(gsub + 4);
        _featureList = gsub + r.U16(gsub + 6);
        _lookupList = gsub + r.U16(gsub + 8);
        _lookupCount = r.U16(_lookupList);
    }

    /// <summary>文字系統 script 下語言 lang 的語言系統（"dflt" = 預設）。沒有回 -1。</summary>
    public int LangSys(string script, string lang)
    {
        var count = _r.U16(_scriptList);
        for (var s = 0; s < count; s++)
        {
            var rec = _scriptList + 2 + s * 6;
            if (_r.AsciiString(rec, 4) != script) continue;
            var table = _scriptList + _r.U16(rec + 4);
            if (lang == "dflt") return _r.U16(table) != 0 ? table + _r.U16(table) : -1;
            var langCount = _r.U16(table + 2);
            for (var l = 0; l < langCount; l++)
            {
                var lr = table + 4 + l * 6;
                if (_r.AsciiString(lr, 4) == lang) return table + _r.U16(lr + 4);
            }
            return -1;
        }
        return -1;
    }

    /// <summary>語言系統掛的 lookup → 掛它的特性。只收 features 裡列的特性。</summary>
    public SortedDictionary<int, HashSet<string>> Lookups(int langSys, IReadOnlySet<string> features)
    {
        var map = new SortedDictionary<int, HashSet<string>>();
        var count = _r.U16(langSys + 4);
        for (var i = 0; i < count; i++)
        {
            var fi = _r.U16(langSys + 6 + i * 2);
            var rec = _featureList + 2 + fi * 6;
            var tag = _r.AsciiString(rec, 4);
            if (!features.Contains(tag)) continue;
            var feature = _featureList + _r.U16(rec + 4);
            var lookupCount = _r.U16(feature + 2);
            for (var k = 0; k < lookupCount; k++)
            {
                var li = _r.U16(feature + 4 + k * 2);
                if (!map.TryGetValue(li, out var tags)) map[li] = tags = [];
                tags.Add(tag);
            }
        }
        return map;
    }

    /// <summary>
    /// 套用 lookups。形狀特性（isol／init／medi／fina）只套在 Form 相符的字符上，其他特性套在所有字符上。
    /// </summary>
    public List<Glyph> Shape(List<Glyph> buffer, SortedDictionary<int, HashSet<string>> lookups, IReadOnlySet<string> formFeatures)
    {
        var buf = new List<Glyph>(buffer);
        foreach (var (li, tags) in lookups)
        {
            var global = tags.Any(t => !formFeatures.Contains(t));
            bool Applies(Glyph g) => global || (g.Form is { } f && tags.Contains(f));
            ApplyLookup(li, buf, Applies);
        }
        return buf;
    }

    private int LookupOffset(int index) => index < _lookupCount ? _lookupList + _r.U16(_lookupList + 2 + index * 2) : -1;

    private IEnumerable<(int Type, int Offset)> Subtables(int lookup)
    {
        var type = _r.U16(lookup);
        var count = _r.U16(lookup + 4);
        for (var s = 0; s < count; s++)
        {
            var sub = lookup + _r.U16(lookup + 6 + s * 2);
            if (type == (_gpos ? 9 : 7)) yield return (_r.U16(sub + 2), sub + (int)_r.U32(sub + 4));
            else yield return (type, sub);
        }
    }

    private void ApplyLookup(int lookupIndex, List<Glyph> buf, Func<Glyph, bool> applies)
    {
        var lookup = LookupOffset(lookupIndex);
        if (lookup < 0) return;
        var subs = Subtables(lookup).ToList();
        if (!_gpos && subs.Count > 0 && subs[0].Type == 8)
        {
            for (var i = buf.Count - 1; i >= 0; i--)
                if (applies(buf[i])) foreach (var (_, sub) in subs) if (ReverseChain(sub, buf, i)) break;
            return;
        }
        var pos = 0;
        while (pos < buf.Count)
        {
            if (applies(buf[pos]) && ApplyAt(subs, buf, pos, out var next)) pos = Math.Max(next, pos + 1);
            else pos++;
        }
    }

    /// <summary>在一個位置套一個 lookup（巢狀呼叫也用它）。</summary>
    private bool ApplyAt(int lookupIndex, List<Glyph> buf, int pos)
    {
        var lookup = LookupOffset(lookupIndex);
        return lookup >= 0 && pos < buf.Count && ApplyAt(Subtables(lookup).ToList(), buf, pos, out _);
    }

    private bool ApplyAt(List<(int Type, int Offset)> subs, List<Glyph> buf, int pos, out int next)
    {
        next = pos + 1;
        if (_gpos) return Position(subs, buf, pos, out next);
        foreach (var (type, sub) in subs)
        {
            switch (type)
            {
                case 1:
                    if (Single(sub, buf[pos].Id) is { } g) { buf[pos] = buf[pos] with { Id = g }; return true; }
                    break;
                case 2:
                    if (Multiple(sub, buf[pos].Id) is { } seq)
                    {
                        var at = buf[pos];
                        buf.RemoveAt(pos);
                        buf.InsertRange(pos, seq.Select(id => at with { Id = id }));
                        next = pos + seq.Count;
                        return true;
                    }
                    break;
                case 4:
                    if (Ligature(sub, buf, pos)) return true;
                    break;
                case 5:
                    if (Context(sub, buf, pos, out next)) return true;
                    break;
                case 6:
                    if (ChainContext(sub, buf, pos, out next)) return true;
                    break;
            }
        }
        return false;
    }

    private bool Position(List<(int Type, int Offset)> subs, List<Glyph> buf, int pos, out int next)
    {
        next = pos + 1;
        foreach (var (type, sub) in subs)
        {
            switch (type)
            {
                case 1:
                    if (SinglePos(sub, buf[pos].Id) is { } adv) { buf[pos] = buf[pos] with { Adv = buf[pos].Adv + adv }; return true; }
                    break;
                case 2:
                    if (pos + 1 < buf.Count && PairPos(sub, buf[pos].Id, buf[pos + 1].Id) is var (a1, a2, second))
                    {
                        buf[pos] = buf[pos] with { Adv = buf[pos].Adv + a1 };
                        buf[pos + 1] = buf[pos + 1] with { Adv = buf[pos + 1].Adv + a2 };
                        next = second ? pos + 2 : pos + 1;
                        return true;
                    }
                    break;
                case 7:
                    if (Context(sub, buf, pos, out next)) return true;
                    break;
                case 8:
                    if (ChainContext(sub, buf, pos, out next)) return true;
                    break;
            }
        }
        return false;
    }

    private static int ValueSize(int format) => 2 * System.Numerics.BitOperations.PopCount((uint)format & 0xFF);

    private int XAdvance(int record, int format) =>
        (format & 0x4) == 0 ? 0 : _r.S16(record + 2 * System.Numerics.BitOperations.PopCount((uint)format & 0x3));

    private int? SinglePos(int sub, int glyph)
    {
        var index = CoverageIndex(sub + _r.U16(sub + 2), glyph);
        if (index < 0) return null;
        var format = _r.U16(sub + 4);
        return _r.U16(sub) switch
        {
            1 => XAdvance(sub + 6, format),
            2 => index < _r.U16(sub + 6) ? XAdvance(sub + 8 + index * ValueSize(format), format) : null,
            _ => null,
        };
    }

    /// <summary>成對調整：回傳兩個字的寬度改變量，以及第二個字是否被「用掉」（它有自己的值時，下一個位置跳過它）。</summary>
    private (int, int, bool)? PairPos(int sub, int first, int second)
    {
        var index = CoverageIndex(sub + _r.U16(sub + 2), first);
        if (index < 0) return null;
        int fmt1 = _r.U16(sub + 4), fmt2 = _r.U16(sub + 6);
        int size1 = ValueSize(fmt1), size2 = ValueSize(fmt2);
        if (_r.U16(sub) == 1)
        {
            if (index >= _r.U16(sub + 8)) return null;
            var set = sub + _r.U16(sub + 10 + index * 2);
            var count = _r.U16(set);
            var recordSize = 2 + size1 + size2;
            for (var i = 0; i < count; i++)
            {
                var rec = set + 2 + i * recordSize;
                if (_r.U16(rec) != second) continue;
                return (XAdvance(rec + 2, fmt1), XAdvance(rec + 2 + size1, fmt2), fmt2 != 0);
            }
            return null;
        }
        if (_r.U16(sub) == 2)
        {
            var c1 = ClassOf(sub + _r.U16(sub + 8), first);
            var c2 = ClassOf(sub + _r.U16(sub + 10), second);
            int c1Count = _r.U16(sub + 12), c2Count = _r.U16(sub + 14);
            if (c1 >= c1Count || c2 >= c2Count) return null;
            var rec = sub + 16 + (c1 * c2Count + c2) * (size1 + size2);
            return (XAdvance(rec, fmt1), XAdvance(rec + size1, fmt2), fmt2 != 0);
        }
        return null;
    }

    // ── 表格讀取 ──

    private int CoverageIndex(int offset, int glyph)
    {
        if (!_coverage.TryGetValue(offset, out var map))
        {
            map = [];
            var format = _r.U16(offset);
            var count = _r.U16(offset + 2);
            if (format == 1)
                for (var i = 0; i < count; i++) map[_r.U16(offset + 4 + i * 2)] = i;
            else if (format == 2)
                for (var i = 0; i < count; i++)
                {
                    var rec = offset + 4 + i * 6;
                    int start = _r.U16(rec), end = _r.U16(rec + 2), index = _r.U16(rec + 4);
                    for (var g = start; g <= end; g++) map[g] = index + g - start;
                }
            _coverage[offset] = map;
        }
        return map.TryGetValue(glyph, out var idx) ? idx : -1;
    }

    private int ClassOf(int offset, int glyph)
    {
        if (!_classes.TryGetValue(offset, out var map))
        {
            map = [];
            var format = _r.U16(offset);
            if (format == 1)
            {
                var start = _r.U16(offset + 2);
                var count = _r.U16(offset + 4);
                for (var i = 0; i < count; i++) map[start + i] = _r.U16(offset + 6 + i * 2);
            }
            else if (format == 2)
            {
                var count = _r.U16(offset + 2);
                for (var i = 0; i < count; i++)
                {
                    var rec = offset + 4 + i * 6;
                    int start = _r.U16(rec), end = _r.U16(rec + 2), cls = _r.U16(rec + 4);
                    for (var g = start; g <= end; g++) map[g] = cls;
                }
            }
            _classes[offset] = map;
        }
        return map.TryGetValue(glyph, out var c) ? c : 0;
    }

    private int? Single(int sub, int glyph)
    {
        var index = CoverageIndex(sub + _r.U16(sub + 2), glyph);
        if (index < 0) return null;
        return _r.U16(sub) switch
        {
            1 => (glyph + _r.S16(sub + 4)) & 0xFFFF,
            2 => index < _r.U16(sub + 4) ? _r.U16(sub + 6 + index * 2) : null,
            _ => null,
        };
    }

    private List<int>? Multiple(int sub, int glyph)
    {
        var index = CoverageIndex(sub + _r.U16(sub + 2), glyph);
        if (index < 0 || index >= _r.U16(sub + 4)) return null;
        var seq = sub + _r.U16(sub + 6 + index * 2);
        var count = _r.U16(seq);
        return [.. Enumerable.Range(0, count).Select(i => (int)_r.U16(seq + 2 + i * 2))];
    }

    private bool Ligature(int sub, List<Glyph> buf, int pos)
    {
        var index = CoverageIndex(sub + _r.U16(sub + 2), buf[pos].Id);
        if (index < 0 || index >= _r.U16(sub + 4)) return false;
        var set = sub + _r.U16(sub + 6 + index * 2);
        var count = _r.U16(set);
        for (var l = 0; l < count; l++)
        {
            var lig = set + _r.U16(set + 2 + l * 2);
            var comps = _r.U16(lig + 2);
            if (pos + comps > buf.Count) continue;
            var ok = true;
            for (var c = 1; c < comps && ok; c++) ok = buf[pos + c].Id == _r.U16(lig + 4 + (c - 1) * 2);
            if (!ok) continue;
            var first = buf[pos];
            buf.RemoveRange(pos, comps);
            buf.Insert(pos, first with { Id = _r.U16(lig) });
            return true;
        }
        return false;
    }

    private void ApplyRecords(int records, int count, List<Glyph> buf, int pos)
    {
        for (var k = 0; k < count; k++)
        {
            var seqIndex = _r.U16(records + k * 4);
            var lookupIndex = _r.U16(records + k * 4 + 2);
            ApplyAt(lookupIndex, buf, pos + seqIndex);
        }
    }

    private bool Context(int sub, List<Glyph> buf, int pos, out int next)
    {
        next = pos + 1;
        var format = _r.U16(sub);
        if (format == 3)
        {
            var glyphCount = _r.U16(sub + 2);
            var substCount = _r.U16(sub + 4);
            if (pos + glyphCount > buf.Count) return false;
            for (var i = 0; i < glyphCount; i++)
                if (CoverageIndex(sub + _r.U16(sub + 6 + i * 2), buf[pos + i].Id) < 0) return false;
            var before = buf.Count;
            ApplyRecords(sub + 6 + glyphCount * 2, substCount, buf, pos);
            next = pos + glyphCount + (buf.Count - before);
            return true;
        }
        if (CoverageIndex(sub + _r.U16(sub + 2), buf[pos].Id) < 0) return false;
        int setIndex;
        int classDef = 0;
        if (format == 1) setIndex = CoverageIndex(sub + _r.U16(sub + 2), buf[pos].Id);
        else if (format == 2) { classDef = sub + _r.U16(sub + 4); setIndex = ClassOf(classDef, buf[pos].Id); }
        else return false;
        var setCountOffset = format == 1 ? sub + 4 : sub + 6;
        if (setIndex >= _r.U16(setCountOffset)) return false;
        var setOffset = _r.U16(setCountOffset + 2 + setIndex * 2);
        if (setOffset == 0) return false;
        var set = sub + setOffset;
        var ruleCount = _r.U16(set);
        for (var ri = 0; ri < ruleCount; ri++)
        {
            var rule = set + _r.U16(set + 2 + ri * 2);
            var glyphCount = _r.U16(rule);
            var substCount = _r.U16(rule + 2);
            if (pos + glyphCount > buf.Count) continue;
            var ok = true;
            for (var i = 1; i < glyphCount && ok; i++)
            {
                var want = _r.U16(rule + 4 + (i - 1) * 2);
                ok = format == 1 ? buf[pos + i].Id == want : ClassOf(classDef, buf[pos + i].Id) == want;
            }
            if (!ok) continue;
            var before = buf.Count;
            ApplyRecords(rule + 4 + (glyphCount - 1) * 2, substCount, buf, pos);
            next = pos + glyphCount + (buf.Count - before);
            return true;
        }
        return false;
    }

    private bool ChainContext(int sub, List<Glyph> buf, int pos, out int next)
    {
        next = pos + 1;
        var format = _r.U16(sub);
        if (format == 3)
        {
            var o = sub + 2;
            var backCount = _r.U16(o);
            for (var i = 0; i < backCount; i++)
            {
                var p = pos - 1 - i;
                if (p < 0 || CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[p].Id) < 0) return false;
            }
            o += 2 + backCount * 2;
            var inputCount = _r.U16(o);
            if (pos + inputCount > buf.Count) return false;
            for (var i = 0; i < inputCount; i++)
                if (CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[pos + i].Id) < 0) return false;
            o += 2 + inputCount * 2;
            var aheadCount = _r.U16(o);
            for (var i = 0; i < aheadCount; i++)
            {
                var p = pos + inputCount + i;
                if (p >= buf.Count || CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[p].Id) < 0) return false;
            }
            o += 2 + aheadCount * 2;
            var substCount = _r.U16(o);
            var before = buf.Count;
            ApplyRecords(o + 2, substCount, buf, pos);
            next = pos + inputCount + (buf.Count - before);
            return true;
        }

        if (CoverageIndex(sub + _r.U16(sub + 2), buf[pos].Id) < 0) return false;
        int backDef = 0, inputDef = 0, aheadDef = 0, setIndex, setCountOffset;
        if (format == 1)
        {
            setIndex = CoverageIndex(sub + _r.U16(sub + 2), buf[pos].Id);
            setCountOffset = sub + 4;
        }
        else if (format == 2)
        {
            backDef = sub + _r.U16(sub + 4);
            inputDef = sub + _r.U16(sub + 6);
            aheadDef = sub + _r.U16(sub + 8);
            setIndex = ClassOf(inputDef, buf[pos].Id);
            setCountOffset = sub + 10;
        }
        else return false;
        if (setIndex >= _r.U16(setCountOffset)) return false;
        var setOffset = _r.U16(setCountOffset + 2 + setIndex * 2);
        if (setOffset == 0) return false;
        var set = sub + setOffset;
        var ruleCount = _r.U16(set);

        bool Match(int value, int glyph, int classDef) => format == 1 ? glyph == value : ClassOf(classDef, glyph) == value;

        for (var ri = 0; ri < ruleCount; ri++)
        {
            var rule = set + _r.U16(set + 2 + ri * 2);
            var o = rule;
            var backCount = _r.U16(o);
            var ok = true;
            for (var i = 0; i < backCount && ok; i++)
            {
                var p = pos - 1 - i;
                ok = p >= 0 && Match(_r.U16(o + 2 + i * 2), buf[p].Id, backDef);
            }
            if (!ok) continue;
            o += 2 + backCount * 2;
            var inputCount = _r.U16(o);
            if (pos + inputCount > buf.Count) continue;
            for (var i = 1; i < inputCount && ok; i++) ok = Match(_r.U16(o + 2 + (i - 1) * 2), buf[pos + i].Id, inputDef);
            if (!ok) continue;
            o += 2 + (inputCount - 1) * 2;
            var aheadCount = _r.U16(o);
            for (var i = 0; i < aheadCount && ok; i++)
            {
                var p = pos + inputCount + i;
                ok = p < buf.Count && Match(_r.U16(o + 2 + i * 2), buf[p].Id, aheadDef);
            }
            if (!ok) continue;
            o += 2 + aheadCount * 2;
            var substCount = _r.U16(o);
            var before = buf.Count;
            ApplyRecords(o + 2, substCount, buf, pos);
            next = pos + inputCount + (buf.Count - before);
            return true;
        }
        return false;
    }

    private bool ReverseChain(int sub, List<Glyph> buf, int pos)
    {
        if (_r.U16(sub) != 1) return false;
        var index = CoverageIndex(sub + _r.U16(sub + 2), buf[pos].Id);
        if (index < 0) return false;
        var o = sub + 4;
        var backCount = _r.U16(o);
        for (var i = 0; i < backCount; i++)
        {
            var p = pos - 1 - i;
            if (p < 0 || CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[p].Id) < 0) return false;
        }
        o += 2 + backCount * 2;
        var aheadCount = _r.U16(o);
        for (var i = 0; i < aheadCount; i++)
        {
            var p = pos + 1 + i;
            if (p >= buf.Count || CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[p].Id) < 0) return false;
        }
        o += 2 + aheadCount * 2;
        if (index >= _r.U16(o)) return false;
        buf[pos] = buf[pos] with { Id = _r.U16(o + 2 + index * 2) };
        return true;
    }
}
