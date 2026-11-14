# Lens Blur's depth mode: the photo's own depth decides what is sharp, the focus point says which depth that is,
# and the lens shape has to show up in the highlights. Skipped when the depth model is not installed.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

function global:Blur([double]$cx, [double]$cy, [int]$bokeh, [int]$range) {
    $s = New-Object PixTune.Step 'lensblur'
    $s.Set('shape', 2); $s.Set('strength', 70); $s.Set('depthrange', $range); $s.Set('transition', 25); $s.Set('vignette', 0)
    $s.Set('bokeh', $bokeh); $s.Set('cx', $cx); $s.Set('cy', $cy)
    [PixTune.Pipeline]::Apply($App.Doc.Preview, $s)
}
# How far a render has moved from the photo, over the pixels the map calls near, and over the ones it calls
# far. Measured over whole regions rather than a window: the nearest point of a photo can be a blown-out
# shirt with no detail to lose, which says nothing about whether it was blurred.
function global:Moved($im, $orig, $map, $mw, $mh, [bool]$near) {
    $w = $im.W; $h = $im.H; $sum = 0.0; $n = 0
    $sx = ($mw - 1) / [double]($w - 1); $sy = ($mh - 1) / [double]($h - 1)
    for ($y = 0; $y -lt $h; $y += 3) {
        $my = [int]($y * $sy)
        for ($x = 0; $x -lt $w; $x += 3) {
            $d = $map[$my * $mw + [int]($x * $sx)]
            if ($near -and $d -lt 0.85) { continue }
            if (-not $near -and $d -gt 0.25) { continue }
            $j = ($y * $w + $x) * 3
            $sum += [Math]::Abs($im.D[$j] - $orig.D[$j]); $n++
        }
    }
    if ($n -eq 0) { return -1 } else { return $sum / $n }
}

St 'the depth map' {
    if (-not [PixTune.Depther]::Installed) { $script:Trace.Add('ok   the depth model is not installed: skipped'); $script:StSteps.Clear(); return }
    $mw = 0; $mh = 0
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $m = [PixTune.Depther]::Map($App.Doc.Preview, [ref]$mw, [ref]$mh)
    $ms = $sw.ElapsedMilliseconds
    Expect ($null -ne $m) ("the depth of the photo was worked out ({0}, {1} ms) {2}" -f [PixTune.Depther]::Device, $ms, [PixTune.Depther]::LastError)
    if ($null -eq $m) { $script:StSteps.Clear(); return }
    Expect ($mw -ge 500 -and $mh -ge 500) ("it is worked out at a useful size ({0} x {1}, from a {2} x {3} preview)" -f $mw, $mh, $App.Doc.Preview.W, $App.Doc.Preview.H)
    # a photo with a subject in front of a background has near and far in it, not one flat distance
    $mn = 1e9; $mx = -1e9
    foreach ($v in $m) { if ($v -lt $mn) { $mn = $v }; if ($v -gt $mx) { $mx = $v } }
    Expect (($mx - $mn) -gt 0.5) ("it separates near from far ({0:N2} to {1:N2})" -f $mn, $mx)
    $sw.Restart(); $null = [PixTune.Depther]::Map($App.Doc.Preview, [ref]$mw, [ref]$mh)
    Expect ($sw.ElapsedMilliseconds -lt 200) ("and it is kept, so asking again costs nothing ({0} ms)" -f $sw.ElapsedMilliseconds)
}
St 'the focus point decides what is sharp' {
    if (-not [PixTune.Depther]::Installed) { return }
    $p = $App.Doc.Preview
    $mw = 0; $mh = 0
    $m = [PixTune.Depther]::Map($p, [ref]$mw, [ref]$mh)
    # the nearest place in the photo and the farthest, found from the map itself rather than guessed
    $nearV = -1; $farV = 2; $nx = 0.5; $ny = 0.5; $fx = 0.5; $fy = 0.5
    for ($y = 4; $y -lt $mh - 4; $y += 16) {
        for ($x = 4; $x -lt $mw - 4; $x += 16) {
            $v = $m[$y * $mw + $x]
            if ($v -gt $nearV) { $nearV = $v; $nx = $x / [double]$mw; $ny = $y / [double]$mh }
            if ($v -lt $farV) { $farV = $v; $fx = $x / [double]$mw; $fy = $y / [double]$mh }
        }
    }
    $script:Trace.Add(("   nearest {0:N2} at {1:N2},{2:N2}; farthest {3:N2} at {4:N2},{5:N2}" -f $nearV, $nx, $ny, $farV, $fx, $fy))
    # a roomy depth range: this is about the focus following the photo's depth, not about how thin a slice
    # a narrow range cuts - with a thin one, most of what counts as near is correctly blurred anyway
    $onNear = Blur $nx $ny 0 60
    $onFar = Blur $fx $fy 0 60
    $nearMovedWhenFocused = Moved $onNear $p $m $mw $mh $true
    $nearMovedWhenNot = Moved $onFar $p $m $mw $mh $true
    $farMovedWhenFocused = Moved $onFar $p $m $mw $mh $false
    $farMovedWhenNot = Moved $onNear $p $m $mw $mh $false
    Expect ($nearMovedWhenFocused -lt $nearMovedWhenNot * 0.6) `
        ("what is near stays put when the focus is on it and is blurred when it is not ({0:N4} against {1:N4})" -f $nearMovedWhenFocused, $nearMovedWhenNot)
    Expect ($farMovedWhenFocused -lt $farMovedWhenNot * 0.6) `
        ("and the same the other way round, for what is far ({0:N4} against {1:N4})" -f $farMovedWhenFocused, $farMovedWhenNot)
}
St 'moving the focus is immediate' {
    if (-not [PixTune.Depther]::Installed) { return }
    $null = Blur 0.5 0.5 0 20
    $sw = [Diagnostics.Stopwatch]::StartNew()
    foreach ($k in 1..5) { $null = Blur (0.3 + $k * 0.06) 0.5 0 20 }
    $each = $sw.ElapsedMilliseconds / 5
    # the depth is known and the blurred copy is kept: all that is left is the mixing
    Expect ($each -lt 150) ("dragging the focus costs {0:N0} ms a frame" -f $each)
}
St 'the lens shape reaches the highlights' {
    # one bright point on a dark field, well out of focus: what comes back is the shape of the lens
    $w = 300; $h = 300
    $im = New-Object PixTune.Img $w, $h
    for ($i = 0; $i -lt $im.D.Length; $i++) { $im.D[$i] = 0.02 }
    $j = (150 * $w + 150) * 3; $im.D[$j] = 60.0; $im.D[$j + 1] = 54.0; $im.D[$j + 2] = 44.0
    $areas = @()
    foreach ($shape in 0, 3, 6) {
        $s = New-Object PixTune.Step 'lensblur'
        $s.Set('shape', 0); $s.Set('strength', 80); $s.Set('transition', 0); $s.Set('vignette', 0)
        $s.Set('bokeh', $shape); $s.Set('cx', 0.02); $s.Set('cy', 0.02); $s.Set('rx', 0.01); $s.Set('ry', 0.01)
        $o = [PixTune.Pipeline]::Apply($im, $s)
        $lit = 0
        for ($i = 0; $i -lt $w * $h; $i++) { if ($o.D[$i * 3] -gt 0.5) { $lit++ } }
        $areas += $lit
    }
    # a circle fills its outline, a triangle covers about half of it, a thin ring far less: if the shape were
    # ignored these would all be the same
    Expect ($areas[0] -gt 0) ("a bright point out of focus becomes a bokeh ({0} pixels for the circle)" -f $areas[0])
    Expect ($areas[1] -lt $areas[0] * 0.75) ("the triangle covers less than the circle ({0} against {1})" -f $areas[1], $areas[0])
    Expect ($areas[2] -lt $areas[0] * 0.6) ("and the ring less again, being a ring ({0} against {1})" -f $areas[2], $areas[0])
}
St 'threshold changes the picture' {
    $w = 300; $h = 300
    $im = New-Object PixTune.Img $w, $h
    # a gradient from dim to bright, so a threshold has something to choose between
    for ($y = 0; $y -lt $h; $y++) { for ($x = 0; $x -lt $w; $x++) {
        $v = 0.25 + 0.8 * ($x / [double]$w)
        $j = ($y * $w + $x) * 3; $im.D[$j] = $v; $im.D[$j+1] = $v * 0.95; $im.D[$j+2] = $v * 0.9 } }
    for ($k = 0; $k -lt 6; $k++) { $j = ((40 + $k * 40) * $w + (30 + $k * 45)) * 3; $im.D[$j] = 30; $im.D[$j+1] = 27; $im.D[$j+2] = 22 }
    $prev = $null
    foreach ($t in 0, 40, 100) {
        $s = New-Object PixTune.Step 'lensblur'
        $s.Set('shape', 0); $s.Set('strength', 70); $s.Set('transition', 0); $s.Set('vignette', 0)
        $s.Set('bokeh', 0); $s.Set('bokehthreshold', $t); $s.Set('cx', 0.02); $s.Set('cy', 0.02); $s.Set('rx', 0.01); $s.Set('ry', 0.01)
        $o = [PixTune.Pipeline]::Apply($im, $s)
        $sum = 0.0; for ($i = 0; $i -lt $o.D.Length; $i += 7) { $sum += $o.D[$i] }
        if ($null -ne $prev) {
            $diff = 0.0; for ($i = 0; $i -lt $o.D.Length; $i += 7) { $diff += [Math]::Abs($o.D[$i] - $prev[$i]) }
            $per = $diff / [Math]::Ceiling($o.D.Length / 7.0)
            # the blurred copy is kept between frames, and what it is kept under has to mention everything that
            # changes it: the threshold was missing from that, so moving the slider did nothing at all
            Expect ($per -gt 0.002) ("moving the bokeh threshold to {0} changes the picture ({1:N5} a pixel)" -f $t, $per)
        }
        $prev = $o.D.Clone()
    }
}
Start-StRun -Log 'depth.log'
