namespace XamlFit.Core.Layout;

/// <summary>
/// 一個文字位置的可用寬度算不算得出來。
///
/// ⚠ <see cref="NoDeclaredLimit"/> 跟 <see cref="Unmeasurable"/> 是兩件事，不准混。
///   前者是「分析範圍內查不到上界」，後者是「路上有東西擋住看不下去」。
///   M0 第一版把兩者混在一起，於是把「確定沒有宣告上界」當成覆蓋率的缺口。
///   —— 但它也**不是**「確定不會被裁」：執行期的視窗有寬度。
///      所以它既不計入覆蓋率，也不能拿來宣稱安全。
/// </summary>
public enum WidthStatus
{
    /// <summary>算得出來。</summary>
    Known,

    /// <summary>分析範圍內沒有任何宣告的寬度上界。
    /// 輸出時必須是具名值，**不准是空白** —— 在地化負責人看到空白會當成「沒限制」。</summary>
    NoDeclaredLimit,

    /// <summary>有上界，但路上有 Grid 的 Auto／星號欄（v2 解）。</summary>
    NeedsGrid,

    /// <summary>有上界，但路上有會限制項目寬度的 ItemsControl（v2 解）。</summary>
    NeedsItems,

    /// <summary>在 ControlTemplate／DataTemplate／Style 裡：寬度由使用端決定。</summary>
    InTemplate,

    /// <summary>其他看不下去的情形（chrome 未知、SizeToContent、水平捲動…）。</summary>
    Unmeasurable,
}

public sealed record AvailableWidth(
    WidthStatus Status,
    double Px,
    string BoundSource,
    int BoundLine,
    bool UsedChromeTable,
    string Reason)
{
    public static AvailableWidth NotKnown(WidthStatus status, string reason) =>
        new(status, 0, "", 0, false, reason);

    public bool IsMeasurable => Status == WidthStatus.Known && Px > 0;
}
