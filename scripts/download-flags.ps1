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
$nodeRoot = Join-Path $tempDirectory "renderer"
$archiveUrl = "https://github.com/hampusborgos/country-flags/archive/refs/heads/main.zip"

try {
    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null
    New-Item -ItemType Directory -Path $nodeRoot -Force | Out-Null

    Write-Host "Downloading country flag SVG assets..."
    Invoke-WebRequest -Uri $archiveUrl -Headers @{ "User-Agent" = "KuaiyunClient-Build" } -OutFile $archivePath
    Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force

    $sourceDirectory = Get-ChildItem -Path $extractPath -Directory -Recurse |
        Where-Object { $_.Name -eq "svg" } |
        Select-Object -First 1

    if ($null -eq $sourceDirectory) {
        throw "The country-flags archive does not contain the svg directory."
    }

    Remove-Item -Path $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    $unknownSvg = Join-Path $sourceDirectory.FullName "unknown.svg"
    @'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 75">
  <rect width="100" height="75" rx="8" fill="#EEF3F9"/>
  <circle cx="50" cy="37.5" r="23" fill="none" stroke="#3D79D3" stroke-width="5"/>
  <path d="M27 37.5h46M50 14.5c-10 10-10 36 0 46M50 14.5c10 10 10 36 0 46" fill="none" stroke="#3D79D3" stroke-width="4" stroke-linecap="round"/>
</svg>
'@ | Set-Content -Path $unknownSvg -Encoding utf8NoBOM

    Write-Host "Installing the PNG renderer..."
    & npm install sharp@0.34.3 --no-save --no-package-lock --prefix $nodeRoot --silent
    if ($LASTEXITCODE -ne 0) {
        throw "npm failed to install sharp. Exit code: $LASTEXITCODE"
    }

    $rendererPath = Join-Path $nodeRoot "render-flags.js"
    @'
const fs = require("fs");
const path = require("path");
const sharp = require("sharp");

async function main() {
  const source = process.argv[2];
  const output = process.argv[3];
  fs.mkdirSync(output, { recursive: true });

  const files = fs.readdirSync(source)
    .filter(name => name.toLowerCase().endsWith(".svg"))
    .sort();

  for (const name of files) {
    const target = path.join(output, path.basename(name, ".svg").toLowerCase() + ".png");
    await sharp(path.join(source, name), {
      density: 72,
      limitInputPixels: false,
      unlimited: true
    })
      .resize({ width: 100, height: 75, fit: "contain", background: { r: 255, g: 255, b: 255, alpha: 0 } })
      .png({ compressionLevel: 9 })
      .toFile(target);
  }

  console.log(`Rendered ${files.length} flag images.`);
}

main().catch(error => {
  console.error(error);
  process.exit(1);
});
'@ | Set-Content -Path $rendererPath -Encoding utf8NoBOM

    Push-Location $nodeRoot
    try {
        & node $rendererPath $sourceDirectory.FullName $OutputDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "Flag rendering failed. Exit code: $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    foreach ($required in @("us.png", "jp.png", "hk.png", "sg.png", "unknown.png")) {
        $path = Join-Path $OutputDirectory $required
        if (-not (Test-Path $path)) {
            throw "Missing required flag asset: $required"
        }
    }

    $count = (Get-ChildItem -Path $OutputDirectory -Filter "*.png" -File).Count
    if ($count -lt 200) {
        throw "Only $count flag images were rendered; expected at least 200."
    }

    Write-Host "$count offline flag images saved to $OutputDirectory"
}
finally {
    Remove-Item -Path $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
