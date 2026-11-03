# Evidencija radnog vremena - MSI installer build (WiX Toolset v4)
#
#   .\installer\build-installer.ps1 -Version 0.1.0.0
#
# Publishes the app as one self-contained exe and wraps it in an MSI. The MSI is also what the in-app update downloads.
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$ScriptRoot = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $ScriptRoot
$ProjectPath = "$ProjectRoot\src\CompanyTimesheet.Desktop\CompanyTimesheet.Desktop.csproj"
$PublishPath = "$ProjectRoot\publish\$RuntimeIdentifier"
$OutputPath = "$ScriptRoot\output"
$WxsFile = "$ScriptRoot\installer.wxs"

# The version of the assembly decides what the updater thinks is installed, so the MSI and the exe get the same one
if ([string]::IsNullOrEmpty($Version)) {
    try {
        $gitVersion = (& dotnet-gitversion --root $ProjectRoot 2>$null) | ConvertFrom-Json
        $Version = $gitVersion.AssemblySemFileVer
    } catch {
        $Version = "0.1.0.0"
    }
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "The version must have four numbers (for example 0.1.0.0), not '$Version'." }

$MsiOutput = "$OutputPath\CompanyTimesheet-v$Version-$RuntimeIdentifier.msi"
Write-Host "Version $Version -> $MsiOutput" -ForegroundColor Cyan

& wix --version | Out-Null
if ($LASTEXITCODE -ne 0) { throw "WiX Toolset not found. Install it with: dotnet tool install --global wix --version 4.0.5" }

New-Item -ItemType Directory -Force -Path $PublishPath, $OutputPath | Out-Null

if (-not $SkipPublish) {
    Write-Host "Publishing..." -ForegroundColor Yellow
    if (Test-Path $PublishPath) { Remove-Item -Recurse -Force "$PublishPath\*" }
    & dotnet publish $ProjectPath -c $Configuration -r $RuntimeIdentifier --self-contained true --output $PublishPath `
        /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishTrimmed=false /p:DebugType=embedded `
        "/p:AssemblyVersion=$Version" "/p:FileVersion=$Version" --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

foreach ($file in "CompanyTimesheet.exe", "appsettings.json") {
    if (-not (Test-Path (Join-Path $PublishPath $file))) { throw "Required file missing in the publish folder: $file" }
}

Write-Host "Building the MSI..." -ForegroundColor Yellow
& wix build $WxsFile -out $MsiOutput -arch x64 -dcl high -d "PublishDir=$PublishPath" -d "Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

$msi = Get-Item $MsiOutput
Write-Host ("Created {0} ({1:N1} MB)" -f $msi.Name, ($msi.Length / 1MB)) -ForegroundColor Green
