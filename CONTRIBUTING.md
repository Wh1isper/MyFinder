# Contributing

Read README.md for setup and AGENTS.md for repository conventions. This project currently ships an engineering foundation, not a completed monitoring bar.

## Changes

Start with a concrete problem and expected behavior. Discuss changes to window policy, configuration contracts, installation, or architecture before building them. Implement complete, focused modules rather than broad unfinished scaffolding.

Keep code and documentation in English. Prefer direct code, small ownership boundaries, and function-style tests. Avoid unrelated cleanup and new infrastructure without a present need. Include relevant configuration and documentation with a behavior change.

## Checks

```powershell
./scripts/build.ps1
./scripts/package.ps1
./scripts/test.ps1
```

Install Pester as documented in README.md. Use installation tests only on a disposable elevated Windows environment. For desktop behavior, report the Windows build, display arrangement/scaling, target apps, and observed results; a successful build is not a desktop compatibility test.

## Issues and pull requests

Describe the scenario, expected behavior, actual behavior, and smallest reproduction. Include sanitized logs and relevant display settings. Do not attach credentials or private signing material.

Pull requests should explain motivation, material behavior changes, validation, and known limitations. Keep titles descriptive; explain compatibility-impacting changes. Review generated code before submitting it. No release tag is needed for an ordinary contribution.

## License

Contributions are submitted under the project's BSD-3-Clause license. Do not copy source from reference projects without checking its license and attribution requirements.
