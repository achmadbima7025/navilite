# Changelog

NaviLite follows [Semantic Versioning 2.0.0](https://semver.org/). While the major version is zero, the application and native API are under active development. New features and breaking changes increment the minor version; compatible fixes increment the patch version. The stable public API begins at 1.0.0.

## [Unreleased]

## [0.1.0] - 2026-10-16

### Added

- Windows x64 WPF connection workspace with saved profiles and environment color badges.
- Rust MySQL/MariaDB TCP connections and required TLS with certificate/hostname verification and optional custom CA certificates.
- Connection authentication check, server version/latency display, open/close session, and database listing.
- Independent metadata and workload pools, a shared Tokio runtime, and a 10-second operation timeout.
- Connection metadata in `%LOCALAPPDATA%/NaviLite/connections.json`; remembered passwords in Windows Credential Manager.
- Staged profile writes with credential recovery and cross-process file locking.
- Generated C# bindings via csbindgen, explicit native memory cleanup, and native/desktop version compatibility checks.
- Build/run scripts, shared version management, and Git initialization.

### Scope

- This first increment covers the connection foundation. SSH tunnels, client TLS certificates, socket transport, object navigation, data editing, query execution, and transfer tools are pending.
