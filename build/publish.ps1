# Builds Loadout for release: a single-file exe, a portable zip, and (if Inno Setup 6 is installed) an installer.
# Outputs go to the artifacts folder at the repo root:
#   artifacts\publish\Loadout.exe                  the single-file program
#   artifacts\Loadout-<version>-portable.zip       the exe plus a short readme, no install needed
#   artifacts\Loadout-<version>-setup.exe          the installer (only if Inno Setup 6 is found)
# Works in Windows PowerShell 5.1 and PowerShell 7.
#
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File build\publish.ps1
#         powershell -NoProfile -ExecutionPolicy Bypass -File build\publish.ps1 -Version 0.2.0

[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Loadout\Loadout.csproj'
$artifacts = Join-Path $repo 'artifacts'
$publishDir = Join-Path $artifacts 'publish'

function Fail([string]$message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

try {
    if (-not (Test-Path $project)) { Fail "Can't find $project." }

    if (-not $Version) {
        $match = [regex]::Match((Get-Content -Raw $project), '<Version>\s*([^<]+?)\s*</Version>')
        if (-not $match.Success) { Fail 'No <Version> found in Loadout.csproj.' }
        $Version = $match.Groups[1].Value
    }
    Write-Host "Building Loadout $Version" -ForegroundColor Cyan

    # Find dotnet: on PATH first, then the default install folder.
    $dotnet = $null
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCmd) {
        $dotnet = $dotnetCmd.Source
    }
    elseif (Test-Path 'C:\Program Files\dotnet\dotnet.exe') {
        $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
    }
    else {
        Fail 'dotnet was not found on PATH or in C:\Program Files\dotnet. Install the .NET 10 SDK first.'
    }

    # 1. Publish the single-file exe.
    Write-Host ''
    Write-Host 'Step 1: publishing the single-file exe...' -ForegroundColor Cyan
    Push-Location $repo
    try {
        $publishArgs = @(
            'publish', 'src/Loadout/Loadout.csproj',
            '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true',
            '-p:PublishReadyToRun=true',
            '-p:DebugType=none',
            '-o', 'artifacts/publish'
        )
        & $dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed (exit code $LASTEXITCODE)." }
    }
    finally {
        Pop-Location
    }

    $exe = Join-Path $publishDir 'Loadout.exe'
    if (-not (Test-Path $exe)) { Fail "The publish step didn't produce $exe." }

    # 2. Portable zip: the exe plus a readme, built in a temp folder so the zip holds only these two files.
    Write-Host ''
    Write-Host 'Step 2: making the portable zip...' -ForegroundColor Cyan
    $staging = Join-Path ([IO.Path]::GetTempPath()) ('loadout-portable-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        Copy-Item $exe (Join-Path $staging 'Loadout.exe')

        $readme = @(
            'Loadout (portable version)',
            '==========================',
            '',
            'How to start',
            '1. Double-click Loadout.exe. You don''t need to install anything.',
            '2. Loadout lives in the system tray: the small icons next to the clock',
            '   at the bottom-right of the screen. If you can''t see it, click the up arrow there.',
            '3. To open your wheel, hold Ctrl + Alt, aim at the item you want, and let go.',
            '4. Right-click the tray icon for Settings, Pause / Resume and Exit.',
            '',
            'Good to know',
            '- Your settings are stored in %AppData%\Loadout. Delete that folder to reset Loadout.',
            '- Loadout never connects to the internet.',
            '- Windows SmartScreen may warn you the first time you run it, because this program',
            '  is not digitally signed yet. Click "More info", then "Run anyway".'
        ) -join "`r`n"
        [IO.File]::WriteAllText((Join-Path $staging 'README-portable.txt'), $readme + "`r`n", (New-Object Text.UTF8Encoding($false)))

        $zip = Join-Path $artifacts "Loadout-$Version-portable.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
    }
    finally {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }

    # 3. Installer, only if the Inno Setup 6 compiler is installed.
    Write-Host ''
    Write-Host 'Step 3: looking for Inno Setup 6 (for the installer)...' -ForegroundColor Cyan
    $iscc = $null
    $isccCmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCmd) {
        $iscc = $isccCmd.Source
    }
    else {
        $candidates = @()
        if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe') }
        if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe') }
        if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe') }
        $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }

    if (-not $iscc) {
        Write-Host ''
        Write-Host 'NOTE: The installer (Loadout-<version>-setup.exe) was NOT built.' -ForegroundColor Yellow
        Write-Host '      Inno Setup 6 was not found (ISCC.exe is not on PATH and not in the usual folders).' -ForegroundColor Yellow
        Write-Host '      To build it, install Inno Setup 6 (free, from https://jrsoftware.org/isdl.php)' -ForegroundColor Yellow
        Write-Host '      and run this script again. The portable zip and the exe are still ready.' -ForegroundColor Yellow
    }
    else {
        Write-Host "Found $iscc"
        $setupScript = Join-Path $repo 'installer\Loadout.iss'
        & $iscc "/DAppVersion=$Version" "/DSourceDir=$publishDir" "/O$artifacts" $setupScript
        if ($LASTEXITCODE -ne 0) { Fail "Inno Setup failed (exit code $LASTEXITCODE)." }
    }

    # Summary.
    Write-Host ''
    Write-Host 'Done. Output files:' -ForegroundColor Green
    $outputs = @($exe) + @(Get-ChildItem -Path $artifacts -File -Filter "Loadout-$Version-*" | ForEach-Object { $_.FullName })
    foreach ($file in $outputs) {
        $item = Get-Item $file
        Write-Host ('  {0,-60} {1,8:N2} MB' -f $item.FullName, ($item.Length / 1MB))
    }
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
