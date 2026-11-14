# Runs the window tests one after the other in a single PixTune session:
#   powershell -STA -ExecutionPolicy Bypass -File PixTune.ps1 <photo> -SelfTest <folder> -TestScript Suite.ps1
# Each test writes its log in <folder>\<test>; suite.log sums them up. Between tests the session is put back to
# a clean start (tool cancelled; edits, history and saved masks cleared; settings as they were); the photo is not
# reloaded, so what was computed from it (AI masks) serves the next tests. Generative fill (Stable Diffusion) is not part of it (ExpandTest skips it unless asked).
# PIXTUNE_SUITE can name the tests to run, comma separated.
$script:SuiteOut = $SelfTest
$script:SuitePhoto = $App.Doc.Path
$script:SuiteTests = New-Object System.Collections.Queue
$names = if ($env:PIXTUNE_SUITE) { $env:PIXTUNE_SUITE -split ',' } else {
    'SelfTest', 'ClickTest', 'FeatureTest', 'ZoomTest', 'HealModeTest', 'ToneCurveTest', 'LookEditTest',
    'RawMaskTest', 'SelectiveZoneTest', 'MaskPanelTest', 'UndoTest', 'ExpandTest', 'LiveRegionTest', 'GpuCheckTest', 'GpuToolTest', 'StackHideTest', 'ProjectTest', 'MemoTest', 'DenoiseTest', 'LatencyTest', 'FeelTest', 'MapTest', 'SettingsTest', 'DepthTest', 'SkinTest'
}
foreach ($n in $names) { $script:SuiteTests.Enqueue($n.Trim()) }
$script:SuiteLog = New-Object System.Collections.Generic.List[string]
$script:SuiteSettings = if (Test-Path $script:SettingsPath) { [IO.File]::ReadAllBytes($script:SettingsPath) } else { $null }
$script:SuiteCurrent = $null
$script:SuiteClock = [Diagnostics.Stopwatch]::StartNew()
$script:SuiteTotal = [Diagnostics.Stopwatch]::StartNew()

# the first line of the test's log ("errors: N"), or what went wrong
function global:Get-SuiteResult([string]$dir) {
    $log = Get-ChildItem $dir -Filter *.log -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($log) { Get-Content $log.FullName -TotalCount 1 } else { 'no log' }
}

$script:SuiteNext = {
    if ($script:SuiteCurrent) {
        $script:SuiteLog.Add(('{0,-18} {1,4}s  {2}' -f $script:SuiteCurrent, [int]$script:SuiteClock.Elapsed.TotalSeconds, (Get-SuiteResult (Join-Path $script:SuiteOut $script:SuiteCurrent))))
    }
    if ($script:StTimer) { $script:StTimer.Stop(); $script:StTimer.Dispose(); $script:StTimer = $null }
    # back to a clean start
    if ($App.Mode -eq 'tool') { Cancel-Tool }
    if ($null -ne $script:SuiteSettings) { [IO.File]::WriteAllBytes($script:SettingsPath, $script:SuiteSettings) }
    $App.Settings = Get-Settings
    $script:ErrorLog.Clear()
    $App.ReturnTo = $null
    if ($script:SuiteTests.Count -eq 0) {
        $script:SuiteLog.Add(('total {0}s' -f [int]$script:SuiteTotal.Elapsed.TotalSeconds))
        $script:SuiteLog | Set-Content (Join-Path $script:SuiteOut 'suite.log')
        $script:SuiteNext = $null
        $App.Form.Close()
        return
    }
    $script:SuiteCurrent = $script:SuiteTests.Dequeue()
    # the same photo is not reloaded: its edits are cleared, and what was computed from it (AI masks) is kept
    if ($script:SuitePhoto -and $App.Doc.Path -eq $script:SuitePhoto) { $App.ResetEdits() } elseif ($script:SuitePhoto) { Open-Image $script:SuitePhoto }
    Show-Home 'tools'
    $App.Canvas.Fit()
    $SelfTest = Join-Path $script:SuiteOut $script:SuiteCurrent
    if (Test-Path $SelfTest) { Remove-Item $SelfTest -Recurse -Force }
    New-Item -ItemType Directory $SelfTest -Force | Out-Null
    $script:SuiteClock.Restart()
    try { . (Join-Path $Root "tests\$($script:SuiteCurrent).ps1") }
    catch { $script:ErrorLog.Add("[start] $($_.Exception.Message)"); (@("errors: $($script:ErrorLog.Count)") + $script:ErrorLog) | Set-Content (Join-Path $SelfTest 'start.log'); Complete-SelfTest }
}
Invoke-Later $script:SuiteNext @()
