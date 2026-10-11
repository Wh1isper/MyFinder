[CmdletBinding()]
param(
    [string]$CertificateThumbprint,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/packages' }
$app = Join-Path $root 'artifacts/app'
if (-not (Test-Path "$app/MyFinder.exe")) { throw 'Run scripts/build.ps1 first.' }
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$packageVersion = [version]"$version.0"
$name = "MyFinder-$version-win-x64"
[xml]$manifest = Get-Content (Join-Path $root 'packaging/AppxManifest.xml') -Raw
$manifest.Package.Identity.Version = $packageVersion.ToString()
if ($CertificateThumbprint) {
    $certificate = Get-Item "Cert:/CurrentUser/My/$CertificateThumbprint"
    if (-not $certificate.HasPrivateKey) { throw 'The signing certificate has no private key.' }
    if ($certificate.Subject -cne [string]$manifest.Package.Identity.Publisher) {
        throw 'Certificate subject must exactly match the package Publisher.'
    }
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
foreach ($file in @("$name-portable.zip", "$name-unsigned.msix", "$name-self-signed.zip", 'SHA256SUMS.txt')) {
    $path = Join-Path $OutputDirectory $file
    if (Test-Path $path) { Remove-Item $path -Force }
}

# Use the same SDK tools that were restored with the application, not a machine-specific path.
$assets = Get-Content (Join-Path $root 'src/MyFinder/obj/project.assets.json') -Raw | ConvertFrom-Json
$buildTools = $assets.libraries.PSObject.Properties | Where-Object Name -Like 'Microsoft.Windows.SDK.BuildTools/*' | Select-Object -First 1
$tools = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $packagePath = Join-Path $folder $buildTools.Value.path
    $candidate = Get-ChildItem "$packagePath/bin/*/x64/makeappx.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidate) { $tools = $candidate.DirectoryName; break }
}
if (-not $tools) { throw 'Windows SDK build tools were not restored. Run scripts/build.ps1.' }

$stage = Join-Path $root 'artifacts/package-staging'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item $stage -ItemType Directory | Out-Null
Copy-Item "$app/*" $stage -Recurse
Copy-Item (Join-Path $root 'LICENSE') $stage
Compress-Archive "$stage/*" (Join-Path $OutputDirectory "$name-portable.zip") -Force
Copy-Item (Join-Path $root 'packaging/Assets') $stage -Recurse
$manifest.Save((Join-Path $stage 'AppxManifest.xml'))
$unsigned = Join-Path $OutputDirectory "$name-unsigned.msix"
$packLog = Join-Path $root 'artifacts/makeappx.log'
& "$tools/makeappx.exe" pack /d $stage /p $unsigned /o *> $packLog
if ($LASTEXITCODE -ne 0) {
    Get-Content $packLog -Tail 30 | Write-Host
    throw "MSIX packaging failed. See $packLog."
}

if ($CertificateThumbprint) {
    $signedDirectory = Join-Path $root 'artifacts/signed-staging'
    if (Test-Path $signedDirectory) { Remove-Item $signedDirectory -Recurse -Force }
    New-Item $signedDirectory -ItemType Directory | Out-Null
    $signed = Join-Path $signedDirectory 'MyFinder.msix'
    Copy-Item $unsigned $signed
    & "$tools/signtool.exe" sign /fd SHA256 /s My /sha1 $CertificateThumbprint $signed
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
    Export-Certificate -Cert $certificate -FilePath (Join-Path $signedDirectory 'MyFinder.cer') | Out-Null
    Copy-Item (Join-Path $root 'scripts/install.ps1') $signedDirectory
    Copy-Item (Join-Path $root 'README.md') $signedDirectory
    Copy-Item (Join-Path $root 'LICENSE') $signedDirectory
    Compress-Archive "$signedDirectory/*" (Join-Path $OutputDirectory "$name-self-signed.zip") -Force
}

Get-ChildItem $OutputDirectory -File | Where-Object Name -Like "$name-*" | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Packages: $OutputDirectory"
