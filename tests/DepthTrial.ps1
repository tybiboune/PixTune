# Three exports of Depth Anything V2 - Small, Base, Large - on the same photo, at the same input size.
# Saves each map as a picture and prints what each one costs, so the choice is made on what they return.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue

function global:SaveMap($m, $mw, $mh, $name) {
    $b = New-Object System.Drawing.Bitmap $mw, $mh
    $r = New-Object System.Drawing.Rectangle 0, 0, $mw, $mh
    $dat = $b.LockBits($r, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $stride = $dat.Stride
    $row = New-Object byte[] $stride
    for ($y = 0; $y -lt $mh; $y++) {
        for ($x = 0; $x -lt $mw; $x++) {
            $v = [byte][Math]::Max(0, [Math]::Min(255, [int]($m[$y * $mw + $x] * 255)))
            $row[$x * 3] = $v; $row[$x * 3 + 1] = $v; $row[$x * 3 + 2] = $v
        }
        [System.Runtime.InteropServices.Marshal]::Copy($row, 0, [IntPtr]($dat.Scan0.ToInt64() + $y * $stride), $stride)
    }
    $b.UnlockBits($dat)
    $b.Save((Join-Path $script:StOut "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}

St 'three sizes of the same model' {
    $p = $App.Doc.Preview
    $faces = $App.FindFaces($p)
    $list = [PixTune.Face]::ParseFaces($faces)
    if ($list.Count -eq 0) { $script:Trace.Add('no face in this photo: nothing to measure the modelling on'); return }
    $box = $list[0]
    $skin = [PixTune.SkinDefects]::Build($p, $list, $faces).A.Skin
    $script:Trace.Add(("photo {0}x{1}, the model is given {2} across, the card holds {3:N0} MB" -f $p.W, $p.H, [PixTune.Depther]::Side, ([PixTune.Gpu]::VideoMemory / 1MB)))
    $models = @(@('small', 'depth_anything_v2_small_fp16.onnx'), @('base', 'trial\base_fp16.onnx'), @('large', 'trial\large_fp16.onnx'))
    foreach ($m in $models) {
        [PixTune.Depther]::ModelFile = $m[1]
        [PixTune.Depther]::Reopen()
        if (-not [PixTune.Depther]::Available) { $script:Trace.Add(("{0,-6} will not open: {1}" -f $m[0], [PixTune.Depther]::LastError)); continue }
        $mw = 0; $mh = 0
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $map = [PixTune.Depther]::Map($p, [ref]$mw, [ref]$mh)
        $sw.Stop()
        if ($null -eq $map) { $script:Trace.Add(("{0,-6} gave nothing back: {1}" -f $m[0], [PixTune.Depther]::LastError)); continue }
        # Measured on the skin alone, found from the photo rather than from the depth, so the measure cannot
        # move with the map it is judging - and inside the skin there is no background to widen it. A face
        # modelled by the map has the nose ahead of the ear; a cut-out has one distance for all of it. Given
        # against the gap from the face to what is behind it, so three maps on different ranges compare.
        $skinD = New-Object System.Collections.Generic.List[double]
        for ($y = 0; $y -lt $p.H; $y += 2) {
            for ($x = 0; $x -lt $p.W; $x += 2) {
                if ($skin[$y * $p.W + $x] -lt 0.6) { continue }
                $skinD.Add([double]$map[[int]($y * $mh / $p.H) * $mw + [int]($x * $mw / $p.W)])
            }
        }
        $back = New-Object System.Collections.Generic.List[double]
        for ($y = 0; $y -lt $mh; $y += 2) { for ($x = 0; $x -lt [int]($mw * 0.12); $x += 2) { $back.Add([double]$map[$y * $mw + $x]) } }
        $fs = ($skinD | Sort-Object); $bs = ($back | Sort-Object)
        $gap = $fs[[int]($fs.Count * 0.5)] - $bs[[int]($bs.Count * 0.5)]
        $range = $fs[[int]($fs.Count * 0.95)] - $fs[[int]($fs.Count * 0.05)]
        $bRange = $bs[[int]($bs.Count * 0.95)] - $bs[[int]($bs.Count * 0.05)]
        $script:Trace.Add(("{0,-6} {1,6:N0} ms  on {2}" -f $m[0], $sw.Elapsed.TotalMilliseconds, [PixTune.Depther]::Device))
        # The range alone cannot tell a smooth white blob from a modelled face: both can span the same
        # distance. What a modelled face has is structure at the size of a nose and an eye socket, so the map
        # is also compared with a blurred copy of itself and the difference taken over the skin.
        $soft = [PixTune.Ops]::Gauss($map, $mw, $mh, [float]($mw / 40))
        $fine = 0.0; $nf = 0
        for ($y = 0; $y -lt $p.H; $y += 2) {
            for ($x = 0; $x -lt $p.W; $x += 2) {
                if ($skin[$y * $p.W + $x] -lt 0.6) { continue }
                $j = [int]($y * $mh / $p.H) * $mw + [int]($x * $mw / $p.W)
                $fine += [Math]::Abs($map[$j] - $soft[$j]); $nf++
            }
        }
        $script:Trace.Add(("       the face is modelled over {0:P1} of the face-to-background gap ({1} skin spots), the far strip over {2:P1}" -f ($range / $gap), $fs.Count, ($bRange / $gap)))
        $script:Trace.Add(("       with nose-and-socket structure worth {0:P2} of that gap" -f ($fine / $nf / $gap)))
        SaveMap $map $mw $mh ("depth-" + $m[0])
    }
    [PixTune.Depther]::ModelFile = 'depth_anything_v2_small_fp16.onnx'
    [PixTune.Depther]::Reopen()
}
Start-StRun -Log 'trial.log'
