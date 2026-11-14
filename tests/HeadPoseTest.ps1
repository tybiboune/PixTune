# Head Pose in the window: face found, 3D face mesh fitted, sliders render quickly, the step applies.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
function global:StWait([string]$name) { $script:StSteps.Enqueue(@{ Name = $name; Wait = $true }) }
$script:Shot = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }

StWait 'load'
St 'open' { Start-Tool 'headpose' }
StWait 'faces'
St 'faces-check' {
    $f = $App.Step.Str('faces', '')
    $script:Trace.Add("faces='$f' mesh=$([PixTune.FaceMesh]::Available) '$([PixTune.FaceMesh]::LastError)'")
    $script:T0 = [DateTime]::Now; Set-Params @{ hrot = 60; smile = 60 }
}
StWait 'render'
St 'shot' {
    $script:Trace.Add("turn+smile rendered in $([int]([DateTime]::Now - $script:T0).TotalMilliseconds) ms (includes the first face fit)")
    & $script:Shot 'headpose'
    $script:T0 = [DateTime]::Now; Set-Params @{ hrot = 30 }
}
StWait 'render2'
St 'slider' { $script:Trace.Add("next slider change rendered in $([int]([DateTime]::Now - $script:T0).TotalMilliseconds) ms (face fit cached)"); Apply-Tool }
St 'applied' {
    $s = $App.Doc.Steps[$App.Doc.Steps.Count - 1]
    $a = $App.Doc.Preview; $b = $App.Doc.RenderPreviewSync($App.Doc.Steps); $d = 0.0
    for ($i = 0; $i -lt $a.D.Length; $i += 101) { $d += [Math]::Abs($a.D[$i] - $b.D[$i]) }
    $script:Trace.Add("applied: tool=$($s.Tool) hrot=$($s.F('hrot', 0)) smile=$($s.F('smile', 0)) changed=$([Math]::Round($d, 1)) (expect headpose, 30, 60, > 0)")
}
# AI mode (only when its models are installed)
St 'ai-open' { Start-Tool 'headpose' }
StWait 'faces2'
St 'ai-mode' {
    $script:Trace.Add("mode tiles=$($App.PoseTiles.Controls.Count) ai=$($App.Step.I('ai', 0)) installed=$([PixTune.LivePortrait]::Installed) (expect 2, 0)")
    if ([PixTune.LivePortrait]::Installed) { Set-HeadPoseMode $true; $script:T0 = [DateTime]::Now; Set-Params @{ hrot = 60 } }
}
StWait 'ai-render'
St 'ai-shot' {
    $script:Trace.Add("AI turn rendered in $([int]([DateTime]::Now - $script:T0).TotalMilliseconds) ms ai=$($App.Step.I('ai', 0)) on $([PixTune.LivePortrait]::Device) '$([PixTune.LivePortrait]::LastError)'")
    & $script:Shot 'headpose-ai'
    Apply-Tool
}
St 'ai-applied' { $s = $App.Doc.Steps[$App.Doc.Steps.Count - 1]; $script:Trace.Add("applied: tool=$($s.Tool) ai=$($s.I('ai', 0)) hrot=$($s.F('hrot', 0)) (expect headpose, 1, 60)") }

$script:StTimer = New-Object System.Windows.Forms.Timer
$script:StTimer.Interval = 800
$script:StTimer.add_Tick({
        if ($script:StSteps.Count -eq 0) {
            $script:StTimer.Stop()
            (@("errors: $($script:ErrorLog.Count)") + $script:ErrorLog + $script:Trace) | Set-Content (Join-Path $script:StOut 'headpose.log')
            Complete-SelfTest; return
        }
        $st = $script:StSteps.Peek()
        if ($st.Wait) { if (-not $App.Canvas.Busy) { [void]$script:StSteps.Dequeue() }; return }
        [void]$script:StSteps.Dequeue()
        try { & $st.Action } catch { $script:ErrorLog.Add("[$($st.Name)] $($_.Exception.Message)`n$($_.ScriptStackTrace)") }
    })
$script:StTimer.Start()
