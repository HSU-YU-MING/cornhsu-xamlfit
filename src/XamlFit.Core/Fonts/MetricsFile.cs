using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlFit.Core.Fonts;

/// <summary>
/// <c>xamlfit.fonts.json</c> 的讀寫。
///
/// **這個檔案裡只有數字。** 沒有字形外框、沒有字型程式碼、沒有任何可以還原成字型的東西。
/// 使用者在「有字型」的機器上從自己已授權的字型匯出，commit 進自己的 repo；
/// CI 讀數字，不需要安裝任何字型。我們從未散布任何字型。
///
/// ⚠ 發版前仍要確認字型 EULA 對「萃取 advance width 並 commit」的立場（規畫書 §4.2）。
/// </summary>
public sealed class MetricsFile
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonPropertyName("generatedBy")] public string GeneratedBy { get; set; } = "xamlfit";
    [JsonPropertyName("generatedOn")] public string GeneratedOn { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } =
        "Advance widths, kerning adjustments and Arabic letter-form widths only. Contains no glyph outlines and no font software.";
    [JsonPropertyName("fonts")] public List<FontEntry> Fonts { get; set; } = [];

    public sealed class FontEntry
    {
        [JsonPropertyName("family")] public string Family { get; set; } = "";
        [JsonPropertyName("weight")] public int Weight { get; set; } = 400;
        [JsonPropertyName("style")] public string Style { get; set; } = "Normal";
        [JsonPropertyName("stretch")] public string Stretch { get; set; } = "Normal";
        [JsonPropertyName("legacyFamily")] public string? LegacyFamily { get; set; }
        [JsonPropertyName("variable")] public bool Variable { get; set; }
        [JsonPropertyName("aliases")] public List<string>? Aliases { get; set; }
        [JsonPropertyName("unitsPerEm")] public int UnitsPerEm { get; set; }
        [JsonPropertyName("ascender")] public int Ascender { get; set; }
        [JsonPropertyName("descender")] public int Descender { get; set; }
        [JsonPropertyName("lineGap")] public int LineGap { get; set; }
        [JsonPropertyName("defaultAdvance")] public int DefaultAdvance { get; set; }

        /// <summary>[起始碼位, 結束碼位, 前進寬度] 的陣列。</summary>
        [JsonPropertyName("ranges")] public List<int[]> Ranges { get; set; } = [];

        /// <summary>GPOS 'kern' 的成對調整：lookup 的陣列，每個 lookup 是子表的陣列（見 <see cref="KerningTable"/>）。</summary>
        [JsonPropertyName("kerning")] public List<List<KernEntry>>? Kerning { get; set; }

        /// <summary>阿拉伯字母的寬度，依 OpenType 語言系統："dflt"、"ARA "…。字母與形狀合成一個數：碼位 × 4 + 形狀（0 獨立、1 字首、2 字中、3 字尾）。
        /// forms＝[碼位, 獨立, 字首, 字中, 字尾]；
        /// pairs＝每個前一個字一列 [前一個字, 這個字, 改變量, 這個字, 改變量, …]（只列非零）；
        /// triples＝每組（中間的字, 結尾的字）一列 [中間的字, 結尾的字, 第一個字, 改變量, 第一個字, 改變量, …]（只列非零）；
        /// pairLetters＝算過成對改變量的字母 [起, 迄]；tripleLetters＝算過三字改變量的結尾字母。
        /// formsFrom／pairsFrom／triplesFrom：跟另一個語言系統一模一樣時只寫它的名字（Segoe UI 的 URD 跟預設完全相同）。</summary>
        [JsonPropertyName("arabic")] public Dictionary<string, ArabicEntry>? Arabic { get; set; }
    }

    public sealed class ArabicEntry
    {
        [JsonPropertyName("forms")] public List<int[]> Forms { get; set; } = [];
        [JsonPropertyName("pairs")] public List<int[]>? Pairs { get; set; }
        [JsonPropertyName("pairLetters")] public List<int[]>? PairLetters { get; set; }
        [JsonPropertyName("triples")] public List<int[]>? Triples { get; set; }
        [JsonPropertyName("tripleLetters")] public List<int>? TripleLetters { get; set; }
        [JsonPropertyName("formsFrom")] public string? FormsFrom { get; set; }
        [JsonPropertyName("pairsFrom")] public string? PairsFrom { get; set; }
        [JsonPropertyName("triplesFrom")] public string? TriplesFrom { get; set; }
    }

    /// <summary>一個字距子表。格式 1 只有 pairs：每個第一個字一列 [碼位, 第二個字, 調整, 第二個字, 調整, …]；格式 2 是類別：
    /// cov＝第一個字的涵蓋範圍 [起, 迄]、first／second＝[起, 迄, 類別]、n1／n2＝類別數、cells＝[格子序號, 調整]（只列非零）。</summary>
    public sealed class KernEntry
    {
        [JsonPropertyName("pairs")] public List<int[]>? Pairs { get; set; }
        [JsonPropertyName("cov")] public List<int[]>? Coverage { get; set; }
        [JsonPropertyName("first")] public List<int[]>? First { get; set; }
        [JsonPropertyName("second")] public List<int[]>? Second { get; set; }
        [JsonPropertyName("n1")] public int? FirstCount { get; set; }
        [JsonPropertyName("n2")] public int? SecondCount { get; set; }
        [JsonPropertyName("cells")] public List<int[]>? Cells { get; set; }
    }

    private static readonly JsonSerializerOptions Compact = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static List<List<KernEntry>>? ToEntries(KerningTable? table) => table?.Lookups.Select(l => l.Subtables.Select(s => s switch
    {
        KerningPairs p => new KernEntry
        {
            Pairs = [.. p.Pairs.GroupBy(kv => (int)(kv.Key >> 32)).OrderBy(g => g.Key)
                .Select(g => new[] { g.Key }.Concat(g.OrderBy(kv => (int)(uint)kv.Key).SelectMany(kv => new[] { (int)(uint)kv.Key, kv.Value })).ToArray())],
        },
        KerningClasses c => new KernEntry
        {
            Coverage = [.. c.Coverage.Select(r => new[] { r.Start, r.End })],
            First = [.. c.First.Select(r => new[] { r.Start, r.End, r.Class })],
            Second = [.. c.Second.Select(r => new[] { r.Start, r.End, r.Class })],
            FirstCount = c.FirstCount,
            SecondCount = c.SecondCount,
            Cells = [.. c.Cells.OrderBy(kv => kv.Key).Select(kv => new[] { kv.Key, kv.Value })],
        },
        _ => throw new NotSupportedException(),
    }).ToList()).ToList();

    private static KerningTable? FromEntries(List<List<KernEntry>>? entries)
    {
        if (entries is not { Count: > 0 }) return null;
        var lookups = new List<KerningLookup>();
        foreach (var lookup in entries)
        {
            var subs = new List<KerningSubtable>();
            foreach (var e in lookup)
            {
                if (e.Pairs is { } pairs)
                {
                    var dict = new Dictionary<long, int>(pairs.Count);
                    foreach (var p in pairs)
                        for (var i = 1; i + 1 < p.Length; i += 2) dict.TryAdd(KerningPairs.Key(p[0], p[i]), p[i + 1]);
                    subs.Add(new KerningPairs { Pairs = dict });
                }
                else if (e.Coverage is { } cov && e.FirstCount is { } n1 && e.SecondCount is { } n2)
                {
                    var cells = new Dictionary<int, int>();
                    foreach (var c in e.Cells ?? []) if (c.Length >= 2) cells[c[0]] = c[1];
                    subs.Add(new KerningClasses
                    {
                        Coverage = [.. cov.Where(r => r.Length >= 2).Select(r => (r[0], r[1])).OrderBy(r => r.Item1)],
                        First = [.. (e.First ?? []).Where(r => r.Length >= 3).Select(r => (r[0], r[1], r[2])).OrderBy(r => r.Item1)],
                        Second = [.. (e.Second ?? []).Where(r => r.Length >= 3).Select(r => (r[0], r[1], r[2])).OrderBy(r => r.Item1)],
                        FirstCount = n1,
                        SecondCount = n2,
                        Cells = cells,
                    });
                }
            }
            if (subs.Count > 0) lookups.Add(new KerningLookup { Subtables = subs });
        }
        return lookups.Count > 0 ? new KerningTable { Lookups = lookups } : null;
    }

    // ⚠ 不用 JsonSerializer 的 WriteIndented 輸出。
    //   它會把每個 [start,end,advance] 攤成五行 —— Segoe UI 一支字型就 2.1 MB。
    //   這個檔案要進使用者的 repo，大小是設計條件，不是細節。
    //   手寫序列化：外層保持可讀的縮排，ranges 壓成一行。讀取端仍是標準 JSON。

    public static MetricsFile FromMetrics(IEnumerable<FontMetrics> metrics, string toolVersion, DateOnly date)
    {
        var f = new MetricsFile
        {
            GeneratedBy = $"xamlfit {toolVersion}",
            GeneratedOn = date.ToString("yyyy-MM-dd"),
        };

        foreach (var m in metrics.OrderBy(m => m.Family, StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(m => m.Weight).ThenBy(m => m.Style))
        {
            f.Fonts.Add(new FontEntry
            {
                Family = m.Family,
                Weight = m.Weight,
                Style = m.Style.ToString(),
                Stretch = m.Stretch,
                LegacyFamily = m.LegacyFamily,
                Variable = m.Variable,
                Aliases = m.Aliases.Count > 0 ? [.. m.Aliases] : null,
                UnitsPerEm = m.UnitsPerEm,
                Ascender = m.Ascender,
                Descender = m.Descender,
                LineGap = m.LineGap,
                DefaultAdvance = m.DefaultAdvance,
                Ranges = [.. m.Ranges.Select(r => new[] { r.Start, r.End, r.Advance })],
                Kerning = ToEntries(m.Kerning),
                Arabic = ToArabicEntries(m.Arabic),
            });
        }

        return f;
    }

    public string ToJson()
    {
        var sb = new System.Text.StringBuilder(1 << 20);
        sb.Append("{\n");
        sb.Append($"  \"schemaVersion\": {SchemaVersion},\n");
        sb.Append($"  \"generatedBy\": {Quote(GeneratedBy)},\n");
        sb.Append($"  \"generatedOn\": {Quote(GeneratedOn)},\n");
        sb.Append($"  \"note\": {Quote(Note)},\n");
        sb.Append("  \"fonts\": [\n");

        for (var i = 0; i < Fonts.Count; i++)
        {
            var f = Fonts[i];
            sb.Append("    {\n");
            sb.Append($"      \"family\": {Quote(f.Family)},\n");
            sb.Append($"      \"weight\": {f.Weight},\n");
            sb.Append($"      \"style\": {Quote(f.Style)},\n");
            sb.Append($"      \"stretch\": {Quote(f.Stretch)},\n");
            if (!string.IsNullOrWhiteSpace(f.LegacyFamily)) sb.Append($"      \"legacyFamily\": {Quote(f.LegacyFamily)},\n");
            if (f.Variable) sb.Append("      \"variable\": true,\n");
            if (f.Aliases is { Count: > 0 }) sb.Append($"      \"aliases\": [{string.Join(", ", f.Aliases.Select(Quote))}],\n");
            sb.Append($"      \"unitsPerEm\": {f.UnitsPerEm},\n");
            sb.Append($"      \"ascender\": {f.Ascender},\n");
            sb.Append($"      \"descender\": {f.Descender},\n");
            sb.Append($"      \"lineGap\": {f.LineGap},\n");
            sb.Append($"      \"defaultAdvance\": {f.DefaultAdvance},\n");
            sb.Append("      \"ranges\": [");
            for (var j = 0; j < f.Ranges.Count; j++)
            {
                var r = f.Ranges[j];
                if (j > 0) sb.Append(',');
                sb.Append('[').Append(r[0]).Append(',').Append(r[1]).Append(',').Append(r[2]).Append(']');
            }
            sb.Append(']');
            if (f.Kerning is { Count: > 0 }) sb.Append(",\n      \"kerning\": ").Append(JsonSerializer.Serialize(f.Kerning, Compact));
            if (f.Arabic is { Count: > 0 }) sb.Append(",\n      \"arabic\": ").Append(JsonSerializer.Serialize(f.Arabic, Compact));
            sb.Append('\n');
            sb.Append(i == Fonts.Count - 1 ? "    }\n" : "    },\n");
        }

        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    private static string Quote(string s) => JsonSerializer.Serialize(s);

    public static MetricsFile Parse(string json)
    {
        var f = JsonSerializer.Deserialize<MetricsFile>(json)
                ?? throw new InvalidDataException("metrics file is empty");
        if (f.SchemaVersion > CurrentSchemaVersion)
            throw new InvalidDataException(
                $"metrics file schemaVersion {f.SchemaVersion} is newer than this tool understands ({CurrentSchemaVersion}); upgrade xamlfit");
        return f;
    }

    public IReadOnlyList<FontMetrics> ToMetrics()
    {
        var list = new List<FontMetrics>(Fonts.Count);
        foreach (var e in Fonts)
        {
            if (e.UnitsPerEm <= 0) continue;
            var ranges = new List<AdvanceRange>(e.Ranges.Count);
            foreach (var r in e.Ranges)
                if (r.Length >= 3) ranges.Add(new AdvanceRange(r[0], r[1], r[2]));
            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

            list.Add(new FontMetrics
            {
                Family = e.Family,
                Weight = e.Weight,
                Style = Enum.TryParse<FontStyleKind>(e.Style, true, out var s) ? s : FontStyleKind.Normal,
                Stretch = e.Stretch,
                LegacyFamily = e.LegacyFamily,
                Variable = e.Variable,
                Aliases = e.Aliases ?? [],
                UnitsPerEm = e.UnitsPerEm,
                Ascender = e.Ascender,
                Descender = e.Descender,
                LineGap = e.LineGap,
                DefaultAdvance = e.DefaultAdvance,
                Ranges = ranges,
                Kerning = FromEntries(e.Kerning),
                Arabic = FromArabicEntries(e.Arabic),
            });
        }
        return list;
    }

    private static Dictionary<string, ArabicEntry>? ToArabicEntries(IReadOnlyDictionary<string, ArabicShapes>? arabic)
    {
        if (arabic is not { Count: > 0 }) return null;
        static int L(int cp, int form) => cp * 4 + form;

        var result = new Dictionary<string, ArabicEntry>();
        var written = new List<(string Lang, List<int[]> Forms, List<int[]>? Pairs, List<int[]>? Triples)>();
        foreach (var (lang, shapes) in arabic.OrderBy(kv => kv.Key == "dflt" ? "" : kv.Key, StringComparer.Ordinal))
        {
            List<int[]> forms = [.. shapes.Forms.OrderBy(f => f.Key).Select(f => new[] { f.Key }.Concat(f.Value).ToArray())];
            var pairs = shapes.Pairs.Count == 0 ? null : shapes.Pairs
                .Select(p => (A: L((int)(p.Key >> 32), (int)((p.Key >> 30) & 3)), B: L((int)((p.Key >> 2) & 0xFFFFFFF), (int)(p.Key & 3)), D: p.Value))
                .GroupBy(p => p.A).OrderBy(g => g.Key)
                .Select(g => new[] { g.Key }.Concat(g.OrderBy(p => p.B).SelectMany(p => new[] { p.B, p.D })).ToArray()).ToList();
            var triples = shapes.Triples.Count == 0 ? null : shapes.Triples
                .Select(t => (X: L((int)((t.Key >> 38) & 0xFFFF), (int)((t.Key >> 36) & 3)),
                              Y: L((int)((t.Key >> 20) & 0xFFFF), (int)((t.Key >> 18) & 3)),
                              Z: L((int)((t.Key >> 2) & 0xFFFF), (int)(t.Key & 3)), D: t.Value))
                .GroupBy(t => (t.Y, t.Z)).OrderBy(g => g.Key.Y).ThenBy(g => g.Key.Z)
                .Select(g => new[] { g.Key.Y, g.Key.Z }.Concat(g.OrderBy(t => t.X).SelectMany(t => new[] { t.X, t.D })).ToArray()).ToList();

            static bool Same(List<int[]>? a, List<int[]>? b) =>
                a is null ? b is null : b is not null && a.Count == b.Count && a.Zip(b).All(x => x.First.AsSpan().SequenceEqual(x.Second));

            var entry = new ArabicEntry
            {
                PairLetters = Runs(shapes.PairLetters),
                TripleLetters = shapes.TripleLetters.Count > 0 ? [.. shapes.TripleLetters.Order()] : null,
            };
            var formsTwin = written.FirstOrDefault(w => Same(w.Forms, forms)).Lang;
            if (formsTwin is not null) entry.FormsFrom = formsTwin; else entry.Forms = forms;
            var pairsTwin = pairs is null ? null : written.FirstOrDefault(w => Same(w.Pairs, pairs)).Lang;
            if (pairsTwin is not null) entry.PairsFrom = pairsTwin; else entry.Pairs = pairs;
            var triplesTwin = triples is null ? null : written.FirstOrDefault(w => Same(w.Triples, triples)).Lang;
            if (triplesTwin is not null) entry.TriplesFrom = triplesTwin; else entry.Triples = triples;
            // 只記自己有寫資料的欄位：「跟誰一樣」不會指向另一個「跟誰一樣」
            written.Add((lang, formsTwin is null ? forms : null!, pairsTwin is null ? pairs : null, triplesTwin is null ? triples : null));
            result[lang] = entry;
        }
        return result;
    }

    private static Dictionary<string, ArabicShapes>? FromArabicEntries(Dictionary<string, ArabicEntry>? entries)
    {
        if (entries is not { Count: > 0 }) return null;
        Dictionary<int, int[]> Forms(ArabicEntry e) => e.Forms.Where(f => f.Length >= 5).ToDictionary(f => f[0], f => f[1..5]);
        Dictionary<long, int> Pairs(ArabicEntry e)
        {
            var map = new Dictionary<long, int>();
            foreach (var row in e.Pairs ?? [])
                for (var i = 1; i + 1 < row.Length; i += 2)
                    map[ArabicShapes.Key(row[0] >> 2, row[0] & 3, row[i] >> 2, row[i] & 3)] = row[i + 1];
            return map;
        }
        Dictionary<long, int> Triples(ArabicEntry e)
        {
            var map = new Dictionary<long, int>();
            foreach (var row in e.Triples ?? [])
                for (var i = 2; i + 1 < row.Length; i += 2)
                    map[ArabicShapes.Key3(row[i] >> 2, row[i] & 3, row[0] >> 2, row[0] & 3, row[1] >> 2, row[1] & 3)] = row[i + 1];
            return map;
        }
        // 「跟某個語言系統一樣」只指向自己有寫資料的那一個；找不到就當成沒有（量不到，不猜）
        ArabicEntry? Twin(string? lang) => lang is not null && entries.TryGetValue(lang, out var t) ? t : null;

        return entries.ToDictionary(kv => kv.Key, kv => new ArabicShapes
        {
            Forms = Forms(kv.Value.FormsFrom is { } f ? Twin(f) ?? new ArabicEntry() : kv.Value),
            Pairs = Pairs(kv.Value.PairsFrom is { } p ? Twin(p) ?? new ArabicEntry() : kv.Value),
            Triples = Triples(kv.Value.TriplesFrom is { } t ? Twin(t) ?? new ArabicEntry() : kv.Value),
            PairLetters = (kv.Value.PairLetters ?? []).Where(r => r.Length >= 2 && r[1] - r[0] < 0x10000)
                .SelectMany(r => Enumerable.Range(r[0], r[1] - r[0] + 1)).ToHashSet(),
            TripleLetters = (kv.Value.TripleLetters ?? []).ToHashSet(),
        });
    }

    /// <summary>一組碼位 → 連續區段 [起, 迄]。</summary>
    private static List<int[]>? Runs(IReadOnlySet<int> cps)
    {
        if (cps.Count == 0) return null;
        var runs = new List<int[]>();
        foreach (var cp in cps.Order())
        {
            if (runs.Count > 0 && runs[^1][1] == cp - 1) runs[^1][1] = cp;
            else runs.Add([cp, cp]);
        }
        return runs;
    }
}
