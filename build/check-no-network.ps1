<#
.SYNOPSIS
    Fails if Loadout contains any networking code (CLAUDE.md rule 1, SPEC section 9).

.DESCRIPTION
    Check A: scans every .cs, .xaml and .csproj file under src/ for networking types.
    Check B: makes sure no NuGet PackageReference exists in src/Loadout/Loadout.csproj,
             in any .props/.targets file at the repo root, or in any .props/.targets file under src/.
    Check C: reads the referenced assembly names of the compiled Loadout.dll (without
             loading it into this process) and fails on System.Net* or System.ServiceModel*.
             Check C needs PowerShell 7 or later and is skipped with a note on Windows PowerShell 5.1.

    Works in Windows PowerShell 5.1 and PowerShell 7. Exit code 0 = pass, 1 = fail.

.PARAMETER AssemblyPath
    Compiled Loadout.dll for check C. If omitted, the first
    src/Loadout/bin/*/net10.0-windows*/Loadout.dll found is used; if none exists, check C is skipped.

.PARAMETER SkipAssembly
    Runs checks A and B only. Used for a fast first pass in CI, before the build.

.EXAMPLE
    powershell -NoProfile -File build/check-no-network.ps1
    pwsh -NoProfile -File build/check-no-network.ps1 -AssemblyPath src/Loadout/bin/Release/net10.0-windows/Loadout.dll
    pwsh -NoProfile -File build/check-no-network.ps1 -SkipAssembly
#>
param(
    [string]$AssemblyPath,
    [switch]$SkipAssembly
)

$ErrorActionPreference = 'Stop'

# Repo root is the parent of the folder this script lives in (build/).
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\', '/')

$problems = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]
$checkCRan = $false

# Returns the path relative to the repo root, with forward slashes (for example "src/Loadout/App.xaml").
function Get-RepoRelativePath([string]$fullPath) {
    return ($fullPath.Substring($repoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
}

# Build output folders (bin/ and obj/) anywhere below the repo root. Matched on the repo-relative
# path, so the absolute location of the clone does not matter.
function Test-IsBuildOutput([string]$relativePath) {
    return $relativePath -match '(^|/)(bin|obj)/'
}

# ---------------------------------------------------------------------------
# Check A: source scan (case-insensitive, comments and string literals count too)
# ---------------------------------------------------------------------------
$forbiddenPatterns = @(
    'System\.Net\b',
    '\bHttpClient\b',
    '\bWebClient\b',
    '\bWebRequest\b',
    '\bHttpWebRequest\b',
    '\bTcpClient\b',
    '\bTcpListener\b',
    '\bUdpClient\b',
    '\bHttpListener\b',
    '\bSocket\b',
    '\bDns\.',
    '\bWebView2?\b',
    '\bWebBrowser\b',
    '\bServicePointManager\b',
    '\bNetworkInterface\b',
    '\bSystem\.ServiceModel\b',
    '\bWebSocket',
    '\bWindows\.Networking\b',
    '\bWindows\.Web\b',
    '\bStreamSocket\b',
    '\bDatagramSocket\b',
    '\b(winhttp|wininet|ws2_32|urlmon|dnsapi|iphlpapi)(\.dll)?\b',
    '\bURLDownloadToFile',
    '\bHttpMessageHandler\b',
    '\bNetworkStream\b'
)

$srcDir = Join-Path $repoRoot 'src'
if (-not (Test-Path -LiteralPath $srcDir)) {
    $problems.Add("src/: folder not found, so the source scan could not run")
}
else {
    # Source files only. bin/ and obj/ hold build output (compiled DLLs, .pdb files, NuGet caches),
    # which is not source and is checked separately by check C.
    $sourceFiles = @(Get-ChildItem -LiteralPath $srcDir -Recurse -File |
        Where-Object {
            $_.Extension -in '.cs', '.xaml', '.csproj' -and
            -not (Test-IsBuildOutput (Get-RepoRelativePath $_.FullName))
        })

    if ($sourceFiles.Count -eq 0) {
        $problems.Add("src/: no source files found, scan is meaningless")
    }

    foreach ($file in $sourceFiles) {
        $relative = Get-RepoRelativePath $file.FullName
        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
            $lineNumber++
            foreach ($pattern in $forbiddenPatterns) {
                if ($line -match $pattern) {
                    # Keep the printed text short and printable.
                    $shown = ($line.Trim() -replace '[^\x20-\x7E]', '?')
                    if ($shown.Length -gt 160) { $shown = $shown.Substring(0, 160) + '...' }
                    $problems.Add(("{0}:{1}: forbidden networking reference '{2}': {3}" -f $relative, $lineNumber, $matches[0], $shown))
                }
            }
        }
    }
    Write-Host ("Check A: scanned {0} source file(s) under src/." -f $sourceFiles.Count)
}

# ---------------------------------------------------------------------------
# Check B: no NuGet packages anywhere in the app's build setup
# ---------------------------------------------------------------------------
$packageCheckFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]

$csprojPath = Join-Path $repoRoot 'src/Loadout/Loadout.csproj'
if (-not (Test-Path -LiteralPath $csprojPath -PathType Leaf)) {
    $problems.Add("src/Loadout/Loadout.csproj: file not found, so the package check could not run")
}
else {
    $packageCheckFiles.Add((Get-Item -LiteralPath $csprojPath))
}

# .props and .targets files at the repo root (Directory.Build.props, Directory.Build.targets, ...).
foreach ($file in @(Get-ChildItem -LiteralPath $repoRoot -File | Where-Object { $_.Extension -in '.props', '.targets' })) {
    $packageCheckFiles.Add($file)
}

# .props and .targets files under src/ (build output excluded).
if (Test-Path -LiteralPath $srcDir) {
    foreach ($file in @(Get-ChildItem -LiteralPath $srcDir -Recurse -File |
            Where-Object { $_.Extension -in '.props', '.targets' -and -not (Test-IsBuildOutput (Get-RepoRelativePath $_.FullName)) })) {
        $packageCheckFiles.Add($file)
    }
}

foreach ($file in $packageCheckFiles) {
    $relative = Get-RepoRelativePath $file.FullName
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++
        if ($line -match '<PackageReference') {
            $problems.Add(("{0}:{1}: NuGet package reference found; the app must have no packages: {2}" -f $relative, $lineNumber, $line.Trim()))
        }
    }
}
Write-Host ("Check B: checked {0} project/props/targets file(s) for package references." -f $packageCheckFiles.Count)

# ---------------------------------------------------------------------------
# Check C: compiled assembly references (PowerShell 7+ only)
# ---------------------------------------------------------------------------
if ($SkipAssembly) {
    $notes.Add("Check C not run: -SkipAssembly was given.")
}
elseif ($PSVersionTable.PSVersion.Major -lt 7) {
    $notes.Add("Check C skipped: it needs PowerShell 7 or later (this is Windows PowerShell $($PSVersionTable.PSVersion)). Run with pwsh to include it.")
}
else {
    $assembly = $AssemblyPath
    $explicitPath = [bool]$AssemblyPath

    if (-not $explicitPath) {
        $candidate = Get-ChildItem -Path (Join-Path $repoRoot 'src/Loadout/bin/*/net10.0-windows*/Loadout.dll') -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($candidate) { $assembly = $candidate.FullName }
    }
    elseif (-not [System.IO.Path]::IsPathRooted($assembly)) {
        $assembly = Join-Path $repoRoot $assembly
    }

    if (-not $assembly) {
        $notes.Add("Check C skipped: no built Loadout.dll found under src/Loadout/bin. Run 'dotnet build' first, or pass -AssemblyPath.")
    }
    elseif (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        $problems.Add(("{0}: assembly passed to -AssemblyPath was not found" -f $AssemblyPath))
    }
    else {
        $assemblyRelative = Get-RepoRelativePath ([System.IO.Path]::GetFullPath($assembly))
        $stream = [System.IO.File]::OpenRead($assembly)
        try {
            # PEReader only reads the file's bytes; the DLL is never loaded into this process.
            # GetMetadataReader is an extension method, so it is called statically.
            $peReader = New-Object System.Reflection.PortableExecutable.PEReader($stream)
            try {
                $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
                foreach ($handle in $metadata.AssemblyReferences) {
                    $reference = $metadata.GetAssemblyReference($handle)
                    $referenceName = $metadata.GetString($reference.Name)
                    if ($referenceName -like 'System.Net*' -or $referenceName -like 'System.ServiceModel*') {
                        $problems.Add(("{0}: compiled code references forbidden assembly '{1}'" -f $assemblyRelative, $referenceName))
                    }
                }
            }
            finally {
                $peReader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
        $checkCRan = $true
        Write-Host ("Check C: read assembly references of {0}." -f $assemblyRelative)
    }
}

# ---------------------------------------------------------------------------
# Result
# ---------------------------------------------------------------------------
foreach ($note in $notes) {
    Write-Host ("NOTE: {0}" -f $note)
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) {
        Write-Host ("FAIL: {0}" -f $problem)
    }
    Write-Host ("No-network check FAILED: {0} problem(s) found." -f $problems.Count)
    exit 1
}

if ($checkCRan) {
    Write-Host "OK: no networking code in src/, no packages in the app's build files, and no networking references in the compiled assembly."
}
else {
    Write-Host "OK: no networking code in src/ and no packages in the app's build files. Compiled-assembly check was not run (see NOTE above)."
}
exit 0
