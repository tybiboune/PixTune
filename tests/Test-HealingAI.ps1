# Content-aware fill vs LaMa (AI) on the same removals, at preview size and full resolution.
param([string]$OutDir = (Join-Path $env:TEMP 'PixTune-tests'), [string]$AiDir = (Join-Path $env:LOCALAPPDATA 'PixTune\ai'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'lib\Engine.ps1')
Import-PixTuneEngine -Root $root
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory $OutDir | Out-Null }
[PixTune.Lama]::Dir = $AiDir
$sw = [Diagnostics.Stopwatch]::StartNew(); $ok = [PixTune.Lama]::Available
"AI model loaded: $ok in $($sw.ElapsedMilliseconds) ms on $([PixTune.Lama]::Device) $([PixTune.Lama]::LastError)"

function Stroke([double]$r, [double[]]$pts, [int]$v) {
    $k = New-Object PixTune.Stroke; $k.Ch = 'heal'; $k.V = $v; $k.R = $r
    for ($i = 0; $i -lt $pts.Count; $i += 2) { $k.Pts.Add((New-Object System.Drawing.PointF ([float]$pts[$i]), ([float]$pts[$i + 1]))) }
    $k
}
$b = [PixTune.ImageIO]::Load('C:\Windows\Web\Screen\img102.jpg'); $full = [PixTune.Img]::FromBitmap($b); $b.Dispose()
$cases = @(
    @{ Label = 'sun'; R = 0.13; Pts = @(0.5, 0.53); View = @(0.35, 0.3, 0.65, 0.75) },
    @{ Label = 'trees'; R = 0.045; Pts = @(0.03, 0.43, 0.15, 0.5, 0.3, 0.6, 0.42, 0.66); View = @(0.0, 0.33, 0.5, 0.72) }
)
$tiles = New-Object System.Collections.ArrayList
foreach ($c in $cases) {
    foreach ($res in 'preview', 'full') {
        $im = if ($res -eq 'full') { $full } else { $full.FitInside(1600) }
        $row = @()
        foreach ($v in 0, 1, 2) {
            if ($v -eq 0) { $out = $im; $label = 'original' }
            else {
                $s = New-Object PixTune.Step 'healing'; $s.Strokes.Add((Stroke $c.R $c.Pts $v))
                $sw.Restart(); $out = [PixTune.Pipeline]::Apply($im, $s)
                $label = "{0} {1:N1} s" -f $(if ($v -eq 2) { 'AI (LaMa)' } else { 'content-aware' }), $sw.Elapsed.TotalSeconds
                "{0,-6} {1,-8} {2}" -f $c.Label, $res, $label
            }
            $cs = New-Object PixTune.Step 'crop'; $cs.Set('x0', $c.View[0]); $cs.Set('y0', $c.View[1]); $cs.Set('x1', $c.View[2]); $cs.Set('y1', $c.View[3])
            $row += , @(([PixTune.Pipeline]::Apply($out, $cs).FitInside(420).ToBitmap()), "$($c.Label) $res - $label")
        }
        [void]$tiles.Add($row)
    }
}
$th = 300
$sheet = New-Object System.Drawing.Bitmap (420 * 3), (($th + 20) * $tiles.Count)
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::FromArgb(28, 28, 30))
$font = New-Object System.Drawing.Font 'Segoe UI', 9
for ($r = 0; $r -lt $tiles.Count; $r++) {
    for ($k = 0; $k -lt 3; $k++) {
        $t = $tiles[$r][$k]
        $g.DrawImage($t[0], [float]($k * 420), [float]($r * ($th + 20)))
        $g.DrawString($t[1], $font, [System.Drawing.Brushes]::White, [float]($k * 420 + 4), [float]($r * ($th + 20) + $th + 2))
    }
}
$p = Join-Path $OutDir 'healing-ai-compare.png'; $sheet.Save($p)
"Sheet: $p"
