use crate::{connection, history};
use mysql_async::{params, prelude::Queryable, Conn, Row};
use serde::Serialize;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SchemaObject {
    pub name: String,
    pub kind: String,
    pub detail: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ObjectDefinition {
    pub database: String,
    pub name: String,
    pub kind: String,
    pub sql: String,
    pub properties: Vec<DefinitionProperty>,
}

#[derive(Serialize)]
pub struct DefinitionProperty {
    pub name: String,
    pub value: Option<String>,
}

#[derive(Clone, PartialEq, Eq)]
pub struct Column {
    pub name: String,
    pub sql_type: String,
    pub binary: bool,
    pub primary_key: bool,
    pub generated: bool,
}

pub fn identifier(value: &str) -> Result<String, String> {
    if value.is_empty() || value.contains('\0') || value.chars().count() > 64 {
        return Err(
            "Database and object names must contain between 1 and 64 characters, without NUL."
                .into(),
        );
    }
    Ok(format!("`{}`", value.replace('`', "``")))
}

pub fn objects(
    handle: u64,
    database: &str,
    table: &str,
    category: &str,
) -> Result<Vec<SchemaObject>, String> {
    identifier(database)?;
    let (kind, sql) = match category {
        "tables" => ("table", "SELECT TABLE_NAME, CONCAT(COALESCE(ENGINE, ''), ' · ~', COALESCE(TABLE_ROWS, 0), ' rows') FROM information_schema.TABLES WHERE TABLE_SCHEMA = :database AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME"),
        "views" => ("view", "SELECT TABLE_NAME, COALESCE(TABLE_COMMENT, '') FROM information_schema.TABLES WHERE TABLE_SCHEMA = :database AND TABLE_TYPE = 'VIEW' ORDER BY TABLE_NAME"),
        "procedures" => ("procedure", "SELECT ROUTINE_NAME, ROUTINE_TYPE FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA = :database AND ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_NAME"),
        "functions" => ("function", "SELECT ROUTINE_NAME, COALESCE(DATA_TYPE, '') FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA = :database AND ROUTINE_TYPE = 'FUNCTION' ORDER BY ROUTINE_NAME"),
        "triggers" => ("trigger", "SELECT TRIGGER_NAME, CONCAT(ACTION_TIMING, ' ', EVENT_MANIPULATION, ' · ', EVENT_OBJECT_TABLE) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = :database ORDER BY TRIGGER_NAME"),
        "events" => ("event", "SELECT EVENT_NAME, STATUS FROM information_schema.EVENTS WHERE EVENT_SCHEMA = :database ORDER BY EVENT_NAME"),
        "columns" => ("column", "SELECT COLUMN_NAME, CONCAT(COLUMN_TYPE, IF(IS_NULLABLE = 'YES', ' · NULL', ' · NOT NULL'), IF(COLUMN_KEY = 'PRI', ' · PK', ''), IF(EXTRA = '', '', CONCAT(' · ', EXTRA))) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = :database AND TABLE_NAME = :table ORDER BY ORDINAL_POSITION"),
        "indexes" => ("index", "SELECT INDEX_NAME, CONCAT(IF(MIN(NON_UNIQUE) = 0, 'UNIQUE · ', ''), GROUP_CONCAT(COALESCE(COLUMN_NAME, 'expression') ORDER BY SEQ_IN_INDEX SEPARATOR ', ')) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = :database AND TABLE_NAME = :table GROUP BY INDEX_NAME ORDER BY INDEX_NAME"),
        "foreignKeys" => ("foreignKey", "SELECT CONSTRAINT_NAME, CONCAT(COLUMN_NAME, ' → ', REFERENCED_TABLE_SCHEMA, '.', REFERENCED_TABLE_NAME, '.', REFERENCED_COLUMN_NAME) FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = :database AND TABLE_NAME = :table AND REFERENCED_TABLE_NAME IS NOT NULL ORDER BY CONSTRAINT_NAME, ORDINAL_POSITION"),
        _ => return Err("Unknown schema object category.".into()),
    };
    if matches!(category, "columns" | "indexes" | "foreignKeys") {
        identifier(table)?;
    }
    let pool = connection::metadata_pool(handle)?;
    connection::run(async {
        let mut conn = pool.get_conn().await.map_err(|e| e.to_string())?;
        let rows: Vec<(String, String)> = history::query(
            handle,
            "Schema objects",
            database,
            sql,
            conn.exec(sql, params! { "database" => database, "table" => table }),
        )
        .await?;
        Ok(rows
            .into_iter()
            .map(|(name, detail)| SchemaObject {
                name,
                detail,
                kind: kind.into(),
            })
            .collect())
    })
}

pub async fn columns(
    conn: &mut Conn,
    handle: u64,
    database: &str,
    table: &str,
) -> Result<Vec<Column>, String> {
    let sql = "SELECT COLUMN_NAME, COLUMN_TYPE, DATA_TYPE, COLUMN_KEY, COALESCE(GENERATION_EXPRESSION, '') <> '' FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ? ORDER BY ORDINAL_POSITION";
    let rows: Vec<(String, String, String, String, u8)> = history::query(
        handle,
        "Column metadata",
        &format!("{database}.{table}"),
        sql,
        conn.exec(sql, (database, table)),
    )
    .await?;
    if rows.is_empty() {
        return Err("The table is unavailable, empty of columns, or not visible to this account. Refresh its schema.".into());
    }
    if rows.len() > 1024 {
        return Err("Data preview supports up to 1024 columns per object.".into());
    }
    rows.into_iter()
        .map(|(name, sql_type, data_type, key, generated)| {
            identifier(&name)?;
            let binary = matches!(
                data_type.to_ascii_lowercase().as_str(),
                "binary"
                    | "varbinary"
                    | "tinyblob"
                    | "blob"
                    | "mediumblob"
                    | "longblob"
                    | "bit"
                    | "geometry"
                    | "point"
                    | "linestring"
                    | "polygon"
                    | "multipoint"
                    | "multilinestring"
                    | "multipolygon"
                    | "geometrycollection"
                    | "vector"
            );
            Ok(Column {
                name,
                sql_type,
                binary,
                primary_key: key == "PRI",
                generated: generated != 0,
            })
        })
        .collect()
}

pub async fn is_base_table(
    conn: &mut Conn,
    handle: u64,
    database: &str,
    table: &str,
) -> Result<bool, String> {
    let sql = "SELECT TABLE_TYPE FROM information_schema.TABLES WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ?";
    let kind: Option<String> = history::query(
        handle,
        "Object type",
        &format!("{database}.{table}"),
        sql,
        conn.exec_first(sql, (database, table)),
    )
    .await?;
    Ok(kind.as_deref() == Some("BASE TABLE"))
}

pub fn ddl(handle: u64, database: &str, table: &str) -> Result<String, String> {
    definition(handle, database, table, "table").map(|definition| definition.sql)
}

pub fn definition(
    handle: u64,
    database: &str,
    name: &str,
    kind: &str,
) -> Result<ObjectDefinition, String> {
    const MAX_DEFINITION_BYTES: usize = 4 * 1024 * 1024;
    let quoted_database = identifier(database)?;
    let quoted_name = identifier(name)?;
    let (keyword, body_columns): (&str, &[&str]) = match kind {
        "database" if name == database => ("DATABASE", &["Create Database"]),
        "table" => ("TABLE", &["Create Table", "Create View"]),
        "view" => ("VIEW", &["Create View"]),
        "procedure" => ("PROCEDURE", &["Create Procedure"]),
        "function" => ("FUNCTION", &["Create Function"]),
        "trigger" => ("TRIGGER", &["SQL Original Statement"]),
        "event" => ("EVENT", &["Create Event"]),
        _ => return Err("Unsupported object kind or mismatched database name.".into()),
    };
    let target = if kind == "database" {
        quoted_database
    } else {
        format!("{quoted_database}.{quoted_name}")
    };
    let sql = format!("SHOW CREATE {keyword} {target}");
    let context = if kind == "database" {
        database.to_owned()
    } else {
        format!("{database}.{name}")
    };
    let pool = connection::metadata_pool(handle)?;
    connection::run(async {
        let mut conn = pool.get_conn().await.map_err(|e| e.to_string())?;
        let row: Row = history::query(
            handle,
            "Object definition",
            &context,
            &sql,
            conn.query_first(&sql),
        )
        .await?
        .ok_or("The server returned no object definition.")?;
        // SHOW CREATE result layouts differ by object kind. A routine can also
        // expose its name and SQL mode while withholding its definition as NULL.
        let body_index = row
            .columns_ref()
            .iter()
            .position(|column| {
                body_columns
                    .iter()
                    .any(|name| column.name_str().eq_ignore_ascii_case(name))
            })
            .ok_or("The server returned no CREATE statement column.")?;
        let sql = definition_value(&row, body_index)?.ok_or(
            "The server withheld the object definition. This account may need additional SHOW CREATE privileges.",
        )?;
        let mut bytes = sql.len();
        if sql.trim().is_empty() || bytes > MAX_DEFINITION_BYTES {
            return Err(
                "The object definition is empty or exceeds the 4 MiB display limit.".into(),
            );
        }
        let mut properties = Vec::new();
        for (index, column) in row.columns_ref().iter().enumerate() {
            if index == body_index {
                continue;
            }
            let name = column.name_str().into_owned();
            let value = definition_value(&row, index)?;
            bytes = bytes
                .saturating_add(name.len())
                .saturating_add(value.as_ref().map_or(0, String::len));
            if bytes > MAX_DEFINITION_BYTES {
                return Err(
                    "The object definition and metadata exceed the 4 MiB display limit.".into(),
                );
            }
            properties.push(DefinitionProperty { name, value });
        }
        Ok(ObjectDefinition {
            database: database.into(),
            name: name.into(),
            kind: kind.into(),
            sql,
            properties,
        })
    })
}

fn definition_value(row: &Row, index: usize) -> Result<Option<String>, String> {
    row.get_opt::<Option<String>, _>(index)
        .ok_or("The server returned incomplete definition metadata.")?
        .map_err(|_| "The server returned invalid UTF-8 definition metadata.".into())
}
