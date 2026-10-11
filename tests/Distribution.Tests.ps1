BeforeAll {
    $root = Split-Path $PSScriptRoot
    [xml]$props = Get-Content "$root/Directory.Build.props" -Raw
    $version = [string]$props.Project.PropertyGroup.Version
    $name = "MyFinder-$version-win-x64"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

Describe 'Build outputs' {
    It 'ships a complete self-contained portable application without signing material' {
        $zip = [IO.Compression.ZipFile]::OpenRead("$root/artifacts/packages/$name-portable.zip")
        try {
            $entries = @($zip.Entries.FullName)
            $entries | Should -Contain 'MyFinder.exe'
            $entries | Should -Contain 'MyFinder.dll'
            $entries | Should -Contain 'MyFinder.runtimeconfig.json'
            $entries | Should -Contain 'coreclr.dll'
            $entries | Should -Contain 'Microsoft.UI.Xaml.dll'
            $entries | Should -Contain 'MyFinder.pri'
            $entries | Should -Contain 'LICENSE'
            @($entries | Where-Object { $_ -match '\.(pfx|p12|key|pem|cer)$' }).Count | Should -Be 0
        }
        finally { $zip.Dispose() }
    }

    It 'creates an unsigned MSIX with consistent identity, assets, and executable' {
        $zip = [IO.Compression.ZipFile]::OpenRead("$root/artifacts/packages/$name-unsigned.msix")
        try {
            $reader = [IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $manifest.Package.Identity.Name | Should -BeExactly 'MyFinder'
            $manifest.Package.Identity.Publisher | Should -BeExactly 'CN=MyFinder'
            $manifest.Package.Identity.Version | Should -BeExactly "$version.0"
            $manifest.Package.Identity.ProcessorArchitecture | Should -BeExactly 'x64'
            $manifest.Package.Applications.Application.Executable | Should -BeExactly 'MyFinder.exe'
            $zip.GetEntry('MyFinder.exe') | Should -Not -BeNullOrEmpty
            $zip.GetEntry('MyFinder.pri') | Should -Not -BeNullOrEmpty
            $zip.GetEntry('Assets/Square150x150Logo.png') | Should -Not -BeNullOrEmpty
            $zip.GetEntry('Assets/Square44x44Logo.png') | Should -Not -BeNullOrEmpty
            $zip.GetEntry('Assets/StoreLogo.png') | Should -Not -BeNullOrEmpty
            $zip.GetEntry('AppxSignature.p7x') | Should -BeNullOrEmpty
        }
        finally { $zip.Dispose() }
    }

    It 'publishes matching SHA-256 checksums' {
        $lines = Get-Content "$root/artifacts/packages/SHA256SUMS.txt"
        $lines.Count | Should -BeGreaterOrEqual 2
        foreach ($line in $lines) {
            $parts = $line -split '  ', 2
            (Get-FileHash "$root/artifacts/packages/$($parts[1])" -Algorithm SHA256).Hash | Should -Be $parts[0]
        }
    }
}

Describe 'Repository scripts' {
    It 'contains valid PowerShell syntax' {
        $scripts = @(Get-ChildItem "$root/scripts/*.ps1") + @(Get-ChildItem "$root/tests/*.ps1")
        foreach ($script in $scripts) {
            $tokens = $null
            $parseErrors = $null
            [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
            @($parseErrors).Count | Should -Be 0 -Because $script.Name
        }
    }
}
