# Draws the Loadout icon (a small 8-slice wheel with the top slice highlighted)
# and writes src/Loadout/Assets/loadout.ico with PNG images at several sizes.
# Run from the repo root in Windows PowerShell:  powershell -File tools/make-icon.ps1

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$out = Join-Path $PSScriptRoot '..\src\Loadout\Assets\loadout.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-WheelPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(0.5, $size * 0.04)
    $outer = $size / 2.0 - $pad
    $inner = $outer * 0.45
    $c = $size / 2.0
    $gapDeg = if ($size -le 20) { 10 } else { 6 }

    $slice = [System.Drawing.Color]::FromArgb(255, 0xC9, 0xCC, 0xD3)
    $hot = [System.Drawing.Color]::FromArgb(255, 0x8E, 0xC9, 0xE8)
    $edge = [System.Drawing.Color]::FromArgb(255, 0x2B, 0x2D, 0x31)

    for ($i = 0; $i -lt 8; $i++) {
        # GDI+ angles: 0 = right, clockwise. Slot 0 is centered on straight up (-90).
        $start = -90 - 22.5 + $i * 45 + $gapDeg / 2
        $sweep = 45 - $gapDeg
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($c - $outer, $c - $outer, 2 * $outer, 2 * $outer, $start, $sweep)
        $path.AddArc($c - $inner, $c - $inner, 2 * $inner, 2 * $inner, $start + $sweep, -$sweep)
        $path.CloseFigure()
        $color = if ($i -eq 0) { $hot } else { $slice }
        $brush = New-Object System.Drawing.SolidBrush $color
        $g.FillPath($brush, $path)
        if ($size -ge 32) {
            $pen = New-Object System.Drawing.Pen $edge, ([Math]::Max(1, $size / 64.0))
            $g.DrawPath($pen, $path)
            $pen.Dispose()
        }
        $brush.Dispose()
        $path.Dispose()
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($s in $sizes) { , (New-WheelPng $s) }

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0)               # reserved
$w.Write([UInt16]1)               # type: icon
$w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim)
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length)
    $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()
$fs.Dispose()
Write-Output "Wrote $out"
