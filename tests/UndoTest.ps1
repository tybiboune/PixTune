# Undo / Redo: inside a tool (sliders, mask), the buttons' state, Ctrl+Z and Ctrl+Shift+Z, and the photo's own
# history once tools are applied (the last 10 operations).
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
function global:StWait([string]$name) { $script:StSteps.Enqueue(@{ Name = $name; Wait = $true }) }
function global:Get-Tune { 'brightness={0} contrast={1} mask={2} undo={3} redo={4} buttons={5}/{6}' -f (Get-Param 'brightness'), (Get-Param 'contrast'), $App.Step.MaskOn, $App.ToolUndo.Count, $App.ToolRedo.Count, $btnUndo.Enabled, $btnRedo.Enabled }
# the keys as the window receives them
function global:Send-Keys([System.Windows.Forms.Keys]$k) {
    [System.Windows.Forms.Form].GetMethod('OnKeyDown', [Reflection.BindingFlags]'Instance, NonPublic').Invoke($App.Form, [object[]]@([System.Windows.Forms.KeyEventArgs]::new($k))) | Out-Null
}

StWait 'load'
St 'open' { Start-Tool 'tune'; $script:Trace.Add("tune opened: $(Get-Tune) (expect 0 0 False 0 0 False/False)"); Set-Params @{ brightness = 30 } }
StWait 'r1'
St 'second' { Set-Params @{ contrast = 20 } }
StWait 'r2'
St 'third' { Set-MaskKind 'all' }
StWait 'r3'
St 'recorded' {
    $script:Trace.Add("after 3 changes: $(Get-Tune) (expect 30 20 True 3 0 True/False)")
    Invoke-Undo
    $script:Trace.Add("undo (button): $(Get-Tune) (expect 30 20 False 2 1)")
    Send-Keys ([System.Windows.Forms.Keys]::Control -bor [System.Windows.Forms.Keys]::Z)
    $script:Trace.Add("Ctrl+Z: $(Get-Tune) (expect 30 0 False 1 2)")
    Send-Keys ([System.Windows.Forms.Keys]::Control -bor [System.Windows.Forms.Keys]::Shift -bor [System.Windows.Forms.Keys]::Z)
    $script:Trace.Add("Ctrl+Shift+Z: $(Get-Tune) (expect 30 20 False 2 1)")
    Set-Params @{ brightness = 50 }
}
StWait 'r4'
St 'branch' { $script:Trace.Add("a new change after undo: $(Get-Tune) (expect 50 20 False 3 0: redo emptied)") }
St 'limit' { for ($i = 1; $i -le 12; $i++) { Set-Params @{ brightness = $i }; Update-ToolHistory }; $script:Trace.Add("after 12 more changes: undo=$($App.ToolUndo.Count) (expect 10)") }
St 'apply' {
    Apply-Tool
    $script:Trace.Add("applied: steps=$($App.Doc.Steps.Count) undo button=$($btnUndo.Enabled) (expect 1, True)")
    Send-Keys ([System.Windows.Forms.Keys]::Control -bor [System.Windows.Forms.Keys]::Z)
    $script:Trace.Add("Ctrl+Z on the photo: steps=$($App.Doc.Steps.Count) (expect 0)")
    Send-Keys ([System.Windows.Forms.Keys]::Control -bor [System.Windows.Forms.Keys]::Shift -bor [System.Windows.Forms.Keys]::Z)
    $script:Trace.Add("Ctrl+Shift+Z on the photo: steps=$($App.Doc.Steps.Count) (expect 1)")
}
St 'doc-limit' {
    for ($i = 0; $i -lt 12; $i++) { Start-Tool 'tune'; Set-Params @{ brightness = 5 + $i }; Apply-Tool }
    $n = 0; while ($App.Doc.CanUndo) { $App.Doc.Undo(); $n++ }
    $script:Trace.Add("photo operations that could be undone: $n (expect 10)")
}

Start-StRun -Log 'undo.log'
