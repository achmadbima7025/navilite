# Changelog

NaviLite follows [Semantic Versioning 2.0.0](https://semver.org/). While the major version is zero, the application and native API are under active development. New features and breaking changes increment the minor version; compatible fixes increment the patch version. The stable public API begins at 1.0.0.

## [Unreleased]

## [0.3.0] - 2026-10-16

### Added

- Server-side structured data filtering with eight comparison/NULL operators, literal Contains/Starts with matching, and parameterized values.
- Numeric parameter validation, 64-bit integer parameters, matching FLOAT/DOUBLE precision, and exact decimal comparison without floating-point conversion.
- Single-column ASC/DESC sorting from controls or grid headers, with primary-key tie-break ordering when available.
- Applied query state across pagination/refresh, pending-draft indicators, and preservation of the current result on request failure.
- Read-only Form View with synchronized record selection, local record navigation, column types, NULL/binary/truncation indicators, and Ctrl+G grid/form switching.
- Additive `navilite_table_page_query` FFI API, retaining the previous page API, ABI 1, and NVLR v1 frame format.

### Scope

- One filter condition and one explicit sort column per data preview. Form View displays the same bounded cell previews as the grid. Editing, full-value inspection, compound queries, user SQL execution, SSH, and transfer tools remain pending.

## [0.2.0] - 2026-10-16

### Added

- Lazy database object tree for tables, views, procedures, functions, triggers, and events, plus columns, indexes, and foreign keys.
- Local filtering of loaded object names, expandable metadata branches, and existing object DDL inspection.
- Read-only table/view preview with previous/next/reload, primary-key ordering when available, and adaptive pages of up to 1,000 rows.
- Server-side text/BLOB projection limits, explicit truncated-cell markers, and an 8 MiB native frame limit.
- NVLR v1 binary frames with row/cell offset directories, generated buffer FFI, validated SafeHandle ownership, and per-cell UTF-8/hex display.
- WPF data explorer workspace with row/column virtualization and metadata/data pool separation.

### Fixed

- Reapply the utf8mb4 session setup after pooled connections are reset, preserving non-ASCII metadata and preview text on servers with other default character sets.

### Scope

- The grid is read only. SQL filtering/sorting, batch editing, Form View, full-value editors, user SQL execution, SSH, and transfer tools remain pending.

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
