# Looks must land in the stack as their individual, editable tools.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
$script:Click = { param($ctrl) $ctrl.GetType().GetMethod('OnClick', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($ctrl, @([EventArgs]::Empty)) | Out-Null }
$script:FindTile = { param([string]$caption) foreach ($grp in $App.Body.Controls) { foreach ($c in $grp.Controls) { if ($c -is [PixTune.Tile] -and $c.Caption -eq $caption) { return $c } } } }
# presses an action of an edit-stack row, the same way a mouse click on it does
$script:StackAction = {
    param([int]$index, [string]$action)
    foreach ($row in $App.Body.Controls) { if ($row -is [PixTune.StackRow] -and $row.Index -eq $index) { $row.RunAction(@('eye', 'edit', 'brush', 'del').IndexOf($action)); return } }
    throw "no stack row $index"
}
$script:Stack = { ($App.Doc.Steps | ForEach-Object { "{0}[{1}]" -f $_.Tool, $_.Str('look', '') }) -join ', ' }

St 'looks' { & $script:Click ($App.Head.Controls | Where-Object { $_.Name -eq 'looks' }) }
St 'pick-push' { & $script:Click (& $script:FindTile 'Push') }
St 'apply' { & $script:Click $btnApply }
St 'after-apply' { $script:Trace.Add("after applying Push: $(& $script:Stack)   (expect tune, details, vignette tagged Push)"); $script:Trace.Add("status: $($App.Canvas.Status)") }
St 'open-stack' { & $script:Click $btnStack }
St 'stack-shot' { $App.SaveScreenshot((Join-Path $script:StOut 'stack.png')); $script:Trace.Add("stack rows: " + (($App.Body.Controls | Where-Object { $_ -is [PixTune.StackRow] } | ForEach-Object Title) -join ' | ')) }
St 'edit-details' { & $script:StackAction 1 'edit' }
St 'in-tool' { $script:Trace.Add("editing: tool=$($App.Step.Tool) structure=$($App.Step.F('structure')) header='$(($App.Head.Controls | Select-Object -First 1).Text)'"); $App.Sliders['structure'].SetValue(-30, $true) }
St 'apply-edit' { & $script:Click $btnApply }
St 'after-edit' { $script:Trace.Add("after edit: details structure=$($App.Doc.Steps[1].F('structure')) (expect -30), look tag='$($App.Doc.Steps[1].Str('look', ''))', mode=$($App.Mode)") }
St 'undo' { Invoke-Undo }
St 'after-undo' { $script:Trace.Add("after undo: structure=$($App.Doc.Steps[1].F('structure')) (expect 40)"); Invoke-Undo }
St 'after-undo2' { $script:Trace.Add("after 2nd undo: $($App.Doc.Steps.Count) steps (expect 0: the whole look undoes in one go)") }
St 'legacy' {
    $l = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    $l.Add((New-LookBundle 'Fine Art' (Get-BuiltinLooks)['Fine Art']))
    $App.Doc.SetSteps($l); Request-Render; Show-Stack
}
St 'split' { & $script:StackAction 0 'edit' }
St 'after-split' { $script:Trace.Add("old sealed look after Edit: $(& $script:Stack)   (expect bw, tonal, vignette tagged Fine Art)") }

Start-StRun -Log 'lookedit.log' -Finish { $App.SaveScreenshot((Join-Path $script:StOut 'final.png')) }
