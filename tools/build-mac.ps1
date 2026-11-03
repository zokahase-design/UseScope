param([string]$Dotnet = 'dotnet', [string]$Python = 'python', [string[]]$Architectures = @('osx-arm64', 'osx-x64'))
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'mac\UseScope.Mac.csproj'
$artifacts = Join-Path $repository 'artifacts'
& (Join-Path $PSScriptRoot 'audit.ps1') -Root $repository -Report (Join-Path $artifacts 'mac-source-audit.json')
& $Dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Mac-Build fehlgeschlagen.' }
$assembly = Join-Path $repository 'mac\bin\Release\net10.0\UseScope.dll'
& $Dotnet $assembly --check (Join-Path $artifacts 'mac-verification')
if ($LASTEXITCODE -ne 0) { throw 'Mac-Offlineprüfung fehlgeschlagen.' }
& $Dotnet $assembly --render (Join-Path $artifacts 'mac-verification')
if ($LASTEXITCODE -ne 0) { throw 'Virtuelle Mac-Darstellung fehlgeschlagen.' }
foreach ($rid in $Architectures) {
    if ($rid -notin @('osx-arm64', 'osx-x64')) { throw 'Unbekannte Mac-Architektur.' }
    $output = Join-Path $artifacts ('mac-publish-' + $rid + '-' + [Guid]::NewGuid().ToString('N'))
    & $Dotnet publish $project -c Release -r $rid --self-contained true -p:UseAppHost=true -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true -o $output
    if ($LASTEXITCODE -ne 0) { throw "Mac-Publish fehlgeschlagen: $rid" }
    & $Python (Join-Path $PSScriptRoot 'package-mac.py') --root $repository --publish $output --rid $rid
    if ($LASTEXITCODE -ne 0) { throw "Mac-Paketprüfung fehlgeschlagen: $rid" }
}
Write-Output 'Mac-Pakete erzeugt. Code-Signatur, Notarisierung und echte macOS-Abnahme bleiben gesonderte Schritte.'
