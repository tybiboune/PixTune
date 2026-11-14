# The little map shown in the corner while zoomed in: it appears only when part of the photo is off screen,
# the marked part matches what is really on screen, and dragging it moves the view there.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

# the private corner rectangle, so the test checks what the window really draws
function global:MapRect {
    $m = [PixTune.ImageCanvas].GetMethod('MapRect', [Reflection.BindingFlags]'Instance,NonPublic')
    [RectangleF]$m.Invoke($App.Canvas, @())
}
function global:Press($x, $y) {
    $ev = New-Object System.Windows.Forms.MouseEventArgs ([System.Windows.Forms.MouseButtons]::Left), 1, [int]$x, [int]$y, 0
    $App.Canvas.GetType().GetMethod('OnMouseDown', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($App.Canvas, @([System.Windows.Forms.MouseEventArgs]$ev))
}

St 'fit' { $App.Canvas.Fit() }
StWait 'fitted'
St 'no-map-when-fit' {
    $r = MapRect
    Expect ($r.IsEmpty) "no map while the whole photo is on screen"
}
St 'zoom' { $App.Canvas.ZoomActual() }
StWait 'zoomed'
St 'map-appears' {
    $r = MapRect
    Expect (-not $r.IsEmpty) "the map appears once zoomed in (corner box $([int]$r.Width)x$([int]$r.Height))"
    if ($r.IsEmpty) { return }
    # it sits inside the canvas, in the bottom right
    Expect ($r.Right -le $App.Canvas.Width -and $r.Bottom -le $App.Canvas.Height -and $r.X -gt $App.Canvas.Width / 2) `
        "it sits in the bottom right corner of the photo area"
    # its shape follows the photo's
    $want = $App.Doc.Preview.W / [double]$App.Doc.Preview.H
    $got = $r.Width / $r.Height
    Expect ([Math]::Abs($want - $got) -lt 0.05) ("its shape follows the photo's ({0:N2} against {1:N2})" -f $got, $want)
}
St 'drag-moves-the-view' {
    $r = MapRect
    if ($r.IsEmpty) { Expect $false "no map to drag"; return }
    $before = $App.Canvas.VisibleNorm
    # press near the top left of the map: the view should move there
    Press ($r.X + $r.Width * 0.2) ($r.Y + $r.Height * 0.2)
    $after = $App.Canvas.VisibleNorm
    Expect ($after.X -lt $before.X -or $after.Y -lt $before.Y) `
        ("dragging the map moves the view ({0:N2},{1:N2} -> {2:N2},{3:N2})" -f $before.X, $before.Y, $after.X, $after.Y)
    # and the view stays inside the photo
    Expect ($after.X -ge -0.001 -and $after.Y -ge -0.001 -and $after.Right -le 1.001 -and $after.Bottom -le 1.001) `
        "the view stays inside the photo"
    # now the other corner
    Press ($r.X + $r.Width * 0.85) ($r.Y + $r.Height * 0.85)
    $far = $App.Canvas.VisibleNorm
    Expect ($far.X -gt $after.X -and $far.Y -gt $after.Y) "dragging to the far corner moves the view the other way"
    $App.SaveScreenshot((Join-Path $script:StOut 'map.png'))
}
St 'back' { $App.Canvas.Fit() }
Start-StRun -Log 'map.log'
