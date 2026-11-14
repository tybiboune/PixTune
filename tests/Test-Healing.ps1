# Compares the previous single-patch healing with the new content-aware fill on real removals.
param([string]$OutDir = (Join-Path $env:TEMP 'PixTune-tests'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'lib\Engine.ps1')
Import-PixTuneEngine -Root $root
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory $OutDir | Out-Null }

function Stroke([double]$r, [double[]]$pts) {
    $k = New-Object PixTune.Stroke; $k.Ch = 'heal'; $k.V = 1; $k.R = $r
    for ($i = 0; $i -lt $pts.Count; $i += 2) { $k.Pts.Add((New-Object System.Drawing.PointF ([float]$pts[$i]), ([float]$pts[$i + 1]))) }
    $k
}
$wall = 'C:\Windows\Web\Wallpaper'
# removals: (photo, label, strokes)
$cases = @(
    @{ File = 'C:\Windows\Web\Screen\img102.jpg'; Label = 'remove the sun'; Strokes = @((Stroke 0.105 @(0.5, 0.53))) },
    @{ File = 'C:\Windows\Web\Screen\img102.jpg'; Label = 'remove the trees'; Strokes = @((Stroke 0.045 @(0.03, 0.43, 0.15, 0.5, 0.3, 0.6, 0.42, 0.66))) },
    @{ File = (Get-ChildItem $wall -Recurse -Filter img50.jpg | Select-Object -First 1).FullName; Label = 'remove the flowers'; Strokes = @((Stroke 0.05 @(0.36, 0.42, 0.38, 0.55, 0.36, 0.7))) },
    @{ File = (Get-ChildItem $wall -Recurse -Filter img50.jpg | Select-Object -First 1).FullName; Label = 'remove the tree'; Strokes = @((Stroke 0.045 @(0.62, 0.42, 0.6, 0.55, 0.6, 0.68))) },
    @{ File = 'C:\Windows\Web\Screen\img104.jpg'; Label = 'remove a fold'; Strokes = @((Stroke 0.035 @(0.35, 0.3, 0.55, 0.55))) }
)
$tw = 420; $th = 270; $rows = $cases.Count
$sheet = New-Object System.Drawing.Bitmap ($tw * 3), (($th + 22) * $rows)
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::FromArgb(28, 28, 30))
$font = New-Object System.Drawing.Font 'Segoe UI', 9
$r = 0
foreach ($c in $cases) {
    $b = [PixTune.ImageIO]::Load($c.File); $im = [PixTune.Img]::FromBitmap($b).FitInside(1600); $b.Dispose()
    $s = New-Object PixTune.Step 'healing'
    foreach ($k in $c.Strokes) { $s.Strokes.Add($k) }
    # the area to remove, outlined in red on the original
    $mask = [PixTune.Raster]::Channel($s.Strokes, 'heal', $im.W, $im.H)
    $shown = $im.Clone()
    for ($i = 0; $i -lt $mask.Length; $i++) { if ($mask[$i] -gt 0.3) { $shown.D[$i * 3] = $shown.D[$i * 3] * 0.5 + 0.5; $shown.D[$i * 3 + 1] *= 0.5; $shown.D[$i * 3 + 2] *= 0.5 } }
    $s.Set('legacy', 1)
    $sw = [Diagnostics.Stopwatch]::StartNew(); $old = [PixTune.Pipeline]::Apply($im, $s); $tOld = $sw.ElapsedMilliseconds
    $s.Set('legacy', 0)
    $sw.Restart(); $new = [PixTune.Pipeline]::Apply($im, $s); $tNew = $sw.ElapsedMilliseconds
    # zoom on the area around the hole (expanded by half its size)
    $x0 = $im.W; $y0 = $im.H; $x1 = 0; $y1 = 0
    for ($i = 0; $i -lt $mask.Length; $i += 3) { if ($mask[$i] -gt 0.3) { $x = $i % $im.W; $y = [Math]::Floor($i / $im.W); if ($x -lt $x0) { $x0 = $x }; if ($x -gt $x1) { $x1 = $x }; if ($y -lt $y0) { $y0 = $y }; if ($y -gt $y1) { $y1 = $y } } }
    $mx = ($x1 - $x0) * 0.5 + 20; $my = ($y1 - $y0) * 0.5 + 20
    $cs = New-Object PixTune.Step 'crop'
    $cs.Set('x0', [Math]::Max(0, ($x0 - $mx) / $im.W)); $cs.Set('x1', [Math]::Min(1, ($x1 + $mx) / $im.W)); $cs.Set('y0', [Math]::Max(0, ($y0 - $my) / $im.H)); $cs.Set('y1', [Math]::Min(1, ($y1 + $my) / $im.H))
    $shown = [PixTune.Pipeline]::Apply($shown, $cs); $old = [PixTune.Pipeline]::Apply($old, $cs); $new = [PixTune.Pipeline]::Apply($new, $cs)
    $col = 0
    foreach ($pair in @(@($shown, "$($c.Label): area to remove"), @($old, "previous healing ($tOld ms)"), @($new, "content-aware fill ($tNew ms)"))) {
        $bm = $pair[0].ToBitmap()
        $k2 = [Math]::Min($tw / $bm.Width, $th / $bm.Height)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.DrawImage($bm, [float]($col * $tw), [float]($r * ($th + 22)), [float]($bm.Width * $k2), [float]($bm.Height * $k2))
        $g.DrawString($pair[1], $font, [System.Drawing.Brushes]::White, [float]($col * $tw + 4), [float]($r * ($th + 22) + $th + 3))
        $bm.Dispose(); $col++
    }
    "{0,-22} previous {1,5} ms   new {2,5} ms" -f $c.Label, $tOld, $tNew
    $r++
}
$g.Dispose()
$p = Join-Path $OutDir 'healing-compare.png'; $sheet.Save($p); $sheet.Dispose()
"Sheet: $p"
