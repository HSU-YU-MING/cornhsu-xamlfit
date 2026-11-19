# 標題文字的寬度：DWM 畫出來的字（像素比對）跟 Segoe UI 12 的線性寬度（WPF Ideal）比。
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
function Settle($w) { $w.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ContextIdle); Start-Sleep -Milliseconds 300 }
function Linear($text, $family) {
  $tf = New-Object Windows.Media.Typeface((New-Object Windows.Media.FontFamily($family)), 'Normal', 'Normal', 'Normal')
  $ft = New-Object Windows.Media.FormattedText($text, [Globalization.CultureInfo]::GetCultureInfo('en-US'), 'LeftToRight', $tf, 12, [Windows.Media.Brushes]::Black, 1.0)
  $g = $ft.BuildGeometry((New-Object Windows.Point(0,0)))
  return @($ft.WidthIncludingTrailingWhitespace, $g.Bounds.Width)
}
$strings = @('Settings', 'Export as GIF', 'Preferences', 'Save Document As', 'WWWWWWWWWW', 'iiiiiiiiiiiiiiii', 'mmmmmmmmmm',
  'Benutzereinstellungen speichern', "Paramètres d'exportation", 'Configuración avanzada del editor', 'Impostazioni di registrazione',
  'Настройки экспорта видео', 'Ρυθμίσεις εξαγωγής', 'Ustawienia nagrywania ekranu', 'Opções de gravação da tela',
  'ScreenToGif - Editor', 'Kindling — Workspace', 'A', 'The quick brown fox jumps over the lazy dog', 'Ünïçødé Àççéñtš', '1234567890', 'Export (12 frames, 1920x1080)')
$w = New-Object Windows.Window
$w.Width = 1400; $w.Height = 150; $w.Left = 20; $w.Top = 40; $w.Title = ''
$w.Show(); Settle $w
$hwnd = (New-Object Windows.Interop.WindowInteropHelper($w)).Handle
$scale = [W32]::GetDpiForWindow($hwnd) / 96.0
$bt = New-Object W32+RECT; [void][W32]::DwmGetWindowAttribute($hwnd, 5, [ref]$bt, 16)
$blank = Capture $hwnd
$worst = -99; $worstText = ''
"scale $scale"
foreach ($t in $strings) {
  $w.Title = $t; Settle $w
  $b = Capture $hwnd
  $minX = 99999; $maxX = -1
  for ($x = 0; $x -lt ($bt.L - 1); $x++) { for ($y = 0; $y -lt $bt.B; $y++) { if ($blank.GetPixel($x, $y).ToArgb() -ne $b.GetPixel($x, $y).ToArgb()) { if ($x -lt $minX) { $minX = $x }; $maxX = $x; break } } }
  $b.Dispose()
  $ink = ($maxX - $minX + 1) / $scale
  $seg = Linear $t 'Segoe UI'; $var = Linear $t 'Segoe UI Variable Text'
  $excess = ($ink - $seg[0]) / [Math]::Max(1, $seg[0]) * 100
  if ($excess -gt $worst) { $worst = $excess; $worstText = $t }
  "{0,-44} drawn ink {1,6:F1} | Segoe UI advance {2,6:F1} ink {3,6:F1} | Variable Text advance {4,6:F1} | drawn ink vs Segoe UI advance {5,5:F1}%" -f $t, $ink, $seg[0], $seg[1], $var[0], $excess
}
"worst: drawn ink exceeds the Segoe UI 12 advance by {0:F1}% ('{1}')" -f $worst, $worstText
$w.Close()
