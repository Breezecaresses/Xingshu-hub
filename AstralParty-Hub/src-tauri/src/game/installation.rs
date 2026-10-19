use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DetectionEvidence {
    pub source: String,
    pub path: String,
    pub detail: String,
    pub passed: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DetectedGameDetails {
    pub install_path: String,
    pub executable_path: Option<String>,
    pub edition: String, // "CN", "INT", "DUAL", or "STANDARD"
    pub build_id: Option<String>,
    pub version: Option<String>,
    pub evidences: Vec<DetectionEvidence>,
    pub is_valid: bool,
}
