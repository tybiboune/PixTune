# Compiles the C# program (engine\*.cs: image engine, window and tools) with the compiler that ships with
# Windows (.NET Framework) into a library, cached so later launches are instant.

function Import-PixTuneEngine {
    param([string]$Root)

    if ('PixTune.Img' -as [type]) { return }

    $sources = Get-ChildItem -Path (Join-Path $Root 'engine') -Filter '*.cs' | Sort-Object Name
    $sha = [System.Security.Cryptography.SHA1]::Create()
    $all = ($sources | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
    $hash = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($all)) | Select-Object -First 8 | ForEach-Object { $_.ToString('x2') })

    $cacheDir = Join-Path $env:LOCALAPPDATA 'PixTune\cache'
    if (-not (Test-Path $cacheDir)) { New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null }
    $dll = Join-Path $cacheDir "PixTune.Engine.$hash.dll"

    Add-Type -AssemblyName System.Drawing, System.Windows.Forms, PresentationCore, WindowsBase, System.Xaml, System.Web.Extensions, System.IO.Compression, System.IO.Compression.FileSystem

    if (-not (Test-Path $dll)) {
        Get-ChildItem $cacheDir -Filter 'PixTune.Engine.*.dll' -ErrorAction SilentlyContinue |
            ForEach-Object { try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch {} }
        $refs = @(
            'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
            'System.Web.Extensions.dll', 'System.Xaml.dll',
            [System.IO.Compression.ZipArchive].Assembly.Location,
            [System.IO.Compression.ZipFile].Assembly.Location,
            [System.Windows.Media.PixelFormats].Assembly.Location,   # PresentationCore
            [System.Windows.Threading.Dispatcher].Assembly.Location  # WindowsBase
        )
        Add-Type -Path ($sources.FullName) -ReferencedAssemblies $refs -OutputAssembly $dll -OutputType Library -ErrorAction Stop
    }
    Add-Type -Path $dll
    [PixTune.RawDecoder]::LibPath = Join-Path $Root 'native\libraw.dll'
    [PixTune.FaceMesh]::ModelPath = Join-Path $Root 'native\face_landmark_detector.onnx'
    [PixTune.FaceMesh]::RuntimePath = Join-Path $Root 'native\onnxruntime.dll'
    [PixTune.LivePortrait]::RuntimePath = Join-Path $Root 'native\onnxruntime.dll'
    [PixTune.LivePortrait]::MaskPath = Join-Path $Root 'native\liveportrait_mask.png'
    [PixTune.LivePortrait]::Dir = Join-Path $env:LOCALAPPDATA 'PixTune\ai\liveportrait'
}
