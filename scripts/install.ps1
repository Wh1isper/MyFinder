[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$package = Join-Path $PSScriptRoot 'MyFinder.msix'
if (-not (Test-Path $package)) { throw 'Extract the complete self-signed package before running install.ps1.' }
# Windows validates the signature and publisher trust. This script never imports certificates.
Add-AppxPackage -Path $package
