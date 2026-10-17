# Draws the macOS app icon — the cat drop of the Windows version (tools/make-icon.ps1 there) on the macOS icon grid —
# and writes src/PcCleaner.Mac/Assets/AppIcon.icns (PNG entries 16..1024 px, 1x and 2x).
# Run with Windows PowerShell 5.1:  powershell -File tools\mac\make-icon.ps1 [-PreviewDir <folder>]
# The same 100x100 drawing is the sidebar logo in MainWindow.axaml (LogoDrop in Themes/Theme.axaml): keep them in sync.
param([string]$PreviewDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$out = Join-Path $PSScriptRoot '..\..\src\PcCleaner.Mac\Assets\AppIcon.icns'

# 100 x 100 design grid (as in the Windows version).
$tileColor = '#34D399'   # AccentColor
$inkColor = '#06261A'    # AccentTextBrush
$tileRadius = 22
$body = 'M27,18 L40,35 Q50,31 60,35 L73,18 L75,45 C80,52 78,58 78,62 A28,28 0 0 1 22,62 C22,58 20,52 25,45 Z'
$eyes = @(@{ X = 40; Y = 60 }, @{ X = 60; Y = 60 })

# macOS icon grid: the tile is 824 of 1024 px, centered; the rest is transparent margin.
$tileShare = 824.0 / 1024.0

# icns entry type -> pixel size (1x and @2x variants of the standard sizes).
$entries = [ordered]@{
    'icp4' = 16; 'icp5' = 32; 'icp6' = 64; 'ic07' = 128; 'ic08' = 256; 'ic09' = 512; 'ic10' = 1024
    'ic11' = 32; 'ic12' = 64; 'ic13' = 256; 'ic14' = 512
}

function New-Brush([string]$hex) {
    $brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex))
    $brush.Freeze()
    return $brush
}

function Get-EyeSize([double]$tilePixels) {
    # On a tiny tile the eyes blur into the drop; on a small one they are drawn a bit larger to stay visible.
    if ($tilePixels -lt 24) { return $null }
    if ($tilePixels -le 32) { return @{ Rx = 5; Ry = 7 } }
    return @{ Rx = 4; Ry = 6 }
}

function Render-Icon([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $tile = $size * $tileShare
    $offset = ($size - $tile) / 2
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform $offset, $offset))
    $scale = $tile / 100.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    $dc.DrawRoundedRectangle((New-Brush $tileColor), $null, (New-Object System.Windows.Rect 0, 0, 100, 100), $tileRadius, $tileRadius)
    $dc.DrawGeometry((New-Brush $inkColor), $null, [System.Windows.Media.Geometry]::Parse($body))
    $eye = Get-EyeSize $tile
    if ($eye) {
        foreach ($e in $eyes) {
            $dc.DrawEllipse((New-Brush $tileColor), $null, (New-Object System.Windows.Point $e.X, $e.Y), $eye.Rx, $eye.Ry)
        }
    }
    $dc.Pop()
    $dc.Pop()
    $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    return $bitmap
}

function Get-Png($bitmap) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

function Get-BigEndian([int]$value) {
    $bytes = [BitConverter]::GetBytes($value)
    [Array]::Reverse($bytes)
    return , $bytes
}

$pngs = @{}
foreach ($size in ($entries.Values | Sort-Object -Unique)) {
    $pngs[$size] = Get-Png (Render-Icon $size)
    if ($PreviewDir) {
        New-Item -ItemType Directory -Force $PreviewDir | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $PreviewDir "icon-$size.png"), $pngs[$size])
    }
}

$entryData = New-Object System.IO.MemoryStream
foreach ($type in $entries.Keys) {
    $png = $pngs[$entries[$type]]
    $typeBytes = [System.Text.Encoding]::ASCII.GetBytes($type)
    $entryData.Write($typeBytes, 0, 4)
    $length = Get-BigEndian ($png.Length + 8)
    $entryData.Write($length, 0, 4)
    $entryData.Write($png, 0, $png.Length)
}
$icns = New-Object System.IO.MemoryStream
$magic = [System.Text.Encoding]::ASCII.GetBytes('icns')
$icns.Write($magic, 0, 4)
$total = Get-BigEndian ([int]$entryData.Length + 8)
$icns.Write($total, 0, 4)
$entryData.WriteTo($icns)
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
[System.IO.File]::WriteAllBytes($out, $icns.ToArray())
"AppIcon.icns: $($icns.Length) bytes, $($entries.Count) entries"
