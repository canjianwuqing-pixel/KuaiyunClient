param(
    [string]$Version = "latest",
    [string]$GeoVersion = "latest",
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

$headers = @{
    "Accept"     = "application/vnd.github+json"
    "User-Agent" = "KuaiyunClient-Build"
}

function Get-GitHubRelease {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$RequestedVersion
    )

    $releaseUrl = if ($RequestedVersion -eq "latest") {
        "https://api.github.com/repos/$Repository/releases/latest"
    }
    else {
        "https://api.github.com/repos/$Repository/releases/tags/$RequestedVersion"
    }

    Write-Host "Reading release: $releaseUrl"
    return Invoke-RestMethod -Uri $releaseUrl -Headers $headers
}

function Get-ExactAsset {
    param(
        [Parameter(Mandatory = $true)]$Release,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $asset = @($Release.assets) |
        Where-Object { $_.name -eq $Name } |
        Select-Object -First 1

    if ($null -eq $asset) {
        throw "Release $($Release.tag_name) does not contain asset '$Name'."
    }

    return $asset
}

function Download-VerifiedAsset {
    param(
        [Parameter(Mandatory = $true)]$Release,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][long]$MinimumSize,
        [Parameter(Mandatory = $true)][string]$TemporaryDirectory
    )

    $asset = Get-ExactAsset -Release $Release -Name $Name
    $checksumAsset = Get-ExactAsset -Release $Release -Name "$Name.sha256sum"
    $downloadPath = Join-Path $TemporaryDirectory $Name
    $checksumPath = Join-Path $TemporaryDirectory "$Name.sha256sum"

    Write-Host "Downloading $Name"
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $downloadPath
    Invoke-WebRequest -Uri $checksumAsset.browser_download_url -Headers $headers -OutFile $checksumPath

    $expectedHash = ((Get-Content -Path $checksumPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $actualHash = (Get-FileHash -Path $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()

    if ([string]::IsNullOrWhiteSpace($expectedHash) -or $actualHash -ne $expectedHash) {
        throw "SHA-256 verification failed for $Name. Expected $expectedHash, actual $actualHash."
    }

    $downloadedFile = Get-Item $downloadPath
    if ($downloadedFile.Length -lt $MinimumSize) {
        throw "$Name is unexpectedly small: $($downloadedFile.Length) bytes."
    }

    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -Path $downloadPath -Destination $Destination -Force

    Write-Host "$Name verified and saved to $Destination"
}

$mihomoRelease = Get-GitHubRelease -Repository "MetaCubeX/mihomo" -RequestedVersion $Version
$mihomoAssets = @($mihomoRelease.assets)
$mihomoAsset = $mihomoAssets |
    Where-Object { $_.name -match '^mihomo-windows-amd64-compatible-.*\.zip$' } |
    Select-Object -First 1

if ($null -eq $mihomoAsset) {
    $mihomoAsset = $mihomoAssets |
        Where-Object {
            $_.name -match '^mihomo-windows-amd64-.*\.zip$' -and
            $_.name -notmatch 'amd64-v3'
        } |
        Select-Object -First 1
}

if ($null -eq $mihomoAsset) {
    throw "The Mihomo release does not contain a compatible Windows amd64 ZIP asset."
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("kuaiyun-runtime-assets-" + [Guid]::NewGuid())
$archivePath = Join-Path $tempDirectory $mihomoAsset.name
$extractPath = Join-Path $tempDirectory "extract"

try {
    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

    Write-Host "Downloading $($mihomoAsset.name)"
    Invoke-WebRequest -Uri $mihomoAsset.browser_download_url -Headers $headers -OutFile $archivePath
    Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force

    $executable = Get-ChildItem -Path $extractPath -Recurse -File |
        Where-Object { $_.Name -match '^mihomo.*\.exe$' } |
        Select-Object -First 1

    if ($null -eq $executable) {
        throw "mihomo.exe was not found after extracting $($mihomoAsset.name)."
    }

    $outputDirectory = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    Copy-Item -Path $executable.FullName -Destination $OutputPath -Force

    $mihomoFile = Get-Item $OutputPath
    if ($mihomoFile.Length -lt 1MB) {
        throw "Downloaded Mihomo executable is unexpectedly small."
    }

    $geoRelease = Get-GitHubRelease -Repository "MetaCubeX/meta-rules-dat" -RequestedVersion $GeoVersion

    Download-VerifiedAsset `
        -Release $geoRelease `
        -Name "geoip.metadb" `
        -Destination (Join-Path $outputDirectory "geoip.metadb") `
        -MinimumSize 256KB `
        -TemporaryDirectory $tempDirectory

    Download-VerifiedAsset `
        -Release $geoRelease `
        -Name "geosite.dat" `
        -Destination (Join-Path $outputDirectory "geosite.dat") `
        -MinimumSize 1MB `
        -TemporaryDirectory $tempDirectory

    $versionPath = Join-Path $outputDirectory "version.txt"
    @(
        "mihomo=$($mihomoRelease.tag_name)"
        "geodata=$($geoRelease.tag_name)"
    ) | Set-Content -Path $versionPath -Encoding utf8NoBOM

    Write-Host "Mihomo $($mihomoRelease.tag_name) and Geo data $($geoRelease.tag_name) saved to $outputDirectory"
}
finally {
    if (Test-Path $tempDirectory) {
        Remove-Item -Path $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
