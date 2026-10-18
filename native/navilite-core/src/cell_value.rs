//! Page-scoped row identities and bounded, read-only cell inspection.
use crate::{connection, history, page_options, schema};
use mysql_async::{prelude::Queryable, Params, Row, Value};
use std::{
    collections::HashMap,
    sync::{
        atomic::{AtomicU64, Ordering},
        Mutex, OnceLock,
    },
};

const MAX_PAGES: usize = 16;
const CELL_BYTES: usize = 1024 * 1024;
static PAGES: OnceLock<Mutex<HashMap<u64, PageContext>>> = OnceLock::new();
static NEXT_PAGE: AtomicU64 = AtomicU64::new(1);

#[derive(Clone)]
pub(crate) struct PageContext {
    pub handle: u64,
    pub database: String,
    pub table: String,
    pub columns: Vec<schema::Column>,
    pub is_base_table: bool,
    pub keys: Vec<Option<Vec<Value>>>,
}

fn pages() -> &'static Mutex<HashMap<u64, PageContext>> {
    PAGES.get_or_init(|| Mutex::new(HashMap::new()))
}

pub(crate) fn supported_key(column: &schema::Column) -> bool {
    let sql_type = column.sql_type.to_ascii_lowercase();
    let base = sql_type.split(['(', ' ']).next().unwrap_or("");
    // Retain only key representations that can be rebound without losing identity.
    // TIMESTAMP local times and ENUM/SET strings can have ambiguous representations.
    matches!(
        base,
        "tinyint"
            | "smallint"
            | "mediumint"
            | "int"
            | "integer"
            | "bigint"
            | "decimal"
            | "numeric"
            | "float"
            | "double"
            | "real"
            | "date"
            | "datetime"
            | "time"
            | "year"
            | "char"
            | "varchar"
            | "tinytext"
            | "text"
            | "mediumtext"
            | "longtext"
            | "binary"
            | "varbinary"
            | "tinyblob"
            | "blob"
            | "mediumblob"
            | "longblob"
    )
}

pub(crate) fn register(context: PageContext) -> Result<u64, String> {
    let mut pages = pages()
        .lock()
        .map_err(|_| "Page registry is unavailable.")?;
    if pages.len() >= MAX_PAGES {
        return Err("Too many data pages are open. Close a data page and try again.".into());
    }
    let id = NEXT_PAGE
        .try_update(Ordering::Relaxed, Ordering::Relaxed, |id| id.checked_add(1))
        .map_err(|_| "Page identifiers are exhausted. Restart NaviLite.")?;
    pages.insert(id, context);
    Ok(id)
}

pub fn release(id: u64) {
    if let Ok(mut pages) = pages().lock() {
        pages.remove(&id);
    }
}

pub(crate) fn copy_context(id: u64) -> Result<PageContext, String> {
    pages()
        .lock()
        .map_err(|_| "Page registry is unavailable.")?
        .get(&id)
        .cloned()
        .ok_or_else(|| "This data page has expired. Refresh it before copying SQL.".into())
}

pub(crate) fn release_session(handle: u64) {
    if let Ok(mut pages) = pages().lock() {
        pages.retain(|_, page| page.handle != handle);
    }
}

pub fn read(id: u64, row: u32, column: u32) -> Result<Vec<u8>, String> {
    let (handle, database, table, columns, keys) = {
        let pages = pages()
            .lock()
            .map_err(|_| "Page registry is unavailable.")?;
        let context = pages
            .get(&id)
            .ok_or("This data page has expired. Refresh it and try again.")?;
        if let Some(key) = context
            .columns
            .iter()
            .find(|column| column.primary_key && !supported_key(column))
        {
            return Err(format!("Loading beyond the preview is unavailable for primary-key column {} ({}). This key type cannot yet be rebound unambiguously.", key.name, key.sql_type));
        }
        let keys = context.keys.get(row as usize)
            .ok_or("Select a record in the current page.")?
            .as_ref().ok_or("Loading beyond the preview requires a primary key with at most 16 KiB of values (4 KiB per key column). This object or record has no usable key.")?;
        (
            context.handle,
            context.database.clone(),
            context.table.clone(),
            context.columns.clone(),
            keys.clone(),
        )
    };
    let selected = columns
        .get(column as usize)
        .ok_or("Select a column in the current page.")?;
    let name = schema::identifier(&selected.name)?;
    let expression = if selected.binary {
        name
    } else {
        format!("CAST({name} AS CHAR CHARACTER SET utf8mb4)")
    };
    let primary: Vec<_> = columns.iter().filter(|column| column.primary_key).collect();
    if primary.is_empty() || primary.len() != keys.len() {
        return Err("This page has no usable primary key. Refresh it and try again.".into());
    }
    let mut predicates = Vec::new();
    let mut parameters = Vec::new();
    for (key, value) in primary.into_iter().zip(keys) {
        let sql_type = key.sql_type.to_ascii_lowercase();
        let base = sql_type.split(['(', ' ']).next().unwrap_or("");
        let (placeholder, parameter) = if matches!(base, "decimal" | "numeric") {
            let Value::Bytes(bytes) = value else {
                return Err("The server returned an invalid decimal key.".into());
            };
            page_options::comparison_parameter(
                key,
                std::str::from_utf8(&bytes)
                    .map_err(|_| "The server returned an invalid decimal key.")?,
            )?
        } else if key.binary {
            ("CAST(? AS BINARY)".into(), value)
        } else {
            ("?".into(), value)
        };
        predicates.push(format!(
            "{} = {placeholder}",
            schema::identifier(&key.name)?
        ));
        parameters.push(parameter);
    }
    let sql = format!(
        "SELECT LEFT(CAST({expression} AS BINARY), {CELL_BYTES}), OCTET_LENGTH({expression}) FROM {}.{} WHERE {} LIMIT 2",
        schema::identifier(&database)?, schema::identifier(&table)?, predicates.join(" AND ")
    );
    let pool = connection::workload_pool(handle)?;
    connection::run(async {
        let mut conn = pool.get_conn().await.map_err(|e| e.to_string())?;
        if schema::columns(&mut conn, handle, &database, &table).await? != columns {
            return Err(
                "The table schema changed. Refresh the data page before inspecting a value.".into(),
            );
        }
        let context = format!("{database}.{table}");
        history::query(
            handle,
            "Begin read",
            &context,
            "START TRANSACTION READ ONLY",
            conn.query_drop("START TRANSACTION READ ONLY"),
        )
        .await?;
        let result: Result<Vec<Row>, _> = history::query(
            handle,
            "Cell value",
            &context,
            &sql,
            conn.exec(&sql, Params::Positional(parameters)),
        )
        .await;
        let rollback = history::query(
            handle,
            "End read",
            &context,
            "ROLLBACK",
            conn.query_drop("ROLLBACK"),
        )
        .await;
        let mut rows = result?;
        rollback?;
        if rows.len() != 1 {
            return Err(
                "The record was removed or its identity changed. Refresh the data page.".into(),
            );
        }
        let values = rows.remove(0).unwrap();
        let mut frame = Vec::with_capacity(24 + CELL_BYTES);
        frame.extend_from_slice(b"NVLC");
        frame.extend_from_slice(&1u32.to_le_bytes());
        let (kind, original, mut payload) = match (&values[0], &values[1]) {
            (Value::NULL, _) => (0u32, 0, Vec::new()),
            (Value::Bytes(bytes), size) => (
                if selected.binary { 2 } else { 1 },
                crate::table_page::length(size)?,
                bytes.clone(),
            ),
            _ => return Err("The server returned an unsupported cell value.".into()),
        };
        if kind == 1 {
            if let Err(error) = std::str::from_utf8(&payload) {
                if error.error_len().is_some() {
                    return Err("The server returned invalid UTF-8 text.".into());
                }
                payload.truncate(error.valid_up_to());
            }
        }
        let flags = kind
            | if original > payload.len() as u64 {
                128
            } else {
                0
            };
        frame.extend_from_slice(&flags.to_le_bytes());
        frame.extend_from_slice(&0u32.to_le_bytes());
        frame.extend_from_slice(&original.to_le_bytes());
        frame.extend_from_slice(&payload);
        Ok(frame)
    })
}
