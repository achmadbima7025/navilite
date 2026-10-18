//! Bounded, in-memory history of internal SQL templates. Bound values are never retained.
use crate::connection;
use serde::Serialize;
use std::{
    collections::VecDeque,
    future::Future,
    sync::{Arc, Mutex},
    time::{Instant, SystemTime, UNIX_EPOCH},
};

const MAX_ENTRIES: usize = 200;
const MAX_STATEMENT_BYTES: usize = 16 * 1024;

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Entry {
    id: u64,
    started_unix_ms: u64,
    duration_ms: u64,
    operation: String,
    context: String,
    statement: String,
    shortened: bool,
    outcome: &'static str,
}

#[derive(Default)]
pub(crate) struct QueryHistory {
    entries: VecDeque<Entry>,
    next_id: u64,
}

struct Pending {
    history: Option<Arc<Mutex<QueryHistory>>>,
    started: Instant,
    entry: Option<Entry>,
}

impl Pending {
    fn new(handle: u64, operation: &str, context: &str, sql: &str) -> Self {
        let mut end = sql.len().min(MAX_STATEMENT_BYTES);
        while !sql.is_char_boundary(end) {
            end -= 1;
        }
        Self {
            history: connection::query_history(handle).ok(),
            started: Instant::now(),
            entry: Some(Entry {
                id: 0,
                started_unix_ms: SystemTime::now()
                    .duration_since(UNIX_EPOCH)
                    .unwrap_or_default()
                    .as_millis()
                    .min(u64::MAX as u128) as u64,
                duration_ms: 0,
                operation: operation.into(),
                context: context.into(),
                statement: sql[..end].into(),
                shortened: end < sql.len(),
                outcome: "interrupted",
            }),
        }
    }
}

impl Drop for Pending {
    fn drop(&mut self) {
        if let (Some(history), Some(mut entry)) = (&self.history, self.entry.take()) {
            if let Ok(mut history) = history.lock() {
                history.next_id = history.next_id.saturating_add(1);
                entry.id = history.next_id;
                entry.duration_ms = self.started.elapsed().as_millis().min(u64::MAX as u128) as u64;
                if history.entries.len() == MAX_ENTRIES {
                    history.entries.pop_front();
                }
                history.entries.push_back(entry);
            }
        }
    }
}

pub(crate) async fn query<T>(
    handle: u64,
    operation: &str,
    context: &str,
    sql: &str,
    future: impl Future<Output = Result<T, mysql_async::Error>>,
) -> Result<T, String> {
    let mut pending = Pending::new(handle, operation, context, sql);
    let result = future.await;
    if let Some(entry) = &mut pending.entry {
        entry.outcome = if result.is_ok() { "success" } else { "error" };
    }
    result.map_err(|error| error.to_string())
}

pub fn list(handle: u64) -> Result<Vec<Entry>, String> {
    let history = connection::query_history(handle)?;
    let history = history.lock().map_err(|_| "SQL history is unavailable.")?;
    Ok(history.entries.iter().rev().cloned().collect())
}

pub fn clear(handle: u64) -> Result<(), String> {
    connection::query_history(handle)?
        .lock()
        .map_err(|_| "SQL history is unavailable.")?
        .entries
        .clear();
    Ok(())
}
