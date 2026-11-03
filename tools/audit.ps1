param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$Report = ''
)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$findings = [Collections.Generic.List[object]]::new()
$count = 0
$textExtensions = @('.cs', '.csproj', '.md', '.txt', '.json', '.yml', '.yaml', '.ps1', '.py', '.plist', '.config')
$secretPatterns = @(
    '\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{20,}\b',
    '\b(?:github_pat_[A-Za-z0-9_]{30,}|gh[pousr]_[A-Za-z0-9]{30,})\b',
    '\beyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{8,}\b',
    '-----BEGIN [A-Z ]*PRIVATE KEY-----',
    '(?i)\b[A-Z]:\\Users\\(?!Public\\|Default\\|<)[^\\\r\n]+\\'
)
foreach ($file in Get-ChildItem -LiteralPath $rootPath -Recurse -Force -File) {
    $relative = [IO.Path]::GetRelativePath($rootPath, $file.FullName)
    if ($relative -match '(^|[\\/])(?:\.git|artifacts|dist|bin|obj)([\\/]|$)') { continue }
    $count++
    if ($file.Name -match '^(?:auth\.json|\.credentials\.json|api-key\.dpapi(?:\..*)?|settings\.json(?:\..*)?|\.env(?:\..*)?|cookies(?:\..*)?)$' -or $relative -match '(^|[\\/])profiles([\\/]|$)' -or $file.Extension -in @('.lnk', '.log', '.dmp')) {
        $findings.Add([ordered]@{ File = $relative; Kind = 'Private or machine-specific file' })
    }
    if ($file.Extension -notin $textExtensions -and $file.Name -notin @('LICENSE', '.gitignore', '.gitattributes')) { continue }
    $content = [IO.File]::ReadAllText($file.FullName)
    foreach ($pattern in $secretPatterns) {
        if ($content -match $pattern) {
            $findings.Add([ordered]@{ File = $relative; Kind = 'Potential secret or personal absolute path' })
            break
        }
    }
}
$result = [ordered]@{ Passed = $findings.Count -eq 0; FilesScanned = $count; Findings = @($findings.ToArray()) }
if ($Report) {
    $reportPath = [IO.Path]::GetFullPath($Report)
    New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
}
if ($findings.Count -gt 0) {
    $findings | ConvertTo-Json -Depth 5 | Write-Output
    throw 'Dateiprüfung fehlgeschlagen. Meldungen vor einer Weitergabe prüfen.'
}
Write-Output "Dateiprüfung bestanden: $count Dateien. Die Prüfung ersetzt keine manuelle Sichtung."
