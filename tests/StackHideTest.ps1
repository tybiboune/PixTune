# The edit stack's eye: a hidden step is skipped (the photo shows the steps below and above it), the row shows it,
# undo / redo switch it back, editing it shows it, projects keep it, and the export leaves it out too.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }
# largest difference between two pictures
function global:Get-ImgDiff($a, $b) {
    if ($a.W -ne $b.W -or $a.H -ne $b.H) { return 99 }
    $x = $a.D; $y = $b.D; $mx = 0.0
    for ($i = 0; $i -lt $x.Length; $i += 97) { $e = [Math]::Abs($x[$i] - $y[$i]); if ($e -gt $mx) { $mx = $e } }
    $mx
}
function global:Get-Row([int]$index) { foreach ($r in $App.Body.Controls) { if ($r -is [PixTune.StackRow] -and $r.Index -eq $index) { return $r } }; $null }
# the photo as the stack shows it, and as it would be with only some of the steps
function global:Get-Shown { $App.Doc.RenderPreviewSync($App.Doc.EffectiveSteps($true)) }
function global:Get-Without([int]$skip) {
    $l = New-Object 'System.Collections.Generic.List[PixTune.Step]'
    for ($i = 0; $i -lt $App.Doc.Steps.Count; $i++) { if ($i -ne $skip) { $s = $App.Doc.Steps[$i].Clone(); $s.Hidden = $false; $l.Add($s) } }
    $App.Doc.RenderPreviewSync($l)
}

St 'tune' { Start-Tool 'tune'; Set-Params @{ brightness = 40 } }
St 'tune-apply' { Apply-Tool }
St 'bw' { Start-Tool 'bw' }
St 'bw-apply' { Apply-Tool }
St 'vignette' { Start-Tool 'vignette'; Set-Params @{ outer = -60 } }
St 'vignette-apply' { Apply-Tool }
St 'stack' {
    Show-Stack
    $script:AllOn = Get-Shown
    $r = Get-Row 1
    Expect ($r.Glyphs.Length -eq 4 -and [int][char]$r.Glyphs[0] -eq 0xE890 -and -not $r.Faded) 'each step row has an eye, open'
}
St 'hide' { (Get-Row 1).RunAction(0) }
StWait 'hidden'
St 'hidden-check' {
    $s = $App.Doc.Steps[1]
    Expect $s.Hidden 'the eye hides Black & White'
    $r = Get-Row 1
    Expect ($r.Faded -and [int][char]$r.Glyphs[0] -eq 0xED1A -and $r.Title -like '*hidden*') 'its row is greyed with a crossed eye'
    $d = Get-ImgDiff (Get-Shown) (Get-Without 1)
    Expect ($d -lt 1e-5) ("the photo shows Tune and Vignette only (difference {0:N6})" -f $d)
    Expect ((Get-ImgDiff (Get-Shown) $script:AllOn) -gt 0.01) 'and differs from the photo with every step'
    $App.SaveScreenshot((Join-Path $script:StOut 'stack-hidden.png'))
}
St 'undo' { Invoke-Undo }
StWait 'undone'
St 'undo-check' {
    Expect (-not $App.Doc.Steps[1].Hidden) 'undo shows the step again'
    Expect ((Get-ImgDiff (Get-Shown) $script:AllOn) -lt 1e-5) 'with the photo as before'
    Invoke-Redo
}
StWait 'redone'
St 'redo-check' {
    Expect $App.Doc.Steps[1].Hidden 'redo hides it again'
    $p = [PixTune.Document]::ParseProject($App.Doc.ProjectJson())
    $l = [PixTune.Step]::ListFromJson($p[1])
    Expect ($l[1].Hidden -and -not $l[0].Hidden -and -not $l[2].Hidden) 'a saved project keeps which step is hidden'
    $full = [PixTune.Pipeline]::ApplyAll($App.Doc.Original, $App.Doc.Steps)
    $vis = New-Object 'System.Collections.Generic.List[PixTune.Step]'; $vis.Add($App.Doc.Steps[0].Clone()); $vis.Add($App.Doc.Steps[2].Clone())
    Expect ((Get-ImgDiff $full ([PixTune.Pipeline]::ApplyAll($App.Doc.Original, $vis))) -lt 1e-5) 'the export leaves the hidden step out'
    Invoke-StackAction 1 'edit'
}
StWait 'editing'
St 'edit-check' {
    Expect ($App.Mode -eq 'tool' -and -not $App.Doc.Pending.Hidden) 'editing a hidden step shows it while the tool is open'
    Cancel-Tool
}
StWait 'cancelled'
St 'cancel-check' {
    Expect $App.Doc.Steps[1].Hidden 'cancelling leaves it hidden'
    Show-Stack
    (Get-Row 1).RunAction(0)
}
StWait 'shown'
St 'shown-check' {
    Expect (-not $App.Doc.Steps[1].Hidden -and -not (Get-Row 1).Faded) 'the eye shows it again'
    Expect ((Get-ImgDiff (Get-Shown) $script:AllOn) -lt 1e-5) 'the photo is back to every step'
}
Start-StRun -Log 'stackhide.log'
