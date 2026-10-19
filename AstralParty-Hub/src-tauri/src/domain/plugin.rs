use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Plugin {
    pub plugin_id: String,
    pub name: String,
    pub installed_version: Option<String>,
    pub status: String,
    pub install_path: Option<String>,
    pub manifest_json: Option<serde_json::Value>,
    pub installed_at: Option<DateTime<Utc>>,
    pub updated_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstalledFile {
    pub target_path: String,
    pub package_id: String,
    pub package_version: String,
    pub installed_sha256: String,
    pub installed_at: DateTime<Utc>,
}
