# Expand: Smart and Generative fills in the window, then the export reuses the generated border.
# The user's settings file is restored at the end. Generative fill (Stable Diffusion) and High detail are slow:
# they only run when the environment variable PIXTUNE_TEST_GENERATIVE is 1; otherwise Smart fill is exported.
$script:Gen = $env:PIXTUNE_TEST_GENERATIVE -eq '1'
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:SettingsBackup = if (Test-Path $script:SettingsPath) { [IO.File]::ReadAllBytes($script:SettingsPath) } else { $null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
# a step that only finishes once the canvas has its picture (generation can take a minute)
function global:StWait([string]$name) { $script:StSteps.Enqueue(@{ Name = $name; Wait = $true }) }
$script:Shot = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }
$script:FindTile = { param([string]$caption) foreach ($grp in $App.Body.Controls) { foreach ($c in $grp.Controls) { if ($c -is [PixTune.Tile] -and $c.Caption -eq $caption) { return $c } } } }
$script:Click = {
    param($t)
    $ev = [System.Windows.Forms.MouseEventArgs]::new([System.Windows.Forms.MouseButtons]::Left, 1, 5, 5, 0)
    $argv = [object[]]::new(1); $argv[0] = [System.EventArgs]$ev
    $t.GetType().GetMethod('OnClick', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($t, $argv) | Out-Null
}

St 'open' { Start-Tool 'expand'; Set-Params @{ left = 40 }; $script:T0 = [DateTime]::Now }
St 'second-side' {
    Set-Params @{ right = 10 }
    $script:Trace.Add("left=$($App.Step.F('left', 0)) right=$($App.Step.F('right', 0)) right slider enabled=$($App.Sliders['right'].Enabled) (expect 40 stored, shown 25; right 0; False)")
}
StWait 'smart-wait'
St 'smart-shot' {
    $script:Trace.Add("smart: $([int]([DateTime]::Now - $script:T0).TotalSeconds) s, gen controls visible=$(@($App.ExpandGen | Where-Object { $_.Visible }).Count) (expect 0)")
    & $script:Shot 'expand-smart'
}
if ($script:Gen) {
    St 'gen-start' { & $script:Click (& $script:FindTile 'Generative'); $script:T0 = [DateTime]::Now }
    St 'gen-check' { $script:Trace.Add("generative: fill=$($App.Step.I('fill', 0)) installed=$([PixTune.Diffusion]::Installed) gen controls visible=$(@($App.ExpandGen | Where-Object { $_.Visible }).Count) (expect 4, 3)") }
    StWait 'gen-wait'
    St 'gen-shot' {
        $script:Trace.Add("generated in $([int]([DateTime]::Now - $script:T0).TotalSeconds) s on $([PixTune.Diffusion]::Device) err='$([PixTune.Diffusion]::LastError)'")
        & $script:Shot 'expand-generative'
    }
}
else { St 'gen-skipped' { $script:Trace.Add('generative fill and high detail skipped (set PIXTUNE_TEST_GENERATIVE=1 to run them)') } }
if ($script:Gen) { St 'export' {
    $hdBtn = foreach ($row in $App.Body.Controls) { foreach ($c in $row.Controls) { if ("$($c.Text)" -eq 'High detail') { $c } } }
    $script:Trace.Add("high detail step=$($App.Step.I('hd', 1)) button found=$([bool]$hdBtn) upscaler installed=$([PixTune.Upscaler]::Installed) (expect 1, True, True)")
    Apply-Tool
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $full = [PixTune.Pipeline]::ApplyAll($App.Doc.Original, $App.Doc.Steps)
    $script:Trace.Add("full resolution render $($full.W)x$($full.H) with high detail in $([int]$sw.Elapsed.TotalSeconds) s ($([PixTune.HighDetail]::LastTiming))")
    $sw.Restart(); $full = [PixTune.Pipeline]::ApplyAll($App.Doc.Original, $App.Doc.Steps)
    $script:Trace.Add("second full render in $([int]$sw.Elapsed.TotalSeconds) s (expect a few seconds: detail reused)")
    $full.FitInside(1600).ToBitmap().Save((Join-Path $script:StOut 'expand-export.png'))
} }
else {
    St 'export' {
        Apply-Tool
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $full = [PixTune.Pipeline]::ApplyAll($App.Doc.Original, $App.Doc.Steps)
        $script:Trace.Add("full resolution render $($full.W)x$($full.H) with smart fill in $([int]$sw.Elapsed.TotalSeconds) s")
        $full.FitInside(1600).ToBitmap().Save((Join-Path $script:StOut 'expand-export.png'))
    }
}
Start-StRun -Log 'expand.log' -Finish { if ($null -ne $script:SettingsBackup) { [IO.File]::WriteAllBytes($script:SettingsPath, $script:SettingsBackup) } else { Remove-Item $script:SettingsPath -ErrorAction SilentlyContinue } }
