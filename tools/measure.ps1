# Starts Loadout with a throwaway data folder, waits, and reports idle CPU and memory
# against the SPEC section 8 targets. Stops the app afterwards.
# Usage (repo root):  powershell -NoProfile -File tools/measure.ps1 [-Exe path\to\Loadout.exe] [-Seconds 10]

param(
    [string]$Exe = 'src\Loadout\bin\Debug\net10.0-windows\Loadout.exe',
    [int]$Seconds = 10
)

$ErrorActionPreference = 'Stop'
$data = Join-Path ([IO.Path]::GetTempPath()) ('loadout-measure-' + [Guid]::NewGuid().ToString('N'))
$env:LOADOUT_DATA_DIR = $data

Get-Process Loadout -ErrorAction SilentlyContinue | Stop-Process -Force
$p = Start-Process (Resolve-Path $Exe).Path -PassThru
try {
    Start-Sleep -Seconds 6
    $p.Refresh()
    $cpuStart = $p.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Seconds $Seconds
    $p.Refresh()
    $cpuMs = $p.TotalProcessorTime.TotalMilliseconds - $cpuStart
    $private = (Get-Counter "\Process(Loadout)\Working Set - Private").CounterSamples[0].CookedValue / 1MB

    $cpuOk = $cpuMs -le 20
    $memOk = $private -le 50
    "Idle CPU over ${Seconds}s : $([Math]::Round($cpuMs, 1)) ms  ($(if ($cpuOk) {'PASS'} else {'FAIL'}), target ~0)"
    "Idle memory          : $([Math]::Round($private, 1)) MB  ($(if ($memOk) {'PASS'} else {'FAIL'}), target <= 50 MB)"
    if (Test-Path (Join-Path $data 'loadout.log')) {
        'Errors were logged:'
        Get-Content (Join-Path $data 'loadout.log')
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
}
