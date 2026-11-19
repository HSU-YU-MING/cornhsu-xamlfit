# 標題列量測：每種 WindowStyle／ResizeMode、幾種 Width，量「標題最多看得見多寬」與標題用的字型。
# 一次跑完印成表；在每一種顯示縮放下各跑一次（縮放要由使用者自己切換）。
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class W32 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
}
'@
function Capture($hwnd) {
  $r = New-Object W32+RECT; [void][W32]::GetWindowRect($hwnd, [ref]$r)
  $bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T))
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [void][W32]::PrintWindow($hwnd, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
  return $bmp
}
function Settle($w) { $w.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ContextIdle); Start-Sleep -Milliseconds 350 }
# 標題像素：跟空標題比，按鈕左邊為止、標題列高度以內，回傳 (最左, 最右)
function Span($w, $hwnd, $blank, $title, $limit, $h) {
  $w.Title = $title; Settle $w
  $b = Capture $hwnd
  $minX = 99999; $maxX = -1
  for ($x = 0; $x -lt $limit; $x++) {
    for ($y = 0; $y -lt $h; $y++) {
      if ($blank.GetPixel($x, $y).ToArgb() -ne $b.GetPixel($x, $y).ToArgb()) { if ($x -lt $minX) { $minX = $x }; $maxX = $x; break }
    }
  }
  $b.Dispose()
  return @($minX, $maxX)
}
function Ink($text, $family, $size) {
  $tf = New-Object Windows.Media.Typeface((New-Object Windows.Media.FontFamily($family)), 'Normal', 'Normal', 'Normal')
  $ft = New-Object Windows.Media.FormattedText($text, [Globalization.CultureInfo]::GetCultureInfo('en-US'), 'LeftToRight', $tf, $size, [Windows.Media.Brushes]::Black, 1.0)
  $g = $ft.BuildGeometry((New-Object Windows.Point(0,0)))
  return $g.Bounds.Width
}

$configs = @(
  @{ S = 'SingleBorderWindow'; R = 'CanResize' }, @{ S = 'SingleBorderWindow'; R = 'CanMinimize' },
  @{ S = 'SingleBorderWindow'; R = 'NoResize' }, @{ S = 'SingleBorderWindow'; R = 'CanResizeWithGrip' },
  @{ S = 'ThreeDBorderWindow'; R = 'CanResize' }, @{ S = 'ToolWindow'; R = 'CanResize' }
)
$fontReport = $null
foreach ($c in $configs) {
  $worst = 0; $worstAt = 0; $start = 0; $buttons = 0; $scale = 1
  foreach ($width in 300, 400, 550, 800) {
    $w = New-Object Windows.Window
    $w.Width = $width; $w.Height = 150; $w.Left = 60; $w.Top = 60
    $w.WindowStyle = $c.S; $w.ResizeMode = $c.R; $w.Title = ''
    $w.Show(); Settle $w
    $hwnd = (New-Object Windows.Interop.WindowInteropHelper($w)).Handle
    $scale = [W32]::GetDpiForWindow($hwnd) / 96.0
    $wr = New-Object W32+RECT; [void][W32]::GetWindowRect($hwnd, [ref]$wr)
    $fr = New-Object W32+RECT; [void][W32]::DwmGetWindowAttribute($hwnd, 9, [ref]$fr, 16)
    $bt = New-Object W32+RECT; [void][W32]::DwmGetWindowAttribute($hwnd, 5, [ref]$bt, 16)
    $offL = $fr.L - $wr.L
    $h = [Math]::Max(1, $bt.B)
    $blank = Capture $hwnd
    # 一長串 l：刪節號停在可用區的右緣之前 —— 用它當「看得見多寬」的下界（保守）
    $s = Span $w $hwnd $blank ('l' * 400) ($bt.L - 1) $h
    $area = ($s[1] - $s[0] + 1) / $scale
    $lost = $width - $area
    if ($lost -gt $worst) { $worst = $lost; $worstAt = $width }
    $start = ($s[0] - $offL) / $scale
    $buttons = ($bt.R - $bt.L) / $scale
    if ($null -eq $fontReport -and $c.S -eq 'SingleBorderWindow' -and $c.R -eq 'CanResize' -and $width -eq 800) {
      $lat = Span $w $hwnd $blank 'Save Document As' ($bt.L - 1) $h
      $cjk = Span $w $hwnd $blank '另存新檔設定' ($bt.L - 1) $h
      $latW = ($lat[1] - $lat[0] + 1) / $scale; $cjkW = ($cjk[1] - $cjk[0] + 1) / $scale
      $cands = 'Segoe UI', 'Segoe UI Variable Display', 'Segoe UI Variable Text', 'Microsoft JhengHei UI', 'Microsoft YaHei UI', 'Yu Gothic UI'
      $fontReport = "caption font check: 'Save Document As' ink {0:F1} DIP, '另存新檔設定' ink {1:F1} DIP; at 12 DIP: " -f $latW, $cjkW
      $fontReport += ($cands | ForEach-Object { "{0} {1:F1}/{2:F1}" -f $_, (Ink 'Save Document As' $_ 12), (Ink '另存新檔設定' $_ 12) }) -join '; '
    }
    $blank.Dispose(); $w.Close()
  }
  "{0,-18} {1,-17} scale {2}: title starts {3,5:F1} DIP in, buttons {4,5:F1} DIP, title loses at most {5,5:F1} DIP of Width (at Width {6})" -f $c.S, $c.R, $scale, $start, $buttons, $worst, $worstAt
}
$fontReport
"system caption font (WPF SystemFonts): {0} {1}; CaptionHeight {2}" -f [Windows.SystemFonts]::CaptionFontFamily.Source, [Windows.SystemFonts]::CaptionFontSize, [Windows.SystemParameters]::CaptionHeight
