[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject 'CN=MyFinder' `
    -FriendlyName 'MyFinder local package signing' `
    -CertStoreLocation 'Cert:/CurrentUser/My' `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy NonExportable `
    -NotAfter (Get-Date).AddYears(2)

# Only the public certificate is exported by package.ps1. No trust store is changed here.
Write-Host "Created a signing certificate, valid until $($certificate.NotAfter.ToString('u'))."
Write-Host 'Keep this certificate to sign future updates. It is not automatically trusted.'
$certificate.Thumbprint
