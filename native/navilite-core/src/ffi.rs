//! Every returned UTF-8 JSON allocation belongs to Rust and must be freed exactly once.

use crate::{
    connection,
    profiles::{self, ConnectionProfile},
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

#[no_mangle]
pub extern "C" fn navilite_profiles_list() -> FfiResult {
    boundary(|| serde_json::to_value(profiles::list()?).map_err(|e| e.to_string()))
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

/// Releases an allocation returned in FfiResult.data. Null is accepted.
/// # Safety
/// Data must be null or an unfreed pointer returned by this library. Never pass a C# allocation.
#[no_mangle]
pub unsafe extern "C" fn navilite_result_free(data: *mut c_char) {
    if !data.is_null() {
        drop(CString::from_raw(data));
    }
}
