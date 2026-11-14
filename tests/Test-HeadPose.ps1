# Head Pose on portraits: each slider at its maximum, shown on a close crop of the face, plus timings.
param([string]$Folder, [string]$OutDir = (Join-Path $env:TEMP 'PixTune-tests\headpose'), [int]$Size = 1400, [switch]$Ai)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'lib\Engine.ps1')
Import-PixTuneEngine -Root $root
New-Item -ItemType Directory $OutDir -Force | Out-Null

# Windows face detector, as PixTune uses it
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.FaceAnalysis.FaceDetector, Windows.Media.FaceAnalysis, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Wait-Op($op, [type]$t) { $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); [void]$task.Wait(-1); $task.Result }
function Find-Faces($im) {
    $bmp = $im.ToBitmap(); $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $ms.Position = 0
    $ras = [System.IO.WindowsRuntimeStreamExtensions]::AsRandomAccessStream($ms)
    $dec = Wait-Op ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($ras)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $sb = Wait-Op ($dec.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $gray = [Windows.Graphics.Imaging.SoftwareBitmap]::Convert($sb, [Windows.Graphics.Imaging.BitmapPixelFormat]::Gray8)
    $det = Wait-Op ([Windows.Media.FaceAnalysis.FaceDetector]::CreateAsync()) ([Windows.Media.FaceAnalysis.FaceDetector])
    $faces = Wait-Op ($det.DetectFacesAsync($gray)) ([System.Collections.Generic.IList[Windows.Media.FaceAnalysis.DetectedFace]])
    $inv = [Globalization.CultureInfo]::InvariantCulture
    (@($faces | ForEach-Object { $b = $_.FaceBox; '{0},{1},{2},{3}' -f ($b.X / $im.W).ToString($inv), ($b.Y / $im.H).ToString($inv), ($b.Width / $im.W).ToString($inv), ($b.Height / $im.H).ToString($inv) }) -join ';')
}

"face mesh: $([PixTune.FaceMesh]::Available) $([PixTune.FaceMesh]::LastError)"
if ($Ai) { $sw0 = [Diagnostics.Stopwatch]::StartNew(); "LivePortrait: $([PixTune.LivePortrait]::Available) $([PixTune.LivePortrait]::Device) $([PixTune.LivePortrait]::LastError) (load $($sw0.ElapsedMilliseconds) ms)" }
$sets = @(@('turn left', @{ hrot = -100 }), @('turn right', @{ hrot = 100 }), @('look up', @{ vrot = 100 }), @('look down', @{ vrot = -100 }),
    @('smile', @{ smile = 100 }), @('pupils', @{ pupil = 100 }), @('focal', @{ focal = 100 }))
$font = New-Object System.Drawing.Font 'Segoe UI', 10
foreach ($f in Get-ChildItem $Folder -Filter *.jpg) {
    $b = [PixTune.ImageIO]::Load($f.FullName); $im = [PixTune.Img]::FromBitmap($b).FitInside($Size); $b.Dispose()
    $faces = Find-Faces $im
    if (-not $faces) { "$($f.BaseName): no face"; continue }
    $box = [PixTune.Face]::ParseFaces($faces)[0]
    $cx = ($box[0] + $box[2] / 2) * $im.W; $cy = ($box[1] + $box[3] / 2) * $im.H; $half = [Math]::Max($box[2] * $im.W, $box[3] * $im.H) * 0.85
    $x0 = [int][Math]::Max(0, $cx - $half); $y0 = [int][Math]::Max(0, $cy - $half); $x1 = [int][Math]::Min($im.W, $cx + $half); $y1 = [int][Math]::Min($im.H, $cy + $half)
    $rect = New-Object System.Drawing.Rectangle $x0, $y0, ($x1 - $x0), ($y1 - $y0)
    $tiles = New-Object System.Collections.ArrayList
    $full = $im.ToBitmap(); [void]$tiles.Add(@('original', $full.Clone($rect, $full.PixelFormat))); $full.Dispose()
    $times = @()
    foreach ($s in $sets) {
        $st = New-Object PixTune.Step 'headpose'; $st.SetS('faces', $faces); if ($Ai) { $st.Set('ai', 1) }
        foreach ($k in $s[1].Keys) { $st.Set($k, $s[1][$k]) }
        $sw = [Diagnostics.Stopwatch]::StartNew(); $o = [PixTune.Pipeline]::Apply($im, $st); $times += "$($s[0]) $($sw.ElapsedMilliseconds) ms"
        $ob = $o.ToBitmap(); [void]$tiles.Add(@($s[0], $ob.Clone($rect, $ob.PixelFormat))); $ob.Dispose()
    }
    "$($f.BaseName): $($times -join ', ')"
    $tw = 360; $th = [int]($tw * $rect.Height / $rect.Width); $cols = 4; $rows = [Math]::Ceiling($tiles.Count / $cols)
    $sheet = New-Object System.Drawing.Bitmap ($tw * $cols), (($th + 20) * $rows)
    $g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::Black); $g.InterpolationMode = 'HighQualityBicubic'
    for ($i = 0; $i -lt $tiles.Count; $i++) {
        $xx = ($i % $cols) * $tw; $yy = [Math]::Floor($i / $cols) * ($th + 20)
        $g.DrawImage($tiles[$i][1], $xx, $yy, $tw, $th); $g.DrawString($tiles[$i][0], $font, [System.Drawing.Brushes]::White, $xx + 3, $yy + $th + 2)
    }
    $sheet.Save((Join-Path $OutDir "$($f.BaseName)$(if ($Ai) { '-ai' }).png")); $g.Dispose(); $sheet.Dispose()
}
