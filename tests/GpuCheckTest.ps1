# GPU check of the AI models: two small models are tried on the graphics card, the results are kept in the
# settings, a model marked as failing opens on the processor, and a memory shortage marks nothing.
# Only small models: Stable Diffusion is never loaded by tests. The results found before the test are put back.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
$script:GcSaved = [PixTune.GpuCheck]::Save()
$script:GcFiles = [string[]]@('masks\detectron2_semantic.onnx', 'esrgan\real_esrgan_x4plus.onnx')
$script:GcRoot = [PixTune.GpuCheck]::Root
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }
function global:Get-GcState([string]$file) { $r = ''; $s = [PixTune.GpuCheck]::State((Join-Path $script:GcRoot $file), [ref]$r); "$s $r".Trim() }

St 'start' {
    $script:Trace.Add("graphics card: $([PixTune.Gpu]::Adapter) directml=$([PixTune.Ort]::HasDirectML)")
    Expect ([PixTune.GpuCheck]::Start($true, $script:GcFiles, $null, $null)) 'the check starts'
}
StUntil 'checked' { -not [PixTune.GpuCheck]::Running } 120000
St 'results' {
    Wait-Idle
    foreach ($f in $script:GcFiles) {
        $s = Get-GcState $f
        $script:Trace.Add("  $f -> $s")
        Expect ($s -match '^(gpu|cpu)') "$f has a result"
    }
    Expect (@($App.Settings.GpuModels | Where-Object { $_ -like 'esrgan*' }).Count -eq 1) 'the result is in the settings'
    $script:Trace.Add("summary: $([PixTune.GpuCheck]::Summary())")
}
St 'fail' {
    $p = Join-Path $script:GcRoot 'esrgan\real_esrgan_x4plus.onnx'
    [PixTune.GpuCheck]::Failed($p, 'test: simulated failure')
    Expect ((Get-GcState 'esrgan\real_esrgan_x4plus.onnx') -eq 'cpu test: simulated failure') 'a failing model is marked for the processor'
    $s = [PixTune.OrtSession]::Open($p, $true, 1)
    Expect ($s.Device -eq 'CPU') "it opens on the processor (device=$($s.Device))"
    $s.Dispose()
}
St 'memory' {
    $before = Get-GcState 'denoise\nafnet_denoise.onnx'
    [PixTune.GpuCheck]::Failed((Join-Path $script:GcRoot 'denoise\nafnet_denoise.onnx'), 'Exception: 8007000E Not enough memory resources')
    Expect ((Get-GcState 'denoise\nafnet_denoise.onnx') -eq $before) 'a memory shortage changes nothing'
}
St 'saved' {
    Wait-Idle
    $l = [PixTune.AppSettings]::Load()
    Expect (@($l.GpuModels | Where-Object { $_ -like 'esrgan*|cpu|test: simulated failure' }).Count -eq 1) 'the failure is saved in settings.json'
}
Start-StRun -Log 'gpucheck.log' -Finish {
    [PixTune.GpuCheck]::Load($script:GcSaved)
    $App.Settings.GpuModels = $script:GcSaved
    $App.Settings.Save()
}
