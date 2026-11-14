# Favorites, live histogram, Crop & Rotate, smart masks and batch editing.
# The user's settings file is restored at the end (these features save into it).
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:SettingsBackup = if (Test-Path $script:SettingsPath) { [IO.File]::ReadAllBytes($script:SettingsPath) } else { $null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
$script:Shot = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }
$script:FindTile = { param([string]$caption) foreach ($grp in $App.Body.Controls) { foreach ($c in $grp.Controls) { if ($c -is [PixTune.Tile] -and $c.Caption -eq $caption) { return $c } } } }
$script:RightClick = {
    param($t)
    $ev = [System.Windows.Forms.MouseEventArgs]::new([System.Windows.Forms.MouseButtons]::Right, 1, 5, 5, 0)
    $argv = [object[]]::new(1); $argv[0] = [System.EventArgs]$ev
    $t.GetType().GetMethod('OnClick', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($t, $argv) | Out-Null
}

St 'pin' { Show-Home 'tools'; $App.Settings.Favorites = @(); & $script:RightClick (& $script:FindTile 'Curves') }
St 'pin-check' {
    $first = ($App.Body.Controls | Where-Object { $_ -is [System.Windows.Forms.Label] } | Select-Object -First 1).Text
    $script:Trace.Add("favorites=$(@($App.Settings.Favorites) -join ',') firstSection='$first' (expect curves, FAVORITES)"); & $script:Shot 'favorites'
}
St 'unpin' { & $script:RightClick (& $script:FindTile 'Curves') }
St 'unpin-check' { $script:Trace.Add("after unpin favorites='$(@($App.Settings.Favorites) -join ',')' (expect empty)") }
St 'hist' { Switch-Histogram }
St 'hist-check' { $script:Trace.Add("histogram shown=$($App.Canvas.ShowHistogram)"); & $script:Shot 'histogram'; Switch-Histogram }
St 'crop' { Start-Tool 'crop'; Set-Params @{ angle = 8 } }
St 'crop-shot' { & $script:Shot 'crop-rotate'; $App.Canvas.CropRect = New-Object System.Drawing.RectangleF 0.1, 0.1, 0.8, 0.8 }
St 'crop-apply' { Apply-Tool }
St 'crop-check' {
    $s = $App.Doc.Steps[$App.Doc.Steps.Count - 1]
    $out = $App.Doc.RenderPreviewSync($App.Doc.Steps)
    $script:Trace.Add("crop step: tool=$($s.Tool) angle=$($s.F('angle')) nocrop=$($s.P.ContainsKey('nocrop')) result=$($out.W)x$($out.H) from $($App.Doc.Preview.W)x$($App.Doc.Preview.H)")
}
St 'mask' { Start-Tool 'bw'; Open-MaskFor 'sky' }
St 'mask-shot' { Update-MaskView; Wait-Idle; & $script:Shot 'smart-mask' }
St 'mask-apply' { Apply-Tool }
St 'mask-check' { $s = $App.Doc.Steps[$App.Doc.Steps.Count - 1]; $script:Trace.Add("masked step: tool=$($s.Tool) maskOn=$($s.MaskOn) smartmask=$($s.Str('smartmask', '')) mode=$($App.Mode)") }
St 'batch' {
    $script:BatchDir = Join-Path $script:StOut 'batch'; New-Item -ItemType Directory $script:BatchDir -Force | Out-Null
    $script:BatchDone = $false
    $job = New-Object PixTune.BatchJob
    $job.add_Progress({ param($s, $e) $script:Trace.Add("batch $($e.Done)/$($e.Total): $([IO.Path]::GetFileName($e.Output)) $($e.Error)"); if ($e.Done -eq $e.Total) { $script:BatchDone = $true } })
    $job.Start($App.Form, @('C:\Windows\Web\Screen\img100.jpg', 'C:\Windows\Web\Screen\img101.jpg'), $App.Doc.Steps, $script:BatchDir, 'jpg', 90, 1600, $false)
}
StUntil 'batch-wait' { $script:BatchDone } 60000
St 'batch-check' { $script:Trace.Add("batch finished=$($script:BatchDone) files=$((Get-ChildItem $script:BatchDir).Count)") }

Start-StRun -Log 'feature.log' -Finish { if ($null -ne $script:SettingsBackup) { [IO.File]::WriteAllBytes($script:SettingsPath, $script:SettingsBackup) } else { Remove-Item $script:SettingsPath -ErrorAction SilentlyContinue } }
