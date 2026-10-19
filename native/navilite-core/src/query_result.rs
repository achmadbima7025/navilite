//! Incremental NVLR frames for arbitrary SELECT columns; no retained row identities.
use mysql_async::{consts::ColumnType, Column, Row, Value};

const MAX_FRAME: usize = 8 * 1024 * 1024;

fn number(value: usize) -> [u8; 4] {
    (value as u32).to_le_bytes()
}
fn string(frame: &mut Vec<u8>, value: &str) {
    frame.extend_from_slice(&number(value.len()));
    frame.extend_from_slice(value.as_bytes());
}

pub(crate) struct Frame {
    bytes: Vec<u8>,
    directory: usize,
    binary: Vec<bool>,
    rows: usize,
    limit: usize,
    shortened: u32,
}

impl Frame {
    pub(crate) fn new(columns: &[Column], limit: usize) -> Result<Self, String> {
        if columns.is_empty() || columns.len() > 1024 {
            return Err("SQL results require 1–1,024 columns.".into());
        }
        let mut bytes = vec![0; 40];
        bytes[..4].copy_from_slice(b"NVLR");
        bytes[4..8].copy_from_slice(&1u32.to_le_bytes());
        bytes[8..12].copy_from_slice(&number(columns.len()));
        let mut binary = Vec::with_capacity(columns.len());
        for column in columns {
            string(&mut bytes, &column.name_str());
            string(&mut bytes, &format!("{:?}", column.column_type()));
            binary.push(match column.column_type() {
                ColumnType::MYSQL_TYPE_BIT | ColumnType::MYSQL_TYPE_GEOMETRY => true,
                ColumnType::MYSQL_TYPE_STRING
                | ColumnType::MYSQL_TYPE_VAR_STRING
                | ColumnType::MYSQL_TYPE_VARCHAR
                | ColumnType::MYSQL_TYPE_BLOB
                | ColumnType::MYSQL_TYPE_TINY_BLOB
                | ColumnType::MYSQL_TYPE_MEDIUM_BLOB
                | ColumnType::MYSQL_TYPE_LONG_BLOB => column.character_set() == 63,
                _ => false,
            });
        }
        let directory = bytes.len();
        bytes.resize(directory + (limit + 1) * 4, 0);
        if bytes.len() > MAX_FRAME {
            return Err("SQL column metadata exceeds the 8 MiB result limit.".into());
        }
        Ok(Self {
            bytes,
            directory,
            binary,
            rows: 0,
            limit,
            shortened: 0,
        })
    }

    pub(crate) fn push(&mut self, row: Row) -> Result<(), String> {
        let values = row.unwrap();
        if values.len() != self.binary.len() || self.rows >= self.limit {
            return Err("Invalid SQL result dimensions.".into());
        }
        let start = self.bytes.len();
        self.bytes[self.directory + self.rows * 4..self.directory + self.rows * 4 + 4]
            .copy_from_slice(&number(start));
        self.bytes.resize(start + values.len() * 4, 0);
        for (index, value) in values.into_iter().enumerate() {
            let offset = self.bytes.len() - start;
            self.bytes[start + index * 4..start + index * 4 + 4].copy_from_slice(&number(offset));
            let (kind, data, shortened) = match value {
                Value::NULL => (0, Vec::new(), false),
                Value::Bytes(bytes) if self.binary[index] => {
                    let length = bytes.len().min(256);
                    (2, bytes[..length].to_vec(), length < bytes.len())
                }
                Value::Bytes(bytes) => {
                    let text = std::str::from_utf8(&bytes)
                        .map_err(|_| "The query returned invalid UTF-8 text.")?;
                    let end = text
                        .char_indices()
                        .nth(1024)
                        .map_or(text.len(), |(byte, _)| byte);
                    (1, bytes[..end].to_vec(), end < bytes.len())
                }
                value => {
                    // The binary protocol preserves numeric precision and temporal components.
                    let formatted = value.as_sql(false);
                    let text = if formatted.starts_with('\'') && formatted.ends_with('\'') {
                        &formatted[1..formatted.len() - 1]
                    } else {
                        &formatted
                    };
                    (1, text.as_bytes().to_vec(), false)
                }
            };
            self.shortened += u32::from(shortened);
            if self.bytes.len() + 5 + data.len() > MAX_FRAME {
                return Err(
                    "The SQL result exceeds 8 MiB. Reduce the row limit or select fewer columns."
                        .into(),
                );
            }
            self.bytes.push(kind | if shortened { 128 } else { 0 });
            self.bytes.extend_from_slice(&number(data.len()));
            self.bytes.extend_from_slice(&data);
        }
        self.rows += 1;
        Ok(())
    }

    pub(crate) fn finish(mut self, more: bool) -> Vec<u8> {
        // Compact the reserved row directory when fewer rows arrive.
        let unused = (self.limit - self.rows) * 4;
        let data_start = self.directory + (self.limit + 1) * 4;
        for index in 0..self.rows {
            let entry = self.directory + index * 4;
            let old = u32::from_le_bytes(
                self.bytes[entry..entry + 4]
                    .try_into()
                    .expect("directory entry"),
            ) as usize;
            self.bytes[entry..entry + 4].copy_from_slice(&number(old - unused));
        }
        self.bytes.drain(data_start - unused..data_start);
        let end = self.bytes.len();
        self.bytes[self.directory + self.rows * 4..self.directory + self.rows * 4 + 4]
            .copy_from_slice(&number(end));
        self.bytes[12..16].copy_from_slice(&number(self.rows));
        self.bytes[24..32].copy_from_slice(&(self.rows as u64).to_le_bytes());
        self.bytes[32..36].copy_from_slice(&u32::from(more).to_le_bytes());
        self.bytes[36..40].copy_from_slice(&self.shortened.to_le_bytes());
        self.bytes
    }
}
