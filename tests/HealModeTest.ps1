# Healing mode switch: content-aware vs AI tiles, the stroke value they paint with, and an AI stroke render.
# The user's settings file is restored at the end (the mode is saved into it).
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:SettingsBackup = if (Test-Path $script:SettingsPath) { [IO.File]::ReadAllBytes($script:SettingsPath) } else { $null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
$script:Shot = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }
$script:FindTile = { param([string]$caption) foreach ($grp in $App.Body.Controls) { foreach ($c in $grp.Controls) { if ($c -is [PixTune.Tile] -and $c.Caption -eq $caption) { return $c } } } }
$script:Click = {
    param($t)
    $ev = [System.Windows.Forms.MouseEventArgs]::new([System.Windows.Forms.MouseButtons]::Left, 1, 5, 5, 0)
    $argv = [object[]]::new(1); $argv[0] = [System.EventArgs]$ev
    $t.GetType().GetMethod('OnClick', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($t, $argv) | Out-Null
}

St 'open' { $App.Settings.HealAi = $false; Start-Tool 'healing' }
St 'open-check' { $script:Trace.Add("installed=$([PixTune.Lama]::Installed) brush=$($App.Canvas.BrushValue) (expect 1)"); & $script:Click (& $script:FindTile 'AI (LaMa)') }
St 'ai-check' {
    $script:Trace.Add("after AI tile: brush=$($App.Canvas.BrushValue) setting=$($App.Settings.HealAi) (expect 2, True)")
    $k = New-Object PixTune.Stroke; $k.Ch = 'heal'; $k.V = $App.Canvas.BrushValue; $k.R = 0.13
    $k.Pts.Add((New-Object System.Drawing.PointF 0.5, 0.53)); $App.Step.Strokes.Add($k)
    $script:T0 = [DateTime]::Now; Request-Render
}
St 'wait1' { }
St 'wait2' { }
St 'wait3' { }
St 'wait4' { }
St 'ai-shot' { & $script:Shot 'heal-ai'; $script:Trace.Add("device=$([PixTune.Lama]::Device) err=$([PixTune.Lama]::LastError)"); Apply-Tool }
St 'reopen' { Start-Tool 'healing' }
St 'reopen-check' {
    $sel = (& $script:FindTile 'AI (LaMa)').Selected
    $script:Trace.Add("reopened: AI tile selected=$sel brush=$($App.Canvas.BrushValue) (expect True, 2)")
    & $script:Click (& $script:FindTile 'Content-aware')
}
St 'ca-check' { $script:Trace.Add("after content-aware tile: brush=$($App.Canvas.BrushValue) setting=$($App.Settings.HealAi) (expect 1, False)"); Cancel-Tool }

Start-StRun -Log 'healmode.log' -Finish { if ($null -ne $script:SettingsBackup) { [IO.File]::WriteAllBytes($script:SettingsPath, $script:SettingsBackup) } else { Remove-Item $script:SettingsPath -ErrorAction SilentlyContinue } }
