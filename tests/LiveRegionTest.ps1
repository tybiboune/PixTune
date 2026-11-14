# Live view: while a slider moves only what the screen shows is computed.
#  - engine: for every tool allowed to work on a part of the photo, the part computed alone (with its margin) gives
#    the same pixels as the whole photo at full resolution; with a subject mask too (found on the whole photo);
#  - window: zoomed in, a slider drag shows a patch, letting go renders the whole photo at full resolution; a tool
#    that needs the whole photo gets whole frames at preview size; at Fit, frames are screen-sized; a drag whose end
#    is never reported still ends with a full render.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

# runs on the photo the session has open (the suite's test photo); zoomed to 200% to pass the preview's detail
function global:New-LvStep([string]$id) {
    $s = New-Object PixTune.Step $id
    foreach ($k in 'strength', 'brightness', 'contrast', 'saturation', 'structure', 'sharpening', 'ambiance', 'shadows', 'highlights', 'warmth', 'glow', 'grain', 'amount', 'high', 'mid', 'low') { $s.Set($k, 30) }
    $s.Set('cx', 0.45); $s.Set('cy', 0.5)
    foreach ($ch in 'dodge', 'skin') {
        $k = New-Object PixTune.Stroke; $k.Ch = $ch; $k.V = 5; $k.R = 0.04
        $k.Pts.Add((New-Object System.Drawing.PointF 0.3, 0.4)); $k.Pts.Add((New-Object System.Drawing.PointF 0.6, 0.45)); $s.Strokes.Add($k)
    }
    $s
}

# largest and mean difference between a live frame and the same part of the full-resolution render
function global:Compare-Live($lf, $full) {
    $W = $full.W; $H = $full.H
    $x0 = [int]([Math]::Round($lf.Patch.X * $W)); $y0 = [int]([Math]::Round($lf.Patch.Y * $H))
    $im = $lf.Img; $mx = 0.0; $sum = 0.0; $n = 0
    for ($y = 0; $y -lt $im.H; $y += 11) { for ($x = 0; $x -lt $im.W; $x += 11) {
        $a = (($y0 + $y) * $W + $x0 + $x) * 3; $b = ($y * $im.W + $x) * 3
        for ($c = 0; $c -lt 3; $c++) { $e = [Math]::Abs($full.D[$a + $c] - $im.D[$b + $c]); $sum += $e; $n++; if ($e -gt $mx) { $mx = $e } } } }
    @($mx, ($sum / [Math]::Max(1, $n)))
}

St 'engine' {
    $doc = $App.Doc; $none = [Threading.CancellationToken]::None
    $base = New-Object PixTune.Step 'wb'; $base.Set('temperature', 10)
    $tools = 'tune', 'details', 'curves', 'wb', 'brush', 'glamour', 'tonal', 'drama', 'vignette', 'lensblur', 'vintage', 'grainy', 'bw', 'noir', 'film', 'bloom', 'halation', 'grain', 'color', 'face'
    $worst = 0.0
    foreach ($id in $tools) {
        $steps = New-Object 'System.Collections.Generic.List[PixTune.Step]'
        $steps.Add($base); $steps.Add((New-LvStep $id))
        Expect ([PixTune.Pipeline]::RegionSafe($steps[1])) "$id may work on a part"
        $full = $doc.RenderFullCached($steps, $none)
        $v = New-Object PixTune.LiveView; $v.Visible = New-Object System.Drawing.RectangleF 0.31, 0.27, 0.33, 0.3; $v.DisplayScale = 1; $v.Full = $true
        $lf = $doc.RenderLive($steps, 1, $v, $none)
        Expect (-not $lf.Patch.IsEmpty) "$id gives a patch"
        $d = Compare-Live $lf $full
        $worst = [Math]::Max($worst, $d[0])
        # Lens Blur blurs a reduced copy whose grid falls slightly differently on a part: under 3 levels of 255
        $tol = if ($id -eq 'lensblur') { 0.012 } else { 0.004 }
        Expect ($d[0] -lt $tol) ("{0}: same pixels as the full render (largest difference {1:N4})" -f $id, $d[0])
    }
    # with a subject mask: found on the whole photo, its part taken (edges differ slightly: preview-size mask)
    $steps = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    $t = New-LvStep 'tune'; $t.MaskOn = $true; $t.S['smartmask'] = 'subject'
    $steps.Add($base); $steps.Add($t)
    $full = $doc.RenderFullCached($steps, $none)
    $v = New-Object PixTune.LiveView; $v.Visible = New-Object System.Drawing.RectangleF 0.2, 0.2, 0.4, 0.4; $v.DisplayScale = 1; $v.Full = $true
    $lf = $doc.RenderLive($steps, 1, $v, $none)
    $d = Compare-Live $lf $full
    Expect ($d[1] -lt 0.01) ("masked tune: close to the full render (mean difference {0:N4})" -f $d[1])
    # a tool that needs the whole photo: no patch
    $steps = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    $steps.Add($base); $steps.Add((New-LvStep 'hdr'))
    $lf = $doc.RenderLive($steps, 1, $v, $none)
    Expect ($lf.Patch.IsEmpty -and $lf.Img.W -eq $doc.Preview.W) 'hdr: whole photo at preview size'
    # half-size view: computed at the size it is shown
    $steps = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    $steps.Add($base); $steps.Add((New-LvStep 'details'))
    $v.DisplayScale = 0.5
    $lf = $doc.RenderLive($steps, 1, $v, $none)
    Expect ([Math]::Abs($lf.Img.W - 0.4 * $App.Doc.Original.W * 0.5) -lt 4 -and $lf.ToOriginal -eq 0.5) "50%: the part at half size ($($lf.Img.W) px)"
    # a slow computer: the same part at a lower resolution while the slider moves
    $v.Quality = 0.5
    $lf = $doc.RenderLive($steps, 1, $v, $none)
    Expect ([Math]::Abs($lf.Img.W - 0.4 * $App.Doc.Original.W * 0.25) -lt 4) "lower live quality: the part at a quarter size ($($lf.Img.W) px)"
    # Tune without shadows, highlights or ambiance needs no margin, and still gives the same pixels
    $steps = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    $pt = New-Object PixTune.Step 'tune'; $pt.Set('brightness', 30); $pt.Set('contrast', 20); $pt.Set('saturation', 15)
    $steps.Add($base); $steps.Add($pt)
    Expect ([PixTune.Pipeline]::RegionMargin($pt, $doc.Original) -eq 0) 'Tune with only brightness, contrast, saturation: no margin'
    $v = New-Object PixTune.LiveView; $v.Visible = New-Object System.Drawing.RectangleF 0.31, 0.27, 0.33, 0.3; $v.DisplayScale = 1; $v.Full = $true
    $d = Compare-Live ($doc.RenderLive($steps, 1, $v, $none)) ($doc.RenderFullCached($steps, $none))
    Expect ($d[0] -lt 0.004) ("Tune without margin: same pixels as the full render (largest difference {0:N4})" -f $d[0])
    # zoomed in after a change: the part on screen of a 3-step stack at full quality, no whole-photo render needed
    $steps = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    $steps.Add($base); $steps.Add((New-LvStep 'tune')); $steps.Add((New-LvStep 'details'))
    $full = [PixTune.Pipeline]::ApplyAll($doc.Original, $steps)
    $z = $doc.RenderZoomedPart($steps, $v, $none)
    $d = Compare-Live $z $full
    Expect (-not $z.Patch.IsEmpty -and $d[0] -lt 0.004) ("zoomed render: the part on screen equals the whole photo's (largest difference {0:N4})" -f $d[0])
    # zoom levels: between Fit and 100% the part is computed at the nearest level at or above the screen's, from a
    # copy of the photo at that level made once (a 3600 x 2400 photo here)
    Expect ([PixTune.Document]::ZoomLevel(0.3) -eq 0.35355339 -and [PixTune.Document]::ZoomLevel(0.5) -eq 0.5 -and [PixTune.Document]::ZoomLevel(0.9) -eq 1) 'zoom levels: 30% -> 35%, 50% -> 50%, 90% -> 100%'
    $big = [PixTune.Ops]::Resize($doc.Original, 3600, 2400)
    $bd = New-Object PixTune.Document; $bd.LoadImg($big, 'big')
    $steps = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    $steps.Add((New-LvStep 'tune')); $steps.Add((New-LvStep 'grain'))
    $zv = New-Object PixTune.LiveView; $zv.Visible = [System.Drawing.RectangleF]::new([float]0.05, [float]0.05, [float]0.9, [float]0.9); $zv.DisplayScale = 0.3; $zv.Full = $true; $zv.Ahead = 0.2
    $z1 = $bd.RenderZoomedPart($steps, $zv, $none)
    Expect ($z1.ToOriginal -eq 0.35355339 -and $z1.Patch.Width -eq 1 -and [Math]::Abs($z1.Img.W - 3600 * 0.35355339) -lt 2) "at 30%, most of the photo on screen: the whole photo at the 35% level ($($z1.Img.W) px), kept"
    $sw = [Diagnostics.Stopwatch]::StartNew(); $zv.DisplayScale = 0.33; $z2 = $bd.RenderZoomedPart($steps, $zv, $none)
    Expect ($z2.Img -eq $z1.Img) "zooming again within the same level reuses it ($($sw.ElapsedMilliseconds) ms)"
    $zv.Visible = [System.Drawing.RectangleF]::new([float]0.4, [float]0.4, [float]0.2, [float]0.2); $zv.DisplayScale = 0.45
    $z3 = $bd.RenderZoomedPart($steps, $zv, $none)
    $whole = [PixTune.Pipeline]::ApplyAll($bd.Original.AreaDownscale(1800, 1200), $steps)
    $d = Compare-Live $z3 $whole
    Expect ($z3.ToOriginal -eq 0.5 -and -not ($z3.Patch.Width -eq 1) -and $d[0] -lt 0.004) ("at 45%, a small part: computed at the 50% level, the same pixels as the whole photo at 50% (largest difference {0:N4})" -f $d[0])
}
# drags stay live until the test lets go (a real drag is ended by the mouse button; here only by the test); the live
# quality is held at full so the sizes can be checked
St 'zoom' { $App.LiveEndMs = 60000; $App.LiveAdaptive = $false; $App.LiveQuality = 1; Start-Tool 'tune'; $App.Canvas.SetDisplayScale(2) }
StWait 'zoomed'
St 'drag' {
    Expect (-not $App.FullShown -and $App.Canvas.PatchCovers($App.Canvas.VisibleNorm, $App.Canvas.DisplayScale)) 'zoomed in: the preview, and the part on screen at full quality'
    $script:Lw = [Diagnostics.Stopwatch]::StartNew()
    $App.Sliders['brightness'].SetValue(35, $false)
}
StWait 'dragged'
St 'drag-check' {
    $script:Trace.Add("live frame in $($script:Lw.ElapsedMilliseconds) ms")
    Expect ($App.LiveDrag -and $App.Canvas.HasPatch) 'while dragging: a patch of what is on screen'
    $App.Sliders['brightness'].SetValue(35, $true)
}
StWait 'released'
St 'release-check' {
    Expect (-not $App.LiveDrag -and -not $App.FullShown -and $App.Canvas.PatchCovers($App.Canvas.VisibleNorm, $App.Canvas.DisplayScale)) 'let go: the part on screen at full quality (no whole-photo render)'
    # panning: the part that comes into view is computed once the view stops
    $App.Canvas.Pan(-200, -120)
    Cancel-Tool; Start-Tool 'hdr'
}
StWait 'hdr-open'
St 'hdr-drag' { $App.Sliders['strength'].SetValue(70, $false) }
StWait 'hdr-dragged'
St 'hdr-check' {
    Expect (-not $App.Canvas.HasPatch -and -not $App.FullShown -and $App.Canvas.ImageSize.Width -eq $App.Doc.Preview.W) 'hdr while dragging: the whole photo at preview size'
    # from now on the end of the drag is lost: the usual short delay applies
    $App.LiveEndMs = 300; $App.BeginLive()
}
# nobody reports the end of this drag: the timer ends it once no mouse button is held
StUntil 'hdr-timeout' { -not $App.LiveDrag } 5000
StWait 'hdr-final'
St 'hdr-final-check' { Expect (-not $App.LiveDrag -and $App.Canvas.PatchCovers($App.Canvas.VisibleNorm, $App.Canvas.DisplayScale)) 'a drag whose end is lost still ends with a full-quality render'; Cancel-Tool; $App.Canvas.Fit() }
StWait 'fit'
St 'fit-drag' { $App.LiveEndMs = 60000; Start-Tool 'details'; $App.Sliders['structure'].SetValue(40, $false) }
StWait 'fit-dragged'
St 'fit-check' {
    # the preview is already the photo's size on screen: the live frame is that size (never bigger), whole
    $w = $App.Canvas.ImageSize.Width
    Expect ($w -le $App.Doc.Preview.W -and $w -ge $App.Doc.Preview.W * 0.85 -and -not $App.Canvas.HasPatch) "at Fit: the whole photo at screen size ($w px, preview $($App.Doc.Preview.W) px)"
    $App.Sliders['structure'].SetValue(40, $true)
}
StWait 'fit-released'
St 'fit-final' { Expect ($App.Canvas.ImageSize.Width -eq $App.Doc.Preview.W) 'let go at Fit: the preview'; Cancel-Tool }
Start-StRun -Log 'live.log' -Finish { $App.LiveEndMs = 300; $App.LiveAdaptive = $true }
