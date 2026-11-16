namespace XamlFit.Core.OpenType;

/// <summary>
/// 匯出度量時用的小型 GSUB／GPOS 引擎：把一串字符照字型的替換規則跑一遍，再套定位規則裡改寬度的部分，
/// 只為了算出**寬度**（見 ArabicExport）。執行期不用它 —— 度量檔裡只存它算出來的數字，不存字型的表。
///
/// GSUB 支援的 lookup：1 單一、2 多重、4 連字、5 上下文、6 串接上下文（三種格式）、7 延伸、8 反向串接。
/// 3（替代字形）沒有預設值，不套。
/// GPOS 支援的 lookup：1 單一調整、2 成對調整（兩種格式）、7 上下文、8 串接上下文、9 延伸；只取 XAdvance
/// （XPlacement、y 方向、裝置表都不改一行的寬度）。附著類（3–6）不改前進寬度，不套。
/// LookupFlag 有處理（GDEF 的字符分類、附加符號分類、附加符號集合）：被忽略的字符比對時跳過，lookup 也不套在它們身上。
/// 沒處理之前，Nirmala UI 的奧里亞文「ଦ + 下加形 ଭ」被一個應該跳過下加形的 lookup 連成很寬的合字，連寫一個都沒變窄。
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
    // 每個 lookup 的子表、旗標，與「可能從這個字開始」的字符（子表第一個覆蓋表的聯集；null = 不知道，每個位置都試）。
    // 一個字串要跑幾百個 lookup，大部分只管幾個字：先看第一個字在不在裡面，匯出快很多
    private readonly Dictionary<int, LookupInfo?> _lookups = [];
    private sealed record LookupInfo(List<(int Type, int Offset)> Subs, Filter Filter, bool[]? First);
    private readonly int _glyphClassDef, _markAttachClassDef, _markGlyphSets;

    /// <summary>一個 lookup 的 LookupFlag 與附加符號集合（UseMarkFilteringSet 時）。</summary>
    private readonly record struct Filter(int Flag, int MarkSet);

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
        if (r.Has("GDEF"))
        {
            var gdef = r.TableOffset("GDEF");
            int Off(int at) => r.U16(gdef + at) is var o and > 0 ? gdef + o : 0;
            _glyphClassDef = Off(4);
            _markAttachClassDef = Off(10);
            if (r.U16(gdef + 2) >= 2) _markGlyphSets = Off(12);   // GDEF 1.2 以上才有
        }
    }

    private Filter FilterOf(int lookup)
    {
        var flag = _r.U16(lookup + 2);
        var markSet = (flag & 0x10) != 0 ? _r.U16(lookup + 6 + _r.U16(lookup + 4) * 2) : -1;
        return new Filter(flag, markSet);
    }

    /// <summary>這個 lookup 要不要跳過這個字符（IgnoreBaseGlyphs／IgnoreLigatures／IgnoreMarks、附加符號分類、附加符號集合）。</summary>
    private bool Skip(int glyph, Filter f)
    {
        if (f.Flag == 0 || _glyphClassDef == 0) return false;
        var cls = ClassOf(_glyphClassDef, glyph);
        if (cls == 1) return (f.Flag & 0x2) != 0;
        if (cls == 2) return (f.Flag & 0x4) != 0;
        if (cls != 3) return false;
        if ((f.Flag & 0x8) != 0) return true;
        if ((f.Flag & 0x10) != 0 && f.MarkSet >= 0 && _markGlyphSets != 0)
        {
            if (f.MarkSet >= _r.U16(_markGlyphSets + 2)) return true;
            var coverage = _markGlyphSets + (int)_r.U32(_markGlyphSets + 4 + f.MarkSet * 4);
            return CoverageIndex(coverage, glyph) < 0;
        }
        var type = f.Flag >> 8;
        return type != 0 && _markAttachClassDef != 0 && ClassOf(_markAttachClassDef, glyph) != type;
    }

    private int NextIndex(List<Glyph> buf, int from, Filter f)
    {
        for (var i = from; i < buf.Count; i++) if (!Skip(buf[i].Id, f)) return i;
        return -1;
    }

    private int PrevIndex(List<Glyph> buf, int from, Filter f)
    {
        for (var i = from; i >= 0; i--) if (!Skip(buf[i].Id, f)) return i;
        return -1;
    }

    /// <summary>從 pos 起（pos 本身算第一個）往後找 count 個不跳過的字符的位置；不夠回 null。</summary>
    private int[]? InputPositions(List<Glyph> buf, int pos, int count, Filter f)
    {
        var positions = new int[count];
        positions[0] = pos;
        for (var k = 1; k < count; k++)
        {
            var next = NextIndex(buf, positions[k - 1] + 1, f);
            if (next < 0) return null;
            positions[k] = next;
        }
        return positions;
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
    /// <param name="noSingle">GPOS：這些字形不套「沒有上下文的單一調整」lookup（上下文規則裡巢狀呼叫的照套）。</param>
    public List<Glyph> Shape(List<Glyph> buffer, SortedDictionary<int, HashSet<string>> lookups, IReadOnlySet<string> formFeatures, Func<Glyph, bool>? noSingle = null)
    {
        var buf = new List<Glyph>(buffer);
        foreach (var (li, tags) in lookups)
        {
            var global = tags.Any(t => !formFeatures.Contains(t));
            var single = noSingle is not null && Info(li) is { } info && info.Subs.All(s => s.Type == 1);
            bool Applies(Glyph g) => (global || (g.Form is { } f && tags.Contains(f))) && !(single && noSingle!(g));
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

    private LookupInfo? Info(int lookupIndex)
    {
        if (_lookups.TryGetValue(lookupIndex, out var info)) return info;
        var lookup = LookupOffset(lookupIndex);
        if (lookup < 0) return _lookups[lookupIndex] = null;
        var subs = Subtables(lookup).ToList();
        var glyphs = new List<int>();
        var known = true;
        foreach (var (type, sub) in subs)
        {
            var coverage = FirstCoverage(type, sub);
            if (coverage == -2) continue;   // 這支引擎不套的子表
            if (coverage < 0) { known = false; break; }
            glyphs.AddRange(CoverageMap(coverage).Keys);
        }
        bool[]? first = null;
        if (known)
        {
            first = new bool[glyphs.Count == 0 ? 0 : glyphs.Max() + 1];
            foreach (var g in glyphs) first[g] = true;
        }
        return _lookups[lookupIndex] = new LookupInfo(subs, FilterOf(lookup), first);
    }

    // 子表的第一個字的覆蓋表；-1 = 讀不出來（每個位置都試），-2 = 這支引擎不套這種子表
    private int FirstCoverage(int type, int sub)
    {
        int Context(bool chain)
        {
            var format = _r.U16(sub);
            if (format is 1 or 2) return sub + _r.U16(sub + 2);
            if (format != 3) return -1;
            if (!chain) return _r.U16(sub + 2) == 0 ? -1 : sub + _r.U16(sub + 6);
            var input = sub + 4 + _r.U16(sub + 2) * 2;
            return _r.U16(input) == 0 ? -1 : sub + _r.U16(input + 2);
        }
        if (_gpos)
            return type switch { 1 or 2 => sub + _r.U16(sub + 2), 7 => Context(false), 8 => Context(true), _ => -2 };
        return type switch { 1 or 2 or 4 or 8 => sub + _r.U16(sub + 2), 5 => Context(false), 6 => Context(true), _ => -2 };
    }

    private void ApplyLookup(int lookupIndex, List<Glyph> buf, Func<Glyph, bool> applies)
    {
        if (Info(lookupIndex) is not { } info) return;
        var (subs, filter, first) = (info.Subs, info.Filter, info.First);
        bool Starts(int glyph) => first is null || glyph < first.Length && first[glyph];
        if (first is not null && !buf.Exists(g => Starts(g.Id))) return;
        if (!_gpos && subs.Count > 0 && subs[0].Type == 8)
        {
            for (var i = buf.Count - 1; i >= 0; i--)
                if (Starts(buf[i].Id) && applies(buf[i]) && !Skip(buf[i].Id, filter)) foreach (var (_, sub) in subs) if (ReverseChain(sub, buf, i, filter)) break;
            return;
        }
        var pos = 0;
        while (pos < buf.Count)
        {
            if (Starts(buf[pos].Id) && applies(buf[pos]) && !Skip(buf[pos].Id, filter) && ApplyAt(subs, buf, pos, filter, out var next)) pos = Math.Max(next, pos + 1);
            else pos++;
        }
    }

    /// <summary>在一個位置套一個 lookup（巢狀呼叫也用它）。</summary>
    private bool ApplyAt(int lookupIndex, List<Glyph> buf, int pos)
    {
        if (pos < 0 || pos >= buf.Count || Info(lookupIndex) is not { } info) return false;
        return !Skip(buf[pos].Id, info.Filter) && ApplyAt(info.Subs, buf, pos, info.Filter, out _);
    }

    private bool ApplyAt(List<(int Type, int Offset)> subs, List<Glyph> buf, int pos, Filter filter, out int next)
    {
        next = pos + 1;
        if (_gpos) return Position(subs, buf, pos, filter, out next);
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
                    if (Ligature(sub, buf, pos, filter)) return true;
                    break;
                case 5:
                    if (Context(sub, buf, pos, filter, out next)) return true;
                    break;
                case 6:
                    if (ChainContext(sub, buf, pos, filter, out next)) return true;
                    break;
            }
        }
        return false;
    }

    private bool Position(List<(int Type, int Offset)> subs, List<Glyph> buf, int pos, Filter filter, out int next)
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
                    if (NextIndex(buf, pos + 1, filter) is var p2 and >= 0 && PairPos(sub, buf[pos].Id, buf[p2].Id) is var (a1, a2, second))
                    {
                        buf[pos] = buf[pos] with { Adv = buf[pos].Adv + a1 };
                        buf[p2] = buf[p2] with { Adv = buf[p2].Adv + a2 };
                        next = second ? p2 + 1 : p2;
                        return true;
                    }
                    break;
                case 7:
                    if (Context(sub, buf, pos, filter, out next)) return true;
                    break;
                case 8:
                    if (ChainContext(sub, buf, pos, filter, out next)) return true;
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

    private int CoverageIndex(int offset, int glyph) => CoverageMap(offset).TryGetValue(glyph, out var idx) ? idx : -1;

    private Dictionary<int, int> CoverageMap(int offset)
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
        return map;
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

    private bool Ligature(int sub, List<Glyph> buf, int pos, Filter filter)
    {
        var index = CoverageIndex(sub + _r.U16(sub + 2), buf[pos].Id);
        if (index < 0 || index >= _r.U16(sub + 4)) return false;
        var set = sub + _r.U16(sub + 6 + index * 2);
        var count = _r.U16(set);
        for (var l = 0; l < count; l++)
        {
            var lig = set + _r.U16(set + 2 + l * 2);
            var comps = _r.U16(lig + 2);
            if (comps == 0 || InputPositions(buf, pos, comps, filter) is not { } at) continue;
            var ok = true;
            for (var c = 1; c < comps && ok; c++) ok = buf[at[c]].Id == _r.U16(lig + 4 + (c - 1) * 2);
            if (!ok) continue;
            // 跳過的字符（夾在中間的附加符號）留在原處，排到連字後面
            for (var c = comps - 1; c >= 1; c--) buf.RemoveAt(at[c]);
            buf[pos] = buf[pos] with { Id = _r.U16(lig) };
            return true;
        }
        return false;
    }

    /// <summary>照比對到的位置套巢狀的 lookup；替換讓字符數變了，後面的位置跟著移。回傳最後一個比對位置的下一格。</summary>
    private int ApplyRecords(int records, int count, List<Glyph> buf, int[] positions)
    {
        // 結束位置照「原本最後一個比對位置的下一格 + 整體增減的字符數」算：替換在最後一個位置插入字符時，
        // 不能停在插進來的字符上（同一條規則會一直重套，字符無限增加）
        var end = positions[^1] + 1;
        var countBefore = buf.Count;
        for (var k = 0; k < count; k++)
        {
            var seqIndex = _r.U16(records + k * 4);
            var lookupIndex = _r.U16(records + k * 4 + 2);
            if (seqIndex >= positions.Length) continue;
            var before = buf.Count;
            ApplyAt(lookupIndex, buf, positions[seqIndex]);
            var delta = buf.Count - before;
            if (delta != 0)
                for (var j = seqIndex + 1; j < positions.Length; j++) positions[j] += delta;
        }
        return Math.Max(end + buf.Count - countBefore, 0);
    }

    private bool Context(int sub, List<Glyph> buf, int pos, Filter filter, out int next)
    {
        next = pos + 1;
        var format = _r.U16(sub);
        if (format == 3)
        {
            var glyphCount = _r.U16(sub + 2);
            var substCount = _r.U16(sub + 4);
            if (glyphCount == 0 || InputPositions(buf, pos, glyphCount, filter) is not { } at) return false;
            for (var i = 0; i < glyphCount; i++)
                if (CoverageIndex(sub + _r.U16(sub + 6 + i * 2), buf[at[i]].Id) < 0) return false;
            next = ApplyRecords(sub + 6 + glyphCount * 2, substCount, buf, at);
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
            if (glyphCount == 0 || InputPositions(buf, pos, glyphCount, filter) is not { } at) continue;
            var ok = true;
            for (var i = 1; i < glyphCount && ok; i++)
            {
                var want = _r.U16(rule + 4 + (i - 1) * 2);
                ok = format == 1 ? buf[at[i]].Id == want : ClassOf(classDef, buf[at[i]].Id) == want;
            }
            if (!ok) continue;
            next = ApplyRecords(rule + 4 + (glyphCount - 1) * 2, substCount, buf, at);
            return true;
        }
        return false;
    }

    private bool ChainContext(int sub, List<Glyph> buf, int pos, Filter filter, out int next)
    {
        next = pos + 1;
        var format = _r.U16(sub);
        if (format == 3)
        {
            var o = sub + 2;
            var backCount = _r.U16(o);
            var p = pos;
            for (var i = 0; i < backCount; i++)
            {
                p = PrevIndex(buf, p - 1, filter);
                if (p < 0 || CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[p].Id) < 0) return false;
            }
            o += 2 + backCount * 2;
            var inputCount = _r.U16(o);
            if (inputCount == 0 || InputPositions(buf, pos, inputCount, filter) is not { } at) return false;
            for (var i = 0; i < inputCount; i++)
                if (CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[at[i]].Id) < 0) return false;
            o += 2 + inputCount * 2;
            var aheadCount = _r.U16(o);
            p = at[^1];
            for (var i = 0; i < aheadCount; i++)
            {
                p = NextIndex(buf, p + 1, filter);
                if (p < 0 || CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[p].Id) < 0) return false;
            }
            o += 2 + aheadCount * 2;
            var substCount = _r.U16(o);
            next = ApplyRecords(o + 2, substCount, buf, at);
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
            var p = pos;
            for (var i = 0; i < backCount && ok; i++)
            {
                p = PrevIndex(buf, p - 1, filter);
                ok = p >= 0 && Match(_r.U16(o + 2 + i * 2), buf[p].Id, backDef);
            }
            if (!ok) continue;
            o += 2 + backCount * 2;
            var inputCount = _r.U16(o);
            if (inputCount == 0 || InputPositions(buf, pos, inputCount, filter) is not { } at) continue;
            for (var i = 1; i < inputCount && ok; i++) ok = Match(_r.U16(o + 2 + (i - 1) * 2), buf[at[i]].Id, inputDef);
            if (!ok) continue;
            o += 2 + (inputCount - 1) * 2;
            var aheadCount = _r.U16(o);
            p = at[^1];
            for (var i = 0; i < aheadCount && ok; i++)
            {
                p = NextIndex(buf, p + 1, filter);
                ok = p >= 0 && Match(_r.U16(o + 2 + i * 2), buf[p].Id, aheadDef);
            }
            if (!ok) continue;
            o += 2 + aheadCount * 2;
            var substCount = _r.U16(o);
            next = ApplyRecords(o + 2, substCount, buf, at);
            return true;
        }
        return false;
    }

    private bool ReverseChain(int sub, List<Glyph> buf, int pos, Filter filter)
    {
        if (_r.U16(sub) != 1) return false;
        var index = CoverageIndex(sub + _r.U16(sub + 2), buf[pos].Id);
        if (index < 0) return false;
        var o = sub + 4;
        var backCount = _r.U16(o);
        var p = pos;
        for (var i = 0; i < backCount; i++)
        {
            p = PrevIndex(buf, p - 1, filter);
            if (p < 0 || CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[p].Id) < 0) return false;
        }
        o += 2 + backCount * 2;
        var aheadCount = _r.U16(o);
        p = pos;
        for (var i = 0; i < aheadCount; i++)
        {
            p = NextIndex(buf, p + 1, filter);
            if (p < 0 || CoverageIndex(sub + _r.U16(o + 2 + i * 2), buf[p].Id) < 0) return false;
        }
        o += 2 + aheadCount * 2;
        if (index >= _r.U16(o)) return false;
        buf[pos] = buf[pos] with { Id = _r.U16(o + 2 + index * 2) };
        return true;
    }
}
