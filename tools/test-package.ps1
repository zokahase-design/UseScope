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
$installedTools = Join-Path $application 'tools'
New-Item -ItemType Directory -Path $installedTools | Out-Null
Copy-Item -LiteralPath (Join-Path $artifacts 'installed-launcher\UseScope starten.exe') -Destination $installedTools
$installedReport = Join-Path $smokeRoot 'target-installed.txt'
Run-Hidden -Executable (Join-Path $installedTools 'UseScope starten.exe') -Arguments @('--check-target', ('"' + $installedReport + '"'))
if ([IO.File]::ReadAllText($installedReport) -ne (Join-Path $application 'runtime\UseScope.exe')) { throw 'Installierter Starter löst das Programmziel falsch auf.' }

$movedParent = Join-Path $smokeRoot 'anderer Ordner mit Leerzeichen und ü'
New-Item -ItemType Directory -Path $movedParent | Out-Null
$moved = [IO.Path]::GetFullPath((Join-Path $movedParent 'UseScope'))
$resolvedApplication = (Resolve-Path -LiteralPath $application).Path
if (-not $resolvedApplication.StartsWith($smokeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not $moved.StartsWith($smokeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsicherer Test-Verschiebepfad.' }
Move-Item -LiteralPath $resolvedApplication -Destination $moved
$secondReport = Join-Path $smokeRoot 'target-relocated.txt'
Run-Hidden -Executable (Join-Path $moved 'UseScope starten.exe') -Arguments @('--check-target', ('"' + $secondReport + '"'))
if ([IO.File]::ReadAllText($secondReport) -ne (Join-Path $moved 'runtime\UseScope.exe')) { throw 'Starter ist nicht portabel.' }
$installedMovedReport = Join-Path $smokeRoot 'target-installed-relocated.txt'
Run-Hidden -Executable (Join-Path $moved 'tools\UseScope starten.exe') -Arguments @('--check-target', ('"' + $installedMovedReport + '"'))
if ([IO.File]::ReadAllText($installedMovedReport) -ne (Join-Path $moved 'runtime\UseScope.exe')) { throw 'Installierter Starter folgt dem verschobenen Programmordner nicht.' }

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
$actualLaunchChecked = $false
$launchDeferredReason = $null
if (@(Get-Process -Name UseScope -ErrorAction SilentlyContinue).Count -gt 0) {
    $launchDeferredReason = 'Laufende Nutzer-App bleibt unangetastet; Kollisionsschutz des Starters bleibt aktiv.'
} else {
    foreach ($starter in @((Join-Path $moved 'UseScope starten.exe'), (Join-Path $moved 'tools\UseScope starten.exe'))) {
        $marker = Join-Path $moved 'runtime\launcher-smoke.ok'
        if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
        Run-Hidden -Executable $starter -Arguments @()
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (-not (Test-Path -LiteralPath $marker)) { throw 'Starter hat den Testprozess nicht im richtigen Arbeitsordner ausgeführt.' }
        while (@(Get-Process -Name UseScope -ErrorAction SilentlyContinue).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    }
    $actualLaunchChecked = $true
}
[ordered]@{
    Passed = $true
    ArchiveHash = $true
    ManifestFilesChecked = $manifest.Files.Count
    RootContainsOnlyStarterAndDirectories = $true
    OfflineApplicationChecks = $true
    RelativeTargetResolution = $true
    InstalledStarterTargetResolution = $true
    InstalledStarterRelocation = $true
    RelocationWithSpacesAndUnicode = $true
    ActualLaunchWithNonUiFixture = $actualLaunchChecked
    ActualLaunchDeferredReason = $launchDeferredReason
    RealInteractiveApplicationStarted = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'package-test.json') -Encoding utf8
Write-Output 'Paket, Prüfsummen und portabler Starter erfolgreich geprüft. Kein sichtbares Widget gestartet.'
