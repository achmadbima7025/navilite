# Changelog

NaviLite follows [Semantic Versioning 2.0.0](https://semver.org/). While the major version is zero, the application and native API are under active development. New features and breaking changes increment the minor version; compatible fixes increment the patch version. The stable public API begins at 1.0.0.

## [Unreleased]

## [0.6.0] - 2026-10-18

### Added

- Up to 16 structured filter conditions combined with all/AND or any/OR, using grouped predicates and typed bound parameters.
- Up to eight unique explicit sort columns, editable priority with move controls, and primary-key tie-break ordering without duplicate columns.
- Dynamic filter/sort draft rows, ignored None rows, semantic pending-change detection, and applied query preservation across pagination and failures.
- Applied sort priorities in grid headers; header clicks replace explicit ordering with a single-column ASC/DESC/default cycle while retaining active filters.
- Additive `filters`, `sorts`, and `matchMode` JSON options with limits and validation, retaining legacy singular `filter`/`sort` input and existing ABI/frame formats.

### Scope

- Filters form one flat AND/OR group. Nested groups, raw SQL expressions, editing, user SQL execution, SQL row-copy formats, SSH, and transfers remain pending.

## [0.5.0] - 2026-10-18

### Added

- CSV/TSV/JSON clipboard copying for current-page grid multi-selection and the active Form View record, with headers, NULL/empty/binary handling, and exact numeric text preservation.
- Atomic copy preparation with an 8 MiB UTF-8 output limit; truncated cells are rejected before clipboard updates, and embedded NUL text requires JSON.
- Session SQL history with start time, native statement duration, operation/context, and success/error/interrupted outcomes, including interruption on operation timeout.
- Native tracing of database/schema/DDL/page/cell reads and explicit read-only transaction boundaries; bound values, results, and server error details are excluded.
- In-memory limits of 200 entries per session and 16 KiB per SQL template, plus additive list/clear FFI APIs and a WPF history viewer with automatic/manual refresh and clear.

### Scope

- Row copying uses complete values from the current page and keeps non-NULL JSON values as strings. SQL INSERT/UPDATE formats and full-table export remain pending. History covers application-issued SQL after connection, excludes authentication and driver setup/reset, and is not persisted to disk. Data editing, user SQL execution, SSH, and transfers remain pending.

## [0.4.0] - 2026-10-18

### Added

- Read-only cell inspection from grid double-click/F2 or Form View, with text, JSON formatting, and virtualized hexadecimal/ASCII views.
- Primary-key identities retained alongside native data pages, supporting bounded on-demand cell reads without locating records by changing page offsets.
- Server-side 1 MiB cell projection, UTF-8 boundary handling, original/loaded byte counts, and explicit partial-value or preview-only explanations.
- Clipboard copying and saving loaded binary bytes or UTF-8 text without modifying database values.
- Additive page-lease and cell-read FFI APIs with NVLC v1 value frames; existing ABI 1, page APIs, and NVLR v1 remain available.

### Fixed

- Prioritize the project's existing local .NET/Rust installations in the activation script when another system installation appears earlier in PATH.

### Scope

- Inspection is read only and bounded at 1 MiB. Missing, oversized, or unsupported primary keys (including BIT, TIMESTAMP, ENUM, and SET) and stale records fall back to the current page preview. JSON formatting supports complete text up to 256 KiB and depth 64. Editing, larger-value streaming, JSON tree/image/date editors, user SQL execution, SSH, and transfers remain pending.

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
