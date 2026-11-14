# Headless check of every tool: applies representative settings to a photo, times each tool
# and writes a labelled contact sheet.
param(
    [string]$Image = 'C:\Windows\Web\Screen\img100.jpg',
    [string]$OutDir = (Join-Path $env:TEMP 'PixTune-tests'),
    [int]$Size = 900
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'lib\Engine.ps1')
Import-PixTuneEngine -Root $root
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory $OutDir | Out-Null }

$bmp = [PixTune.ImageIO]::Load($Image)
$src = [PixTune.Img]::FromBitmap($bmp).FitInside($Size)
$bmp.Dispose()

function New-Step([string]$tool, [hashtable]$p = @{}, [hashtable]$s = @{}) {
    $st = New-Object PixTune.Step $tool
    foreach ($k in $p.Keys) { $st.P[$k] = [double]$p[$k] }
    foreach ($k in $s.Keys) { $st.S[$k] = [string]$s[$k] }
    $st
}
function New-Stroke([string]$ch, [double]$v, [double]$r, [double[]]$pts) {
    $k = New-Object PixTune.Stroke
    $k.Ch = $ch; $k.V = $v; $k.R = $r
    for ($i = 0; $i -lt $pts.Count; $i += 2) { $k.Pts.Add((New-Object System.Drawing.PointF ([float]$pts[$i]), ([float]$pts[$i + 1]))) }
    $k
}

$overlay = 'C:\Windows\Web\Screen\img102.jpg'
$cases = [ordered]@{
    'original'    = New-Step 'none'
    'tune'        = New-Step 'tune' @{ brightness = 20; contrast = 30; saturation = 25; ambiance = 30; shadows = 30; highlights = -30; warmth = 20 }
    'details'     = New-Step 'details' @{ structure = 70; sharpening = 50 }
    'curves'      = New-Step 'curves' @{} @{ curve_rgb = '0,0;0.25,0.18;0.75,0.85;1,1'; curve_b = '0,0.08;1,0.92' }
    'wb'          = New-Step 'wb' @{ temperature = 50; tint = -20 }
    'rawdev'      = New-Step 'rawdev' @{ exposure = 20; contrast = 20; highlights = -40; shadows = 40; structure = 30; saturation = 15; temperature = -20 }
    'selective'   = New-Step 'selective' @{} @{ points = '0.5,0.5,0.25,60,30,50,40|0.2,0.3,0.15,-50,0,-60,0' }
    'hdr'         = New-Step 'hdr' @{ style = 0; strength = 70 }
    'glamour'     = New-Step 'glamour' @{ style = 1; glow = 60 }
    'tonal'       = New-Step 'tonal' @{ high = 50; mid = 50; low = 50 }
    'drama'       = New-Step 'drama' @{ style = 3; strength = 60 }
    'vignette'    = New-Step 'vignette' @{ outer = -70; inner = 20; size = 40 }
    'lensblur'    = New-Step 'lensblur' @{ strength = 70; transition = 30; vignette = 30; rx = 0.25; ry = 0.18; bokeh = 1 }
    'crop'        = New-Step 'crop' @{ x0 = 0.1; y0 = 0.1; x1 = 0.8; y1 = 0.9 }
    'rotate'      = New-Step 'rotate' @{ angle = 12; flipH = 1 }
    'perspective' = New-Step 'perspective' @{ tiltv = 50; prot = 5 }
    'expand'      = New-Step 'expand' @{ left = 40; right = 40; top = 20; bottom = 20 }
    'vintage'     = New-Step 'vintage' @{ style = 1; strength = 80; vignette = 60 }
    'grainy'      = New-Step 'grainy' @{ style = 3; grain = 60; strength = 70 }
    'retrolux'    = New-Step 'retrolux' @{ style = 2; strength = 70; scratches = 60; leaks = 70; seed = 3 }
    'grunge'      = New-Step 'grunge' @{ style = 42; texture = 1; texstrength = 70 }
    'bw'          = New-Step 'bw' @{ style = 1; filter = 2; grain = 20 }
    'noir'        = New-Step 'noir' @{ style = 4; wash = 40; strength = 70; grain = 40 }
    'double'      = New-Step 'double' @{ mode = 7; opacity = 60 } @{ path = $overlay }
    'text'        = New-Step 'text' @{ style = 7; color = 3; tsize = 50; ty = 0.7 } @{ text = "PixTune`nStudio" }
    'text-inv'    = New-Step 'text' @{ style = 1; color = 0; tsize = 70; invert = 1 } @{ text = 'SUMMER' }
    'frames'      = New-Step 'frames' @{ style = 7; fwidth = 60 }
    'frames2'     = New-Step 'frames' @{ style = 10; fwidth = 60 }
    'portrait'    = New-Step 'portrait' @{ spotlight = 60; smoothing = 70; eyes = 50 } @{ faces = '0.4,0.3,0.2,0.3' }
    'headpose'    = New-Step 'headpose' @{ hrot = 60; pupil = 50; smile = 50 } @{ faces = '0.4,0.3,0.2,0.3' }
}
$b = New-Step 'brush'
$b.Strokes.Add((New-Stroke 'exposure' 10 0.08 @(0.1, 0.5, 0.5, 0.55, 0.9, 0.5)))
$b.Strokes.Add((New-Stroke 'sat' -10 0.1 @(0.5, 0.1, 0.5, 0.9)))
$cases['brush'] = $b
$h = New-Step 'healing'
$h.Strokes.Add((New-Stroke 'heal' 1 0.05 @(0.45, 0.45, 0.55, 0.5)))
$cases['healing'] = $h
$f = New-Step 'face'
$f.Strokes.Add((New-Stroke 'spot' 10 0.15 @(0.5, 0.5)))
$cases['face'] = $f
$m = New-Step 'bw' @{ style = 0 }
$m.MaskOn = $true
$m.Mask.Add((New-Stroke 'mask' 1 0.2 @(0.3, 0.3, 0.7, 0.7)))
$cases['stack-mask'] = $m

$tiles = @()
foreach ($name in $cases.Keys) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $out = [PixTune.Pipeline]::Apply($src, $cases[$name])
    $ms = $sw.ElapsedMilliseconds
    "{0,-12} {1,5} ms  {2}x{3}" -f $name, $ms, $out.W, $out.H
    $tiles += [pscustomobject]@{ Name = $name; Bmp = $out.ToBitmap(); Ms = $ms }
}

# Contact sheet
$tw = 300; $th = 200; $cols = 6
$rows = [Math]::Ceiling($tiles.Count / $cols)
$sheet = New-Object System.Drawing.Bitmap ($tw * $cols), (($th + 22) * $rows)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::FromArgb(30, 30, 30))
$g.InterpolationMode = 'HighQualityBicubic'
$font = New-Object System.Drawing.Font 'Segoe UI', 10
for ($i = 0; $i -lt $tiles.Count; $i++) {
    $t = $tiles[$i]
    $x = ($i % $cols) * $tw; $y = [Math]::Floor($i / $cols) * ($th + 22)
    $k = [Math]::Min(($tw - 6) / $t.Bmp.Width, ($th - 6) / $t.Bmp.Height)
    $w = [int]($t.Bmp.Width * $k); $hh = [int]($t.Bmp.Height * $k)
    $g.DrawImage($t.Bmp, $x + ($tw - $w) / 2, $y + ($th - $hh) / 2, $w, $hh)
    $g.DrawString(("{0} ({1} ms)" -f $t.Name, $t.Ms), $font, [System.Drawing.Brushes]::White, $x + 4, $y + $th)
    $t.Bmp.Dispose()
}
$g.Dispose()
$path = Join-Path $OutDir 'contact-sheet.png'
$sheet.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
"Sheet: $path"
