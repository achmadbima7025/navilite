use crate::connection;
use mysql_async::{params, prelude::Queryable, Conn, Row};
use serde::Serialize;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SchemaObject {
    pub name: String,
    pub kind: String,
    pub detail: String,
}

#[derive(Clone, PartialEq, Eq)]
pub struct Column {
    pub name: String,
    pub sql_type: String,
    pub binary: bool,
    pub primary_key: bool,
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
        let rows: Vec<(String, String)> = conn
            .exec(sql, params! { "database" => database, "table" => table })
            .await
            .map_err(|e| e.to_string())?;
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

pub async fn columns(conn: &mut Conn, database: &str, table: &str) -> Result<Vec<Column>, String> {
    let rows: Vec<(String, String, String, String)> = conn.exec(
        "SELECT COLUMN_NAME, COLUMN_TYPE, DATA_TYPE, COLUMN_KEY FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ? ORDER BY ORDINAL_POSITION",
        (database, table)).await.map_err(|e| e.to_string())?;
    if rows.is_empty() {
        return Err("The table is unavailable, empty of columns, or not visible to this account. Refresh its schema.".into());
    }
    if rows.len() > 1024 {
        return Err("Data preview supports up to 1024 columns per object.".into());
    }
    rows.into_iter()
        .map(|(name, sql_type, data_type, key)| {
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
            })
        })
        .collect()
}

pub fn ddl(handle: u64, database: &str, table: &str) -> Result<String, String> {
    let sql = format!(
        "SHOW CREATE TABLE {}.{}",
        identifier(database)?,
        identifier(table)?
    );
    let pool = connection::metadata_pool(handle)?;
    connection::run(async {
        let mut conn = pool.get_conn().await.map_err(|e| e.to_string())?;
        let row: Row = conn
            .query_first(sql)
            .await
            .map_err(|e| e.to_string())?
            .ok_or("The server returned no object definition.")?;
        row.get_opt::<String, _>(1)
            .ok_or("The object definition is missing.")?
            .map_err(|_| "The server returned an invalid object definition.".into())
    })
}
