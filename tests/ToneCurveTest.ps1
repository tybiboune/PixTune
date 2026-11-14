# Always-visible tone curve: editing it adds a curves step kept last; tools applied later go before it; undo follows.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
function global:StWait([string]$name) { $script:StSteps.Enqueue(@{ Name = $name; Wait = $true }) }
$script:Shot = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }
# as if the user had moved points: set them, then raise the editor's change event (drag, then release)
$script:Edit = {
    param([string]$pts)
    $toneEd.PointsString = $pts
    $fire = $toneEd.GetType().GetMethod('Fire', [Reflection.BindingFlags]'Instance,NonPublic')
    $a = [object[]]::new(1); $a[0] = $false; $fire.Invoke($toneEd, $a) | Out-Null
    $a[0] = $true; $fire.Invoke($toneEd, $a) | Out-Null
}
$script:Stack = { ($App.Doc.Steps | ForEach-Object { if ($_.Str('tonecurve', '') -eq '1') { 'TONE' } else { $_.Tool } }) -join ',' }

StWait 'load'
St 'check-panel' {
    $script:Trace.Add("panel visible=$($curveDock.Visible) height=$($curveDock.Height) points=$(@($toneEd.Pts).Count) histogram=$([bool]$toneEd.ChannelHistograms) (expect True, 5, True)")
    & $script:Shot 'tone-start'
    & $script:Edit '0,0;0.25,0.15;0.5,0.5;0.75,0.85;1,1'
}
StWait 'render1'
St 's-curve' {
    $script:Trace.Add("after S-curve: stack=$(& $script:Stack) undo=$($App.Doc.CanUndo) (expect TONE, True)")
    & $script:Shot 'tone-scurve'
    Start-Tool 'tune'; Set-Params @{ saturation = 40 }
}
StWait 'render2'
St 'tool-open' {
    $eff = ($App.Doc.EffectiveSteps($true) | ForEach-Object { if ($_.Str('tonecurve', '') -eq '1') { 'TONE' } else { $_.Tool } }) -join ','
    $inp = ($App.Doc.StepsBeforePending() | ForEach-Object { $_.Tool }) -join ','
    $script:Trace.Add("tool open: shown=$eff tool input='$inp' (expect tune,TONE and empty input)")
    Apply-Tool
}
St 'applied' { $script:Trace.Add("after apply: stack=$(& $script:Stack) (expect tune,TONE)"); & $script:Edit '0,0.05;0.25,0.2;0.5,0.5;0.75,0.8;1,0.95' }
StWait 'render3'
St 'second-edit' { $script:Trace.Add("second edit: stack=$(& $script:Stack) curve=$($App.Doc.ToneCurve.Str('curve_rgb', ''))"); Invoke-Undo }
StWait 'render4'
St 'undo1' { $script:Trace.Add("undo: stack=$(& $script:Stack) editor=$($toneEd.PointsString) (expect S-curve back)"); Invoke-Undo; Invoke-Undo }
StWait 'render5'
St 'undo-all' { $script:Trace.Add("undo x3: stack='$(& $script:Stack)' editor=$($toneEd.PointsString) (expect empty, default points)"); & $script:Shot 'tone-end' }

Start-StRun -Log 'tonecurve.log'
