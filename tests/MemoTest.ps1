# Work kept between frames: what a tool derives from its input without its sliders (luminance, edge-aware
# smoothing, wide blurs) is made once and reused while a slider moves, with the same pixels; opening a tool makes it
# in the background before the first drag; the preview is the photo's size on screen at Fit.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

St 'preview' {
    $c = $App.Canvas; $o = $App.Doc.Original; $p = $App.Doc.Preview
    $k = [Math]::Min(($c.ClientSize.Width - 32) / $o.W, ($c.ClientSize.Height - 32) / $o.H)
    $shown = [Math]::Ceiling([Math]::Max($o.W, $o.H) * $k)
    $side = [Math]::Max($p.W, $p.H)
    Expect ($side -le [Math]::Max(800, $shown) + 1 -and $side -le $App.Settings.PreviewSize) "the preview is the photo's size on screen ($side px for $shown px shown, setting $($App.Settings.PreviewSize))"
}
St 'same-pixels' {
    # every tool that keeps work: identical pixels with and without it, on the CPU and on the GPU
    $doc = New-Object PixTune.Document; $doc.LoadImg($App.Doc.Preview.Clone(), 'memo')
    $cases = @(@('tune', 'brightness', @{ shadows = 30 }), @('details', 'sharpening', @{ structure = 40 }), @('tonal', 'high', @{ mid = 30 }),
        @('drama', 'saturation', @{ strength = 60 }), @('hdr', 'strength', @{ brightness = 10 }), @('glamour', 'glow', @{ saturation = 10 }))
    foreach ($gpu in $true, $false) {
        [PixTune.Gpu]::Enabled = $gpu
        foreach ($c in $cases) {
            $res = @()
            foreach ($memo in $false, $true) {
                [PixTune.Memo]::Enabled = $memo; [PixTune.Memo]::Forget()
                $doc.ClearCaches()
                foreach ($v in 10, 25) {
                    $l = New-Object 'System.Collections.Generic.List[PixTune.Step]'
                    $a = New-Object PixTune.Step 'wb'; $a.Set('temperature', 10); $l.Add($a)
                    $s = New-Object PixTune.Step $c[0]; foreach ($k in $c[2].Keys) { $s.Set($k, $c[2][$k]) }; $s.Set($c[1], $v); $l.Add($s)
                    $img = $doc.RenderPreviewSync($l)
                }
                $res += , $img.D   # the second frame: with kept work it reuses the first one's
            }
            $mx = 0.0; for ($i = 0; $i -lt $res[0].Length; $i += 61) { $e = [Math]::Abs($res[0][$i] - $res[1][$i]); if ($e -gt $mx) { $mx = $e } }
            Expect ($mx -lt 1e-6) ("{0} on the {1}: same pixels with kept work (largest difference {2:N7})" -f $c[0], $(if ($gpu) { 'GPU' } else { 'CPU' }), $mx)
        }
    }
    [PixTune.Memo]::Enabled = $true; [PixTune.Gpu]::Enabled = $App.Settings.UseGpu
}
St 'open-tool' {
    [PixTune.Gpu]::Enabled = $false   # on the CPU, where it matters most
    Start-Tool 'tune'
}
StWait 'opened'
# the background preparation runs after the tool's first frame; it is done when the kept work stops growing
St 'prepared' { Wait-Idle; [Threading.Thread]::Sleep(1500); $script:M0 = [PixTune.Memo]::Misses; $script:H0 = [PixTune.Memo]::Hits }
St 'first-drag' { $script:Sw = [Diagnostics.Stopwatch]::StartNew(); Set-Params @{ shadows = 35 } }
StWait 'dragged'
St 'drag-check' {
    $h = [PixTune.Memo]::Hits - $script:H0; $m = [PixTune.Memo]::Misses - $script:M0
    Expect ($h -gt 0 -and $m -eq 0) "the first shadows frame found Tune's work ready ($h reused, $m made; $($script:Sw.ElapsedMilliseconds) ms)"
    Cancel-Tool
    [PixTune.Gpu]::Enabled = $App.Settings.UseGpu
}
Start-StRun -Log 'memo.log' -Finish { [PixTune.Memo]::Enabled = $true; [PixTune.Gpu]::Enabled = $App.Settings.UseGpu }
