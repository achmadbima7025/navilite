//! UPDATE text uses native primary-key snapshots; it never executes SQL.
use crate::{
    cell_value,
    row_insert::{literal_kind, parse_rows, LiteralKind, SqlOutput},
    schema,
};
use mysql_async::Value;

fn key_kind(column: &schema::Column) -> Result<LiteralKind, String> {
    let kind = literal_kind(column)?;
    // Floating-point comparison needs a typed round-trip, which portable SQL
    // literals cannot currently guarantee. Timestamp/ENUM/SET keys also have
    // representation ambiguities and are excluded by native key retention.
    if !cell_value::supported_key(column) || matches!(kind, LiteralKind::Float) {
        return Err("SQL UPDATE copying requires unambiguous primary keys. FLOAT/DOUBLE, TIMESTAMP, ENUM/SET, BIT, spatial/vector, and unknown key types are not supported.".into());
    }
    Ok(kind)
}

fn key_literal(sql: &mut SqlOutput, column: &schema::Column, value: &Value) -> Result<(), String> {
    let kind = key_kind(column)?;
    let sql_type = column.sql_type.to_ascii_lowercase();
    let base = sql_type.split(['(', ' ']).next().unwrap_or("");
    if matches!(value, Value::NULL) {
        return Err(
            "The page has an incomplete primary key. Refresh it before copying SQL UPDATE.".into(),
        );
    }
    if column.binary {
        let Value::Bytes(bytes) = value else {
            return Err("The retained binary primary key is invalid. Refresh the page.".into());
        };
        sql.push("X'")?;
        sql.hex(bytes)?;
        return sql.push("'");
    }
    let text =
        match value {
            Value::Int(value) => value.to_string(),
            Value::UInt(value) => value.to_string(),
            Value::Bytes(bytes) => std::str::from_utf8(bytes)
                .map_err(|_| "The retained primary key is not valid UTF-8. Refresh the page.")?
                .to_owned(),
            Value::Date(year, month, day, hour, minute, second, micros) => {
                if base == "date" {
                    format!("{year:04}-{month:02}-{day:02}")
                } else if base == "datetime" {
                    format!(
                    "{year:04}-{month:02}-{day:02} {hour:02}:{minute:02}:{second:02}.{micros:06}"
                )
                } else {
                    return Err(
                    "The retained temporal primary key has an unexpected type. Refresh the page."
                        .into(),
                );
                }
            }
            Value::Time(negative, days, hours, minutes, seconds, micros) if base == "time" => {
                let hours = u64::from(*days) * 24 + u64::from(*hours);
                format!(
                    "{}{hours:02}:{minutes:02}:{seconds:02}.{micros:06}",
                    if *negative { "-" } else { "" }
                )
            }
            _ => return Err(
                "The retained primary-key value cannot be copied as SQL UPDATE. Refresh the page."
                    .into(),
            ),
        };
    match base {
        // YEAR 0 must remain numeric 0: a string such as '0' can mean 2000.
        "year" => sql.literal(&LiteralKind::Integer, Some(&text)),
        "date" | "datetime" | "time" => {
            sql.push("CAST(")?;
            sql.literal(&LiteralKind::Text, Some(&text))?;
            sql.push(match base {
                "date" => " AS DATE)",
                "datetime" => " AS DATETIME(6))",
                _ => " AS TIME(6))",
            })
        }
        _ if matches!(kind, LiteralKind::Text) => {
            // A literal has coercibility 4, so the key column's collation (2)
            // controls equality. CONVERT(... USING utf8mb4) has coercibility 2
            // and can conflict with or override a different column charset.
            sql.push("_utf8mb4 X'")?;
            sql.hex(text.as_bytes())?;
            sql.push("'")
        }
        _ => sql.literal(&kind, Some(&text)),
    }
}

pub fn copy(page_id: u64, payload: &str) -> Result<String, String> {
    let rows = parse_rows(payload)?;
    let context = cell_value::copy_context(page_id)?;
    if !context.is_base_table {
        return Err(
            "SQL UPDATE copying supports base tables. Open a table instead of a view.".into(),
        );
    }
    let primary: Vec<_> = context
        .columns
        .iter()
        .filter(|column| column.primary_key)
        .collect();
    if primary.is_empty() {
        return Err("SQL UPDATE copying requires a primary key. No SQL was copied.".into());
    }
    for column in &primary {
        key_kind(column)?;
    }
    let assignments: Vec<_> = context
        .columns
        .iter()
        .enumerate()
        .filter(|(_, column)| !column.generated && !column.primary_key)
        .map(|(index, column)| {
            Ok((
                index,
                schema::identifier(&column.name)?,
                literal_kind(column)?,
            ))
        })
        .collect::<Result<_, String>>()?;
    if assignments.is_empty() {
        return Err(
            "This table has no non-key, non-generated columns to copy as SQL UPDATE.".into(),
        );
    }
    let qualified = format!(
        "{}.{}",
        schema::identifier(&context.database)?,
        schema::identifier(&context.table)?
    );
    let mut sql = SqlOutput::new();
    for row in rows {
        row.validate(&context)?;
        let keys = context.keys[row.index].as_ref()
            .filter(|keys| keys.len() == primary.len())
            .ok_or("SQL UPDATE copying needs a complete retained primary key (at most 4 KiB per key column and 16 KiB per record). Refresh the page or choose another record.")?;
        sql.push("UPDATE ")?;
        sql.push(&qualified)?;
        sql.push(" SET ")?;
        for (position, (index, name, kind)) in assignments.iter().enumerate() {
            if position > 0 {
                sql.push(", ")?;
            }
            sql.push(name)?;
            sql.push(" = ")?;
            sql.literal(kind, row.cells[*index].complete()?)?;
        }
        sql.push(" WHERE ")?;
        for (position, (column, value)) in primary.iter().zip(keys).enumerate() {
            if position > 0 {
                sql.push(" AND ")?;
            }
            sql.push(&schema::identifier(&column.name)?)?;
            sql.push(" = ")?;
            key_literal(&mut sql, column, value)?;
        }
        sql.push(" LIMIT 1;\r\n")?;
    }
    Ok(sql.finish())
}
