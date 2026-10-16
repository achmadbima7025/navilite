# NaviLite development

- Work code first. Use the PRD and feature specification as context, and deliver working increments.
- Do not create tests or test projects unless the user explicitly requests them. Verify with builds, formatting, static analysis, and manual application checks.
- Keep the WPF presentation layer separate from the Rust database and credential core. Generate C# FFI bindings with csbindgen.
- Never store database passwords in source, logs, or connection metadata. Use Windows Credential Manager for remembered passwords.
- Use Semantic Versioning. Keep Cargo workspace and .NET versions aligned; record released changes in CHANGELOG.md and tag releases as vMAJOR.MINOR.PATCH.
- Never modify the developer's MySQL accounts, server settings, or databases as part of environment verification.
