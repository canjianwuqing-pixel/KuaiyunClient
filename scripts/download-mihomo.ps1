param(
    [string]$Version = "latest",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot "src\KuaiyunClient\core\mihomo.exe"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot $OutputPath
}

$releaseUrl = if ($Version -eq "latest") {
    "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest"
}
else {
    "https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/$Version"
}

$headers = @{
    "Accept"     = "application/vnd.github+json"
    "User-Agent" = "KuaiyunClient-Build"
}

Write-Host "Reading Mihomo release: $releaseUrl"
$release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers
$assets = @($release.assets)

$asset = $assets |
    Where-Object { $_.name -match '^mihomo-windows-amd64-compatible-.*\.zip$' } |
    Select-Object -First 1

if ($null -eq $asset) {
    $asset = $assets |
        Where-Object {
            $_.name -match '^mihomo-windows-amd64-.*\.zip$' -and
            $_.name -notmatch 'amd64-v3'
        } |
        Select-Object -First 1
}

if ($null -eq $asset) {
    throw "The release does not contain a compatible Windows amd64 ZIP asset."
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("kuaiyun-mihomo-" + [Guid]::NewGuid())
$archivePath = Join-Path $tempDirectory $asset.name
$extractPath = Join-Path $tempDirectory "extract"

try {
    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

    Write-Host "Downloading $($asset.name)"
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $archivePath

    Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force

    $executable = Get-ChildItem -Path $extractPath -Recurse -File |
        Where-Object { $_.Name -match '^mihomo.*\.exe$' } |
        Select-Object -First 1

    if ($null -eq $executable) {
        throw "mihomo.exe was not found after extracting $($asset.name)."
    }

    $outputDirectory = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    Copy-Item -Path $executable.FullName -Destination $OutputPath -Force

    $fileInfo = Get-Item $OutputPath
    if ($fileInfo.Length -lt 1MB) {
        throw "Downloaded Mihomo executable is unexpectedly small."
    }

    $versionPath = Join-Path $outputDirectory "version.txt"
    Set-Content -Path $versionPath -Value $release.tag_name -Encoding utf8NoBOM

    Write-Host "Mihomo $($release.tag_name) saved to $OutputPath"
}
finally {
    if (Test-Path $tempDirectory) {
        Remove-Item -Path $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
