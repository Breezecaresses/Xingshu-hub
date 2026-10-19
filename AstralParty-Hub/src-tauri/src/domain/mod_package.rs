use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ModPackage {
    pub mod_id: String,
    pub name: String,
    pub installed_version: Option<String>,
    pub status: String,
    pub install_path: Option<String>,
    pub manifest_json: Option<serde_json::Value>,
    pub installed_at: Option<DateTime<Utc>>,
    pub updated_at: Option<DateTime<Utc>>,
}
