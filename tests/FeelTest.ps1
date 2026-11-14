# Does the photo still move under the hand while a render is running? This drives the real window: it starts a
# full-resolution render of a heavy stack, then zooms and pans the canvas the way a person would, timing how long
# each one takes to be drawn. The time is what the window thread spent on it, so it is what the hand would feel.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

# one gesture, drawn and on screen; the canvas is repainted at once rather than when Windows gets round to it
function global:Gesture([string]$what, [scriptblock]$act) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $act
    $App.Canvas.Refresh()
    $ms = $sw.Elapsed.TotalMilliseconds
    $script:Feel.Add(@{ What = $what; Ms = $ms })
    $ms
}

St 'setup' {
    $script:Feel = New-Object System.Collections.Generic.List[hashtable]
    $App.Settings.PreviewSize = 2400
    $App.Doc.PreviewSizeFor = $null
    $App.Doc.RefreshPreview()
    foreach ($p in @(@('tune', 'shadows', 60), @('details', 'structure', 70), @('hdr', 'strength', 70), @('film', 'strength', 80))) {
        $s = New-Object PixTune.Step $p[0]; $s.Set($p[1], $p[2]); $App.Doc.Steps.Add($s)
    }
}
StWait 'ready'
St 'warm' { $App.Doc.ClearCaches(); $App.Renderer.RequestFull() }
StUntil 'warmed' { -not $App.Renderer.Busy } 180000

St 'while-rendering' {
    # a fresh full-resolution render of the whole stack, then the photo is handled while it runs
    $App.Doc.Steps[0].Set('shadows', 45)
    $App.Doc.ClearCaches()
    $App.Renderer.RequestFull()
    $script:Busy0 = $App.Renderer.Busy
    $null = Gesture 'zoom to 100%' { $App.Canvas.ZoomActual() }
    foreach ($i in 1..6) { $null = Gesture "pan $i" { $App.Canvas.Pan(-90, -60) } }
    $null = Gesture 'zoom to 50%' { $App.Canvas.SetDisplayScale(0.5) }
    foreach ($i in 1..6) { $null = Gesture "pan $i (at 50%)" { $App.Canvas.Pan(70, 45) } }
    $null = Gesture 'back to fit' { $App.Canvas.Fit() }
    $script:StillBusy = $App.Renderer.Busy
    $App.SaveScreenshot((Join-Path $script:StOut 'while-rendering.png'))
}
StUntil 'render-done' { -not $App.Renderer.Busy } 180000
# the same gestures with nothing being worked on, to tell the render's cost from the drawing's
St 'while-idle' {
    [PixTune.ImageCanvas]::PClear = 0; [PixTune.ImageCanvas]::PShadow = 0; [PixTune.ImageCanvas]::PScale = 0
    [PixTune.ImageCanvas]::PPhoto = 0; [PixTune.ImageCanvas]::POverlay = 0; [PixTune.ImageCanvas]::PTotal = 0; [PixTune.ImageCanvas]::Paints = 0
    $script:Busy = New-Object System.Collections.Generic.List[hashtable]
    foreach ($f in $script:Feel) { $script:Busy.Add($f) }
    $script:Feel = New-Object System.Collections.Generic.List[hashtable]
    $null = Gesture 'zoom to 100%' { $App.Canvas.ZoomActual() }
    foreach ($i in 1..6) { $null = Gesture "pan $i" { $App.Canvas.Pan(-90, -60) } }
    $null = Gesture 'zoom to 50%' { $App.Canvas.SetDisplayScale(0.5) }
    foreach ($i in 1..6) { $null = Gesture "pan $i (at 50%)" { $App.Canvas.Pan(70, 45) } }
    $null = Gesture 'back to fit' { $App.Canvas.Fit() }
}
St 'report' {
    $App.SaveScreenshot((Join-Path $script:StOut 'after-render.png'))
    Expect $script:Busy0 "a render really was under way when the photo was handled"
    $bt = ($script:Busy | % { $_.Ms } | Sort-Object)
    $it = ($script:Feel | % { $_.Ms } | Sort-Object)
    $script:Trace.Add(("while a render runs : middling {0,5:N0} ms, slowest {1,5:N0} ms" -f $bt[[int]($bt.Count / 2)], $bt[-1]))
    $script:Trace.Add(("with nothing to do  : middling {0,5:N0} ms, slowest {1,5:N0} ms" -f $it[[int]($it.Count / 2)], $it[-1]))
    for ($k = 0; $k -lt $script:Busy.Count; $k++) {
        $script:Trace.Add(("   {0,-18} {1,6:N1} ms while rendering, {2,6:N1} ms idle" -f $script:Busy[$k].What, $script:Busy[$k].Ms, $script:Feel[$k].Ms))
    }
    $sorted = $bt; $worst = $sorted[-1]; $mid = $sorted[[int]($sorted.Count / 2)]
    $script:Trace.Add("the render was still running when the last gesture was made: $($script:StillBusy)")
    $n = [Math]::Max(1, [PixTune.ImageCanvas]::Paints)
    # a repaint is two copies of the canvas: this one into the back buffer, and Windows' own flush to the screen
    $script:Trace.Add(("a repaint with nothing to do, over {0} of them: {1:N1} ms in all - clearing {2:N1}, shadow {3:N1}, shrinking {4:N1}, the photo {5:N1}, what is over it {6:N1}" -f `
        $n, ([PixTune.ImageCanvas]::PTotal / $n), ([PixTune.ImageCanvas]::PClear / $n), ([PixTune.ImageCanvas]::PShadow / $n),
        ([PixTune.ImageCanvas]::PScale / $n), ([PixTune.ImageCanvas]::PPhoto / $n), ([PixTune.ImageCanvas]::POverlay / $n)))
    # What this is about is that a render under way does not hold the window up, so it is measured against the
    # same gestures with nothing to do rather than against a fixed number of milliseconds. A fixed number says as
    # much about how busy the computer is as about the window: run on its own these gestures take 24 ms, run at
    # the end of the whole suite on the same code 105 ms, and the fixed bar then failed for the machine's state,
    # not for the window's. The stall this guards against was the window waiting on the render, which is several
    # times idle, not a fraction more. The ceiling catches a machine so slow the comparison stops meaning much.
    $idleMid = $it[[int]($it.Count / 2)]; $idleWorst = $it[-1]
    Expect (($worst -lt $idleWorst * 2.5 + 25) -and ($mid -lt $idleMid * 2 + 20) -and ($worst -lt 400)) `
        ("a render under way does not hold up a zoom or a pan (slowest {0:N0} ms against {1:N0} idle, middling {2:N0} against {3:N0}, {4} gestures)" -f $worst, $idleWorst, $mid, $idleMid, $script:Feel.Count)
}
Start-StRun -Log 'feel.log'
