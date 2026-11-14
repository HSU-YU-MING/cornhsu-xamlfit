namespace XamlFit.Core.Analysis;

/// <summary>
/// 翻譯之後字串會長多少。
///
/// ⚠ **膨脹率跟原字串長度強相關，不是單一倍數。** 短字串膨脹最兇，
///   而按鈕標籤正是最短的那一類（`Save` → `Speichern` 是 +150%）。
///   用單一 1.4 會在短字串上嚴重低估、在長句上過度報警。
///
/// 分段表採 W3C 式的級距；Unity 偽本地化預設的倍率形狀一致
/// （0–10 字元用 2×、10–20 用 1×、20–30 用 0.8×）。
///
/// ⚠ **這張表只用來估算「大約幾個字」與 `--pseudo` 模式，不參與 CI 判定。**
///   拿猜測去擋別人的 PR，工具會被關掉。判定一律用真實譯文。
/// </summary>
public static class ExpansionTable
{
    /// <summary>(原字串長度上限, 成長倍率)。例如 2.0 代表「可能長到原本的兩倍」。</summary>
    private static readonly (int MaxLength, double Factor)[] Buckets =
    [
        (10, 2.00),
        (20, 1.80),
        (30, 1.60),
        (50, 1.30),
        (int.MaxValue, 1.20),
    ];

    /// <summary>這個長度的字串，翻譯後可能長到原本的幾倍。</summary>
    public static double FactorFor(int sourceLength)
    {
        foreach (var (max, f) in Buckets)
            if (sourceLength <= max) return f;
        return 1.20;
    }

    public static double FactorFor(string? source) => FactorFor(source?.Length ?? 0);

    /// <summary>表格本身，給報告與文件引用。</summary>
    public static IReadOnlyList<(int MaxLength, double Factor)> Rows => Buckets;
}
