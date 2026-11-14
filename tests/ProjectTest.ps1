# Projects: saved with the photo, a project opens on "another computer" (the original folder deleted) with every
# step editable, hidden steps and saved masks kept, and the same picture; a linked project finds its photo next to
# it; projects from earlier versions still open; unpacked photos go when PixTune closes.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }
function global:Get-PjDiff($a, $b) {
    if ($a.W -ne $b.W -or $a.H -ne $b.H) { return 99 }
    $x = $a.D; $y = $b.D; $mx = 0.0
    for ($i = 0; $i -lt $x.Length; $i += 97) { $e = [Math]::Abs($x[$i] - $y[$i]); if ($e -gt $mx) { $mx = $e } }
    $mx
}
function global:Get-PjShown { $App.Doc.RenderPreviewSync($App.Doc.EffectiveSteps($true)) }

# "computer A": the photo in its own folder; "computer B": where the project travels
$script:PjA = Join-Path $script:StOut 'computer-A'; $script:PjB = Join-Path $script:StOut 'computer-B'
New-Item -ItemType Directory $script:PjA, $script:PjB -Force | Out-Null
$script:PjPhoto = Join-Path $script:PjA 'holiday photo.jpg'
Copy-Item $App.Doc.Path $script:PjPhoto

St 'open' {
    # brush strokes and painted masks survive the JSON every stack goes through (undo, projects, copied edits);
    # they were once lost there
    $s = New-Object PixTune.Step 'brush'
    $k = New-Object PixTune.Stroke; $k.Ch = 'dodge'; $k.V = 5; $k.R = 0.05; $k.Pts.Add((New-Object System.Drawing.PointF 0.3, 0.4)); $k.Pts.Add((New-Object System.Drawing.PointF 0.6, 0.5)); $s.Strokes.Add($k)
    $l = New-Object 'System.Collections.Generic.List[PixTune.Step]'; $l.Add($s)
    $back = [PixTune.Step]::ListFromJson([PixTune.Step]::ListToJson($l))
    Expect ($back[0].Strokes.Count -eq 1 -and $back[0].Strokes[0].Pts.Count -eq 2 -and $back[0].Sig() -eq $s.Sig()) 'strokes survive undo, projects and copied edits'
    Open-Image $script:PjPhoto
}
St 'tune' { Start-Tool 'tune'; Set-Params @{ brightness = 30; contrast = 20 } }
St 'tune-apply' { Apply-Tool }
St 'bw' { Start-Tool 'bw' }
St 'bw-apply' { Apply-Tool }
St 'vignette' { Start-Tool 'vignette'; Set-Params @{ outer = -50 } }
St 'vignette-apply' { Apply-Tool }
St 'save' {
    $App.Doc.SetHidden(1, $true)
    $m = New-Object 'PixTune.App+SavedMask'; $m.Name = 'Top half'
    $k = New-Object PixTune.Stroke; $k.Ch = 'mask'; $k.V = 1; $k.R = 0.2; $k.Pts.Add((New-Object System.Drawing.PointF 0.5, 0.25)); $m.Strokes.Add($k)
    $App.MaskLib.Add($m)
    $script:Before = Get-PjShown
    $script:Packed = Join-Path $script:PjB 'my work.pixtune'
    $script:Linked = Join-Path $script:PjA 'linked.pixtune'
    $App.SaveProjectTo($script:Packed, $true)
    $App.SaveProjectTo($script:Linked, $false)
    $kb = (Get-Item $script:Packed).Length / 1KB
    Expect ($kb -gt (Get-Item $script:PjPhoto).Length / 1KB) ("the project with the photo holds the photo ({0:N0} KB)" -f $kb)
    Expect ((Get-Item $script:Linked).Length -lt 20KB) 'the linked project is only the edits'
    # the old kind of project (version 1), as earlier versions wrote it
    Set-Content (Join-Path $script:PjA 'old.pixtune') $App.Doc.ProjectJson()
    # another computer: the photo's folder is gone
    Copy-Item $script:PjPhoto (Join-Path $script:PjB 'holiday photo.jpg')
    Copy-Item $script:Linked (Join-Path $script:PjB 'linked.pixtune')
    Copy-Item (Join-Path $script:PjA 'old.pixtune') (Join-Path $script:PjB 'old.pixtune')
    Remove-Item $script:PjA -Recurse -Force
    Open-Image (Join-Path $script:PjB 'holiday photo.jpg')
}
StWait 'other-photo'
St 'open-packed' { $App.OpenProject($script:Packed) }
StWait 'opened'
St 'packed-check' {
    $d = $App.Doc
    Expect ($d.Path -like "$([PixTune.App]::ProjectPhotos)*" -and (Split-Path $d.Path -Leaf) -eq 'holiday photo.jpg') 'the photo came out of the project'
    Expect ($d.Steps.Count -eq 3 -and $d.Steps[0].Tool -eq 'tune' -and $d.Steps[1].Tool -eq 'bw' -and $d.Steps[2].Tool -eq 'vignette') 'the three steps are back, in order'
    Expect ($d.Steps[1].Hidden -and -not $d.Steps[0].Hidden) 'the hidden step is still hidden'
    $ml = $App.MaskLib
    Expect ($ml.Count -eq 1 -and $ml[0].Name -eq 'Top half' -and $ml[0].Strokes.Count -eq 1) ("the saved mask is back (count {0}, name '{1}', strokes {2})" -f $ml.Count, $(if ($ml.Count) { $ml[0].Name }), $(if ($ml.Count) { $ml[0].Strokes.Count }))
    $diff = Get-PjDiff (Get-PjShown) $script:Before
    Expect ($diff -lt 1e-5) ("the same picture as before saving (difference {0:N6})" -f $diff)
    Expect ($App.ProjectFile -eq $script:Packed) 'saving again goes to the same project'
    Invoke-StackAction 0 'edit'
}
StWait 'editing'
St 'edit-check' {
    Expect ($App.Mode -eq 'tool' -and $App.Step.Tool -eq 'tune' -and $App.Step.F('brightness') -eq 30) 'a step opens in its tool with its settings'
    Cancel-Tool
    # saved again from the unpacked photo: still a project with the photo
    $again = Join-Path $script:PjB 'again.pixtune'
    $App.SaveProjectTo($again, $true)
    Expect ((Get-Item $again).Length -gt 50KB) 'saving again keeps the photo inside'
    $App.OpenProject((Join-Path $script:PjB 'linked.pixtune'))
}
StWait 'linked'
St 'linked-check' {
    Expect ($App.Doc.Path -eq (Join-Path $script:PjB 'holiday photo.jpg') -and $App.Doc.Steps.Count -eq 3) 'a linked project finds its photo next to it'
    $App.OpenProject((Join-Path $script:PjB 'old.pixtune'))
}
StWait 'old'
St 'old-check' {
    Expect ($App.Doc.Steps.Count -eq 3 -and $App.Doc.Steps[1].Hidden) 'a project from an earlier version still opens'
    [PixTune.Caches]::DeleteFiles()
    Expect (-not (Test-Path ([PixTune.App]::ProjectPhotos))) 'unpacked photos are removed with the other temporary files'
}
Start-StRun -Log 'project.log'
