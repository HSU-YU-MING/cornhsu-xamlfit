using System.Xml.Linq;

namespace XamlFit.Core.Layout;

/// <summary>
/// 算出一個元素的「期望寬度」—— WPF 版面術語裡的 DesiredSize.Width，含它自己的 Margin。
///
/// 解 Grid 的 <c>Auto</c> 欄一定要這個：Auto 欄的寬度等於該欄所有子元素期望寬度的最大值。
///
/// ⚠ 算不出來就回 <c>null</c>，**絕對不要猜**。
///   Auto 欄的寬度猜錯，整個 Grid 的星號分配跟著錯，而且錯的方向不可控 ——
///   可能高估可用寬度（漏報溢出），那是最糟的失敗。
/// </summary>
public interface IElementMeasurer
{
    /// <summary>期望寬度（DIP，含 Margin）。靜態算不出來時回 null。</summary>
    double? DesiredWidth(XElement element, int depth);
}
