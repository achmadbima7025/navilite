//! NVLR v1: a bounded little-endian frame; see README for the wire layout.
use crate::{connection, page_options::PageOptions, schema};
use mysql_async::{prelude::Queryable, Params, Row, Value};

const MAX_FRAME: usize = 8 * 1024 * 1024;
const WIRE_BUDGET: usize = 4 * 1024 * 1024;
const TEXT_CHARACTERS: usize = 1024;
const BINARY_BYTES: usize = 256;
const HEADER_BYTES: usize = 40;

fn u32_bytes(value: usize) -> [u8; 4] {
    (value as u32).to_le_bytes()
}

fn string(frame: &mut Vec<u8>, value: &str) {
    frame.extend_from_slice(&u32_bytes(value.len()));
    frame.extend_from_slice(value.as_bytes());
}

fn length(value: &Value) -> Result<u64, String> {
    match value {
        Value::UInt(value) => Ok(*value),
        Value::Int(value) if *value >= 0 => Ok(*value as u64),
        Value::Bytes(value) => std::str::from_utf8(value)
            .ok()
            .and_then(|value| value.parse::<u64>().ok())
            .ok_or_else(|| "The server returned an invalid cell length.".into()),
        _ => Err("The server returned an invalid cell length.".into()),
    }
}

fn encode_row(row: Row, columns: &[schema::Column], frame: &mut Vec<u8>) -> Result<u32, String> {
    let values = row.unwrap();
    if values.len() != columns.len() * 2 {
        return Err("The table schema changed during preview. Refresh and open it again.".into());
    }
    let start = frame.len();
    frame.resize(start + columns.len() * 4, 0);
    let mut truncated = 0;
    for (index, (pair, column)) in values.as_chunks::<2>().0.iter().zip(columns).enumerate() {
        let cell = frame.len() - start;
        frame[start + index * 4..start + index * 4 + 4].copy_from_slice(&u32_bytes(cell));
        match &pair[0] {
            Value::NULL => {
                frame.push(0);
                frame.extend_from_slice(&0u32.to_le_bytes());
            }
            Value::Bytes(bytes) => {
                let actual = if column.binary {
                    bytes.len()
                } else {
                    std::str::from_utf8(bytes)
                        .map_err(|_| "The server returned invalid UTF-8 text.")?
                        .chars()
                        .count()
                };
                let shortened = length(&pair[1])? > actual as u64;
                truncated += u32::from(shortened);
                let kind = if column.binary { 2 } else { 1 };
                frame.push(kind | if shortened { 128 } else { 0 });
                frame.extend_from_slice(&u32_bytes(bytes.len()));
                frame.extend_from_slice(bytes);
            }
            _ => return Err("The server returned an unsupported preview value.".into()),
        }
    }
    Ok(truncated)
}

pub fn read(
    handle: u64,
    database: &str,
    table: &str,
    offset: u64,
    requested: u32,
) -> Result<Vec<u8>, String> {
    read_with_options(
        handle,
        database,
        table,
        offset,
        requested,
        PageOptions::default(),
    )
}

pub fn read_with_options(
    handle: u64,
    database: &str,
    table: &str,
    offset: u64,
    requested: u32,
    options: PageOptions,
) -> Result<Vec<u8>, String> {
    if !(1..=1000).contains(&requested) {
        return Err("Page size must be between 1 and 1000 rows.".into());
    }
    let qualified = format!(
        "{}.{}",
        schema::identifier(database)?,
        schema::identifier(table)?
    );
    let pool = connection::workload_pool(handle)?;
    connection::run(async {
        let mut conn = pool.get_conn().await.map_err(|e| e.to_string())?;
        let columns = schema::columns(&mut conn, database, table).await?;
        let row_ceiling: usize = columns
            .iter()
            .map(|c| {
                if c.binary {
                    BINARY_BYTES + 64
                } else {
                    TEXT_CHARACTERS * 4 + 64
                }
            })
            .sum();
        // Reserve space for the lookahead row too. Projection bounds data at the server.
        let limit = (requested as usize).min((WIRE_BUDGET / row_ceiling).saturating_sub(1).max(1));
        let projection = columns.iter().map(|column| {
            let name = schema::identifier(&column.name)?;
            Ok(if column.binary {
                format!("LEFT({name}, {BINARY_BYTES}), OCTET_LENGTH({name})")
            } else {
                format!("LEFT(CAST({name} AS CHAR CHARACTER SET utf8mb4), {TEXT_CHARACTERS}), CHAR_LENGTH(CAST({name} AS CHAR CHARACTER SET utf8mb4))")
            })
        }).collect::<Result<Vec<_>, String>>()?.join(", ");
        let (clauses, parameters, stable_order) = options.clauses(&columns)?;
        let sql = format!(
            "SELECT {projection} FROM {qualified}{clauses} LIMIT {offset}, {}",
            limit + 1
        );
        conn.query_drop("START TRANSACTION READ ONLY")
            .await
            .map_err(|e| e.to_string())?;
        let rows: Result<Vec<Row>, _> = conn.exec(sql, Params::Positional(parameters)).await;
        let rollback = conn.query_drop("ROLLBACK").await;
        let mut rows = rows.map_err(|e| e.to_string())?;
        rollback.map_err(|e| e.to_string())?;
        let has_more = rows.len() > limit;
        rows.truncate(limit);
        let next = offset
            .checked_add(rows.len() as u64)
            .ok_or("Page offset is too large.")?;
        let mut frame = vec![0u8; HEADER_BYTES];
        frame[..4].copy_from_slice(b"NVLR");
        frame[4..8].copy_from_slice(&1u32.to_le_bytes());
        frame[8..12].copy_from_slice(&u32_bytes(columns.len()));
        frame[12..16].copy_from_slice(&u32_bytes(rows.len()));
        frame[16..24].copy_from_slice(&offset.to_le_bytes());
        frame[24..32].copy_from_slice(&next.to_le_bytes());
        let flags = u32::from(has_more) | if stable_order { 2 } else { 0 };
        frame[32..36].copy_from_slice(&flags.to_le_bytes());
        for column in &columns {
            string(&mut frame, &column.name);
            string(&mut frame, &column.sql_type);
        }
        let directory = frame.len();
        let count = rows.len();
        frame.resize(directory + (count + 1) * 4, 0);
        let mut truncated = 0u32;
        for (index, row) in rows.into_iter().enumerate() {
            let position = frame.len();
            frame[directory + index * 4..directory + index * 4 + 4]
                .copy_from_slice(&u32_bytes(position));
            truncated += encode_row(row, &columns, &mut frame)?;
            if frame.len() > MAX_FRAME {
                return Err("The data page exceeds the 8 MiB preview limit.".into());
            }
        }
        let end = frame.len();
        frame[directory + count * 4..directory + count * 4 + 4].copy_from_slice(&u32_bytes(end));
        frame[36..40].copy_from_slice(&truncated.to_le_bytes());
        if frame.len() > MAX_FRAME {
            return Err("The data page exceeds the 8 MiB preview limit.".into());
        }
        Ok(frame)
    })
}
