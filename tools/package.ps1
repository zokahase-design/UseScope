param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repository 'artifacts'
$buildPath = Join-Path $artifacts 'build.json'
if (-not (Test-Path -LiteralPath $buildPath)) { throw 'Zuerst tools/build.ps1 ausführen.' }
$build = Get-Content -LiteralPath $buildPath -Raw | ConvertFrom-Json
$publish = (Resolve-Path -LiteralPath $build.PublishDirectory).Path
if (-not $publish.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Buildverzeichnis liegt außerhalb der Artefakte.' }
if (-not $build.Passed) { throw 'Build ist nicht erfolgreich geprüft.' }
$stageParent = Join-Path $artifacts ('package-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $stageParent 'UseScope'
$runtime = Join-Path $stage 'runtime'
$docs = Join-Path $stage 'docs'
New-Item -ItemType Directory -Path $runtime, (Join-Path $docs 'licenses') -Force | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination $runtime -Recurse
Copy-Item -LiteralPath (Join-Path $artifacts 'launcher\UseScope starten.exe') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $docs 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repository 'THIRD_PARTY_NOTICES.md') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repository 'docs\QUICKSTART.md') -Destination (Join-Path $docs 'README.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\PRIVACY.md') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repository 'docs\PROVIDERS.md') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repository 'docs\BRAND_ASSETS.md') -Destination $docs
foreach ($document in @('API_METER.md', 'PROVIDER_MATRIX.md', 'TOKENS.md', 'provider-catalog.json')) { Copy-Item -LiteralPath (Join-Path $repository ('docs\' + $document)) -Destination $docs }

$dependencies = Get-Content -LiteralPath (Join-Path $publish 'UseScope.deps.json') -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath (Join-Path $repository 'source\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$componentList = [Collections.Generic.List[object]]::new()
foreach ($definition in @(
    @{ Prefix = 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/'; Package = 'microsoft.netcore.app.runtime.win-x64'; Label = 'DotNet'; License = 'LICENSE.TXT' },
    @{ Prefix = 'runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/'; Package = 'microsoft.windowsdesktop.app.runtime.win-x64'; Label = 'WindowsDesktop'; License = 'LICENSE' },
    @{ Prefix = 'runtimepack.Microsoft.AspNetCore.App.Runtime.win-x64/'; Package = 'microsoft.aspnetcore.app.runtime.win-x64'; Label = 'AspNetCore'; License = 'LICENSE.txt' }
)) {
    $entry = @($dependencies.libraries.PSObject.Properties.Name | Where-Object { $_.StartsWith($definition.Prefix, [StringComparison]::Ordinal) })
    if ($entry.Count -ne 1) { throw "Laufzeitversion nicht eindeutig: $($definition.Package)" }
    $version = $entry[0].Substring($definition.Prefix.Length)
    $package = $null
    foreach ($packageRoot in $packageRoots) {
        $candidate = Join-Path (Join-Path $packageRoot $definition.Package) $version
        if (Test-Path -LiteralPath (Join-Path $candidate $definition.License)) { $package = $candidate; break }
    }
    if (-not $package) { throw "Lizenztext des Laufzeitpakets fehlt: $($definition.Package) $version" }
    Copy-Item -LiteralPath (Join-Path $package $definition.License) -Destination (Join-Path $docs ('licenses\' + $definition.Label + '-LICENSE.txt'))
    $notices = Join-Path $package 'THIRD-PARTY-NOTICES.TXT'
    if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination (Join-Path $docs ('licenses\' + $definition.Label + '-THIRD-PARTY-NOTICES.txt')) }
    $componentList.Add([ordered]@{ Package = $definition.Package; Version = $version; License = 'MIT and included third-party terms' })
}
[ordered]@{ Application = 'UseScope'; Version = $build.Version; SDK = $build.SDK; OfflineChecks = $build.OfflineChecks; Components = @($componentList.ToArray()) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $docs 'build-info.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'audit.ps1') -Root $stage -Report (Join-Path $artifacts 'package-audit.json')
& python (Join-Path $PSScriptRoot 'privacy-audit.py') --root $stage --report (Join-Path $artifacts 'package-privacy.json')
if ($LASTEXITCODE -ne 0) { throw 'Datenschutzprüfung des Windows-Pakets fehlgeschlagen.' }

$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    [ordered]@{ Path = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/'); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; Bytes = $_.Length }
})
[ordered]@{ Application = 'UseScope'; Version = $build.Version; Files = $files } | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $docs 'manifest.json') -Encoding utf8
$distribution = Join-Path $repository 'dist'
New-Item -ItemType Directory -Path $distribution -Force | Out-Null
$name = 'UseScope-' + $build.Version + '-win-x64.zip'
$zip = Join-Path $distribution $name
Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $distribution ($name + '.sha256')), "$hash  $name`n", [Text.UTF8Encoding]::new($false))
[ordered]@{ Archive = $name; SHA256 = $hash; Files = $files.Count; Stage = $stage } | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $artifacts 'package.json') -Encoding utf8
Write-Output "Download-Paket erstellt: $zip"
