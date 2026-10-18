//! Every returned UTF-8 JSON allocation belongs to Rust and must be freed exactly once.

use crate::{
    cell_value, connection, history,
    page_options::PageOptions,
    profile_transfer,
    profiles::{self, ConnectionProfile},
    row_insert, row_update, schema, sql_script, table_page,
};
use serde_json::{json, Value};
use std::{
    ffi::{c_char, CStr, CString},
    panic::{catch_unwind, AssertUnwindSafe},
};
use zeroize::Zeroizing;

#[repr(C)]
pub struct FfiResult {
    pub code: i32,
    pub data: *mut c_char,
}

#[repr(C)]
pub struct FfiBuffer {
    pub code: i32,
    pub data: *mut u8,
    pub length: usize,
}

#[repr(C)]
pub struct FfiPage {
    pub buffer: FfiBuffer,
    pub page_id: u64,
}

fn buffer_boundary(operation: impl FnOnce() -> Result<Vec<u8>, String>) -> FfiBuffer {
    let result = catch_unwind(AssertUnwindSafe(operation))
        .unwrap_or_else(|_| Err("The native data operation failed unexpectedly.".into()));
    let (code, payload) = match result {
        Ok(bytes) => (0, bytes),
        Err(message) => (-1, json!({ "message": message }).to_string().into_bytes()),
    };
    let mut allocation = payload.into_boxed_slice();
    let buffer = FfiBuffer {
        code,
        data: allocation.as_mut_ptr(),
        length: allocation.len(),
    };
    std::mem::forget(allocation);
    buffer
}

fn boundary(operation: impl FnOnce() -> Result<Value, String>) -> FfiResult {
    let result = catch_unwind(AssertUnwindSafe(operation))
        .unwrap_or_else(|_| Err("The native operation failed unexpectedly.".into()));
    let (code, payload) = match result {
        Ok(value) => (0, value),
        Err(message) => (-1, json!({ "message": message })),
    };
    // JSON escapes embedded NUL bytes, so the CString conversion cannot fail.
    let data = CString::new(payload.to_string())
        .expect("JSON cannot contain literal NUL")
        .into_raw();
    FfiResult { code, data }
}

unsafe fn text<'a>(value: *const c_char) -> Result<&'a str, String> {
    if value.is_null() {
        return Err("A required native argument is missing.".into());
    }
    CStr::from_ptr(value)
        .to_str()
        .map_err(|_| "Native arguments must be UTF-8.".into())
}

unsafe fn profile(value: *const c_char) -> Result<ConnectionProfile, String> {
    let profile: ConnectionProfile = serde_json::from_str(text(value)?)
        .map_err(|e| format!("Invalid connection configuration: {e}"))?;
    profile.validate()?;
    Ok(profile)
}

unsafe fn password(value: *const c_char) -> Result<Option<Zeroizing<String>>, String> {
    if value.is_null() {
        Ok(None)
    } else {
        Ok(Some(Zeroizing::new(text(value)?.to_owned())))
    }
}

#[no_mangle]
pub extern "C" fn navilite_core_info() -> FfiResult {
    boundary(|| Ok(json!({ "version": env!("CARGO_PKG_VERSION"), "abiVersion": 1 })))
}

/// Finds offline MySQL statement spans in UTF-16 offsets. Never executes SQL.
/// # Safety
/// Request must be a valid NUL-terminated UTF-8 JSON string for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_sql_script_analyze(request: *const c_char) -> FfiResult {
    boundary(|| {
        serde_json::to_value(sql_script::analyze(text(request)?)?).map_err(|e| e.to_string())
    })
}

#[no_mangle]
pub extern "C" fn navilite_profiles_list() -> FfiResult {
    boundary(|| serde_json::to_value(profiles::list()?).map_err(|e| e.to_string()))
}

/// Exports a versioned metadata-only document. Never accesses the credential store.
#[no_mangle]
pub extern "C" fn navilite_profiles_export() -> FfiResult {
    boundary(|| Ok(json!(profile_transfer::export_document()?)))
}

/// Validates an import and previews duplicates/names without updating profiles or credentials.
/// # Safety
/// Document must be a valid NUL-terminated UTF-8 string for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_profiles_import_preview(document: *const c_char) -> FfiResult {
    boundary(|| {
        serde_json::to_value(profile_transfer::preview(text(document)?)?)
            .map_err(|error| error.to_string())
    })
}

/// Atomically adds selected new profiles with new IDs and no saved passwords.
/// # Safety
/// Document and selection (JSON array of zero-based indices) must be valid NUL-terminated UTF-8 strings.
#[no_mangle]
pub unsafe extern "C" fn navilite_profiles_import(
    document: *const c_char,
    selection: *const c_char,
) -> FfiResult {
    boundary(|| {
        serde_json::to_value(profile_transfer::import(text(document)?, text(selection)?)?)
            .map_err(|error| error.to_string())
    })
}

/// Saves metadata and optionally a Windows credential. A null password preserves the saved password.
/// # Safety
/// Arguments must be valid NUL-terminated UTF-8 strings for the duration of this call; password may be null.
#[no_mangle]
pub unsafe extern "C" fn navilite_profile_save(
    config: *const c_char,
    secret: *const c_char,
) -> FfiResult {
    boundary(|| {
        serde_json::to_value(profiles::save(profile(config)?, password(secret)?)?)
            .map_err(|e| e.to_string())
    })
}

/// Deletes a saved connection profile and its credential.
/// # Safety
/// ID must be a valid NUL-terminated UTF-8 string for the duration of this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_profile_delete(id: *const c_char) -> FfiResult {
    boundary(|| {
        profiles::delete(text(id)?)?;
        Ok(Value::Null)
    })
}

/// Checks authentication without retaining a session or saving configuration.
/// # Safety
/// Arguments must be valid NUL-terminated UTF-8 strings for the duration of this call; password may be null.
#[no_mangle]
pub unsafe extern "C" fn navilite_conn_test(
    config: *const c_char,
    secret: *const c_char,
) -> FfiResult {
    boundary(|| {
        serde_json::to_value(connection::check(profile(config)?, password(secret)?)?)
            .map_err(|e| e.to_string())
    })
}

/// Opens a session with independent metadata and interactive workload pools.
/// # Safety
/// Arguments must be valid NUL-terminated UTF-8 strings for the duration of this call; password may be null.
#[no_mangle]
pub unsafe extern "C" fn navilite_conn_open(
    config: *const c_char,
    secret: *const c_char,
) -> FfiResult {
    boundary(|| {
        serde_json::to_value(connection::open(profile(config)?, password(secret)?)?)
            .map_err(|e| e.to_string())
    })
}

#[no_mangle]
pub extern "C" fn navilite_conn_close(handle: u64) -> FfiResult {
    boundary(|| {
        connection::close(handle)?;
        Ok(Value::Null)
    })
}

#[no_mangle]
pub extern "C" fn navilite_databases_list(handle: u64) -> FfiResult {
    boundary(|| serde_json::to_value(connection::databases(handle)?).map_err(|e| e.to_string()))
}

/// Lists up to 200 internal SQL templates for this session. Bound values are excluded.
#[no_mangle]
pub extern "C" fn navilite_history_list(handle: u64) -> FfiResult {
    boundary(|| serde_json::to_value(history::list(handle)?).map_err(|error| error.to_string()))
}

/// Clears the in-memory SQL history without executing SQL or modifying the server.
#[no_mangle]
pub extern "C" fn navilite_history_clear(handle: u64) -> FfiResult {
    boundary(|| {
        history::clear(handle)?;
        Ok(Value::Null)
    })
}

/// Lists one metadata category without loading other branches.
/// # Safety
/// Database, table, and category must be valid NUL-terminated UTF-8 strings for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_schema_objects(
    handle: u64,
    database: *const c_char,
    table: *const c_char,
    category: *const c_char,
) -> FfiResult {
    boundary(|| {
        serde_json::to_value(schema::objects(
            handle,
            text(database)?,
            text(table)?,
            text(category)?,
        )?)
        .map_err(|e| e.to_string())
    })
}

/// Gets the existing CREATE TABLE or CREATE VIEW definition, without executing it.
/// # Safety
/// Database and table must be valid NUL-terminated UTF-8 strings for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_object_ddl(
    handle: u64,
    database: *const c_char,
    table: *const c_char,
) -> FfiResult {
    boundary(|| Ok(json!(schema::ddl(handle, text(database)?, text(table)?)?)))
}

/// Reads an existing database, table, view, routine, trigger, or event definition and metadata.
/// # Safety
/// Database, name, and kind must be valid NUL-terminated UTF-8 strings for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_object_definition(
    handle: u64,
    database: *const c_char,
    name: *const c_char,
    kind: *const c_char,
) -> FfiResult {
    boundary(|| {
        serde_json::to_value(schema::definition(
            handle,
            text(database)?,
            text(name)?,
            text(kind)?,
        )?)
        .map_err(|error| error.to_string())
    })
}

/// Reads a bounded NVLR v1 page. Its allocation must be released with navilite_buffer_free.
/// # Safety
/// Database and table must be valid NUL-terminated UTF-8 strings for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_table_page(
    handle: u64,
    database: *const c_char,
    table: *const c_char,
    offset: u64,
    page_size: u32,
) -> FfiBuffer {
    buffer_boundary(|| table_page::read(handle, text(database)?, text(table)?, offset, page_size))
}

/// Reads a page using validated column/operator filters and server-side ordering.
/// # Safety
/// Database, table, and options must be valid NUL-terminated UTF-8 strings for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_table_page_query(
    handle: u64,
    database: *const c_char,
    table: *const c_char,
    offset: u64,
    page_size: u32,
    options: *const c_char,
) -> FfiBuffer {
    buffer_boundary(|| {
        let options: PageOptions = serde_json::from_str(text(options)?)
            .map_err(|e| format!("Invalid data filter or sort options: {e}"))?;
        table_page::read_with_options(
            handle,
            text(database)?,
            text(table)?,
            offset,
            page_size,
            options,
        )
    })
}

/// Opens an NVLR v1 page and retains bounded primary-key values for cell inspection.
/// Release the page ID with navilite_page_release and its buffer with navilite_buffer_free.
/// # Safety
/// Database, table, and options must be valid NUL-terminated UTF-8 strings for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_table_page_open(
    handle: u64,
    database: *const c_char,
    table: *const c_char,
    offset: u64,
    page_size: u32,
    options: *const c_char,
) -> FfiPage {
    let mut page_id = 0;
    let buffer = buffer_boundary(|| {
        let options: PageOptions = serde_json::from_str(text(options)?)
            .map_err(|e| format!("Invalid data filter or sort options: {e}"))?;
        let (frame, id) = table_page::open(
            handle,
            text(database)?,
            text(table)?,
            offset,
            page_size,
            options,
        )?;
        page_id = id;
        Ok(frame)
    });
    FfiPage { buffer, page_id }
}

/// Releases a retained page identity. Zero, expired, and already released IDs are accepted.
#[no_mangle]
pub extern "C" fn navilite_page_release(page_id: u64) {
    cell_value::release(page_id);
}

/// Formats selected page values as one bounded INSERT statement; never executes SQL.
/// # Safety
/// Rows must be a valid NUL-terminated UTF-8 JSON array for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_rows_insert_sql(page_id: u64, rows: *const c_char) -> FfiResult {
    boundary(|| Ok(json!(row_insert::copy(page_id, text(rows)?)?)))
}

/// Formats bounded UPDATE statements using captured native primary keys; never executes SQL.
/// # Safety
/// Rows must be a valid NUL-terminated UTF-8 JSON array for this call.
#[no_mangle]
pub unsafe extern "C" fn navilite_rows_update_sql(page_id: u64, rows: *const c_char) -> FfiResult {
    boundary(|| Ok(json!(row_update::copy(page_id, text(rows)?)?)))
}

/// Reads the current cell by the retained row primary key, capped at 1 MiB, as NVLC v1.
/// The returned allocation must be released with navilite_buffer_free.
#[no_mangle]
pub extern "C" fn navilite_cell_read(page_id: u64, row: u32, column: u32) -> FfiBuffer {
    buffer_boundary(|| cell_value::read(page_id, row, column))
}

/// Releases a binary or error buffer, using its original pointer and length. Null is accepted.
/// # Safety
/// Data and length must identify an unfreed allocation returned in FfiBuffer by this library.
#[no_mangle]
pub unsafe extern "C" fn navilite_buffer_free(data: *mut u8, length: usize) {
    if !data.is_null() {
        drop(Box::from_raw(std::ptr::slice_from_raw_parts_mut(
            data, length,
        )));
    }
}

/// Releases an allocation returned in FfiResult.data. Null is accepted.
/// # Safety
/// Data must be null or an unfreed pointer returned by this library. Never pass a C# allocation.
#[no_mangle]
pub unsafe extern "C" fn navilite_result_free(data: *mut c_char) {
    if !data.is_null() {
        drop(CString::from_raw(data));
    }
}
