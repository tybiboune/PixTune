<#
.SYNOPSIS
    PixTune - a desktop photo editor for Windows.
.DESCRIPTION
    Non-destructive editing with an editable stack of tools and filters, looks, masks, before/after compare and
    export. The program is C# (engine\*.cs), compiled by the C# compiler that ships with Windows into a library
    kept in %LOCALAPPDATA%\PixTune\cache (only when the code changes) and run inside this PowerShell process: no
    program file is created or started. Start it with PixTune.bat or:
        powershell -STA -ExecutionPolicy Bypass -File PixTune.ps1 [photo]
#>
param(
    [Parameter(Position = 0)][string]$Path,
    [string]$SelfTest,
    [string]$TestScript = 'SelfTest.ps1',
    # the copy that asked for this one to start, after an update: it is left to close first
    [int]$AfterPid = 0
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-STA', '-File', "`"$PSCommandPath`"")
    if ($Path) { $a += "`"$Path`"" }
    if ($AfterPid -gt 0) { $a += @('-AfterPid', $AfterPid) }
    Start-Process powershell.exe -ArgumentList $a
    return
}

# An update starts this copy and then closes the one that asked for it. Nothing of the new version is loaded
# until that one is gone, so there are never two PixTunes on screen and the old one cannot write its settings
# over what this one does. If it will not close, this one starts anyway rather than leaving the owner with
# nothing: a minute is far longer than closing takes.
if ($AfterPid -gt 0) {
    try {
        $old = Get-Process -Id $AfterPid -ErrorAction SilentlyContinue
        # Windows gives the number of a closed program to another one soon enough: only a PowerShell is waited for
        if ($old -and $old.ProcessName -like 'powershell*') { $null = $old.WaitForExit(60000) }
    } catch { }
}

. (Join-Path $Root 'lib\Engine.ps1')
Import-PixTuneEngine -Root $Root
[PixTune.Native]::SetProcessDPIAware() | Out-Null
[System.Windows.Forms.Application]::EnableVisualStyles()

if ($Path -and (Test-Path $Path)) { $Path = (Resolve-Path $Path).Path }
$App = [PixTune.App]::Create($Root)

# Window tests (tests\*.ps1) drive the running program: tests\Compat.ps1 gives them the commands they use.
if ($SelfTest) {
    $script:ErrorLog = $App.ErrorLog
    $script:SettingsPath = [PixTune.AppSettings]::FilePath
    $script:SuiteNext = $null
    # test runs do not start the GPU check of the AI models by themselves, and never load Stable Diffusion for it
    [PixTune.GpuCheck]::AtLaunch = $false
    [PixTune.GpuCheck]::SkipHeavy = $true
    # a test script calls this when it is finished: alone, it closes the window; run by tests\Suite.ps1, the next
    # test starts in the same session
    function Complete-SelfTest { if ($script:SuiteNext) { Invoke-Later $script:SuiteNext @() } else { $App.Form.Close() } }
    $App.add_Started({
            try {
                . (Join-Path $Root 'tests\Compat.ps1')
                . (Join-Path $Root "tests\$TestScript")
            } catch {
                $App.ErrorLog.Add("$($_.Exception.Message)`n$($_.ScriptStackTrace)")
                if (-not (Test-Path $SelfTest)) { New-Item -ItemType Directory $SelfTest -Force | Out-Null }
                $App.ErrorLog | Set-Content (Join-Path $SelfTest 'startup-error.log')
                $App.Form.Close()
            }
        })
}

$App.Run($Path)
