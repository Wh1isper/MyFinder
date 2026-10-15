# MyFinder

[![CI](https://github.com/Wh1isper/MyFinder/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Wh1isper/MyFinder/actions/workflows/ci.yml) [![License: BSD-3-Clause](https://img.shields.io/badge/license-BSD--3--Clause-blue)](LICENSE)

A movable, persistent Windows monitoring panel built with C# and WinUI 3.

**Status: CPU/memory monitoring and the desktop panel are implemented; network and storage are in progress.** The application samples real Windows counters, retains bounded recent history, and provides a tray-owned panel, details and persistent settings. See [MONITORING.md](MONITORING.md) for source meanings, controls and verification boundaries.

## Direction

Deliver complete modules, including configuration, failure behavior, tests, and documentation:

- A normally visible, movable panel with horizontal and vertical layouts, optional four-edge alignment, and position locking. No edge auto-hide or delayed collapse.
- A persistent tray entry for explicit show/hide, settings, and exit, with configurable fullscreen retreat on the panel's display that preserves manual visibility intent.
- Per-display placement, mixed-DPI layouts, display hot-plug, and recovery after sleep or Explorer restart.
- Built-in CPU, memory, network, and storage monitoring, with click-to-open larger detail views, recent line charts, meaningful pie/ring charts, and resource facts such as disk capacity.
- Concurrent monitoring of multiple drives, with independent compact-panel selection and detail inspection; switching inspected resources does not reset their histories.
- Adjustable compact size, three styles, and system-following light/dark appearance. Optional background-only opacity preserves foreground readability, with a solid fallback.
- Explicit preferences for placement, layout, visibility, selected metrics/resources, observation cadence, and recent-history presentation.

The adopted compatibility target is Windows 10 21H2 build 19044 or newer, x64; this is not a vendor lifecycle-support claim. Matching runtime and packaging tests are required before that target becomes a shipped capability. **The current build still requires Windows 11 build 22000 or newer.** Design images do not establish runtime or packaging compatibility.

Read the [design specification](spec/README.md) from overview to detailed flows, and compare the [visual prototypes](spec/prototypes/README.md). They describe the target product, not implemented capabilities. The [design principles and boundaries](spec/08-design-baseline.md) connect the high-level concepts; concrete tuning parameters and verification environments belong to implementation planning.

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

Clone the repository, then build:

```powershell
git clone https://github.com/Wh1isper/MyFinder.git
cd MyFinder
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

Tests use function-style Pester specifications and a test-only .NET scenario host invoking production observation logic. They inspect actual archives and manifests, exercise real CPU/memory collection and native UI controls with isolated profiles, and exercise self-signing with a temporary certificate. The default test run never imports a trusted certificate or installs an app. Reports are written to `TestResults/results.xml`.

On a **disposable Windows machine with an interactive desktop**, an administrator can additionally run:

```powershell
./scripts/test.ps1 -Install
```

This explicitly enables installation tests that temporarily trust their own test certificate, install the MSIX, and remove both afterwards. They refuse to run over an existing MyFinder installation. Do not run these tests on your daily workstation unless you intend those changes.

## Repository and automation

- `src/MyFinder`: WinUI windows, tray, native desktop integration and application lifetime.
- `src/MyFinder.Core`: preferences, presentation policy, native collection and bounded history shared with the test-only scenario host.
- `scripts`: shared local/CI build, test, packaging, and signing commands.
- `tests`: executable packaging, launch, and installation checks.
- `packaging`: MSIX manifest and original application assets.
- `spec`: high-level product and technical design, with written rationale, architecture diagrams, interaction flows, and review decisions.
- `.github/workflows/ci.yml`: Windows build, tests, packaging, and retained test reports on pull requests and `main`.
- `.github/workflows/release.yml`: reruns CI for a `v<version>` tag and creates a **draft** GitHub Release with unsigned artifacts. No Store, package-registry, or automatic public distribution is configured.

The release tag must match `Directory.Build.props`. GitHub provides the corresponding source archive. Self-signed builds are created locally with a user's own certificate; the release workflow neither generates a new public trust identity per release nor stores signing keys. Keep release tags immutable. The initial repository setup does not create a release tag.

For a dependency update, change the package version, run `dotnet restore src/MyFinder/MyFinder.csproj --force-evaluate`, commit the updated lock file, and run the checks. Normal builds use locked restore.

Build and contributor documentation stays in root Markdown files; conceptual design lives in `spec/`. There is no separate `docs/` site.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md). Read [SECURITY.md](SECURITY.md) before reporting a vulnerability.

## Sponsor

Support development through [GitHub Sponsors: Wh1isper](https://github.com/sponsors/Wh1isper). Sponsorship helps fund ongoing development and maintenance; it does not change the project's open-source license.

## License

[BSD-3-Clause](LICENSE). Third-party dependencies retain their own licenses. YASB and TrafficMonitor are behavior references, not source code included in this project.
