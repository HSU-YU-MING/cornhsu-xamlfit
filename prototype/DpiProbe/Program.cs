// DpiProbe —— 不改顯示設定，直接問 Windows：在 100%~300% 每一種縮放下，
// 標準視窗的左右邊框合計幾個像素、垂直捲軸幾個像素，換算成 WPF 的 DIP 是多少。
//
// 為什麼要它：control-chrome.json 的 windowFrame 寫 16、scrollBarWidth 寫 17.5，
// 理由是「100% 縮放時最大」—— 但那是推論，這台機器只量過 125%。
//
// 用的是 Windows 10 1607 起的 *ForDpi API：AdjustWindowRectExForDpi 回答
// 「客戶區要多大、整個視窗就要多大」，差額就是非工作區（邊框）。
// 拋棄式工具，Windows only。

using System.Globalization;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_EX_CLIENTEDGE = 0x00000200;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_WINDOWEDGE = 0x00000100;
    private const uint WS_EX_DLGMODALFRAME = 0x00000001;

    private const int SM_CXVSCROLL = 2;
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CXPADDEDBORDER = 92;
    private const int SM_CXFIXEDFRAME = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectExForDpi(ref RECT rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    private static int Frame(uint style, uint exStyle, uint dpi)
    {
        var r = new RECT { Left = 0, Top = 0, Right = 1000, Bottom = 1000 };
        if (!AdjustWindowRectExForDpi(ref r, style, false, exStyle, dpi)) return -1;
        return (r.Right - r.Left) - 1000;
    }

    private static int Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.WriteLine($"system DPI right now: {GetDpiForSystem()}");
        Console.WriteLine();

        // WPF 的 WindowStyle 對應的 Win32 樣式（WPF 原始碼 Window.cs 的 CreateWindowStyle）
        (string Name, uint Style, uint Ex)[] styles =
        [
            ("SingleBorderWindow, CanResize", WS_OVERLAPPEDWINDOW, WS_EX_WINDOWEDGE),
            ("SingleBorderWindow, NoResize", WS_CAPTION | WS_SYSMENU, WS_EX_WINDOWEDGE | WS_EX_DLGMODALFRAME),
            ("ThreeDBorderWindow, CanResize", WS_OVERLAPPEDWINDOW, WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE),
            ("ToolWindow, CanResize", WS_OVERLAPPEDWINDOW, WS_EX_TOOLWINDOW | WS_EX_WINDOWEDGE),
            ("None, CanResize", WS_THICKFRAME, 0),
        ];

        Console.WriteLine($"{"scale",-6} {"dpi",4}  {"scrollbar px",12} {"DIP",6}  " + string.Join("  ", styles.Select(s => $"{s.Name,-30}")));
        foreach (var dpi in new uint[] { 96, 120, 144, 168, 192, 240, 288 })
        {
            var scale = dpi / 96.0;
            var sb = GetSystemMetricsForDpi(SM_CXVSCROLL, dpi);
            var cells = styles.Select(s =>
            {
                var px = Frame(s.Style, s.Ex, dpi);
                return $"{px,3} px = {px / scale,6:F2} DIP".PadRight(30);
            });
            Console.WriteLine((
                $"{scale * 100,4:F0}%  {dpi,4}  {sb,12} {sb / scale,6:F2}  " + string.Join("  ", cells)));
        }

        Console.WriteLine();
        Console.WriteLine("raw metrics (px): sizeframe / paddedborder / fixedframe");
        foreach (var dpi in new uint[] { 96, 120, 144, 192 })
            Console.WriteLine($"  dpi {dpi}: {GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi)} / {GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi)} / {GetSystemMetricsForDpi(SM_CXFIXEDFRAME, dpi)}");

        return 0;
    }
}
