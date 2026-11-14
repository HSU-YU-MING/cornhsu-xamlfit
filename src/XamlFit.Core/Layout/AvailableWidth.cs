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

    /// <summary>有上界，但路上有解不開的 Grid <c>Auto</c>／<c>*</c> 欄。</summary>
    NeedsGrid,

    /// <summary>有上界，但路上有項目寬度另有一套算法的清單控制項（DataGrid、自訂 ItemsPanel…）。</summary>
    NeedsItems,

    /// <summary>在 ControlTemplate／DataTemplate／Style／資源字典裡：寬度由使用端決定。</summary>
    InTemplate,

    /// <summary>其他看不下去的情形（內距未知、屬性讀不出來、兄弟元素寬度未知…）。</summary>
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
    /// <summary>算的時候假設了垂直捲軸在場（少算可用寬度只會誤報，多算會漏報）。</summary>
    public bool AssumedScrollBar { get; init; }

    /// <summary>
    /// 這個位置的文字是用**無限寬**去量的（水平 StackPanel、Grid 的 Auto 欄、WrapPanel）。
    /// WPF 實測：這種位置上 <c>TextWrapping="Wrap"</c> **不會換行**，文字一路畫出去被裁掉。
    /// 判定時要把它當成 NoWrap，否則會把「溢出」報成無害的「換行」。
    /// </summary>
    public bool WrapDefeated { get; init; }

    /// <summary>
    /// 這個寬度是「扣掉同一排其他在地化字串之後」剩下的 —— 那些字串換一個語言就不一樣寬。
    /// check 模式會用每個語言自己的譯文重算；budget 的數字則是「其他字串維持來源語言寬度」時的值。
    /// </summary>
    public bool DependsOnNeighbourText { get; init; }

    /// <summary>同一行裡、這個 Run 前面的行內內容佔掉的寬度（DIP）。0 = 它在行首。
    /// 報告要說出來 —— 不然「只剩 221 px」在一個 560 寬的框裡看起來像工具算錯。</summary>
    public double TakenByInlineBefore { get; init; }

    public static AvailableWidth NotKnown(WidthStatus status, string reason) =>
        new(status, 0, "", 0, false, reason);

    /// <summary>算得出來。⚠ Px 可以是 0：同一排前面的兄弟已經佔滿，這個元素被擠到看不見。</summary>
    public bool IsMeasurable => Status == WidthStatus.Known && Px >= 0;
}
