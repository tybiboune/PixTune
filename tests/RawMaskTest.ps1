# RAW Develop with the one-tap masks: the edit must change only the masked area.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
function global:StWait([string]$name) { $script:StSteps.Enqueue(@{ Name = $name; Wait = $true }) }
$script:Shot = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }
# average change between the photo and the edited result in the top and bottom fifth of the picture
$script:Change = {
    param([string]$label)
    $a = $App.Doc.Preview; $b = $App.Doc.RenderPreviewSync($App.Doc.Steps)
    if ($a.W -ne $b.W) { $script:Trace.Add("$label size differs"); return }
    $top = 0.0; $bot = 0.0; $n = 0
    for ($y = 0; $y -lt [int]($a.H / 5); $y += 3) { for ($x = 0; $x -lt $a.W; $x += 7) { $i = ($y * $a.W + $x) * 3; $top += [Math]::Abs($b.D[$i + 1] - $a.D[$i + 1]); $j = (($a.H - 1 - $y) * $a.W + $x) * 3; $bot += [Math]::Abs($b.D[$j + 1] - $a.D[$j + 1]); $n++ } }
    $s = $App.Doc.Steps[$App.Doc.Steps.Count - 1]
    $script:Trace.Add(("{0}: steps={1} last={2} maskOn={3} smart='{4}' invert={5} | change top={6:N3} bottom={7:N3}" -f $label, $App.Doc.Steps.Count, $s.Tool, $s.MaskOn, $s.Str('smartmask', ''), $s.MaskInvert, ($top / $n), ($bot / $n)))
}

foreach ($kind in 'sky', 'subject', 'background') {
    St "open-$kind" { $App.Doc.SetSteps((New-Object 'System.Collections.Generic.List[PixTune.Step]')); Start-Tool 'rawdev'; Set-Params @{ exposure = 80; saturation = 60 } }
    St "mask-$kind" { Open-MaskFor $script:Kinds.Dequeue() }
    StWait "wait-$kind"
    St "status-$kind" {
        $img = $App.Canvas.GetType().GetField('img', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($App.Canvas)
        $px = $img.GetPixel(800, 20); $ref = $App.Doc.RenderPreviewSync($App.Doc.EffectiveSteps($true)); $i = (20 * $ref.W + 800) * 3
        $script:Trace.Add("canvas pixel top: $($px.R),$($px.G),$($px.B) vs correct render: $([int](255*$ref.D[$i])),$([int](255*$ref.D[$i+1])),$([int](255*$ref.D[$i+2])) pending=$($App.PendingKind) renders=$($script:RenderCount)")
    }
    St "pause1-$kind" { }
    St "pause2-$kind" { }
    StWait "wait2-$kind"
    St "shot-$kind" { $k = $App.Step.Str('smartmask', ''); & $script:Shot "rawmask-$k"; $eff = $App.Doc.EffectiveSteps($true); $a = $App.Doc.Preview; $b = $App.Doc.RenderPreviewSync($eff); $d = 0.0; for ($i = 1; $i -lt $a.W * 40 * 3; $i += 97) { $d += [Math]::Abs($b.D[$i] - $a.D[$i]) }
    $script:Trace.Add("in mask editor: mode=$($App.Mode) tool=$($App.ToolDef.Id) smart='$k' editIndex=$($App.Doc.EditIndex) steps=$($App.Doc.Steps.Count) shown=$(($eff | % { $_.Tool + '/' + $_.MaskOn + '/' + $_.Str('smartmask','') }) -join ',') topchange=$([Math]::Round($d, 2)) canvas=$($App.Canvas.ImageSize) seg=$([PixTune.Segmenter]::Available) err='$([PixTune.Segmenter]::LastError)'"); Apply-Tool }
    St "check-$kind" { & $script:Change "after $kind" }
}
$script:RenderCount = 0
$script:RenderLog = { param($s, $e) $script:RenderCount++; if ($e.Image) { $i = (20 * $e.Image.W + 800) * 3; $script:Trace.Add("   render #$($script:RenderCount): top=$([int](255*$e.Image.D[$i])) full=$($e.Full) err='$($e.Error)' mode=$($App.Mode) pending=$(if ($App.Doc.Pending) { $App.Doc.Pending.Tool + '/' + $App.Doc.Pending.MaskOn + '/' + $App.Doc.Pending.Str('smartmask','') })") } else { $script:Trace.Add("   render #$($script:RenderCount): no image err='$($e.Error)'") } }
$App.Renderer.add_Rendered($script:RenderLog)
$script:Kinds = New-Object System.Collections.Queue
foreach ($k in 'sky', 'subject', 'background') { $script:Kinds.Enqueue($k) }

Start-StRun -Log 'rawmask.log' -Finish { $App.Renderer.remove_Rendered($script:RenderLog) }
