//! Offline MySQL script boundaries. This is a lexer, not a SQL validator.
use serde::{Deserialize, Serialize};

const MAX_BYTES: usize = 1024 * 1024;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Request {
    script: String,
    caret_utf16: u32,
    #[serde(default = "yes")]
    backslash_escapes: bool,
    #[serde(default)]
    ansi_quotes: bool,
}

fn yes() -> bool {
    true
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Statement {
    index: usize,
    start_utf16: u32,
    length_utf16: u32,
    start_line: u32,
    preview: String,
    #[serde(skip)]
    pub(crate) read_candidate: bool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Analysis {
    pub(crate) statements: Vec<Statement>,
    pub(crate) current_index: Option<usize>,
    pub(crate) delimiter_count: u32,
    document_length_utf16: u32,
}

#[derive(Clone, Copy, Default)]
struct Position {
    byte: usize,
    utf16: u32,
    line: u32,
    column: u32,
    cr: bool,
}

struct Lexer<'a> {
    text: &'a str,
    position: Position,
    start: Option<Position>,
    end: Position,
    has_sql: bool,
    read_candidate: bool,
    statements: Vec<Statement>,
}

impl<'a> Lexer<'a> {
    fn rest(&self) -> &'a str {
        &self.text[self.position.byte..]
    }

    fn advance(&mut self, track: bool) {
        let ch = self.rest().chars().next().expect("advance before EOF");
        let before = self.position;
        self.position.byte += ch.len_utf8();
        self.position.utf16 += ch.len_utf16() as u32;
        match ch {
            '\r' => {
                self.position.line += 1;
                self.position.column = 0;
            }
            '\n' => {
                if !self.position.cr {
                    self.position.line += 1;
                }
                self.position.column = 0;
            }
            _ => self.position.column += ch.len_utf16() as u32,
        }
        self.position.cr = ch == '\r';
        if track && !ch.is_whitespace() && !(before.byte == 0 && ch == '\u{feff}') {
            self.start.get_or_insert(before);
            self.end = self.position;
        }
    }

    fn consume(&mut self, bytes: usize, track: bool) {
        let end = self.position.byte + bytes;
        while self.position.byte < end {
            self.advance(track);
        }
    }

    fn finish(&mut self) -> Result<(), String> {
        if self.has_sql {
            if self.statements.len() == 1000 {
                return Err("SQL scripts are limited to 1,000 statements.".into());
            }
            let start = self.start.expect("SQL has a source span");
            let source = &self.text[start.byte..self.end.byte];
            let preview = source.split_whitespace().collect::<Vec<_>>().join(" ");
            let mut preview: String = preview.chars().take(160).collect();
            if source.chars().count() > 160 {
                preview.push('…');
            }
            self.statements.push(Statement {
                index: self.statements.len() + 1,
                start_utf16: start.utf16,
                length_utf16: self.end.utf16 - start.utf16,
                start_line: start.line,
                preview,
                read_candidate: self.read_candidate,
            });
        }
        self.start = None;
        self.has_sql = false;
        self.read_candidate = true;
        Ok(())
    }

    fn error(&self, message: &str) -> String {
        format!(
            "{message} (line {}, column {}).",
            self.position.line,
            self.position.column + 1
        )
    }
}

pub fn analyze(json: &str) -> Result<Analysis, String> {
    if json.len() > 8 * MAX_BYTES {
        return Err("SQL analysis input is too large.".into());
    }
    let request: Request = serde_json::from_str(json)
        .map_err(|_| "Invalid SQL analysis request. Check the script and lexer options.")?;
    analyze_script(
        &request.script,
        request.caret_utf16,
        request.backslash_escapes,
        request.ansi_quotes,
    )
}

pub(crate) fn analyze_script(
    script: &str,
    caret_utf16: u32,
    backslash_escapes: bool,
    ansi_quotes: bool,
) -> Result<Analysis, String> {
    if script.len() > MAX_BYTES {
        return Err("SQL scripts are limited to 1 MiB of UTF-8.".into());
    }
    if script.contains('\0') {
        return Err("SQL scripts cannot contain NUL characters.".into());
    }
    let length = script.encode_utf16().count() as u32;
    if caret_utf16 > length {
        return Err("The SQL caret is outside the document.".into());
    }
    let mut lexer = Lexer {
        text: script,
        position: Position {
            line: 1,
            ..Position::default()
        },
        start: None,
        end: Position::default(),
        has_sql: false,
        read_candidate: true,
        statements: Vec::new(),
    };
    let mut delimiter = ";".to_owned();
    let mut delimiter_count = 0;
    while !lexer.rest().is_empty() {
        // Long-form mysql client directives are supported only at a physical line start.
        if lexer.position.column == 0 {
            let line = lexer.rest().split(['\r', '\n']).next().unwrap_or_default();
            let command = line.trim_start_matches([' ', '\t', '\u{feff}']);
            if command
                .get(..9)
                .is_some_and(|s| s.eq_ignore_ascii_case("delimiter"))
                && command
                    .get(9..)
                    .is_some_and(|s| s.is_empty() || s.starts_with(char::is_whitespace))
            {
                if lexer.has_sql {
                    return Err(lexer.error("Finish the current statement before DELIMITER"));
                }
                let token = command[9..].trim();
                if token.is_empty()
                    || token.len() > 32
                    || !token
                        .bytes()
                        .all(|b| b.is_ascii_graphic() && !b"'\"`\\#".contains(&b))
                    || ["--", "/*", "*/"].iter().any(|s| token.contains(s))
                    || token == "/"
                    || token == "-"
                {
                    return Err(lexer.error("DELIMITER requires one unquoted ASCII token of 1–32 characters without quotes, backslashes, or comment markers"));
                }
                delimiter = token.to_owned();
                delimiter_count += 1;
                lexer.finish()?;
                lexer.consume(line.len(), false);
                if !lexer.rest().is_empty() {
                    lexer.advance(false);
                    if lexer.position.cr && lexer.rest().starts_with('\n') {
                        lexer.advance(false);
                    }
                }
                continue;
            }
        }
        if lexer.rest().starts_with(&delimiter) {
            lexer.finish()?;
            lexer.consume(delimiter.len(), false);
            continue;
        }
        let ch = lexer.rest().chars().next().expect("not EOF");
        if ch.is_whitespace() || (lexer.position.byte == 0 && ch == '\u{feff}') {
            lexer.advance(true);
        } else if ch == '#'
            || (lexer.rest().starts_with("--")
                && lexer
                    .rest()
                    .as_bytes()
                    .get(2)
                    .is_none_or(|b| b.is_ascii_whitespace() || b.is_ascii_control()))
        {
            while !lexer.rest().is_empty() && !lexer.rest().starts_with(['\r', '\n']) {
                lexer.advance(true);
            }
        } else if lexer.rest().starts_with("/*") {
            let opened = lexer.position;
            if lexer.rest().starts_with("/*!")
                || lexer.rest().starts_with("/*M!")
                || lexer.rest().starts_with("/*+")
            {
                lexer.has_sql = true;
                lexer.read_candidate = false;
            }
            lexer.consume(2, true);
            while !lexer.rest().is_empty() && !lexer.rest().starts_with("*/") {
                lexer.advance(true);
            }
            if lexer.rest().is_empty() {
                return Err(format!(
                    "Unterminated block comment (line {}, column {}).",
                    opened.line,
                    opened.column + 1
                ));
            }
            lexer.consume(2, true);
        } else if matches!(ch, '\'' | '"' | '`') {
            if !lexer.has_sql {
                lexer.read_candidate = false;
            }
            lexer.has_sql = true;
            let opened = lexer.position;
            let escapes = backslash_escapes && ch != '`' && !(ch == '"' && ansi_quotes);
            lexer.advance(true);
            loop {
                let Some(next) = lexer.rest().chars().next() else {
                    return Err(format!(
                        "Unterminated quoted value or identifier (line {}, column {}).",
                        opened.line,
                        opened.column + 1
                    ));
                };
                lexer.advance(true);
                if next == ch {
                    if lexer.rest().starts_with(ch) {
                        lexer.advance(true);
                    } else {
                        break;
                    }
                } else if next == '\\' && escapes && !lexer.rest().is_empty() {
                    lexer.advance(true);
                }
            }
        } else if ch.is_ascii_alphabetic() || ch == '_' {
            let start = lexer.position.byte;
            while lexer
                .rest()
                .chars()
                .next()
                .is_some_and(|c| c.is_alphanumeric() || matches!(c, '_' | '$'))
                && !lexer.rest().starts_with(&delimiter)
            {
                lexer.advance(true);
            }
            let word = &script[start..lexer.position.byte];
            if !lexer.has_sql {
                lexer.read_candidate = word.eq_ignore_ascii_case("SELECT");
            }
            if [
                "INTO",
                "FOR",
                "LOCK",
                "PROCEDURE",
                "GET_LOCK",
                "RELEASE_LOCK",
                "RELEASE_ALL_LOCKS",
                "LOAD_FILE",
                "SLEEP",
                "BENCHMARK",
                "SQL_CALC_FOUND_ROWS",
                "INSERT",
                "UPDATE",
                "DELETE",
                "DROP",
                "ALTER",
                "CREATE",
                "TRUNCATE",
                "CALL",
                "SET",
            ]
            .iter()
            .any(|blocked| word.eq_ignore_ascii_case(blocked))
            {
                lexer.read_candidate = false;
            }
            lexer.has_sql = true;
        } else {
            if !lexer.has_sql || matches!(ch, '@' | '?' | ':' | '\\') {
                lexer.read_candidate = false;
            }
            lexer.has_sql = true;
            lexer.advance(true);
        }
    }
    lexer.finish()?;
    // At a statement end (including the first terminator character), prefer that statement.
    // Otherwise use the following statement; after the last statement use the previous one.
    let current_index = lexer
        .statements
        .iter()
        .position(|s| caret_utf16 <= s.start_utf16 + s.length_utf16)
        .or_else(|| lexer.statements.len().checked_sub(1));
    Ok(Analysis {
        statements: lexer.statements,
        current_index,
        delimiter_count,
        document_length_utf16: length,
    })
}

/// Convert a WPF offset without silently splitting a surrogate pair.
pub(crate) fn byte_offset(text: &str, utf16: u32) -> Result<usize, String> {
    let mut units = 0;
    for (byte, ch) in text.char_indices() {
        if units == utf16 {
            return Ok(byte);
        }
        units += ch.len_utf16() as u32;
        if units > utf16 {
            break;
        }
    }
    if units == utf16 {
        Ok(text.len())
    } else {
        Err(
            "The SQL selection must lie within the document and cannot split a Unicode character."
                .into(),
        )
    }
}

impl Statement {
    pub(crate) fn bounds(&self) -> (u32, u32) {
        (self.start_utf16, self.length_utf16)
    }

    pub(crate) fn source<'a>(&self, script: &'a str) -> Result<&'a str, String> {
        Ok(&script[byte_offset(script, self.start_utf16)?
            ..byte_offset(script, self.start_utf16 + self.length_utf16)?])
    }
}
