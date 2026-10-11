# MyFinder

A Windows monitoring bar built with C# and WinUI 3.

**Status: engineering foundation, not a finished monitoring application.** The current application opens a development window. Build, package, and test commands are provided; CPU/network/memory/disk widgets and desktop-bar behavior are not implemented yet. No mock metrics are presented as real monitoring.

## Direction

Deliver complete modules, including configuration, failure behavior, tests, and documentation:

- A top bar and compact indicator with automatic hiding, delayed collapse, and configurable fullscreen hiding.
- Per-display placement and settings, mixed-DPI layouts, display hot-plug, and recovery after sleep or Explorer restart.
- Built-in CPU, memory, network, and disk monitoring with shared sampling and detail/history panels.
- Explicit configuration for display selection, visibility policies, timing, widgets, and sampling.

Fullscreen overlays are not universally guaranteed, particularly for exclusive-fullscreen games. The implementation must distinguish maximized windows, borderless fullscreen, and exclusive fullscreen. No process injection or Explorer replacement is planned.

## Build and run from source

Requirements:

- Windows 11 x64 (build 22000 or newer).
- The .NET 10 SDK specified in [global.json](global.json), available as `dotnet` on PATH.
- Windows PowerShell 5.1 or PowerShell 7.
- Internet access for the first NuGet restore. Windows SDK build tools are restored as a pinned package; Visual Studio is optional.

If you installed the SDK into a user-owned directory with Microsoft's `dotnet-install.ps1`, prepend that directory to PATH in your current shell before building. For example:

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
dotnet --version
```

From a checkout, run:

```powershell
./scripts/build.ps1
./artifacts/app/MyFinder.exe
```

The output includes the .NET and Windows App SDK runtimes. Keep the entire output directory together; this is not a single-file executable. Running this build requires no signing certificate, package installation, or administrator privileges. It does not enable startup, modify work areas, or install trust certificates.

To keep a source-built installation, copy the entire `artifacts/app` directory to a user-owned location and create a shortcut to `MyFinder.exe`. Stop the app before replacing that directory when updating. Delete the directory and shortcut to remove it.

## Package

```powershell
./scripts/package.ps1
```

`artifacts/packages` contains:

| Output | Purpose |
| --- | --- |
| `MyFinder-<version>-win-x64-portable.zip` | Extract and run; no certificate trust needed. |
| `MyFinder-<version>-win-x64-unsigned.msix` | Input for signing; Windows will not normally install it as-is. |
| `SHA256SUMS.txt` | SHA-256 checksums for the generated package files. |

These packages are development builds, not a claim of production readiness. x64 is the currently supported architecture.

### Self-signed MSIX

A self-signed certificate does **not** provide a publicly verified publisher identity and does not automatically avoid Windows warnings. Prefer building locally. Never trust an unfamiliar certificate merely to dismiss a warning.

Create a local signing certificate once, then reuse its thumbprint for later builds:

```powershell
$thumbprint = ./scripts/new-dev-certificate.ps1
./scripts/package.ps1 -CertificateThumbprint $thumbprint
```

The certificate's private key stays non-exportable in your current-user certificate store. The script does not grant trust. `MyFinder-<version>-win-x64-self-signed.zip` includes only the signed MSIX, public `.cer`, install script, README, and license. No private key is included. The package publisher is `CN=MyFinder`; a supplied certificate must match exactly.

Extract the archive. If you choose to trust **your own** signing certificate, inspect it first:

```powershell
Get-PfxCertificate ./MyFinder.cer | Format-List Subject, Thumbprint, NotAfter
```

Verify the thumbprint against the certificate you created (or an independently verified publisher). Then, in an **administrator PowerShell**, explicitly trust that certificate in Trusted People, not Trusted Root:

```powershell
Import-Certificate -FilePath ./MyFinder.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Return to a normal PowerShell under the account that will use the app:

```powershell
./install.ps1
```

`install.ps1` only calls `Add-AppxPackage`. It never imports certificates, elevates itself, or bypasses signature validation. Enterprise policy can still prohibit sideloading. Retain the same signing certificate and increment the version for updates; plan renewal before its two-year expiration. This development signing path does not use a timestamp service.

Uninstall through Windows Settings, or:

```powershell
Get-AppxPackage -Name MyFinder | Remove-AppxPackage
```

After uninstalling packages that rely on it, remove only the trusted certificate you added, using its verified thumbprint in an administrator PowerShell:

```powershell
Remove-Item "Cert:\LocalMachine\TrustedPeople\<thumbprint>"
```

Do not remove your signing certificate from `CurrentUser\My` while you still need it for updates. Certificates and private keys are never committed to this repository.

## Test

Install the pinned test dependency once:

```powershell
Install-Module Pester -RequiredVersion 5.7.1 -Scope CurrentUser -Repository PSGallery -Force -SkipPublisherCheck
```

Pester documents `-SkipPublisherCheck` for side-by-side installation because Windows ships an older Microsoft-signed Pester while current versions have a different publisher certificate. This installs the pinned community-maintained module; it does not alter Windows application signing or trust settings.

Run the same build and package checks used by CI:

```powershell
./scripts/build.ps1
./scripts/package.ps1
./scripts/test.ps1
```

Tests use function-style Pester specifications. They inspect actual archives and manifests, start and close the compiled application, and exercise self-signing with a temporary certificate. The default test run never imports a trusted certificate or installs an app. Reports are written to `TestResults/results.xml`.

On a **disposable Windows machine with an interactive desktop**, an administrator can additionally run:

```powershell
./scripts/test.ps1 -Install
```

This explicitly enables installation tests that temporarily trust their own test certificate, install the MSIX, and remove both afterwards. They refuse to run over an existing MyFinder installation. Do not run these tests on your daily workstation unless you intend those changes.

## Repository and automation

- `src/MyFinder`: the application; no speculative service framework or plugin layer.
- `scripts`: shared local/CI build, test, packaging, and signing commands.
- `tests`: executable packaging, launch, and installation checks.
- `packaging`: MSIX manifest and original application assets.
- `.github/workflows/ci.yml`: Windows build, tests, packaging, and retained test reports on pull requests and `main`.
- `.github/workflows/release.yml`: reruns CI for a `v<version>` tag and creates a **draft** GitHub Release with unsigned artifacts. No Store, package-registry, or automatic public distribution is configured.

The release tag must match `Directory.Build.props`. GitHub provides the corresponding source archive. Self-signed builds are created locally with a user's own certificate; the release workflow neither generates a new public trust identity per release nor stores signing keys. Keep release tags immutable. The initial repository setup does not create a release tag.

For a dependency update, change the package version, run `dotnet restore src/MyFinder/MyFinder.csproj --force-evaluate`, commit the updated lock file, and run the checks. Normal builds use locked restore.

Documentation stays in root Markdown files for now; there is no separate `docs/` site.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md). Read [SECURITY.md](SECURITY.md) before reporting a vulnerability.

## License

[BSD-3-Clause](LICENSE). Third-party dependencies retain their own licenses. YASB and TrafficMonitor are behavior references, not source code included in this project.
