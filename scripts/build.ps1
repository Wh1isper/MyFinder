[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'src/MyFinder/MyFinder.csproj'
$output = Join-Path $root 'artifacts/app'
Push-Location $root
try {
    & dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    & dotnet publish $project --no-restore -c $Configuration -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
    Write-Host "Application: $output/MyFinder.exe"
}
finally {
    Pop-Location
}
