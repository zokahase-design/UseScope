param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repository 'artifacts'
$publish = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
$verification = Join-Path $artifacts 'verification'
& (Join-Path $PSScriptRoot 'audit.ps1') -Root $repository -Report (Join-Path $artifacts 'source-audit.json')
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$sdk = (& $Dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw '.NET SDK nicht gefunden.' }
& $Dotnet publish (Join-Path $repository 'source\UseScope.csproj') -c Release -r win-x64 --self-contained true -o $publish -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
$test = Start-Process -FilePath (Join-Path $publish 'UseScope.exe') -ArgumentList @('--check', ('"' + $verification + '"')) -Wait -PassThru -WindowStyle Hidden
if ($test.ExitCode -ne 0) { throw "Offline-Prüfung fehlgeschlagen: $verification\checks.json" }

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows-.NET-Framework-Compiler für den portablen Starter fehlt.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $repository 'source\UseScope.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$launcherDirectory = Join-Path $artifacts 'launcher'
New-Item -ItemType Directory -Path $launcherDirectory -Force | Out-Null
$assembly = Join-Path $launcherDirectory 'AssemblyInfo.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("UseScope Starter")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
"@ | Set-Content -LiteralPath $assembly -Encoding utf8
$launcher = Join-Path $launcherDirectory 'UseScope starten.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /debug- /reference:System.Windows.Forms.dll "/win32icon:$(Join-Path $repository 'source\UseScope.ico')" "/out:$launcher" (Join-Path $repository 'launcher\Start.cs') $assembly
if ($LASTEXITCODE -ne 0) { throw 'Starter-Build fehlgeschlagen.' }
$installedLauncherDirectory = Join-Path $artifacts 'installed-launcher'
New-Item -ItemType Directory -Path $installedLauncherDirectory -Force | Out-Null
$installedLauncher = Join-Path $installedLauncherDirectory 'UseScope starten.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /debug- /define:INSTALLED_STARTER /reference:System.Windows.Forms.dll "/win32icon:$(Join-Path $repository 'source\UseScope.ico')" "/out:$installedLauncher" (Join-Path $repository 'launcher\Start.cs') $assembly
if ($LASTEXITCODE -ne 0) { throw 'Build des festen installierten Starters fehlgeschlagen.' }
$checks = Get-Content -LiteralPath (Join-Path $verification 'checks.json') -Raw | ConvertFrom-Json
[ordered]@{ Version = $version; SDK = $sdk; OfflineChecks = $checks.Checks.Count; Passed = $checks.Passed; PublishDirectory = $publish } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'build.json') -Encoding utf8
Write-Output "Build $version und $($checks.Checks.Count) Offline-Prüfungen bestanden."
