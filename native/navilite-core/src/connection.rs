use crate::profiles::{self, ConnectionProfile};
use mysql_async::{
    prelude::Queryable, Conn, OptsBuilder, Pool, PoolConstraints, PoolOpts, SslOpts,
};
use serde::Serialize;
use std::{
    collections::HashMap,
    future::Future,
    path::PathBuf,
    sync::{
        atomic::{AtomicU64, Ordering},
        Mutex, OnceLock,
    },
    time::{Duration, Instant},
};
use tokio::runtime::{Builder, Runtime};
use zeroize::Zeroizing;

const OPERATION_TIMEOUT: Duration = Duration::from_secs(10);
static RUNTIME: OnceLock<Result<Runtime, String>> = OnceLock::new();
static SESSIONS: OnceLock<Mutex<HashMap<u64, Session>>> = OnceLock::new();
static NEXT_HANDLE: AtomicU64 = AtomicU64::new(1);

struct Session {
    metadata: Pool,
    workload: Pool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ConnectionInfo {
    handle: u64,
    server_version: String,
    connection_id: u64,
    latency_ms: u64,
    tls: bool,
}

fn sessions() -> &'static Mutex<HashMap<u64, Session>> {
    SESSIONS.get_or_init(|| Mutex::new(HashMap::new()))
}

pub(crate) fn run<T>(future: impl Future<Output = Result<T, String>>) -> Result<T, String> {
    let runtime = RUNTIME.get_or_init(|| {
        Builder::new_multi_thread()
            .worker_threads(2)
            .enable_all()
            .build()
            .map_err(|e| format!("Cannot start the database runtime: {e}"))
    });
    runtime.as_ref().map_err(Clone::clone)?.block_on(async {
        tokio::time::timeout(OPERATION_TIMEOUT, future).await
            .map_err(|_| "Database operation timed out after 10 seconds. Check host, port, and network access.".to_string())?
    })
}

fn options(
    profile: &ConnectionProfile,
    password: &str,
    maximum: usize,
) -> Result<OptsBuilder, String> {
    profile.validate()?;
    let pool = PoolOpts::default()
        .with_constraints(PoolConstraints::new(0, maximum).ok_or("Invalid session pool limits.")?);
    let mut builder = OptsBuilder::default()
        .ip_or_hostname(profile.host.trim())
        .tcp_port(profile.port)
        .user(Some(profile.username.as_str()))
        .pass(Some(password))
        .tcp_nodelay(true)
        .prefer_socket(false)
        .conn_ttl(Duration::from_secs(300))
        .pool_opts(pool)
        // SET NAMES must be reapplied after a pooled connection is reset as well.
        .setup(vec!["SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci"]);
    if profile.use_tls {
        let mut ssl = SslOpts::default();
        if let Some(path) = profile.ca_certificate.as_ref().filter(|p| !p.is_empty()) {
            let path = PathBuf::from(path);
            if !path.is_file() {
                return Err("The CA certificate file does not exist.".into());
            }
            ssl = ssl.with_root_certs(vec![path.into()]);
        }
        // Certificate and hostname verification remain enabled, with no plaintext fallback.
        builder = builder.ssl_opts(ssl);
    }
    Ok(builder)
}

async fn inspect(conn: &mut Conn, started: Instant, tls: bool) -> Result<ConnectionInfo, String> {
    let (version, id): (String, u64) = conn
        .query_first("SELECT VERSION(), CONNECTION_ID()")
        .await
        .map_err(|e| e.to_string())?
        .ok_or("The server did not return connection information.")?;
    Ok(ConnectionInfo {
        handle: 0,
        server_version: version,
        connection_id: id,
        latency_ms: started.elapsed().as_millis() as u64,
        tls,
    })
}

fn redact<T>(result: Result<T, String>, password: &str) -> Result<T, String> {
    result.map_err(|error| {
        if password.is_empty() {
            error
        } else {
            error.replace(password, "[redacted]")
        }
    })
}

pub fn check(
    profile: ConnectionProfile,
    supplied: Option<Zeroizing<String>>,
) -> Result<ConnectionInfo, String> {
    let password = profiles::password(&profile, supplied)?;
    let opts = options(&profile, &password, 1)?;
    redact(
        run(async {
            let started = Instant::now();
            let mut conn = Conn::new(opts).await.map_err(|e| e.to_string())?;
            let info = inspect(&mut conn, started, profile.use_tls).await;
            conn.disconnect().await.map_err(|e| e.to_string())?;
            info
        }),
        &password,
    )
}

pub fn open(
    profile: ConnectionProfile,
    supplied: Option<Zeroizing<String>>,
) -> Result<ConnectionInfo, String> {
    let password = profiles::password(&profile, supplied)?;
    let metadata = Pool::new(options(&profile, &password, 2)?);
    let workload = Pool::new(options(&profile, &password, 10)?);
    redact(
        run(async {
            let started = Instant::now();
            let mut conn = metadata.get_conn().await.map_err(|e| e.to_string())?;
            let mut info = inspect(&mut conn, started, profile.use_tls).await?;
            drop(conn);
            let handle = NEXT_HANDLE.fetch_add(1, Ordering::Relaxed);
            sessions()
                .lock()
                .map_err(|_| "Session registry is unavailable.")?
                .insert(handle, Session { metadata, workload });
            info.handle = handle;
            Ok(info)
        }),
        &password,
    )
}

pub fn close(handle: u64) -> Result<(), String> {
    let session = sessions()
        .lock()
        .map_err(|_| "Session registry is unavailable.")?
        .remove(&handle);
    if let Some(session) = session {
        run(async {
            let (metadata, workload) =
                tokio::join!(session.metadata.disconnect(), session.workload.disconnect());
            metadata.map_err(|e| e.to_string())?;
            workload.map_err(|e| e.to_string())?;
            Ok(())
        })?;
    }
    Ok(())
}

pub fn databases(handle: u64) -> Result<Vec<String>, String> {
    let pool = metadata_pool(handle)?;
    run(async {
        let mut conn = pool.get_conn().await.map_err(|e| e.to_string())?;
        conn.query("SHOW DATABASES")
            .await
            .map_err(|e| e.to_string())
    })
}

pub(crate) fn metadata_pool(handle: u64) -> Result<Pool, String> {
    Ok(sessions()
        .lock()
        .map_err(|_| "Session registry is unavailable.")?
        .get(&handle)
        .ok_or("Connection is closed. Connect again to refresh databases.")?
        .metadata
        .clone())
}

pub(crate) fn workload_pool(handle: u64) -> Result<Pool, String> {
    Ok(sessions()
        .lock()
        .map_err(|_| "Session registry is unavailable.")?
        .get(&handle)
        .ok_or("Connection is closed. Connect again to open data.")?
        .workload
        .clone())
}
