use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GameVersionInfo {
    pub display_version: Option<String>,
    pub build_id: Option<String>,
    pub source: String,
    pub confidence: String,
}

pub struct VersionDetector;

impl VersionDetector {
    pub fn resolve_version(build_id: Option<&str>, display_version: Option<&str>) -> GameVersionInfo {
        if let Some(b) = build_id {
            GameVersionInfo {
                display_version: display_version.map(|s| s.to_string()),
                build_id: Some(b.to_string()),
                source: "STEAM_BUILD_ID".into(),
                confidence: "HIGH".into(),
            }
        } else {
            GameVersionInfo {
                display_version: display_version.map(|s| s.to_string()),
                build_id: None,
                source: "EXECUTABLE_METADATA".into(),
                confidence: "MEDIUM".into(),
            }
        }
    }
}
