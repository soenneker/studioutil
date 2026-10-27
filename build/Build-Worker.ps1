param(
    [Parameter(Mandatory = $true)][string] $Configuration,
    [Parameter(Mandatory = $true)][string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
# devenv's MSBuild environment must not override dotnet's .NET 11 SDK resolution.
Remove-Item Env:MSBUILD_EXE_PATH, Env:MSBuildSDKsPath -ErrorAction SilentlyContinue
$project = Join-Path $PSScriptRoot '..\src\StudioUtil.Worker\StudioUtil.Worker.csproj'
& dotnet publish $project --configuration $Configuration --no-self-contained --output $OutputDirectory --nologo
exit $LASTEXITCODE
