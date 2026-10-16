use keyring::Entry;
use serde::{Deserialize, Serialize};
use std::{fs, path::PathBuf, sync::Mutex};
use zeroize::Zeroizing;

static STORE_LOCK: Mutex<()> = Mutex::new(());

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ConnectionProfile {
    pub id: String,
    pub name: String,
    pub host: String,
    pub port: u16,
    pub username: String,
    pub environment: Environment,
    pub remember_password: bool,
    pub use_tls: bool,
    pub ca_certificate: Option<String>,
}

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum Environment {
    Local,
    Staging,
    Production,
}

impl ConnectionProfile {
    pub fn validate(&self) -> Result<(), String> {
        validate_id(&self.id)?;
        if self.name.trim().is_empty()
            || self.host.trim().is_empty()
            || self.username.trim().is_empty()
        {
            return Err("Name, host, and username are required.".into());
        }
        if self.port == 0 {
            return Err("Port must be between 1 and 65535.".into());
        }
        if !self.use_tls && self.ca_certificate.as_ref().is_some_and(|s| !s.is_empty()) {
            return Err("Enable TLS before selecting a CA certificate.".into());
        }
        Ok(())
    }
}

fn validate_id(id: &str) -> Result<(), String> {
    if id.is_empty() || id.len() > 64 || !id.bytes().all(|c| c.is_ascii_alphanumeric() || c == b'-')
    {
        return Err("Invalid connection profile ID.".into());
    }
    Ok(())
}

fn store_path() -> Result<PathBuf, String> {
    let root = std::env::var_os("LOCALAPPDATA").ok_or("LOCALAPPDATA is unavailable.")?;
    Ok(PathBuf::from(root)
        .join("NaviLite")
        .join("connections.json"))
}

fn lock_store() -> Result<fs::File, String> {
    let path = store_path()?.with_extension("lock");
    fs::create_dir_all(path.parent().ok_or("Invalid profile directory.")?)
        .map_err(|e| format!("Cannot create profile directory: {e}"))?;
    let file = fs::File::options()
        .read(true)
        .write(true)
        .create(true)
        .truncate(false)
        .open(path)
        .map_err(|e| format!("Cannot open connection store lock: {e}"))?;
    file.try_lock().map_err(|_| {
        "Connection profiles are being updated by another NaviLite window. Try again."
    })?;
    // Closing this handle releases the OS lock, including after a process crash.
    Ok(file)
}

fn read_profiles() -> Result<Vec<ConnectionProfile>, String> {
    match fs::read(store_path()?) {
        Ok(bytes) => {
            let profiles: Vec<ConnectionProfile> = serde_json::from_slice(&bytes)
                .map_err(|e| format!("Cannot read connection profiles: {e}"))?;
            for profile in &profiles {
                profile.validate()?;
            }
            Ok(profiles)
        }
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Vec::new()),
        Err(e) => Err(format!("Cannot read connection profiles: {e}")),
    }
}

struct PendingProfiles {
    temporary: PathBuf,
    target: PathBuf,
}

impl PendingProfiles {
    fn commit(&self) -> Result<(), String> {
        fs::rename(&self.temporary, &self.target)
            .map_err(|e| format!("Cannot replace connection profiles: {e}"))
    }
}

impl Drop for PendingProfiles {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.temporary);
    }
}

fn prepare_profiles(profiles: &[ConnectionProfile]) -> Result<PendingProfiles, String> {
    let path = store_path()?;
    fs::create_dir_all(path.parent().ok_or("Invalid profile directory.")?)
        .map_err(|e| format!("Cannot create profile directory: {e}"))?;
    let bytes = serde_json::to_vec_pretty(profiles).map_err(|e| e.to_string())?;
    let pending = PendingProfiles {
        temporary: path.with_extension("json.tmp"),
        target: path,
    };
    fs::write(&pending.temporary, bytes).map_err(|e| format!("Cannot write profiles: {e}"))?;
    Ok(pending)
}

fn entry(id: &str) -> Result<Entry, String> {
    validate_id(id)?;
    Entry::new("NaviLite.MySQL", id).map_err(|_| "Cannot access Windows Credential Manager.".into())
}

fn forget_password(id: &str) -> Result<(), String> {
    match entry(id)?.delete_credential() {
        Ok(()) | Err(keyring::Error::NoEntry) => Ok(()),
        Err(_) => Err("Cannot delete the saved password from Windows Credential Manager.".into()),
    }
}

fn saved_password(id: &str) -> Result<Option<Zeroizing<String>>, String> {
    match entry(id)?.get_password() {
        Ok(password) => Ok(Some(Zeroizing::new(password))),
        Err(keyring::Error::NoEntry) => Ok(None),
        Err(_) => Err("Cannot read the saved password from Windows Credential Manager.".into()),
    }
}

fn set_password(id: &str, password: Option<&str>) -> Result<(), String> {
    match password {
        Some(password) => entry(id)?
            .set_password(password)
            .map_err(|_| "Cannot save the password in Windows Credential Manager.".into()),
        None => forget_password(id),
    }
}

fn commit_with_password(
    pending: PendingProfiles,
    id: &str,
    previous: Option<&str>,
    next: Option<&str>,
) -> Result<(), String> {
    if previous == next {
        return pending.commit();
    }
    set_password(id, next)?;
    if let Err(error) = pending.commit() {
        return match set_password(id, previous) {
            Ok(()) => Err(format!("{error}. The previous saved credential was restored.")),
            Err(_) => Err(format!("{error}. Credential recovery also failed; enter the previous password again before reconnecting.")),
        };
    }
    Ok(())
}

pub fn password(
    profile: &ConnectionProfile,
    supplied: Option<Zeroizing<String>>,
) -> Result<Zeroizing<String>, String> {
    if let Some(password) = supplied {
        return Ok(password);
    }
    if !profile.remember_password {
        return Ok(Zeroizing::new(String::new()));
    }
    let _guard = STORE_LOCK
        .lock()
        .map_err(|_| "Profile store is unavailable.")?;
    let _file_guard = lock_store()?;
    entry(&profile.id)?
        .get_password()
        .map(Zeroizing::new)
        .map_err(|_| "Saved password is unavailable. Enter the password again.".into())
}

pub fn list() -> Result<Vec<ConnectionProfile>, String> {
    let _guard = STORE_LOCK
        .lock()
        .map_err(|_| "Profile store is unavailable.")?;
    let _file_guard = lock_store()?;
    read_profiles()
}

pub fn save(
    profile: ConnectionProfile,
    supplied: Option<Zeroizing<String>>,
) -> Result<ConnectionProfile, String> {
    profile.validate()?;
    let _guard = STORE_LOCK
        .lock()
        .map_err(|_| "Profile store is unavailable.")?;
    let _file_guard = lock_store()?;
    let mut profiles = read_profiles()?;
    let previous = saved_password(&profile.id)?;
    let next = if profile.remember_password {
        // A null password preserves the existing credential; an empty string replaces it.
        Some(
            supplied
                .or_else(|| previous.clone())
                .ok_or("Enter a password before enabling Remember password.")?,
        )
    } else {
        None
    };
    profiles.retain(|p| p.id != profile.id);
    profiles.push(profile.clone());
    profiles.sort_by_key(|p| p.name.to_lowercase());
    let pending = prepare_profiles(&profiles)?;
    commit_with_password(
        pending,
        &profile.id,
        previous.as_ref().map(|p| p.as_str()),
        next.as_ref().map(|p| p.as_str()),
    )?;
    Ok(profile)
}

pub fn delete(id: &str) -> Result<(), String> {
    let _guard = STORE_LOCK
        .lock()
        .map_err(|_| "Profile store is unavailable.")?;
    let _file_guard = lock_store()?;
    let mut profiles = read_profiles()?;
    let previous = saved_password(id)?;
    profiles.retain(|p| p.id != id);
    let pending = prepare_profiles(&profiles)?;
    commit_with_password(pending, id, previous.as_ref().map(|p| p.as_str()), None)
}
