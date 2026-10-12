# Repository Guide

MyFinder is an independently implemented, movable Windows monitoring panel. Read README.md for implemented capabilities and commands, and spec/README.md for target product design; planned features are not completed behavior.

## Working rules

- Communicate with the project owner in Chinese. Write code, identifiers, commits, and documentation in English.
- Read the affected source, tests, and contracts before changing behavior.
- Prefer simple direct implementations. Do not introduce plugin frameworks, generic service layers, speculative recovery mechanisms, or defensive checks without an actual requirement.
- Deliver complete capability modules: state and configuration, behavior, UI, meaningful tests, and documentation together. Do not substitute mock metrics or staged demos for completed modules.
- Keep implementation on one primary working line. Use subagents only for bounded exploration or justified independent review unless the owner requests otherwise.
- Preserve unrelated changes. Ask before changing product scope or unresolved externally visible behavior.
- Keep operational documentation in root Markdown files and conceptual design in spec/. Do not create a docs/ directory unless requested.
- Keep spec/ at high-level design scope: explain goals, responsibility boundaries, technical choices, rationale and tradeoffs in prose, supported by Mermaid architecture diagrams and flowcharts. Do not replace explanations with diagrams alone or expand into code, API signatures or class designs. Follow the adopted defaults and scope in spec/08-design-baseline.md. Record future changes explicitly; never confuse decided requirements with verified implementation.

## Structure and boundaries

- src/MyFinder owns the WinUI application. Windows-specific code belongs near its concrete owner, not in a speculative abstraction layer.
- scripts owns build/package/install behavior shared by developers and CI.
- tests uses function-style specifications, not test classes. The foundation uses Pester for actual artifact and desktop integration checks.
- packaging owns MSIX identity and assets. Directory.Build.props owns the application version; package.ps1 derives the MSIX version.
- Add files and projects when real behavior needs them, not merely to anticipate a future architecture.
- Never copy third-party source into the BSD-3-Clause project without checking license compatibility and preserving required notices.

## Desktop behavior

The panel is normally persistent, movable, and available in horizontal or vertical layouts. Edge alignment is only a placement aid: do not reintroduce edge auto-hide, delayed collapse, or edge wake-up zones. A persistent tray entry owns explicit show/hide, settings, and exit.

Manual visibility intent takes precedence over automatic fullscreen retreat. Retreat is local to the panel's display and must not overwrite user intent; leaving fullscreen must not reshow a manually hidden panel or reopen dismissed details. Design placement, DPI, focus, input hit testing, and detail lifetimes together. Distinguish maximized, borderless-fullscreen, and exclusive-fullscreen applications. Do not promise universal overlay compatibility. Do not inject into other processes or replace Explorer.

Keep orientation, style and readable size independent. The compact footprint must not grow with the monitored drive count. Monitor, compact-pin and detail-inspection selections are separate; changing inspection preserves other resources' collection and histories. Follow system light/dark appearance consistently; optional background opacity must not fade foreground data. The Windows 10+ product target is not the current Windows 11 implementation baseline.

Monitoring must distinguish unavailable data from a real zero. Share sampling where appropriate, bound history, and avoid expensive drawing for closed detail panels. Click-to-open details include larger recent-trend charts, meaningful part-to-whole charts, and exact resource facts. Keep units, resource identity, timestamps, gaps, and capacity-versus-activity meaning explicit. Do not run the whole application as administrator to obtain optional sensor data.

## Validation

Run scripts/build.ps1, scripts/package.ps1, and scripts/test.ps1 for changes affecting the foundation. Check README.md for prerequisites. Installation tests require explicit -Install on a disposable elevated Windows environment; ordinary tests never change certificate trust or install packages.

Add focused behavior tests as modules are implemented. Do not claim build checks prove fullscreen, mixed-DPI, hot-plug, sleep, or Explorer-restart behavior; those require matching integration evidence. Report unavailable checks and failures honestly.

Use independent review for material changes to architecture, authorization, signing, installation, persistence, concurrency, or cross-component contracts. Verify findings before changing code; clean reviews are valid.

## Signing, publication, and CI

- Never commit credentials, signing private keys, certificates, or local build outputs.
- Certificate creation, certificate trust, package installation, and publication are separate operations. Never silently import into a trust store or bypass Windows signature validation.
- Unpackaged source builds must remain usable without a signing certificate.
- Test private keys are temporary and must be cleaned up. Do not include test-signed artifacts in release uploads.
- Use readable major-version tags for GitHub-owned Actions. Give CI read permissions; restrict release write permission to the publication job.
- Treat pull-request code as untrusted. Do not check it out in privileged pull_request_target jobs or expose release secrets to it.
- Do not create tags, publish releases, or modify remote repository policy beyond the user's authorization.
