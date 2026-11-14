# The step runner shared by the window tests. A test queues its steps with St (an action), StWait (wait until the
# program is idle) and StUntil (wait for a condition), then calls Start-StRun. A step starts as soon as the program
# is idle (no render under way, no deferred action waiting): no fixed delays. Each step's duration is written next
# to the test's log (<log>.times), slowest first.
function global:St([string]$name, [scriptblock]$action) { $script:StSteps.Enqueue(@{ Name = $name; Action = $action }) }
function global:StWait([string]$name) { $script:StSteps.Enqueue(@{ Name = $name; Wait = $true }) }
function global:StUntil([string]$name, [scriptblock]$cond, [int]$timeoutMs = 120000) { $script:StSteps.Enqueue(@{ Name = $name; Until = $cond; Timeout = $timeoutMs }) }

function global:Test-StIdle { $App.Idle -and -not $App.PendingKind -and $global:PsQueue.Count -eq 0 }

# inside a step: lets the program finish what it is doing (renders, mask overlays) before reading its state
function global:Wait-Idle([int]$maxMs = 60000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-StIdle) -and $sw.ElapsedMilliseconds -lt $maxMs) { [System.Windows.Forms.Application]::DoEvents(); [Threading.Thread]::Sleep(5) }
}

# -Log: the log file name; -Snap: a screenshot (<step>.png) once each step is done; -Finish: run at the end (restore
# settings...); -Lines: the log lines (default: error count, errors, $script:Trace)
function global:Start-StRun([string]$Log, [switch]$Snap, [scriptblock]$Finish, [scriptblock]$Lines) {
    $script:StRun = @{ Log = $Log; Snap = [bool]$Snap; Finish = $Finish; Lines = $Lines; Last = $null; Clock = [Diagnostics.Stopwatch]::StartNew(); Since = 0.0
        Times = (New-Object System.Collections.Generic.List[string]); WaitStart = $null }
    $script:StTimer = New-Object System.Windows.Forms.Timer
    $script:StTimer.Interval = 30
    $script:StTimer.add_Tick({
            $r = $script:StRun
            # a step waiting with Wait-Idle lets events run: the clock must not start the next step meanwhile
            if ($r.InStep) { return }
            $st = if ($script:StSteps.Count) { $script:StSteps.Peek() } else { $null }
            # a step waiting for a condition does not need the program to be idle
            if ($st -and $st.Until) {
                if (-not $r.WaitStart) { $r.WaitStart = $r.Clock.ElapsedMilliseconds }
                $ok = $false; try { $ok = [bool](& $st.Until) } catch { }
                if (-not $ok -and $r.Clock.ElapsedMilliseconds - $r.WaitStart -lt $st.Timeout) { return }
                $r.WaitStart = $null
            }
            elseif (-not (Test-StIdle)) { return }
            # the previous step is done: its time and screenshot
            $now = $r.Clock.Elapsed.TotalMilliseconds
            if ($r.Last) {
                $r.Times.Add(('{0,7:N0} ms  {1}' -f ($now - $r.Since), $r.Last))
                if ($r.Snap) { $App.SaveScreenshot((Join-Path $script:StOut "$($r.Last).png")) }
            }
            if (-not $st) {
                $script:StTimer.Stop()
                if ($r.Finish) { try { & $r.Finish } catch { $script:ErrorLog.Add("[finish] $($_.Exception.Message)") } }
                $lines = if ($r.Lines) { & $r.Lines } else { @("errors: $($script:ErrorLog.Count)") + $script:ErrorLog + $script:Trace }
                $lines | Set-Content (Join-Path $script:StOut $r.Log)
                @(('total {0:N0} ms' -f $now)) + @($r.Times | Sort-Object -Descending) | Set-Content (Join-Path $script:StOut "$($r.Log).times")
                Complete-SelfTest
                return
            }
            [void]$script:StSteps.Dequeue()
            $r.Last = $st.Name; $r.Since = $now
            if ($st.Action) {
                $r.InStep = $true
                try { & $st.Action } catch { $script:ErrorLog.Add("[$($st.Name)] $($_.Exception.Message)`n$($_.ScriptStackTrace)") }
                finally { $r.InStep = $false }
            }
        })
    $script:StTimer.Start()
}
