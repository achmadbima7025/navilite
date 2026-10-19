//! One bounded SELECT per fresh workload connection. No editor SQL enters logs/history.
use crate::{connection, history, query_result::Frame, schema, sql_script};
use mysql_async::{prelude::Queryable, Conn, Error, Params};
use serde::Deserialize;
use std::{
    future::Future,
    pin::pin,
    task::{Context, Waker},
};

/// Owns the session across every await, including result.next() and cancellation.
struct QueryConnection(Option<Conn>);

impl Drop for QueryConnection {
    fn drop(&mut self) {
        let Some(conn) = self.0.take() else {
            return;
        };
        if std::thread::panicking() {
            // The driver's panic Drop path already discards its socket directly.
            drop(conn);
            return;
        }
        // mysql_async 0.37.1 has no public abort API. disconnect() marks the Conn
        // disconnected BEFORE polling clean_dirty(), which otherwise drains every
        // unread row. Poll ONCE and drop the owned future immediately: a Pending
        // cleanup is cancelled and the pool discards the disconnected socket.
        // Do not await/spawn this future or return the raw Conn to a pool. Recheck
        // this ordering in conn/mod.rs and pool/recycler.rs before driver upgrades.
        let mut close = pin!(conn.disconnect());
        let mut context = Context::from_waker(Waker::noop());
        let _ = close.as_mut().poll(&mut context);
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Request {
    script: String,
    caret_utf16: u32,
    #[serde(default)]
    selection_start_utf16: u32,
    #[serde(default)]
    selection_length_utf16: u32,
    #[serde(default)]
    database: String,
    #[serde(default = "default_rows")]
    row_limit: u32,
    #[serde(default = "yes")]
    backslash_escapes: bool,
    #[serde(default)]
    ansi_quotes: bool,
}
fn yes() -> bool {
    true
}
fn default_rows() -> u32 {
    1000
}

fn safe_error(error: Error) -> String {
    match error {
        Error::Server(error) => format!("The server rejected the SQL request (error {}). Server message and SQL text are omitted.", error.code),
        _ => "The SQL connection or protocol failed. Check connectivity and the 8 MiB packet limit; SQL text and server details are omitted.".into(),
    }
}

fn selected_sql(request: &Request) -> Result<String, String> {
    // Always bound the whole document, even when only a short selection is executed.
    if request.script.len() > 1024 * 1024 || request.script.contains('\0') {
        return Err("SQL documents must be NUL-free UTF-8 text of at most 1 MiB.".into());
    }
    let source = if request.selection_length_utf16 != 0 {
        let end = request
            .selection_start_utf16
            .checked_add(request.selection_length_utf16)
            .ok_or("The SQL selection is outside the document.")?;
        &request.script[sql_script::byte_offset(&request.script, request.selection_start_utf16)?
            ..sql_script::byte_offset(&request.script, end)?]
    } else {
        &request.script
    };
    let caret = if request.selection_length_utf16 == 0 {
        request.caret_utf16
    } else {
        0
    };
    let analysis = sql_script::analyze_script(
        source,
        caret,
        request.backslash_escapes,
        request.ansi_quotes,
    )?;
    if request.selection_length_utf16 != 0
        && (analysis.statements.len() != 1 || analysis.delimiter_count != 0)
    {
        return Err("Select exactly one SELECT statement without DELIMITER directives.".into());
    }
    let statement = analysis
        .current_index
        .and_then(|i| analysis.statements.get(i))
        .ok_or("Place the caret on a SELECT statement or select one query.")?;
    if !statement.read_candidate {
        return Err("Run SELECT supports plain SELECT only. Variables, INTO, locking reads, executable/hint comments, and administrative or modifying statements are unavailable.".into());
    }
    Ok(statement.source(source)?.to_owned())
}

fn request(json: &str) -> Result<Request, String> {
    if json.len() > 8 * 1024 * 1024 {
        return Err("SQL execution input is too large.".into());
    }
    let request: Request =
        serde_json::from_str(json).map_err(|_| "Invalid SQL execution request.")?;
    if !(1..=1000).contains(&request.row_limit) {
        return Err("SQL row limit must be between 1 and 1000.".into());
    }
    Ok(request)
}

/// Offline preflight of every statement, using exactly the single-read selection policy.
pub fn prepare_reads(json: &str) -> Result<sql_script::Analysis, String> {
    let mut request = request(json)?;
    if request.selection_length_utf16 != 0 {
        return Err("Run All prepares the entire script, not a text selection.".into());
    }
    let analysis = sql_script::analyze_script(
        &request.script,
        request.caret_utf16,
        request.backslash_escapes,
        request.ansi_quotes,
    )?;
    if analysis.statements.is_empty() || analysis.statements.len() > 8 {
        return Err("Run All requires 1–8 SELECT statements. Use Run SELECT for a single statement in a larger script.".into());
    }
    for (index, statement) in analysis.statements.iter().enumerate() {
        (
            request.selection_start_utf16,
            request.selection_length_utf16,
        ) = statement.bounds();
        selected_sql(&request).map_err(|message| format!("Statement {}: {message}", index + 1))?;
    }
    Ok(analysis)
}

pub fn read(handle: u64, json: &str) -> Result<Vec<u8>, String> {
    let request = request(json)?;
    let sql = selected_sql(&request)?;
    let database = if request.database.is_empty() {
        None
    } else {
        Some(schema::identifier(&request.database)?)
    };
    let pool = connection::workload_pool(handle)?;
    connection::run(history::user_read(handle, &request.database, async {
        let mut connection = QueryConnection(Some(pool.get_conn().await.map_err(safe_error)?));
        let conn = connection.0.as_mut().expect("owned query connection");
        let outcome = async {
            // Reserve a workload slot, then clear any state from its previous borrower.
            if !conn.reset().await.map_err(safe_error)? {
                return Err("Run SELECT requires connection reset support (MySQL 5.7.3+ or MariaDB 10.2.4+).".into());
            }
            let mode: String = conn.query_first("SELECT @@SESSION.sql_mode").await.map_err(safe_error)?.ok_or("The server returned no SQL mode.")?;
            let ansi = mode.split(',').any(|m| m.eq_ignore_ascii_case("ANSI_QUOTES"));
            let escapes = !mode.split(',').any(|m| m.eq_ignore_ascii_case("NO_BACKSLASH_ESCAPES"));
            if ansi != request.ansi_quotes || escapes != request.backslash_escapes {
                return Err(format!("Match the editor options to the server before running: ANSI_QUOTES={}, Backslash escapes in strings={}.", ansi, escapes));
            }
            if let Some(database) = database { conn.query_drop(format!("USE {database}")).await.map_err(safe_error)?; }
            // Session-only ceiling for SELECTs without LIMIT. The streaming reader enforces
            // the same ceiling even when an explicit LIMIT takes precedence on the server.
            conn.query_drop(format!("SET SESSION sql_select_limit = {}", request.row_limit + 1)).await.map_err(safe_error)?;
            conn.query_drop("START TRANSACTION READ ONLY").await.map_err(safe_error)?;
            let statement = conn.prep(sql.as_bytes()).await.map_err(safe_error)?;
            if statement.num_params() != 0 { return Err("Query parameters are not supported in this editor increment.".into()); }
            let mut result = conn.exec_iter(&statement, Params::Empty).await.map_err(safe_error)?;
            let mut frame = Frame::new(result.columns_ref(), request.row_limit as usize)?;
            let mut count = 0;
            let mut more = false;
            while let Some(row) = result.next().await.map_err(safe_error)? {
                if count == request.row_limit { more = true; break; }
                frame.push(row)?;
                count += 1;
            }
            // Do not drain an unbounded result or return this session to a pool.
            drop(result);
            Ok(frame.finish(more))
        }.await;
        // The RAII guard aborts this session on success, error, or outer timeout.
        outcome
    }))
}
