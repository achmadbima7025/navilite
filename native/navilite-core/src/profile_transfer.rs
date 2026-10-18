use crate::profiles::{self, ConnectionProfile, Environment};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use uuid::Uuid;

const MAX_DOCUMENT_BYTES: usize = 1024 * 1024;
const MAX_CONNECTIONS: usize = 1000;
const FORMAT: &str = "navilite.connections";

// A separate allowlist keeps IDs, password flags, and all credential material
// out of the portable format, even if the local profile gains new fields.
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct PortableConnection {
    name: String,
    host: String,
    port: u16,
    username: String,
    environment: Environment,
    use_tls: bool,
    ca_certificate: Option<String>,
}

impl PortableConnection {
    fn from_profile(profile: &ConnectionProfile) -> Self {
        Self {
            name: profile.name.clone(),
            host: profile.host.clone(),
            port: profile.port,
            username: profile.username.clone(),
            environment: profile.environment.clone(),
            use_tls: profile.use_tls,
            ca_certificate: profile.ca_certificate.clone(),
        }
    }

    fn normalize(&mut self) {
        self.name = self.name.trim().into();
        self.host = self.host.trim().into();
        self.username = self.username.trim().into();
        self.ca_certificate = self
            .ca_certificate
            .as_ref()
            .map(|path| path.trim())
            .filter(|path| !path.is_empty())
            .map(String::from);
    }

    fn validate_raw(&self) -> Result<(), String> {
        let valid = |value: &str, maximum| {
            value.chars().count() <= maximum && !value.chars().any(char::is_control)
        };
        if !valid(&self.name, 256) || !valid(&self.host, 1024) || !valid(&self.username, 256) {
            return Err("Name, host, and username must be within their length limits and contain no control characters.".into());
        }
        if self
            .ca_certificate
            .as_ref()
            .is_some_and(|path| !valid(path, 4096))
        {
            return Err(
                "The CA certificate path is too long or contains control characters.".into(),
            );
        }
        Ok(())
    }

    fn validate(&self) -> Result<(), String> {
        if self.name.is_empty() || self.host.is_empty() || self.username.is_empty() {
            return Err("Name, host, and username are required.".into());
        }
        if self.port == 0 {
            return Err("Port must be between 1 and 65535.".into());
        }
        if !self.use_tls && self.ca_certificate.is_some() {
            return Err("A CA certificate path requires TLS to be enabled.".into());
        }
        Ok(())
    }

    fn matches(&self, profile: &ConnectionProfile) -> bool {
        self.host.eq_ignore_ascii_case(profile.host.trim())
            && self.port == profile.port
            && self.username == profile.username.trim()
            && self.environment == profile.environment
            && self.use_tls == profile.use_tls
            && self.ca_certificate.as_deref()
                == profile
                    .ca_certificate
                    .as_deref()
                    .map(str::trim)
                    .filter(|path| !path.is_empty())
    }

    fn new_profile(&self, name: String, id: String) -> ConnectionProfile {
        ConnectionProfile {
            id,
            name,
            host: self.host.clone(),
            port: self.port,
            username: self.username.clone(),
            environment: self.environment.clone(),
            remember_password: false,
            use_tls: self.use_tls,
            ca_certificate: self.ca_certificate.clone(),
        }
    }
}

#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct ConnectionBundle {
    format: String,
    format_version: u32,
    connections: Vec<PortableConnection>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ImportCandidate {
    connection: PortableConnection,
    duplicate: bool,
    planned_name: String,
}

#[derive(Serialize)]
pub struct ImportPreview {
    candidates: Vec<ImportCandidate>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ImportResult {
    pub imported: usize,
    pub skipped: usize,
    pub first_imported_id: Option<String>,
}

pub(crate) struct ImportPlan {
    preview: ImportPreview,
    pub additions: Vec<(usize, ConnectionProfile)>,
}

fn parse(document: &str) -> Result<Vec<PortableConnection>, String> {
    if document.len() > MAX_DOCUMENT_BYTES {
        return Err("Connection files are limited to 1 MiB.".into());
    }
    // Never include serde's input values in errors: a rejected document may
    // contain credential fields or secrets in an invalid field value.
    let mut bundle: ConnectionBundle = serde_json::from_str(document.trim_start_matches('\u{feff}'))
        .map_err(|_| "Invalid connection file. Use the NaviLite metadata-only format; unknown fields, IDs, and credential fields are not accepted.")?;
    if bundle.format != FORMAT || bundle.format_version != 1 {
        return Err("Unsupported connection file format or version. Expected navilite.connections version 1.".into());
    }
    if bundle.connections.is_empty() || bundle.connections.len() > MAX_CONNECTIONS {
        return Err("A connection file must contain between 1 and 1000 connections.".into());
    }
    for (index, connection) in bundle.connections.iter_mut().enumerate() {
        connection
            .validate_raw()
            .map_err(|error| format!("Connection {}: {error}", index + 1))?;
        connection.normalize();
        connection
            .validate()
            .map_err(|error| format!("Connection {}: {error}", index + 1))?;
    }
    Ok(bundle.connections)
}

pub fn export_document() -> Result<String, String> {
    let connections: Vec<_> = profiles::list()?
        .iter()
        .map(PortableConnection::from_profile)
        .collect();
    if connections.is_empty() || connections.len() > MAX_CONNECTIONS {
        return Err("Export requires between 1 and 1000 saved connections.".into());
    }
    let document = serde_json::to_string_pretty(&ConnectionBundle {
        format: FORMAT.into(),
        format_version: 1,
        connections,
    })
    .map_err(|_| "Cannot serialize connection metadata.")?;
    // Export must produce a document that this version can import.
    parse(&document)?;
    Ok(document)
}

pub fn preview(document: &str) -> Result<ImportPreview, String> {
    let connections = parse(document)?;
    Ok(plan(&profiles::list()?, &connections).preview)
}

pub fn import(document: &str, selection: &str) -> Result<ImportResult, String> {
    let connections = parse(document)?;
    if selection.len() > 8192 {
        return Err("The import selection is too large.".into());
    }
    let selected: Vec<usize> =
        serde_json::from_str(selection).map_err(|_| "Invalid connection import selection.")?;
    let indices: HashSet<_> = selected.iter().copied().collect();
    if selected.is_empty()
        || selected.len() != indices.len()
        || selected.iter().any(|index| *index >= connections.len())
    {
        return Err("Select one or more distinct connections from the preview.".into());
    }
    profiles::import_connections(connections, indices)
}

pub(crate) fn plan(
    existing: &[ConnectionProfile],
    connections: &[PortableConnection],
) -> ImportPlan {
    let mut additions: Vec<(usize, ConnectionProfile)> = Vec::new();
    let mut candidates = Vec::with_capacity(connections.len());
    let mut ids: HashSet<_> = existing.iter().map(|profile| profile.id.clone()).collect();
    let mut names: HashSet<_> = existing
        .iter()
        .map(|profile| profile.name.to_lowercase())
        .collect();
    for (index, connection) in connections.iter().enumerate() {
        let duplicate = existing
            .iter()
            .chain(additions.iter().map(|(_, profile)| profile))
            .find(|profile| connection.matches(profile));
        let planned_name = if let Some(profile) = duplicate {
            profile.name.clone()
        } else {
            let mut name = connection.name.clone();
            let stem: String = connection.name.chars().take(230).collect();
            let mut suffix = 2;
            while names.contains(&name.to_lowercase()) {
                name = format!("{stem} (imported {suffix})");
                suffix += 1;
            }
            name
        };
        let is_duplicate = duplicate.is_some();
        if !is_duplicate {
            let mut id = Uuid::new_v4().simple().to_string();
            while !ids.insert(id.clone()) {
                id = Uuid::new_v4().simple().to_string();
            }
            names.insert(planned_name.to_lowercase());
            additions.push((index, connection.new_profile(planned_name.clone(), id)));
        }
        candidates.push(ImportCandidate {
            connection: connection.clone(),
            duplicate: is_duplicate,
            planned_name,
        });
    }
    ImportPlan {
        preview: ImportPreview { candidates },
        additions,
    }
}
