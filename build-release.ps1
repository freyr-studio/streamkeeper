# Builds a ready-to-use StreamKeeper release zip (exe + tools) in .\release
$ErrorActionPreference = "Stop"
$root  = $PSScriptRoot
$src   = Join-Path $root "src"
$tools = Join-Path $src "tools"
$out   = Join-Path $root "release\StreamKeeper"

# 1. Download yt-dlp and ffmpeg if missing
if (-not (Test-Path "$tools\yt-dlp.exe")) {
    Write-Host "Downloading yt-dlp..."
    Invoke-WebRequest "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" -OutFile "$tools\yt-dlp.exe"
}
if (-not (Test-Path "$tools\ffmpeg.exe")) {
    Write-Host "Downloading ffmpeg..."
    $zip = Join-Path $env:TEMP "ffmpeg.zip"; $tmp = Join-Path $env:TEMP "ffmpeg-tmp"
    Invoke-WebRequest "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" -OutFile $zip
    Expand-Archive $zip -DestinationPath $tmp -Force
    Copy-Item (Get-ChildItem $tmp -Recurse -Filter ffmpeg.exe | Select-Object -First 1).FullName $tools
    Remove-Item $zip, $tmp -Recurse -Force
}

# 2. Publish a self-contained single exe (tools stay next to it)
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$src\StreamKeeper.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out

# 3. Zip it, named with the version
[xml]$proj = Get-Content "$src\StreamKeeper.csproj"
$version = $proj.Project.PropertyGroup.Version
$zipPath = Join-Path $root "release\StreamKeeper-v$version-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path $out -DestinationPath $zipPath
Write-Host "Done: $zipPath"
