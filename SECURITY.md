# Security Policy

Use the repository's GitHub **Security > Report a vulnerability** entry to report suspected vulnerabilities privately. Do not disclose vulnerability details, credentials, certificates with private keys, or personal data in public issues. Include the affected commit/version, reproduction, and impact.

This is a development-stage project. Security fixes target the current main branch; there is no long-term-support release policy yet.

The application is intended to run as a normal user. The source-build path does not require certificate trust. Self-signed packages require a separate, informed trust decision; a self-signed certificate is not a verified publisher identity. Trust scripts must not silently grant trust, and installation must retain Windows signature validation.
