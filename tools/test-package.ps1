$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repository 'artifacts'
$package = Get-Content -LiteralPath (Join-Path $artifacts 'package.json') -Raw | ConvertFrom-Json
$zip = Join-Path (Join-Path $repository 'dist') $package.Archive
if ((Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant() -ne $package.SHA256) { throw 'ZIP-Prüfsumme stimmt nicht.' }
$smokeRoot = Join-Path $artifacts ('smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeRoot | Out-Null
Expand-Archive -LiteralPath $zip -DestinationPath $smokeRoot
$application = Join-Path $smokeRoot 'UseScope'
$manifest = Get-Content -LiteralPath (Join-Path $application 'docs\manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.Files) {
    $file = [IO.Path]::GetFullPath((Join-Path $application $entry.Path))
    if (-not $file.StartsWith($application + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Ungültiger Manifestpfad.' }
    if ((Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256) { throw "Archivdatei weicht ab: $($entry.Path)" }
}
$rootFiles = @(Get-ChildItem -LiteralPath $application -File)
if ($rootFiles.Count -ne 1 -or $rootFiles[0].Name -ne 'UseScope starten.exe') { throw 'Unerwartete Dateien im Programm-Hauptordner.' }
& (Join-Path $PSScriptRoot 'audit.ps1') -Root $application

function Run-Hidden([string]$Executable, [string[]]$Arguments) {
    $start = @{ FilePath = $Executable; PassThru = $true; WindowStyle = 'Hidden' }
    if ($Arguments.Count -gt 0) { $start.ArgumentList = $Arguments }
    $process = Start-Process @start
    try {
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Paketprüfung hat das Zeitlimit überschritten.' }
        if ($process.ExitCode -ne 0) { throw "Paketprüfung fehlgeschlagen: Exit $($process.ExitCode)" }
    } finally { $process.Dispose() }
}

$checkDirectory = Join-Path $smokeRoot 'offline-checks'
Run-Hidden -Executable (Join-Path $application 'runtime\UseScope.exe') -Arguments @('--check', ('"' + $checkDirectory + '"'))
$firstReport = Join-Path $smokeRoot 'target-first.txt'
Run-Hidden -Executable (Join-Path $application 'UseScope starten.exe') -Arguments @('--check-target', ('"' + $firstReport + '"'))
if ([IO.File]::ReadAllText($firstReport) -ne (Join-Path $application 'runtime\UseScope.exe')) { throw 'Starter löst das Programmziel falsch auf.' }

$movedParent = Join-Path $smokeRoot 'anderer Ordner mit Leerzeichen und ü'
New-Item -ItemType Directory -Path $movedParent | Out-Null
$moved = [IO.Path]::GetFullPath((Join-Path $movedParent 'UseScope'))
$resolvedApplication = (Resolve-Path -LiteralPath $application).Path
if (-not $resolvedApplication.StartsWith($smokeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not $moved.StartsWith($smokeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsicherer Test-Verschiebepfad.' }
Move-Item -LiteralPath $resolvedApplication -Destination $moved
$secondReport = Join-Path $smokeRoot 'target-relocated.txt'
Run-Hidden -Executable (Join-Path $moved 'UseScope starten.exe') -Arguments @('--check-target', ('"' + $secondReport + '"'))
if ([IO.File]::ReadAllText($secondReport) -ne (Join-Path $moved 'runtime\UseScope.exe')) { throw 'Starter ist nicht portabel.' }

# Exercise the launcher's actual start path with a non-UI test payload. Never
# start the real interactive application as part of the package verification.
$fixture = Join-Path $smokeRoot 'Fixture.cs'
@'
using System.IO;
internal static class Fixture {
    private static void Main() { File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "launcher-smoke.ok"), "ok"); }
}
'@ | Set-Content -LiteralPath $fixture -Encoding utf8
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixtureExecutable = Join-Path $moved 'runtime\UseScope.exe'
& $compiler /nologo /target:winexe /platform:anycpu /debug- "/out:$fixtureExecutable" $fixture
if ($LASTEXITCODE -ne 0) { throw 'Testprogramm konnte nicht erstellt werden.' }
Run-Hidden -Executable (Join-Path $moved 'UseScope starten.exe') -Arguments @()
$marker = Join-Path $moved 'runtime\launcher-smoke.ok'
$deadline = [DateTime]::UtcNow.AddSeconds(10)
while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
if (-not (Test-Path -LiteralPath $marker)) { throw 'Starter hat den Testprozess nicht im richtigen Arbeitsordner ausgeführt.' }
[ordered]@{
    Passed = $true
    ArchiveHash = $true
    ManifestFilesChecked = $manifest.Files.Count
    RootContainsOnlyStarterAndDirectories = $true
    OfflineApplicationChecks = $true
    RelativeTargetResolution = $true
    RelocationWithSpacesAndUnicode = $true
    ActualLaunchWithNonUiFixture = $true
    RealInteractiveApplicationStarted = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'package-test.json') -Encoding utf8
Write-Output 'Paket, Prüfsummen und portabler Starter erfolgreich geprüft. Kein sichtbares Widget gestartet.'
