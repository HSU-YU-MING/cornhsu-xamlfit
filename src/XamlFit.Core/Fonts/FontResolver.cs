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
    int DeclaredCount = int.MaxValue);

/// <summary>
/// 把 XAML 的 <c>FontFamily</c>／<c>FontWeight</c>／<c>FontStyle</c> 對應到度量。
///
/// 支援 WPF 的後備清單寫法：<c>FontFamily="Segoe UI, Microsoft JhengHei"</c> ——
/// 逐個碼位往後找第一支畫得出來的（實際的逐碼位後備在 <see cref="TextMeasure"/>）。
/// </summary>
public sealed class FontResolver
{
    private readonly List<FontMetrics> _all;
    private readonly Dictionary<string, List<FontMetrics>> _byFamily;

    public FontResolver(IEnumerable<FontMetrics> metrics)
    {
        _all = [.. metrics];
        _byFamily = _all
            .GroupBy(m => Normalize(m.Family), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
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
    public FontLookup Resolve(string? fontFamily, int weight, FontStyleKind style, string defaultFamily)
    {
        var names = SplitFamilies(fontFamily);
        if (names.Count == 0) names = [defaultFamily];

        var chain = new List<FontMetrics>();
        string? firstMissing = null;
        var sawFamily = false;

        foreach (var n in names)
        {
            if (!_byFamily.TryGetValue(Normalize(n), out var faces))
            {
                firstMissing ??= n;
                continue;
            }

            sawFamily = true;
            var best = PickFace(faces, weight, style);
            if (best is not null) chain.Add(best);
        }

        var declared = chain.Count;

        // ⚠ WPF 會做**系統層級的自動字型後備**：Segoe UI 畫不出中文時，Windows 會自己
        //   從字型連結表找一支補上，XAML 裡不必寫任何東西。
        //   只認明寫的後備清單，會讓真實專案裡大量中文字串變成「量不到」——
        //   實測 QuillNest 的可量測字串因此掉到個位數。
        //   所以這裡自動把度量檔裡其他家族接在後面，並標記「用到自動後備」：
        //   Windows 實際選哪一支我們不知道，報告要講出來。
        if (AutoFallback && chain.Count > 0)
        {
            var already = new HashSet<string>(chain.Select(c => Normalize(c.Family)), StringComparer.Ordinal);
            foreach (var family in _byFamily)
            {
                if (already.Contains(family.Key)) continue;
                var face = PickFace(family.Value, weight, style);
                if (face is not null) chain.Add(face);
            }
        }

        if (chain.Count > 0) return new FontLookup(chain, FontMissReason.None, null, declared);

        return new FontLookup(
            [],
            sawFamily ? FontMissReason.NoSuchFace : FontMissReason.NoSuchFamily,
            firstMissing ?? names[0],
            0);
    }

    /// <summary>自動補上度量檔裡的其他字型當後備（模仿 Windows 的字型連結）。
    /// 關掉它會讓缺字的位置一律變成不可量測 —— 更保守，但覆蓋率會大幅下降。</summary>
    public bool AutoFallback { get; init; } = true;

    /// <summary>
    /// 字重比對。先要樣式對得上，再挑字重最接近的。
    ///
    /// ⚠ 不用 CSS 的完整字重比對演算法（那個規則在 400/500 附近有刻意的不對稱）——
    ///   對 UI 文字量測來說，取最接近的就夠，而且行為好預測。差異記在盲區表。
    /// </summary>
    private static FontMetrics? PickFace(List<FontMetrics> faces, int weight, FontStyleKind style)
    {
        var sameStyle = faces.Where(f => f.Style == style).ToList();

        // 要斜體但沒有真斜體時，退回正體並讓報告標註 ——
        // WPF 會做合成斜體（寬度幾乎不變），所以用正體的寬度是合理近似。
        if (sameStyle.Count == 0) sameStyle = faces;
        if (sameStyle.Count == 0) return null;

        return sameStyle.MinBy(f => Math.Abs(f.Weight - weight));
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
    public static int ParseWeight(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return 400;
        var t = v.Trim();
        if (int.TryParse(t, out var n) && n is >= 1 and <= 1000) return n;
        return t.ToLowerInvariant() switch
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
            _ => 400,
        };
    }

    public static FontStyleKind ParseStyle(string? v) =>
        v?.Trim().ToLowerInvariant() switch
        {
            "italic" => FontStyleKind.Italic,
            "oblique" => FontStyleKind.Oblique,
            _ => FontStyleKind.Normal,
        };
}
