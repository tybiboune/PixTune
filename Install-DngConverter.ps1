# Installs Adobe DNG Converter (free, by Adobe), which PixTune uses for RAW files LibRaw cannot read yet,
# such as the Nikon Z6III "High Efficiency" NEFs. Run by PixTune.bat when the converter is missing; run it by
# hand to be asked again after answering No. The installer always comes from Adobe and is checked first.
# -Auto (used by PixTune.bat): do not ask again after a previous "No".
param([switch]$Auto)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$marker = Join-Path $env:APPDATA 'PixTune\dng-converter-declined'
function Test-Converter {
    foreach ($root in $env:ProgramFiles, ${env:ProgramFiles(x86)}) {
        if ($root -and (Test-Path (Join-Path $root 'Adobe\Adobe DNG Converter\Adobe DNG Converter.exe'))) { return $true }
    }
    $k = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Adobe DNG Converter.exe' -ErrorAction SilentlyContinue
    return [bool]($k -and $k.'(default)' -and (Test-Path $k.'(default)'.Trim('"')))
}

if (Test-Converter) { 'Adobe DNG Converter is installed.'; exit 0 }
if ($Auto -and (Test-Path $marker)) { exit 0 }

$msg = "PixTune opens most RAW files by itself. For a few it needs Adobe DNG Converter, Adobe's free tool:" +
    " the Nikon Z6III, Z8, Z9 and Z f in ""High Efficiency"" mode, and cameras newer than PixTune's RAW decoder.`n`n" +
    "Download it from Adobe and install it now? (about 1.7 GB; Windows will ask for administrator approval)`n`n" +
    "No: PixTune still works, and opens those files as the full-size JPEG stored in them. Run Install-DngConverter.ps1 later to install it."
$r = [System.Windows.Forms.MessageBox]::Show($msg, 'PixTune - Adobe DNG Converter', 'YesNo', 'Question')
if ($r -ne 'Yes') {
    New-Item -ItemType Directory (Split-Path $marker) -Force | Out-Null
    Set-Content $marker -Value (Get-Date -Format s)
    exit 0
}
Remove-Item $marker -ErrorAction SilentlyContinue

if (Get-Command winget -ErrorAction SilentlyContinue) {
    # winget downloads Adobe's current release and checks it against the hash in its catalogue
    'Installing Adobe DNG Converter with winget (download about 1.7 GB, then Windows asks for approval)...'
    & winget install --id Adobe.DNGConverter --exact --silent --accept-package-agreements --accept-source-agreements
}
else {
    # no winget: Adobe's 18.7 installer, checked against its known fingerprint and Adobe's signature
    $url = 'https://download.adobe.com/pub/adobe/dng/win/AdobeDNGConverter_x64_18_7.exe'
    $sha = '360B392693999094AC6D22E863DB297FB5AD299F767120F08C548EE2138CC90E'
    $dir = Join-Path $env:TEMP 'PixTune-dng'
    New-Item -ItemType Directory $dir -Force | Out-Null
    $exe = Join-Path $dir 'AdobeDNGConverter_x64_18_7.exe'
    'Downloading Adobe DNG Converter 18.7 from download.adobe.com (about 1.7 GB)...'
    & curl.exe -L --progress-bar -A 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' -o $exe $url
    if ((Get-FileHash $exe).Hash -ne $sha) { Remove-Item $exe; throw 'The download is not the expected Adobe installer.' }
    $sig = Get-AuthenticodeSignature $exe
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Adobe Inc\.') { Remove-Item $exe; throw 'The installer is not signed by Adobe.' }
    'Installing (Windows asks for approval)...'
    $p = Start-Process -FilePath $exe -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Verb RunAs -PassThru
    $p.WaitForExit()
    Remove-Item $exe -ErrorAction SilentlyContinue
}

if (Test-Converter) { 'Adobe DNG Converter is installed: PixTune now develops those RAW files at full quality.'; exit 0 }
[System.Windows.Forms.MessageBox]::Show('Adobe DNG Converter was not installed (the approval may have been declined). PixTune still works; run Install-DngConverter.ps1 to try again.', 'PixTune', 'OK', 'Warning') | Out-Null
exit 1
