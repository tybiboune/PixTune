# Exercises the UI through real Click events (the same path as a mouse click), not direct function calls.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
$script:StSnap = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }
$script:Click = {
    param($ctrl)
    $m = $ctrl.GetType().GetMethod('OnClick', [Reflection.BindingFlags]'Instance,NonPublic')
    $m.Invoke($ctrl, @([EventArgs]::Empty)) | Out-Null
}
$script:FindTile = {
    param([string]$caption)
    foreach ($grp in $App.Body.Controls) { foreach ($c in $grp.Controls) { if ($c -is [PixTune.Tile] -and $c.Caption -eq $caption) { return $c } } }
    $null
}
$script:Note = { param($n) $script:Trace.Add(("{0}: mode={1} pending={2} steps={3} hud='{4}'" -f $n, $App.Mode, [bool]$App.Doc.Pending, $App.Doc.Steps.Count, $App.Canvas.Hud)) }

St 'looks-tab' { & $script:Click ($App.Head.Controls | Where-Object { $_.Name -eq 'looks' }) }
St 'click-look' { & $script:Note 'before look click'; & $script:Click (& $script:FindTile 'Pop') }
St 'after-look-click' { & $script:Note 'after look click' }
St 'click-apply' { & $script:Click $btnApply }
St 'after-apply' { & $script:Note 'after apply' }
St 'tools-tab' { & $script:Click ($App.Head.Controls | Where-Object { $_.Name -eq 'tools' }) }
St 'click-tool' { & $script:Click (& $script:FindTile 'Drama') }
St 'after-tool-click' { & $script:Note "after tool click tool=$($App.Step.Tool)" }
St 'click-style' { & $script:Click (& $script:FindTile 'Dark 2') }
St 'after-style' { $script:Trace.Add("drama style=$($App.Step.I('style', -1)) (expect 4)") }
St 'click-apply2' { & $script:Click $btnApply }
St 'open-bw' { & $script:Click (& $script:FindTile 'Black & White') }
St 'bw-filter' { & $script:Click (& $script:FindTile 'Red') }
St 'after-bw' { $script:Trace.Add("bw tool=$($App.Step.Tool) filter=$($App.Step.I('filter', -1)) (expect 1)") }
St 'bw-cancel' { & $script:Click $btnCancel }
St 'open-text' { & $script:Click (& $script:FindTile 'Text') }
St 'text-invert' {
    $b = foreach ($row in $App.Body.Controls) { foreach ($c in $row.Controls) { if ($c -is [PixTune.IconButton] -and $c.Text -eq 'Invert') { $c } } }
    & $script:Click @($b)[0]
}
St 'after-invert' { $script:Trace.Add("text invert=$($App.Step.I('invert', -1)) (expect 1)") }
St 'text-cancel' { & $script:Click $btnCancel }
St 'looks-tab2' { & $script:Click ($App.Head.Controls | Where-Object { $_.Name -eq 'looks' }) }
St 'looks-count' { $n = 0; foreach ($grp in $App.Body.Controls) { foreach ($c in $grp.Controls) { if ($c -is [PixTune.Tile]) { $n++ } } }; $script:Trace.Add("look tiles: $n") }
St 'click-portra' { & $script:Click (& $script:FindTile 'Portra') }
St 'after-portra' { & $script:Note 'after Portra look click' }
St 'apply-portra' { & $script:Click $btnApply }
St 'tools-tab2' { & $script:Click ($App.Head.Controls | Where-Object { $_.Name -eq 'tools' }) }
St 'open-film' { & $script:Click (& $script:FindTile 'Film') }
St 'film-style' { & $script:Click (& $script:FindTile 'Polaroid 600') }
St 'after-film' { $script:Trace.Add("film tool=$($App.Step.Tool) style=$($App.Step.I('style', -1)) (expect 25)") }
St 'film-apply' { & $script:Click $btnApply }
St 'open-color' { & $script:Click (& $script:FindTile 'Color') }
St 'color-blue' { & $script:Click (& $script:FindTile 'Blue') }
St 'color-sat' { $App.Sliders['sat'].SetValue(60, $true) }
St 'after-color' { $script:Trace.Add("color band=$($App.ColorBand) s_blue=$($App.Step.Get('s_blue', -999)) (expect 60)") }
St 'color-apply' { & $script:Click $btnApply }
St 'end' { & $script:Note 'end'; $script:Trace.Add("stack: " + (($App.Doc.Steps | ForEach-Object { if ($_.Tool -eq 'look') { 'look:' + $_.Str('name', '?') } else { $_.Tool } }) -join ', ')) }

Start-StRun -Log 'clicktest.log' -Snap
