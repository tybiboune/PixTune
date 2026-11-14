# Expand fills compared on the same photos: content-aware (Smart) and AI (LaMa, when installed).
# -Generative adds Stable Diffusion's generative fill (slow, left out by default).
param([string]$OutDir = (Join-Path $env:TEMP 'PixTune-tests'), [string]$AiDir = (Join-Path $env:LOCALAPPDATA 'PixTune\ai'), [int]$Size = 1600, [int[]]$Only = @(), [switch]$Generative)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'lib\Engine.ps1')
Import-PixTuneEngine -Root $root
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory $OutDir | Out-Null }
[PixTune.Lama]::Dir = $AiDir
[PixTune.Diffusion]::RuntimePath = [PixTune.Lama]::RuntimePath; [PixTune.Diffusion]::Dir = Join-Path $AiDir 'sd15-inpaint'
$null = [PixTune.Gpu]::Available
$modes = @(@{ Fill = 0; Label = 'Smart' })
if ([PixTune.Lama]::Installed) { $modes += @{ Fill = 3; Label = 'AI (LaMa)' } }
if ($Generative -and [PixTune.Diffusion]::Installed) {
    $sw0 = [Diagnostics.Stopwatch]::StartNew(); $ok = [PixTune.Diffusion]::Available
    "Generative model loaded: $ok in $($sw0.ElapsedMilliseconds) ms on $([PixTune.Diffusion]::Device) $([PixTune.Diffusion]::LastError)"
    $modes += @{ Fill = 4; Label = 'Generative' }
}
$cases = @(
    @{ Photo = 'C:\Windows\Web\Screen\img102.jpg'; P = @{ left = 25 } },
    @{ Photo = 'C:\Windows\Web\Screen\img103.png'; P = @{ top = 20 } },
    @{ Photo = 'C:\Windows\Web\Screen\img101.jpg'; P = @{ right = 25 } }
)
if ($Only.Count) { $modes = @($modes | Where-Object { $Only -contains $_.Fill }) }
$rows = New-Object System.Collections.ArrayList
$sw = New-Object Diagnostics.Stopwatch
foreach ($c in $cases) {
    if (-not (Test-Path $c.Photo)) { continue }
    $b = [PixTune.ImageIO]::Load($c.Photo); $im = [PixTune.Img]::FromBitmap($b).FitInside($Size); $b.Dispose()
    $row = @()
    foreach ($m in $modes) {
        $s = New-Object PixTune.Step 'expand'
        foreach ($k in $c.P.Keys) { $s.Set($k, $c.P[$k]) }
        $s.Set('fill', $m.Fill)
        $sw.Restart(); $o = [PixTune.Pipeline]::Apply($im, $s)
        $label = "{0} {1} {2:N1} s" -f [IO.Path]::GetFileName($c.Photo), $m.Label, $sw.Elapsed.TotalSeconds; $label
        $o.ToBitmap().Save((Join-Path $OutDir ("expand-{0}-fill{1}.png" -f [IO.Path]::GetFileNameWithoutExtension($c.Photo), $m.Fill)))
        $row += , @($o.FitInside(640).ToBitmap(), $label)
    }
    [void]$rows.Add($row)
}
$cw = 660; $rh = 0; foreach ($r in $rows) { $rh = [Math]::Max($rh, $r[0][0].Height) }
$sheet = New-Object System.Drawing.Bitmap ($cw * $modes.Count), (($rh + 22) * $rows.Count)
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::FromArgb(28, 28, 30))
$font = New-Object System.Drawing.Font 'Segoe UI', 9
for ($i = 0; $i -lt $rows.Count; $i++) {
    for ($k = 0; $k -lt $rows[$i].Count; $k++) {
        $g.DrawImage($rows[$i][$k][0], [float]($k * $cw), [float]($i * ($rh + 22)))
        $g.DrawString($rows[$i][$k][1], $font, [System.Drawing.Brushes]::White, [float]($k * $cw + 4), [float]($i * ($rh + 22) + $rh + 3))
    }
}
$p = Join-Path $OutDir 'expand-compare.png'; $sheet.Save($p)
"Sheet: $p"
