param(
    [Parameter(Mandatory = $true)][ValidateRange(1, 65534)][int] $RunNumber,
    [Parameter(Mandatory = $true)][ValidateRange(1, 65534)][int] $RunAttempt
)
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot '../src/StudioUtil/source.extension.vsixmanifest'
$sourcePath = Join-Path $PSScriptRoot '../src/StudioUtil/source.extension.cs'
$manifestText = Get-Content $manifestPath -Raw
[xml] $manifest = $manifestText
$baseVersion = [version] $manifest.PackageManifest.Metadata.Identity.Version
$version = '{0}.{1}.{2}.{3}' -f $baseVersion.Major, $baseVersion.Minor, $RunNumber, $RunAttempt
$manifestText = $manifestText.Replace('Version="' + $baseVersion + '"', 'Version="' + $version + '"')
Set-Content $manifestPath $manifestText -NoNewline
$source = Get-Content $sourcePath -Raw
if ($source -notmatch 'public const string Version = "[^"]+";') { throw 'VSIX version constant not found.' }
$source = $source -replace 'public const string Version = "[^"]+";', ('public const string Version = "' + $version + '";')
Set-Content $sourcePath $source -NoNewline
Write-Host "Release version: $version"
if ($env:GITHUB_OUTPUT) { "version=$version" >> $env:GITHUB_OUTPUT }
