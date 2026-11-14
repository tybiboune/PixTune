# Creates a PixTune shortcut on the desktop and in the Start menu.
$root = $PSScriptRoot
$sh = New-Object -ComObject WScript.Shell
$targets = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'PixTune.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'PixTune.lnk')
)
foreach ($t in $targets) {
    $l = $sh.CreateShortcut($t)
    $l.TargetPath = 'powershell.exe'
    $l.Arguments = "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `"$root\PixTune.ps1`""
    $l.WorkingDirectory = $root
    $l.IconLocation = "$env:WINDIR\System32\mspaint.exe,0"
    $l.Description = 'PixTune photo editor'
    $l.Save()
    "Created $t"
}
