# Skin work by kind of fault: each slider does something, each does its own thing, and - the point of all of
# it - taking the blemishes out does not take the pores with them, where one smoothing slider does.
# Skipped when no face is found in the photo the suite was given.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

function global:Run([string]$faces, [hashtable]$set) {
    $s = New-Object PixTune.Step 'portrait'
    $s.S['faces'] = $faces
    $s.Set('spotlight', 0); $s.Set('smoothing', 0); $s.Set('eyes', 0)
    $s.Set('spots', 0); $s.Set('redness', 0); $s.Set('wrinkles', 0); $s.Set('shine', 0); $s.Set('undereye', 0); $s.Set('keeptexture', 60)
    foreach ($k in $set.Keys) { $s.Set($k, $set[$k]) }
    [PixTune.Pipeline]::Apply($App.Doc.Preview, $s)
}
# how much fine detail is left where the skin is: the pores, the grain, the stubble
function global:Grain($im, $skin) {
    $w = $im.W; $sum = 0.0; $n = 0
    for ($y = 1; $y -lt $im.H - 1; $y += 2) {
        for ($x = 1; $x -lt $w - 1; $x += 2) {
            $i = $y * $w + $x
            if ($skin[$i] -lt 0.5) { continue }
            $c = $im.D[$i * 3]
            $d = [Math]::Abs($c - $im.D[($i + 1) * 3]) + [Math]::Abs($c - $im.D[($i + $w) * 3])
            $sum += $d; $n++
        }
    }
    if ($n -eq 0) { return -1 } else { return $sum / $n }
}

St 'skin by kind of fault' {
    $p = $App.Doc.Preview
    $faces = $App.FindFaces($p)
    if ([string]::IsNullOrEmpty($faces)) {
        $script:Trace.Add("ok   no face in this photo: skipped ($($App.FaceError))")
        return
    }
    $list = [PixTune.Face]::ParseFaces($faces)
    $maps = [PixTune.SkinDefects]::Build($p, $list, $faces)
    $skin = $maps.A.Skin
    # The 468-point mesh cannot fit a face turned far enough, and that is not a failure: the detector's own
    # five points carry the regions instead. What matters is that the regions came from somewhere real.
    $skinPixels = 0
    for ($i = 0; $i -lt $skin.Length; $i++) { if ($skin[$i] -gt 0.5) { $skinPixels++ } }
    Expect ($skinPixels -gt 1000) ("the face gave real regions to work in ({0} skin pixels, from {1})" -f $skinPixels, $(if ($maps.A.FromMesh) { 'the 468-point mesh' } else { 'the detector five points' }))
    Expect ($maps.A.CrSpread -lt 12) ("the skin colour is fitted to this face, not assumed (Cr spread {0:N1})" -f $maps.A.CrSpread)

    $plain = Run $faces @{}
    $base = Grain $plain $skin
    Expect ($base -gt 0) ("the untouched photo has grain in the skin to lose ({0:N5})" -f $base)

    # each slider has to do something, and on its own
    foreach ($one in @(@('spots', 'Spots'), @('redness', 'Redness'), @('wrinkles', 'Lines'), @('undereye', 'Under eyes'))) {
        $o = Run $faces @{ $one[0] = 100 }
        $moved = 0.0; $n = 0
        for ($i = 0; $i -lt $o.N; $i++) {
            if ($skin[$i] -lt 0.5) { continue }
            $moved += [Math]::Abs($o.D[$i * 3] - $plain.D[$i * 3]); $n++
        }
        $per = if ($n -gt 0) { $moved / $n } else { 0 }
        Expect ($per -gt 0.0005) ("{0} changes the skin on its own ({1:N5} a pixel over {2} skin pixels)" -f $one[1], $per, $n)
    }

    # Where the redness ends up. The point of the first target is that a red patch comes out the colour of
    # the cheek beside it, so it is measured against this face's own middle rather than against "less red".
    $red = $maps.Redness
    function global:RedCr($im) {
        $sum = 0.0; $n = 0
        for ($i = 0; $i -lt $im.N; $i++) {
            if ($red[$i] -lt 0.4) { continue }
            $cb = 0.0; $cr = 0.0
            [PixTune.SkinAtlas]::Chroma($im.D[$i * 3], $im.D[$i * 3 + 1], $im.D[$i * 3 + 2], [ref]$cb, [ref]$cr)
            $sum += $cr; $n++
        }
        if ($n) { return $sum / $n } else { return 0 }
    }
    $mid = $maps.A.CrMid
    $wasCr = RedCr $plain
    $ways = @()
    for ($k = 0; $k -lt 4; $k++) { $ways += (RedCr (Run $faces @{ redness = 100; rednessway = $k })) }
    Expect ($wasCr -gt $mid + 2) ("there is redness on this face to move (Cr {0:N1} against the face's {1:N1})" -f $wasCr, $mid)
    $got = ($wasCr - $ways[0]) / ($wasCr - $mid)
    Expect ($got -gt 0.6) ("a full slider brings it most of the way to this face's own colour ({0:P0}, Cr {1:N1} to {2:N1})" -f $got, $wasCr, $ways[0])
    Expect ($ways[1] -gt $ways[0] + 1) ("half way stops short of it (Cr {0:N1})" -f $ways[1])
    Expect ($ways[2] -gt $ways[0] + 1) ("warm keeps some of the red (Cr {0:N1})" -f $ways[2])
    Expect ($ways[3] -lt $ways[0] - 1) ("and even goes past it, the flattest of the four (Cr {0:N1})" -f $ways[3])

    # the point of the whole thing: the blemishes go and the grain stays, where one smoothing slider takes both
    $spots = Run $faces @{ spots = 100 }
    $smooth = Run $faces @{ smoothing = 70 }
    $gSpots = Grain $spots $skin
    $gSmooth = Grain $smooth $skin
    Expect ($gSpots -gt $base * 0.8) ("taking the spots out keeps the skin's grain ({0:N5} against {1:N5} untouched)" -f $gSpots, $base)
    Expect ($gSmooth -lt $base * 0.85) ("where the old smoothing takes it away ({0:N5}, {1:P0} of what was there)" -f $gSmooth, ($gSmooth / $base))
    Expect ($gSpots -gt $gSmooth * 1.3) "so the two are not doing the same thing"
}
Start-StRun -Log 'skin.log'
