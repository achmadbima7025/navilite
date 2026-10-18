use crate::schema::{self, Column};
use mysql_async::Value;
use serde::Deserialize;

#[derive(Default, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PageOptions {
    pub filter: Option<Filter>,
    pub sort: Option<Sort>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Filter {
    column: String,
    operator: Operator,
    value: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
enum Operator {
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    GreaterThan,
    LessThan,
    IsNull,
    IsNotNull,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Sort {
    column: String,
    direction: Direction,
}

#[derive(Deserialize)]
#[serde(rename_all = "lowercase")]
enum Direction {
    Asc,
    Desc,
}

fn column<'a>(columns: &'a [Column], name: &str) -> Result<&'a Column, String> {
    columns
        .iter()
        .find(|column| column.name == name)
        .ok_or_else(|| {
            "A filter or sort column no longer exists. Refresh the table and choose a column again."
                .into()
        })
}

pub(crate) fn comparison_parameter(
    column: &Column,
    input: &str,
) -> Result<(String, Value), String> {
    let sql_type = column.sql_type.to_ascii_lowercase();
    let base = sql_type.split(['(', ' ']).next().unwrap_or("");
    let number = input.trim();
    let invalid = || format!("Enter a valid {base} number for {}.", column.name);
    let value = match base {
        "tinyint" | "smallint" | "mediumint" | "int" | "integer" | "bigint" => number
            .parse::<i64>()
            .map(Value::Int)
            .or_else(|_| number.parse::<u64>().map(Value::UInt))
            .map_err(|_| invalid())?,
        "float" => {
            let value = number.parse::<f32>().map_err(|_| invalid())?;
            if !value.is_finite() {
                return Err(invalid());
            }
            Value::Float(value)
        }
        "double" | "real" => {
            let value = number.parse::<f64>().map_err(|_| invalid())?;
            if !value.is_finite() {
                return Err(invalid());
            }
            Value::Double(value)
        }
        "decimal" | "numeric" => {
            let unsigned = number.strip_prefix(['+', '-']).unwrap_or(number);
            let mut parts = unsigned.split('.');
            let whole = parts.next().unwrap_or("");
            let fraction = parts.next().unwrap_or("");
            let digits = whole.len() + fraction.len();
            // MySQL permits scale 30; use the declared scale for MariaDB columns above 30.
            let declared_scale = sql_type
                .split_once(',')
                .and_then(|(_, scale)| scale.split(')').next())
                .and_then(|scale| scale.trim().parse::<usize>().ok())
                .unwrap_or(0);
            if parts.next().is_some()
                || !(1..=65).contains(&digits)
                || fraction.len() > declared_scale.max(30)
                || !whole
                    .bytes()
                    .chain(fraction.bytes())
                    .all(|byte| byte.is_ascii_digit())
            {
                return Err(invalid());
            }
            // Preserve exact decimal comparison instead of coercing a string to double.
            return Ok((
                format!("CAST(? AS DECIMAL(65, {}))", fraction.len()),
                Value::Bytes(number.as_bytes().to_vec()),
            ));
        }
        _ => Value::Bytes(input.as_bytes().to_vec()),
    };
    Ok(("?".into(), value))
}

impl PageOptions {
    pub fn clauses(&self, columns: &[Column]) -> Result<(String, Vec<Value>, bool), String> {
        let mut sql = String::new();
        let mut values = Vec::new();
        if let Some(filter) = &self.filter {
            let metadata = column(columns, &filter.column)?;
            let name = schema::identifier(&metadata.name)?;
            let null_filter = matches!(filter.operator, Operator::IsNull | Operator::IsNotNull);
            if metadata.binary && !null_filter {
                return Err("Binary columns support IS NULL and IS NOT NULL filters. Text comparisons are unavailable for binary data.".into());
            }
            let mut operator = match filter.operator {
                Operator::Equals => "= ?",
                Operator::NotEquals => "<> ?",
                Operator::Contains | Operator::StartsWith => "LIKE ? ESCAPE '!'",
                Operator::GreaterThan => "> ?",
                Operator::LessThan => "< ?",
                Operator::IsNull => "IS NULL",
                Operator::IsNotNull => "IS NOT NULL",
            }
            .to_owned();
            if !null_filter {
                let value = filter
                    .value
                    .as_ref()
                    .ok_or("Enter a filter value. Use IS NULL to find NULL cells.")?;
                if value.chars().count() > 4096 {
                    return Err("Filter values are limited to 4096 characters.".into());
                }
                let escaped = || {
                    value
                        .replace('!', "!!")
                        .replace('%', "!%")
                        .replace('_', "!_")
                };
                let value = match filter.operator {
                    Operator::Contains => Value::Bytes(format!("%{}%", escaped()).into_bytes()),
                    Operator::StartsWith => Value::Bytes(format!("{}%", escaped()).into_bytes()),
                    _ => {
                        let (placeholder, value) = comparison_parameter(metadata, value)?;
                        operator = operator.replace('?', &placeholder);
                        value
                    }
                };
                values.push(value);
            }
            sql.push_str(&format!(" WHERE {name} {operator}"));
        }
        let mut ordering = Vec::new();
        if let Some(sort) = &self.sort {
            let name = schema::identifier(&column(columns, &sort.column)?.name)?;
            let direction = match sort.direction {
                Direction::Asc => "ASC",
                Direction::Desc => "DESC",
            };
            ordering.push(format!("{name} {direction}"));
        }
        let keys: Vec<_> = columns.iter().filter(|column| column.primary_key).collect();
        for key in &keys {
            if !self
                .sort
                .as_ref()
                .is_some_and(|sort| sort.column == key.name)
            {
                ordering.push(format!("{} ASC", schema::identifier(&key.name)?));
            }
        }
        if !ordering.is_empty() {
            sql.push_str(&format!(" ORDER BY {}", ordering.join(", ")));
        }
        Ok((sql, values, !keys.is_empty()))
    }
}
