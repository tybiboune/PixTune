# The commands and names the window tests use, mapped onto the program ([PixTune.App], engine\App*.cs).
# Loaded by PixTune.ps1 before a test script when it runs with -SelfTest.
$global:App = [PixTune.App]::Current
$global:Tools = [PixTune.App]::Tools
foreach ($n in 'BtnUndo', 'BtnRedo', 'BtnFit', 'Btn100', 'BtnStack', 'BtnApply', 'BtnCancel', 'BtnHist', 'BtnExport', 'BtnInfo', 'BtnCompare',
    'MaskState', 'MaskDock', 'MaskBody', 'MaskLabel', 'ToneEd', 'ToneReset', 'CurveDock', 'Side', 'LblTitle') {
    Set-Variable -Scope Global -Name ($n.Substring(0, 1).ToLower() + $n.Substring(1)) -Value $App.$n
}

# deferred script blocks, run after the current event like the program's own deferred actions
$global:PsQueue = New-Object System.Collections.Queue
function global:Invoke-Later([scriptblock]$sb, [object[]]$argList) {
    $global:PsQueue.Enqueue(@{ Sb = $sb; Args = $argList })
    $App.Later([Action] { Invoke-PsQueue })
}
function global:Invoke-PsQueue {
    while ($global:PsQueue.Count -gt 0) {
        $it = $global:PsQueue.Dequeue(); $a = @($it.Args)
        try { & $it.Sb @a } catch { $App.ErrorLog.Add("$($_.Exception.Message)`n$($_.ScriptStackTrace)") }
    }
}
function global:Invoke-Queue { $App.RunQueue(); Invoke-PsQueue }
. (Join-Path $PSScriptRoot 'Harness.ps1')

function global:Get-Settings { [PixTune.AppSettings]::Load() }
function global:Set-Status([string]$m) { $App.SetStatus($m) }
function global:Start-Tool([string]$id, $existing = $null, [int]$index = -1) { $App.StartTool($id, $existing, $index) }
function global:Apply-Tool { $App.ApplyTool() }
function global:Cancel-Tool { $App.CancelTool() }
function global:Show-Home([string]$tab) { $App.ShowHome($tab) }
function global:Show-Stack { $App.ShowStack() }
function global:Show-Info { $App.ShowInfo() }
function global:Show-SettingsDialog { $App.ShowSettingsDialog() }
function global:Set-Params([hashtable]$h) { $App.SetParams($h) }
function global:Get-Param([string]$k) { $App.GetParam($k) }
function global:Request-Render([switch]$Live) { $App.RequestRender([bool]$Live) }
function global:Get-ToolInput { $App.ToolInput() }
function global:Invoke-Undo { $App.InvokeUndo() }
function global:Invoke-Redo { $App.InvokeRedo() }
function global:Update-ToolHistory { $App.UpdateToolHistory() }
function global:Open-Image([string]$p, $steps = $null) { $App.OpenImage($p, $steps) }
function global:Export-ToFolder { $App.ExportToFolder() }
function global:Switch-Histogram { $App.SwitchHistogram() }
function global:Invoke-StackAction([int]$i, [string]$a) { $App.InvokeStackAction($i, $a) }
function global:Start-StackBrush([int]$i, [string]$smart = '') { $App.StartStackBrush($i, $smart) }
function global:Update-BrushMask { $App.UpdateBrushMask() }
function global:Set-CropAspect([double]$ar) { $App.SetCropAspect($ar) }
function global:Set-HeadPoseMode([bool]$ai) { $App.SetHeadPoseMode($ai) }
function global:Get-SelPoints { , $App.GetSelPoints() }
function global:Get-ZoneSize($p) { $App.GetZoneSize($p) }

# looks
function global:Get-BuiltinLooks {
    $h = [ordered]@{}
    foreach ($kv in [PixTune.App]::BuiltinLooks()) { $h[$kv.Key] = $kv.Value }
    $h
}
function global:New-LookBundle([string]$name, $steps) { [PixTune.App]::NewLookBundle($name, (Convert-Steps $steps)) }
function global:Start-Look([string]$name, $steps) { $App.StartLook($name, (Convert-Steps $steps)) }
function global:Convert-Steps($steps) {
    $l = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    foreach ($s in $steps) { $l.Add($s) }
    , $l
}

# mask panel
function global:Open-MaskFor([string]$kind) { $App.OpenMaskFor($kind) }
function global:Set-MaskKind([string]$kind, [switch]$Toggle) { $App.SetMaskKind($kind, [bool]$Toggle) }
function global:Set-MaskPaint([int]$v, [switch]$Quiet) { $App.SetMaskPaint($v, [bool]$Quiet) }
function global:Save-CurrentMask([string]$Name) { $App.SaveCurrentMask($Name) }
function global:Switch-SavedMask([int]$i) { $App.SwitchSavedMask($i) }
function global:Show-SavedMask([int]$i) { $App.ShowSavedMask($i) }
function global:Rename-SavedMask([int]$i, [string]$Name) { $App.RenameSavedMask($i, $Name) }
function global:Remove-SavedMask([int]$i) { $App.RemoveSavedMask($i) }
function global:Get-MaskSummary($s) { [PixTune.App]::MaskSummary($s) }
function global:Get-MaskLabel($s) { [PixTune.App]::MaskText($s) }
function global:Get-MaskExtras($s) { , [PixTune.App]::MaskExtras($s) }
function global:Show-MaskEdit { $App.ShowMaskEdit() }
function global:Update-MaskView { $App.UpdateMaskView() }
function global:Update-PickedMask { $App.UpdatePickedMask() }
function global:Undo-MaskPick { $App.UndoMaskPick() }
function global:Switch-MaskFold { $App.SwitchMaskFold() }
