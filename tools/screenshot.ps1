# Saves a PNG of the whole virtual screen (all monitors), or of one region, so a reviewer can look at the UI.
# Usage:  powershell -NoProfile -File tools/screenshot.ps1 -Out C:\path\shot.png [-X 0 -Y 0 -Width 800 -Height 600]
# Coordinates are physical pixels.

param(
    [Parameter(Mandatory)] [string]$Out,
    [int]$X, [int]$Y, [int]$Width, [int]$Height
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System.Runtime.InteropServices;
public static class DpiHelper {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr value);
}
'@
[DpiHelper]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null  # per-monitor v2, so pixels are physical

if (-not $Width -or -not $Height) {
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $X = $bounds.X; $Y = $bounds.Y; $Width = $bounds.Width; $Height = $bounds.Height
}

$bmp = New-Object System.Drawing.Bitmap $Width, $Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X, $Y, 0, 0, $bmp.Size)
$g.Dispose()
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Saved $Out ($Width x $Height at $X,$Y)"
