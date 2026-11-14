# Selective: a zone's handles stretch and turn it like Lens Blur's, the Size slider scales it, and the effect follows.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
function global:StWait([string]$name) { $script:StSteps.Enqueue(@{ Name = $name; Wait = $true }) }
$script:Shot = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }

# drives the canvas's own mouse handlers from one screen point to another
$script:Flags = [Reflection.BindingFlags]'Instance, NonPublic'
function global:Get-Screen($pt) { [PixTune.ImageCanvas].GetMethod('ToScreen', $script:Flags).Invoke($App.Canvas, @($pt)) }
function global:Invoke-CanvasDrag($from, $to) {
    $c = $App.Canvas
    $ev = { param($n, $p) [PixTune.ImageCanvas].GetMethod($n, $script:Flags).Invoke($c, [object[]]@([System.Windows.Forms.MouseEventArgs]::new([System.Windows.Forms.MouseButtons]::Left, 1, [int]$p.X, [int]$p.Y, 0))) }
    & $ev 'OnMouseDown' $from
    for ($k = 1; $k -le 6; $k++) { & $ev 'OnMouseMove' (New-Object System.Drawing.PointF ($from.X + ($to.X - $from.X) * $k / 6), ($from.Y + ($to.Y - $from.Y) * $k / 6)) }
    & $ev 'OnMouseUp' $to
}
function global:Get-Zone { $p = (Get-SelPoints)[$App.Canvas.Selected]; 'R={0:N3} RY={1:N3} Ang={2:N0}' -f $p.R, $p.RY, $p.Ang }

StWait 'load'
St 'open' {
    Start-Tool 'selective'
    $App.ToolDef.OnPointAdded.Invoke([System.Drawing.PointF]::new(0.5, 0.45))
    Set-Params @{ brightness = 80 }
    $script:Trace.Add("new point: $(Get-Zone) handles=$($App.Canvas.PointRadiiY.Count) (expect R=RY=0.150, 1)")
}
StWait 'r1'
St 'stretch' {
    $script:Before = (Get-SelPoints)[0].ToString()
    $p = (Get-SelPoints)[0]; $c = Get-Screen (New-Object System.Drawing.PointF $p.X, $p.Y)
    $r =$p.R * 0.8 * [PixTune.ImageCanvas].GetProperty('MinDimScreen', $script:Flags).GetValue($App.Canvas, $null)
    $script:Trace.Add("handle at +$([int]$r) px from the centre")
    Invoke-CanvasDrag (New-Object System.Drawing.PointF ($c.X + $r), $c.Y) (New-Object System.Drawing.PointF ($c.X + 2.2 * $r), $c.Y)
    $script:Trace.Add("after width drag: $(Get-Zone) slider=$($App.Sliders['size'].Value) (expect R about 0.33, RY 0.150)")
}
StWait 'r2'
St 'rotate' {
    $p = (Get-SelPoints)[0]; $c = Get-Screen (New-Object System.Drawing.PointF $p.X, $p.Y)
    $ms = [PixTune.ImageCanvas].GetProperty('MinDimScreen', $script:Flags).GetValue($App.Canvas, $null)
    $rx = $p.R * 0.8 * $ms
    $from = New-Object System.Drawing.PointF ($c.X + $rx + 26), $c.Y
    $to = New-Object System.Drawing.PointF ($c.X + ($rx + 26) * 0.7071), ($c.Y + ($rx + 26) * 0.7071)
    Invoke-CanvasDrag $from $to
    $script:Trace.Add("after rotate drag: $(Get-Zone) (expect Ang 45)")
}
StWait 'r3'
St 'shot' { & $script:Shot 'selective-zone' }
St 'size' {
    $p0 = (Get-SelPoints)[0]; $ratio0 = $p0.R / $p0.RY
    $App.ToolDef.SetParam.Invoke('size', 30); Request-Render
    $p1 = (Get-SelPoints)[0]
    $script:Trace.Add(("size slider 30: {0} ratio {1:N2} -> {2:N2} (expect larger radius 0.150, ratio kept)" -f (Get-Zone), $ratio0, ($p1.R / $p1.RY)))
}
StWait 'r4'
St 'old-format' {
    $l = [PixTune.Adjust]::ParseCtl('0.3,0.4,0.2,10,0,0,0')
    $script:Trace.Add("old 7-value point: R=$($l[0].R) RY=$($l[0].RY) Ang=$($l[0].Ang) (expect 0.2 0.2 0) -> '$($l[0])'")
    Apply-Tool
}
St 'applied' { $s = $App.Doc.Steps[$App.Doc.Steps.Count - 1]; $script:Trace.Add("applied: tool=$($s.Tool) points='$($s.Str('points', ''))'") }

Start-StRun -Log 'selzone.log'
