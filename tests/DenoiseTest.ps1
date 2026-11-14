# AI denoise: the denoised picture must stay a picture, and must actually remove noise. The model's numbers run
# away when what it is shown sits outside what it was trained on - a dark, flat tile, or a tile noisier than any
# it saw - and it then returns values many times brighter than white, which the clamp at the end turns into
# coloured mush. The tile's level is changed before the model sees it and put back after. This checks a
# deliberately dark picture and a deliberately noisy one. Skipped when the denoise model is not installed.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

# the photo darkened towards the bottom, which is what the model used to choke on
function global:DarkPhoto {
    $p = $App.Doc.Preview
    $w = [Math]::Min(700, $p.W); $h = [Math]::Min(500, $p.H)
    $im = $p.Region(0, 0, $w, $h)
    for ($y = 0; $y -lt $h; $y++) {
        $k = 0.75 - 0.72 * ($y / [double]$h)
        for ($x = 0; $x -lt $w; $x++) { $i = ($y * $w + $x) * 3; for ($c = 0; $c -lt 3; $c++) { $im.D[$i + $c] = [float]($im.D[$i + $c] * $k) } }
    }
    $im
}

function global:Worst($a, $b) {
    $m = 0.0
    for ($i = 0; $i -lt $a.D.Length; $i++) { $e = [Math]::Abs($a.D[$i] - $b.D[$i]); if ($e -gt $m) { $m = $e } }
    $m * 255
}

function global:Rms($a, $b) {
    $s = 0.0
    for ($i = 0; $i -lt $a.D.Length; $i++) { $e = ($a.D[$i] - $b.D[$i]) * 255; $s += $e * $e }
    [Math]::Sqrt($s / $a.D.Length)
}

# the same photo with grain of the given size in levels
function global:Grainy($im, [double]$sigma) {
    $o = New-Object PixTune.Img $im.W, $im.H
    $r = New-Object Random 7
    for ($i = 0; $i -lt $im.D.Length; $i++) {
        $u1 = 1.0 - $r.NextDouble(); $u2 = $r.NextDouble()
        $g = [Math]::Sqrt(-2.0 * [Math]::Log($u1)) * [Math]::Cos(2.0 * [Math]::PI * $u2)
        $v = $im.D[$i] + $g * $sigma / 255.0
        $o.D[$i] = [float]([Math]::Max(0.0, [Math]::Min(1.0, $v)))
    }
    $o
}

# how much of a picture sits pinned at black or white: what the broken output looked like
function global:Pinned($im) {
    $n = 0
    for ($i = 0; $i -lt $im.D.Length; $i++) { if ($im.D[$i] -le 0.0 -or $im.D[$i] -ge 1.0) { $n++ } }
    $n / [double]$im.D.Length
}

function global:Guard([bool]$on) {
    if ($on) { [PixTune.Denoiser]::Comfort = 0.9; [PixTune.Denoiser]::Retries = 3; [PixTune.Denoiser]::Sane = 1.15 }
    else { [PixTune.Denoiser]::Comfort = 0; [PixTune.Denoiser]::Retries = 0; [PixTune.Denoiser]::Sane = 1000 }
}

St 'dark' {
    if (-not [PixTune.Denoiser]::Installed) { $script:Trace.Add('ok   the denoise model is not installed: skipped'); return }
    $im = DarkPhoto
    Guard $false; [PixTune.Denoiser]::Forget()
    $loose = Pinned ([PixTune.Denoiser]::Denoise($im))
    Guard $true; [PixTune.Denoiser]::Forget()
    $o = [PixTune.Denoiser]::Denoise($im)
    Expect ($null -ne $o) "the denoiser returned a picture ($([PixTune.Denoiser]::Device)) $([PixTune.Denoiser]::LastError)"
    if ($null -eq $o) { return }
    $share = Pinned $o
    Expect ($share -lt 0.01) ("a dark picture keeps its tones: {0:P2} pinned at black or white, against {1:P2} with the model left to itself" -f $share, $loose)
    # a denoiser moves pixels a little; the broken output was a saturated mess
    $w = Worst $im $o
    Expect ($w -lt 60) "no pixel is thrown across the range (largest change $([int]$w) levels, quieted $([PixTune.Denoiser]::Quieted), refused $([PixTune.Denoiser]::Refused))"
}
St 'noisy' {
    if (-not [PixTune.Denoiser]::Installed) { $script:Trace.Add('ok   the denoise model is not installed: skipped'); return }
    $p = $App.Doc.Preview
    $clean = $p.Region(0, 0, [Math]::Min(700, $p.W), [Math]::Min(500, $p.H))
    # grain heavier than the model was trained for: this used to come back as coloured confetti
    $noisy = Grainy $clean 25
    $before = Rms $clean $noisy
    Guard $false; [PixTune.Denoiser]::Forget()
    $loose = Rms $clean ([PixTune.Denoiser]::Denoise($noisy))
    Guard $true; [PixTune.Denoiser]::Forget()
    $o = [PixTune.Denoiser]::Denoise($noisy)
    if ($null -eq $o) { Expect $false "the denoiser returned a picture ($([PixTune.Denoiser]::LastError))"; return }
    $after = Rms $clean $o
    Expect ($after -lt $before * 0.75) ("grain of 25 levels is reduced, not multiplied: {0:N1} -> {1:N1} levels from the clean photo (quieted {2}, refused {3})" -f $before, $after, [PixTune.Denoiser]::Quieted, [PixTune.Denoiser]::Refused)
    Expect ($after -lt $loose) ("and the model left to itself makes it worse, not better: {0:N1} levels" -f $loose)
    Expect ((Pinned $o) -lt 0.01) ("the result keeps its tones: {0:P2} pinned at black or white" -f (Pinned $o))
}
# A card short of memory does not refuse the work, it answers with nonsense, and every tile is then kept as it
# came in: the photo used to come back untouched with nothing said. Now the processor runs the model instead.
St 'the card gives up' {
    if (-not [PixTune.Denoiser]::Installed) { $script:Trace.Add('ok   the denoise model is not installed: skipped'); return }
    if ([PixTune.Denoiser]::Device -eq 'CPU') { $script:Trace.Add('ok   the model is already on the processor: nothing to fall back from'); return }
    $p = $App.Doc.Preview
    $clean = $p.Region(0, 0, [Math]::Min(256, $p.W), [Math]::Min(256, $p.H))
    $noisy = Grainy $clean 25
    $before = Rms $clean $noisy
    Guard $true
    # without the fallback a tile the card gives up on is kept as it came in, which is what used to happen
    [PixTune.Denoiser]::SkipCard = $true; [PixTune.Denoiser]::CpuFallback = $false; [PixTune.Denoiser]::Forget()
    $old = [PixTune.Denoiser]::Denoise($noisy)
    Expect ([PixTune.Denoiser]::Refused -gt 0 -and (Rms $clean $old) -gt $before * 0.9) `
        ("left to the card alone the photo comes back as it went in (every tile kept as it came in: {0}, {1:N1} levels of grain)" -f [PixTune.Denoiser]::Refused, (Rms $clean $old))
    # with it, the same tiles go through the model on the processor
    [PixTune.Denoiser]::CpuFallback = $true; [PixTune.Denoiser]::Forget()
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $o = [PixTune.Denoiser]::Denoise($noisy)
    $ms = $sw.ElapsedMilliseconds
    [PixTune.Denoiser]::SkipCard = $false; [PixTune.Denoiser]::Forget()
    if ($null -eq $o) { Expect $false "the processor returned a picture ($([PixTune.Denoiser]::LastError))"; return }
    Expect ([PixTune.Denoiser]::OnCpu -gt 0 -and [PixTune.Denoiser]::Refused -eq 0) `
        ("the processor takes the tiles the card gave up on ({0} of them, {1} refused, {2} ms)" -f [PixTune.Denoiser]::OnCpu, [PixTune.Denoiser]::Refused, $ms)
    $after = Rms $clean $o
    Expect ($after -lt $before * 0.75) ("and the grain is removed all the same: {0:N1} -> {1:N1} levels from the clean photo" -f $before, $after)
}
Start-StRun -Log 'denoise.log' -Finish {
    [PixTune.Denoiser]::Comfort = 0.9; [PixTune.Denoiser]::Retries = 3; [PixTune.Denoiser]::Sane = 1.15
    [PixTune.Denoiser]::SkipCard = $false; [PixTune.Denoiser]::CpuFallback = $true
}
