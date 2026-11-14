# Drives the running window through the main workflows and saves screenshots.
# Started by:  PixTune.ps1 -SelfTest <output folder> <photo>
# Dot-sourced from the window's Shown handler; everything below runs from a timer.

$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$App.Settings.ExportDir = Join-Path $script:StOut 'export'

$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }

$snap = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }
$script:StSnap = $snap

St 'home-tools' { }
St 'home-looks' { Show-Home 'looks' }
St 'tune' { Show-Home 'tools'; Start-Tool 'tune'; Set-Params @{ brightness = 20; contrast = 25; saturation = 20; shadows = 30 } }
St 'tune-applied' { Apply-Tool }
St 'crop' { Start-Tool 'crop'; Set-CropAspect 1.5 }
St 'crop-applied' { Apply-Tool }
St 'selective' { Start-Tool 'selective'; $App.ToolDef.OnPointAdded.Invoke([System.Drawing.PointF]::new(0.5, 0.45)); Set-Params @{ brightness = 60; saturation = 40 } }
St 'selective-applied' { Apply-Tool }
St 'drama' { Start-Tool 'drama' }
St 'drama-applied' { Apply-Tool }
St 'curves' { Start-Tool 'curves' }
St 'curves-cancel' { Cancel-Tool }
St 'brush' {
    Start-Tool 'brush'
    $k = New-Object PixTune.Stroke; $k.Ch = 'exposure'; $k.V = 8; $k.R = 0.06
    $k.Pts.Add((New-Object System.Drawing.PointF 0.2, 0.7)); $k.Pts.Add((New-Object System.Drawing.PointF 0.8, 0.7))
    $App.Step.Strokes.Add($k); $App.ShowMask = $true; Update-BrushMask; Request-Render
}
St 'brush-applied' { Apply-Tool }
St 'lensblur' { Start-Tool 'lensblur' }
St 'lensblur-applied' { Apply-Tool }
St 'text' { Start-Tool 'text'; $App.TextBox.Text = "PixTune`r`nself test" }
St 'text-applied' { Apply-Tool }
St 'portrait' { Start-Tool 'portrait' }
St 'portrait-cancel' { Cancel-Tool }
St 'frames' { Start-Tool 'frames' }
St 'frames-cancel' { Cancel-Tool }
St 'stack' { Show-Stack }
St 'stackbrush' {
    Start-StackBrush 2
    $k = New-Object PixTune.Stroke; $k.Ch = 'mask'; $k.V = 1; $k.R = 0.2
    $k.Pts.Add((New-Object System.Drawing.PointF 0.3, 0.5)); $k.Pts.Add((New-Object System.Drawing.PointF 0.7, 0.5))
    $App.Step.Mask.Add($k); Request-Render
}
St 'stackbrush-applied' { Apply-Tool }
St 'edit-from-stack' { Invoke-StackAction 0 'edit' }
St 'edit-from-stack-applied' { Set-Params @{ brightness = -10 }; Apply-Tool }
St 'look' { Show-Home 'looks'; Start-Look 'Fine Art' (Get-BuiltinLooks)['Fine Art'] }
St 'look-applied' { Apply-Tool }
St 'export' { Export-ToFolder; Show-Home 'export' }
St 'undo' { Invoke-Undo }
St 'undo2' { Invoke-Undo }
St 'redo' { Invoke-Redo }

Start-StRun -Log 'selftest.log' -Snap -Lines {
    $exp = @(Get-ChildItem (Join-Path $script:StOut 'export') -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Name) $($_.Length)" })
    @("errors: $($script:ErrorLog.Count)") + $script:ErrorLog + @("exported: $($exp -join ', ')", "steps: $($App.Doc.Steps.Count)")
}
