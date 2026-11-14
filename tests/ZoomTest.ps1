# Checks the 100% zoom: full resolution is shown and screen pixels map 1:1 to photo pixels.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
$script:Click = { param($ctrl) $ctrl.GetType().GetMethod('OnClick', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($ctrl, @([EventArgs]::Empty)) | Out-Null }

# Compares a 41x41 block of screen pixels around the canvas centre with the full resolution render.
$script:Check1to1 = {
    param([string]$label)
    $c = $App.Canvas
    $shot = New-Object System.Drawing.Bitmap $c.Width, $c.Height
    $c.DrawToBitmap($shot, (New-Object System.Drawing.Rectangle 0, 0, $c.Width, $c.Height))
    $full = $App.Doc.RenderFullCached($App.Doc.EffectiveSteps($true), [Threading.CancellationToken]::None).ToBitmap()
    $cx = [int]($c.Width / 2); $cy = [int]($c.Height / 2)
    $n = $c.ToNorm((New-Object System.Drawing.Point $cx, $cy))
    # nearest photo pixel: the view's size in photo pixels can be a hair off a whole number (the preview under it
    # is smaller than the photo), and rounding down would then land one pixel short
    $ix = [int][Math]::Round($n.X * $full.Width); $iy = [int][Math]::Round($n.Y * $full.Height)
    $bad = 0; $tot = 0
    for ($dy = -20; $dy -le 20; $dy++) { for ($dx = -20; $dx -le 20; $dx++) {
            $a = $shot.GetPixel($cx + $dx, $cy + $dy); $b = $full.GetPixel($ix + $dx, $iy + $dy)
            $tot++; if ([Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B) -gt 3) { $bad++ }
        } }
    $script:Trace.Add(("{0}: scale={1:N4} shownBitmap={2}x{3} full={4}x{5} sourceW={6} fullShown={7} mismatched={8}/{9}" -f $label, $c.DisplayScale, $c.ImageSize.Width, $c.ImageSize.Height, $full.Width, $full.Height, $c.SourceWidth, $App.FullShown, $bad, $tot))
    $shot.Dispose(); $full.Dispose()
}

St 'fit' { $script:Trace.Add("start: fit=$($App.Canvas.IsFit) bitmap=$($App.Canvas.ImageSize.Width)") }
St 'click-100' { & $script:Click $btn100 }
St 'check-100' { & $script:Check1to1 'at 100%' ; $script:Trace.Add("100% button highlighted: $($btn100.Toggled)") }
St 'tune' { Start-Tool 'tune'; Set-Params @{ brightness = 40; contrast = 30 } }
St 'check-tune' { & $script:Check1to1 'editing Tune at 100%' }
St 'apply' { Apply-Tool }
St 'crop' { Start-Tool 'crop'; $App.Canvas.CropRect = New-Object System.Drawing.RectangleF 0.25, 0.25, 0.5, 0.5; Apply-Tool }
St 'crop-100' { $App.Canvas.ZoomActual() }
St 'check-crop' { & $script:Check1to1 'after crop at 100%' }
St 'click-fit' { & $script:Click $btnFit }
St 'check-fit' { $script:Trace.Add("after Fit: fit=$($App.Canvas.IsFit) fullShown=$($App.FullShown) bitmap=$($App.Canvas.ImageSize.Width) fitHighlighted=$($btnFit.Toggled)") }

Start-StRun -Log 'zoomtest.log' -Snap
