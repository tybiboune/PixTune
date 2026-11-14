# Mask panel: always under the tone curve, works in place in any adjustment tool, shows the mask in red while it
# is defined, hides it when a setting changes, folds, and is not offered by tools that move pixels.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
function global:StWait([string]$name) { $script:StSteps.Enqueue(@{ Name = $name; Wait = $true }) }
$script:Shot = { param($n) $App.SaveScreenshot((Join-Path $script:StOut "$n.png")) }

$script:Flags = [Reflection.BindingFlags]'Instance, NonPublic'
function global:Invoke-CanvasDrag($from, $to) {
    $c = $App.Canvas
    $ev = { param($n, $p) [PixTune.ImageCanvas].GetMethod($n, $script:Flags).Invoke($c, [object[]]@([System.Windows.Forms.MouseEventArgs]::new([System.Windows.Forms.MouseButtons]::Left, 1, [int]$p.X, [int]$p.Y, 0))) }
    & $ev 'OnMouseDown' $from
    for ($k = 1; $k -le 8; $k++) { & $ev 'OnMouseMove' (New-Object System.Drawing.PointF ($from.X + ($to.X - $from.X) * $k / 8), ($from.Y + ($to.Y - $from.Y) * $k / 8)) }
    & $ev 'OnMouseUp' $to
}
function global:Get-State {
    Wait-Idle
    $s = $App.Step
    "mode=$($App.Canvas.Mode) paint=$($App.MaskPaint) view=$($App.MaskView) overlay=$($null -ne [PixTune.ImageCanvas].GetField('mask', $script:Flags).GetValue($App.Canvas)) " +
    "maskOn=$($s.MaskOn) smart='$($s.Str('smartmask', ''))' strokes=$($s.Mask.Count) invert=$($s.MaskInvert) dock=$($maskDock.Height) body=$($maskBody.Visible) '$($maskState.Text)'"
}

StWait 'load'
St 'home' { $script:Trace.Add("home: dock visible=$($maskDock.Visible) height=$($maskDock.Height) '$($maskState.Text)' (expect folded bar, open a tool)") }
St 'tune' {
    if ($App.Settings.MaskFolded) { Switch-MaskFold }
    Start-Tool 'tune'; Set-Params @{ brightness = 60 }
    $script:Trace.Add("tune open: $(Get-State) (expect no mask, panel open)")
}
StWait 'r1'
St 'erase' {
    Set-MaskPaint 0
    $script:Trace.Add("erase on: $(Get-State) (expect Brush, paint=0, smart all, overlay)")
    $w = $App.Canvas.Width; $h = $App.Canvas.Height
    Invoke-CanvasDrag (New-Object System.Drawing.PointF ($w * 0.2), ($h * 0.5)) (New-Object System.Drawing.PointF ($w * 0.8), ($h * 0.5))
    $script:Trace.Add("after a stroke: $(Get-State) (expect 1 stroke)")
    $script:Trace.Add("stored check marks: $(($App.MaskTiles.Controls | ForEach-Object { "$($_.Caption)=$($_.Stored)" }) -join ' ') (expect Photo=True only)")
}
StWait 'r2'
St 'shot-red' { Update-MaskView; & $script:Shot 'mask-defining' }
St 'setting' {
    $App.Sliders['contrast'].SetValue(30, $true)
    $script:Trace.Add("after a slider: $(Get-State) (expect Adjust, paint=-1, view False, no overlay, mask kept)")
}
StWait 'r3'
St 'shot-hidden' { & $script:Shot 'mask-hidden' }
St 'invert' {
    $App.MaskButtons[2].Toggled = $true; Invoke-Later { Show-MaskEdit } @(); $App.Step.MaskInvert = $true
    $script:Trace.Add("invert: $(Get-State)")
}
StWait 'r4'
St 'fold' {
    Switch-MaskFold; $f = $maskDock.Height
    Switch-MaskFold; $u = $maskDock.Height
    $script:Trace.Add("folded height=$f unfolded=$u (expect bar only, then full)")
    Apply-Tool
}
St 'applied' {
    $s = $App.Doc.Steps[$App.Doc.Steps.Count - 2]; if ($s.Tool -ne 'tune') { $s = $App.Doc.Steps[$App.Doc.Steps.Count - 1] }
    $script:Trace.Add("applied: tool=$($s.Tool) maskOn=$($s.MaskOn) smart='$($s.Str('smartmask', ''))' strokes=$($s.Mask.Count) invert=$($s.MaskInvert) | panel '$($maskState.Text)' body=$($maskBody.Visible)")
    Start-Tool 'crop'
    $script:Trace.Add("crop: '$($maskState.Text)' body=$($maskBody.Visible) height=$($maskDock.Height) (expect not used, bar only)")
    Open-MaskFor 'all'
    $script:Trace.Add("mask asked in crop: maskOn=$($App.Step.MaskOn) (expect False)")
    Cancel-Tool
}
St 'bw-sky' {
    Start-Tool 'bw'; Open-MaskFor 'sky'
    $script:Trace.Add("bw sky: $(Get-State)")
}
StWait 'r5'
St 'shot-sky' { & $script:Shot 'mask-sky'; Apply-Tool }
# Pick one: a click selects the object under it (the red ellipse of the test photo near 0.612, 0.144)
St 'pick' {
    Start-Tool 'tune'; Set-Params @{ saturation = -100 }
    Open-MaskFor 'pick'
    $script:Trace.Add("pick mode: $(Get-State) installed=$([PixTune.SubjectPicker]::Installed) (expect mode Pick, paint=2)")
    $p = [PixTune.ImageCanvas].GetMethod('ToScreen', $script:Flags).Invoke($App.Canvas, [object[]]@([System.Drawing.PointF]::new(0.612, 0.144)))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-CanvasDrag $p $p
    $script:Trace.Add("after a click: $(Get-State) pts='$($App.Step.Str('pickpts', ''))' in $($sw.ElapsedMilliseconds) ms err='$([PixTune.SubjectPicker]::LastError)'")
    $m = $App.MaskOf($App.Step); $c = [Linq.Enumerable]::Sum([float[]]$m)
    $script:Trace.Add(("picked area {0:P2} of the photo (expect about 0.6 %)" -f ($c / $m.Length)))
}
StWait 'r6'
St 'pick-second' {
    # a second click adds another part (the pink ellipse at the bottom right)
    $p = [PixTune.ImageCanvas].GetMethod('ToScreen', $script:Flags).Invoke($App.Canvas, [object[]]@([System.Drawing.PointF]::new(0.9435, 0.85)))
    Invoke-CanvasDrag $p $p
    $m = $App.MaskOf($App.Step); $c = [Linq.Enumerable]::Sum([float[]]$m)
    $script:Trace.Add(("two parts: pts='{0}' area {1:P2} marks={2} '{3}' (expect 2 points, larger area, 2 marks)" -f $App.Step.Str('pickpts', ''), ($c / $m.Length), $App.Canvas.Marks.Count, $maskState.Text))
}
StWait 'r7'
St 'shot-pick' { & $script:Shot 'mask-pick' }
St 'pick-cut' {
    # a part clicked with Ctrl is cut out: here the first one again, so only the second part is left
    $App.Step.SetS('pickpts', $App.Step.Str('pickpts', '') + ';0.612,0.144,0'); Update-PickedMask
    $m = $App.MaskOf($App.Step); $c = [Linq.Enumerable]::Sum([float[]]$m)
    $script:Trace.Add(("after a cut: area {0:P2} '{1}' (expect the second part only)" -f ($c / $m.Length), $maskState.Text))
    Undo-MaskPick
    $script:Trace.Add("undo click: pts='$($App.Step.Str('pickpts', ''))' (expect 2 points)")
    Set-MaskPaint -1
    $script:Trace.Add("done: mode=$($App.Canvas.Mode) paint=$($App.MaskPaint) (expect Adjust, -1)")
}
St 'save' {
    Save-CurrentMask -Name 'Two blobs'
    $script:Trace.Add("saved: library=$($App.MaskLib.Count) rows=$($App.MaskLibBox.Controls.Count) on=$((Get-MaskExtras $App.Step).Count) own='$(Get-MaskSummary $App.Step)' '$($maskState.Text)' (expect 1, 1, 1, '', Two blobs)")
    $App.Sliders['saturation'].SetValue(-90, $true)
    Apply-Tool
    $s = $App.Doc.Steps | Where-Object { $_.Str('maskextra', '') } | Select-Object -Last 1
    $script:Trace.Add("applied: tool=$($s.Tool) label='$(Get-MaskLabel $s)'")
}
# several masks at once on Black & White: a new Sky mask saved, then the saved blobs switched on too
St 'combine' {
    Start-Tool 'bw'
    $script:Trace.Add("bw opened: maskOn=$($App.Step.MaskOn) rows=$($App.MaskLibBox.Controls.Count) (expect False, 1)")
    Set-MaskKind 'sky'; Save-CurrentMask -Name 'Sky'
    $App.MaskLibBox.Controls[0].RunAction(3)
}
StWait 'r8'
St 'combine-check' {
    $m = $App.MaskOf($App.Step); $c = [Linq.Enumerable]::Sum([float[]]$m)
    $sel = ($App.MaskLibBox.Controls | ForEach-Object { "$($_.Title)=$($_.Selected)" }) -join ' '
    $script:Trace.Add(("two saved masks on: '{0}' rows {1} area {2:P1} (expect Sky + Two blobs, both outlined)" -f $maskState.Text, $sel, ($c / $m.Length)))
    & $script:Shot 'mask-combined'
    Set-MaskKind 'subject' -Toggle; Set-MaskKind 'background' -Toggle
    $script:Trace.Add("plus two kinds (Ctrl+click): own='$(Get-MaskSummary $App.Step)' checks=$(($App.MaskTiles.Controls | Where-Object Stored | ForEach-Object Caption) -join ',')")
    Set-MaskKind 'background' -Toggle
    Switch-SavedMask 1
    $script:Trace.Add("Sky off again, background out: '$($maskState.Text)' (expect Two blobs + subject)")
    Show-SavedMask 0; Wait-Idle
    $script:Trace.Add("eye on: lit=$($App.MaskLibBox.Controls[0].Lit) overlay=$($null -ne [PixTune.ImageCanvas].GetField('mask', $script:Flags).GetValue($App.Canvas)) (expect 0, True)")
    Rename-SavedMask 0 'Blobs'
    $script:Trace.Add("renamed: '$($App.MaskLib[0].Name)' '$($maskState.Text)' (expect Blobs, Blobs + subject)")
    Apply-Tool
    $s = $App.Doc.Steps | Where-Object { $_.Tool -eq 'bw' } | Select-Object -Last 1
    Show-Stack
    $script:Trace.Add("stack: '$(Get-MaskLabel $s)'")
}
St 'stack-shot' { & $script:Shot 'mask-stack' }
St 'delete' {
    Show-Home 'tools'; Start-Tool 'tune'
    $App.MaskLibBox.Controls[0].RunAction(3)
    Remove-SavedMask 0
    $script:Trace.Add("deleted while on: library=$($App.MaskLib.Count) rows=$($App.MaskLibBox.Controls.Count) maskOn=$($App.Step.MaskOn) (expect 1, 1, False)")
    Save-CurrentMask -Name 'ignored'   # no mask on this tool: nothing to save
    Set-MaskKind 'all'; Save-CurrentMask -Name 'Everything'
    $script:Trace.Add("saved again: library=$($App.MaskLib.Count) (expect 2)")
    Cancel-Tool
    Open-Image $App.Doc.Path
    $script:Trace.Add("another photo opened: library=$($App.MaskLib.Count) (expect 0)")
}
Start-StRun -Log 'maskpanel.log'
