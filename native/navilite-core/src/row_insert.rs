//! SQL text generation from a captured page; no SQL is executed or logged here.
use crate::{cell_value, schema};
use serde::Deserialize;

const MAX_BYTES: usize = 8 * 1024 * 1024;

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct CopyRow {
    pub index: usize,
    pub cells: Vec<CopyCell>,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct CopyCell {
    pub value: Option<String>,
    pub truncated: bool,
}

pub(crate) enum LiteralKind {
    Integer,
    Decimal,
    Float,
    Text,
    Binary,
}

pub(crate) fn literal_kind(column: &schema::Column) -> Result<LiteralKind, String> {
    let sql_type = column.sql_type.to_ascii_lowercase();
    let base = sql_type.split(['(', ' ']).next().unwrap_or("");
    match base {
        "tinyint" | "smallint" | "mediumint" | "int" | "integer" | "bigint" => Ok(LiteralKind::Integer),
        "decimal" | "numeric" => Ok(LiteralKind::Decimal),
        "float" | "double" | "real" => Ok(LiteralKind::Float),
        "char" | "varchar" | "tinytext" | "text" | "mediumtext" | "longtext" | "json"
        | "enum" | "set" | "date" | "datetime" | "time" | "timestamp" | "year" => Ok(LiteralKind::Text),
        "binary" | "varbinary" | "tinyblob" | "blob" | "mediumblob" | "longblob" => Ok(LiteralKind::Binary),
        _ => Err(format!("SQL row copying does not yet support column {}. BIT, spatial, vector, and unknown types require another export format.", column.name)),
    }
}

pub(crate) struct SqlOutput(String);

impl SqlOutput {
    pub fn new() -> Self {
        Self(String::new())
    }

    pub fn finish(self) -> String {
        self.0
    }

    fn reserve(&mut self, bytes: usize) -> Result<(), String> {
        if self.0.len().saturating_add(bytes) > MAX_BYTES {
            return Err("SQL clipboard output is limited to 8 MiB. Select fewer records.".into());
        }
        self.0.reserve(bytes);
        Ok(())
    }

    pub fn push(&mut self, value: &str) -> Result<(), String> {
        self.reserve(value.len())?;
        self.0.push_str(value);
        Ok(())
    }

    pub fn hex(&mut self, value: &[u8]) -> Result<(), String> {
        self.reserve(value.len().saturating_mul(2))?;
        const DIGITS: &[u8; 16] = b"0123456789ABCDEF";
        for byte in value {
            self.0.push(DIGITS[(byte >> 4) as usize] as char);
            self.0.push(DIGITS[(byte & 15) as usize] as char);
        }
        Ok(())
    }

    pub fn literal(&mut self, kind: &LiteralKind, value: Option<&str>) -> Result<(), String> {
        let Some(value) = value else {
            return self.push("NULL");
        };
        match kind {
            LiteralKind::Integer => {
                let digits = value
                    .strip_prefix('-')
                    .or_else(|| value.strip_prefix('+'))
                    .unwrap_or(value);
                if digits.is_empty()
                    || !digits.bytes().all(|byte| byte.is_ascii_digit())
                    || value.parse::<i128>().is_err()
                {
                    return Err("A selected integer value cannot be represented as SQL. No rows were copied.".into());
                }
                self.push(value)
            }
            LiteralKind::Decimal => {
                let unsigned = value
                    .strip_prefix('-')
                    .or_else(|| value.strip_prefix('+'))
                    .unwrap_or(value);
                let mut parts = unsigned.split('.');
                let whole = parts.next().unwrap_or("");
                let fraction = parts.next();
                if whole.is_empty()
                    || !whole.bytes().all(|byte| byte.is_ascii_digit())
                    || fraction.is_some_and(|part| {
                        part.is_empty() || !part.bytes().all(|byte| byte.is_ascii_digit())
                    })
                    || parts.next().is_some()
                    || unsigned.len() > 80
                {
                    return Err("A selected decimal value cannot be represented as SQL. No rows were copied.".into());
                }
                self.push(value)
            }
            LiteralKind::Float => {
                if value.is_empty()
                    || value.len() > 80
                    || !value.bytes().all(|byte| {
                        byte.is_ascii_digit() || matches!(byte, b'+' | b'-' | b'.' | b'e' | b'E')
                    })
                    || !value.parse::<f64>().is_ok_and(f64::is_finite)
                {
                    return Err("A selected floating-point value cannot be represented as SQL. No rows were copied.".into());
                }
                self.push(value)
            }
            LiteralKind::Text => {
                self.push("CONVERT(X'")?;
                self.hex(value.as_bytes())?;
                self.push("' USING utf8mb4)")
            }
            LiteralKind::Binary => {
                if !value.len().is_multiple_of(2)
                    || !value.bytes().all(|byte| byte.is_ascii_hexdigit())
                {
                    return Err("A selected binary value is invalid. No rows were copied.".into());
                }
                self.push("X'")?;
                self.push(value)?;
                self.push("'")
            }
        }
    }
}

pub(crate) fn parse_rows(payload: &str) -> Result<Vec<CopyRow>, String> {
    if payload.len() > MAX_BYTES {
        return Err("SQL copy input is limited to 8 MiB. Select fewer records.".into());
    }
    // A rejected payload can contain database values. Do not echo parser details.
    let mut rows: Vec<CopyRow> = serde_json::from_str(payload)
        .map_err(|_| "Invalid SQL row-copy input. Refresh the page and try again.")?;
    if rows.is_empty() || rows.len() > 1000 {
        return Err("Select between 1 and 1000 records in the current page.".into());
    }
    rows.sort_by_key(|row| row.index);
    if rows.windows(2).any(|pair| pair[0].index == pair[1].index) {
        return Err("SQL copying requires distinct selected records.".into());
    }
    Ok(rows)
}

impl CopyRow {
    pub fn validate(&self, context: &cell_value::PageContext) -> Result<(), String> {
        if self.index >= context.keys.len() || self.cells.len() != context.columns.len() {
            return Err(
                "The selected record does not belong to this data page. Refresh and try again."
                    .into(),
            );
        }
        Ok(())
    }
}

impl CopyCell {
    pub fn complete(&self) -> Result<Option<&str>, String> {
        if self.truncated {
            return Err("A selected assignment value is shortened. No rows were copied. Choose records with complete values or inspect the cell.".into());
        }
        Ok(self.value.as_deref())
    }
}

pub fn copy(page_id: u64, payload: &str) -> Result<String, String> {
    let rows = parse_rows(payload)?;
    let context = cell_value::copy_context(page_id)?;
    if !context.is_base_table {
        return Err(
            "SQL INSERT copying supports base tables. Open a table instead of a view.".into(),
        );
    }
    let columns: Vec<_> = context
        .columns
        .iter()
        .enumerate()
        .filter(|(_, column)| !column.generated)
        .map(|(index, column)| {
            Ok((
                index,
                schema::identifier(&column.name)?,
                literal_kind(column)?,
            ))
        })
        .collect::<Result<_, String>>()?;
    if columns.is_empty() {
        return Err("This table has no non-generated columns to copy.".into());
    }
    let mut sql = SqlOutput::new();
    sql.push("INSERT INTO ")?;
    sql.push(&schema::identifier(&context.database)?)?;
    sql.push(".")?;
    sql.push(&schema::identifier(&context.table)?)?;
    sql.push(" (")?;
    for (position, (_, name, _)) in columns.iter().enumerate() {
        if position > 0 {
            sql.push(", ")?;
        }
        sql.push(name)?;
    }
    sql.push(") VALUES\r\n")?;
    for (position, row) in rows.iter().enumerate() {
        row.validate(&context)?;
        if position > 0 {
            sql.push(",\r\n")?;
        }
        sql.push("(")?;
        for (position, (index, _, kind)) in columns.iter().enumerate() {
            if position > 0 {
                sql.push(", ")?;
            }
            let cell = &row.cells[*index];
            sql.literal(kind, cell.complete()?)?;
        }
        sql.push(")")?;
    }
    sql.push(";\r\n")?;
    Ok(sql.finish())
}
