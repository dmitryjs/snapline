param([Parameter(Mandatory)][string]$Ffmpeg, [Parameter(Mandatory)][string]$Capture, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $Output 'Snapline'
if (Test-Path $stage) { throw "Choose an empty output directory." }
$tools = New-Item -ItemType Directory -Path "$stage/tools" -Force
Copy-Item $Ffmpeg "$tools/ffmpeg.exe"
Copy-Item $Capture "$tools/capture.exe"
$manifest = Join-Path $Output 'ToolHashes.txt'
@('capture.exe','ffmpeg.exe') | ForEach-Object { (Get-FileHash "$tools/$_" -Algorithm SHA256).Hash.ToLowerInvariant() + "  $_" } | Set-Content -Encoding ascii $manifest
$ref = "${env:ProgramFiles(x86)}/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8"
$sdk = (dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^([^ ]+) \[(.+)\]$') { throw 'Install the .NET SDK.' }
$compiler = Join-Path $Matches[2] "$($Matches[1])/Roslyn/bincore/csc.dll"
$flags = @('-nologo','-nostdlib','-target:winexe','-platform:x64','-optimize+','-deterministic+',"-out:$stage/Snapline.exe","-win32manifest:$root/windows/app.manifest","-win32icon:$root/brand/snapline.ico","-resource:$manifest,ToolHashes.txt","-resource:$root/brand/snapline.png,brand.png")
foreach ($lib in 'mscorlib','System','System.Core','System.Xaml','System.Xml','System.Drawing','System.Windows.Forms','WindowsBase','PresentationCore','PresentationFramework') { $flags += "-r:$ref/$lib.dll" }
foreach ($file in Get-ChildItem "$root/windows/*.xaml","$root/windows/art/*.png") { $flags += "-resource:$($file.FullName),$($file.Name)" }
$files = (Get-ChildItem "$root/windows/*.cs").FullName
& dotnet $compiler @flags @files
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed' }
Copy-Item "$root/release/README-Windows.txt" "$stage/README.txt"
Copy-Item "$root/release/licenses" "$stage/licenses" -Recurse
Push-Location $root
try {
    $metadata = cargo metadata --locked --format-version 1 --filter-platform x86_64-pc-windows-msvc
    if ($LASTEXITCODE -ne 0) { throw 'Dependency inventory failed' }
    $metadata | Set-Content -Encoding utf8 "$Output/metadata.json"
    python release/inventory.py "$Output/metadata.json" Cargo.lock $stage
    if ($LASTEXITCODE -ne 0) { throw 'Missing license notices' }
} finally { Pop-Location }
$zip = Join-Path $Output 'Snapline-Windows-0.9.1-x64.zip'
Compress-Archive -Path $stage -DestinationPath $zip
(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $zip -Leaf) | Set-Content -Encoding ascii "$Output/SHA256SUMS.txt"
