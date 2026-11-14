# Tools on the graphics card: every tool with a GPU version gives the same pixels as its CPU code, on the whole photo
# and on a part of it (live view), the simple tools included; a stack whose pictures stay on the card gives the same
# frame (pixels and bitmap) as the CPU. A shader that cannot be compiled makes that tool run on the CPU without
# turning the GPU off. Times of both are logged.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

function global:New-GtStep([string]$id, [int]$style) {
    $s = New-Object PixTune.Step $id
    foreach ($k in 'strength', 'brightness', 'contrast', 'saturation', 'structure', 'sharpening', 'ambiance', 'shadows', 'highlights', 'warmth', 'glow', 'grain', 'amount', 'high', 'mid', 'low', 'temperature', 'tint', 'blur', 'wash', 'halation', 'vignette', 'texstrength', 'leaks', 'scratches') { $s.Set($k, 30) }
    $s.Set('style', $style); $s.Set('cx', 0.4); $s.Set('cy', 0.55)
    if ($id -eq 'grunge') { $s.Set('texture', $style) }   # grunge's five textures are its own setting
    $s.S['curve_rgb'] = '0,0;0.3,0.4;1,1'; $s.S['curve_lum'] = '0,0;0.6,0.5;1,1'
    foreach ($b in 'red', 'orange', 'yellow', 'green', 'aqua', 'blue', 'purple', 'magenta') { $s.Set("h_$b", 20); $s.Set("s_$b", 30); $s.Set("l_$b", -10) }
    $s
}
function global:Test-GpuTool($im, [string]$id, [int]$style, [string]$where) {
    $s = New-GtStep $id $style
    [PixTune.Gpu]::Enabled = $false
    $sw = [Diagnostics.Stopwatch]::StartNew(); $cpu = [PixTune.Pipeline]::Apply($im, $s); $tc = $sw.ElapsedMilliseconds
    [PixTune.Gpu]::Enabled = $true; [PixTune.Gpu]::LastError = ''
    $sw.Restart(); $gpu = [PixTune.Pipeline]::Apply($im, $s); $tg = $sw.ElapsedMilliseconds
    $mx = 0.0
    for ($i = 0; $i -lt $cpu.D.Length; $i += 211) { $e = [Math]::Abs($cpu.D[$i] - $gpu.D[$i]); if ($e -gt $mx) { $mx = $e } }
    Expect ($mx -lt 0.004 -and -not [PixTune.Gpu]::LastError) ("{0} {1} ({2}): same pixels on GPU and CPU (largest difference {3:N5}; cpu {4} ms, gpu {5} ms) {6}" -f $id, $style, $where, $mx, $tc, $tg, [PixTune.Gpu]::LastError)
}
$script:GtCases = @(@('tune', 0), @('details', 0), @('hdr', 0), @('glamour', 2), @('tonal', 0), @('drama', 4), @('vintage', 11), @('grainy', 3),
    @('noir', 4), @('film', 0), @('film', 8), @('film', 9), @('bloom', 0), @('halation', 0), @('rawdev', 0),
    @('curves', 0), @('wb', 0), @('vignette', 0), @('bw', 5), @('grain', 0), @('color', 0),
    @('retrolux', 0), @('grunge', 0), @('grunge', 1), @('grunge', 2), @('grunge', 3), @('grunge', 4))

$script:GtPoint = [PixTune.Gpu]::PointJobs
St 'whole' {
    Expect ([PixTune.Gpu]::Available) "graphics card in use: $([PixTune.Gpu]::Adapter)"
    [PixTune.Gpu]::PointJobs = $true
    foreach ($c in $script:GtCases) { Test-GpuTool $App.Doc.Preview $c[0] $c[1] 'whole photo' }
}
St 'part' {
    $p = $App.Doc.Preview
    $part = $p.Region(300, 200, $p.W - 500, $p.H - 400)
    foreach ($c in $script:GtCases) { Test-GpuTool $part $c[0] $c[1] 'part' }
}
St 'stack' {
    # a slider frame on the second of five steps: the pictures stay on the card, only the bitmap comes back
    $doc = New-Object PixTune.Document; $doc.LoadImg($App.Doc.Preview.Clone(), 'gpu-stack')
    $mk = {
        param($b)
        $l = New-Object 'System.Collections.Generic.List[PixTune.Step]'
        $a = New-Object PixTune.Step 'wb'; $a.Set('temperature', 15); $l.Add($a)
        $t = New-Object PixTune.Step 'tune'; $t.Set('brightness', $b); $t.Set('shadows', 20); $t.MaskOn = $true
        $k = New-Object PixTune.Stroke; $k.Ch = 'mask'; $k.V = 1; $k.R = 0.2; $k.Pts.Add((New-Object System.Drawing.PointF 0.5, 0.5)); $t.Mask.Add($k); $l.Add($t)
        $f = New-Object PixTune.Step 'film'; $f.Set('strength', 60); $l.Add($f)
        $v = New-Object PixTune.Step 'vignette'; $v.Set('outer', -40); $l.Add($v)
        $c = New-Object PixTune.Step 'curves'; $c.S['curve_rgb'] = '0,0;0.4,0.45;1,1'; $l.Add($c)
        , $l
    }
    $null = $doc.RenderPreviewSync((& $mk 10))
    $sw = [Diagnostics.Stopwatch]::StartNew(); $g = $doc.RenderPreviewSync((& $mk 30)); $gb = $g.ToBitmap(); $tg = $sw.ElapsedMilliseconds
    $kept = $g.OnGpu
    [PixTune.Gpu]::Enabled = $false
    $doc2 = New-Object PixTune.Document; $doc2.LoadImg($App.Doc.Preview.Clone(), 'cpu-stack')
    $null = $doc2.RenderPreviewSync((& $mk 10))
    $sw.Restart(); $c = $doc2.RenderPreviewSync((& $mk 30)); $cb = $c.ToBitmap(); $tc = $sw.ElapsedMilliseconds
    [PixTune.Gpu]::Enabled = $true
    $mx = 0.0; $gd = $g.D; $cd = $c.D
    for ($i = 0; $i -lt $gd.Length; $i += 101) { $e = [Math]::Abs($gd[$i] - $cd[$i]); if ($e -gt $mx) { $mx = $e } }
    $bd = 0
    for ($y = 0; $y -lt $gb.Height; $y += 23) { for ($x = 0; $x -lt $gb.Width; $x += 23) { $p = $gb.GetPixel($x, $y); $q = $cb.GetPixel($x, $y); $bd = [Math]::Max($bd, [Math]::Max([Math]::Abs($p.R - $q.R), [Math]::Max([Math]::Abs($p.G - $q.G), [Math]::Abs($p.B - $q.B)))) } }
    $gb.Dispose(); $cb.Dispose()
    Expect $kept 'the frame stays on the card until it is read'
    Expect ($mx -lt 0.004 -and $bd -le 1) ("stack frame: same pixels on GPU and CPU (largest difference {0:N5}, bitmap {1} level(s); cpu {2} ms, gpu {3} ms)" -f $mx, $bd, $tc, $tg)
}
St 'bad-shader' {
    $f = [Func[PixTune.Gpu+Job, PixTune.Gpu+GB]] { param($j) $j.K('NoSuchKernel', 3, $null, $null, [PixTune.Gpu+GB[]]@($j.Source)) }
    $r = [PixTune.Gpu]::RunJob($App.Doc.Preview, $f)
    Expect ($null -eq $r -and [PixTune.Gpu]::Available) 'a shader that cannot be used: CPU for that tool, the GPU stays on'
    [PixTune.Gpu]::LastError = ''
}
Start-StRun -Log 'gputools.log' -Finish { [PixTune.Gpu]::PointJobs = $script:GtPoint; [PixTune.Gpu]::Enabled = $App.Settings.UseGpu }
