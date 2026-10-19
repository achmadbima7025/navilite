//! Conservative, offline SELECT layout. Non-whitespace lexemes are never rewritten.
use crate::sql_script;
use serde::{Deserialize, Serialize};

const MAX_BYTES: usize = 1024 * 1024;
const MAX_TOKENS: usize = 100_000;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Request {
    script: String,
    caret_utf16: u32,
    #[serde(default)]
    selection_start_utf16: u32,
    #[serde(default)]
    selection_length_utf16: u32,
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
pub struct Edit {
    start_utf16: u32,
    length_utf16: u32,
    replacement: String,
    caret_utf16: u32,
    formatted_statements: usize,
    changed: bool,
}

#[derive(Clone, Copy, PartialEq)]
enum Kind {
    Space,
    Word,
    Quoted,
    LineComment,
    BlockComment,
    Symbol,
    Bom,
}

struct Token {
    start: usize,
    end: usize,
    unit_start: u32,
    unit_end: u32,
    kind: Kind,
}
impl Token {
    fn text<'a>(&self, source: &'a str) -> &'a str {
        &source[self.start..self.end]
    }
    fn comment(&self) -> bool {
        matches!(self.kind, Kind::LineComment | Kind::BlockComment)
    }
}

fn step(source: &str, at: &mut usize) -> char {
    let ch = source[*at..].chars().next().expect("step before EOF");
    *at += ch.len_utf8();
    ch
}

fn word_char(ch: char) -> bool {
    // Extended BMP characters can belong to unquoted MySQL identifiers, including
    // combining marks and zero-width characters. Keep non-ASCII runs together so
    // selections cannot cut them; unsupported spacing is still rejected separately.
    ch.is_ascii_alphanumeric() || matches!(ch, '_' | '$') || (!ch.is_ascii() && !ch.is_whitespace())
}

fn tokens(source: &str, escapes: bool, ansi: bool) -> Result<Vec<Token>, String> {
    let mut result = Vec::new();
    let mut at = 0;
    let mut units = 0;
    while at < source.len() {
        if result.len() == MAX_TOKENS {
            return Err("Formatting is limited to 100,000 lexical tokens.".into());
        }
        let start = at;
        let rest = &source[at..];
        let ch = step(source, &mut at);
        // MySQL may treat non-ASCII spacing characters as identifier bytes.
        // Reject these outside protected lexemes instead of normalizing them.
        if ch.is_whitespace() && !ch.is_ascii() {
            return Err("Formatting supports ASCII whitespace outside quoted values, identifiers, and comments.".into());
        }
        let kind = if ch.is_ascii_whitespace() {
            while source[at..]
                .chars()
                .next()
                .is_some_and(|c| c.is_ascii_whitespace())
            {
                step(source, &mut at);
            }
            Kind::Space
        } else if start == 0 && ch == '\u{feff}' {
            Kind::Bom
        } else if ch == '#'
            || (rest.starts_with("--")
                && rest
                    .as_bytes()
                    .get(2)
                    .is_none_or(|b| b.is_ascii_whitespace() || b.is_ascii_control()))
        {
            while at < source.len() && !source[at..].starts_with(['\r', '\n']) {
                step(source, &mut at);
            }
            Kind::LineComment
        } else if let Some(comment) = rest.strip_prefix("/*") {
            let end = comment
                .find("*/")
                .ok_or("Complete all block comments before formatting.")?;
            at = start + 2 + end + 2;
            Kind::BlockComment
        } else if matches!(ch, '\'' | '"' | '`') {
            let backslash = escapes && ch != '`' && !(ch == '"' && ansi);
            let mut closed = false;
            while at < source.len() {
                let next = step(source, &mut at);
                if next == ch {
                    if source[at..].starts_with(ch) {
                        step(source, &mut at);
                    } else {
                        closed = true;
                        break;
                    }
                } else if next == '\\' && backslash && at < source.len() {
                    step(source, &mut at);
                }
            }
            if !closed {
                return Err("Complete all quoted values and identifiers before formatting.".into());
            }
            Kind::Quoted
        } else if word_char(ch) {
            while source[at..].chars().next().is_some_and(word_char) {
                step(source, &mut at);
            }
            Kind::Word
        } else {
            Kind::Symbol
        };
        let length = source[start..at].encode_utf16().count() as u32;
        result.push(Token {
            start,
            end: at,
            unit_start: units,
            unit_end: units + length,
            kind,
        });
        units += length;
    }
    Ok(result)
}

fn word(token: &Token, source: &str, words: &[&str]) -> bool {
    token.kind == Kind::Word
        && words
            .iter()
            .any(|word| token.text(source).eq_ignore_ascii_case(word))
}

fn newline(source: &str) -> &'static str {
    match source.find(['\r', '\n']).map(|at| &source[at..]) {
        Some(rest) if rest.starts_with("\r\n") => "\r\n",
        Some(rest) if rest.starts_with('\r') => "\r",
        Some(_) => "\n",
        None => "\r\n",
    }
}

fn layout(
    source: &str,
    lexemes: &[Token],
    preserve_edges: bool,
    caret: u32,
) -> Result<(String, u32), String> {
    let nl = newline(source);
    let mut output = String::new();
    let mut previous: Option<&Token> = None;
    let mut depth = 0usize;
    let mut unit_length = 0;
    let mut mapped_caret = None;
    for token in lexemes.iter().filter(|t| t.kind != Kind::Space) {
        let text = token.text(source);
        let gap = &source[previous.map_or(0, |p| p.end)..token.start];
        let separator = match previous {
            None => {
                if preserve_edges {
                    gap.to_owned()
                } else {
                    String::new()
                }
            }
            Some(p) if p.kind == Kind::Bom => gap.to_owned(),
            Some(p) if p.comment() || token.comment() => gap.to_owned(),
            Some(p) if depth == 0 && p.text(source) == ";" => format!("{nl}{nl}"),
            Some(p) if depth == 0 && p.text(source) == "," => format!("{nl}    "),
            Some(_) if gap.is_empty() => String::new(),
            Some(p)
                if depth == 0
                    && word(
                        token,
                        source,
                        &[
                            "SELECT",
                            "FROM",
                            "WHERE",
                            "GROUP",
                            "HAVING",
                            "ORDER",
                            "LIMIT",
                            "UNION",
                            "EXCEPT",
                            "INTERSECT",
                            "JOIN",
                            "LEFT",
                            "RIGHT",
                            "INNER",
                            "CROSS",
                            "NATURAL",
                            "STRAIGHT_JOIN",
                            "ON",
                            "AND",
                            "OR",
                        ],
                    )
                    && !(word(token, source, &["JOIN"])
                        && word(
                            p,
                            source,
                            &["LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "NATURAL"],
                        )) =>
            {
                if word(token, source, &["ON", "AND", "OR"]) {
                    format!("{nl}    ")
                } else {
                    nl.to_owned()
                }
            }
            Some(_) => " ".into(),
        };
        if output.len() + separator.len() + text.len() > MAX_BYTES {
            return Err("Formatted SQL exceeds the 1 MiB document limit.".into());
        }
        output.push_str(&separator);
        unit_length += separator.encode_utf16().count() as u32;
        if mapped_caret.is_none() && caret <= token.unit_end {
            mapped_caret = Some(unit_length + caret.saturating_sub(token.unit_start));
        }
        output.push_str(text);
        unit_length += token.unit_end - token.unit_start;
        if token.kind == Kind::Symbol {
            if text == "(" {
                depth += 1;
                if depth > 64 {
                    return Err("Formatting supports at most 64 nested parentheses.".into());
                }
            } else if text == ")" {
                depth = depth
                    .checked_sub(1)
                    .ok_or("Balance parentheses before formatting.")?;
            }
        }
        previous = Some(token);
    }
    if depth != 0 {
        return Err("Balance parentheses before formatting.".into());
    }
    let tail = &source[previous.map_or(0, |p| p.end)..];
    let tail = if preserve_edges || previous.is_some_and(Token::comment) {
        tail
    } else if tail.contains(['\r', '\n']) {
        nl
    } else {
        ""
    };
    if output.len() + tail.len() > MAX_BYTES {
        return Err("Formatted SQL exceeds the 1 MiB document limit.".into());
    }
    output.push_str(tail);
    unit_length += tail.encode_utf16().count() as u32;
    Ok((output, mapped_caret.unwrap_or(unit_length)))
}

pub fn format(json: &str) -> Result<Edit, String> {
    if json.len() > 8 * MAX_BYTES {
        return Err("SQL formatting input is too large.".into());
    }
    let request: Request =
        serde_json::from_str(json).map_err(|_| "Invalid SQL formatting request.")?;
    let script = &request.script;
    if script.len() > MAX_BYTES || script.contains('\0') {
        return Err("SQL documents must be NUL-free UTF-8 of at most 1 MiB.".into());
    }
    sql_script::byte_offset(script, request.caret_utf16)?;
    let selected = request.selection_length_utf16 != 0;
    let start_units = if selected {
        request.selection_start_utf16
    } else {
        0
    };
    let length_units = if selected {
        request.selection_length_utf16
    } else {
        script.encode_utf16().count() as u32
    };
    let end_units = start_units
        .checked_add(length_units)
        .ok_or("The SQL selection is outside the document.")?;
    let start = sql_script::byte_offset(script, start_units)?;
    let end = sql_script::byte_offset(script, end_units)?;
    let original_tokens = tokens(script, request.backslash_escapes, request.ansi_quotes)?;
    if selected
        && original_tokens.iter().any(|t| {
            t.kind != Kind::Space
                && ((start > t.start && start < t.end) || (end > t.start && end < t.end))
        })
    {
        return Err(
            "Select complete SQL tokens outside quoted values, identifiers, and comments.".into(),
        );
    }
    let source = &script[start..end];
    let analysis =
        sql_script::analyze_script(source, 0, request.backslash_escapes, request.ansi_quotes)?;
    if analysis.delimiter_count != 0 || analysis.statements.iter().any(|s| !s.read_candidate) {
        return Err("Formatting currently supports plain SELECT scripts under the Run SELECT policy, without DELIMITER directives. Select a supported SELECT to format it separately.".into());
    }
    let lexemes = tokens(source, request.backslash_escapes, request.ansi_quotes)?;
    let (replacement, caret) = if analysis.statements.is_empty() {
        (
            source.to_owned(),
            request
                .caret_utf16
                .saturating_sub(start_units)
                .min(length_units),
        )
    } else {
        layout(
            source,
            &lexemes,
            selected,
            request
                .caret_utf16
                .saturating_sub(start_units)
                .min(length_units),
        )?
    };
    if script.len() - source.len() + replacement.len() > MAX_BYTES {
        return Err("Formatted SQL exceeds the 1 MiB document limit.".into());
    }
    let mut updated = String::with_capacity(script.len() - source.len() + replacement.len());
    updated.push_str(&script[..start]);
    updated.push_str(&replacement);
    updated.push_str(&script[end..]);
    let after = tokens(&updated, request.backslash_escapes, request.ansi_quotes)?;
    let before = original_tokens
        .iter()
        .filter(|t| t.kind != Kind::Space)
        .map(|t| (t.kind, t.text(script)));
    let after = after
        .iter()
        .filter(|t| t.kind != Kind::Space)
        .map(|t| (t.kind, t.text(&updated)));
    if !before.eq(after) {
        return Err("Formatting would change SQL tokens; the document was preserved.".into());
    }
    Ok(Edit {
        start_utf16: start_units,
        length_utf16: length_units,
        changed: replacement != source,
        replacement,
        caret_utf16: start_units + caret,
        formatted_statements: analysis.statements.len(),
    })
}
