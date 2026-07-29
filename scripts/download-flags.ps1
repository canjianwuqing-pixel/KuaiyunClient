param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "src\KuaiyunClient\Assets\Flags"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("kuaiyun-flags-" + [Guid]::NewGuid())
$archivePath = Join-Path $tempDirectory "country-flags.zip"
$extractPath = Join-Path $tempDirectory "extract"
$archiveUrl = "https://github.com/hampusborgos/country-flags/archive/refs/heads/main.zip"

try {
    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

    Write-Host "Downloading country flag PNG assets..."
    Invoke-WebRequest -Uri $archiveUrl -Headers @{ "User-Agent" = "KuaiyunClient-Build" } -OutFile $archivePath
    Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force

    $sourceDirectory = Get-ChildItem -Path $extractPath -Directory -Recurse |
        Where-Object { $_.Name -eq "png100px" } |
        Select-Object -First 1

    if ($null -eq $sourceDirectory) {
        throw "The country-flags archive does not contain png100px."
    }

    Remove-Item -Path $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    Get-ChildItem -Path $sourceDirectory.FullName -Filter "*.png" -File | ForEach-Object {
        $destinationName = $_.Name.ToLowerInvariant()
        Copy-Item $_.FullName (Join-Path $OutputDirectory $destinationName) -Force
    }

    Add-Type -AssemblyName System.Drawing
    $unknownPath = Join-Path $OutputDirectory "unknown.png"
    $bitmap = New-Object System.Drawing.Bitmap 100, 75
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::FromArgb(238, 243, 249))
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(61, 121, 211)), 5
        try {
            $graphics.DrawEllipse($pen, 24, 11, 52, 52)
            $graphics.DrawArc($pen, 38, 11, 24, 52, 90, 180)
            $graphics.DrawArc($pen, 38, 11, 24, 52, 270, 180)
            $graphics.DrawLine($pen, 25, 37, 75, 37)
        }
        finally {
            $pen.Dispose()
        }

        $bitmap.Save($unknownPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    foreach ($required in @("us.png", "jp.png", "hk.png", "sg.png", "unknown.png")) {
        $path = Join-Path $OutputDirectory $required
        if (-not (Test-Path $path)) {
            throw "Missing required flag asset: $required"
        }
    }

    $count = (Get-ChildItem -Path $OutputDirectory -Filter "*.png" -File).Count
    if ($count -lt 200) {
        throw "Only $count flag images were copied; expected at least 200."
    }

    Write-Host "$count offline flag images saved to $OutputDirectory"
}
finally {
    Remove-Item -Path $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
