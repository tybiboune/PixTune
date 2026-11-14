# The Settings window is laid out by hand, control by control: this checks that what it puts there fits inside
# the window and that nothing is drawn on top of anything else. The daily update tick box used to sit inside the
# line above it and half under the OK button.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

St 'the settings window' {
    $t = New-Object System.Windows.Forms.Timer
    $t.Interval = 1200
    $t.add_Tick({
        $t.Stop()
        foreach ($f in [System.Windows.Forms.Application]::OpenForms) {
            if ($f.Text -ne 'Settings') { continue }
            $b = New-Object System.Drawing.Bitmap $f.Width, $f.Height
            $f.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $f.Width, $f.Height))
            $b.Save((Join-Path $script:StOut 'settings.png'), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
            $cs = $f.ClientSize
            $cl = @($f.Controls | Where-Object { $_.Visible })
            # everything inside the window, with a margin to spare
            $out = @($cl | Where-Object { $_.Left -lt 0 -or $_.Top -lt 0 -or $_.Right -gt $cs.Width -or $_.Bottom -gt $cs.Height })
            Expect ($out.Count -eq 0) ("every control is inside the window ({0} of {1} outside: {2})" -f $out.Count, $cl.Count, (($out | ForEach-Object { "'" + ($_.Text -replace "`r`n", ' ') + "'" }) -join ', '))
            # nothing on top of anything else
            $hits = New-Object System.Collections.Generic.List[string]
            for ($i = 0; $i -lt $cl.Count; $i++) {
                for ($j = $i + 1; $j -lt $cl.Count; $j++) {
                    $a = New-Object System.Drawing.Rectangle $cl[$i].Left, $cl[$i].Top, $cl[$i].Width, $cl[$i].Height
                    $c = New-Object System.Drawing.Rectangle $cl[$j].Left, $cl[$j].Top, $cl[$j].Width, $cl[$j].Height
                    if ($a.IntersectsWith($c)) { $hits.Add(("'{0}' over '{1}'" -f ($cl[$i].Text -replace "`r`n", ' '), ($cl[$j].Text -replace "`r`n", ' '))) }
                }
            }
            Expect ($hits.Count -eq 0) ("no control is drawn over another ({0} found{1})" -f $hits.Count, $(if ($hits.Count) { ': ' + ($hits -join '; ') } else { '' }))
            $script:Trace.Add(("window {0} x {1}, {2} controls" -f $cs.Width, $cs.Height, $cl.Count))
            $f.Close()
        }
    })
    $t.Start()
    $App.ShowSettingsDialog()
}
Start-StRun -Log 'settings.log'
