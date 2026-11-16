using System.Globalization;

namespace XamlFit.Core.Fonts;

/// <summary>為什麼一個文字位置量不到。報告要能說出原因，不能只說「量不到」。</summary>
public enum FontMissReason
{
    None,
    /// <summary>度量檔裡沒有這個家族 —— 跑 `xamlfit fonts export` 補。</summary>
    NoSuchFamily,
    /// <summary>家族有，但這個字重／樣式沒有。</summary>
    NoSuchFace,
}

public sealed record FontLookup(
    IReadOnlyList<FontMetrics> Chain,
    FontMissReason Miss,
    string? MissingFamily,
    /// <summary>Chain 的前幾支是 XAML 明寫的；其餘是自動補的後備。</summary>
    int DeclaredCount = int.MaxValue)
{
    /// <summary>
    /// 明寫的字型都畫不出來的字，WPF 會拿哪支補（照 WPF 自己的複合字型規則，見 <see cref="FontFallback"/>）。
    /// 回傳 null 是 WPF 那張表裡的字型都不在度量檔裡（或沒有一支有這個字）—— 那個字量不到。
    /// </summary>
    public Func<int, (FontMetrics Face, double Scale)?>? Fallback { get; init; }

    /// <summary>元素的語言（決定阿拉伯文用哪個 OpenType 語言系統的字形）。</summary>
    public string? Language { get; init; }

    /// <summary>某個字 WPF 會依序嘗試的後備字型（給「該匯出哪支字型」的提示用）。</summary>
    public Func<int, IReadOnlyList<string>>? FallbackCandidates { get; init; }

    /// <summary>給使用者看的缺漏說明；有度量時是 null。</summary>
    public string? MissDescription => Miss switch
    {
        FontMissReason.NoSuchFamily => $"no metrics for font family '{MissingFamily}'",
        FontMissReason.NoSuchFace => $"no metrics for this weight/style of '{MissingFamily}'",
        _ => null,
    };
}

/// <summary>
/// 把 XAML 的 <c>FontFamily</c>／<c>FontWeight</c>／<c>FontStyle</c> 對應到度量。
///
/// 支援 WPF 的後備清單寫法：<c>FontFamily="Segoe UI, Microsoft JhengHei"</c> ——
/// 逐個碼位往後找第一支畫得出來的（實際的逐碼位後備在 <see cref="TextMeasure"/>）。
///
/// ⚠ **寧可說「沒有這支字型的度量」，也不要拿別支字型去量。** 全面重讀時發現第一版有三個
///   會默默量錯的地方：
///
///   1. 清單的**第一個**家族不在度量檔裡時，直接拿第二個去量。
///      <c>"Segoe UI Variable, Segoe UI"</c> 在 Windows 11 上用的是前者，寬度不一樣。
///   2. 字重取「最接近的」而且**沒有距離上限**：度量檔只有 Regular 時，粗體文字用 Regular 的寬度去量。
///      粗體比較寬（Segoe UI 約 +7%），連 WPF 的模擬粗體也比較寬（LayoutOracle 實測）—— 低估文字寬度就是漏報。
///   3. 壓縮字面（Arial Narrow 在字型家族模型裡屬於 Arial）可能被挑去量正常寬度的文字。
/// </summary>
public sealed class FontResolver
{
    private readonly List<FontMetrics> _all;
    private readonly Dictionary<string, List<FontMetrics>> _byFamily;
    private readonly Dictionary<string, List<FontMetrics>> _byLegacy;
    private readonly HashSet<string> _absent;

    private readonly HashSet<string>? _installed;

    /// <param name="absentFamilies">使用者宣告「目標機器上沒有」的家族：出現在後備清單裡時直接跳過。</param>
    /// <param name="installedFamilies">匯出那台機器上裝的家族名（度量檔的 installedFamilies）；有它才照 WPF 拆「家族 + 字面名稱」。</param>
    public FontResolver(IEnumerable<FontMetrics> metrics, IEnumerable<string>? absentFamilies = null, IEnumerable<string>? installedFamilies = null)
    {
        _installed = installedFamilies is null ? null : new HashSet<string>(installedFamilies.Select(Normalize), StringComparer.Ordinal);
        _all = [.. metrics];
        _byFamily = _all
            .GroupBy(m => Normalize(m.Family), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // 其他語言的家族名（「微軟正黑體」）指向同一組字面
        foreach (var m in _all)
            foreach (var alias in m.Aliases)
                if (_byFamily.TryGetValue(Normalize(m.Family), out var faces))
                    _byFamily.TryAdd(Normalize(alias), faces);
        _byLegacy = _all
            .Where(m => !string.IsNullOrWhiteSpace(m.LegacyFamily)
                        && !string.Equals(Normalize(m.LegacyFamily!), Normalize(m.Family), StringComparison.Ordinal))
            .GroupBy(m => Normalize(m.LegacyFamily!), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        _absent = new HashSet<string>((absentFamilies ?? []).Select(Normalize), StringComparer.Ordinal);
    }

    public int FamilyCount => _byFamily.Count;
    public IEnumerable<string> Families => _all.Select(m => m.Family).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>家族名正規化：WPF 的字型家族比對不分大小寫，也不在意多餘空白。</summary>
    private static string Normalize(string family) =>
        string.Join(' ', family.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
              .ToLowerInvariant();

    /// <summary>
    /// 解析一個 FontFamily 字串（可能是逗號分隔的後備清單）。
    /// </summary>
    /// <param name="fontFamily">XAML 裡的值。null／空字串代表沒寫，走 <paramref name="defaultFamily"/>。</param>
    /// <param name="language">元素的 Language（xml:lang）。決定 WPF 拿哪支字型補中日韓的字；WPF 預設是 en-US。</param>
    public FontLookup Resolve(string? fontFamily, int weight, FontStyleKind style, string defaultFamily,
        string language = FontFallback.DefaultLanguage)
    {
        var names = SplitFamilies(fontFamily);
        if (names.Count == 0) names = [defaultFamily];

        var chain = new List<FontMetrics>();

        foreach (var n in names)
        {
            var key = Normalize(n);
            if (_absent.Contains(key)) continue;

            FontMetrics? face = null;
            var known = false;

            List<FontMetrics>? siblings = null;
            if (_byFamily.TryGetValue(key, out var faces))
            {
                known = true;
                face = PickFace(faces, weight, style, strict: true);
                siblings = faces;
            }
            else if (_byLegacy.TryGetValue(key, out var legacy))
            {
                // 舊式家族名（"Segoe UI Semibold"）：那個名字本身就指定了字面，只接受不另外加粗的寫法
                known = true;
                if (weight == 400) face = PickStyle(legacy, style)?.FirstOrDefault();
            }
            else if (SplitFaceName(key, weight, style) is { } split)
            {
                // WPF：整個名字不是裝著的家族時，取最長的家族名前綴，剩下的字當字面名稱
                known = true;
                face = PickFace(split.Faces, split.Weight, split.Style, strict: true);
                siblings = split.Faces;
            }

            if (face is not null)
            {
                chain.Add(face);
                // ⚠ 這個字面畫不出來的字，WPF 先找**同一個家族的其他字面**，才換下一個家族或全域後備。
                //   實測：微軟正黑體 UI 的 Regular 沒有西里爾字母，Light（msjhl.ttc）有 —— WPF 用 Light 的寬度，
                //   粗體文字也一樣（不加寬）；Segoe UI Black 缺的字用 Bold 的、Italic 缺的字用 Regular 的。
                //   沒做這一步時，ScreenToGif 的烏克蘭文說明在正黑體下少算 13.6 px。
                if (siblings is not null)
                    foreach (var s in OrderedFaces(siblings, weight, style))
                        if (!ReferenceEquals(s, face)) chain.Add(s);
                continue;
            }

            // ⚠ 主要字型（清單裡第一支可用的）不知道就不能量 —— 後面那支只是它畫不出來時的後備。
            //   主要字型已經找到之後，後面的缺漏只影響後備，交給自動後備並標記。
            if (chain.Count == 0)
                return new FontLookup([], known ? FontMissReason.NoSuchFace : FontMissReason.NoSuchFamily, n, 0);
        }

        if (chain.Count == 0)
            return new FontLookup([], FontMissReason.NoSuchFamily, names[0], 0);

        var declared = chain.Count;

        // ⚠ 明寫的字型都畫不出來的字，WPF 照它自己的複合字型規則補（GlobalUserInterface.CompositeFont）：
        //   依字的範圍與元素的語言挑一串候選字型。只認明寫的後備清單會讓真實專案的中文字串大量變成
        //   「量不到」（實測 QuillNest 掉到個位數）；而第一版「度量檔裡的其他字型依字母順序」
        //   跟 WPF 無關，日文假名量寬了 40%（CorpusRender 實測）。
        if (!AutoFallback) return new FontLookup(chain, FontMissReason.None, null, declared) { Language = language };

        var cache = new Dictionary<int, (FontMetrics, double)?>();
        (FontMetrics Face, double Scale)? Fallback(int cp)
        {
            if (cache.TryGetValue(cp, out var hit)) return hit;
            (FontMetrics, double)? found = null;
            var (targets, scale) = FontFallback.For(cp, language);
            foreach (var target in targets)
            {
                var key = Normalize(target);
                if (_absent.Contains(key) || !_byFamily.TryGetValue(key, out var faces)) continue;
                // 後備的那個家族也一樣：挑最接近、而且畫得出這個字的字面
                foreach (var face in OrderedFaces(faces, weight, style))
                    if (face.TryAdvance(cp, out _)) { found = (face, scale); break; }
                if (found is not null) break;
            }
            cache[cp] = found;
            return found;
        }

        return new FontLookup(chain, FontMissReason.None, null, declared)
        {
            Fallback = Fallback,
            Language = language,
            FallbackCandidates = cp => FontFallback.For(cp, language).Targets,
        };
    }

    private static readonly Dictionary<string, int> WeightWords = new(StringComparer.Ordinal)
    {
        ["thin"] = 100, ["hairline"] = 100, ["extralight"] = 200, ["ultralight"] = 200, ["light"] = 300, ["semilight"] = 350, ["demilight"] = 350,
        ["regular"] = 400, ["normal"] = 400, ["book"] = 400, ["medium"] = 500, ["semibold"] = 600, ["demibold"] = 600, ["bold"] = 700,
        ["extrabold"] = 800, ["ultrabold"] = 800, ["black"] = 900, ["heavy"] = 900,
    };

    /// <summary>
    /// WPF 的「家族 + 字面名稱」：<c>FontFamily="Segoe UI Variable"</c> 畫的是 Segoe UI，<c>"Times New Roman Italic"</c> 是斜體，
    /// <c>"Arial Foo Bar"</c> 照 XAML 的字重畫 Arial（WpfOracle 實測）。剩下的字全是字重／樣式就照用；一個都不是就忽略；混著的不知道 WPF 怎麼讀，不拆。
    /// 只在度量檔記了匯出機器裝了哪些家族、而且整個名字不在裡面時才拆 —— 否則「Segoe UI Historic」這種真的另一支字型會被當成 Segoe UI。
    /// </summary>
    private (List<FontMetrics> Faces, int Weight, FontStyleKind Style)? SplitFaceName(string key, int weight, FontStyleKind style)
    {
        if (_installed is null || _installed.Contains(key)) return null;
        var words = key.Split(' ');
        for (var k = words.Length - 1; k >= 1; k--)
        {
            var family = string.Join(' ', words[..k]);
            if (!_installed.Contains(family) && !_byFamily.ContainsKey(family)) continue;
            if (!_byFamily.TryGetValue(family, out var faces)) return null;   // WPF 用的家族不在度量檔裡
            var rest = words[k..];
            var styleWords = rest.Count(w => WeightWords.ContainsKey(w) || w is "italic" or "oblique");
            if (styleWords == 0) return (faces, weight, style);
            if (styleWords != rest.Length) return null;
            var w = weight;
            var st = style;
            foreach (var word in rest)
                if (WeightWords.TryGetValue(word, out var ww)) w = ww; else st = FontStyleKind.Italic;
            return (faces, w, st);
        }
        return null;
    }

    /// <summary>照 WPF 的複合字型規則補字（見 <see cref="FontFallback"/>）。
    /// 關掉它會讓明寫字型畫不出來的字一律量不到。</summary>
    public bool AutoFallback { get; init; } = true;

    /// <summary>字重相差超過這個值就不算同一個字面（Regular 400 與 Bold 700 之間不能互相代替）。</summary>
    private const int MaxWeightDistance = 100;

    /// <summary>
    /// 挑字面：寬度要是 Normal、樣式對得上、字重最接近。
    /// </summary>
    /// <param name="strict">主要字型用嚴格規則（字重太遠就不要）；自動後備用寬鬆規則
    /// （後備字型多半是 CJK，全形字的寬度不隨字重變）。</param>
    private static FontMetrics? PickFace(List<FontMetrics> faces, int weight, FontStyleKind style, bool strict)
    {
        var candidates = PickStyle(faces, style);
        if (candidates is null) return null;

        if (strict)
        {
            // 可變字型的檔案裡只有預設軸位置的寬度；其他字重的寬度在 HVAR／gvar 裡，我們沒讀
            candidates = candidates.Where(f => !f.Variable || f.Weight == weight).ToList();
            if (candidates.Count == 0) return null;
        }

        // 距離相同時：要粗的往粗的挑，要細的往細的挑（WPF／CSS 的字重比對方向）
        var best = candidates
            .OrderBy(f => Math.Abs(f.Weight - weight))
            .ThenBy(f => weight >= 500 ? -f.Weight : f.Weight)
            .First();

        return strict && Math.Abs(best.Weight - weight) > MaxWeightDistance ? null : best;
    }

    /// <summary>
    /// 一個家族的所有字面，依 WPF（DirectWrite 的 GetMatchingFonts）比對的接近程度排序：
    /// 寬度 Normal 優先、再來樣式（正體／斜體）對得上的、再來字重最接近的。
    /// 可變字型只收字重剛好的那一筆 —— 其他字重的寬度不在度量檔裡。
    /// </summary>
    internal static IEnumerable<FontMetrics> OrderedFaces(List<FontMetrics> faces, int weight, FontStyleKind style)
    {
        var wantUpright = style == FontStyleKind.Normal;
        return faces
            .Where(f => !f.Variable || f.Weight == weight)
            .OrderBy(f => f.Stretch.Equals("Normal", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => (f.Style == FontStyleKind.Normal) == wantUpright ? 0 : 1)
            .ThenBy(f => Math.Abs(f.Weight - weight))
            .ThenBy(f => weight >= 500 ? -f.Weight : f.Weight);
    }

    /// <summary>寬度 Normal、樣式對得上的字面。要斜體但沒有真斜體時退回正體 ——
    /// WPF 會做合成斜體，前進寬度不變。要正體卻只有斜體則沒有可用的。</summary>
    private static List<FontMetrics>? PickStyle(List<FontMetrics> faces, FontStyleKind style)
    {
        var normalWidth = faces.Where(f => f.Stretch.Equals("Normal", StringComparison.OrdinalIgnoreCase)).ToList();
        if (normalWidth.Count == 0) return null;

        var wantUpright = style == FontStyleKind.Normal;
        var same = normalWidth.Where(f => (f.Style == FontStyleKind.Normal) == wantUpright).ToList();
        if (same.Count > 0) return same;

        return wantUpright ? null : normalWidth;
    }

    public static List<string> SplitFamilies(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)) return [];

        // 綁定／標記延伸：不是字型名
        var t = fontFamily.Trim();
        if (t.StartsWith('{')) return [];

        // pack URI 寫法：".../#Family Name" —— 取井號後面那段
        var list = new List<string>();
        foreach (var part in t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part;
            var hash = p.LastIndexOf('#');
            if (hash >= 0 && hash + 1 < p.Length) p = p[(hash + 1)..].Trim();
            if (p.Length > 0) list.Add(p);
        }
        return list;
    }

    /// <summary>WPF 的 FontWeight 名稱 → 數值。認不得就回 400。</summary>
    public static int ParseWeight(string? v) => TryParseWeight(v, out var w) ? w : 400;

    /// <summary>沒寫（null／空白）算成功，是 400。寫了但認不得才是 false。</summary>
    public static bool TryParseWeight(string? v, out int weight)
    {
        weight = 400;
        if (string.IsNullOrWhiteSpace(v)) return true;

        var t = v.Trim();
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 999)
        {
            weight = n;
            return true;
        }

        int? named = t.ToLowerInvariant() switch
        {
            "thin" => 100,
            "extralight" or "ultralight" => 200,
            "light" => 300,
            "normal" or "regular" => 400,
            "medium" => 500,
            "demibold" or "semibold" => 600,
            "bold" => 700,
            "extrabold" or "ultrabold" => 800,
            "black" or "heavy" => 900,
            "extrablack" or "ultrablack" => 950,
            _ => null,
        };

        if (named is not { } w) return false;
        weight = w;
        return true;
    }

    public static FontStyleKind ParseStyle(string? v) =>
        v?.Trim().ToLowerInvariant() switch
        {
            "italic" => FontStyleKind.Italic,
            "oblique" => FontStyleKind.Oblique,
            _ => FontStyleKind.Normal,
        };
}
