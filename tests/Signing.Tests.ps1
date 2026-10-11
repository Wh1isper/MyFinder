BeforeAll {
    $root = Split-Path $PSScriptRoot
    $thumbprint = $null
    $thumbprint = & "$root/scripts/new-dev-certificate.ps1"
    $certificate = Get-Item "Cert:/CurrentUser/My/$thumbprint"
    $output = Join-Path $TestDrive 'packages'
    & "$root/scripts/package.ps1" -CertificateThumbprint $thumbprint -OutputDirectory $output
    $bundle = Get-ChildItem "$output/*-self-signed.zip" | Select-Object -First 1
    $extracted = Join-Path $TestDrive 'signed'
    Expand-Archive $bundle.FullName $extracted
}

AfterAll {
    if ($thumbprint -and (Test-Path "Cert:/CurrentUser/My/$thumbprint")) {
        Remove-Item "Cert:/CurrentUser/My/$thumbprint" -DeleteKey
    }
}

Describe 'Self-signed packages' {
    It 'signs the MSIX with the requested certificate' {
        $signature = Get-AuthenticodeSignature "$extracted/MyFinder.msix"
        $signature.SignerCertificate.Thumbprint | Should -BeExactly $thumbprint
        $signature.Status | Should -Not -Be 'HashMismatch'
        $signature.Status | Should -Not -Be 'NotSigned'
    }

    It 'exports only the public certificate and does not grant trust' {
        $public = [Security.Cryptography.X509Certificates.X509Certificate2]::new("$extracted/MyFinder.cer")
        try {
            $public.Thumbprint | Should -BeExactly $thumbprint
            $public.HasPrivateKey | Should -BeFalse
        }
        finally { $public.Dispose() }
        Test-Path "Cert:/LocalMachine/TrustedPeople/$thumbprint" | Should -BeFalse
        Test-Path "Cert:/CurrentUser/TrustedPeople/$thumbprint" | Should -BeFalse
        @(Get-ChildItem $extracted -Recurse -Include '*.pfx', '*.p12', '*.key', '*.pem').Count | Should -Be 0
    }

    It 'includes the explicit installer and trust documentation' {
        Test-Path "$extracted/install.ps1" | Should -BeTrue
        Test-Path "$extracted/README.md" | Should -BeTrue
        Test-Path "$extracted/LICENSE" | Should -BeTrue
    }

    It 'rejects a signing identity that does not match the publisher' {
        $other = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=MyFinder test mismatch' -CertStoreLocation 'Cert:/CurrentUser/My'
        try {
            { & "$root/scripts/package.ps1" -CertificateThumbprint $other.Thumbprint -OutputDirectory (Join-Path $TestDrive 'mismatch') } | Should -Throw '*must exactly match*'
        }
        finally { Remove-Item "Cert:/CurrentUser/My/$($other.Thumbprint)" -DeleteKey }
    }
}

Describe 'MSIX installation lifecycle' -Tag 'Install' {
    BeforeAll {
        $canInstall = $false
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Installation tests require an elevated disposable Windows environment.'
        }
        if (Get-AppxPackage -Name MyFinder) { throw 'Refusing to change an existing MyFinder installation.' }
        $canInstall = $true
        Import-Certificate -FilePath "$extracted/MyFinder.cer" -CertStoreLocation 'Cert:/LocalMachine/TrustedPeople' | Out-Null
    }

    AfterAll {
        if ($canInstall) {
            Get-AppxPackage -Name MyFinder | Remove-AppxPackage
            if (Test-Path "Cert:/LocalMachine/TrustedPeople/$thumbprint") {
                Remove-Item "Cert:/LocalMachine/TrustedPeople/$thumbprint"
            }
        }
    }

    It 'verifies, installs, and uninstalls the signed package using Windows' {
        (Get-AuthenticodeSignature "$extracted/MyFinder.msix").Status | Should -Be 'Valid'
        & "$extracted/install.ps1"
        $installed = Get-AppxPackage -Name MyFinder
        $installed | Should -Not -BeNullOrEmpty
        $installed.Publisher | Should -BeExactly 'CN=MyFinder'
        Remove-AppxPackage -Package $installed.PackageFullName
        Get-AppxPackage -Name MyFinder | Should -BeNullOrEmpty
    }
}
